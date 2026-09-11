"""Static checks on the deployed (or source) Wardens mod: everything the game resolves by name at load.

    python wardens/tools/validate.py                 # deployed copy in Documents/Timberborn/Mods/Wardens
    python wardens/tools/validate.py wardens/src     # the source tree

Every crash so far came from a name the game looks up while loading: a template in a tutorial step,
a loc key, a stage id, a planter group with two buildings. This script resolves the same names
against the faction's template collections (ours + the vanilla Blueprints.zip) and the loc tables,
and prints what would throw. Exit code 1 when there are problems.

Also cross-checks the chapter table in src/WardensChapters.cs against the blueprints: every
building a chapter is about must exist, the opening tutorial must exist, each chapter needs its
Title/Unlocked loc rows, and no building in the faction's own collections may carry a science cost:
the whole bar is open from the first frame of every level (the chapters are story beats, not gates).

And the campaign table in src/WardensCampaign.cs against the shipped maps: every level marked shipped
has its .timber in Maps/, every .timber is claimed by a level, `next` points at a level that exists,
and a shipped level names an ending tutorial that exists (without one it can never complete). The
table is keyed by map name, so a rename in gen_map.py that misses it strands the level silently.

The cutscene files (Cutscenes/*.json) are checked by check_cutscenes.py, which this script calls (it
also runs alone, without the game's files).
"""
from __future__ import annotations

import csv
import json
import re
import sys
import zipfile
from pathlib import Path

import bot_workforce
from check_cutscenes import check as check_cutscenes, read_chapters, read_levels
from check_level_tasks import check_level_tasks, levels_with_tasks

GAME = Path("F:/Steam/steamapps/common/Timberborn/Timberborn_Data/StreamingAssets/Modding")
DEFAULT_MOD = Path.home() / "Documents/Timberborn/Mods/Wardens"
FACTION = "Wardens"
CHAPTERS_CS = Path(__file__).resolve().parents[1] / "src" / "WardensChapters.cs"
CAMPAIGN_CS = Path(__file__).resolve().parents[1] / "src" / "WardensCampaign.cs"
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


def chapter_problems(chapters: list[tuple[str, str, list[str]]], templates: set[str], tutorials: set[str],
                     loc: set[str]) -> list[str]:
    """The chapter table (src/WardensChapters.cs) against the names it uses: the opening tutorial (or
    trigger) exists, the Title/Unlocked loc rows exist, every building a chapter is about exists, and
    no building is listed by two chapters."""
    out: list[str] = []
    listed: dict[str, str] = {}
    for cid, tutorial, names in chapters:
        if tutorial not in tutorials:
            out.append(f"chapter {cid}: opening tutorial unknown: {tutorial}")
        for key in (f"Wardens.Chapter.{cid}.Title", f"Wardens.Chapter.{cid}.Unlocked"):
            if key not in loc:
                out.append(f"chapter {cid}: loc key missing: {key}")
        for n in names:
            if n in listed:
                out.append(f"chapter {cid}: {n} already listed by chapter {listed[n]}")
            listed[n] = cid
            if n not in templates:
                out.append(f"chapter {cid}: template unknown: {n}")
    return out


def free_bar_problems(templates: dict[str, dict], origin: dict[str, str], collections: set[str]) -> list[str]:
    """Every building in the faction's own collections ships with ScienceCost 0: the bar is open from
    the first frame of every level (src/WardensChapters.cs). A padlock or a science price here is a
    generator that forgot (tools/gen_buildings.py) or a collection wired in without the rule."""
    out: list[str] = []
    for name in sorted(templates):
        if origin.get(name) not in collections:
            continue
        spec = templates[name].get("BuildingSpec")
        if not isinstance(spec, dict):
            continue
        cost = spec.get("ScienceCost", 0)
        if cost:
            out.append(f"{name}: ScienceCost {cost} ({origin[name]}); every Wardens building is available "
                       "from the start, so it must be 0")
    return out


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
    origin: dict[str, str] = {}     # template name -> the collection that listed it
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
            origin[name] = c
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

    # chapters (src/WardensChapters.cs): story beats over the tutorial line, and a bar with nothing
    # locked on it (every building the faction lists at ScienceCost 0)
    chapters = read_chapters(CHAPTERS_CS) if CHAPTERS_CS.exists() else []
    if not chapters:
        problems.append(f"no chapters parsed from {CHAPTERS_CS}")
    problems += chapter_problems(chapters, set(templates), set(tutorials) | TRIGGER_IDS, loc)
    priced = free_bar_problems(templates, origin, wanted)
    problems += priced
    bar = sum(1 for n, d in templates.items() if origin.get(n) in wanted and isinstance(d.get("BuildingSpec"), dict))

    # the campaign (src/WardensCampaign.cs) vs. the shipped maps and the tutorial line.
    # A level is a map plus the chapter line on it, and the table is keyed by map name, so a
    # rename in gen_map.py that misses the table strands the level silently: the game loads the
    # map, WardensCampaignService finds no row and goes quiet. Same for a level whose ending
    # tutorial does not exist — it can never complete.
    levels = read_levels(CAMPAIGN_CS) if CAMPAIGN_CS.exists() else []
    if not levels:
        problems.append(f"no levels parsed from {CAMPAIGN_CS}")
    maps = {p.stem for p in (mod / "Maps").glob("*.timber")} if (mod / "Maps").is_dir() else set()
    with_tasks = levels_with_tasks(mod)
    shipped_ids = {lid for lid, _, _, _, _, s in levels if s}
    ids: set[str] = set()
    for lid, map_name, title, ends, nxt, shipped in levels:
        if lid in ids:
            problems.append(f"level {lid}: duplicate id")
        ids.add(lid)
        if not map_name.startswith("Wardens "):
            problems.append(f"level {lid}: map name {map_name!r} does not start with 'Wardens ' (the campaign prefix)")
        if shipped and map_name not in maps:
            problems.append(f"level {lid}: marked shipped but {map_name}.timber is not in {mod / 'Maps'}")
        if not shipped and map_name in maps:
            problems.append(f"level {lid}: {map_name}.timber exists but the table says shipped=false")
        if ends and ends not in tutorials:
            problems.append(f"level {lid}: ending tutorial unknown: {ends}")
        # A level ends by its tasks (Levels/<id>.tasks.json, any tutorial setting) or its ending
        # tutorial. The last playable level may have neither: there is nothing to continue to.
        if shipped and not ends and lid not in with_tasks and nxt in shipped_ids:
            problems.append(f"level {lid}: shipped without tasks or an ending tutorial, so it can never "
                            f"complete and level {nxt} can never be reached")
    for lid, _, _, _, nxt, _ in levels:
        if nxt and nxt not in ids:
            problems.append(f"level {lid}: next level {nxt!r} is not in the table")
    for name in sorted(maps - {m for _, m, _, _, _, _ in levels}):
        problems.append(f"{name}.timber ships but no level in WardensCampaign.cs claims it")

    # level tasks (Levels/<id>.tasks.json): how a level ends with the tutorial off; check_level_tasks.py
    goods = set()
    for src in (ours, vanilla):
        for key in src:
            if "/good." in "/" + key.rsplit("/", 1)[-1] or key.rsplit("/", 1)[-1].startswith("good."):
                d = blueprint(key)
                if d and "GoodSpec" in d:
                    goods.add(d["GoodSpec"].get("Id"))
    problems += check_level_tasks(mod, templates, goods, loc, ids)

    # cutscenes (Cutscenes/*.json): captions, triggers, anchors; see check_cutscenes.py
    cutscene_files = sorted((mod / "Cutscenes").glob("*.json")) if (mod / "Cutscenes").is_dir() else []
    problems += check_cutscenes(mod, loc=loc, tutorials=set(tutorials), chapters={cid for cid, _, _ in chapters},
                                chapters_cs=CHAPTERS_CS)

    # the workforce rule (bot_workforce.py): bots by default, no bot science, bot-default districts.
    # Every Wardens blueprint on disk, wired or not: a ported building must be right the day it is wired in.
    for key, p in sorted(ours.items()):
        for v in bot_workforce.violations(load_json(p.read_bytes())):
            problems.append(f"{p.relative_to(mod).as_posix()}: {v} (run tools/bot_workforce.py)")

    print(f"mod: {mod}")
    print(f"collections: {', '.join(active)}")
    print(f"chapters: {len(chapters)}, buildings on the bar: {bar}, science-priced: {len(priced)} (must be 0)")
    print(f"campaign levels: {len(levels)} ({sum(1 for l in levels if l[5])} with a map), maps: {len(maps)}")
    print(f"templates: {len(templates)} (+{len(aliases)} aliases), goods: {len(goods)}, planters: "
          + ", ".join(f"{g}: {len(planters.get(g, []))}" for g in sorted(groups)))
    print(f"tutorials: {len(tutorials)}, stages: {len(stages)}, loc keys: {len(loc)}, cutscenes: {len(cutscene_files)}")
    if problems:
        print("problems:")
        for p in problems:
            print("  -", p)
        return 1
    print("problems: none")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
