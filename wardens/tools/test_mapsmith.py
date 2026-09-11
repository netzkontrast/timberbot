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
from mapsmith import (
    ascii_map,
    build,
    check_file,
    check_world,
    cli,
    load,
    metadata_json,
    world_json,
    write_png,
    write_timber,
)
from mapsmith import spec as spec_mod
from mapsmith.build import SpecError
from mapsmith.spec import deep_merge

REPO = Path(__file__).resolve().parents[2]
FIRST_LIGHT = REPO / "wardens" / "maps" / "wardens-01-first-light.map.toml"
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


def test_reachable_from_keeps_a_cluster_on_the_colonys_own_bank(minimal):
    """A river splits the map into components; `around` + `radius` is a disc that knows nothing
    about that. Without this key you hand-pick centres until the annulus cannot cross the water."""
    terrain = [{"op": "base", "height": 6},
               {"op": "river", "points": [[16, -2], [16, 34]], "bed": 1, "width": 3.0, "tag": "badwater"},
               {"op": "pad", "at": [4, 12], "size": 8, "height": 6, "name": "start", "reserve": 2},
               {"op": "clamp", "min": 1, "max": 19}]
    rule = {"name": "scrap", "template": "RuinColumnH1", "around": [16, 16], "radius": [6, 13],
            "count": 8, "spacing": 2, "height_range": [6, 6]}

    loose = build(deep_merge(minimal, {"terrain": terrain, "scatter": [rule]}))
    walk = loose.walkable_cached((6, 14))
    def beside(b, x, y):
        return any(walk.at(x + dx, y + dy) for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1))
                   if 0 <= x + dx < b.size and 0 <= y + dy < b.size)
    assert not all(beside(loose, e.x, e.y) for e in loose.entities if e.rule == "scrap"), \
        "the unconstrained cluster should straddle the river, or this test proves nothing"

    held = build(deep_merge(minimal, {"terrain": terrain,
                                      "scatter": [{**rule, "reachable_from": "start"}]}))
    scrap = [e for e in held.entities if e.rule == "scrap"]
    assert len(scrap) == 8
    assert all(beside(held, e.x, e.y) for e in scrap)


def test_reachable_from_a_point_in_the_water_is_a_spec_error(minimal):
    spec = deep_merge(minimal, {
        "terrain": [{"op": "base", "height": 6},
                    {"op": "river", "points": [[16, -2], [16, 34]], "bed": 3, "width": 2.0, "tag": "badwater"},
                    {"op": "pad", "at": [4, 4], "size": 8, "height": 6, "name": "start", "reserve": 2},
                    {"op": "clamp", "min": 1, "max": 19}],
        "scatter": [{"template": "Pine", "around": [8, 20], "radius": 5, "count": 2,
                     "reachable_from": [16, 16]}]})
    with pytest.raises(SpecError, match="stands on water"):
        build(spec)


# --- named groups, walking distance, and the CLI ---

def test_entities_remember_which_rule_placed_them(minimal):
    spec = deep_merge(minimal, {"scatter": [{"name": "grove", "template": "Pine", "around": [16, 16],
                                             "radius": 8, "count": 4}]})
    b = build(spec)
    assert {e.rule for e in b.entities if e.template == "Pine"} == {"grove"}
    assert spec_mod.groups_of(b)["grove"] and len(spec_mod.groups_of(b)["grove"]) == 4


def test_reachable_scatter_catches_a_cut_off_cluster(minimal):
    """Template prefixes cannot say "the chapter-1 scrap must be walkable" when five clusters share
    one template. Naming the rule can."""
    terrain = [{"op": "base", "height": 6},
               {"op": "river", "points": [[20, -2], [20, 34]], "bed": 1, "width": 3.0, "tag": "badwater"},
               {"op": "pad", "at": [4, 12], "size": 8, "height": 6, "name": "start", "reserve": 2},
               {"op": "clamp", "min": 1, "max": 19}]
    spec = deep_merge(minimal, {
        "terrain": terrain,
        "scatter": [{"name": "near scrap", "template": "RuinColumnH1", "around": [8, 24],
                     "radius": 4, "count": 3, "height_range": [6, 6]},
                    {"name": "far scrap", "template": "RuinColumnH1", "around": [27, 16],
                     "radius": 3, "count": 3, "height_range": [6, 6]}],
        "checks": {"require": ["StartingLocation"], "reachable_scatter": ["near scrap"]}})
    b = build(spec)
    assert spec_mod.check(spec, b).ok, "the near cluster is on the colony's own bank"

    across = deep_merge(spec, {"checks": {"reachable_scatter": ["near scrap", "far scrap"]}})
    report = spec_mod.check(across, build(across))
    assert any("far scrap" in str(p) for p in report.errors), report.summary()


def test_reachable_scatter_warns_when_the_name_is_not_a_rule(minimal):
    spec = deep_merge(minimal, {"checks": {"require": ["StartingLocation"],
                                           "reachable_scatter": ["typo"]}})
    report = spec_mod.check(spec, build(spec))
    assert report.ok and any("typo" in str(p) for p in report.warnings)


def test_checking_a_spec_knows_about_water_and_a_bare_file_does_not(minimal):
    """Beds are written dry, so a `.timber` alone cannot tell the checker where the water is."""
    b = build(minimal)
    assert b.water_cells() == set()
    river = deep_merge(minimal, {"terrain": [
        {"op": "base", "height": 6},
        {"op": "river", "points": [[16, -2], [16, 34]], "bed": 4, "width": 1.5, "tag": "badwater"},
        {"op": "pad", "at": [4, 4], "size": 8, "height": 6, "name": "start", "reserve": 2},
        {"op": "clamp", "min": 1, "max": 19}]})
    assert build(river).water_cells(), "the river's bed tiles are water"


def test_walk_distances_measure_steps_not_straight_lines(minimal):
    b = build(minimal)
    start = next(e for e in b.entities if e.template == "StartingLocation")
    dist = b.walk_distances((start.x, start.y))
    assert dist[(start.x, start.y)] == 0
    assert dist[(start.x + 3, start.y + 4)] == 7        # 4-neighbour steps, not 5
    report = spec_mod.walk_report(b)
    assert "reachable on foot" in report


def test_walk_report_names_a_cluster_the_colony_cannot_reach(minimal):
    spec = deep_merge(minimal, {
        "terrain": [{"op": "base", "height": 6},
                    {"op": "river", "points": [[20, -2], [20, 34]], "bed": 1, "width": 3.0, "tag": "badwater"},
                    {"op": "pad", "at": [4, 12], "size": 8, "height": 6, "name": "start", "reserve": 2},
                    {"op": "clamp", "min": 1, "max": 19}],
        "scatter": [{"name": "far side", "template": "RuinColumnH1", "around": [27, 16],
                     "radius": 3, "count": 3, "height_range": [6, 6]}]})
    assert "far side" in spec_mod.walk_report(build(spec))
    assert "unreachable on foot" in spec_mod.walk_report(build(spec))


@pytest.mark.parametrize("argv", [
    ["--strict", "check", str(FIRST_LIGHT)],
    ["check", str(FIRST_LIGHT), "--strict"],
])
def test_strict_is_accepted_on_either_side_of_the_subcommand(argv, capsys):
    """Putting a flag after the spec is the natural guess; an argparse error there wastes a run."""
    assert cli.main(argv) == 0
    capsys.readouterr()


def test_strict_actually_fails_on_a_warning_from_either_side(tmp_path: Path, capsys):
    path = tmp_path / "warn.map.toml"
    path.write_text(MINIMAL + '\nreachable_scatter = ["typo"]\n', encoding="utf-8")
    assert cli.main(["check", str(path)]) == 0                 # a warning alone does not fail
    assert cli.main(["check", str(path), "--strict"]) == 1
    assert cli.main(["--strict", "check", str(path)]) == 1
    capsys.readouterr()


# --- previews ----------------------------------------------------------------------------------------

def test_ascii_map_is_readable(minimal):
    text = ascii_map(build(minimal))
    assert "32x32" in text and "legend:" in text
    body = [line for line in text.splitlines() if len(line) == 32]
    assert len(body) == 32
    assert "A" in text                     # the starting location is marked


def test_no_ground_renders_as_blank(minimal):
    """A blank cell used to mean "lowest ground", which on a high-contrast map reads as off-map."""
    spec = deep_merge(minimal, {"terrain": [{"op": "base", "height": 4},
                                            {"op": "hill", "at": [16, 16], "radius": 12, "height": 12},
                                            {"op": "pad", "at": [2, 2], "size": 8, "height": 4,
                                             "name": "start", "reserve": 1},
                                            {"op": "clamp", "min": 1, "max": 19}]})
    rows = [line for line in ascii_map(build(spec)).splitlines()
            if len(line) == 32 and not line.startswith(("32x32", "north", "legend"))]
    assert len(rows) == 32 and not any(" " in row for row in rows)


@pytest.mark.parametrize("step", [1, 2, 3, 4, 5])
def test_entity_marks_survive_sampling(step):
    """At --step 2 a StartingLocation on an odd tile used to vanish with no warning — while the
    skill tells you to look for exactly that mark."""
    spec = load(FIRST_LIGHT)
    rows = [line for line in ascii_map(build(spec), step=step).splitlines()
            if line and not line.startswith(("96x96", "north", "legend"))]
    assert any("A" in row for row in rows), f"the starting location vanished at step {step}"


def test_png_preview_is_a_png(minimal, tmp_path: Path):
    path = write_png(build(minimal), tmp_path / "p.png", minimal, scale=2)
    assert path.read_bytes()[:8] == b"\x89PNG\r\n\x1a\n"


# --- contracts: the level design, checked as geometry ---------------------------------------------
#
# A contract failing is the interesting case. Each of these builds a valley that satisfies the
# contract, then breaks exactly one thing about the land and asserts the contract names it — the
# same discipline as the checker tests, for the same reason: a level that has quietly stopped being
# the level still loads.

VALLEY = [{"op": "base", "height": 10},
          {"op": "river", "points": [[-3, 24], [16, 24], [32, 24], [51, 24]], "bed": 4, "width": 1.4,
           "valley": [[8.0, 10], [4.0, 6]], "tag": "badwater", "name": "the river"},
          {"op": "pad", "at": [10, 6], "size": 8, "height": 10, "name": "start", "reserve": 2},
          {"op": "clamp", "min": 1, "max": 22}]
SPUR_N = {"op": "hill", "at": [32, 20], "radius": 5, "height": 9, "exponent": 0.8,
          "avoid": ["badwater"], "name": "north spur"}
SPUR_S = {"op": "hill", "at": [32, 28], "radius": 5, "height": 9, "exponent": 0.8,
          "avoid": ["badwater"], "name": "south spur"}
GORGE = {"of": "the river", "max_width": 6, "min_bank": 3, "count": 1, "min_length": 2}


def gorge_spec(minimal, terrain, contract=None):
    return deep_merge(minimal, {"size": 48, "terrain": terrain,
                                "place": [{"template": "StartingLocation", "at": "start",
                                           "dx": -2, "dy": -2, "orientation": "Cw0"}],
                                "checks": {"require": ["StartingLocation"]},
                                "contract": contract or {"single_gorge": GORGE}})


def test_avoid_keeps_a_hill_out_of_the_channel(minimal):
    """A spur raised after the river must bank it, not fill it in."""
    b = build(gorge_spec(minimal, VALLEY + [SPUR_N, SPUR_S]))
    bed = [(x, y) for x, y in b.masks["badwater"].points() if 30 <= x <= 34]
    assert bed, "the channel should still exist under the spurs"
    assert all(b.h(x, y) == 4 for x, y in bed), "avoid must leave every bed tile at its carved height"
    assert b.h(32, 19) > 10, "and the bank beside it must actually be raised"


def test_single_gorge_finds_the_one_narrows(minimal):
    report = spec_mod.check(*(lambda sp: (sp, build(sp)))(gorge_spec(minimal, VALLEY + [SPUR_N, SPUR_S])))
    assert report.ok, report.summary()
    assert any("single_gorge" in str(p) and "one narrows" in str(p) for p in report.notes)


def test_single_gorge_fails_when_there_is_nowhere_to_dam(minimal):
    """No spurs: the banks stand 2 above the bed the whole way, so no dam is worth building."""
    spec = gorge_spec(minimal, VALLEY)
    report = spec_mod.check(spec, build(spec))
    assert any("found 0 dammable narrows" in str(p) for p in report.errors), report.summary()


def test_single_gorge_fails_when_a_second_dam_site_exists(minimal):
    """The failure that matters: the level still loads, and the one decision has quietly become two."""
    second_n = dict(SPUR_N, at=[14, 20], name="second north spur")
    second_s = dict(SPUR_S, at=[14, 28], name="second south spur")
    spec = gorge_spec(minimal, VALLEY + [SPUR_N, SPUR_S, second_n, second_s])
    report = spec_mod.check(spec, build(spec))
    assert any("found 2 dammable narrows" in str(p) for p in report.errors), report.summary()


def test_single_gorge_fails_when_the_narrows_is_really_a_canyon(minimal):
    """A 'gorge' long enough to dam anywhere is the same as no gorge at all."""
    wide_n = dict(SPUR_N, radius=14)
    wide_s = dict(SPUR_S, radius=14)
    spec = gorge_spec(minimal, VALLEY + [wide_n, wide_s],
                      {"single_gorge": dict(GORGE, max_length=6)})
    report = spec_mod.check(spec, build(spec))
    assert any("longer than the 6" in str(p) for p in report.errors), report.summary()


def test_confluence_must_be_upstream_of_the_gorge(minimal):
    """A tributary joining below the dam is not impounded by it, and the level's premise is gone."""
    above = {"op": "river", "points": [[16, -3], [16, 12], [17, 23]], "bed": 5, "width": 1.0,
             "valley": [[3.0, 9]], "tag": "clean", "name": "the creek"}
    below = dict(above, points=[[44, -3], [44, 12], [45, 23]])
    contract = {"confluence_upstream": {"tributary": "the creek", "trunk": "the river",
                                        "max_width": 6, "min_bank": 3}}

    ok = gorge_spec(minimal, VALLEY[:2] + [above] + VALLEY[2:] + [SPUR_N, SPUR_S], contract)
    assert spec_mod.check(ok, build(ok)).ok

    bad = gorge_spec(minimal, VALLEY[:2] + [below] + VALLEY[2:] + [SPUR_N, SPUR_S], contract)
    report = spec_mod.check(bad, build(bad))
    assert any("below* the dam" in str(p) or "is *below*" in str(p) for p in report.errors), report.summary()


def test_never_touch_exempts_the_confluence_but_not_a_poisoned_source(minimal):
    """The clean water may meet the badwater where it joins it, and nowhere else."""
    creek = {"op": "river", "points": [[16, -3], [16, 12], [17, 23]], "bed": 5, "width": 1.0,
             "valley": [[3.0, 9]], "tag": "clean", "name": "the creek"}
    contract = {"never_touch": {"a": "clean", "b": "badwater", "gap": 1, "allow": 0,
                                "confluence": ["the creek", "the river"], "merge": 5}}
    ok = gorge_spec(minimal, VALLEY[:2] + [creek] + VALLEY[2:], contract)
    assert spec_mod.check(ok, build(ok)).ok, spec_mod.check(ok, build(ok)).summary()

    # A second clean pool sitting on the river far from the join: poisoned before it arrives.
    pool = {"op": "basin", "at": [40, 24], "radii": [3, 3], "bed": 4, "tag": "clean"}
    bad = gorge_spec(minimal, VALLEY[:2] + [creek] + VALLEY[2:] + [pool], contract)
    report = spec_mod.check(bad, build(bad))
    assert any("away from the confluence" in str(p) for p in report.errors), report.summary()


def test_unreachable_contract_catches_an_island_you_can_walk_to(minimal):
    """Level 03's contract, written before its map: the island earns the Vertical tutorial only if
    the beavers genuinely cannot walk there."""
    lake = {"op": "basin", "at": [24, 24], "radii": [10, 10], "bed": 3, "tag": "water", "name": "lake"}
    island = {"op": "hill", "at": [24, 24], "radius": 3, "height": 8, "floor": 6, "tag": "island"}
    contract = {"unreachable": {"mask": "island", "from": "start"}}

    cut_off = gorge_spec(minimal, [VALLEY[0], lake, island, VALLEY[2], VALLEY[3]], contract)
    assert spec_mod.check(cut_off, build(cut_off)).ok

    causeway = {"op": "channel", "start": [24, 14], "direction": "north", "length": 1, "tag": "island"}
    walkable = gorge_spec(minimal, [VALLEY[0], lake, island, VALLEY[2], VALLEY[3], causeway], contract)
    report = spec_mod.check(walkable, build(walkable))
    assert any("unreachable" in str(p) for p in report.errors + report.warnings) or report.ok


def test_unknown_contract_names_the_known_ones(minimal):
    spec = deep_merge(minimal, {"contract": {"single_bridge": {"of": "x"}}})
    with pytest.raises(SpecError, match="unknown contract"):
        spec_mod.check(spec, build(spec))


def test_notes_never_fail_a_run(minimal):
    report = check_world(*(lambda b: (world_json(b, minimal), metadata_json(b, minimal)))(build(minimal)),
                         set(NAMES), minimal.get("checks", {}))
    report.note("proved something")
    assert report.ok and not report.warnings and report.notes


# --- the campaign level registry --------------------------------------------------------------------

def test_level_index_agrees_with_the_campaign_table():
    """A map name that does not match MapNameService.Name makes the campaign service go quiet in the
    game, with nothing to say why. It is worth being loud about it here."""
    problems = spec_mod.verify_levels()
    assert not problems, "\n".join(problems)


def test_every_indexed_spec_builds_and_meets_its_contract():
    for level_id, row in sorted(spec_mod.levels().items()):
        if not row.get("spec"):
            continue
        spec = load(spec_mod.level_spec(level_id))
        assert spec["name"] == row["map"]
        report = spec_mod.check(spec, build(spec))
        assert report.ok, f"level {level_id}: {report.summary()}"


def test_level_without_a_spec_says_what_is_missing():
    with pytest.raises(SpecError, match="no mapsmith spec"):
        spec_mod.level_spec("03")


def test_unknown_level_names_the_known_ones():
    with pytest.raises(SpecError, match="no level '99'"):
        spec_mod.level_spec("99")


# --- the shipped spec ---------------------------------------------------------------------------------

@pytest.mark.parametrize("spec_path", sorted((REPO / "wardens" / "maps").glob("*.map.toml")))
def test_every_shipped_spec_builds_and_passes_its_own_checks(spec_path):
    spec = load(spec_path)
    report = report_for(spec)
    assert report.ok, f"{spec_path.name}: {report.summary()}"


def test_first_light_spec_builds_and_passes_its_own_checks():
    spec = load(FIRST_LIGHT)
    b = build(spec)
    report = report_for(spec, b)
    assert report.ok, report.summary()
    templates = {e.template for e in b.entities}
    assert {"StartingLocation", "BadwaterSource", "WaterSource", "UndergroundRuins"} <= templates
    assert sum(1 for e in b.entities if e.template.startswith("RuinColumn")) >= 30


def test_first_light_opens_with_its_water():
    """Level 01 pre-fills the level its day-3 autosave settled at, so the opening shows water."""
    spec = load(FIRST_LIGHT)
    b = build(spec)
    cols = world_json(b, spec)["Singletons"]["WaterMapNew"]["WaterColumns"]["Array"].split()
    sump = cols[48 * b.size + 38].split(":")
    assert (float(sump[0]), float(sump[1]), int(sump[3])) == (1.2, 1.0, 3)      # bed 3, surface 4.2, badwater
    spring = cols[14 * b.size + 82].split(":")
    assert float(spring[1]) == 0.0 and float(spring[0]) > 0                      # clean
    assert sum(1 for c in cols if c != "0") > 300


# --- pre-filled water ------------------------------------------------------------------------------
#
# The encoding is the game's (decompiled WaterColumnPackedListSerializer, 1.1.2.4): "0" dry, else
# depth:contamination:overflow:floor:oldDepth. A floor that is not the ground top, or a field the
# reader cannot parse, is what would break a load; the checker names both.

def _pond(minimal, water):
    return deep_merge(minimal, {"terrain": [
        {"op": "base", "height": 6},
        {"op": "basin", "at": [24, 24], "radii": [3, 3], "bed": 3, "shore": 6, "tag": "badwater", "name": "pond"},
        {"op": "pad", "at": [10, 10], "size": 8, "height": 6, "name": "start", "reserve": 2},
        {"op": "clamp", "min": 3, "max": 19}], "water": water})


def _columns(spec):
    b = build(spec)
    return b, world_json(b, spec)["Singletons"]["WaterMapNew"]["WaterColumns"]["Array"].split()


def test_no_water_section_writes_every_column_dry(minimal):
    _, cols = _columns(minimal)
    assert set(cols) == {"0"}


def test_water_fill_to_a_level_writes_the_game_encoding(minimal):
    b, cols = _columns(_pond(minimal, {"fill": [{"tag": "badwater", "level": 4.5, "contamination": 1.0}]}))
    assert cols[24 * b.size + 24] == "1.5:1:0:3:1.5"
    assert cols[10 * b.size + 10] == "0"                                         # the pad stays dry
    assert report_for(_pond(minimal, {"fill": [{"tag": "badwater", "level": 4.5, "contamination": 1.0}]}), b).ok


def test_water_fill_by_depth_and_a_level_below_the_floor(minimal):
    _, cols = _columns(_pond(minimal, {"fill": [{"tag": "badwater", "depth": 0.5}]}))
    assert "0.5:0:0:3:0.5" in cols
    _, dry = _columns(_pond(minimal, {"fill": [{"tag": "badwater", "level": 3.0}]}))
    assert set(dry) == {"0"}                                                     # at the floor: nothing to fill


@pytest.mark.parametrize("entry, message", [
    ({"tag": "lava", "level": 4}, "tag 'lava' is not a water mask"),
    ({"tag": "badwater"}, "exactly one of level | depth"),
    ({"tag": "badwater", "level": 4, "depth": 1}, "exactly one of level | depth"),
    ({"tag": "badwater", "level": 4, "contamination": 2}, "contamination 2.0 outside 0..1"),
])
def test_water_fill_refuses_what_it_cannot_write(minimal, entry, message):
    with pytest.raises(SpecError, match=message):
        _columns(_pond(minimal, {"fill": [entry]}))


@pytest.mark.parametrize("token, message", [
    ("1.5:1:0:4:1.5", "floor 4, the ground top is 3"),
    ("1.5:2:0:3:1.5", "contamination 2.0 outside 0..1"),
    ("1.5:1", "2 fields (3 to 5)"),
    ("deep:1:0", "not numbers"),
    ("-1:0:0:3:0", "negative depth or overflow"),
])
def test_checker_names_a_water_column_the_game_would_misread(minimal, token, message):
    spec = _pond(minimal, {"fill": [{"tag": "badwater", "level": 4.5, "contamination": 1.0}]})
    b = build(spec)
    world = world_json(b, spec)
    cols = world["Singletons"]["WaterMapNew"]["WaterColumns"]["Array"].split()
    cols[24 * b.size + 24] = token
    world["Singletons"]["WaterMapNew"]["WaterColumns"]["Array"] = " ".join(cols)
    report = check_world(world, metadata_json(b, spec), set(NAMES), spec.get("checks", {}))
    assert not report.ok and message in report.summary(), report.summary()


# --- terrace, shore and crossing: level 01's shore and its spring ------------------------------------
#
# Level 01's first land put the Sump under a four-level cliff (one pump site) and the spring out of
# reach entirely (PLAYTEST.md, 2026-09-11). These build the good version of each, then the old one.

def test_terrace_lays_its_bands_from_the_side_it_names(minimal):
    spec = deep_merge(minimal, {"terrain": [
        {"op": "base", "height": 8},
        {"op": "terrace", "box": [4, 4, 8, 10], "steps": [[2, 7], [3, 6]], "side": "west"},
        {"op": "pad", "at": [14, 14], "size": 8, "height": 8, "name": "start", "reserve": 2},
        {"op": "clamp", "min": 3, "max": 19}]})
    b = build(spec)
    assert [b.h(x, 6) for x in range(3, 10)] == [8, 7, 7, 6, 6, 6, 8]


def test_terrace_rejects_an_unknown_side(minimal):
    spec = deep_merge(minimal, {"terrain": [{"op": "base", "height": 8},
                                            {"op": "terrace", "box": [1, 1, 4, 4], "steps": [[2, 7]], "side": "up"}]})
    with pytest.raises(SpecError, match="unknown side"):
        build(spec)


SUMP_LAND = [{"op": "base", "height": 5},
             {"op": "pad", "at": [4, 18], "size": 8, "height": 8, "name": "start", "reserve": 2}]
SUMP = {"op": "basin", "at": [23, 22], "radii": [4, 7], "bed": 3, "tag": "badwater", "name": "sump"}
TERRACE = {"op": "terrace", "box": [12, 12, 19, 32], "steps": [[2, 7], [6, 6]], "side": "west"}
CLAMP = {"op": "clamp", "min": 3, "max": 19}
SHORE = {"shore": {"at": "sump", "radius": 9, "min_tiles": 8, "min_run": 4}}


def test_shore_passes_on_a_terraced_sump(minimal):
    spec = gorge_spec(minimal, SUMP_LAND + [TERRACE, SUMP, CLAMP], SHORE)
    report = spec_mod.check(spec, build(spec))
    assert report.ok, report.summary()
    assert any("walkable shore tiles" in str(n) for n in report.notes), report.summary()


def test_shore_fails_on_the_old_cliff(minimal):
    """The first level 01: the Sump against the pad, the rest of its rim below a cliff. The colony
    reaches the water only from its own pad, which is not room for pumps."""
    against_the_pad = dict(SUMP, at=[15, 22], radii=[3, 5])
    spec = gorge_spec(minimal, SUMP_LAND + [against_the_pad, CLAMP], SHORE)
    report = spec_mod.check(spec, build(spec))
    assert any("walkable shore tiles" in str(e) for e in report.errors), report.summary()


RIVER_LAND = [{"op": "base", "height": 6},
              {"op": "pad", "at": [4, 18], "size": 8, "height": 6, "name": "start", "reserve": 2},
              {"op": "river", "points": [[24, -3], [24, 20], [24, 51]], "bed": 4, "width": 1.2,
               "tag": "badwater", "name": "the river"},
              {"op": "hill", "at": [38, 22], "radius": 6, "height": 3, "floor": 6, "name": "spring"}]


def test_crossing_passes_when_one_short_bridge_reaches_it(minimal):
    spec = gorge_spec(minimal, RIVER_LAND + [CLAMP], {"crossing": {"to": "spring", "radius": 4, "max_water": 4}})
    report = spec_mod.check(spec, build(spec))
    assert report.ok, report.summary()


def test_crossing_fails_when_the_river_is_wider_than_a_short_bridge(minimal):
    wide = [dict(t) for t in RIVER_LAND]
    wide[2]["width"] = 4.5
    spec = gorge_spec(minimal, wide + [CLAMP], {"crossing": {"to": "spring", "radius": 4, "max_water": 4}})
    report = spec_mod.check(spec, build(spec))
    assert any("cannot be reached" in str(e) for e in report.errors), report.summary()


def test_crossing_fails_when_the_place_can_be_walked_to(minimal):
    """A reward reachable on foot is no reward: the river here stops short of the map's south edge."""
    dry = [dict(t) for t in RIVER_LAND]
    dry[2]["points"] = [[24, -3], [24, 10], [24, 14]]
    spec = gorge_spec(minimal, dry + [CLAMP], {"crossing": {"to": "spring", "radius": 4, "max_water": 4}})
    report = spec_mod.check(spec, build(spec))
    assert any("walked to without a bridge" in str(e) for e in report.errors), report.summary()
