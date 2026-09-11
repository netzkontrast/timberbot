"""Checks the level task files (src/Levels/<id>.tasks.json) that WardensLevelTasks.cs runs.

A task file is how a campaign level ends when the vanilla tutorial is off, so a mistake in it is a
level that never completes: an unknown template counts 0 forever, a stock check on a good that does
not exist never fills. This catches those before the game does.

    python wardens/tools/check_level_tasks.py [wardens/src]

validate.py calls check_level_tasks() with the templates and goods it already resolved.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

TYPES = {"built", "powered", "generating", "workers", "stock", "beavers", "built_in", "clean_water"}
PANEL_KEYS = ["Wardens.Tasks.Header", "Wardens.Tasks.Complete", "Wardens.Tasks.CompleteText",
              "Wardens.Tasks.LastText", "Wardens.Tasks.Continue", "Wardens.Tasks.Stay",
              "Wardens.Tasks.Done", "Wardens.Tasks.Next", "Wardens.Tasks.Starting"]
SUFFIX = ".tasks.json"


def task_files(mod: Path) -> list[Path]:
    folder = mod / "Levels"
    return sorted(folder.glob("*" + SUFFIX)) if folder.is_dir() else []


def levels_with_tasks(mod: Path) -> set[str]:
    """Level ids whose task file parses and has at least one task."""
    out = set()
    for p in task_files(mod):
        try:
            if json.loads(p.read_text(encoding="utf-8")).get("tasks"):
                out.add(p.name[:-len(SUFFIX)])
        except (OSError, ValueError):
            pass
    return out


def check_level_tasks(mod: Path, templates: dict[str, dict] | None, goods: set[str] | None, loc: set[str],
                      level_ids: set[str]) -> list[str]:
    """`templates` and `goods` None skips those lookups (the stand-alone run has no vanilla data)."""
    problems: list[str] = []
    files = task_files(mod)
    used_types: set[str] = set()
    for p in files:
        where = f"Levels/{p.name}"
        level = p.name[:-len(SUFFIX)]
        try:
            data = json.loads(p.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            problems.append(f"{where}: not readable JSON ({exc})")
            continue
        if level_ids and level not in level_ids:
            problems.append(f"{where}: level {level!r} is not in WardensCampaign.cs")
        if data.get("level") != level:
            problems.append(f"{where}: \"level\" is {data.get('level')!r}, the file name says {level!r}")
        tasks = data.get("tasks")
        if not isinstance(tasks, list) or not tasks:
            problems.append(f"{where}: no tasks, so the level cannot end by its tasks")
            continue
        seen: set[str] = set()
        for i, task in enumerate(tasks):
            tid = task.get("id") if isinstance(task, dict) else None
            at = f"{where} task {tid or i}"
            if not tid or tid in seen:
                problems.append(f"{at}: id missing or repeated")
                continue
            seen.add(tid)
            for field in ("title", "text"):
                key = task.get(field)
                if not key:
                    problems.append(f"{at}: no {field}")
                elif key not in loc:
                    problems.append(f"{at}: {field} loc key missing: {key}")
            checks = task.get("checks")
            if not isinstance(checks, list) or not checks:
                problems.append(f"{at}: no checks, it could never be done")
                continue
            for c in checks:
                kind = c.get("type") if isinstance(c, dict) else None
                if kind not in TYPES:
                    problems.append(f"{at}: unknown check type {kind!r} (known: {', '.join(sorted(TYPES))})")
                    continue
                used_types.add(kind)
                count = c.get("count", 1)
                if not isinstance(count, int) or count < 1:
                    problems.append(f"{at}: {kind} count must be a whole number >= 1, got {count!r}")
                if kind in ("built_in", "clean_water"):
                    box = c.get("box")
                    if not (isinstance(box, list) and len(box) == 4 and all(isinstance(v, int) for v in box)
                            and box[0] <= box[2] and box[1] <= box[3]):
                        problems.append(f"{at}: {kind} needs box [x1, y1, x2, y2] with x1 <= x2 and y1 <= y2, got {box!r}")
                    where = c.get("where")
                    if not where:
                        problems.append(f"{at}: {kind} needs `where`, the loc key naming its box on the panel")
                    elif where not in loc:
                        problems.append(f"{at}: where loc key missing: {where}")
                if kind == "stock":
                    if goods is not None and c.get("good") not in goods:
                        problems.append(f"{at}: stock of an unknown good {c.get('good')!r}")
                    continue
                if kind in ("beavers", "clean_water"):
                    continue
                names = c.get("templates") or ([c["template"]] if c.get("template") else [])
                if not names:
                    problems.append(f"{at}: {kind} needs a template")
                    continue
                if templates is None:
                    continue
                unknown = [n for n in names if n not in templates]
                if unknown:
                    problems.append(f"{at}: {kind} of an unknown template {unknown[0]!r}")
                    continue
                template = names[0]
                spec = templates[template]
                if kind in ("powered", "generating") and "MechanicalNodeSpec" not in spec:
                    problems.append(f"{at}: {kind} on {template}, which has no MechanicalNodeSpec")
                if kind == "generating" and not spec.get("MechanicalNodeSpec", {}).get("PowerOutput"):
                    problems.append(f"{at}: generating on {template}, which makes no power")
                if kind == "workers" and "WorkplaceSpec" not in spec:
                    problems.append(f"{at}: workers on {template}, which is not a workplace")
    if files:
        extra = ["Wardens.Tasks.In"] if "built_in" in used_types else []
        for key in PANEL_KEYS + extra + [f"Wardens.Tasks.Check.{t}" for t in sorted(used_types)]:
            if key not in loc:
                problems.append(f"loc key missing for the task panel: {key}")
    return problems


def main() -> int:
    """Stand-alone: structure and loc only. Templates and goods need the game's blueprints, which
    validate.py resolves; run that for the full check."""
    import csv
    mod = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "src"
    loc = {r[0] for r in csv.reader(open(mod / "Localizations" / "enUS.csv", encoding="utf-8")) if r}
    problems = check_level_tasks(mod, None, None, loc, set())
    print(f"task files: {len(task_files(mod))} (templates and goods: run validate.py)")
    print("problems: none" if not problems else "problems:\n  - " + "\n  - ".join(problems))
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
