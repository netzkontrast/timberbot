"""Two previews: ASCII for the terminal, PNG for the design docs.

The ASCII view is the one that matters for an agent. I cannot open an image in a terminal session,
so a map I cannot see is a map I cannot judge; a legend-backed character grid tells me at a glance
whether the river runs where the spec said, whether the ruins ring the start, and whether the pad
sits on a shelf or halfway down a cliff.

The PNG writer is stdlib (zlib + a hand-rolled chunk writer), so `preview` never needs Pillow.
"""
from __future__ import annotations

import struct
import zlib
from pathlib import Path

from .build import MapBuild

# Height ramp, low to high. Chosen so water and ground are distinguishable in a plain terminal.
RAMP = ".:-=+*#%@"
LEGEND = [
    ("~", "water (any mask tagged water/badwater/clean)"),
    ("O", "water source"),
    ("A", "starting location"),
    ("n", "ruin / scrap"),
    ("t", "tree or bush"),
    ("o", "other entity"),
    (RAMP, "ground, low to high (left to right)"),
]

_WATER_MASKS = ("water", "badwater", "clean")


def _entity_char(template: str) -> str:
    t = template.lower()
    if "startinglocation" in t:
        return "A"
    if "source" in t:
        return "O"
    if "ruin" in t or "scrap" in t:
        return "n"
    if any(k in t for k in ("pine", "birch", "maple", "oak", "chestnut", "bush", "berry", "grass", "mangrove", "cattail")):
        return "t"
    return "o"


def ascii_map(b: MapBuild, step: int = 1, legend: bool = True) -> str:
    """A character grid of the map. `step` samples every Nth tile so a 256 map fits a terminal."""
    size = b.size
    lo, hi = b.height.min(), b.height.max()
    span = max(hi - lo, 1e-6)
    water = [b.masks[m] for m in _WATER_MASKS if m in b.masks]
    def sampled(v: int) -> int:
        """Nearest sampled column/row, so a mark on an unsampled tile still shows."""
        return min(round(v / step) * step, (size - 1) // step * step)

    marks: dict[tuple[int, int], str] = {}
    for e in b.entities:
        char = _entity_char(e.template)
        # Entities drawn later must not hide the landmarks: A > O > n > t > o.
        cell = (sampled(e.x), sampled(e.y))
        rank = "otnOA"
        if char != "o" and rank.index(char) >= rank.index(marks.get(cell, "o")):
            marks[cell] = char
        else:
            marks.setdefault(cell, char)

    rows = []
    for y in range(0, size, step):
        row = []
        for x in range(0, size, step):
            if (x, y) in marks:
                row.append(marks[(x, y)])
            elif any(m.at(x, y) for m in water):
                row.append("~")
            else:
                t = (b.height.at(x, y) - lo) / span
                row.append(RAMP[min(int(t * (len(RAMP) - 1)), len(RAMP) - 1)])
        rows.append("".join(row))

    header = [f"{b.size}x{b.size}  heights {int(lo)}..{int(hi)}  {len(b.entities)} entities"
              f"{'' if step == 1 else f'  (every {step}th tile)'}",
              "north is up, x runs east, y runs south"]
    out = header + rows
    if legend:
        out += ["", "legend: " + "  ".join(f"{c} {d}" for c, d in LEGEND)]
    return "\n".join(out)


def _rgb(palette: dict, key: str, default: tuple[int, int, int]) -> tuple[int, int, int]:
    value = palette.get(key, default)
    if len(value) != 3:
        raise ValueError(f"palette.{key}: expected [r, g, b], got {value!r}")
    return (int(value[0]), int(value[1]), int(value[2]))


def rgb_image(b: MapBuild, spec: dict | None = None) -> tuple[bytearray, int]:
    """Flat RGB bytes for the map, one pixel per tile (row-major)."""
    spec = spec or {}
    palette = spec.get("palette", {})
    ground_low = _rgb(palette, "ground_low", (88, 78, 66))
    ground_high = _rgb(palette, "ground_high", (158, 138, 114))
    colours = {
        "water": _rgb(palette, "water", (54, 96, 150)),
        "badwater": _rgb(palette, "badwater", (72, 30, 92)),
        "clean": _rgb(palette, "clean", (60, 150, 200)),
    }
    stain = spec.get("soil", {}).get("contamination")
    size = b.size
    lo, hi = b.height.min(), b.height.max()
    span = max(hi - lo, 1e-6)
    px = bytearray(size * size * 3)
    contamination = None
    if stain and stain.get("from") in b.masks:
        from .world import soil_field  # noqa: PLC0415 - avoids an import cycle
        contamination = soil_field(b, stain)

    for y in range(size):
        for x in range(size):
            t = (b.height.at(x, y) - lo) / span
            rgb = tuple(int(a + (c - a) * t) for a, c in zip(ground_low, ground_high, strict=True))
            if contamination is not None:
                c = contamination[y * size + x]
                rgb = (int(rgb[0] * (1 - 0.25 * c) + 40 * c), int(rgb[1] * (1 - 0.45 * c)), int(rgb[2] * (1 - 0.15 * c) + 30 * c))
            for name, colour in colours.items():
                m = b.masks.get(name)
                if m is not None and m.at(x, y):
                    rgb = colour
                    break
            i = (y * size + x) * 3
            px[i], px[i + 1], px[i + 2] = (max(0, min(255, v)) for v in rgb)

    marks = {"A": (0, 229, 255), "O": (220, 90, 230), "n": (200, 200, 200), "t": (70, 140, 60), "o": (240, 200, 80)}
    for e in b.entities:
        colour = marks[_entity_char(e.template)]
        i = (e.y * size + e.x) * 3
        px[i], px[i + 1], px[i + 2] = colour
    return px, size


def write_png(b: MapBuild, path: Path, spec: dict | None = None, scale: int = 6) -> Path:
    """Write the preview as a PNG using only the standard library."""
    px, size = rgb_image(b, spec)
    width = size * scale
    raw = bytearray()
    for y in range(size):
        row = bytearray()
        for x in range(size):
            i = (y * size + x) * 3
            row += px[i:i + 3] * scale
        raw += (b"\x00" + bytes(row)) * scale

    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, width, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
           + chunk(b"IEND", b""))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)
    return path
