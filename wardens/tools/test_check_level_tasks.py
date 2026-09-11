"""Tests for check_level_tasks.py: every way a task file can leave a level that never ends.

    uv run --project python --extra dev pytest wardens/tools/test_check_level_tasks.py
"""
from __future__ import annotations

import csv
import json
from pathlib import Path

import pytest
from check_level_tasks import PANEL_KEYS, check_level_tasks, levels_with_tasks

SRC = Path(__file__).resolve().parents[1] / "src"
TEMPLATES = {
    "ChargingPost.Wardens": {"MechanicalNodeSpec": {"PowerInput": 50, "PowerOutput": 0}},
    "BadwaterCell.Wardens": {"MechanicalNodeSpec": {"PowerInput": 0, "PowerOutput": 100}},
    "HaulingPost.Wardens": {"WorkplaceSpec": {"MaxWorkers": 10}},
    "SludgeTank.Wardens": {},
}
GOODS = {"ScrapMetal", "Badwater"}
LOC = set(PANEL_KEYS) | {f"Wardens.Tasks.Check.{t}" for t in ("built", "powered", "generating", "workers", "stock",
                                                                "beavers", "built_in", "clean_water")} \
    | {"T.A", "T.A.Text", "T.B", "T.B.Text", "Wardens.Tasks.In", "W.Creek"}


def _write(tmp: Path, data: dict, name: str = "01") -> Path:
    folder = tmp / "Levels"
    folder.mkdir(exist_ok=True)
    (folder / f"{name}.tasks.json").write_text(json.dumps(data), encoding="utf-8")
    return tmp


def _good() -> dict:
    return {"level": "01", "tasks": [
        {"id": "A", "title": "T.A", "text": "T.A.Text", "checks": [
            {"type": "powered", "template": "ChargingPost.Wardens", "count": 2},
            {"type": "stock", "good": "ScrapMetal", "count": 30}]},
        {"id": "B", "title": "T.B", "text": "T.B.Text", "checks": [
            {"type": "generating", "template": "BadwaterCell.Wardens"},
            {"type": "workers", "template": "HaulingPost.Wardens", "count": 2},
            {"type": "beavers", "count": 1}]},
    ]}


def _problems(tmp: Path, data: dict, loc=LOC) -> list[str]:
    return check_level_tasks(_write(tmp, data), TEMPLATES, GOODS, loc, {"01", "02"})


def test_a_good_file_passes(tmp_path):
    assert _problems(tmp_path, _good()) == []
    assert levels_with_tasks(tmp_path) == {"01"}


@pytest.mark.parametrize("change, expect", [
    (lambda d: d["tasks"][0]["checks"][0].update(type="finished"), "unknown check type"),
    (lambda d: d["tasks"][0]["checks"][0].update(template="ChargingPost.Folktails"), "unknown template"),
    (lambda d: d["tasks"][0]["checks"][1].update(good="Scrap"), "unknown good"),
    (lambda d: d["tasks"][1]["checks"][0].update(template="ChargingPost.Wardens"), "makes no power"),
    (lambda d: d["tasks"][1]["checks"][1].update(template="SludgeTank.Wardens"), "not a workplace"),
    (lambda d: d["tasks"][0]["checks"][0].update(count=0), "whole number"),
    (lambda d: d["tasks"][1].update(checks=[]), "could never be done"),
    (lambda d: d["tasks"][1].update(id="A"), "repeated"),
    (lambda d: d["tasks"][0].update(title="T.Missing"), "loc key missing"),
    (lambda d: d.update(level="02"), "file name says"),
    (lambda d: d.update(tasks=[]), "no tasks"),
])
def test_each_mistake_is_named(tmp_path, change, expect):
    data = _good()
    change(data)
    problems = _problems(tmp_path, data)
    assert any(expect in p for p in problems), problems


def _places() -> dict:
    data = _good()
    data["tasks"].append({"id": "C", "title": "T.A", "text": "T.A.Text", "checks": [
        {"type": "built_in", "templates": ["ChargingPost.Wardens", "SludgeTank.Wardens"], "box": [1, 2, 9, 9],
         "where": "W.Creek", "count": 2},
        {"type": "clean_water", "box": [0, 0, 5, 5], "where": "W.Creek", "count": 30}]})
    return data


def test_place_checks_pass_when_well_formed(tmp_path):
    assert _problems(tmp_path, _places()) == []


@pytest.mark.parametrize("change, expect", [
    (lambda c: c[0].pop("box"), "needs box"),
    (lambda c: c[0].update(box=[9, 2, 1, 9]), "x1 <= x2"),
    (lambda c: c[1].pop("where"), "needs `where`"),
    (lambda c: c[1].update(where="W.Nowhere"), "where loc key missing"),
    (lambda c: c[0].update(templates=["ChargingPost.Wardens", "Dam.Nope"]), "unknown template"),
])
def test_place_check_mistakes_are_named(tmp_path, change, expect):
    data = _places()
    change(data["tasks"][2]["checks"])
    problems = _problems(tmp_path, data)
    assert any(expect in p for p in problems), problems


def test_a_level_not_in_the_campaign_table_is_named(tmp_path):
    _write(tmp_path, dict(_good(), level="07"), name="07")
    problems = check_level_tasks(tmp_path, TEMPLATES, GOODS, LOC, {"01"})
    assert any("not in WardensCampaign.cs" in p for p in problems), problems


def test_the_panel_loc_keys_are_required(tmp_path):
    problems = _problems(tmp_path, _good(), loc=LOC - {"Wardens.Tasks.Continue", "Wardens.Tasks.Check.workers"})
    assert any("Wardens.Tasks.Continue" in p for p in problems)
    assert any("Wardens.Tasks.Check.workers" in p for p in problems)


def test_the_shipped_level_02_tasks_close_the_creek_before_the_gorge():
    data = json.loads((SRC / "Levels" / "02.tasks.json").read_text(encoding="utf-8"))
    ids = [t["id"] for t in data["tasks"]]
    assert len(ids) == 6
    assert ids.index("Creek") < ids.index("Gorge"), "damming the gorge first floods the creek with badwater"


def test_the_shipped_level_01_tasks_are_well_formed():
    """Structure and loc of the real file (templates and goods need the game: validate.py)."""
    loc = {r[0] for r in csv.reader(open(SRC / "Localizations" / "enUS.csv", encoding="utf-8")) if r}
    assert check_level_tasks(SRC, None, None, loc, {"01", "02"}) == []
    data = json.loads((SRC / "Levels" / "01.tasks.json").read_text(encoding="utf-8"))
    last = data["tasks"][-1]
    assert any(c["type"] == "beavers" for c in last["checks"]), "level 01 ends on its first beaver"
