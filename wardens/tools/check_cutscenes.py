"""Static checks on the cutscene files: everything a scene resolves by name when it plays.

    python wardens/tools/check_cutscenes.py                 # deployed copy in Documents/Timberborn/Mods/Wardens
    python wardens/tools/check_cutscenes.py wardens/src     # the source tree

The rules are WardensCutsceneScript.Parse's (design/wardens-cutscenes.md §3) plus the names only the
mod knows: caption loc keys against Localizations/enUS*.csv (and their {0}.. placeholders against the
shot's `args`), chapter triggers against the table in src/WardensChapters.cs, tutorial triggers
against Tutorials/*.blueprint.json, `when` conditions against the choice keys some shot defines, and
unknown fields (the parser ignores them; a typo such as "captoin" would otherwise play as a shot
without text). Needs no game files, so it runs in any checkout; validate.py calls check() and merges
the problems. Exit code 1 when there are problems.
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

ANCHORS = {"start", "core", "selection", "bot", "beaver", "grid", "world"}
POINTER_ANCHORS = {"core", "grid", "selection"}          # the anchors with grid coordinates
WAITS = {"time", "continue"}
ARG_NAMES = {"day", "cycle", "cycle_day", "bots", "beavers", "archive", "science"}
ARG_PREFIXES = ("good:", "choice:", "mark:")
SCENE_FIELDS = {"id", "on", "pause", "leave_paused", "restore_camera", "letterbox", "skippable", "say", "shots"}
SCENE_BOOLS = ("pause", "leave_paused", "restore_camera", "letterbox", "skippable")
SHOT_FIELDS = {"id", "caption", "text", "args", "seconds", "wait", "camera", "point", "highlight", "toast", "say",
               "mark", "badtide", "choices", "choice_key", "when"}
KEYFRAME_FIELDS = {"t", "anchor", "x", "y", "z", "offset", "h", "v", "zoom", "dh", "dv", "dzoom"}
POINTER_FIELDS = {"anchor", "x", "y", "z", "offset", "message", "seconds", "color"}
CHOICE_FIELDS = {"id", "caption", "text"}
WHEN_FIELDS = {"choice", "is", "is_not"}
TRIGGER_NEW_GAME = "new_game"
TRIGGER_CHAPTER = "chapter:"
TRIGGER_TUTORIAL = "tutorial:"
PLACEHOLDER = re.compile(r"\{(\d+)\}")


def read_chapters(path: Path = CHAPTERS_CS) -> list[tuple[str, str, list[str]]]:
    """(id, gating tutorial id, template names) per `new WardensChapter(...)` entry in the C# table."""
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"//[^\n]*", "", text)   # strip line comments; the table itself has none inside
    entries = re.findall(r'new WardensChapter\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*new\[\]\s*\{([^}]*)\}\s*\)', text)
    return [(cid, tutorial, re.findall(r'"([^"]+)"', names)) for cid, tutorial, names in entries]


def read_levels(path: Path) -> list[tuple[str, str, str, str, str, bool]]:
    """(id, map name, title, ending tutorial, next, shipped) per `new WardensLevel(...)` in the C# table."""
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"//[^\n]*", "", text)   # strip line comments; the table itself has none inside
    entries = re.findall(
        r'new WardensLevel\(\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,'
        r'\s*(?:"([^"]*)"|null)\s*,\s*(true|false)\s*\)', text)
    return [(lid, name, title, ends, nxt or "", shipped == "true")
            for lid, name, title, ends, nxt, shipped in entries]


def read_loc_texts(mod: Path) -> dict[str, str]:
    """Loc key -> text from the mod's Localizations/enUS*.csv (the vanilla table is validate.py's job)."""
    texts: dict[str, str] = {}
    for p in (mod / "Localizations").glob("enUS*.csv"):
        with open(p, encoding="utf-8-sig", newline="") as fh:
            for row in csv.reader(fh):
                if row:
                    texts[row[0]] = row[1] if len(row) > 1 else ""
    return texts


def read_loc_keys(mod: Path) -> set[str]:
    return set(read_loc_texts(mod))


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


def _is_arg(name) -> bool:
    if not isinstance(name, str):
        return False
    return name in ARG_NAMES or any(name.startswith(p) and len(name) > len(p) for p in ARG_PREFIXES)


def placeholders(text: str) -> int:
    """How many args a text needs: the highest {n} plus one, or 0."""
    found = [int(m) for m in PLACEHOLDER.findall(text)]
    return max(found) + 1 if found else 0


def _check_keyframe(k, where: str, out: list[str]) -> None:
    if not isinstance(k, dict):
        out.append(f"{where}: not an object")
        return
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
        out.append(f"{where}: not an object")
        return
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


def _check_text(s: dict, where: str, loc: set[str], texts: dict[str, str], out: list[str]) -> None:
    """caption (a loc key) or text (a literal), and the args its {0}.. placeholders need."""
    args = s.get("args", [])
    if not isinstance(args, list) or not all(isinstance(a, str) for a in args):
        out.append(f"{where}.args: not an array of strings")
        args = []
    for a in args:
        if not _is_arg(a):
            out.append(f"{where}.args: {a!r} ({' | '.join(sorted(ARG_NAMES))} | good:<Id> | choice:<key> | mark:<name>)")
    caption = s.get("caption")
    text = None
    if isinstance(caption, str):
        if caption not in loc:
            out.append(f"{where}.caption: loc key missing: {caption}")
        else:
            text = texts.get(caption)
    elif isinstance(s.get("text"), str):
        text = s["text"]
    if text is not None:
        need = placeholders(text)
        if need != len(args):
            out.append(f"{where}: {need} placeholder(s) in the text, {len(args)} args")


def _check_shot(s, where: str, loc: set[str], texts: dict[str, str], choice_keys: set[str] | None, out: list[str]) -> None:
    if not isinstance(s, dict):
        out.append(f"{where}: not an object")
        return
    for f in sorted(set(s) - SHOT_FIELDS):
        out.append(f"{where}.{f}: unknown field")
    for f in ("id", "caption", "text", "toast", "say", "mark", "choice_key"):
        if f in s and not isinstance(s[f], str):
            out.append(f"{where}.{f}: not a string")
    for f in ("mark", "choice_key"):
        if isinstance(s.get(f), str) and not s[f]:
            out.append(f"{where}.{f}: empty")
    _check_text(s, where, loc, texts, out)
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
    for f in ("point", "highlight"):
        if f in s:
            _check_pointer(s[f], f"{where}.{f}", out)
    if "badtide" in s:
        b = s["badtide"]
        if not isinstance(b, int) or isinstance(b, bool) or b < 1:
            out.append(f"{where}.badtide: {b!r} (a whole number, 1 or more)")
    choices = s.get("choices", [])
    if not isinstance(choices, list):
        out.append(f"{where}.choices: not an array")
    else:
        seen: set[str] = set()
        for i, c in enumerate(choices):
            cw = f"{where}.choices[{i}]"
            if not isinstance(c, dict):
                out.append(f"{cw}: not an object")
                continue
            for f in sorted(set(c) - CHOICE_FIELDS):
                out.append(f"{cw}.{f}: unknown field")
            cid = c.get("id")
            if not isinstance(cid, str) or not cid:
                out.append(f"{cw}.id: required")
            elif cid in seen:
                out.append(f"{where}.choices: duplicate id '{cid}'")
            else:
                seen.add(cid)
            if "caption" not in c and "text" not in c:
                out.append(f"{cw}: caption or text required")
            _check_text(c, cw, loc, texts, out)
    if "when" in s:
        w = s["when"]
        if not isinstance(w, dict):
            out.append(f"{where}.when: not an object")
        else:
            for f in sorted(set(w) - WHEN_FIELDS):
                out.append(f"{where}.when.{f}: unknown field")
            key = w.get("choice")
            if not isinstance(key, str) or not key:
                out.append(f"{where}.when.choice: required")
            elif choice_keys is not None and key not in choice_keys:
                out.append(f"{where}.when.choice: no shot records a choice under {key!r}")
            if ("is" in w) == ("is_not" in w):
                out.append(f"{where}.when: exactly one of is | is_not")
            for f in ("is", "is_not"):
                if f in w and not isinstance(w[f], str):
                    out.append(f"{where}.when.{f}: not a string")


def choice_keys_of(d, stem: str) -> set[str]:
    """The choice keys a scene's shots record picks under (default <Scene>.<shot id or index>)."""
    keys: set[str] = set()
    if not isinstance(d, dict) or not isinstance(d.get("shots"), list):
        return keys
    sid = d.get("id") if isinstance(d.get("id"), str) else stem
    for i, s in enumerate(d["shots"]):
        if isinstance(s, dict) and isinstance(s.get("choices"), list) and s["choices"]:
            key = s.get("choice_key")
            shot_id = s.get("id") if isinstance(s.get("id"), str) else str(i)
            keys.add(key if isinstance(key, str) and key else f"{sid}.{shot_id}")
    return keys


def check_scene(d, stem: str, loc: set[str], chapters: set[str], tutorials: set[str],
                texts: dict[str, str] | None = None, choice_keys: set[str] | None = None) -> list[str]:
    """Problems in one parsed scene file; `stem` is the file name without .json. `texts` (loc key ->
    text) enables the placeholder count check; `choice_keys` (from every scene) the `when` check."""
    out: list[str] = []
    where = f"{stem}.json"
    texts = texts or {}
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
            _check_shot(s, f"{where}: shots[{i}]", loc, texts, choice_keys, out)
    return out


def check(mod: Path, loc: set[str] | None = None, tutorials: set[str] | None = None,
          chapters: set[str] | None = None, chapters_cs: Path = CHAPTERS_CS) -> list[str]:
    """Problems across every Cutscenes/*.json in `mod`. The name tables are read from the mod
    (and the C# chapter table) when the caller does not pass them."""
    texts = read_loc_texts(mod)
    if loc is None:
        loc = set(texts)
    if tutorials is None:
        tutorials = read_tutorial_ids(mod)
    if chapters is None:
        chapters = {cid for cid, _, _ in read_chapters(chapters_cs)} if chapters_cs.exists() else set()
    folder = mod / FOLDER
    if not folder.is_dir():
        return [f"{FOLDER}/: folder missing in {mod}"]
    problems: list[str] = []
    parsed: list[tuple[Path, str, object]] = []
    for p in sorted(folder.glob("*.json")):
        stem = p.name[:-len(".json")]
        try:
            parsed.append((p, stem, json.loads(p.read_text(encoding="utf-8-sig"))))
        except (json.JSONDecodeError, UnicodeDecodeError) as exc:
            problems.append(f"{p.name}: {exc}")
    choice_keys: set[str] = set()
    for _, stem, d in parsed:
        choice_keys |= choice_keys_of(d, stem)
    seen_ids: dict[str, str] = {}
    for p, stem, d in parsed:
        problems += check_scene(d, stem, loc, chapters, tutorials, texts, choice_keys)
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
