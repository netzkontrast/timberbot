"""Tests for bot_workforce.py: bots by default, no bot science, bot-default districts."""
from __future__ import annotations

import json

import bot_workforce as bw


def workplace(name: str, default: str = "Beaver", costs: list | None = None, only: bool = False, science: int = 0) -> dict:
    return {
        "TemplateSpec": {"TemplateName": name},
        "BuildingSpec": {"ScienceCost": science},
        "WorkplaceSpec": {"DefaultWorkerType": default, "DisallowOtherWorkerTypes": only,
                          "WorkerTypeUnlockCosts": costs if costs is not None else [{"WorkerType": "Bot", "ScienceCost": 250}]},
    }


def test_a_beaver_workplace_becomes_a_free_bot_workplace():
    d = workplace("Mine.Wardens")
    assert bw.violations(d)
    assert bw.botify(d)
    assert d["WorkplaceSpec"]["DefaultWorkerType"] == "Bot"
    assert d["WorkplaceSpec"]["WorkerTypeUnlockCosts"] == []
    assert bw.violations(d) == []
    assert not bw.botify(d)   # idempotent


def test_a_workplace_locked_to_its_own_worker_type_is_left_alone():
    d = workplace("PowerTreadmill.Wardens", only=True, costs=[])
    assert bw.violations(d) == []
    assert not bw.botify(d)
    assert d["WorkplaceSpec"]["DefaultWorkerType"] == "Beaver"


def test_bot_buildings_cost_no_science_but_a_chapter_padlock_stays():
    free = workplace("BotAssembler.Wardens", science=10000)
    bw.botify(free)
    assert free["BuildingSpec"]["ScienceCost"] == 0
    locked = workplace("BotAssembler.Wardens", science=bw.CHAPTER_LOCK)
    bw.botify(locked)
    assert locked["BuildingSpec"]["ScienceCost"] == bw.CHAPTER_LOCK


def test_other_buildings_keep_their_science_cost():
    d = workplace("Refinery.Wardens", science=2500)
    bw.botify(d)
    assert d["BuildingSpec"]["ScienceCost"] == 2500


def test_a_district_center_gets_the_bot_default_spec():
    d = workplace("Core.Wardens", costs=[])
    d["DistrictCenterSpec"] = {}
    bw.botify(d)
    assert bw.SPEC in d
    assert bw.violations(d) == []


def test_blueprints_that_are_not_ours_are_never_touched():
    d = workplace("Mine.IronTeeth")
    before = json.dumps(d)
    assert bw.violations(d) == []
    assert not bw.botify(d)
    assert json.dumps(d) == before


def test_apply_tree_rewrites_only_what_breaks_the_rule(tmp_path):
    bad = tmp_path / "Mine.Wardens.blueprint.json"
    good = tmp_path / "Pump.Wardens.blueprint.json"
    bad.write_text(json.dumps(workplace("Mine.Wardens")), encoding="utf-8")
    good.write_text(json.dumps(workplace("Pump.Wardens", default="Bot", costs=[])), encoding="utf-8")
    good_before = good.read_bytes()
    assert bw.apply_tree(tmp_path) == [bad]
    assert json.loads(bad.read_text(encoding="utf-8"))["WorkplaceSpec"]["DefaultWorkerType"] == "Bot"
    assert good.read_bytes() == good_before


def test_the_shipped_source_follows_the_rule():
    bad = []
    for p in bw.SRC.rglob("*.blueprint.json"):
        d = json.loads(p.read_text(encoding="utf-8-sig"))
        bad += [f"{p.name}: {v}" for v in bw.violations(d)] if isinstance(d, dict) else []
    assert bad == []
