"""Where every campaign level stands, offline: its row, its land, its tasks, its scenes, and the gaps.

    python .claude/skills/wardens-level-workshop/scripts/level_status.py [--root .]

Reads the level table in wardens/src/WardensCampaign.cs, wardens/maps/levels.toml, the task files in
wardens/src/Levels/, the scenes in wardens/src/Cutscenes/ and the maps in wardens/src/Maps/. Standard
library only (tomllib, 3.11+), no game files. Prints one block per level and exits 0; it reports, it
does not judge (the checkers do that).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import tomllib
from pathlib import Path


def read_levels(cs: Path) -> list[tuple[str, str, str, str, str, bool]]:
    text = re.sub(r"//[^\n]*", "", cs.read_text(encoding="utf-8"))
    rows = re.findall(r'new WardensLevel\(\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,'
                      r'\s*(?:"([^"]*)"|null)\s*,\s*(true|false)\s*\)', text)
    return [(i, m, t, e, n or "", s == "true") for i, m, t, e, n, s in rows]


def dependencies(tasks: list[dict]) -> dict[str, list[str]]:
    deps: dict[str, list[str]] = {}
    previous = None
    for t in tasks:
        tid = t.get("id")
        if not isinstance(tid, str):
            continue
        after = t.get("after")
        deps[tid] = list(after) if isinstance(after, list) else ([previous] if previous else [])
        previous = tid
    return deps


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=".", help="repository root")
    args = ap.parse_args()
    root = Path(args.root)
    src = root / "wardens" / "src"
    cs = src / "WardensCampaign.cs"
    if not cs.exists():
        print(f"no {cs}; run from the repository root or pass --root", file=sys.stderr)
        return 2
    index = {}
    toml_path = root / "wardens" / "maps" / "levels.toml"
    if toml_path.exists():
        index = tomllib.loads(toml_path.read_text(encoding="utf-8")).get("levels", {})

    scenes: dict[str, list[tuple[str, int, float]]] = {}
    for p in sorted((src / "Cutscenes").glob("*.json")):
        try:
            d = json.loads(p.read_text(encoding="utf-8-sig"))
        except ValueError:
            print(f"(unreadable scene {p.name})")
            continue
        shots = d.get("shots", [])
        seconds = sum(s.get("seconds", 0) for s in shots if isinstance(s, dict))
        for trig in d.get("on", []):
            scenes.setdefault(trig, []).append((d.get("id", p.stem), len(shots), seconds))

    def named(trigger: str) -> str:
        found = scenes.get(trigger, [])
        return ", ".join(f"{sid} ({n} shots, {sec:g} s)" for sid, n, sec in found) or "none"

    maps = {p.stem for p in (src / "Maps").glob("*.timber")}
    for lid, map_name, title, ends, nxt, shipped in read_levels(cs):
        spec = index.get(lid, {}).get("spec", "")
        spec_ok = bool(spec) and (root / "wardens" / "maps" / spec).exists()
        print(f"level {lid}  {title}  ({map_name})  next={nxt or '-'}  shipped={'yes' if shipped else 'no'}")
        print(f"  land:    spec {spec or 'none'}{'' if spec_ok or not spec else ' (MISSING FILE)'}; "
              f".timber {'yes' if map_name in maps else 'no'}")
        task_file = src / "Levels" / f"{lid}.tasks.json"
        if task_file.exists():
            tasks = json.loads(task_file.read_text(encoding="utf-8")).get("tasks", [])
            deps = dependencies(tasks)
            no_scene = [t for t in deps if f"task:{lid}.{t}" not in scenes]
            print(f"  tasks:   {len(deps)}  " + "  ".join(f"{t}<{'+'.join(d) or 'start'}" for t, d in deps.items()))
            print(f"  scenes:  opening {named(f'level:{lid}')}")
            for t in deps:
                beat = scenes.get(f"task:{lid}.{t}")
                done = scenes.get(f"task_done:{lid}.{t}")
                if beat or done:
                    extra = f"; done: {named(f'task_done:{lid}.{t}')}" if done else ""
                    print(f"           {t}: {named(f'task:{lid}.{t}')}{extra}")
            print(f"           end {named(f'level_complete:{lid}')}")
            print(f"  gaps:    tasks without a scene: {', '.join(no_scene) or 'none'}")
        else:
            print(f"  tasks:   none (Levels/{lid}.tasks.json missing){'; ends with ' + ends if ends else ''}")
            print(f"  scenes:  opening {named(f'level:{lid}')}")
        print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
