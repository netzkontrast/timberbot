"""Generate the Wardens' wasteland map: a `.timber` file the game loads as a custom map.

    python wardens/tools/gen_map.py                   # writes src/Maps/Wardens Wasteland.timber + design preview
    python wardens/tools/gen_map.py --seed 7 --size 96
    python wardens/tools/gen_map.py --check "path/to/map.timber"

The faction design (design/faction-wardens.md §2, §7) asks for one shipped wasteland: badwater
sources, contaminated soil, ruins for scrap, nothing green. Nobody can drive the in-game map editor
from a script, so this writes the map file directly.

File format (verified against a map that loads in 0.7.10 and its exporter, lawless-m/Toberboon, plus
a Timberborn-editor map for the entity shapes): a `.timber` is a zip of world.json, map_metadata.json,
version.txt and map_thumbnail.jpg. Terrain is a voxel string (width x depth x 23 layers, "1" solid,
"0" air, Z-major then Y then X); water, evaporation, soil moisture and soil contamination are
per-cell strings; entities are {Id, Template, Components}. The file claims GameVersion 0.7.10.0 on
purpose: that is the format we verified, and the game migrates older maps on load (it may show an
"older version" notice), whereas claiming 1.1 for an unverified 1.1 layout would skip the migration.

Layout (default seed): a rolling ash plateau at Z 6-9 with a rim of hills; a badwater river that
enters at the north edge from three BadwaterSources, meanders in an S across the map and leaves at
the south edge; the Sump, a basin beside the starting location that the river fills (the Sludge Pump
goes there); ruin clusters within scavenging range of the start (RuinColumnH1..H5, ScrapMetal
15 per level) and two richer fields further out; UndergroundRuins under the plateau for later
mines; and one clean spring on a hill in the north-east corner, the map's only green (pines,
birches, blueberries) for the Reforestation tutorial's "there is not much".

Water starts dry: the new water map's column encoding for pre-filled water is not documented, so
the sources fill the river and the Sump during the first day (the Cold Boot orbit shows it arriving).
Soil contamination in the file is cosmetic and recomputed by the game from the badwater.
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
MAP_NAME = "Wardens Wasteland"
GAME_VERSION = "0.7.10.0"          # the verified format; the game migrates it (see module docstring)
TIMESTAMP = "2026-09-03 18:00:00"  # fixed so re-runs are byte-identical
LAYERS = 23                        # Z 0..22, top layer always air
NAMESPACE = uuid.UUID("6f1c2d3e-7a1b-4c5d-9e8f-0a1b2c3d4e5f")


# --- terrain -------------------------------------------------------------------------------------

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


class Wasteland:
    def __init__(self, size: int, seed: int):
        self.size = size
        self.seed = seed
        self.rng = np.random.default_rng(seed)
        s = size
        # Design anchors, scaled from the 96 x 96 reference layout.
        f = s / 96.0
        self.start = (int(20 * f), int(46 * f))              # starting location (bottom-left of its pad)
        self.pad = 8                                          # flat pad size
        self.sump = (int(31 * f), int(46 * f), 5 * f, 4 * f)  # cx, cy, rx, ry
        self.river_pts = [(34 * f, -2), (36 * f, 14 * f), (52 * f, 26 * f), (38 * f, 42 * f), (30 * f, 47 * f),
                          (40 * f, 58 * f), (62 * f, 66 * f), (56 * f, 82 * f), (48 * f, s + 2)]
        self.spring = (int(82 * f), int(14 * f))              # clean spring hill centre
        self.height: np.ndarray | None = None
        self.river: np.ndarray | None = None      # bool: badwater bed cells
        self.clean: np.ndarray | None = None      # bool: clean-water bed cells
        self.entities: list[dict] = []
        self.sources: list[tuple[int, int]] = []
        self.ruins: list[tuple[int, int, int]] = []
        self.trees: list[tuple[int, int]] = []

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
        x0, y0 = self.start
        if x0 - 3 <= x < x0 + self.pad + 3 and y0 - 3 <= y < y0 + self.pad + 3:
            return False
        return True

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

    # -- singletons ---------------------------------------------------------------------------------

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
        s = self.size
        h = self.height
        layers = []
        for z in range(LAYERS):
            solid = (h > z) if z < LAYERS - 1 else np.zeros_like(h, dtype=bool)
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
            "GameVersion": GAME_VERSION,
            "Timestamp": TIMESTAMP,
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

    # -- pictures --------------------------------------------------------------------------------------

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
        x0, y0 = self.start
        dr.rectangle([x0 * scale, y0 * scale, (x0 + self.pad) * scale - 1, (y0 + self.pad) * scale - 1], outline=(0, 229, 255), width=2)
        return im


# --- packaging -------------------------------------------------------------------------------------------

MINIMAL_JPEG = bytes.fromhex(
    "ffd8ffe000104a46494600010101004800480000ffdb004300" + "ff" * 64 +
    "ffc0000b080001000101011100ffc40014000100000000000000000000000000000003ffc40014100100000000000000000000000000000000"
    "ffda0008010100003f0037ffd9")


def write_timber(w: Wasteland, out: Path, preview: Path | None) -> None:
    world = w.world()
    metadata = {"Width": w.size, "Height": w.size, "MapNameLocKey": "", "MapDescriptionLocKey": "",
                "MapDescription": "The Wardens' wasteland. Badwater from the north, ruins for scrap, one clean spring in the north-east. Bots only.",
                "IsRecommended": False, "IsDev": False}
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
    stamp = (2026, 9, 3, 18, 0, 0)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for name, data in (("world.json", json.dumps(world, separators=(",", ":")).encode("utf-8")),
                           ("map_metadata.json", json.dumps(metadata, separators=(",", ":")).encode("utf-8")),
                           ("version.txt", GAME_VERSION.encode("ascii")),
                           ("map_thumbnail.jpg", thumb)):
            info = zipfile.ZipInfo(name, date_time=stamp)
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, data)


# --- self-check ------------------------------------------------------------------------------------------

def check(path: Path) -> list[str]:
    """Static checks the game would fail on: array sizes, entity placement, the starting pad."""
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
    vox = sing["TerrainMap"]["Voxels"]["Array"].split()
    if len(vox) != sx * sy * LAYERS:
        problems.append(f"voxels: {len(vox)} values, expected {sx * sy * LAYERS}")
    grid = np.array(vox, dtype=np.uint8).reshape(LAYERS, sy, sx)
    if grid[LAYERS - 1].any():
        problems.append("top layer (Z=22) must be air")
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
    height = grid.sum(axis=0)          # solid blocks per column (terrain has no overhangs here)
    ids = set()
    starts = 0
    for e in w["Entities"]:
        if e["Id"] in ids:
            problems.append(f"duplicate entity id {e['Id']}")
        ids.add(e["Id"])
        c = e["Components"]["BlockObject"]["Coordinates"]
        x, y, zc = c["X"], c["Y"], c["Z"]
        if not (0 <= x < sx and 0 <= y < sy and 0 <= zc <= LAYERS - 1):
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
    if not any(e["Template"] == "BadwaterSource" for e in w["Entities"]):
        problems.append("no BadwaterSource")
    return problems


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=96)
    ap.add_argument("--seed", type=int, default=3000)
    ap.add_argument("--out", type=Path, default=SRC / "Maps" / f"{MAP_NAME}.timber")
    ap.add_argument("--preview", type=Path, default=DESIGN / "wardens-wasteland.png")
    ap.add_argument("--no-preview", action="store_true")
    ap.add_argument("--check", type=Path, help="only run the static checks on an existing .timber")
    args = ap.parse_args()
    if args.check:
        problems = check(args.check)
        print("problems:", problems or "none")
        return 1 if problems else 0
    w = Wasteland(args.size, args.seed)
    w.build_terrain()
    w.build_entities()
    write_timber(w, args.out, None if args.no_preview else args.preview)
    print(f"wrote {args.out} ({args.out.stat().st_size} bytes): {args.size}x{args.size}, {len(w.entities)} entities "
          f"({len(w.sources)} badwater sources, {len(w.ruins)} ruins, {len(w.trees)} plants)")
    problems = check(args.out)
    print("problems:", problems or "none")
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
