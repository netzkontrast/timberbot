"""Tests for check_cutscenes.py: the shipped scenes pass, and every rule has a failing case.

    uv run --project python --extra dev pytest wardens/tools/test_check_cutscenes.py
"""
from __future__ import annotations

import copy
import json
from pathlib import Path

import pytest

import check_cutscenes as cc

SRC = Path(__file__).resolve().parents[1] / "src"

CHAPTERS_CS = '''
public static readonly WardensChapter[] Chapters =
{
    new WardensChapter("Badwater", "Wardens.Scrap",
        new[] { "SludgePump.Wardens", "ReedBed.Wardens" }),   // comment with "quotes"
    new WardensChapter("Signal", "Wardens.WorkingHours", new[] { "Cruncher.Wardens" }),
};
'''

GOOD = {
    "id": "Scene",
    "on": ["new_game", "chapter:Badwater", "tutorial:Wardens.Scrap"],
    "pause": True,
    "leave_paused": False,
    "restore_camera": True,
    "letterbox": True,
    "skippable": True,
    "say": "A line.",
    "shots": [
        {
            "id": "one",
            "caption": "Wardens.Cutscene.Scene.One",
            "seconds": 4,
            "wait": "continue",
            "camera": [
                {"t": 0, "anchor": "core", "v": 60, "dzoom": 0.1},
                {"t": 4, "anchor": "grid", "x": 10, "y": 12, "z": 3, "offset": [1, -1], "dh": 90},
                {"t": 4, "anchor": "world", "x": 1.5, "y": 2.5, "z": 3.5, "offset": [0, 0, 1]},
            ],
            "point": {"anchor": "grid", "x": 10, "y": 12, "message": "here", "seconds": 5, "color": "#00E5FF"},
            "toast": "toast",
            "say": "line",
        },
        {"text": "a literal caption, no camera"},
    ],
}


@pytest.fixture
def mod(tmp_path: Path) -> Path:
    (tmp_path / "Cutscenes").mkdir()
    (tmp_path / "Localizations").mkdir()
    (tmp_path / "Localizations" / "enUS.csv").write_text(
        "ID,Text,Comment\nWardens.Cutscene.Scene.One,\"One, with a comma.\",caption\n", encoding="utf-8")
    (tmp_path / "Tutorials").mkdir()
    (tmp_path / "Tutorials" / "Tutorials.Wardens.Scrap.blueprint.json").write_text(
        json.dumps({"TutorialSpec": {"Id": "Wardens.Scrap", "Stages": []}}), encoding="utf-8")
    (tmp_path / "WardensChapters.cs").write_text(CHAPTERS_CS, encoding="utf-8")
    return tmp_path


def write(mod: Path, name: str, scene) -> None:
    (mod / "Cutscenes" / f"{name}.json").write_text(json.dumps(scene), encoding="utf-8")


def run(mod: Path) -> list[str]:
    return cc.check(mod, chapters_cs=mod / "WardensChapters.cs")


def test_read_chapters_parses_the_table(mod: Path) -> None:
    assert cc.read_chapters(mod / "WardensChapters.cs") == [
        ("Badwater", "Wardens.Scrap", ["SludgePump.Wardens", "ReedBed.Wardens"]),
        ("Signal", "Wardens.WorkingHours", ["Cruncher.Wardens"]),
    ]


def test_name_tables_are_read_from_the_mod(mod: Path) -> None:
    assert cc.read_loc_keys(mod) == {"ID", "Wardens.Cutscene.Scene.One"}
    assert cc.read_tutorial_ids(mod) == {"Wardens.Scrap"}


def test_good_scene_has_no_problems(mod: Path) -> None:
    write(mod, "Scene", GOOD)
    assert run(mod) == []


def test_shipped_scenes_pass_against_the_source_tree() -> None:
    assert (SRC / "Cutscenes" / "ColdBoot.json").exists()
    assert cc.check(SRC) == []


def test_missing_folder(tmp_path: Path) -> None:
    assert run(tmp_path) == ["Cutscenes/: folder missing in " + str(tmp_path)]


def test_invalid_json_is_reported(mod: Path) -> None:
    (mod / "Cutscenes" / "Broken.json").write_text("{ not json", encoding="utf-8")
    problems = run(mod)
    assert len(problems) == 1 and problems[0].startswith("Broken.json:")


def test_id_must_match_the_file_stem_and_be_unique(mod: Path) -> None:
    write(mod, "Scene", GOOD)
    write(mod, "Other", GOOD)
    problems = run(mod)
    assert "Other.json: id 'Scene' is not the file stem 'Other'" in problems
    assert "Scene.json: duplicate id 'Scene' (Other.json)" in problems


def test_id_is_required(mod: Path) -> None:
    scene = copy.deepcopy(GOOD)
    del scene["id"]
    write(mod, "Scene", scene)
    assert "Scene.json: id: required" in run(mod)


@pytest.mark.parametrize("trigger, expected", [
    ("chapter:Nope", "chapter unknown: 'Nope'"),
    ("tutorial:Wardens.Nope", "tutorial unknown: 'Wardens.Nope'"),
    ("level:01", "'level:01' (new_game | chapter:<Id> | tutorial:<Id>)"),
    ("chapter:", "'chapter:' (new_game | chapter:<Id> | tutorial:<Id>)"),
])
def test_unknown_triggers(mod: Path, trigger: str, expected: str) -> None:
    scene = copy.deepcopy(GOOD)
    scene["on"] = [trigger]
    write(mod, "Scene", scene)
    problems = run(mod)
    assert len(problems) == 1 and expected in problems[0], problems


def test_scene_field_types(mod: Path) -> None:
    scene = copy.deepcopy(GOOD)
    scene["pause"] = "yes"
    scene["say"] = 3
    scene["on"] = "new_game"
    scene["extra"] = 1
    write(mod, "Scene", scene)
    problems = run(mod)
    assert "Scene.json: pause: not a boolean" in problems
    assert "Scene.json: say: not a string" in problems
    assert "Scene.json: on: not an array" in problems
    assert "Scene.json: extra: unknown field" in problems


def test_at_least_one_shot(mod: Path) -> None:
    scene = copy.deepcopy(GOOD)
    scene["shots"] = []
    write(mod, "Scene", scene)
    assert run(mod) == ["Scene.json: shots: at least one shot required"]


def test_shot_rules(mod: Path) -> None:
    scene = copy.deepcopy(GOOD)
    scene["shots"] = [{
        "captoin": "Wardens.Cutscene.Scene.One",     # a typo the parser would silently ignore
        "caption": "Wardens.Cutscene.Scene.Missing",
        "seconds": -1,
        "wait": "click",
        "camera": {"t": 0},
        "toast": 5,
    }]
    write(mod, "Scene", scene)
    problems = run(mod)
    assert "Scene.json: shots[0].captoin: unknown field" in problems
    assert "Scene.json: shots[0].caption: loc key missing: Wardens.Cutscene.Scene.Missing" in problems
    assert "Scene.json: shots[0].seconds: not a number >= 0" in problems
    assert "Scene.json: shots[0].wait: 'click' (time | continue)" in problems
    assert "Scene.json: shots[0].camera: not an array" in problems
    assert "Scene.json: shots[0].toast: not a string" in problems


def test_keyframe_rules(mod: Path) -> None:
    scene = copy.deepcopy(GOOD)
    scene["shots"] = [{"camera": [
        {"t": "soon"},
        {"anchor": "moon"},
        {"anchor": "grid", "x": 1},
        {"anchor": "world", "x": 1, "y": "2"},
        {"offset": [1]},
        {"dh": True},
        {"zoomm": 1},
        "not an object",
    ]}]
    write(mod, "Scene", scene)
    problems = run(mod)
    assert "Scene.json: shots[0].camera[0].t: not a number >= 0" in problems
    assert any(p.startswith("Scene.json: shots[0].camera[1].anchor: 'moon'") for p in problems)
    assert "Scene.json: shots[0].camera[2]: anchor grid needs x and y" in problems
    assert "Scene.json: shots[0].camera[3].y: not a number" in problems
    assert "Scene.json: shots[0].camera[4].offset: [dx, dy] or [dx, dy, dz]" in problems
    assert "Scene.json: shots[0].camera[5].dh: not a number" in problems
    assert "Scene.json: shots[0].camera[6].zoomm: unknown field" in problems
    assert "Scene.json: shots[0].camera[7]: not an object" in problems


def test_pointer_rules(mod: Path) -> None:
    scene = copy.deepcopy(GOOD)
    scene["shots"] = [
        {"point": {"anchor": "bot"}},
        {"point": {"anchor": "grid", "y": 2}},
        {"point": {"anchor": "core", "seconds": "long", "message": 1, "offset": [1, 2, 3, 4], "colour": "#fff"}},
        {"point": "core"},
    ]
    write(mod, "Scene", scene)
    problems = run(mod)
    assert any(p.startswith("Scene.json: shots[0].point.anchor: 'bot'") for p in problems)
    assert "Scene.json: shots[1].point: anchor grid needs x and y" in problems
    assert "Scene.json: shots[2].point.seconds: not a number" in problems
    assert "Scene.json: shots[2].point.message: not a string" in problems
    assert "Scene.json: shots[2].point.offset: [dx, dy] or [dx, dy, dz]" in problems
    assert "Scene.json: shots[2].point.colour: unknown field" in problems
    assert "Scene.json: shots[3].point: not an object" in problems


def test_booleans_are_not_numbers(mod: Path) -> None:
    scene = copy.deepcopy(GOOD)
    scene["shots"] = [{"seconds": True, "camera": [{"t": False}]}]
    write(mod, "Scene", scene)
    problems = run(mod)
    assert "Scene.json: shots[0].seconds: not a number >= 0" in problems
    assert "Scene.json: shots[0].camera[0].t: not a number >= 0" in problems
