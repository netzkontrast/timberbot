"""Tests for gen_map.py's level registry: level 10 is level 01 healed, and a level that is not
shipped never lands in src/Maps.

    uv run --project python --extra dev --with numpy pytest wardens/tools/test_gen_map.py

gen_map.py needs numpy, which the Python dev extras do not carry (mapsmith is the standard-library
path); the tests skip without it.
"""
from __future__ import annotations

from pathlib import Path

import pytest

np = pytest.importorskip("numpy")
import gen_map  # noqa: E402


def test_home_is_first_light_healed() -> None:
    first = gen_map.FirstLight().build()
    home = gen_map.Home().build()
    assert np.array_equal(home.height, first.height)
    assert not any(e["Template"] == "BadwaterSource" for e in home.entities)
    assert home.contamination().sum() == 0
    assert len(home.trees) >= gen_map.Home.TREE_TARGET * len(first.trees)


def test_home_passes_its_own_check(tmp_path: Path) -> None:
    out = tmp_path / "Wardens 10 Home.timber"
    assert gen_map.generate(gen_map.Home, out, None, None, None) == 0
    assert gen_map.check(out) == []


def test_unshipped_level_refuses_src_maps() -> None:
    with pytest.raises(SystemExit):
        gen_map.generate(gen_map.Home, None, None, None, None)
