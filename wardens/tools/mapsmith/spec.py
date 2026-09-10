"""Loading a map spec and running it: TOML in, a built map out.

A spec is data, not code, for three reasons: two specs can be diffed, a spec can be checked before
it is built, and a spec is short enough that writing a new map is an act of description rather than
programming. The shape is:

    name / size / seed / description       — identity
    [[terrain]]  op = "..."                — landscaping, in order (see terrain.py for the verbs)
    [[place]]    template = "..."          — one entity, exactly here
    [[scatter]]  templates = [...]         — many entities, by rule
    [soil]                                 — moisture and contamination fields
    [checks]                               — what `mapsmith check` must be able to prove

`variants` lets one file describe a family: `mapsmith build spec.toml --variant winter` deep-merges
`[variants.winter]` over the base before anything runs.
"""
from __future__ import annotations

import copy
import sys
from pathlib import Path

if sys.version_info >= (3, 11):   # tomllib is stdlib on 3.11+; timberbot pins tomli for 3.10
    import tomllib
else:  # pragma: no cover - only on 3.10
    import tomli as tomllib

from . import placement, terrain
from .build import MapBuild, SpecError
from .checks import Report, check_world
from .world import metadata_json, world_json

REQUIRED = ("name", "size")


def load(path: Path, variant: str | None = None) -> dict:
    try:
        spec = tomllib.loads(Path(path).read_text(encoding="utf-8"))
    except tomllib.TOMLDecodeError as exc:
        raise SpecError(f"{path}: {exc}") from exc
    spec.setdefault("source", str(path))
    if variant:
        variants = spec.get("variants", {})
        if variant not in variants:
            raise SpecError(f"{path}: no [variants.{variant}]; known: {sorted(variants) or 'none'}")
        spec = deep_merge(spec, variants[variant])
        spec["name"] = spec.get("name", "map")
        spec["variant"] = variant
    spec.pop("variants", None)
    for key in REQUIRED:
        if key not in spec:
            raise SpecError(f"{path}: `{key}` is required at the top of the spec")
    if int(spec["size"]) < 16:
        raise SpecError(f"{path}: size {spec['size']} is too small to hold a starting location")
    return spec


def deep_merge(base: dict, over: dict) -> dict:
    """Tables merge key by key; arrays and scalars are replaced outright."""
    out = copy.deepcopy(base)
    for key, value in over.items():
        if isinstance(value, dict) and isinstance(out.get(key), dict):
            out[key] = deep_merge(out[key], value)
        else:
            out[key] = copy.deepcopy(value)
    return out


def build(spec: dict) -> MapBuild:
    """Run the spec: terrain ops in order, then placements, then scatters."""
    b = MapBuild(int(spec["size"]), int(spec.get("seed", 0)))
    for step in spec.get("terrain", []):
        terrain.apply(b, step)
    if not any(step.get("op") == "clamp" for step in spec.get("terrain", [])):
        b.clamp_height(1, 20)      # the voxel writer needs integers; clamp defensively
    for rule in spec.get("place", []):
        placement.place(b, rule)
    for rule in spec.get("scatter", []):
        placement.scatter(b, rule)
    return b


MEMBERS = {"world.json", "map_metadata.json", "version.txt", "map_thumbnail.jpg"}


def groups_of(b: MapBuild) -> dict[str, list[tuple[int, int]]]:
    """Entities by the `name` of the rule that placed them."""
    out: dict[str, list[tuple[int, int]]] = {}
    for e in b.entities:
        if e.rule:
            out.setdefault(e.rule, []).append((e.x, e.y))
    return out


def check(spec: dict, b: MapBuild) -> Report:
    """Check a built spec. Unlike checking a bare `.timber`, this knows where the water is and which
    rule placed what, so the walkability checks are the strict ones."""
    return check_world(world_json(b, spec), metadata_json(b, spec), set(MEMBERS),
                       spec.get("checks", {}), water=b.water_cells(), groups=groups_of(b))


def walk_report(b: MapBuild, start: tuple[int, int] | None = None) -> str:
    """How far a beaver actually walks from the start to each named group of entities.

    The design language is "scrap within a day's walk", not "scrap at radius 5" — and you cannot
    read walking distance off a heightmap, which is why this exists rather than leaving it to the
    eye. Steps are 4-neighbour, so they are a lower bound on the real path.
    """
    if start is None:
        anchor = next((e for e in b.entities if e.template == "StartingLocation"), None)
        if anchor is None:
            return "  walk: no StartingLocation to measure from"
        start = (anchor.x, anchor.y)
    dist = b.walk_distances(start)
    lines = [f"  walk from the start at {start[0]},{start[1]} ({len(dist)} tiles reachable on foot):"]
    for name, spots in sorted(groups_of(b).items()):
        steps = sorted(d for s in spots
                       for d in [min((dist[(s[0] + dx, s[1] + dy)]
                                      for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                                      if (s[0] + dx, s[1] + dy) in dist), default=None)]
                       if d is not None)
        if not steps:
            lines.append(f"    {name:24s} unreachable on foot ({len(spots)} entities)")
            continue
        mid = steps[len(steps) // 2]
        tail = "" if len(steps) == len(spots) else f"  ({len(spots) - len(steps)} cut off)"
        lines.append(f"    {name:24s} {steps[0]:3d} min  {mid:3d} median  {steps[-1]:3d} max{tail}")
    return "\n".join(lines)


def describe(spec: dict, b: MapBuild | None = None) -> str:
    """A short human/agent-readable account of what a spec asks for and what came out."""
    lines = [f"{spec['name']}  {spec['size']}x{spec['size']}  seed {spec.get('seed', 0)}"]
    if spec.get("variant"):
        lines[0] += f"  variant {spec['variant']}"
    if spec.get("description"):
        lines.append(f"  {spec['description']}")
    ops = [s.get("op", "?") for s in spec.get("terrain", [])]
    lines.append(f"  terrain: {' -> '.join(ops) if ops else 'flat'}")
    for rule in spec.get("place", []):
        where = rule.get("at", f"along {rule.get('along')}")
        lines.append(f"  place   {rule['template']} @ {where}")
    for rule in spec.get("scatter", []):
        templates = rule.get("templates", [rule.get("template")])
        lines.append(f"  scatter {rule.get('count', 1)}x {'/'.join(str(t) for t in templates)} "
                     f"around {rule.get('around')} r={rule.get('radius')}"
                     + (f"  ({rule['name']})" if rule.get("name") else ""))
    if b is not None:
        by_template: dict[str, int] = {}
        for e in b.entities:
            by_template[e.template] = by_template.get(e.template, 0) + 1
        lines.append(f"  built: heights {int(b.height.min())}..{int(b.height.max())}, "
                     f"{len(b.entities)} entities")
        for template, n in sorted(by_template.items(), key=lambda kv: (-kv[1], kv[0])):
            lines.append(f"          {n:4d}  {template}")
        for name, mask in sorted(b.masks.items()):
            lines.append(f"          mask {name}: {mask.count()} tiles")
        lines.append(walk_report(b))
    return "\n".join(lines)
