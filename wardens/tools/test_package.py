"""Tests for package.py: the ZIP holds what the Deploy target deploys, and nothing local-only.

    uv run --project python --extra dev pytest wardens/tools/test_package.py
"""
from __future__ import annotations

import json
import zipfile
from pathlib import Path

import pytest

import package as pk


@pytest.fixture
def src(tmp_path: Path) -> Path:
    s = tmp_path / "src"
    (s / "Buildings" / "Core").mkdir(parents=True)
    (s / "Buildings" / "Core" / "Core.Wardens.blueprint.json").write_text("{}", encoding="utf-8")
    (s / "Cutscenes").mkdir()
    (s / "Cutscenes" / "ColdBoot.json").write_text("{}", encoding="utf-8")
    (s / "Maps").mkdir()
    (s / "Maps" / "Wardens Wasteland.timber").write_bytes(b"map")
    (s / "Localizations").mkdir()
    (s / "Localizations" / "enUS.csv").write_text("ID,Text,Comment\n", encoding="utf-8")
    (s / "manifest.json").write_text(json.dumps({"Version": "0.3.0", "Id": "Wardens"}), encoding="utf-8")
    (s / "settings.json").write_text("{}", encoding="utf-8")
    (s / "thumbnail.png").write_bytes(b"png")
    # never shipped
    (s / "bin" / "Release").mkdir(parents=True)
    (s / "bin" / "Release" / "Wardens.dll").write_bytes(b"dll")
    (s / "obj").mkdir()
    (s / "obj" / "junk.txt").write_text("x", encoding="utf-8")
    (s / "WardensCutscenes.cs").write_text("// code", encoding="utf-8")
    (s / "Wardens.csproj").write_text("<Project/>", encoding="utf-8")
    (s / "Directory.Build.props").write_text("<Project/>", encoding="utf-8")
    (s / ".gitignore").write_text("bin/\n", encoding="utf-8")
    (s / "Scripts" / "Harmony").mkdir(parents=True)
    (s / "Scripts" / "Harmony" / "0Harmony.dll").write_bytes(b"dll")
    (s / "Factions").mkdir()
    (s / "Factions" / "Faction.Wardens.blueprint.json").write_text("{}", encoding="utf-8")
    (s / "Factions" / "Faction.LeafCoats.blueprint.json").write_text("{}", encoding="utf-8")
    (s / ".leafcoats-import.txt").write_text("/Factions/Faction.LeafCoats.blueprint.json\n/Scripts/Harmony/0Harmony.dll\n",
                                            encoding="utf-8")
    return s


def test_mod_files_apply_the_csproj_rules(src: Path) -> None:
    names = [arc for _, arc in pk.mod_files(src)]
    assert names == [
        "Wardens/Buildings/Core/Core.Wardens.blueprint.json",
        "Wardens/Cutscenes/ColdBoot.json",
        "Wardens/Factions/Faction.Wardens.blueprint.json",
        "Wardens/Localizations/enUS.csv",
        "Wardens/Maps/Wardens Wasteland.timber",
        "Wardens/manifest.json",
        "Wardens/settings.json",
        "Wardens/thumbnail.png",
    ]


def test_zip_layout(src: Path, tmp_path: Path) -> None:
    dll = tmp_path / "Wardens.dll"
    dll.write_bytes(b"built")
    doc = tmp_path / "WARDEN.md"
    doc.write_text("# playbook", encoding="utf-8")
    out = tmp_path / "dist" / "Wardens-v0.3.0.zip"
    names = pk.build_zip(dll, out, src=src, docs=[doc, tmp_path / "missing.md"])
    assert names[0] == "Wardens/Wardens.dll"
    assert "Wardens/docs/WARDEN.md" in names
    assert "Maps/Wardens Wasteland.timber" in names
    assert names[-1] == "README.txt"
    assert not any(n.endswith(".cs") or "/bin/" in n or "/obj/" in n or "Harmony" in n or "LeafCoats" in n for n in names)
    with zipfile.ZipFile(out) as zf:
        assert sorted(zf.namelist()) == sorted(names)
        assert zf.read("Wardens/Wardens.dll") == b"built"
        readme = zf.read("README.txt").decode("utf-8")
        assert readme.startswith("The Wardens v0.3.0 for Timberborn")
        assert "Documents/Timberborn/Mods/" in readme and "Documents/Timberborn/Maps/" in readme


def test_no_import_listing_means_nothing_skipped(src: Path) -> None:
    (src / ".leafcoats-import.txt").unlink()
    names = [arc for _, arc in pk.mod_files(src)]
    assert "Wardens/Factions/Faction.LeafCoats.blueprint.json" in names   # nothing says it is local-only
    assert not any(n.endswith(".dll") for n in names)                       # foreign DLLs never ship


def test_shipped_source_tree_has_the_expected_shape() -> None:
    names = [arc for _, arc in pk.mod_files(pk.SRC)]
    assert "Wardens/manifest.json" in names
    assert "Wardens/settings.json" in names
    assert "Wardens/thumbnail.png" in names
    assert "Wardens/Cutscenes/ColdBoot.json" in names
    assert "Wardens/Maps/Wardens Wasteland.timber" in names
    assert not any(n.endswith((".cs", ".dll", ".csproj")) for n in names)
    assert pk.read_version() == json.loads((pk.SRC / "manifest.json").read_text(encoding="utf-8"))["Version"]


def test_main_without_dll_exits_2(monkeypatch: pytest.MonkeyPatch, tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    monkeypatch.setattr("sys.argv", ["package.py", "--dll", str(tmp_path / "nope.dll")])
    assert pk.main() == 2
    assert "build first" in capsys.readouterr().err
