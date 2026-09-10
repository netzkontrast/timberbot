"""Contracts: the properties a level's land must guarantee, checked as geometry.

`design/wardens-campaign-map-set.md` §0 is the argument for this file. Timberborn has no objective
system: a level cannot tell the player what to do and cannot stop them doing something else, so the
only thing that reliably constrains play is the land itself. Every level therefore collapses to one
question — *what must be true of the ground for the intended play to be the only play that works?* —
and the answer is a short list of properties. That list is the level design. The scenery is
decoration.

The ordinary `[checks]` table answers "will the game load this, and can the colony move". A contract
answers "is this still the level it was meant to be" — that The Sump has exactly one place worth
damming, that The Pods' island cannot be walked to. Those break silently: a seed change or a widened
valley turns a level into a suggestion, the map still loads, and nobody finds out until a playtest.

Written like `terrain.py`: one function per contract, registered in CONTRACTS, documented by its own
docstring. `mapsmith contracts` prints them.

    [contract]
    single_gorge = { of = "the river", max_width = 6, min_bank = 3 }
    confluence_upstream = { tributary = "the creek", trunk = "the river", of = "the river" }
    never_touch_before = { a = "clean", b = "badwater", before = "confluence" }
"""
from __future__ import annotations

import math
from collections.abc import Callable

from .build import MapBuild, SpecError
from .checks import Report


def _path(b: MapBuild, name: str) -> list[tuple[float, float]]:
    if name in b.paths:
        return b.paths[name]
    keys = b.path_tags.get(name, [])
    if len(keys) == 1:
        return b.paths[keys[0]]
    if len(keys) > 1:
        raise SpecError(f"contract: {name!r} is a tag shared by {', '.join(keys)}; name one watercourse")
    raise SpecError(f"contract: no watercourse called {name!r}; known: {', '.join(sorted(b.paths)) or 'none'}")


def _cross_section(b: MapBuild, x: int, y: int, dx: int, dy: int, reach: int = 24):
    """Walk out from a bed tile in one direction until the ground rises clear of the water.

    Returns (width_in_tiles, bank_height_above_bed) for that side, or None if the map edge comes
    first — a channel that leaves the map is not a gorge, it is an exit.
    """
    water = [b.masks[m] for m in ("water", "badwater", "clean") if m in b.masks]
    bed = b.h(x, y)
    for step in range(1, reach + 1):
        nx, ny = x + dx * step, y + dy * step
        if not b.height.inside(nx, ny):
            return None
        if any(m.at(nx, ny) for m in water):
            continue                      # still in the channel
        return step, b.h(nx, ny) - bed
    return None


def contract_single_gorge(b: MapBuild, params: dict, r: Report) -> None:
    """Exactly one place along a watercourse is narrow enough and banked enough to dam.

    A dam decision only matters if there is one place to make it. This measures the channel's width
    and its banks' height above the bed at every point of the path, and counts the runs where both
    "narrow enough" and "high enough" hold at once. Two such runs mean two dams, and the level's
    whole premise is a suggestion.

    of, max_width, min_bank, count (default 1), min_length (default 2 tiles, so a one-tile pinch does
    not count as a gorge), max_length (optional, and worth setting: a "gorge" 25 tiles long is a
    canyon, and a dam anywhere along it is as good as anywhere else, which is the same as having no
    single decision), ignore_ends (default 6, path points that close to the map edge are the river's
    entrance and exit, not a gorge).

    Lengths are counted in distinct tiles, not path points — a Catmull-Rom path visits the same tile
    several times, and a run of 84 points over 26 tiles reads as far tighter than it is.
    """
    name = params["of"]
    max_width = float(params.get("max_width", 6))
    min_bank = float(params.get("min_bank", 3))
    want = int(params.get("count", 1))
    min_length = int(params.get("min_length", 2))
    max_length = params.get("max_length")
    ignore_ends = float(params.get("ignore_ends", 6))

    path = _path(b, name)
    gorges: list[list[tuple[int, int]]] = []
    run: list[tuple[int, int]] = []

    def close_run() -> None:
        """End the current run, keeping it if it is long enough.

        Every branch that ends a run has to come through here. Resetting `run` directly in the
        off-map and near-the-edge branches threw away any gorge that reached the map border — which
        is most of them, since a river that enters and leaves the map ends its last run there.
        """
        nonlocal run
        if len(set(run)) >= min_length:
            gorges.append(run)
        run = []

    for px, py in path:
        x, y = int(px), int(py)
        if not b.height.inside(x, y):
            close_run()
            continue
        edge = min(x, b.size - 1 - x, y, b.size - 1 - y)
        if edge < ignore_ends:
            close_run()
            continue
        # Measure across the flow: try both axes and take the narrower crossing.
        best = None
        for dx, dy in ((1, 0), (0, 1)):
            left = _cross_section(b, x, y, -dx, -dy)
            right = _cross_section(b, x, y, dx, dy)
            if left is None or right is None:
                continue
            width = left[0] + right[0] - 1
            bank = min(left[1], right[1])
            if best is None or width < best[0]:
                best = (width, bank)
        if best is not None and best[0] <= max_width and best[1] >= min_bank:
            if not run or run[-1] != (x, y):
                run.append((x, y))
        else:
            close_run()
    close_run()

    # Runs that touch are one gorge seen twice; merge anything within a few tiles.
    merged: list[list[tuple[int, int]]] = []
    for g in gorges:
        if merged and min(abs(g[0][0] - p[0]) + abs(g[0][1] - p[1]) for p in merged[-1]) <= 4:
            merged[-1].extend(g)
        else:
            merged.append(g)

    lengths = [len(set(g)) for g in merged]
    where = "; ".join(f"{g[len(g) // 2][0]},{g[len(g) // 2][1]} ({n} tiles long)"
                      for g, n in zip(merged, lengths, strict=True))
    if max_length is not None:
        for g, n in zip(merged, lengths, strict=True):
            if n > int(max_length):
                mid = g[len(g) // 2]
                r.err(f"contract single_gorge on {name!r}: the narrows at {mid[0]},{mid[1]} runs "
                      f"{n} tiles, longer than the {max_length} the contract allows — a dam anywhere "
                      f"along it is as good as anywhere else, so there is no single decision")
    if len(merged) != want:
        r.err(f"contract single_gorge on {name!r}: found {len(merged)} dammable narrows "
              f"(<= {max_width:g} wide with banks >= {min_bank:g} above the bed), expected {want}"
              + (f" — at {where}" if merged else " — nowhere on this river is worth damming"))
    else:
        r.note(f"single_gorge on {name!r}: one narrows at {where}")


def contract_confluence_upstream(b: MapBuild, params: dict, r: Report) -> None:
    """A tributary joins the trunk above the gorge, so one dam impounds both.

    "Above" is measured along the trunk's own path, not in map coordinates: the join has to happen
    at a smaller path fraction than the narrows, or damming the narrows does not hold the tributary
    and the level's central decision evaporates.

    tributary, trunk, and the same gorge parameters (max_width, min_bank) used to locate the narrows.
    """
    trunk = _path(b, params["trunk"])
    trib = _path(b, params["tributary"])
    max_width = float(params.get("max_width", 6))
    min_bank = float(params.get("min_bank", 3))
    ignore_ends = float(params.get("ignore_ends", 6))

    # Where does the tributary first come within a tile of the trunk?
    join_at = None
    for i, (px, py) in enumerate(trunk):
        for tx, ty in trib:
            if abs(px - tx) <= 1.5 and abs(py - ty) <= 1.5:
                join_at = i
                break
        if join_at is not None:
            break
    if join_at is None:
        r.err(f"contract confluence_upstream: {params['tributary']!r} never meets {params['trunk']!r}; "
              "the level's clean water never reaches the water the player dams")
        return

    gorge_at = None
    for i, (px, py) in enumerate(trunk):
        x, y = int(px), int(py)
        if not b.height.inside(x, y):
            continue
        if min(x, b.size - 1 - x, y, b.size - 1 - y) < ignore_ends:
            continue
        for dx, dy in ((1, 0), (0, 1)):
            left = _cross_section(b, x, y, -dx, -dy)
            right = _cross_section(b, x, y, dx, dy)
            if left is None or right is None:
                continue
            if left[0] + right[0] - 1 <= max_width and min(left[1], right[1]) >= min_bank:
                gorge_at = i
                break
        if gorge_at is not None:
            break
    if gorge_at is None:
        r.err("contract confluence_upstream: no gorge on the trunk to be upstream of "
              "(single_gorge will have said so too)")
        return

    if join_at >= gorge_at:
        r.err(f"contract confluence_upstream: {params['tributary']!r} joins at {join_at / len(trunk):.0%} "
              f"along {params['trunk']!r}, but the gorge is at {gorge_at / len(trunk):.0%} — the "
              f"confluence is *below* the dam, so damming it impounds only the trunk")
    else:
        r.note(f"confluence_upstream: joins at {join_at / len(trunk):.0%}, gorge at "
               f"{gorge_at / len(trunk):.0%} — one dam holds both")


def contract_never_touch(b: MapBuild, params: dict, r: Report) -> None:
    """Two water masks never share or neighbour a tile.

    The Sump's clean creek has to arrive clean: if its cells touch badwater anywhere before the
    confluence, the spring is poisoned at the source and the level has no clean water to protect.

    a, b (mask names), gap (default 1 — how many tiles must separate them), allow (default 0 —
    stray contacts tolerated anywhere), and `confluence` (a pair of watercourse names): contacts
    within `merge` tiles of where those two paths meet are the confluence itself and are exempt,
    which is what makes this "never touch *before* the confluence" rather than "never touch".
    """
    name_a, name_b = params["a"], params["b"]
    gap = float(params.get("gap", 1))
    allow = int(params.get("allow", 0))
    merge = float(params.get("merge", 4))
    join = None
    if params.get("confluence"):
        first, second = params["confluence"]
        pa, pb = _path(b, first), _path(b, second)
        for px, py in pa:
            if any(abs(px - qx) <= 1.5 and abs(py - qy) <= 1.5 for qx, qy in pb):
                join = (px, py)
                break
        if join is None:
            r.err(f"contract never_touch: {first!r} and {second!r} never meet, so there is no "
                  f"confluence to exempt")
            return
    for name in (name_a, name_b):
        if name not in b.masks:
            raise SpecError(f"contract never_touch: no mask {name!r}; known: {', '.join(sorted(b.masks))}")

    dist = b.distance_to(name_b, max_dist=gap + 2)
    touching = [(x, y) for x, y in b.masks[name_a].points() if dist.at(x, y) < gap]
    if join is not None:
        near_join = [c for c in touching if math.hypot(c[0] - join[0], c[1] - join[1]) <= merge]
        touching = [c for c in touching if c not in near_join]
    if len(touching) > allow:
        sample = ", ".join(f"{x},{y}" for x, y in touching[:4])
        r.err(f"contract never_touch: {len(touching)} cells of {name_a!r} are within {gap:g} of "
              f"{name_b!r} away from the confluence (at most {allow} allowed) — e.g. {sample}. "
              f"The clean water is poisoned before it arrives.")
    else:
        where = f" (confluence at {join[0]:.0f},{join[1]:.0f} exempt)" if join else ""
        r.note(f"never_touch: {name_a!r} keeps {gap:g} tile(s) from {name_b!r}{where} "
               f"— {len(touching)} stray contact(s), {allow} allowed")


def contract_unreachable(b: MapBuild, params: dict, r: Report) -> None:
    """A named region cannot be walked to from the start — the vertical architecture is the only way.

    The Pods' island earns the Vertical tutorial only if the beavers genuinely cannot walk there.
    Written now because the check is the same shape as `reachable_scatter` inverted, and because a
    contract that only exists once the map does is a contract nobody writes.

    mask (the region), from (anchor, default "start"), min_area / max_area (optional, tiles).
    """
    mask_name = params["mask"]
    if mask_name not in b.masks:
        raise SpecError(f"contract unreachable: no mask {mask_name!r}; known: {', '.join(sorted(b.masks))}")
    region = b.masks[mask_name]
    origin = b.resolve_point(params.get("from", "start"))
    walk = b.walkable_cached((int(round(origin[0])), int(round(origin[1]))))
    reached = [(x, y) for x, y in region.points() if walk.at(x, y)]
    if reached:
        r.err(f"contract unreachable: {len(reached)} tiles of {mask_name!r} can be walked to from "
              f"{params.get('from', 'start')!r} (e.g. {reached[0]}) — the region is not isolated")
    area = region.count()
    lo, hi = params.get("min_area"), params.get("max_area")
    if lo is not None and area < int(lo):
        r.err(f"contract unreachable: {mask_name!r} is {area} tiles, the contract asks for at least {lo}")
    if hi is not None and area > int(hi):
        r.err(f"contract unreachable: {mask_name!r} is {area} tiles, the contract asks for at most {hi}")
    if not reached and (lo is None or area >= int(lo)) and (hi is None or area <= int(hi)):
        r.note(f"unreachable: {mask_name!r} is {area} tiles and none of them can be walked to")


CONTRACTS: dict[str, Callable[[MapBuild, dict, Report], None]] = {
    "single_gorge": contract_single_gorge,
    "confluence_upstream": contract_confluence_upstream,
    "never_touch": contract_never_touch,
    "unreachable": contract_unreachable,
}


def check_contract(b: MapBuild, spec: dict, r: Report) -> None:
    """Run every entry of the spec's `[contract]` table."""
    for name, params in (spec.get("contract") or {}).items():
        fn = CONTRACTS.get(name)
        if fn is None:
            raise SpecError(f"unknown contract {name!r}; known: {', '.join(sorted(CONTRACTS))}")
        if not isinstance(params, dict):
            raise SpecError(f"contract {name!r}: expected a table of parameters, got {params!r}")
        try:
            fn(b, params, r)
        except KeyError as exc:
            raise SpecError(f"contract {name!r}: missing parameter {exc}") from exc
