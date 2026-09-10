"""Generate the Wardens' campaign maps: `.timber` files the game loads as custom maps.

    python wardens/tools/gen_map.py                        # level 01, the default
    python wardens/tools/gen_map.py --level 01 --preview   # + the design preview PNG
    python wardens/tools/gen_map.py --all                  # every level in the registry
    python wardens/tools/gen_map.py --list                 # what the registry holds
    python wardens/tools/gen_map.py --check "path/to/map.timber" [--level 01]

One class per level (design/wardens-campaign-map-set.md §4): `Terrain` holds the shared toolkit and
the file plumbing, a level subclass composes primitives into land and declares its **contract** —
the properties the level's play depends on, checked by `--check`. Timberborn has no objective
system, so the land is the only rule engine and the contract is the level design.

Seeds are pinned per level in the class and must never change after a map ships: the same name with
a different seed is a different map, and level 10 (`Home`) regenerates level 01's exact heightfield.

Nobody can drive the in-game map editor from a script, so this writes the map file directly.

File format (verified against a map that loads in 0.7.10 and its exporter, lawless-m/Toberboon, plus
a Timberborn-editor map for the entity shapes): a `.timber` is a zip of world.json, map_metadata.json,
version.txt and map_thumbnail.jpg. Terrain is a voxel string (width x depth x LAYERS, "1" solid,
"0" air, Z-major then Y then X); water, evaporation, soil moisture and soil contamination are
per-cell strings; entities are {Id, Template, Components}. The file claims GameVersion 0.7.10.0 on
purpose: that is the format we verified, and the game migrates older maps on load (it may show an
"older version" notice), whereas claiming 1.1 for an unverified 1.1 layout would skip the migration.

Water starts dry: the new water map's column encoding for pre-filled water is not documented, so the
sources fill the channels during the first day. That is cosmetic for level 01 and fatal for any level
whose puzzle is water separating things — see design/wardens-campaign-map-set.md §5 before designing
levels 03, 04 or 08 around it.
"""
from __future__ import annotations

import argparse
import io
import json
import math
import uuid
import zipfile
from pathlib import Path

import numpy as np

SRC = Path(__file__).resolve().parents[1] / "src"
DESIGN = Path(__file__).resolve().parents[2] / "design"
NAMESPACE = uuid.UUID("6f1c2d3e-7a1b-4c5d-9e8f-0a1b2c3d4e5f")


# --- the shared toolkit --------------------------------------------------------------------------

def value_noise(rng: np.random.Generator, size: int, octaves=(8, 16, 32), weights=(1.0, 0.5, 0.25)) -> np.ndarray:
    """Sum of bilinear value-noise octaves in [0, 1]."""
    out = np.zeros((size, size))
    total = 0.0
    for cells, w in zip(octaves, weights):
        grid = rng.random((cells + 1, cells + 1))
        xs = np.linspace(0, cells, size)
        x0 = np.floor(xs).astype(int).clip(0, cells - 1)
        fx = xs - x0
        # smoothstep for softer hills
        fx = fx * fx * (3 - 2 * fx)
        row = grid[:, x0] * (1 - fx) + grid[:, x0 + 1] * fx          # (cells+1, size)
        col = row[x0, :] * (1 - fx)[:, None] + row[x0 + 1, :] * fx[:, None]
        out += w * col
        total += w
    return out / total


def catmull_rom(points: list[tuple[float, float]], per_segment: int = 40) -> list[tuple[float, float]]:
    pts = [points[0]] + points + [points[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for k in range(per_segment):
            t = k / per_segment
            t2, t3 = t * t, t * t * t
            x = 0.5 * ((2 * p1[0]) + (-p0[0] + p2[0]) * t + (2 * p0[0] - 5 * p1[0] + 4 * p2[0] - p3[0]) * t2 + (-p0[0] + 3 * p1[0] - 3 * p2[0] + p3[0]) * t3)
            y = 0.5 * ((2 * p1[1]) + (-p0[1] + p2[1]) * t + (2 * p0[1] - 5 * p1[1] + 4 * p2[1] - p3[1]) * t2 + (-p0[1] + 3 * p1[1] - 3 * p2[1] + p3[1]) * t3)
            out.append((x, y))
    out.append(points[-1])
    return out


def distance_field(size: int, path: list[tuple[float, float]]) -> np.ndarray:
    """Distance from every cell centre to the nearest path point."""
    ys, xs = np.mgrid[0:size, 0:size]
    cx, cy = xs + 0.5, ys + 0.5
    best = np.full((size, size), 1e9)
    for px, py in path:
        d = np.hypot(cx - px, cy - py)
        np.minimum(best, d, out=best)
    return best


# --- the base ------------------------------------------------------------------------------------

class Terrain:
    """Format plumbing and the shared primitives. A level subclass builds the land.

    Subclasses override `anchors()` (design constants scaled to the size), `build_terrain()` and
    `build_entities()`, and declare their contract in `contract()`.
    """

    LEVEL = "00"
    MAP_NAME = ""
    DESCRIPTION = ""
    SIZE = 96
    SEED = 0
    LAYERS = 23                        # Z 0..LAYERS-1, top layer always air
    GAME_VERSION = "0.7.10.0"          # the verified format; the game migrates it (see module docstring)
    TIMESTAMP = "2026-09-03 18:00:00"  # fixed so re-runs are byte-identical
    STAMP = (2026, 9, 3, 18, 0, 0)     # zip entry mtime, same reason
    REQUIRE_BADWATER = True            # false only where the land is healed (level 10)

    def __init__(self, size: int | None = None, seed: int | None = None):
        self.size = self.SIZE if size is None else size
        self.seed = self.SEED if seed is None else seed
        self.rng = np.random.default_rng(self.seed)
        self.start: tuple[int, int] | None = None   # starting pad, bottom-left
        self.pad = 8
        self.height: np.ndarray | None = None
        self.river: np.ndarray | None = None      # bool: badwater bed cells
        self.clean: np.ndarray | None = None      # bool: clean-water bed cells
        self.entities: list[dict] = []
        self.sources: list[tuple[int, int]] = []
        self.ruins: list[tuple[int, int, int]] = []
        self.trees: list[tuple[int, int]] = []
        self.anchors()

    # -- to override -----------------------------------------------------------------------------

    def anchors(self) -> None:
        """Design constants, scaled from the level's reference layout. No rng."""

    def build_terrain(self) -> None:
        raise NotImplementedError

    def build_entities(self) -> None:
        raise NotImplementedError

    def contract(self, world: dict, grid: np.ndarray) -> list[str]:
        """Level-specific assertions on a written map. Empty list means the contract holds."""
        return []

    def build(self) -> "Terrain":
        self.build_terrain()
        self.build_entities()
        return self

    # -- entity helpers --------------------------------------------------------------------------

    def ident(self, kind: str, x: int, y: int, z: int = 0) -> str:
        return str(uuid.uuid5(NAMESPACE, f"{self.seed}:{kind}:{x}:{y}:{z}"))

    def z_at(self, x: int, y: int) -> int:
        return int(self.height[y, x])      # first air layer above the column

    def free(self, x: int, y: int, margin: int = 0) -> bool:
        s = self.size
        if x < 1 + margin or y < 1 + margin or x >= s - 1 - margin or y >= s - 1 - margin:
            return False
        if self.river[y, x] or self.clean[y, x]:
            return False
        if self.start is not None:
            x0, y0 = self.start
            if x0 - 3 <= x < x0 + self.pad + 3 and y0 - 3 <= y < y0 + self.pad + 3:
                return False
        return True

    # -- singletons ------------------------------------------------------------------------------

    def contamination(self) -> np.ndarray:
        d = self.dist_to_river
        return np.clip(1.0 - (d - 2.5) / 6.0, 0.0, 1.0)

    @property
    def dist_to_river(self) -> np.ndarray:
        if not hasattr(self, "_dist"):
            ys, xs = np.nonzero(self.river)
            self._dist = distance_field(self.size, list(zip(xs + 0.5, ys + 0.5)))
        return self._dist

    def voxels(self) -> str:
        h = self.height
        layers = []
        for z in range(self.LAYERS):
            solid = (h > z) if z < self.LAYERS - 1 else np.zeros_like(h, dtype=bool)
            layers.append(solid.astype(np.uint8))
        arr = np.stack(layers)               # (Z, Y, X)
        return " ".join(map(str, arr.reshape(-1).tolist()))

    def world(self) -> dict:
        s = self.size
        n = s * s
        cont = self.contamination().reshape(-1)
        def floats(a: np.ndarray) -> str:
            return " ".join("0" if v == 0 else f"{v:.4g}" for v in a.tolist())
        return {
            "GameVersion": self.GAME_VERSION,
            "Timestamp": self.TIMESTAMP,
            "Singletons": {
                "MapSize": {"Size": {"X": s, "Y": s}},
                "TerrainMap": {"Voxels": {"Array": self.voxels()}},
                "WaterMapNew": {"Levels": 1,
                                "WaterColumns": {"Array": " ".join(["0"] * n)},
                                "ColumnOutflows": {"Array": " ".join(["0|0:0|0:0|0:0|0"] * n)}},
                "WaterEvaporationMap": {"Levels": 1, "EvaporationModifiers": {"Array": " ".join(["1"] * n)}},
                "SoilMoistureSimulator": {"Size": 1, "MoistureLevels": {"Array": " ".join(["0"] * n)}},
                "SoilContaminationSimulator": {"Size": 1,
                                               "ContaminationCandidates": {"Array": floats(cont)},
                                               "ContaminationLevels": {"Array": floats(cont)}},
                "HazardousWeatherHistory": {"HistoryData": []},
                "MapThumbnailCameraMover": {"CurrentConfiguration": {
                    "Position": {"X": s / 2.0, "Y": round(s * 0.64, 2), "Z": -s / 2.0},
                    "Rotation": {"X": 0.342020124, "Y": 0.0, "Z": 0.0, "W": 0.9396926},
                    "ShadowDistance": 150.0}},
            },
            "Entities": self.entities,
        }

    def metadata(self) -> dict:
        return {"Width": self.size, "Height": self.size, "MapNameLocKey": "", "MapDescriptionLocKey": "",
                "MapDescription": self.DESCRIPTION,
                "IsRecommended": False, "IsDev": False}

    # -- pictures --------------------------------------------------------------------------------

    def render(self, scale: int = 6):
        """Top-down preview: ash plateau by height, badwater, the clean pond, ruins, trees, the start."""
        from PIL import Image, ImageDraw
        s = self.size
        h = self.height.astype(float)
        cont = self.contamination()
        img = np.zeros((s, s, 3), dtype=np.uint8)
        t = np.clip((h - 3) / 14.0, 0, 1)
        base = np.stack([88 + 70 * t, 78 + 60 * t, 66 + 48 * t], axis=-1)           # ash / rust
        poison = np.stack([70 * cont, 40 * cont, 60 * cont], axis=-1)                # purple stain near badwater
        img[:] = np.clip(base - poison * 0.6, 0, 255).astype(np.uint8)
        img[self.river] = (72, 30, 92)
        img[self.clean] = (60, 150, 200)
        im = Image.fromarray(img, "RGB").resize((s * scale, s * scale), Image.NEAREST)
        dr = ImageDraw.Draw(im)
        for x, y in self.trees:
            dr.rectangle([x * scale + 1, y * scale + 1, x * scale + scale - 2, y * scale + scale - 2], fill=(60, 120, 50))
        for x, y, lvl in self.ruins:
            g = 120 + 20 * lvl
            dr.rectangle([x * scale, y * scale, x * scale + scale - 1, y * scale + scale - 1], fill=(g, g, g), outline=(30, 30, 30))
        for x, y in self.sources:
            dr.ellipse([x * scale - 2, y * scale - 2, x * scale + scale + 1, y * scale + scale + 1], outline=(200, 80, 220), width=2)
        if self.start is not None:
            x0, y0 = self.start
            dr.rectangle([x0 * scale, y0 * scale, (x0 + self.pad) * scale - 1, (y0 + self.pad) * scale - 1], outline=(0, 229, 255), width=2)
        return im


# --- contract helpers ----------------------------------------------------------------------------

def surface(grid: np.ndarray) -> np.ndarray:
    """Solid blocks per column: the ground top, given terrain without overhangs."""
    return grid.sum(axis=0)


def templates(world: dict, prefix: str) -> list[dict]:
    return [e for e in world["Entities"] if e["Template"].startswith(prefix)]


def coords(entity: dict) -> tuple[int, int, int]:
    c = entity["Components"]["BlockObject"]["Coordinates"]
    return c["X"], c["Y"], c["Z"]


def floats_of(world: dict, singleton: str, key: str) -> np.ndarray:
    return np.array(world["Singletons"][singleton][key]["Array"].split(), dtype=float)


# --- level 01: First Light -----------------------------------------------------------------------

class FirstLight(Terrain):
    """Level 01. The wasteland the Wardens wake in: badwater from the north, ruins for scrap, one
    clean spring in the north-east. design/wardens-wasteland.md.

    Layout: a rolling ash plateau at Z 6-9 with a rim of hills; a badwater river that enters at the
    north edge from three BadwaterSources, meanders in an S across the map and leaves at the south
    edge; the Sump, a basin beside the starting location that the river fills (the Sludge Pump goes
    there); ruin clusters within scavenging range of the start (RuinColumnH1..H5, ScrapMetal 15 per
    level) and two richer fields further out; UndergroundRuins under the plateau for later mines;
    and one clean spring on a hill in the north-east corner, the map's only green (pines, birches,
    blueberries) for the Reforestation tutorial's "there is not much".
    """

    LEVEL = "01"
    MAP_NAME = "Wardens 01 First Light"
    DESCRIPTION = ("The Wardens' wasteland. Badwater from the north, ruins for scrap, one clean spring "
                   "in the north-east. Bots only. Campaign level 01: First Light.")
    SIZE = 96
    SEED = 3000

    def anchors(self) -> None:
        s = self.size
        # Design anchors, scaled from the 96 x 96 reference layout.
        f = s / 96.0
        self.start = (int(20 * f), int(46 * f))              # starting location (bottom-left of its pad)
        self.pad = 8                                          # flat pad size
        self.sump = (int(31 * f), int(46 * f), 5 * f, 4 * f)  # cx, cy, rx, ry
        self.river_pts = [(34 * f, -2), (36 * f, 14 * f), (52 * f, 26 * f), (38 * f, 42 * f), (30 * f, 47 * f),
                          (40 * f, 58 * f), (62 * f, 66 * f), (56 * f, 82 * f), (48 * f, s + 2)]
        self.spring = (int(82 * f), int(14 * f))              # clean spring hill centre

    # -- terrain ---------------------------------------------------------------------------------

    def build_terrain(self) -> None:
        s = self.size
        n = value_noise(self.rng, s)
        relief = np.clip((n - 0.5) * 1.9 + 0.5, 0, 1)          # stretch the noise so hills and hollows show
        h = 6 + np.round(relief * 3.4).astype(int)            # 6..9 ash plateau
        # rim of hills so the basin reads as a basin
        ys, xs = np.mgrid[0:s, 0:s]
        edge = np.minimum(np.minimum(xs, s - 1 - xs), np.minimum(ys, s - 1 - ys))
        rim = np.clip((10 - edge) / 10.0, 0, 1) ** 2 * 5
        h = h + np.round(rim * (0.6 + n)).astype(int)

        # clean spring hill in the north-east
        sx, sy = self.spring
        d = np.hypot(xs - sx, ys - sy)
        hill = np.clip((13 - d) / 13.0, 0, 1) ** 1.5 * 6
        h = np.maximum(h, np.round(8 + hill).astype(int))

        # the badwater river: bed at 4, banks sloping to the plateau
        path = catmull_rom(self.river_pts)
        dist = distance_field(s, path)
        valley = dist <= 4.5
        h = np.where(valley, np.minimum(h, 7), h)
        h = np.where(dist <= 3.0, np.minimum(h, 6), h)
        bed = dist <= 1.7
        h = np.where(bed, 4, h)

        # the Sump: a basin beside the start, cut into the river bank
        cx, cy, rx, ry = self.sump
        basin = ((xs - cx) / rx) ** 2 + ((ys - cy) / ry) ** 2 <= 1.0
        h = np.where(basin, 4, h)
        bed = bed | basin
        shore = (((xs - cx) / (rx + 2)) ** 2 + ((ys - cy) / (ry + 2)) ** 2 <= 1.0) & ~basin
        h = np.where(shore, np.minimum(h, 6), h)

        # clean spring: a crater pond on the hill, overflow runs down the north face
        px, py = sx, sy
        pond = np.hypot(xs - px, ys - py) <= 2.6
        crater_bed = int(h[py, px]) - 2
        h = np.where(pond, crater_bed, h)
        clean = pond.copy()
        for k in range(1, 40):                                 # notch running north to the rim
            yy = py - 3 - k
            if yy < 0:
                break
            h[yy, px] = min(int(h[yy, px]), crater_bed + 1 + k // 6)
            clean[yy, px] = True

        # the starting pad: flat, above the Sump, 3 blocks of air guaranteed by the height cap
        x0, y0 = self.start
        h[y0:y0 + self.pad, x0:x0 + self.pad] = 8
        h = np.clip(h, 3, 19)
        self.height = h
        self.river = bed
        self.clean = clean

    # -- entities --------------------------------------------------------------------------------

    def build_entities(self) -> None:
        h = self.height
        s = self.size
        ents: list[dict] = []

        # starting location on its pad
        x0, y0 = self.start
        sx, sy = x0 + self.pad // 2 - 2, y0 + self.pad // 2 - 2
        ents.append({"Id": self.ident("start", sx, sy), "Template": "StartingLocation",
                     "Components": {"BlockObject": {"Coordinates": {"X": sx, "Y": sy, "Z": int(h[sy, sx])}, "Orientation": "Cw0"}}})

        # badwater sources on the river bed at the north end
        head = [p for p in catmull_rom(self.river_pts) if 1.5 <= p[1] <= 5.0]
        placed = set()
        for px, py in head:
            x, y = int(px), int(py)
            for dx in (-1, 0, 1):
                xx = x + dx
                if 0 < xx < s - 1 and self.river[y, xx] and (xx, y) not in placed and len(placed) < 3:
                    placed.add((xx, y))
        for x, y in sorted(placed):
            self.sources.append((x, y))
            ents.append({"Id": self.ident("badwater", x, y), "Template": "BadwaterSource",
                         "Components": {"WaterSource": {"SpecifiedStrength": 4.0, "CurrentStrength": 4.0},
                                        "BlockObject": {"Coordinates": {"X": x, "Y": y, "Z": self.z_at(x, y)}, "Orientation": "Cw0"}}})

        # the clean spring in its crater
        px, py = self.spring
        ents.append({"Id": self.ident("spring", px, py), "Template": "WaterSource",
                     "Components": {"WaterSource": {"SpecifiedStrength": 1.5, "CurrentStrength": 1.5},
                                    "BlockObject": {"Coordinates": {"X": px, "Y": py, "Z": self.z_at(px, py)}, "Orientation": "Cw0"}}})

        # ruins: clusters around the start (small columns close, taller further out) + two far fields
        ruin_sites: list[tuple[int, int, int]] = []
        cx, cy = x0 + self.pad // 2, y0 + self.pad // 2
        def cluster(centre: tuple[int, int], count: int, radius: float, heights: tuple[int, ...]) -> None:
            tries = 0
            got = 0
            while got < count and tries < 400:
                tries += 1
                a = self.rng.random() * 2 * math.pi
                r = radius * math.sqrt(self.rng.random())
                x, y = int(centre[0] + r * math.cos(a)), int(centre[1] + r * math.sin(a))
                if not self.free(x, y, margin=2) or h[y, x] < 6 or h[y, x] > 10:
                    continue
                if any(abs(x - rx) <= 1 and abs(y - ry) <= 1 for rx, ry, _ in ruin_sites):
                    continue
                ruin_sites.append((x, y, int(self.rng.choice(heights))))
                got += 1
        f = s / 96.0
        cluster((cx - 9, cy - 6), 7, 4.5, (1, 1, 2, 2, 3))          # the near ruins: chapter 1 scrap
        cluster((cx + 2, cy + 9), 6, 4.0, (1, 2, 2, 3))
        cluster((cx - 4, cy + 12), 5, 3.5, (1, 2, 3, 3))
        cluster((int(66 * f), int(36 * f)), 9, 5.5, (2, 3, 3, 5))    # across the river: for the bridge builders
        cluster((int(24 * f), int(78 * f)), 9, 5.5, (3, 3, 5, 5))    # the south field: tall, rich
        cluster((int(74 * f), int(78 * f)), 6, 4.5, (2, 3, 5))
        variants = "ABC"
        for x, y, lvl in ruin_sites:
            self.ruins.append((x, y, lvl))
            ents.append({"Id": self.ident(f"ruin{lvl}", x, y), "Template": f"RuinColumnH{lvl}",
                         "Components": {"BlockObject": {"Coordinates": {"X": x, "Y": y, "Z": self.z_at(x, y)}, "Orientation": "Cw0"},
                                        "RuinModels": {"VariantId": variants[int(self.rng.integers(3))]},
                                        "Yielder:Ruin": {"Yield": {"Good": {"Id": "ScrapMetal"}, "Amount": 15 * lvl}}}})

        # underground ruins: inside the plateau, for the mines of a later act
        got = 0
        tries = 0
        while got < 6 and tries < 300:
            tries += 1
            x, y = int(self.rng.integers(6, s - 6)), int(self.rng.integers(6, s - 6))
            if not self.free(x, y, margin=3) or h[y, x] < 7:
                continue
            if math.hypot(x - cx, y - cy) > 30 * f:
                continue
            if any(abs(x - rx) <= 2 and abs(y - ry) <= 2 for rx, ry, _ in ruin_sites):
                continue
            z = int(h[y, x]) - 3
            ents.append({"Id": self.ident("underground", x, y, z), "Template": "UndergroundRuins",
                         "Components": {"BlockObject": {"Coordinates": {"X": x, "Y": y, "Z": z}, "Orientation": "Cw0"}}})
            ruin_sites.append((x, y, 0))
            got += 1

        # the only green: around the spring, plus a few lone birches on the far plateau
        px, py = self.spring
        planted = set()
        def plant(x: int, y: int, template: str) -> None:
            if (x, y) in planted or not self.free(x, y, margin=1):
                return
            if any(abs(x - rx) <= 1 and abs(y - ry) <= 1 for rx, ry, _ in ruin_sites):
                return
            planted.add((x, y))
            self.trees.append((x, y))
            comps = {"BlockObject": {"Coordinates": {"X": x, "Y": y, "Z": self.z_at(x, y)}},
                     "Growable": {"GrowthProgress": float(round(0.7 + 0.3 * self.rng.random(), 3))}}
            ents.append({"Id": self.ident(template, x, y), "Template": template, "Components": comps})
        for _ in range(140):
            a = self.rng.random() * 2 * math.pi
            r = 3.5 + 8.0 * self.rng.random()
            x, y = int(px + r * math.cos(a)), int(py + r * math.sin(a))
            kind = self.rng.random()
            plant(x, y, "Pine" if kind < 0.45 else "Birch" if kind < 0.85 else "BlueberryBush")
        for _ in range(60):
            x, y = int(self.rng.integers(4, s - 4)), int(self.rng.integers(4, s - 4))
            if math.hypot(x - px, y - py) < 16 or math.hypot(x - cx, y - cy) < 14:
                continue
            if self.dist_to_river[y, x] < 9:
                continue
            plant(x, y, "Birch" if self.rng.random() < 0.7 else "Pine")

        self.entities = ents

    # -- the contract ----------------------------------------------------------------------------

    def contract(self, world: dict, grid: np.ndarray) -> list[str]:
        """What the level's play depends on (design/wardens-campaign-map-set.md §2, level 01).

        First Light is the tutorial level: everything Chapter 1 asks for has to be within reach of
        the starting pad, and the map's one clean spring has to exist and be far from the badwater.
        """
        bad = []
        h = surface(grid)
        s = h.shape[0]

        start = templates(world, "StartingLocation")
        if len(start) != 1:
            return [f"{len(start)} StartingLocation entities, expected 1"]
        sx, sy, _ = coords(start[0])

        # Scrap is the only building material: near ruins the first Scavenger Flags can reach.
        near = [e for e in templates(world, "RuinColumnH") if math.hypot(coords(e)[0] - sx, coords(e)[1] - sy) <= 16]
        if len(near) < 5:
            bad.append(f"{len(near)} ruin columns within 16 tiles of the start, expected >= 5 (chapter 1 scrap)")
        scrap = sum(e["Components"]["Yielder:Ruin"]["Yield"]["Amount"] for e in templates(world, "RuinColumnH"))
        if scrap < 600:
            bad.append(f"total ruin scrap {scrap}, expected >= 600")

        # Badwater is fuel and Data feedstock; it has to cross the map, not puddle at the edge.
        sources = templates(world, "BadwaterSource")
        if len(sources) < 3:
            bad.append(f"{len(sources)} BadwaterSource entities, expected >= 3")
        bedrock = h == 4
        if not bedrock[:3, :].any() or not bedrock[-3:, :].any():
            bad.append("the badwater river does not reach both the north and the south edge")

        # The Sump: the Sludge Pump's basin, beside the pad.
        ys, xs = np.nonzero(bedrock)
        sump = int(((np.abs(xs - sx) <= 12) & (np.abs(ys - sy) <= 12)).sum())
        if sump < 40:
            bad.append(f"the Sump is {sump} cells at bed height within 12 tiles of the start, expected >= 40")

        # "Trees only grow on irrigated, green ground. Find some. There is not much."
        springs = templates(world, "WaterSource")
        if len(springs) != 1:
            bad.append(f"{len(springs)} clean WaterSource entities, expected exactly 1")
        else:
            px, py, _ = coords(springs[0])
            for e in sources:
                bx, by, _ = coords(e)
                if math.hypot(bx - px, by - py) < 20:
                    bad.append("a BadwaterSource is within 20 tiles of the clean spring")
                    break
        plants = len(templates(world, "Pine")) + len(templates(world, "Birch")) + len(templates(world, "BlueberryBush"))
        if plants < 80:
            bad.append(f"{plants} plants, expected >= 80 (the spring's grove)")

        # Poison is the plot: a band, not a flood and not a rumour.
        cont = floats_of(world, "SoilContaminationSimulator", "ContaminationLevels")
        share = float((cont > 0.5).mean())
        if not 0.03 <= share <= 0.40:
            bad.append(f"{share:.0%} of tiles contaminated above 0.5, expected 3-40%")

        # Later acts mine the plateau.
        under = templates(world, "UndergroundRuins")
        if len(under) < 4:
            bad.append(f"{len(under)} UndergroundRuins, expected >= 4")
        return bad


# --- the registry --------------------------------------------------------------------------------

LEVELS: dict[str, type[Terrain]] = {
    FirstLight.LEVEL: FirstLight,
}
DEFAULT_LEVEL = FirstLight.LEVEL


def level_class(level: str) -> type[Terrain]:
    key = level.zfill(2)
    if key not in LEVELS:
        raise SystemExit(f"unknown level {level!r}; have {', '.join(sorted(LEVELS))}")
    return LEVELS[key]


def level_for_map_name(name: str) -> type[Terrain] | None:
    for cls in LEVELS.values():
        if cls.MAP_NAME == name:
            return cls
    return None


# --- packaging -------------------------------------------------------------------------------------------

MINIMAL_JPEG = bytes.fromhex(
    "ffd8ffe000104a46494600010101004800480000ffdb004300" + "ff" * 64 +
    "ffc0000b080001000101011100ffc40014000100000000000000000000000000000003ffc40014100100000000000000000000000000000000"
    "ffda0008010100003f0037ffd9")


def write_timber(w: Terrain, out: Path, preview: Path | None) -> None:
    world = w.world()
    metadata = w.metadata()
    try:
        im = w.render(scale=4)
        buf = io.BytesIO()
        im.convert("RGB").save(buf, "JPEG", quality=85)
        thumb = buf.getvalue()
        if preview is not None:
            preview.parent.mkdir(parents=True, exist_ok=True)
            w.render(scale=6).save(preview, "PNG", optimize=True)
    except ImportError:
        thumb = MINIMAL_JPEG
    out.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for name, data in (("world.json", json.dumps(world, separators=(",", ":")).encode("utf-8")),
                           ("map_metadata.json", json.dumps(metadata, separators=(",", ":")).encode("utf-8")),
                           ("version.txt", w.GAME_VERSION.encode("ascii")),
                           ("map_thumbnail.jpg", thumb)):
            info = zipfile.ZipInfo(name, date_time=w.STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, data)


# --- self-check ------------------------------------------------------------------------------------------

def check(path: Path, level: type[Terrain] | None = None) -> list[str]:
    """Two tiers: the format the game would choke on, then the level's contract.

    `level` defaults to the registry entry whose MAP_NAME matches the file name, so
    `--check "Wardens 01 First Light.timber"` runs level 01's contract without being told.
    """
    problems: list[str] = []
    z = zipfile.ZipFile(path)
    names = set(z.namelist())
    for required in ("world.json", "map_metadata.json", "version.txt", "map_thumbnail.jpg"):
        if required not in names:
            problems.append(f"missing {required}")
    if problems:
        return problems
    if z.read("map_thumbnail.jpg")[:2] != b"\xff\xd8":
        problems.append("map_thumbnail.jpg is not a JPEG")
    w = json.loads(z.read("world.json"))
    meta = json.loads(z.read("map_metadata.json"))
    sing = w["Singletons"]
    sx, sy = sing["MapSize"]["Size"]["X"], sing["MapSize"]["Size"]["Y"]
    if (meta["Width"], meta["Height"]) != (sx, sy):
        problems.append("map_metadata size differs from MapSize")
    if level is None:
        level = level_for_map_name(path.stem)
    layers = level.LAYERS if level else Terrain.LAYERS
    vox = sing["TerrainMap"]["Voxels"]["Array"].split()
    if len(vox) != sx * sy * layers:
        problems.append(f"voxels: {len(vox)} values, expected {sx * sy * layers} ({layers} layers)")
        return problems
    grid = np.array(vox, dtype=np.uint8).reshape(layers, sy, sx)
    if grid[layers - 1].any():
        problems.append(f"top layer (Z={layers - 1}) must be air")
    per_cell = {"WaterMapNew": ("WaterColumns", "ColumnOutflows"), "WaterEvaporationMap": ("EvaporationModifiers",),
                "SoilMoistureSimulator": ("MoistureLevels",), "SoilContaminationSimulator": ("ContaminationCandidates", "ContaminationLevels")}
    for single, keys in per_cell.items():
        for k in keys:
            n = len(sing[single][k]["Array"].split())
            if n != sx * sy:
                problems.append(f"{single}.{k}: {n} values, expected {sx * sy}")
    for single in ("SoilMoistureSimulator", "SoilContaminationSimulator"):
        if "Size" not in sing[single]:
            problems.append(f"{single}: Size missing")
    height = surface(grid)          # solid blocks per column (terrain has no overhangs here)
    ids = set()
    starts = 0
    for e in w["Entities"]:
        if e["Id"] in ids:
            problems.append(f"duplicate entity id {e['Id']}")
        ids.add(e["Id"])
        x, y, zc = coords(e)
        if not (0 <= x < sx and 0 <= y < sy and 0 <= zc <= layers - 1):
            problems.append(f"{e['Template']} at {x},{y},{zc}: out of bounds")
            continue
        if e["Template"] == "UndergroundRuins":
            if grid[zc, y, x] != 1:
                problems.append(f"UndergroundRuins at {x},{y},{zc}: must be inside terrain")
            continue
        if grid[zc, y, x] != 0:
            problems.append(f"{e['Template']} at {x},{y},{zc}: placed inside terrain")
        if zc != int(height[y, x]):
            problems.append(f"{e['Template']} at {x},{y},{zc}: not on the surface (ground top is {int(height[y, x])})")
        if e["Template"] == "StartingLocation":
            starts += 1
            if e["Components"]["BlockObject"].get("Orientation") is None:
                problems.append("StartingLocation without Orientation")
            pad = height[y - 2:y + 6, x - 2:x + 6]
            if pad.shape != (8, 8) or pad.min() != pad.max():
                problems.append("StartingLocation pad is not flat 8x8")
            if grid[zc:zc + 3, y - 2:y + 6, x - 2:x + 6].any():
                problems.append("StartingLocation needs 3 blocks of air above the pad")
    if starts != 1:
        problems.append(f"{starts} StartingLocation entities, expected 1")
    # A map with no badwater is a bug everywhere except the healed land of the epilogue.
    require_badwater = level.REQUIRE_BADWATER if level else Terrain.REQUIRE_BADWATER
    has_badwater = any(e["Template"] == "BadwaterSource" for e in w["Entities"])
    if require_badwater and not has_badwater:
        problems.append("no BadwaterSource")
    if not require_badwater and has_badwater:
        problems.append("BadwaterSource on a level whose land is healed")
    if level is not None and not problems:
        problems.extend(f"contract: {p}" for p in level(size=sx).contract(w, grid))
    return problems


# --- cli -------------------------------------------------------------------------------------------------

def generate(cls: type[Terrain], out: Path | None, preview: Path | None,
             size: int | None, seed: int | None) -> int:
    w = cls(size=size, seed=seed).build()
    target = out or (SRC / "Maps" / f"{cls.MAP_NAME}.timber")
    write_timber(w, target, preview)
    print(f"wrote {target} ({target.stat().st_size} bytes): level {cls.LEVEL}, {w.size}x{w.size}, "
          f"{len(w.entities)} entities ({len(w.sources)} badwater sources, {len(w.ruins)} ruins, {len(w.trees)} plants)")
    problems = check(target, cls)
    print("problems:", problems or "none")
    return 1 if problems else 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Generate the Wardens campaign maps.")
    ap.add_argument("--level", default=DEFAULT_LEVEL, help=f"level id, default {DEFAULT_LEVEL}")
    ap.add_argument("--all", action="store_true", help="generate every level in the registry")
    ap.add_argument("--list", action="store_true", help="list the registry and exit")
    ap.add_argument("--size", type=int, help="override the level's size (variants only; ships pinned)")
    ap.add_argument("--seed", type=int, help="override the level's seed (variants only; ships pinned)")
    ap.add_argument("--out", type=Path, help="output path, default src/Maps/<map name>.timber")
    ap.add_argument("--preview", type=Path, help="also write a design preview PNG here")
    ap.add_argument("--check", type=Path, help="only run the checks on an existing .timber")
    args = ap.parse_args()

    if args.list:
        for key in sorted(LEVELS):
            cls = LEVELS[key]
            print(f"{key}  {cls.MAP_NAME:<28} {cls.SIZE}x{cls.SIZE}  seed {cls.SEED}")
        return 0
    if args.check:
        cls = level_class(args.level) if args.level != DEFAULT_LEVEL else None
        problems = check(args.check, cls)
        print("problems:", problems or "none")
        return 1 if problems else 0
    if args.all:
        rc = 0
        for key in sorted(LEVELS):
            rc |= generate(LEVELS[key], None, None, None, None)
        return rc
    return generate(level_class(args.level), args.out, args.preview, args.size, args.seed)


if __name__ == "__main__":
    raise SystemExit(main())
