"""Tests for validate.py's offline rules: the chapter table and the free bar. The rest of validate.py
resolves names against the game's Blueprints.zip and runs on the game machine only; these rules
need nothing but the source tree, so they also guard the rule in CI.

    uv run --project python --extra dev pytest wardens/tools/test_validate.py
"""
from __future__ import annotations

import json
from pathlib import Path

import validate as v
from check_cutscenes import read_chapters, read_loc_keys, read_tutorial_ids

SRC = Path(__file__).resolve().parents[1] / "src"
LOC_OK = {"Wardens.Chapter.Badwater.Title", "Wardens.Chapter.Badwater.Unlocked"}


def test_chapter_problems_clean_table() -> None:
    chapters = [("Badwater", "Wardens.Scrap", ["SludgePump.Wardens"])]
    assert v.chapter_problems(chapters, {"SludgePump.Wardens"}, {"Wardens.Scrap"}, LOC_OK) == []


def test_chapter_problems_every_rule_has_a_failing_case() -> None:
    chapters = [("A", "Wardens.Nope", ["X.Wardens", "Y.Wardens"]), ("Badwater", "Wardens.Scrap", ["X.Wardens"])]
    out = v.chapter_problems(chapters, {"X.Wardens"}, {"Wardens.Scrap"}, LOC_OK)
    assert "chapter A: opening tutorial unknown: Wardens.Nope" in out
    assert "chapter A: loc key missing: Wardens.Chapter.A.Title" in out
    assert "chapter A: loc key missing: Wardens.Chapter.A.Unlocked" in out
    assert "chapter A: template unknown: Y.Wardens" in out
    assert "chapter Badwater: X.Wardens already listed by chapter A" in out
    assert len(out) == 5


def test_free_bar_flags_a_science_price_in_the_factions_own_collections() -> None:
    templates = {
        "Mine.Wardens": {"BuildingSpec": {"ScienceCost": 4000}},
        "Path": {"BuildingSpec": {"ScienceCost": 50}},        # Common: vanilla's pool, not ours
        "Bot.IronTeeth": {"BotSpec": {}},                      # no BuildingSpec
        "Core.Wardens": {"BuildingSpec": {"ScienceCost": 0}},
    }
    origin = {"Mine.Wardens": "Buildings.WardensPort", "Path": "Common",
              "Bot.IronTeeth": "Characters.IronTeeth", "Core.Wardens": "Buildings.Wardens"}
    out = v.free_bar_problems(templates, origin, {"Buildings.Wardens", "Buildings.WardensPort", "Characters.IronTeeth"})
    assert out == ["Mine.Wardens: ScienceCost 4000 (Buildings.WardensPort); every Wardens building is available "
                   "from the start, so it must be 0"]
    assert v.free_bar_problems(templates, origin, {"Buildings.Wardens"}) == []


# --- the shipped source tree, without the game's files ------------------------------------------------

def _bar() -> dict[str, dict]:
    """Template name -> blueprint for every entry of the faction's building collection whose file is
    ours (vanilla entries such as the Scavenger Flag live in Blueprints.zip and are the game's)."""
    coll = json.loads((SRC / "Collections/TemplateCollection.Buildings.Wardens.blueprint.json").read_text(encoding="utf-8-sig"))
    out: dict[str, dict] = {}
    for rel in coll["TemplateCollectionSpec"]["Blueprints"]:
        p = SRC / f"{rel}.json"
        if p.exists():
            d = json.loads(p.read_text(encoding="utf-8-sig"))
            out[d["TemplateSpec"]["TemplateName"]] = d
    return out


def test_shipped_bar_is_free_from_the_start() -> None:
    bar = _bar()
    assert "Core.Wardens" in bar and "Cruncher.Wardens" in bar
    assert v.free_bar_problems(bar, {n: "Buildings.Wardens" for n in bar}, {"Buildings.Wardens"}) == []


def test_shipped_chapter_table_resolves_against_the_source_tree() -> None:
    chapters = read_chapters()
    assert chapters, "no chapters parsed from WardensChapters.cs"
    assert v.chapter_problems(chapters, set(_bar()), read_tutorial_ids(SRC) | v.TRIGGER_IDS, read_loc_keys(SRC)) == []
