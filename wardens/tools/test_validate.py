"""Tests for validate.py's offline rules: the free bar. The rest of validate.py
resolves names against the game's Blueprints.zip and runs on the game machine only; these rules
need nothing but the source tree, so they also guard the rule in CI.

    uv run --project python --extra dev pytest wardens/tools/test_validate.py
"""
from __future__ import annotations

import json
from pathlib import Path

import validate as v

SRC = Path(__file__).resolve().parents[1] / "src"


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


def test_the_chapter_table_is_gone() -> None:
    """The chapters were a second progression beside the tasks, dead with the tutorial off; they are
    retired (iteration 05, WP1). Their loc rows go with them, and no scene hangs on one."""
    assert not (SRC / "WardensChapters.cs").exists()
    loc = (SRC / "Localizations" / "enUS.csv").read_text(encoding="utf-8")
    assert "Wardens.Chapter." not in loc
