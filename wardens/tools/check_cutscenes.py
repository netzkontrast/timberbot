"""Static checks on the cutscene files: everything a scene resolves by name when it plays.

    python wardens/tools/check_cutscenes.py                 # deployed copy in Documents/Timberborn/Mods/Wardens
    python wardens/tools/check_cutscenes.py wardens/src     # the source tree

The rules are WardensCutsceneScript.Parse's (design/wardens-cutscenes.md §3) plus the names only the
mod knows: caption loc keys against Localizations/enUS*.csv, chapter triggers against the table in
src/WardensChapters.cs, tutorial triggers against Tutorials/*.blueprint.json, and unknown fields
(the parser ignores them; a typo such as "captoin" would otherwise play as a shot without text).
Needs no game files, so it runs in any checkout; validate.py calls check() and merges the problems.
Exit code 1 when there are problems.
"""
from __future__ import annotations

import csv
import json
import re
import sys
from pathlib import Path

DEFAULT_MOD = Path.home() / "Documents/Timberborn/Mods/Wardens"
CHAPTERS_CS = Path(__file__).resolve().parents[1] / "src" / "WardensChapters.cs"
FOLDER = "Cutscenes"

ANCHORS = {"start", "core", "selection", "bot", "grid", "world"}
POINTER_ANCHORS = {"core", "grid", "selection"}          # the anchors with grid coordinates
WAITS = {"time", "continue"}
SCENE_FIELDS = {"id", "on", "pause", "leave_paused", "restore_camera", "letterbox", "skippable", "say", "shots"}
SCENE_BOOLS = ("pause", "leave_paused", "restore_camera", "letterbox", "skippable")
SHOT_FIELDS = {"id", "caption", "text", "seconds", "wait", "camera", "point", "toast", "say"}
KEYFRAME_FIELDS = {"t", "anchor", "x", "y", "z", "offset", "h", "v", "zoom", "dh", "dv", "dzoom"}
POINTER_FIELDS = {"anchor", "x", "y", "z", "offset", "message", "seconds", "color"}
TRIGGER_NEW_GAME = "new_game"
TRIGGER_CHAPTER = "chapter:"
TRIGGER_TUTORIAL = "tutorial:"


def read_chapters(path: Path = CHAPTERS_CS) -> list[tuple[str, str, list[str]]]:
    """(id, gating tutorial id, template names) per `new WardensChapter(...)` entry in the C# table."""
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"//[^\n]*", "", text)   # strip line comments; the table itself has none inside
    entries = re.findall(r'new WardensChapter\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*new\[\]\s*\{([^}]*)\}\s*\)', text)
    return [(cid, tutorial, re.findall(r'"([^"]+)"', names)) for cid, tutorial, names in entries]


def read_loc_keys(mod: Path) -> set[str]:
    keys: set[str] = set()
    for p in (mod / "Localizations").glob("enUS*.csv"):
        with open(p, encoding="utf-8-sig", newline="") as fh:
            for row in csv.reader(fh):
                if row:
                    keys.add(row[0])
    return keys


def read_tutorial_ids(mod: Path) -> set[str]:
    ids: set[str] = set()
    for p in mod.rglob("*.blueprint.json"):
        try:
            d = json.loads(p.read_text(encoding="utf-8-sig"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            continue
        spec = d.get("TutorialSpec") if isinstance(d, dict) else None
        if spec and spec.get("Id"):
            ids.add(spec["Id"])
    return ids


def _is_number(v) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def _check_keyframe(k, where: str, out: list[str]) -> None:
    if not isinstance(k, dict):
        out.append(f"{where}: not an object"); return
    for f in sorted(set(k) - KEYFRAME_FIELDS):
        out.append(f"{where}.{f}: unknown field")
    t = k.get("t", 0)
    if not _is_number(t) or t < 0:
        out.append(f"{where}.t: not a number >= 0")
    anchor = k.get("anchor", "start")
    if not isinstance(anchor, str) or anchor.strip().lower() not in ANCHORS:
        out.append(f"{where}.anchor: {anchor!r} ({' | '.join(sorted(ANCHORS))})")
        anchor = "start"
    for f in ("x", "y", "z", "h", "v", "zoom", "dh", "dv", "dzoom"):
        if f in k and not _is_number(k[f]):
            out.append(f"{where}.{f}: not a number")
    if anchor.strip().lower() in ("grid", "world") and ("x" not in k or "y" not in k):
        out.append(f"{where}: anchor {anchor} needs x and y")
    _check_offset(k, where, out)


def _check_offset(o: dict, where: str, out: list[str]) -> None:
    if "offset" not in o:
        return
    off = o["offset"]
    if not isinstance(off, list) or len(off) not in (2, 3) or not all(_is_number(v) for v in off):
        out.append(f"{where}.offset: [dx, dy] or [dx, dy, dz]")


def _check_pointer(p, where: str, out: list[str]) -> None:
    if not isinstance(p, dict):
        out.append(f"{where}: not an object"); return
    for f in sorted(set(p) - POINTER_FIELDS):
        out.append(f"{where}.{f}: unknown field")
    anchor = p.get("anchor", "core")
    if not isinstance(anchor, str) or anchor.strip().lower() not in POINTER_ANCHORS:
        out.append(f"{where}.anchor: {anchor!r} ({' | '.join(sorted(POINTER_ANCHORS))}: the anchors with grid coordinates)")
    elif anchor.strip().lower() == "grid" and ("x" not in p or "y" not in p):
        out.append(f"{where}: anchor grid needs x and y")
    for f in ("x", "y", "z", "seconds"):
        if f in p and not _is_number(p[f]):
            out.append(f"{where}.{f}: not a number")
    for f in ("message", "color"):
        if f in p and not isinstance(p[f], str):
            out.append(f"{where}.{f}: not a string")
    _check_offset(p, where, out)


def _check_shot(s, where: str, loc: set[str], out: list[str]) -> None:
    if not isinstance(s, dict):
        out.append(f"{where}: not an object"); return
    for f in sorted(set(s) - SHOT_FIELDS):
        out.append(f"{where}.{f}: unknown field")
    for f in ("id", "caption", "text", "toast", "say"):
        if f in s and not isinstance(s[f], str):
            out.append(f"{where}.{f}: not a string")
    caption = s.get("caption")
    if isinstance(caption, str) and caption not in loc:
        out.append(f"{where}.caption: loc key missing: {caption}")
    if "seconds" in s and (not _is_number(s["seconds"]) or s["seconds"] < 0):
        out.append(f"{where}.seconds: not a number >= 0")
    if s.get("wait", "time") not in WAITS:
        out.append(f"{where}.wait: {s.get('wait')!r} (time | continue)")
    camera = s.get("camera", [])
    if not isinstance(camera, list):
        out.append(f"{where}.camera: not an array")
    else:
        for i, k in enumerate(camera):
            _check_keyframe(k, f"{where}.camera[{i}]", out)
    if "point" in s:
        _check_pointer(s["point"], f"{where}.point", out)


def check_scene(d, stem: str, loc: set[str], chapters: set[str], tutorials: set[str]) -> list[str]:
    """Problems in one parsed scene file; `stem` is the file name without .json."""
    out: list[str] = []
    where = f"{stem}.json"
    if not isinstance(d, dict):
        return [f"{where}: not a JSON object"]
    for f in sorted(set(d) - SCENE_FIELDS):
        out.append(f"{where}: {f}: unknown field")
    sid = d.get("id")
    if not isinstance(sid, str) or not sid:
        out.append(f"{where}: id: required")
    elif sid != stem:
        out.append(f"{where}: id '{sid}' is not the file stem '{stem}'")
    on = d.get("on", [])
    if not isinstance(on, list):
        out.append(f"{where}: on: not an array")
    else:
        for i, t in enumerate(on):
            if not isinstance(t, str):
                out.append(f"{where}: on[{i}]: not a string")
            elif t == TRIGGER_NEW_GAME:
                pass
            elif t.startswith(TRIGGER_CHAPTER) and len(t) > len(TRIGGER_CHAPTER):
                if t[len(TRIGGER_CHAPTER):] not in chapters:
                    out.append(f"{where}: on[{i}]: chapter unknown: {t[len(TRIGGER_CHAPTER):]!r} (WardensChapters.cs has {', '.join(sorted(chapters)) or 'none'})")
            elif t.startswith(TRIGGER_TUTORIAL) and len(t) > len(TRIGGER_TUTORIAL):
                if t[len(TRIGGER_TUTORIAL):] not in tutorials:
                    out.append(f"{where}: on[{i}]: tutorial unknown: {t[len(TRIGGER_TUTORIAL):]!r}")
            else:
                out.append(f"{where}: on[{i}]: {t!r} (new_game | chapter:<Id> | tutorial:<Id>)")
    for f in SCENE_BOOLS:
        if f in d and not isinstance(d[f], bool):
            out.append(f"{where}: {f}: not a boolean")
    if "say" in d and not isinstance(d["say"], str):
        out.append(f"{where}: say: not a string")
    shots = d.get("shots")
    if not isinstance(shots, list) or not shots:
        out.append(f"{where}: shots: at least one shot required")
    else:
        for i, s in enumerate(shots):
            _check_shot(s, f"{where}: shots[{i}]", loc, out)
    return out


def check(mod: Path, loc: set[str] | None = None, tutorials: set[str] | None = None,
          chapters: set[str] | None = None, chapters_cs: Path = CHAPTERS_CS) -> list[str]:
    """Problems across every Cutscenes/*.json in `mod`. The name tables are read from the mod
    (and the C# chapter table) when the caller does not pass them."""
    if loc is None:
        loc = read_loc_keys(mod)
    if tutorials is None:
        tutorials = read_tutorial_ids(mod)
    if chapters is None:
        chapters = {cid for cid, _, _ in read_chapters(chapters_cs)} if chapters_cs.exists() else set()
    folder = mod / FOLDER
    if not folder.is_dir():
        return [f"{FOLDER}/: folder missing in {mod}"]
    problems: list[str] = []
    seen_ids: dict[str, str] = {}
    for p in sorted(folder.glob("*.json")):
        stem = p.name[:-len(".json")]
        try:
            d = json.loads(p.read_text(encoding="utf-8-sig"))
        except (json.JSONDecodeError, UnicodeDecodeError) as exc:
            problems.append(f"{p.name}: {exc}")
            continue
        problems += check_scene(d, stem, loc, chapters, tutorials)
        sid = d.get("id") if isinstance(d, dict) else None
        if isinstance(sid, str) and sid:
            if sid in seen_ids:
                problems.append(f"{p.name}: duplicate id '{sid}' ({seen_ids[sid]})")
            seen_ids[sid] = p.name
    return problems


def main() -> int:
    mod = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_MOD
    files = sorted((mod / FOLDER).glob("*.json")) if (mod / FOLDER).is_dir() else []
    problems = check(mod)
    print(f"mod: {mod}")
    print(f"cutscenes: {len(files)} ({', '.join(p.name[:-len('.json')] for p in files) or 'none'})")
    if problems:
        print("problems:")
        for p in problems:
            print("  -", p)
        return 1
    print("problems: none")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
