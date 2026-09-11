"""Writing the `.timber`: world.json, map_metadata.json, version.txt, map_thumbnail.jpg, zipped.

The format is the one verified in design/wardens-wasteland.md. Two choices carried over from
`gen_map.py` on purpose:

  * the file claims the GameVersion whose layout was verified, so the game runs its migration on
    load instead of trusting an unverified newer layout;
  * water starts dry unless the spec has a `[water]` section: then `fill` pre-fills water masks in
    the column encoding of the game's own serializer (decompiled 1.1.2.4
    `WaterColumnPackedListSerializer`: "0" for a dry column, else
    depth:contamination:overflow:floor:oldDepth). The sources take over from there.

The zip is written with a fixed timestamp so the same spec produces the same bytes.
"""
from __future__ import annotations

import json
import zipfile
from pathlib import Path

from .build import LAYERS, MapBuild

# A 1x1 white baseline JPEG: a valid placeholder when Pillow is not installed. The map browser
# shows it as a blank tile; everything else about the map is unaffected.
MINIMAL_JPEG = bytes.fromhex(
    "ffd8ffe000104a46494600010101004800480000ffdb004300" + "ff" * 64 +
    "ffc0000b080001000101011100ffc40014000100000000000000000000000000000003"
    "ffc40014100100000000000000000000000000000000ffda0008010100003f0037ffd9")

ZIP_STAMP = (2026, 1, 1, 0, 0, 0)


def soil_field(b: MapBuild, rule: dict | None) -> list[float]:
    """Per-cell 0..1 from distance to a mask: `{from = "badwater", reach = 6.0, offset = 2.5}`."""
    n = b.size * b.size
    if not rule:
        return [0.0] * n
    source = rule.get("from")
    if source is None:
        return [float(rule.get("value", 0.0))] * n
    reach = float(rule.get("reach", 6.0))
    offset = float(rule.get("offset", 0.0))
    peak = float(rule.get("peak", 1.0))
    dist = b.distance_to(source, max_dist=offset + reach + 2)
    return [max(0.0, min(peak, peak * (1.0 - (d - offset) / reach))) for d in dist.cells]


def _floats(values: list[float]) -> str:
    return " ".join("0" if v == 0 else f"{v:.4g}" for v in values)


def water_fill(b: MapBuild, water: dict | None) -> list[tuple[float, float]]:
    """(depth, contamination) per cell from `[water] fill`: each entry names a water mask (`tag`) and
    fills its cells to an absolute surface `level`, or `depth` above each cell's floor, with the
    water's `contamination` (1 for badwater, 0 for clean). Later entries win on shared cells; a cell
    whose floor is at or above the level stays dry. No `[water]` section: every column dry."""
    n = b.size * b.size
    out = [(0.0, 0.0)] * n
    if not water:
        return out
    from .build import SpecError  # noqa: PLC0415 - keeps the module's import surface as it was
    for k, entry in enumerate(water.get("fill", [])):
        where = f"water.fill[{k}]"
        tag = entry.get("tag")
        mask = b.masks.get(tag) if isinstance(tag, str) else None
        if mask is None:
            raise SpecError(f"{where}: tag {tag!r} is not a water mask (known: {', '.join(sorted(b.masks))})")
        if ("level" in entry) == ("depth" in entry):
            raise SpecError(f"{where}: exactly one of level | depth")
        contamination = float(entry.get("contamination", 0.0))
        if not 0.0 <= contamination <= 1.0:
            raise SpecError(f"{where}: contamination {contamination} outside 0..1")
        for x, y in mask.points():
            floor = int(b.h(x, y))
            depth = float(entry["level"]) - floor if "level" in entry else float(entry["depth"])
            if depth > 0.0:
                out[y * b.size + x] = (round(depth, 4), contamination)
    return out


def _water_columns(b: MapBuild, fill: list[tuple[float, float]]) -> str:
    cols = []
    for i, (depth, contamination) in enumerate(fill):
        if depth <= 0.0:
            cols.append("0")
            continue
        floor = int(b.height.cells[i])
        d = f"{depth:.4g}"
        cols.append(f"{d}:{contamination:.4g}:0:{floor}:{d}")
    return " ".join(cols)


def world_json(b: MapBuild, spec: dict) -> dict:
    size = b.size
    n = size * size
    soil = spec.get("soil", {})
    contamination = soil_field(b, soil.get("contamination"))
    moisture = soil_field(b, soil.get("moisture"))
    camera = spec.get("thumbnail_camera", {})
    return {
        "GameVersion": spec.get("game_version", "0.7.10.0"),
        "Timestamp": spec.get("timestamp", "2026-01-01 00:00:00"),
        "Singletons": {
            "MapSize": {"Size": {"X": size, "Y": size}},
            "TerrainMap": {"Voxels": {"Array": b.voxels()}},
            "WaterMapNew": {
                "Levels": 1,
                "WaterColumns": {"Array": _water_columns(b, water_fill(b, spec.get("water")))},
                "ColumnOutflows": {"Array": " ".join(["0|0:0|0:0|0:0|0"] * n)},
            },
            "WaterEvaporationMap": {"Levels": 1, "EvaporationModifiers": {"Array": " ".join(["1"] * n)}},
            "SoilMoistureSimulator": {"Size": 1, "MoistureLevels": {"Array": _floats(moisture)}},
            "SoilContaminationSimulator": {
                "Size": 1,
                "ContaminationCandidates": {"Array": _floats(contamination)},
                "ContaminationLevels": {"Array": _floats(contamination)},
            },
            "HazardousWeatherHistory": {"HistoryData": []},
            "MapThumbnailCameraMover": {"CurrentConfiguration": {
                "Position": {"X": camera.get("x", size / 2.0),
                             "Y": camera.get("y", round(size * 0.64, 2)),
                             "Z": camera.get("z", -size / 2.0)},
                "Rotation": {"X": 0.342020124, "Y": 0.0, "Z": 0.0, "W": 0.9396926},
                "ShadowDistance": float(camera.get("shadow_distance", 150.0))}},
        },
        "Entities": [e.to_json(b.seed) for e in b.entities],
    }


def metadata_json(b: MapBuild, spec: dict) -> dict:
    return {
        "Width": b.size,
        "Height": b.size,
        "MapNameLocKey": spec.get("name_loc_key", ""),
        "MapDescriptionLocKey": spec.get("description_loc_key", ""),
        "MapDescription": spec.get("description", ""),
        "IsRecommended": bool(spec.get("recommended", False)),
        "IsDev": bool(spec.get("dev", False)),
    }


def thumbnail(b: MapBuild, spec: dict) -> bytes:
    """A real JPEG rendered from the preview when Pillow is installed, else the blank placeholder."""
    try:
        from PIL import Image  # noqa: PLC0415 - optional dependency, checked at call time
    except ImportError:
        return MINIMAL_JPEG
    from .preview import rgb_image  # noqa: PLC0415 - avoids a cycle at import time
    pixels, size = rgb_image(b, spec)
    im = Image.frombytes("RGB", (size, size), bytes(pixels)).resize((size * 4, size * 4), Image.NEAREST)
    import io  # noqa: PLC0415
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=85)
    return buf.getvalue()


def write_timber(b: MapBuild, spec: dict, out: Path) -> Path:
    out.parent.mkdir(parents=True, exist_ok=True)
    payload = (
        ("world.json", json.dumps(world_json(b, spec), separators=(",", ":")).encode("utf-8")),
        ("map_metadata.json", json.dumps(metadata_json(b, spec), separators=(",", ":")).encode("utf-8")),
        ("version.txt", str(spec.get("game_version", "0.7.10.0")).encode("ascii")),
        ("map_thumbnail.jpg", thumbnail(b, spec)),
    )
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for name, data in payload:
            info = zipfile.ZipInfo(name, date_time=ZIP_STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, data)
    return out


def read_timber(path: Path) -> tuple[dict, dict, set[str]]:
    with zipfile.ZipFile(path) as z:
        names = set(z.namelist())
        world = json.loads(z.read("world.json")) if "world.json" in names else {}
        meta = json.loads(z.read("map_metadata.json")) if "map_metadata.json" in names else {}
        if "map_thumbnail.jpg" in names and z.read("map_thumbnail.jpg")[:2] != b"\xff\xd8":
            names.add("!thumbnail-not-jpeg")
    return world, meta, names


__all__ = ["LAYERS", "write_timber", "read_timber", "world_json", "metadata_json", "thumbnail"]
