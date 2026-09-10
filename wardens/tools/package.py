"""Package the built Wardens mod as a release ZIP.

    python wardens/tools/package.py                          # dist/Wardens-v<version>.zip, DLL from src/bin/Release
    python wardens/tools/package.py --dll path/to/Wardens.dll
    python wardens/tools/package.py --list                   # print what would go in; write nothing

Layout of the ZIP (each top-level folder drops into Documents/Timberborn/):

    Wardens/        the mod: Wardens.dll, manifest.json, thumbnail.png, settings.json, the data folders
                    (Buildings, Collections, Cutscenes, Decals, Factions, Goods, IlluminationColors,
                    Localizations, Maps, Materials, NaturalResources, Needs, Recipes, Sprites, Tutorials)
                    and docs/ (WARDEN.md plus the Timberbot API docs the MCP `manual` tool serves)
    Maps/           the shipped map(s) again, because the game lists custom maps from Documents/Timberborn/Maps
    README.txt      the install steps

Same content rules as the Deploy target in src/Wardens.csproj: everything under src/ except bin/, obj/,
*.cs, the csproj, Directory.Build.props, .gitignore, .leafcoats-import.txt, any DLL that is not ours, and
the local-only Leaf Coats copies that .leafcoats-import.txt lists (they are never redistributed).
The build itself is `dotnet build wardens/src/Wardens.csproj -c Release` (it needs the game's assemblies);
this script only packages what it produced. Exit code 2 when the DLL is missing.
"""
from __future__ import annotations

import argparse
import json
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "wardens" / "src"
DIST = ROOT / "dist"
MOD_NAME = "Wardens"
DEFAULT_DLL = SRC / "bin" / "Release" / "netstandard2.1" / "Wardens.dll"
DOCS = [ROOT / "docs" / "api-reference.md", ROOT / "docs" / "events.md",
        ROOT / "docs" / "websocket-protocol.md", ROOT / "wardens" / "WARDEN.md"]
EXCLUDED_DIRS = {"bin", "obj"}
EXCLUDED_FILES = {"Wardens.csproj", "Directory.Build.props", ".gitignore", ".leafcoats-import.txt"}
EXCLUDED_SUFFIXES = {".cs", ".dll"}
README = """The Wardens {version} for Timberborn

1. Copy the Wardens folder into Documents/Timberborn/Mods/ (so that Documents/Timberborn/Mods/Wardens/manifest.json exists).
2. Copy the contents of the Maps folder into Documents/Timberborn/Maps/ (the New Game screen then lists "[Custom] Wardens 01 First Light").
3. In the Mod Manager enable The Wardens and disable Timberbot API (the same code is compiled into the Wardens; two copies fight over port 8085).
4. New Game -> The Wardens, tutorial on, map [Custom] Wardens 01 First Light.

An AI agent connects through the MCP server in the mod (http://127.0.0.1:8090/mcp); its playbook is Wardens/docs/WARDEN.md.
Settings: Wardens/settings.json. Source and documentation: https://github.com/netzkontrast/timberbot/tree/main/wardens
"""


def read_version(src: Path = SRC) -> str:
    return json.loads((src / "manifest.json").read_text(encoding="utf-8"))["Version"]


def leafcoats_imports(src: Path) -> set[str]:
    """Relative paths (posix, no leading slash) of the local-only Leaf Coats copies, if any."""
    listing = src / ".leafcoats-import.txt"
    if not listing.exists():
        return set()
    return {line.strip().lstrip("/") for line in listing.read_text(encoding="utf-8").splitlines() if line.strip()}


def mod_files(src: Path = SRC) -> list[tuple[Path, str]]:
    """(file, arcname) for everything under src/ that ships, with the csproj's exclusions applied."""
    skip = leafcoats_imports(src)
    out: list[tuple[Path, str]] = []
    for p in sorted(src.rglob("*")):
        if not p.is_file():
            continue
        rel = p.relative_to(src)
        if rel.parts[0] in EXCLUDED_DIRS:
            continue
        if rel.name in EXCLUDED_FILES or p.suffix.lower() in EXCLUDED_SUFFIXES:
            continue
        if rel.as_posix() in skip:
            continue
        out.append((p, f"{MOD_NAME}/{rel.as_posix()}"))
    return out


def entries(dll: Path, src: Path = SRC, docs: list[Path] | None = None) -> list[tuple[Path | None, str, str | None]]:
    """Everything the ZIP holds: (file, arcname, text) with either a file or an inline text."""
    docs = DOCS if docs is None else docs
    out: list[tuple[Path | None, str, str | None]] = [(dll, f"{MOD_NAME}/Wardens.dll", None)]
    out += [(p, arc, None) for p, arc in mod_files(src)]
    out += [(d, f"{MOD_NAME}/docs/{d.name}", None) for d in docs if d.exists()]
    out += [(p, f"Maps/{p.name}", None) for p in sorted((src / "Maps").glob("*.timber"))]
    out.append((None, "README.txt", README.format(version="v" + read_version(src))))
    return out


def build_zip(dll: Path, out_path: Path, src: Path = SRC, docs: list[Path] | None = None) -> list[str]:
    """Write the ZIP; returns the arcnames in order."""
    names: list[str] = []
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for path, arc, text in entries(dll, src, docs):
            if text is not None:
                zf.writestr(arc, text)
            else:
                zf.write(path, arc)
            names.append(arc)
    return names


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dll", type=Path, default=DEFAULT_DLL, help="the built Wardens.dll")
    ap.add_argument("--out", type=Path, default=None, help="ZIP path (default dist/Wardens-v<version>.zip)")
    ap.add_argument("--list", action="store_true", help="print the entries and write nothing")
    args = ap.parse_args()
    version = read_version()
    if args.list:
        for _, arc, _ in entries(args.dll):
            print(arc)
        return 0
    if not args.dll.exists():
        print(f"no DLL at {args.dll}: build first (dotnet build wardens/src/Wardens.csproj -c Release) or pass --dll",
              file=sys.stderr)
        return 2
    out = args.out or DIST / f"{MOD_NAME}-v{version}.zip"
    names = build_zip(args.dll, out)
    print(f"packaged {out.relative_to(ROOT) if out.is_relative_to(ROOT) else out}: {len(names)} entries, v{version}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
