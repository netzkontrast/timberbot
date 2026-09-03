"""Static checks on the deployed (or source) Wardens mod: everything the game resolves by name at load.

    python wardens/tools/validate.py                 # deployed copy in Documents/Timberborn/Mods/Wardens
    python wardens/tools/validate.py wardens/src     # the source tree

Every crash so far came from a name the game looks up while loading: a template in a tutorial step,
a loc key, a stage id, a planter group with two buildings. This script resolves the same names
against the faction's template collections (ours + the vanilla Blueprints.zip) and the loc tables,
and prints what would throw. Exit code 1 when there are problems.
"""
from __future__ import annotations

import csv
import json
import re
import sys
import zipfile
from pathlib import Path

GAME = Path("F:/Steam/steamapps/common/Timberborn/Timberborn_Data/StreamingAssets/Modding")
DEFAULT_MOD = Path.home() / "Documents/Timberborn/Mods/Wardens"
FACTION = "Wardens"
# Trigger ids that vanilla or WardensTriggers.cs finish through ITutorialTriggers.AddTrigger.
TRIGGER_IDS = {"StairsUnlockedTrigger", "SurvivedFirstDroughtTrigger", "SurvivedFirstBadtideTrigger",
               "Wardens.MissingDamTrigger", "Wardens.PlatformBuiltTrigger", "Wardens.IdleWardensTrigger"}
# Our own step specs and the template/good/loc fields they carry (vanilla ones are listed below).
STEP_FIELDS = {
    "BuildingTutorialStepSpec": {"templates": "TemplateNames"},
    "ConnectBuildingsTutorialStepSpec": {"templates": ["TemplateName", "HighlightableBuildingIds"]},
    "PoweredBuildingStepSpec": {"templates": ["TemplateName", "ShaftTemplateNames"]},
    "PowerBuildingsTutorialStepSpec": {"templates": "TemplateName"},
    "SelectStockpileGoodTutorialStepSpec": {"templates": "TemplateName", "goods": "GoodId"},
    "MarkPlantablesTutorialStepSpec": {"plantables": "TemplateName"},
    "SelectEntityStepSpec": {"templates": "TemplateNames", "loc": "DescriptionLocKey"},
    "UnlockBuildingTutorialStepSpec": {"templates": "TemplateName"},
    "AccumulateScienceForBuildingStepSpec": {"templates": "TemplateName"},
    "IncreaseDesiredWorkersStepSpec": {"templates": "TemplateName"},
    "DecreasePriorityStepSpec": {"templates": "TemplateName"},
    "ChangePausedStateStepSpec": {"templates": "TemplateName"},
    "GoodStockStepSpec": {"goods": "GoodId"},
}


def load_json(raw: bytes) -> dict:
    text = raw.decode("utf-8-sig")
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        # The game's parser tolerates trailing commas (some Leaf Coats blueprints have them).
        return json.loads(re.sub(r",(\s*[}\]])", lambda m: m.group(1), text))


def main() -> int:
    mod = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_MOD
    problems: list[str] = []
    zbp = zipfile.ZipFile(GAME / "Blueprints.zip")
    vanilla = {n[:-len(".blueprint.json")].lower(): n for n in zbp.namelist() if n.endswith(".blueprint.json")}
    ours = {p.relative_to(mod).as_posix()[:-len(".blueprint.json")].lower(): p
            for p in mod.rglob("*.blueprint.json")}

    def blueprint(path: str) -> dict | None:
        key = path.lower()
        if key.endswith(".blueprint"):
            key = key[:-len(".blueprint")]
        if key in ours:
            return load_json(ours[key].read_bytes())
        if key in vanilla:
            return load_json(zbp.read(vanilla[key]))
        return None

    # faction + collections
    faction = next((load_json(p.read_bytes()) for p in ours.values()
                    if p.name.startswith("Faction.") and load_json(p.read_bytes()).get("FactionSpec", {}).get("Id") == FACTION), None)
    if faction is None:
        print("no FactionSpec for", FACTION); return 1
    wanted = set(faction["FactionSpec"]["TemplateCollectionIds"])
    collections: dict[str, list[str]] = {}
    for src in (ours, vanilla):
        for key in src:
            if "templatecollection." not in key:
                continue
            d = blueprint(key)
            spec = d.get("TemplateCollectionSpec") if d else None
            if spec:
                # every vanilla *.Common file carries CollectionId "Common": one shared pool
                collections.setdefault(spec["CollectionId"], []).extend(spec["Blueprints"])
    active = [c for c in collections if c in wanted or c == "Common"]
    missing_cols = wanted - set(collections)
    for c in sorted(missing_cols):
        problems.append(f"template collection not found: {c}")

    templates: dict[str, dict] = {}
    aliases: set[str] = set()
    for c in active:
        for path in collections[c]:
            d = blueprint(path)
            if d is None:
                problems.append(f"{c}: blueprint missing: {path}")
                continue
            t = d.get("TemplateSpec", {})
            name = t.get("TemplateName")
            if not name:
                continue   # model-part blueprints (ModularShaftParts) carry no TemplateSpec
            if name in templates:
                problems.append(f"duplicate template name {name} ({c})")
            templates[name] = d
            aliases.update(t.get("BackwardCompatibleTemplateNames", []))

    # planters: exactly one PlanterBuildingSpec per plantable ResourceGroup the faction can see
    groups = {d["PlantableSpec"]["ResourceGroup"] for d in templates.values() if "PlantableSpec" in d}
    planters: dict[str, list[str]] = {}
    for name, d in templates.items():
        if "PlanterBuildingSpec" in d:
            planters.setdefault(d["PlanterBuildingSpec"]["PlantableResourceGroup"], []).append(name)
    for g in sorted(groups):
        n = planters.get(g, [])
        if len(n) != 1:
            problems.append(f"planter group {g}: need exactly one PlanterBuildingSpec, have {n}")

    # goods
    goods: set[str] = set()
    for src in (ours, vanilla):
        for key in src:
            if "/good." in key or key.startswith("goods/good."):
                d = blueprint(key)
                if d and "GoodSpec" in d:
                    goods.add(d["GoodSpec"]["Id"])

    # loc keys
    loc: set[str] = set()
    with zipfile.ZipFile(GAME / "Localizations.zip") as zl:
        for n in zl.namelist():
            if n.endswith("enUS.csv"):
                for row in csv.reader(zl.read(n).decode("utf-8-sig").splitlines()):
                    if row:
                        loc.add(row[0])
    for p in (mod / "Localizations").glob("enUS*.csv"):
        for row in csv.reader(open(p, encoding="utf-8-sig", newline="")):
            if row:
                loc.add(row[0])

    def check_loc(key: str, where: str) -> None:
        if key and key not in loc:
            problems.append(f"{where}: loc key missing: {key}")

    def walk_loc(obj, where: str) -> None:
        if isinstance(obj, dict):
            for k, v in obj.items():
                if k.endswith("LocKey") and isinstance(v, str):
                    check_loc(v, where)
                else:
                    walk_loc(v, where)
        elif isinstance(obj, list):
            for v in obj:
                walk_loc(v, where)

    for key, p in ours.items():
        if "/scripts/" in key or key.startswith("scripts/"):
            continue
        walk_loc(load_json(p.read_bytes()), p.name)
    for name, d in templates.items():
        if "LabeledEntitySpec" in d:
            walk_loc(d["LabeledEntitySpec"], name)

    # illumination colors
    colors = {load_json(p.read_bytes())["IlluminationColorSpec"]["Id"] for p in ours.values() if "IlluminationColorSpec" in load_json(p.read_bytes())}
    for key in vanilla:
        if "illuminationcolor." in key:
            colors.add(load_json(zbp.read(vanilla[key]))["IlluminationColorSpec"]["Id"])
    for name, d in templates.items():
        c = d.get("DefaultIlluminatorColorSpec", {}).get("ColorId")
        if c and c not in colors:
            problems.append(f"{name}: illumination color missing: {c}")

    # tutorials
    tutorials: dict[str, dict] = {}
    stages: dict[str, dict] = {}
    for key, p in ours.items():
        d = load_json(p.read_bytes())
        if "TutorialSpec" in d:
            tutorials[d["TutorialSpec"]["Id"]] = d
        if "TutorialStageSpec" in d:
            stages[d["TutorialStageSpec"]["Id"]] = d
    for tid, d in tutorials.items():
        t = d["TutorialSpec"]
        for req in t.get("RequiredTutorialIds", []):
            if req not in tutorials and req not in TRIGGER_IDS:
                problems.append(f"tutorial {tid}: required id unknown: {req}")
        skip = t.get("SkipIfTutorialFinished")
        if skip and skip not in tutorials:
            problems.append(f"tutorial {tid}: SkipIfTutorialFinished unknown: {skip}")
        for sid in t.get("Stages", []):
            if sid not in stages:
                problems.append(f"tutorial {tid}: stage missing: {sid}")
    template_names = set(templates) | aliases
    for sid, d in stages.items():
        for step_key, step in d.get("Children", {}).items():
            for spec_name, spec in step.items():
                fields = STEP_FIELDS.get(spec_name, {})
                where = f"stage {sid}/{step_key} {spec_name}"
                for f in [fields.get("templates")] if isinstance(fields.get("templates"), str) else fields.get("templates", []):
                    vals = spec.get(f)
                    for v in (vals if isinstance(vals, list) else [vals]):
                        if v and v not in template_names:
                            problems.append(f"{where}: template unknown: {v}")
                g = fields.get("goods")
                if g and spec.get(g) not in goods:
                    problems.append(f"{where}: good unknown: {spec.get(g)}")
                pl = fields.get("plantables")
                if pl:
                    v = spec.get(pl)
                    if v not in templates or "PlantableSpec" not in templates[v]:
                        problems.append(f"{where}: plantable unknown: {v}")
                lk = fields.get("loc")
                if lk:
                    check_loc(spec.get(lk, ""), where)

    print(f"mod: {mod}")
    print(f"collections: {', '.join(active)}")
    print(f"templates: {len(templates)} (+{len(aliases)} aliases), goods: {len(goods)}, planters: "
          + ", ".join(f"{g}: {len(planters.get(g, []))}" for g in sorted(groups)))
    print(f"tutorials: {len(tutorials)}, stages: {len(stages)}, loc keys: {len(loc)}")
    if problems:
        print("problems:")
        for p in problems:
            print("  -", p)
        return 1
    print("problems: none")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
