"""mapsmith — Timberborn `.timber` maps from a declarative spec.

    from mapsmith import load, build, write_timber, check_file, ascii_map

    spec = load(Path("wardens/maps/wardens-01-first-light.map.toml"))
    m = build(spec)
    print(ascii_map(m))
    write_timber(m, spec, Path("out.timber"))

Standard library only (Pillow is used for a nicer thumbnail if it happens to be installed).
The command line is `python wardens/tools/mapsmith --help`.
"""
from __future__ import annotations

from .build import Entity, MapBuild, SpecError
from .checks import Problem, Report, check_file, check_world
from .preview import ascii_map, write_png
from .spec import build, describe, load
from .world import metadata_json, read_timber, world_json, write_timber

__all__ = [
    "Entity", "MapBuild", "SpecError", "Problem", "Report",
    "load", "build", "describe", "write_timber", "read_timber", "world_json", "metadata_json",
    "check_file", "check_world", "ascii_map", "write_png",
]
