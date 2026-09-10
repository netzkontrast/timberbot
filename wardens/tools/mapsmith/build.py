"""The build state every op writes into: a heightfield, named masks, named anchors, entities."""
from __future__ import annotations

import math
import random
import uuid
from dataclasses import dataclass, field

from .grid import Grid, Mask, Point

LAYERS = 23  # Z 0..22; the top layer must stay air (the game's convention, see design/wardens-wasteland.md)
NAMESPACE = uuid.UUID("6f1c2d3e-7a1b-4c5d-9e8f-0a1b2c3d4e5f")


@dataclass
class Entity:
    """One `.timber` entity. `z` is the first air layer above the column unless `buried` is set."""

    template: str
    x: int
    y: int
    z: int
    components: dict = field(default_factory=dict)
    orientation: str | None = None
    ident: str = ""
    rule: str = ""      # the [[place]]/[[scatter]] `name` that produced it, for checks and reports

    def to_json(self, seed: int) -> dict:
        block: dict = {"Coordinates": {"X": self.x, "Y": self.y, "Z": self.z}}
        if self.orientation is not None:
            block["Orientation"] = self.orientation
        components = {"BlockObject": block}
        components.update(self.components)
        ident = self.ident or str(uuid.uuid5(NAMESPACE, f"{seed}:{self.template}:{self.x}:{self.y}:{self.z}"))
        return {"Id": ident, "Template": self.template, "Components": components}


class MapBuild:
    """Terrain ops mutate `height` and register masks/anchors; placement reads them and adds entities."""

    def __init__(self, size: int, seed: int):
        self.size = size
        self.seed = seed
        self.rng = random.Random(seed)
        self.height = Grid(size, 6.0)
        self.masks: dict[str, Mask] = {}
        self.anchors: dict[str, Point] = {}
        self.paths: dict[str, list[Point]] = {}
        self.path_tags: dict[str, list[str]] = {}   # tag -> names of the paths carrying it
        self.path_tag_of: dict[str, str] = {}       # path name -> its tag
        self.entities: list[Entity] = []
        self.occupied = Mask(size)      # cells an entity already claims
        self.reserved = Mask(size)      # cells placement must keep clear (the starting pad, mainly)
        self._distance_cache: dict[str, Grid] = {}

    # -- terrain reads ---------------------------------------------------------------------------

    def h(self, x: int, y: int) -> int:
        return int(self.height.at(x, y))

    def surface_z(self, x: int, y: int) -> int:
        """The first air layer above the column — where a surface entity stands."""
        return self.h(x, y)

    def clamp_height(self, low: int, high: int) -> None:
        self.height.cells = [float(min(max(round(v), low), high)) for v in self.height.cells]

    # -- masks and anchors -----------------------------------------------------------------------

    def mask(self, name: str) -> Mask:
        if name not in self.masks:
            self.masks[name] = Mask(self.size)
        return self.masks[name]

    def tag(self, name: str | None, cells: Mask) -> None:
        if not name:
            return
        self.masks[name] = self.mask(name).union(cells)
        self._distance_cache.pop(name, None)

    def anchor(self, name: str | None, point: Point) -> None:
        if name:
            self.anchors[name] = point

    def resolve_point(self, value) -> Point:
        """`[x, y]` or the name of an anchor."""
        if isinstance(value, str):
            if value not in self.anchors:
                raise SpecError(f"unknown anchor {value!r}; known: {sorted(self.anchors) or 'none'}")
            return self.anchors[value]
        if isinstance(value, (list, tuple)) and len(value) == 2:
            return (float(value[0]), float(value[1]))
        raise SpecError(f"expected [x, y] or an anchor name, got {value!r}")

    def distance_to(self, mask_name: str, max_dist: float = 24.0) -> Grid:
        """Distance from every cell to the nearest cell of a named mask (cached)."""
        from .grid import distance_field
        if mask_name not in self._distance_cache:
            if mask_name not in self.masks:
                raise SpecError(f"unknown mask {mask_name!r}; known: {sorted(self.masks) or 'none'}")
            pts = [(x + 0.5, y + 0.5) for x, y in self.masks[mask_name].points()]
            self._distance_cache[mask_name] = distance_field(self.size, pts, max_dist)
        return self._distance_cache[mask_name]

    # -- entities --------------------------------------------------------------------------------

    def add(self, entity: Entity, footprint: int = 1) -> None:
        self.entities.append(entity)
        r = footprint - 1
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                x, y = entity.x + dx, entity.y + dy
                if 0 <= x < self.size and 0 <= y < self.size:
                    self.occupied.set(x, y)

    def reserve(self, x0: int, y0: int, x1: int, y1: int) -> None:
        for y in range(max(0, y0), min(self.size, y1 + 1)):
            for x in range(max(0, x0), min(self.size, x1 + 1)):
                self.reserved.set(x, y)

    def free(self, x: int, y: int, margin: int = 1) -> bool:
        """Inside the border, not water, not reserved, not already taken."""
        if not (margin <= x < self.size - margin and margin <= y < self.size - margin):
            return False
        if self.occupied.at(x, y) or self.reserved.at(x, y):
            return False
        for name in ("water", "badwater", "clean"):
            m = self.masks.get(name)
            if m is not None and m.at(x, y):
                return False
        return True

    # -- walkability -----------------------------------------------------------------------------

    def walkable_from(self, start: tuple[int, int], max_step: int = 1) -> Mask:
        """Flood fill over the surface, stepping at most `max_step` levels between neighbours.

        Beavers climb one block without stairs, so this is the ground a colony can reach on foot
        before it builds anything. Water cells are walls.
        """
        reached = Mask(self.size)
        water = self.masks.get("water")
        bad = self.masks.get("badwater")
        clean = self.masks.get("clean")

        def blocked(x: int, y: int) -> bool:
            return any(m is not None and m.at(x, y) for m in (water, bad, clean))

        if blocked(*start):
            return reached
        reached.set(*start)
        stack = [start]
        while stack:
            x, y = stack.pop()
            h0 = self.h(x, y)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < self.size and 0 <= ny < self.size) or reached.at(nx, ny):
                    continue
                if blocked(nx, ny) or abs(self.h(nx, ny) - h0) > max_step:
                    continue
                reached.set(nx, ny)
                stack.append((nx, ny))
        return reached

    def walk_distances(self, start: tuple[int, int], max_step: int = 1) -> dict[tuple[int, int], int]:
        """Steps from `start` to every walkable tile — how far a beaver actually walks, not how far
        it looks on a heightmap. Diagonals are excluded, so treat these as a lower bound."""
        water = [self.masks[m] for m in ("water", "badwater", "clean") if m in self.masks]

        def blocked(x: int, y: int) -> bool:
            return any(m.at(x, y) for m in water)

        if blocked(*start):
            return {}
        dist = {start: 0}
        queue = [start]
        head = 0
        while head < len(queue):
            x, y = queue[head]
            head += 1
            d = dist[(x, y)]
            h0 = self.h(x, y)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < self.size and 0 <= ny < self.size) or (nx, ny) in dist:
                    continue
                if blocked(nx, ny) or abs(self.h(nx, ny) - h0) > max_step:
                    continue
                dist[(nx, ny)] = d + 1
                queue.append((nx, ny))
        return dist

    def water_cells(self) -> set[tuple[int, int]]:
        """Every tile any water mask covers — what the checker needs to agree with `walkable_from`."""
        out: set[tuple[int, int]] = set()
        for name in ("water", "badwater", "clean"):
            m = self.masks.get(name)
            if m is not None:
                out.update(m.points())
        return out

    # -- voxels ----------------------------------------------------------------------------------

    def voxels(self) -> str:
        """`width x depth x LAYERS` values, Z-major then Y then X, "1" solid "0" air."""
        size = self.size
        heights = [int(v) for v in self.height.cells]
        out: list[str] = []
        for z in range(LAYERS):
            if z == LAYERS - 1:
                out.extend("0" * (size * size))
            else:
                out.extend("1" if h > z else "0" for h in heights)
        return " ".join(out)


class SpecError(ValueError):
    """A spec the tool refuses to build: unknown op, bad anchor, impossible geometry."""


def ellipse_mask(size: int, cx: float, cy: float, rx: float, ry: float) -> Mask:
    m = Mask(size)
    if rx <= 0 or ry <= 0:
        return m
    for y in range(max(0, int(cy - ry - 1)), min(size, int(cy + ry + 2))):
        for x in range(max(0, int(cx - rx - 1)), min(size, int(cx + rx + 2))):
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                m.set(x, y)
    return m


def polar_sample(rng: random.Random, centre: Point, inner: float, outer: float) -> tuple[int, int]:
    """A point in an annulus, uniform by area."""
    angle = rng.random() * 2 * math.pi
    r = math.sqrt(rng.random() * (outer ** 2 - inner ** 2) + inner ** 2)
    return int(round(centre[0] + r * math.cos(angle))), int(round(centre[1] + r * math.sin(angle)))
