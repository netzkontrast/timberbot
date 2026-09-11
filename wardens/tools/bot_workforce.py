"""The Wardens' workforce rule: bots work everywhere by default, and nothing about bots costs science.

The Warden is the best AI its makers ever built; it knows everything there is to know about bots. So
in every Wardens blueprint (TemplateName ending in ".Wardens"):

  - a workplace defaults to Bot workers and has no WorkerTypeUnlockCosts (vanilla charges science,
    per building, before bots may work there);
  - a building whose purpose is bots (its template name contains "Bot") costs no science;
  - a district center carries WardensBotWorkforceSpec, so src/WardensBotWorkforce.cs sets the
    district's own default worker type to Bot. That default, "Beaver" in vanilla, is copied over a
    workplace's blueprint default when the workplace finishes (WorkplaceWorkerType,
    SetWorkerTypeToDistrict), so without it the first two rules would not show in the game.

Two exceptions: a workplace that allows only its own worker type (DisallowOtherWorkerTypes, e.g. the
Power Treadmill a beaver runs) keeps it, and a chapter padlock (ScienceCost CHAPTER_LOCK) is story
gating, not science, so it stays.

The generators call apply_tree() after writing; tools/validate.py calls violations().

    python wardens/tools/bot_workforce.py            # rewrite wardens/src in place
    python wardens/tools/bot_workforce.py --check    # exit 1 if any blueprint breaks the rule
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

SRC = Path(__file__).resolve().parents[1] / "src"
BOT = "Bot"
SPEC = "WardensBotWorkforceSpec"
# Must equal gen_buildings.CHAPTER_LOCK and WardensChapterService.LockedCost (validate.py checks those two).
CHAPTER_LOCK = 999999


def is_wardens(d: dict) -> bool:
    return str(d.get("TemplateSpec", {}).get("TemplateName", "")).endswith(".Wardens")


def violations(d: dict) -> list[str]:
    """What in this blueprint breaks the rule, in words; empty when it holds or is not ours."""
    if not is_wardens(d):
        return []
    out: list[str] = []
    work = d.get("WorkplaceSpec")
    if isinstance(work, dict) and not work.get("DisallowOtherWorkerTypes", False):
        if work.get("DefaultWorkerType", "Beaver") != BOT:
            out.append(f"WorkplaceSpec.DefaultWorkerType is {work.get('DefaultWorkerType', 'Beaver')!r}, not {BOT!r}")
        if work.get("WorkerTypeUnlockCosts"):
            costs = ", ".join(f"{c.get('WorkerType')}:{c.get('ScienceCost')}" for c in work["WorkerTypeUnlockCosts"])
            out.append(f"WorkplaceSpec.WorkerTypeUnlockCosts is not empty ({costs})")
    name = d["TemplateSpec"]["TemplateName"]
    science = d.get("BuildingSpec", {}).get("ScienceCost", 0)
    if BOT in name and 0 < science < CHAPTER_LOCK:
        out.append(f"a bot building costs {science} science")
    if "DistrictCenterSpec" in d and SPEC not in d:
        out.append(f"district center without {SPEC}")
    return out


def botify(d: dict) -> bool:
    """Make one blueprint follow the rule. True when it changed."""
    if not violations(d):
        return False
    work = d.get("WorkplaceSpec")
    if isinstance(work, dict) and not work.get("DisallowOtherWorkerTypes", False):
        work["DefaultWorkerType"] = BOT
        work["WorkerTypeUnlockCosts"] = []
    science = d.get("BuildingSpec", {}).get("ScienceCost", 0)
    if BOT in d["TemplateSpec"]["TemplateName"] and 0 < science < CHAPTER_LOCK:
        d["BuildingSpec"]["ScienceCost"] = 0
    if "DistrictCenterSpec" in d:
        d.setdefault(SPEC, {})
    return True


def apply_tree(root: Path = SRC) -> list[Path]:
    """Rewrite every Wardens blueprint under root that breaks the rule; the paths that changed."""
    changed: list[Path] = []
    for p in sorted(root.rglob("*.blueprint.json")):
        try:
            d = json.loads(p.read_text(encoding="utf-8-sig"))
        except (OSError, ValueError):
            continue
        if isinstance(d, dict) and botify(d):
            with open(p, "w", encoding="utf-8", newline="\n") as f:
                json.dump(d, f, indent=2)
                f.write("\n")
            changed.append(p)
    return changed


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true", help="report and exit 1 instead of rewriting")
    ap.add_argument("root", nargs="?", default=str(SRC))
    args = ap.parse_args()
    root = Path(args.root)
    if args.check:
        bad = 0
        for p in sorted(root.rglob("*.blueprint.json")):
            try:
                d = json.loads(p.read_text(encoding="utf-8-sig"))
            except (OSError, ValueError):
                continue
            for v in violations(d) if isinstance(d, dict) else []:
                print(f"{p.relative_to(root).as_posix()}: {v}")
                bad += 1
        print(f"bot workforce: {'problems: ' + str(bad) if bad else 'problems: none'}")
        return 1 if bad else 0
    for p in apply_tree(root):
        print("botified", p.relative_to(root).as_posix())
    return 0


if __name__ == "__main__":
    sys.exit(main())
