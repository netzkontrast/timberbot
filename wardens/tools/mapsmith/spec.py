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
    return "\n".join(lines)
