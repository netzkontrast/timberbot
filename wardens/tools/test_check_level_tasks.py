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
LOC = set(PANEL_KEYS) | {f"Wardens.Tasks.Check.{t}" for t in ("built", "powered", "generating", "workers", "stock", "beavers")} \
    | {"T.A", "T.A.Text", "T.B", "T.B.Text"}


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


def test_a_level_not_in_the_campaign_table_is_named(tmp_path):
    _write(tmp_path, dict(_good(), level="07"), name="07")
    problems = check_level_tasks(tmp_path, TEMPLATES, GOODS, LOC, {"01"})
    assert any("not in WardensCampaign.cs" in p for p in problems), problems


def test_the_panel_loc_keys_are_required(tmp_path):
    problems = _problems(tmp_path, _good(), loc=LOC - {"Wardens.Tasks.Continue", "Wardens.Tasks.Check.workers"})
    assert any("Wardens.Tasks.Continue" in p for p in problems)
    assert any("Wardens.Tasks.Check.workers" in p for p in problems)


def test_the_shipped_level_01_tasks_are_well_formed():
    """Structure and loc of the real file (templates and goods need the game: validate.py)."""
    loc = {r[0] for r in csv.reader(open(SRC / "Localizations" / "enUS.csv", encoding="utf-8")) if r}
    assert check_level_tasks(SRC, None, None, loc, {"01", "02"}) == []
    data = json.loads((SRC / "Levels" / "01.tasks.json").read_text(encoding="utf-8"))
    last = data["tasks"][-1]
    assert any(c["type"] == "beavers" for c in last["checks"]), "level 01 ends on its first beaver"
