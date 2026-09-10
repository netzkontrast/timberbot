#!/usr/bin/env python3
"""check_doc_drift.py: flag documents whose named sources changed since the doc was last synced.

A hand-written document that restates something the code or another document defines (the
five documents of the Wardens' agent contract; a design note that describes a source file)
declares its sources near its top:

    <!-- doc-source: wardens/WARDEN.md wardens/src/WardensMcpTools.cs -->
    <!-- doc-hash: 4f57b84b630c0f21 -->

This script hashes the listed sources and compares the result with the stamped hash. A
mismatch means a source changed after the doc was last reviewed: the doc is STALE and a human
reads it before re-stamping. The check never edits prose; `--update` only re-stamps the hash,
so run it after the review, not instead of it.

Usage:
    check_doc_drift.py [--root DIR] [--update] [--strict] DOC [DOC ...]
    check_doc_drift.py --root . --strict $(git ls-files 'design/*.md')

Exit 0: every marked doc is in sync. Exit 1: a doc is stale or broken (a listed source does
not exist), or, with --strict, a doc carries no marker at all. Standard library only.

Pattern taken from `scripts/check-doc-drift` in netzkontrast/agency (MIT), rewritten.
"""
from __future__ import annotations

import argparse
import hashlib
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

SOURCE_RE = re.compile(r"<!--\s*doc-source:\s*(.+?)\s*-->")
HASH_RE = re.compile(r"<!--\s*doc-hash:\s*([0-9a-fA-F]*)\s*-->")
HEAD_LINES = 12  # the markers must sit near the top


@dataclass
class Report:
    stale: list[Path] = field(default_factory=list)
    broken: list[Path] = field(default_factory=list)
    unmarked: list[Path] = field(default_factory=list)
    updated: list[Path] = field(default_factory=list)
    in_sync: list[Path] = field(default_factory=list)
    strict: bool = False

    @property
    def ok(self) -> bool:
        if self.stale or self.broken:
            return False
        return not (self.strict and self.unmarked)


def hash_sources(root: Path, sources: list[str]) -> str | None:
    """Hash the bytes of every listed source in order; None when one is missing."""
    h = hashlib.sha256()
    for rel in sources:
        p = root / rel
        if not p.is_file():
            return None
        h.update(rel.encode("utf-8"))
        h.update(b"\0")
        h.update(p.read_bytes())
    return h.hexdigest()[:16]


def check(docs: list[Path], root: Path, update: bool = False, strict: bool = False) -> Report:
    report = Report(strict=strict)
    for doc in docs:
        rel = doc.resolve().relative_to(root.resolve()) if doc.is_absolute() else doc
        text = (root / rel).read_text(encoding="utf-8")
        head = "\n".join(text.splitlines()[:HEAD_LINES])
        src = SOURCE_RE.search(head)
        if not src:
            report.unmarked.append(rel)
            continue
        expected = hash_sources(root, src.group(1).split())
        if expected is None:
            report.broken.append(rel)
            continue
        stamped = HASH_RE.search(head)
        if stamped and stamped.group(1).lower() == expected:
            report.in_sync.append(rel)
            continue
        if update:
            new_marker = f"<!-- doc-hash: {expected} -->"
            if stamped:
                new_text = text.replace(stamped.group(0), new_marker, 1)
            else:
                new_text = text.replace(src.group(0), src.group(0) + "\n" + new_marker, 1)
            (root / rel).write_text(new_text, encoding="utf-8")
            report.updated.append(rel)
        else:
            report.stale.append(rel)
    return report


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("docs", nargs="+", help="documents to check, relative to --root")
    ap.add_argument("--root", default=".", help="repository root (default: cwd)")
    ap.add_argument("--update", action="store_true", help="re-stamp doc-hash after a human review")
    ap.add_argument("--strict", action="store_true", help="also fail on docs without a marker")
    args = ap.parse_args(argv)
    root = Path(args.root)
    report = check([Path(d) for d in args.docs], root, update=args.update, strict=args.strict)
    for label, items in (("IN-SYNC", report.in_sync), ("UPDATED", report.updated),
                         ("STALE", report.stale), ("BROKEN", report.broken),
                         ("UNMARKED", report.unmarked)):
        for p in items:
            print(f"{label:9} {p}")
    if report.stale:
        print("stale: a listed source changed since the doc was stamped. Read the doc against the "
              "source, fix the prose, then re-run with --update.")
    if report.broken:
        print("broken: a listed source does not exist. Fix the doc-source line.")
    print("problems: none" if report.ok else "problems: yes")
    return 0 if report.ok else 1


if __name__ == "__main__":
    sys.exit(main())
