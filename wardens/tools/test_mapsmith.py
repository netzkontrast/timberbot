"""Tests for mapsmith: the ops do what they say, the writer matches the verified format, and the
checker fails the maps it is supposed to fail.

    uv run --project python --extra dev pytest wardens/tools/test_mapsmith.py

The checker tests matter most. There is no Timberborn in CI, so a check that quietly passes a
broken map is worse than no check at all: every one of them is written by breaking a good map in
one specific way and asserting the report names it.
"""
from __future__ import annotations

import copy
import json
import zipfile
from pathlib import Path

import pytest
from mapsmith import ascii_map, build, check_file, check_world, load, metadata_json, world_json, write_png, write_timber
from mapsmith.build import SpecError
from mapsmith.spec import deep_merge

REPO = Path(__file__).resolve().parents[2]
WASTELAND = REPO / "wardens" / "maps" / "wardens-wasteland.map.toml"
NAMES = {"world.json", "map_metadata.json", "version.txt", "map_thumbnail.jpg"}

MINIMAL = """
name = "Test Flats"
size = 32
seed = 5

[[terrain]]
op = "base"
height = 6

[[terrain]]
op = "pad"
at = [12, 12]
size = 8
height = 6
name = "start"
reserve = 2

[[terrain]]
op = "clamp"
min = 3
max = 19

[[place]]
template = "StartingLocation"
at = "start"
dx = -2
dy = -2
orientation = "Cw0"

[checks]
require = ["StartingLocation"]
"""


@pytest.fixture
def minimal(tmp_path: Path) -> dict:
    path = tmp_path / "minimal.map.toml"
    path.write_text(MINIMAL, encoding="utf-8")
    return load(path)


def report_for(spec: dict, b=None):
    b = b or build(spec)
    return check_world(world_json(b, spec), metadata_json(b, spec), set(NAMES), spec.get("checks", {}))


# --- the spec language ----------------------------------------------------------------------------

def test_minimal_spec_builds_and_passes(minimal):
    b = build(minimal)
    assert b.size == 32
    assert len(b.entities) == 1
    assert report_for(minimal, b).ok


def test_build_is_deterministic(minimal):
    a, b = build(minimal), build(minimal)
    assert a.height.cells == b.height.cells
    assert [e.to_json(a.seed) for e in a.entities] == [e.to_json(b.seed) for e in b.entities]


def test_seed_changes_the_land(minimal):
    noisy = deep_merge(minimal, {"terrain": [{"op": "base", "height": 6},
                                             {"op": "noise", "amplitude": 6.0},
                                             {"op": "pad", "at": [12, 12], "size": 8, "height": 6, "name": "start", "reserve": 2},
                                             {"op": "clamp", "min": 1, "max": 19}]})
    a = build(noisy)
    b = build(deep_merge(noisy, {"seed": 99}))
    assert a.height.cells != b.height.cells


def test_mismatched_octaves_and_weights_are_caught(minimal):
    """zip() would silently drop the extra octave; a spec that means three should get three."""
    spec = deep_merge(minimal, {"terrain": [{"op": "noise", "octaves": [8, 16, 32], "weights": [1.0, 0.5]}]})
    with pytest.raises(SpecError, match="3 octaves but 2 weights"):
        build(spec)


def test_unknown_op_is_a_spec_error(minimal):
    with pytest.raises(SpecError, match="unknown terrain op 'volcano'"):
        build(deep_merge(minimal, {"terrain": [{"op": "volcano"}]}))


def test_unknown_anchor_is_a_spec_error(minimal):
    with pytest.raises(SpecError, match="unknown anchor 'nowhere'"):
        build(deep_merge(minimal, {"place": [{"template": "StartingLocation", "at": "nowhere"}]}))


def test_scatter_that_cannot_be_satisfied_raises(minimal):
    spec = deep_merge(minimal, {"scatter": [{"name": "impossible", "template": "Pine",
                                             "around": [16, 16], "radius": 1.0, "count": 50,
                                             "spacing": 5, "attempts": 200}]})
    with pytest.raises(SpecError, match="placed .* of 50"):
        build(spec)


def test_variant_overrides_the_base(tmp_path: Path):
    path = tmp_path / "v.map.toml"
    path.write_text(MINIMAL + '\n[variants.small]\nsize = 24\nseed = 7\n', encoding="utf-8")
    assert load(path)["size"] == 32
    variant = load(path, "small")
    assert (variant["size"], variant["seed"], variant["variant"]) == (24, 7, "small")


def test_unknown_variant_names_the_known_ones(tmp_path: Path):
    path = tmp_path / "v.map.toml"
    path.write_text(MINIMAL + '\n[variants.small]\nsize = 24\n', encoding="utf-8")
    with pytest.raises(SpecError, match="small"):
        load(path, "winter")


# --- terrain ops ------------------------------------------------------------------------------------

def test_river_carves_a_bed_and_tags_it(minimal):
    spec = deep_merge(minimal, {"terrain": [
        {"op": "base", "height": 8},
        {"op": "river", "points": [[16, -2], [16, 34]], "bed": 4, "width": 1.5, "tag": "badwater"},
        {"op": "pad", "at": [4, 4], "size": 8, "height": 6, "name": "start", "reserve": 2},
        {"op": "clamp", "min": 1, "max": 19}]})
    b = build(spec)
    assert b.h(16, 16) == 4
    assert b.masks["badwater"].at(16, 16)
    assert not b.masks["badwater"].at(30, 16)


def test_pad_is_flat_and_reserved(minimal):
    b = build(minimal)
    levels = {b.h(x, y) for y in range(12, 20) for x in range(12, 20)}
    assert levels == {6}
    assert b.reserved.at(11, 11) and not b.reserved.at(5, 5)


def test_basin_and_crater_sink_the_ground(minimal):
    spec = deep_merge(minimal, {"terrain": [
        {"op": "base", "height": 10},
        {"op": "basin", "at": [8, 8], "radii": [3, 3], "bed": 5, "tag": "water"},
        {"op": "crater", "at": [24, 24], "radius": 2, "depth": 3, "tag": "clean"},
        {"op": "pad", "at": [12, 12], "size": 8, "height": 10, "name": "start", "reserve": 2},
        {"op": "clamp", "min": 1, "max": 19}]})
    b = build(spec)
    assert b.h(8, 8) == 5 and b.masks["water"].at(8, 8)
    assert b.h(24, 24) == 7 and b.masks["clean"].at(24, 24)


def test_scatter_keeps_out_of_water_and_reservations(minimal):
    spec = deep_merge(minimal, {
        "terrain": [{"op": "base", "height": 6},
                    {"op": "river", "points": [[16, -2], [16, 34]], "bed": 4, "width": 2.0, "tag": "badwater"},
                    {"op": "pad", "at": [4, 4], "size": 8, "height": 6, "name": "start", "reserve": 2},
                    {"op": "clamp", "min": 1, "max": 19}],
        "scatter": [{"name": "trees", "template": "Pine", "around": [16, 16], "radius": 14,
                     "count": 25, "spacing": 1, "away_from": {"badwater": 3.0}}]})
    b = build(spec)
    trees = [e for e in b.entities if e.template == "Pine"]
    assert len(trees) == 25
    assert not any(b.masks["badwater"].at(e.x, e.y) for e in trees)
    assert not any(b.reserved.at(e.x, e.y) for e in trees)


def test_away_from_accepts_an_anchor(minimal):
    spec = deep_merge(minimal, {"scatter": [{"template": "Pine", "around": [16, 16], "radius": 15,
                                             "count": 10, "away_from": {"start": 8.0}}]})
    b = build(spec)
    cx, cy = b.anchors["start"]
    assert all(((e.x - cx) ** 2 + (e.y - cy) ** 2) ** 0.5 >= 8.0 for e in b.entities if e.template == "Pine")


def test_away_from_an_unknown_name_raises(minimal):
    spec = deep_merge(minimal, {"scatter": [{"template": "Pine", "around": [16, 16], "radius": 8,
                                             "count": 1, "away_from": {"lagoon": 3.0}}]})
    with pytest.raises(SpecError, match="lagoon"):
        build(spec)


def test_buried_entities_sit_inside_the_terrain(minimal):
    spec = deep_merge(minimal, {"scatter": [{"template": "UndergroundRuins", "around": [16, 16],
                                             "radius": 10, "count": 3, "buried": 3, "margin": 2}]})
    b = build(spec)
    ruins = [e for e in b.entities if e.template == "UndergroundRuins"]
    assert len(ruins) == 3
    assert all(e.z == b.h(e.x, e.y) - 3 for e in ruins)
    assert report_for(spec, b).ok


def test_two_rivers_with_one_tag_must_be_named(minimal):
    """Silently placing sources on the wrong stream is exactly the bug a preview caught once."""
    terrain = [{"op": "base", "height": 8},
               {"op": "river", "points": [[8, -2], [8, 34]], "bed": 4, "tag": "clean", "name": "west"},
               {"op": "river", "points": [[24, -2], [24, 34]], "bed": 4, "tag": "clean", "name": "east"},
               {"op": "pad", "at": [14, 14], "size": 8, "height": 8, "name": "start", "reserve": 2},
               {"op": "clamp", "min": 1, "max": 19}]
    ambiguous = deep_merge(minimal, {"terrain": terrain, "place": [
        {"template": "StartingLocation", "at": "start", "dx": -2, "dy": -2, "orientation": "Cw0"},
        {"template": "WaterSource", "along": "clean", "segment": [0.0, 0.05], "count": 1, "strength": 1.0}]})
    with pytest.raises(SpecError, match="share that tag"):
        build(ambiguous)

    named = deep_merge(ambiguous, {"place": [
        {"template": "StartingLocation", "at": "start", "dx": -2, "dy": -2, "orientation": "Cw0"},
        {"template": "WaterSource", "along": "east", "segment": [0.1, 0.2], "count": 1, "strength": 1.0}]})
    b = build(named)
    source = next(e for e in b.entities if e.template == "WaterSource")
    assert abs(source.x - 24) <= 1, "the source must sit on the river it was told to follow, not the other one"


def test_along_a_segment_that_starts_off_the_map_says_so(minimal):
    spec = deep_merge(minimal, {
        "terrain": [{"op": "base", "height": 8},
                    {"op": "river", "points": [[16, -6], [16, 34]], "bed": 4, "tag": "clean", "name": "creek"},
                    {"op": "pad", "at": [4, 4], "size": 8, "height": 8, "name": "start", "reserve": 2},
                    {"op": "clamp", "min": 1, "max": 19}],
        "place": [{"template": "StartingLocation", "at": "start", "dx": -2, "dy": -2, "orientation": "Cw0"},
                  {"template": "WaterSource", "along": "creek", "segment": [0.0, 0.02], "count": 2}]})
    with pytest.raises(SpecError, match="outside the map"):
        build(spec)


def test_along_an_unknown_river_names_the_known_ones(minimal):
    spec = deep_merge(minimal, {"place": [{"template": "WaterSource", "along": "nile", "count": 1}]})
    with pytest.raises(SpecError, match="no watercourse"):
        build(spec)


# --- the file format --------------------------------------------------------------------------------

def test_written_file_has_the_four_members(minimal, tmp_path: Path):
    out = write_timber(build(minimal), minimal, tmp_path / "m.timber")
    with zipfile.ZipFile(out) as z:
        assert set(z.namelist()) == NAMES
        assert z.read("map_thumbnail.jpg")[:2] == b"\xff\xd8"
        assert z.read("version.txt").decode() == "0.7.10.0"
        world = json.loads(z.read("world.json"))
    voxels = world["Singletons"]["TerrainMap"]["Voxels"]["Array"].split()
    assert len(voxels) == 32 * 32 * 23
    assert all(v == "0" for v in voxels[22 * 32 * 32:]), "the top layer must be air"


def test_writing_twice_gives_the_same_bytes(minimal, tmp_path: Path):
    a = write_timber(build(minimal), minimal, tmp_path / "a.timber").read_bytes()
    b = write_timber(build(minimal), minimal, tmp_path / "b.timber").read_bytes()
    assert a == b


def test_entity_shapes_match_the_verified_map(minimal):
    """Plants carry no Orientation; every other block object does. See design/wardens-wasteland.md."""
    spec = deep_merge(minimal, {"scatter": [
        {"template": "Pine", "around": [16, 16], "radius": 8, "count": 2, "growth": [0.7, 1.0]},
        {"template": "RuinColumnH2", "around": [24, 24], "radius": 4, "count": 1, "levels": [2],
         "orientation": "Cw0", "variants": ["A"], "yield": {"good": "ScrapMetal", "amount_per_level": 15}}]})
    world = world_json(build(spec), spec)
    by_template = {e["Template"]: e["Components"] for e in world["Entities"]}
    assert "Orientation" not in by_template["Pine"]["BlockObject"]
    assert set(by_template["Pine"]) == {"BlockObject", "Growable"}
    ruin = by_template["RuinColumnH2"]
    assert ruin["BlockObject"]["Orientation"] == "Cw0"
    assert ruin["Yielder:Ruin"]["Yield"] == {"Good": {"Id": "ScrapMetal"}, "Amount": 30}
    assert ruin["RuinModels"]["VariantId"] == "A"


def test_soil_contamination_follows_the_water(minimal):
    spec = deep_merge(minimal, {
        "terrain": [{"op": "base", "height": 6},
                    {"op": "river", "points": [[16, -2], [16, 34]], "bed": 4, "width": 1.5, "tag": "badwater"},
                    {"op": "pad", "at": [2, 2], "size": 8, "height": 6, "name": "start"},
                    {"op": "clamp", "min": 1, "max": 19}],
        "soil": {"contamination": {"from": "badwater", "offset": 1.0, "reach": 5.0}}})
    b = build(spec)
    levels = world_json(b, spec)["Singletons"]["SoilContaminationSimulator"]["ContaminationLevels"]["Array"].split()
    assert float(levels[16 * 32 + 16]) == pytest.approx(1.0, abs=0.01)   # in the river
    assert float(levels[16 * 32 + 31]) == 0.0                            # far from it


# --- the checker: each test breaks one thing --------------------------------------------------------

def _good(minimal) -> tuple[dict, dict, dict]:
    b = build(minimal)
    return world_json(b, minimal), metadata_json(b, minimal), minimal.get("checks", {})


def test_checker_passes_a_good_map(minimal):
    world, meta, opts = _good(minimal)
    assert check_world(world, meta, set(NAMES), opts).ok


@pytest.mark.parametrize("missing", sorted(NAMES))
def test_checker_catches_a_missing_member(minimal, missing):
    world, meta, opts = _good(minimal)
    report = check_world(world, meta, NAMES - {missing}, opts)
    assert any(missing in str(p) for p in report.errors)


def test_checker_catches_a_short_voxel_array(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    array = world["Singletons"]["TerrainMap"]["Voxels"]["Array"].split()
    world["Singletons"]["TerrainMap"]["Voxels"]["Array"] = " ".join(array[:-10])
    assert any("Voxels" in str(p) for p in check_world(world, meta, set(NAMES), opts).errors)


def test_checker_catches_a_solid_top_layer(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    array = world["Singletons"]["TerrainMap"]["Voxels"]["Array"].split()
    array[-1] = "1"
    world["Singletons"]["TerrainMap"]["Voxels"]["Array"] = " ".join(array)
    assert any("top layer" in str(p) for p in check_world(world, meta, set(NAMES), opts).errors)


def test_checker_catches_a_floating_entity(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    world["Entities"][0]["Components"]["BlockObject"]["Coordinates"]["Z"] += 4
    assert any("floating" in str(p) for p in check_world(world, meta, set(NAMES), opts).errors)


def test_checker_catches_an_entity_inside_the_ground(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    world["Entities"].append({"Id": "x", "Template": "Pine",
                              "Components": {"BlockObject": {"Coordinates": {"X": 4, "Y": 4, "Z": 2}}}})
    problems = [str(p) for p in check_world(world, meta, set(NAMES), opts).errors]
    assert any("buried" in p for p in problems), problems


def test_checker_allows_the_templates_the_spec_says_may_be_buried(minimal):
    world, meta, _ = _good(minimal)
    world = copy.deepcopy(world)
    world["Entities"].append({"Id": "x", "Template": "OreVein",
                              "Components": {"BlockObject": {"Coordinates": {"X": 4, "Y": 4, "Z": 2}}}})
    opts = {"require": ["StartingLocation"], "buried_ok": ["UndergroundRuins", "OreVein"]}
    assert check_world(world, meta, set(NAMES), opts).ok


def test_checker_catches_a_missing_orientation(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    del world["Entities"][0]["Components"]["BlockObject"]["Orientation"]
    assert any("Orientation" in str(p) for p in check_world(world, meta, set(NAMES), opts).errors)


def test_checker_catches_a_pad_that_is_not_flat(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    array = world["Singletons"]["TerrainMap"]["Voxels"]["Array"].split()
    plane = 32 * 32
    array[7 * plane + 13 * 32 + 13] = "1"     # one raised block under the pad
    world["Singletons"]["TerrainMap"]["Voxels"]["Array"] = " ".join(array)
    problems = [str(p) for p in check_world(world, meta, set(NAMES), opts).errors]
    assert any("not flat" in p for p in problems), problems


def test_checker_catches_no_headroom(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    array = world["Singletons"]["TerrainMap"]["Voxels"]["Array"].split()
    plane = 32 * 32
    for z in (6, 7, 8):                        # rebuild the ground one level higher across the pad
        for y in range(12, 20):
            for x in range(12, 20):
                array[z * plane + y * 32 + x] = "1"
    world["Singletons"]["TerrainMap"]["Voxels"]["Array"] = " ".join(array)
    problems = [str(p) for p in check_world(world, meta, set(NAMES), opts).errors]
    assert any("buried" in p or "air above the pad" in p for p in problems), problems


def test_checker_catches_something_hanging_over_the_pad(minimal):
    """An overhang above the pad leaves the start with nowhere to put the Core."""
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    array = world["Singletons"]["TerrainMap"]["Voxels"]["Array"].split()
    plane = 32 * 32
    array[7 * plane + 18 * 32 + 18] = "1"     # a block floating one level above a pad tile
    world["Singletons"]["TerrainMap"]["Voxels"]["Array"] = " ".join(array)
    problems = [str(p) for p in check_world(world, meta, set(NAMES), opts).errors]
    assert any("air above the pad" in p for p in problems), problems


def test_checker_catches_a_missing_starting_location(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    world["Entities"] = []
    problems = [str(p) for p in check_world(world, meta, set(NAMES), opts).errors]
    assert any("StartingLocation" in p for p in problems)


def test_checker_catches_duplicate_ids(minimal):
    world, meta, opts = _good(minimal)
    world = copy.deepcopy(world)
    world["Entities"].append(copy.deepcopy(world["Entities"][0]))
    assert any("duplicate entity id" in str(p) for p in check_world(world, meta, set(NAMES), opts).errors)


def test_checker_catches_a_required_template(minimal):
    world, meta, _ = _good(minimal)
    problems = check_world(world, meta, set(NAMES), {"require": ["BadwaterSource"]}).errors
    assert any("BadwaterSource" in str(p) for p in problems)


def test_checker_catches_a_boxed_in_start(minimal):
    """A start ringed by cliffs loads fine and is unplayable — the reason the check exists."""
    world, meta, _ = _good(minimal)
    world = copy.deepcopy(world)
    array = world["Singletons"]["TerrainMap"]["Voxels"]["Array"].split()
    plane = 32 * 32
    for z in range(6, 12):
        for y in range(11, 21):
            for x in range(11, 21):
                if y in (11, 20) or x in (11, 20):
                    array[z * plane + y * 32 + x] = "1"
    world["Singletons"]["TerrainMap"]["Voxels"]["Array"] = " ".join(array)
    problems = [str(p) for p in check_world(world, meta, set(NAMES), {"min_reachable": 200}).errors]
    assert any("walkable" in p for p in problems), problems


def test_checker_catches_a_size_mismatch(minimal):
    world, meta, opts = _good(minimal)
    assert any("map_metadata size" in str(p) for p in
               check_world(world, dict(meta, Width=64), set(NAMES), opts).errors)


def test_check_file_reads_a_real_timber(minimal, tmp_path: Path):
    out = write_timber(build(minimal), minimal, tmp_path / "m.timber")
    assert check_file(out, minimal.get("checks", {})).ok
    assert not check_file(tmp_path / "nope.timber").ok


# --- previews ----------------------------------------------------------------------------------------

def test_ascii_map_is_readable(minimal):
    text = ascii_map(build(minimal))
    assert "32x32" in text and "legend:" in text
    body = [line for line in text.splitlines() if len(line) == 32]
    assert len(body) == 32
    assert "A" in text                     # the starting location is marked


def test_png_preview_is_a_png(minimal, tmp_path: Path):
    path = write_png(build(minimal), tmp_path / "p.png", minimal, scale=2)
    assert path.read_bytes()[:8] == b"\x89PNG\r\n\x1a\n"


# --- the shipped spec ---------------------------------------------------------------------------------

@pytest.mark.parametrize("spec_path", sorted((REPO / "wardens" / "maps").glob("*.map.toml")))
def test_every_shipped_spec_builds_and_passes_its_own_checks(spec_path):
    spec = load(spec_path)
    report = report_for(spec)
    assert report.ok, f"{spec_path.name}: {report.summary()}"


def test_wasteland_spec_builds_and_passes_its_own_checks():
    spec = load(WASTELAND)
    b = build(spec)
    report = report_for(spec, b)
    assert report.ok, report.summary()
    templates = {e.template for e in b.entities}
    assert {"StartingLocation", "BadwaterSource", "WaterSource", "UndergroundRuins"} <= templates
    assert sum(1 for e in b.entities if e.template.startswith("RuinColumn")) >= 30
