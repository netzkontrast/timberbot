"""Tests for check_doc_drift.py: a doc that names its sources is stale when a source changed."""
import subprocess
import sys
from pathlib import Path

import check_doc_drift as cdd

HERE = Path(__file__).resolve().parent


def _write(p: Path, text: str) -> None:
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")


def _repo(tmp_path: Path) -> Path:
    _write(tmp_path / "wardens" / "WARDEN.md", "# playbook v1\n")
    _write(tmp_path / "design" / "play.md",
           "<!-- doc-source: wardens/WARDEN.md -->\n<!-- doc-hash: 0000000000000000 -->\n# stance\n")
    _write(tmp_path / "docs" / "plain.md", "# no marker\n")
    return tmp_path


def test_stale_when_hash_does_not_match_sources(tmp_path):
    root = _repo(tmp_path)
    report = cdd.check([root / "design" / "play.md"], root)
    assert report.stale == [Path("design/play.md")]
    assert not report.ok


def test_update_restamps_and_then_check_is_clean(tmp_path):
    root = _repo(tmp_path)
    doc = root / "design" / "play.md"
    cdd.check([doc], root, update=True)
    assert cdd.check([doc], root).ok
    assert "<!-- doc-hash: 0000000000000000 -->" not in doc.read_text()


def test_source_change_makes_a_stamped_doc_stale_again(tmp_path):
    root = _repo(tmp_path)
    doc = root / "design" / "play.md"
    cdd.check([doc], root, update=True)
    (root / "wardens" / "WARDEN.md").write_text("# playbook v2\n", encoding="utf-8")
    assert cdd.check([doc], root).stale == [Path("design/play.md")]


def test_missing_source_is_broken_not_stale(tmp_path):
    root = _repo(tmp_path)
    doc = root / "design" / "play.md"
    doc.write_text("<!-- doc-source: wardens/GONE.md -->\n<!-- doc-hash: 00 -->\n", encoding="utf-8")
    report = cdd.check([doc], root)
    assert report.broken == [Path("design/play.md")]
    assert not report.ok


def test_unmarked_doc_is_reported_but_only_fails_in_strict(tmp_path):
    root = _repo(tmp_path)
    doc = root / "docs" / "plain.md"
    assert cdd.check([doc], root).ok
    assert cdd.check([doc], root).unmarked == [Path("docs/plain.md")]
    assert not cdd.check([doc], root, strict=True).ok


def test_cli_exit_codes(tmp_path):
    root = _repo(tmp_path)
    cmd = [sys.executable, str(HERE / "check_doc_drift.py"), "--root", str(root), "design/play.md"]
    assert subprocess.run(cmd, capture_output=True).returncode == 1
    assert subprocess.run(cmd + ["--update"], capture_output=True).returncode == 0
    assert subprocess.run(cmd, capture_output=True).returncode == 0
