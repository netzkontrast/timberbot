"""Placement: `[[place]]` puts one thing somewhere exact, `[[scatter]]` sows many by rule.

Both build `Entity` records. Neither knows what a template means to the game — the vocabulary
(`BadwaterSource`, `RuinColumnH3`, `Pine`) is data in the spec, so the same code places a Wardens
scrap field and a vanilla birch forest. The convenience keys (`strength`, `growth`, `yield`,
`variants`) exist because those four components are what every Timberborn map entity actually uses;
anything else goes in a raw `components` table and is copied through verbatim.
"""
from __future__ import annotations

import math

from .build import ON_FOOT, Entity, MapBuild, SpecError, polar_sample
from .grid import Mask


def _components(rule: dict) -> dict:
    """Turn the convenience keys into the game's component shapes."""
    comps: dict = dict(rule.get("components", {}))
    if "strength" in rule:
        s = float(rule["strength"])
        comps["WaterSource"] = {"SpecifiedStrength": s, "CurrentStrength": s}
    return comps


def _decorate(comps: dict, rule: dict, b: MapBuild, level: int) -> dict:
    comps = dict(comps)
    if "growth" in rule:
        lo, hi = (rule["growth"] if isinstance(rule["growth"], (list, tuple)) else (rule["growth"], rule["growth"]))
        comps["Growable"] = {"GrowthProgress": round(b.rng.uniform(float(lo), float(hi)), 3)}
    variants = rule.get("variants")
    if variants:
        comps["RuinModels"] = {"VariantId": b.rng.choice(list(variants))}
    y = rule.get("yield")
    if y:
        amount = y.get("amount")
        if amount is None:
            amount = int(y.get("amount_per_level", 0)) * max(level, 1)
        comps["Yielder:Ruin"] = {"Yield": {"Good": {"Id": y["good"]}, "Amount": int(amount)}}
    return comps


def _entity(b: MapBuild, template: str, x: int, y: int, rule: dict, level: int = 1) -> Entity:
    buried = int(rule.get("buried", 0))
    z = b.surface_z(x, y) - buried
    if z < 0:
        raise SpecError(f"{template} at {x},{y}: buried {buried} puts it below Z 0")
    return Entity(template=template, x=x, y=y, z=z,
                  components=_decorate(_components(rule), rule, b, level),
                  orientation=rule.get("orientation"),
                  rule=str(rule.get("name", "")))


def _footprint(rule: dict) -> int:
    return int(rule.get("footprint", 1))


def place(b: MapBuild, rule: dict) -> list[Entity]:
    """One entity at an exact spot, or `count` of them spread along a tagged path."""
    template = rule.get("template")
    if not template:
        raise SpecError(f"place block without a `template`: {rule!r}")
    placed: list[Entity] = []

    along = rule.get("along")
    if along is not None:
        path, mask_name = _resolve_path(b, along)
        lo, hi = rule.get("segment", [0.0, 0.05])
        count = int(rule.get("count", 1))
        window = [p for i, p in enumerate(path) if lo <= i / max(len(path) - 1, 1) <= hi]
        if not window:
            raise SpecError(f"place along {along!r}: segment {[lo, hi]} selects no path points")
        seen: set[tuple[int, int]] = set()
        spread = rule.get("spread", 1)   # also try neighbours across the channel
        edge = int(rule.get("margin", 1))  # keep off the map border
        off_map = 0
        for px, py in window:
            for dx in range(-spread, spread + 1):
                x, y = int(px) + dx, int(py)
                if len(seen) >= count:
                    break
                if (x, y) in seen:
                    continue
                if not (edge <= x < b.size - edge and edge <= y < b.size - edge):
                    off_map += 1
                    continue
                mask = b.masks.get(mask_name)
                if mask is not None and not mask.at(x, y):
                    continue
                seen.add((x, y))
                e = _entity(b, template, x, y, rule)
                b.add(e, _footprint(rule))
                placed.append(e)
        if len(placed) < count:
            why = (f" — {off_map} candidate tiles were outside the map or inside its {edge}-tile "
                   f"border, so the segment starts off the edge; move it inland "
                   f"(segment = [{max(lo, 0.03):.2f}, {hi + 0.05:.2f}])" if off_map else
                   "; widen `segment`, raise `spread`, or lower `count`")
            raise SpecError(f"place along {along!r}: wanted {count} {template}, placed {len(placed)}{why}")
        return placed

    x, y = (int(round(v)) for v in b.resolve_point(rule.get("at", [0, 0])))
    x += int(rule.get("dx", 0))
    y += int(rule.get("dy", 0))
    if not b.height.inside(x, y):
        raise SpecError(f"place {template} at {x},{y}: outside the {b.size}x{b.size} map")
    e = _entity(b, template, x, y, rule)
    b.add(e, _footprint(rule))
    return [e]


def scatter(b: MapBuild, rule: dict) -> list[Entity]:
    """Sow `count` entities in a disc or annulus, honouring spacing, height band and keep-outs.

    Sampling is rejection-based and bounded by `attempts`; a rule that cannot be satisfied raises
    rather than silently under-filling, because a map that quietly lost half its scrap is a map
    whose playtest wastes an evening.
    """
    templates = rule.get("templates") or ([rule["template"]] if "template" in rule else None)
    if not templates:
        raise SpecError(f"scatter block without `templates`: {rule!r}")
    weights = [float(w) for w in rule.get("weights", [1.0] * len(templates))]
    if len(weights) != len(templates):
        raise SpecError(f"scatter {rule.get('name', '')!r}: {len(templates)} templates but {len(weights)} weights")
    levels = rule.get("levels") or [1] * len(templates)

    centre = b.resolve_point(rule.get("around", [b.size / 2, b.size / 2]))
    radius = rule.get("radius", 5.0)
    inner, outer = (float(radius[0]), float(radius[1])) if isinstance(radius, (list, tuple)) else (0.0, float(radius))
    count = int(rule.get("count", 1))
    spacing = int(rule.get("spacing", 2))
    margin = int(rule.get("margin", 1))
    lo_h, hi_h = rule.get("height_range", [0, 99])
    keep_out = rule.get("away_from", {})     # {mask_name: distance}
    attempts = int(rule.get("attempts", max(400, count * 60)))
    required = int(rule.get("min_count", count))

    # A river cuts a map into components, and `around` + `radius` is a disc that knows nothing about
    # that: without this, keeping a cluster on the colony's own bank means hand-picking centres and
    # radii small enough that the annulus geometrically cannot cross the water.
    walk: Mask | None = None
    if "reachable_from" in rule:
        origin = b.resolve_point(rule["reachable_from"])
        walk = b.walkable_cached((int(round(origin[0])), int(round(origin[1]))))
        if not walk.any():
            raise SpecError(f"scatter {rule.get('name', templates[0])!r}: reachable_from "
                            f"{rule['reachable_from']!r} stands on water or off the map, so nothing "
                            f"is reachable from it")
    # Stricter: the entity's own tile on the same level as the point, joined by flat ground. A
    # Scavenger Flag's range is the terrain it can walk to without Stairs, and a ruin counts only
    # when its own tile lies in that range, so this is what "the first scrap needs no Stairs" means.
    on_foot: Mask | None = None
    if "on_foot_from" in rule:
        origin = b.resolve_point(rule["on_foot_from"])
        on_foot = b.walkable_cached((int(round(origin[0])), int(round(origin[1]))), ON_FOOT)
        if not on_foot.any():
            raise SpecError(f"scatter {rule.get('name', templates[0])!r}: on_foot_from "
                            f"{rule['on_foot_from']!r} stands on water or off the map, so nothing "
                            f"is reachable from it")

    placed: list[Entity] = []
    for _try in range(attempts):
        if len(placed) >= count:
            break
        x, y = polar_sample(b.rng, centre, inner, outer)
        if not b.free(x, y, margin=margin):
            continue
        h = b.h(x, y)
        if not (lo_h <= h <= hi_h):
            continue
        if spacing > 1 and any(abs(x - e.x) < spacing and abs(y - e.y) < spacing for e in placed):
            continue
        if _too_close(b, x, y, keep_out):
            continue
        if walk is not None and not _beside(walk, x, y, b.size):
            continue
        if on_foot is not None and not on_foot.at(x, y):
            continue
        i = _weighted_index(b, weights)
        e = _entity(b, templates[i], x, y, rule, level=int(levels[i]))
        b.add(e, _footprint(rule))
        placed.append(e)

    if len(placed) < required:
        raise SpecError(
            f"scatter {rule.get('name', templates[0])!r}: placed {len(placed)} of {count} in {attempts} "
            f"attempts (needed {required}). Widen `radius`, loosen `height_range`/`spacing`, "
            f"or lower `count`/`min_count`."
            + (f" `reachable_from` is on, so candidates outside the walkable component of "
               f"{rule['reachable_from']!r} were also skipped — the annulus may sit across water."
               if walk is not None else "")
            + (f" `on_foot_from` is on, so only tiles at the height of {rule['on_foot_from']!r} "
               f"and joined to it by flat ground were candidates."
               if on_foot is not None else ""))
    return placed


def _resolve_path(b: MapBuild, along: str) -> tuple[list, str]:
    """`along` is a river's `name`, or a `tag` carried by exactly one river."""
    if along in b.paths:
        return b.paths[along], b.path_tag_of.get(along, along)
    keys = b.path_tags.get(along, [])
    if len(keys) == 1:
        return b.paths[keys[0]], along
    if len(keys) > 1:
        raise SpecError(f"place along {along!r}: {len(keys)} watercourses share that tag "
                        f"({', '.join(keys)}). Name the one you mean: along = \"<river name>\".")
    raise SpecError(f"place along {along!r}: no watercourse with that name or tag; "
                    f"known: {', '.join(sorted(b.paths)) or 'none'}")


def _beside(walk: Mask, x: int, y: int, size: int) -> bool:
    """A beaver has to stand next to a thing to use it, so touching the component is enough."""
    for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < size and 0 <= ny < size and walk.at(nx, ny):
            return True
    return False


def _too_close(b: MapBuild, x: int, y: int, keep_out: dict) -> bool:
    """`away_from = { badwater = 9.0, start = 16.0 }` — names may be masks or anchors."""
    for name, dist in keep_out.items():
        if name in b.masks:
            if b.distance_to(name).at(x, y) < float(dist):
                return True
        elif name in b.anchors:
            ax, ay = b.anchors[name]
            if math.hypot(x - ax, y - ay) < float(dist):
                return True
        else:
            raise SpecError(f"away_from {name!r}: no mask or anchor with that name; "
                            f"masks {sorted(b.masks)}, anchors {sorted(b.anchors)}")
    return False


def _weighted_index(b: MapBuild, weights: list[float]) -> int:
    total = sum(weights)
    pick = b.rng.random() * total
    acc = 0.0
    for i, w in enumerate(weights):
        acc += w
        if pick <= acc:
            return i
    return len(weights) - 1
