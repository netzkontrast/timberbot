"""`mapsmith` — build, inspect and check Timberborn maps from a spec file.

    python wardens/tools/mapsmith build wardens/maps/wardens-wasteland.map.toml
    python wardens/tools/mapsmith preview wardens/maps/wardens-wasteland.map.toml
    python wardens/tools/mapsmith check "wardens/src/Maps/Wardens Wasteland.timber"
    python wardens/tools/mapsmith new wardens/maps/level-02.map.toml --size 96 --seed 7
    python wardens/tools/mapsmith ops

Exit code 0 when there is nothing to fix, 1 when there is. Warnings alone do not fail a build
unless `--strict` is passed.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import spec as spec_mod
from .build import MapBuild, SpecError
from .checks import check_file
from .preview import ascii_map, write_png
from .terrain import OPS
from .world import read_timber, write_timber

REPO = Path(__file__).resolve().parents[3]
DEFAULT_OUT = REPO / "wardens" / "src" / "Maps"

TEMPLATE = '''# {name} — a Timberborn map spec for mapsmith.
#   python wardens/tools/mapsmith build {path}
name = "{name}"
size = {size}
seed = {seed}
description = "TODO: one sentence the map browser shows."

# Landscaping, in order. `python wardens/tools/mapsmith ops` lists every verb and its parameters.
[[terrain]]
op = "base"
height = 6

[[terrain]]
op = "noise"
amplitude = 3.5
contrast = 1.9

[[terrain]]
op = "rim"
width = 10
height = 5

[[terrain]]
op = "pad"
at = [{pad}, {pad}]
size = 8
height = 8
name = "start"
reserve = 3

[[terrain]]
op = "clamp"
min = 3
max = 19

[[place]]
template = "StartingLocation"
at = "start"
dx = -2
dy = -2
orientation = "Cw0"

[checks]
require = ["StartingLocation"]
min_reachable = 500
'''


def _load(args) -> tuple[dict, MapBuild]:
    spec = spec_mod.load(Path(args.spec), getattr(args, "variant", None))
    return spec, spec_mod.build(spec)


def cmd_build(args) -> int:
    spec, b = _load(args)
    out = Path(args.out) if args.out else Path(args.out_dir or DEFAULT_OUT) / f"{spec['name']}.timber"
    write_timber(b, spec, out)
    report = spec_mod.check(spec, b)
    print(f"wrote {out} ({out.stat().st_size} bytes)")
    print(spec_mod.describe(spec, b))
    if args.preview:
        png = write_png(b, Path(args.preview), spec)
        print(f"preview {png}")
    if args.ascii:
        print()
        print(ascii_map(b, step=args.step))
    print(report.summary())
    return 1 if (report.errors or (args.strict and report.warnings)) else 0


def cmd_preview(args) -> int:
    spec, b = _load(args)
    print(spec_mod.describe(spec, b))
    print()
    print(ascii_map(b, step=args.step))
    if args.png:
        print(f"\npreview {write_png(b, Path(args.png), spec)}")
    return 0


def cmd_check(args) -> int:
    target = Path(args.target)
    opts: dict = {}
    if target.suffix == ".toml":
        spec = spec_mod.load(target, args.variant)
        b = spec_mod.build(spec)
        report = spec_mod.check(spec, b)
        print(spec_mod.walk_report(b))
    else:
        if args.spec:
            opts = spec_mod.load(Path(args.spec), args.variant).get("checks", {})
        report = check_file(target, opts)
    print(f"{target}: {report.summary()}")
    return 1 if (report.errors or (args.strict and report.warnings)) else 0


def cmd_describe(args) -> int:
    target = Path(args.target)
    if target.suffix == ".toml":
        spec = spec_mod.load(target, args.variant)
        print(spec_mod.describe(spec, spec_mod.build(spec) if args.build else None))
        return 0
    world, meta, _ = read_timber(target)
    sing = world.get("Singletons", {})
    size = sing.get("MapSize", {}).get("Size", {})
    counts: dict[str, int] = {}
    for e in world.get("Entities", []):
        counts[e.get("Template", "?")] = counts.get(e.get("Template", "?"), 0) + 1
    print(f"{target.name}: {size.get('X')}x{size.get('Y')}  GameVersion {world.get('GameVersion')}  "
          f"{len(world.get('Entities', []))} entities")
    if meta.get("MapDescription"):
        print(f"  {meta['MapDescription']}")
    for template, n in sorted(counts.items(), key=lambda kv: (-kv[1], kv[0])):
        print(f"  {n:4d}  {template}")
    return 0


def cmd_new(args) -> int:
    path = Path(args.spec)
    if path.exists() and not args.force:
        print(f"{path} exists (pass --force to overwrite)", file=sys.stderr)
        return 1
    name = args.name or path.stem.replace("-", " ").replace(".map", "").title()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(TEMPLATE.format(name=name, size=args.size, seed=args.seed, path=path,
                                    pad=args.size // 4), encoding="utf-8")
    print(f"wrote {path}\nnext: python wardens/tools/mapsmith preview {path}")
    return 0


def cmd_ops(_args) -> int:
    print("terrain ops (use as `op = \"<name>\"` in a [[terrain]] block):\n")
    for name, fn in sorted(OPS.items()):
        doc = (fn.__doc__ or "").strip().splitlines()
        print(f"  {name:9s} {doc[0] if doc else ''}")
        for line in doc[1:]:
            if line.strip():
                print(f"            {line.strip()}")
        print()
    print("placement: [[place]] (template, at|along, orientation, strength, buried, footprint)")
    print("           [[scatter]] (templates, weights, levels, around, radius, count, spacing,")
    print("                        height_range, away_from, margin, growth, variants, yield)")
    return 0


def main(argv: list[str] | None = None) -> int:
    # `--strict` is accepted on either side of the subcommand: putting a flag after the spec is the
    # natural guess, and an argparse error there is a pure waste of the reader's time. The two need
    # separate dests because a subparser overwrites a shared one with its own default.
    strict = argparse.ArgumentParser(add_help=False)
    strict.add_argument("--strict", action="store_true", dest="strict_here",
                        help="fail on warnings too")
    ap = argparse.ArgumentParser(prog="mapsmith", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--strict", action="store_true", dest="strict_first",
                    help="fail on warnings too (also accepted after the subcommand)")
    sub = ap.add_subparsers(dest="cmd", required=True)

    b = sub.add_parser("build", parents=[strict], help="write the .timber a spec describes, then check it")
    b.add_argument("spec")
    b.add_argument("--out", help="explicit output path")
    b.add_argument("--out-dir", help=f"directory to write into (default {DEFAULT_OUT})")
    b.add_argument("--variant")
    b.add_argument("--preview", help="also write a PNG preview here")
    b.add_argument("--ascii", action="store_true", help="also print the ASCII map")
    b.add_argument("--step", type=int, default=1)
    b.set_defaults(func=cmd_build)

    p = sub.add_parser("preview", parents=[strict], help="build in memory and print the ASCII map (writes nothing)")
    p.add_argument("spec")
    p.add_argument("--variant")
    p.add_argument("--step", type=int, default=1, help="sample every Nth tile")
    p.add_argument("--png", help="also write a PNG here")
    p.set_defaults(func=cmd_preview)

    c = sub.add_parser("check", parents=[strict], help="validate a .timber file or a spec")
    c.add_argument("target", help="a .timber file or a .map.toml spec")
    c.add_argument("--spec", help="spec whose [checks] table to apply to a .timber")
    c.add_argument("--variant")
    c.set_defaults(func=cmd_check)

    d = sub.add_parser("describe", parents=[strict], help="what is in a spec or a built map")
    d.add_argument("target")
    d.add_argument("--variant")
    d.add_argument("--build", action="store_true", help="also run the spec and report what came out")
    d.set_defaults(func=cmd_describe)

    n = sub.add_parser("new", parents=[strict], help="write a starter spec")
    n.add_argument("spec")
    n.add_argument("--name")
    n.add_argument("--size", type=int, default=96)
    n.add_argument("--seed", type=int, default=1)
    n.add_argument("--force", action="store_true")
    n.set_defaults(func=cmd_new)

    o = sub.add_parser("ops", parents=[strict], help="list the terrain ops and placement keys")
    o.set_defaults(func=cmd_ops)

    args = ap.parse_args(argv)
    args.strict = bool(getattr(args, "strict_first", False) or getattr(args, "strict_here", False))
    try:
        return args.func(args)
    except SpecError as exc:
        print(f"spec error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
