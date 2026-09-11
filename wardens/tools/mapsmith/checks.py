"""Everything the game would reject, checked offline.

There is no Timberborn in this repo's CI and often none on the machine writing the map, so the
checker is the only thing standing between a spec and a wasted playtest. It works on a finished
`.timber` (so it also checks files written by other tools, including `gen_map.py`) and gets extra
context when it is called straight after a build.

Findings come back as `Problem(level, message)`. `error` means the game or the colony breaks;
`warning` means the map loads but probably is not the map you meant.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .build import LAYERS, SIZES, cells_of
from .world import read_timber


@dataclass(frozen=True)
class Problem:
    level: str      # "error" | "warning" | "note"
    message: str

    def __str__(self) -> str:
        return f"[{self.level}] {self.message}"


class Report(list):
    @property
    def errors(self) -> list[Problem]:
        return [p for p in self if p.level == "error"]

    @property
    def warnings(self) -> list[Problem]:
        return [p for p in self if p.level == "warning"]

    @property
    def notes(self) -> list[Problem]:
        return [p for p in self if p.level == "note"]

    @property
    def ok(self) -> bool:
        return not self.errors

    def err(self, message: str) -> None:
        self.append(Problem("error", message))

    def warn(self, message: str) -> None:
        self.append(Problem("warning", message))

    def note(self, message: str) -> None:
        """What a check *proved*, not what it found wrong.

        A contract that passes silently is a contract nobody trusts: "single_gorge: one narrows at
        48,60 (5 tiles)" is the evidence that the level is still the level. Notes never fail a run,
        not even under --strict.
        """
        self.append(Problem("note", message))

    def summary(self) -> str:
        lines = [str(p) for p in self]
        if not self.errors and not self.warnings:
            return "\n".join(lines + ["problems: none"]) if lines else "problems: none"
        return "\n".join(lines) + f"\n{len(self.errors)} error(s), {len(self.warnings)} warning(s)"


def _heights(voxels: list[str], size_x: int, size_y: int) -> list[int]:
    """Solid blocks per column. The generator writes no overhangs, so this is the ground top."""
    plane = size_x * size_y
    heights = [0] * plane
    for z in range(LAYERS):
        base = z * plane
        for i in range(plane):
            if voxels[base + i] == "1":
                heights[i] = z + 1
    return heights


def _check_water_columns(r, block: dict, heights: list[int], plane: int) -> None:
    """Every WaterColumns token as the game's WaterColumnPackedListSerializer reads it: "0" (dry) or
    depth:contamination:overflow[:floor[:oldDepth]], numbers, depth >= 0, contamination 0..1, and a
    floor that is the column's ground top (the level the water stands on)."""
    tokens = block.get("WaterColumns", {}).get("Array", "").split()
    if len(tokens) != plane:
        return                                     # the count is reported by the per-cell check
    bad = 0
    for i, t in enumerate(tokens):
        if t == "0":
            continue
        parts = t.split(":")
        problem = None
        if not 3 <= len(parts) <= 5:
            problem = f"{len(parts)} fields (3 to 5)"
        else:
            try:
                depth, contamination, overflow = (float(p) for p in parts[:3])
                floor = int(parts[3]) if len(parts) >= 4 else None
                if len(parts) == 5:
                    float(parts[4])
            except ValueError:
                problem = "not numbers"
            else:
                if depth < 0 or overflow < 0:
                    problem = "negative depth or overflow"
                elif not 0 <= contamination <= 1:
                    problem = f"contamination {contamination} outside 0..1"
                elif floor is not None and floor != heights[i]:
                    problem = f"floor {floor}, the ground top is {heights[i]}"
        if problem:
            bad += 1
            if bad <= 3:
                r.err(f"WaterMapNew.WaterColumns[{i}] (x {i % int(plane ** 0.5)}, y {i // int(plane ** 0.5)}): "
                      f"{t!r}: {problem}")
    if bad > 3:
        r.err(f"WaterMapNew.WaterColumns: {bad - 3} more bad column(s)")


def _reachable(heights: list[int], size: int, start: tuple[int, int], blocked: set[tuple[int, int]],
               max_step: int = 1) -> set[tuple[int, int]]:
    if start in blocked:
        return set()
    seen = {start}
    stack = [start]
    while stack:
        x, y = stack.pop()
        h0 = heights[y * size + x]
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if not (0 <= nx < size and 0 <= ny < size) or (nx, ny) in seen or (nx, ny) in blocked:
                continue
            if abs(heights[ny * size + nx] - h0) > max_step:
                continue
            seen.add((nx, ny))
            stack.append((nx, ny))
    return seen


def check_world(world: dict, meta: dict, names: set[str], opts: dict | None = None,
                water: set[tuple[int, int]] | None = None,
                groups: dict[str, list[tuple[int, int]]] | None = None) -> Report:
    """Structural + playability checks on a parsed map. `opts` is the spec's `[checks]` table.

    `water` and `groups` are the extra context a freshly built spec can supply and a bare `.timber`
    cannot: which tiles are water (every bed is written dry, so the file alone cannot say), and which
    entities came from which named `[[scatter]]`/`[[place]]` rule. Without them the walkability check
    is the lenient version — it stops beavers at riverbanks only where the height step does — so a
    check run against a file alone can pass a map that `check <spec>` would fail. Prefer checking the
    spec."""
    opts = opts or {}
    r = Report()

    for required in ("world.json", "map_metadata.json", "version.txt", "map_thumbnail.jpg"):
        if required not in names:
            r.err(f"missing {required}")
    if "!thumbnail-not-jpeg" in names:
        r.err("map_thumbnail.jpg is not a JPEG")
    if r.errors:
        return r

    sing = world.get("Singletons", {})
    try:
        size_x = sing["MapSize"]["Size"]["X"]
        size_y = sing["MapSize"]["Size"]["Y"]
    except KeyError:
        r.err("Singletons.MapSize is missing")
        return r
    if (meta.get("Width"), meta.get("Height")) != (size_x, size_y):
        r.err(f"map_metadata size {meta.get('Width')}x{meta.get('Height')} differs from MapSize {size_x}x{size_y}")
    if size_x != size_y:
        r.warn(f"non-square map {size_x}x{size_y}: mapsmith's ops assume square, geometry may be off")

    voxels = sing.get("TerrainMap", {}).get("Voxels", {}).get("Array", "").split()
    expected = size_x * size_y * LAYERS
    if len(voxels) != expected:
        r.err(f"TerrainMap.Voxels: {len(voxels)} values, expected {expected}")
        return r
    plane = size_x * size_y
    if any(v == "1" for v in voxels[(LAYERS - 1) * plane:]):
        r.err(f"top layer (Z={LAYERS - 1}) must be air")

    per_cell = {
        "WaterMapNew": ("WaterColumns", "ColumnOutflows"),
        "WaterEvaporationMap": ("EvaporationModifiers",),
        "SoilMoistureSimulator": ("MoistureLevels",),
        "SoilContaminationSimulator": ("ContaminationCandidates", "ContaminationLevels"),
    }
    for single, keys in per_cell.items():
        block = sing.get(single)
        if block is None:
            r.err(f"Singletons.{single} is missing")
            continue
        for key in keys:
            count = len(block.get(key, {}).get("Array", "").split())
            if count != plane:
                r.err(f"{single}.{key}: {count} values, expected {plane}")
    for single in ("SoilMoistureSimulator", "SoilContaminationSimulator"):
        if "Size" not in sing.get(single, {}):
            r.err(f"{single}: Size missing (the game reads it before the arrays)")

    heights = _heights(voxels, size_x, size_y)
    _check_water_columns(r, sing.get("WaterMapNew", {}), heights, plane)
    entities = world.get("Entities", [])
    ids: set[str] = set()
    cells: dict[tuple[int, int], str] = {}
    starts: list[dict] = []
    templates: dict[str, int] = {}
    # Nothing mapsmith places is an underground block: UndergroundRuins's blocks say Underground false,
    # and the game deleted every buried one on load (PLAYTEST.md, 2026-09-11).
    buried_ok = set(opts.get("buried_ok", []))
    covered: dict[tuple[int, int], str] = {}

    for e in entities:
        template = e.get("Template", "?")
        templates[template] = templates.get(template, 0) + 1
        if e.get("Id") in ids:
            r.err(f"duplicate entity id {e.get('Id')}")
        ids.add(e.get("Id"))
        coords = e.get("Components", {}).get("BlockObject", {}).get("Coordinates")
        if coords is None:
            r.err(f"{template}: no BlockObject.Coordinates")
            continue
        x, y, z = coords["X"], coords["Y"], coords["Z"]
        if not (0 <= x < size_x and 0 <= y < size_y and 0 <= z < LAYERS):
            r.err(f"{template} at {x},{y},{z}: out of bounds")
            continue
        if template == "StartingLocation":
            starts.append(e)
        ground = heights[y * size_x + x]
        solid = voxels[z * plane + y * size_x + x] == "1"
        if z < ground:                       # buried on purpose (underground ruins, ore)
            if not solid:
                r.err(f"{template} at {x},{y},{z}: below the surface but not inside terrain")
            elif template not in buried_ok:
                r.err(f"{template} at {x},{y},{z}: buried {ground - z} level(s) under the surface; "
                      f"the game deletes it on load. Everything mapsmith places stands on top of its "
                      f"column" + (f" (buried_ok: {', '.join(sorted(buried_ok))})" if buried_ok else "") + ".")
            continue
        if template in SIZES:
            sx, sy = SIZES[template]
            footprint = cells_of(template, x, y)
            if any(not (0 <= cx < size_x and 0 <= cy < size_y) for cx, cy in footprint):
                r.err(f"{template} at {x},{y},{z}: its {sx}x{sy} footprint runs off the map")
            else:
                uneven = sorted({heights[cy * size_x + cx] for cx, cy in footprint} - {z})
                if uneven:
                    r.err(f"{template} at {x},{y},{z}: its {sx}x{sy} footprint is not flat at {z} "
                          f"(ground at {uneven}); every block needs ground directly below, so the game "
                          f"deletes it on load")
                clash = next((covered[c] for c in footprint if c in covered), None)
                if clash is not None:
                    r.err(f"{template} at {x},{y},{z}: its {sx}x{sy} footprint overlaps {clash}; the "
                          f"game keeps the first and deletes the other on load")
                for c in footprint:
                    covered.setdefault(c, f"{template} at {x},{y}")
        if solid:
            r.err(f"{template} at {x},{y},{z}: placed inside terrain")
        elif z != ground:
            r.err(f"{template} at {x},{y},{z}: floating — the ground top here is {ground}")
        if (x, y) in cells and template not in ("StartingLocation",):
            r.warn(f"{template} at {x},{y} shares a tile with {cells[(x, y)]}")
        cells[(x, y)] = template

    for (x, y), template in cells.items():            # a one-tile entity under a bigger footprint
        owner = covered.get((x, y))
        if owner is not None and template not in SIZES:
            r.err(f"{template} at {x},{y} stands inside the footprint of {owner}; the game deletes "
                  f"one of them on load")

    if len(starts) != 1:
        r.err(f"{len(starts)} StartingLocation entities, expected exactly 1")
    for e in starts:
        block = e["Components"]["BlockObject"]
        c = block["Coordinates"]
        x, y, z = c["X"], c["Y"], c["Z"]
        if block.get("Orientation") is None:
            r.err("StartingLocation without Orientation (the game reads it while spawning)")
        # The entity sits `anchor` tiles inside its pad's lower-left corner (mapsmith centres a
        # 4-tile footprint on an 8-tile pad), so the pad runs from x - anchor to x - anchor + pad.
        pad = int(opts.get("start_pad", 8))
        anchor = int(opts.get("start_anchor", pad // 2 - 2))
        x0, y0 = x - anchor, y - anchor
        headroom = int(opts.get("headroom", 3))
        levels: set[int] = set()
        off_map = solid_above = False
        for yy in range(y0, y0 + pad):
            for xx in range(x0, x0 + pad):
                if not (0 <= xx < size_x and 0 <= yy < size_y):
                    off_map = True
                    continue
                levels.add(heights[yy * size_x + xx])
                for zz in range(z, min(z + headroom, LAYERS)):
                    if voxels[zz * plane + yy * size_x + xx] == "1":
                        solid_above = True
        if off_map:
            r.err(f"StartingLocation at {x},{y}: its {pad}x{pad} pad runs off the map")
        if len(levels) > 1:
            r.err(f"StartingLocation at {x},{y}: pad is not flat (levels {sorted(levels)})")
        if solid_above:
            r.err(f"StartingLocation at {x},{y}: needs {headroom} blocks of air above the pad")

    for template in opts.get("require", []):
        if not templates.get(template):
            r.err(f"no {template} on the map (spec `[checks] require`)")
    for template, minimum in (opts.get("require_at_least") or {}).items():
        if templates.get(template, 0) < int(minimum):
            r.err(f"{templates.get(template, 0)} {template}, spec asks for at least {minimum}")

    # -- playability -----------------------------------------------------------------------------
    if starts and size_x == size_y:
        c = starts[0]["Components"]["BlockObject"]["Coordinates"]
        blocked = set(water) if water is not None else set()
        blocked |= {(e["Components"]["BlockObject"]["Coordinates"]["X"],
                     e["Components"]["BlockObject"]["Coordinates"]["Y"])
                    for e in entities if "Source" in e.get("Template", "")}
        # `reach` is what the colony gets to by building Stairs (one level each); `on_foot` is what it
        # gets to before it builds anything: the game joins a tile only to neighbours of its own height.
        reach = _reachable(heights, size_x, (c["X"], c["Y"]), blocked, int(opts.get("max_step", 1)))
        on_foot = _reachable(heights, size_x, (c["X"], c["Y"]), blocked, 0)
        min_area = int(opts.get("min_reachable", 0))
        if min_area and len(reach) < min_area:
            r.err(f"only {len(reach)} tiles are reachable from the starting location, even with Stairs "
                  f"(spec asks for {min_area}); the colony is boxed in")
        elif len(reach) < plane * 0.05:
            r.warn(f"only {len(reach)} of {plane} tiles are reachable from the start, even with Stairs "
                   f"({100 * len(reach) / plane:.1f}%) — check the pad is not ringed by cliffs")
        for group in opts.get("on_foot_scatter", []):
            spots = (groups or {}).get(group)
            if spots is None:
                r.warn(f"[checks] on_foot_scatter names {group!r}, which is not a named "
                       f"[[scatter]]/[[place]] rule in this spec"
                       + ("" if groups is not None else " (checking a .timber cannot see rule names — "
                                                        "run `check` on the spec instead)"))
                continue
            stranded = [s for s in spots if s not in on_foot]
            if stranded:
                r.err(f"{len(stranded)} of {len(spots)} entities from {group!r} need Stairs: their tile is "
                      f"not on the start's level, joined by flat ground, e.g. {stranded[0]}. A Scavenger "
                      f"Flag on the pad cannot reach them until one is built.")
        for group in opts.get("reachable_scatter", []):
            spots = (groups or {}).get(group)
            if spots is None:
                r.warn(f"[checks] reachable_scatter names {group!r}, which is not a named "
                       f"[[scatter]]/[[place]] rule in this spec"
                       + ("" if groups is not None else " (checking a .timber cannot see rule names — "
                                                        "run `check` on the spec instead)"))
                continue
            cut_off = [s for s in spots
                       if not any(abs(s[0] - rx) <= 1 and abs(s[1] - ry) <= 1 for rx, ry in reach)]
            if cut_off:
                r.err(f"{len(cut_off)} of {len(spots)} entities from {group!r} cannot be reached from "
                      f"the starting location, even with Stairs, e.g. {cut_off[0]}. If that is "
                      f"deliberate, drop it from `[checks] reachable_scatter`.")

        for template in opts.get("reachable", []):
            spots = [(e["Components"]["BlockObject"]["Coordinates"]["X"],
                      e["Components"]["BlockObject"]["Coordinates"]["Y"])
                     for e in entities if e.get("Template", "").startswith(template)]
            near = [s for s in spots if any(abs(s[0] - rx) <= 1 and abs(s[1] - ry) <= 1 for rx, ry in reach)]
            if spots and not near:
                r.err(f"no {template}* is reachable from the starting location, even with Stairs "
                      f"({len(spots)} exist, all cut off)")
            elif spots and len(near) < len(spots) * float(opts.get("reachable_fraction", 0.2)):
                r.warn(f"only {len(near)} of {len(spots)} {template}* are reachable, even with Stairs")

    return r


def check_file(path: Path, opts: dict | None = None) -> Report:
    if not path.exists():
        r = Report()
        r.err(f"{path} does not exist")
        return r
    world, meta, names = read_timber(path)
    return check_world(world, meta, names, opts)
