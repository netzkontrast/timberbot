"""Bump the Wardens mod version (patch number) in the three places it lives.

Run by the BumpVersion target in src/Wardens.csproj before every build, so each deployed build
shows a new version in the game's mod manager. Can also be run by hand:

    python wardens/tools/bump_version.py            # 0.2.7 -> 0.2.8
    python wardens/tools/bump_version.py --minor    # 0.2.7 -> 0.3.0
    python wardens/tools/bump_version.py --show

Files: src/manifest.json ("Version"), src/WardensMcpServer.cs (Version constant),
src/Wardens.csproj (<Version>). The manifest is the source of truth; the other two are set to
the same string.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SRC = Path(__file__).resolve().parents[1] / "src"
MANIFEST = SRC / "manifest.json"
SERVER = SRC / "WardensMcpServer.cs"
CSPROJ = SRC / "Wardens.csproj"


def read_version() -> str:
    return json.load(open(MANIFEST, encoding="utf-8"))["Version"]


def bump(version: str, minor: bool) -> str:
    parts = [int(x) for x in version.split(".")]
    while len(parts) < 3:
        parts.append(0)
    if minor:
        parts[1] += 1
        parts[2] = 0
    else:
        parts[2] += 1
    return ".".join(str(x) for x in parts[:3])


def write_version(version: str) -> None:
    m = json.load(open(MANIFEST, encoding="utf-8"))
    m["Version"] = version
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        json.dump(m, f, indent=2)
        f.write("\n")

    s = SERVER.read_text(encoding="utf-8")
    s2 = re.sub(r'public const string Version = "[^"]*";', f'public const string Version = "{version}";', s, count=1)
    if s2 != s:
        SERVER.write_text(s2, encoding="utf-8", newline="\n")

    c = CSPROJ.read_text(encoding="utf-8")
    c2 = re.sub(r"<Version>[^<]*</Version>", f"<Version>{version}</Version>", c, count=1)
    if c2 != c:
        CSPROJ.write_text(c2, encoding="utf-8", newline="\n")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--minor", action="store_true")
    ap.add_argument("--show", action="store_true")
    args = ap.parse_args()
    current = read_version()
    if args.show:
        print(current)
        return 0
    new = bump(current, args.minor)
    write_version(new)
    print(f"Wardens version {current} -> {new}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
