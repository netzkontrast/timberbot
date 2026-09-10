"""Compose the Mod Manager thumbnail (src/thumbnail.png, 512x512) from the faction's sprites.

    python wardens/tools/gen_thumbnail.py            # writes src/thumbnail.png
    python wardens/tools/gen_thumbnail.py --check    # exit 1 when src/thumbnail.png is not what it would write
    python wardens/tools/gen_thumbnail.py --probe    # print the sprites' sizes and edge pixels, write nothing

The bot avatar (Sprites/Avatars/WardensBot.png, 512x512, on its own dark ground) is the picture; the
faction logo (Sprites/Avatars/WardensLogo.png, 98x98) sits in the bottom-right corner on a dark disc so
it reads at the Mod Manager's tile size. Pure Python (zlib + struct): the PNGs are decoded and encoded
here, without Pillow, so the file is reproducible in any checkout and the build needs nothing new.
Wardens.csproj copies thumbnail.png into the mod folder; tools/package.py puts it in the release ZIP.
Re-run after tools/recolor_assets.py changes the sprites. The sprites are derived from Leaf Coats
(Sprites/ATTRIBUTION.md); the thumbnail carries the same restriction.
"""
from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

SRC = Path(__file__).resolve().parents[1] / "src"
BOT = SRC / "Sprites" / "Avatars" / "WardensBot.png"
LOGO = SRC / "Sprites" / "Avatars" / "WardensLogo.png"
OUT = SRC / "thumbnail.png"

SIZE = 512
PAD = 18            # from the tile's edge to the disc
RING = 8            # the disc's margin around the logo
DISC = (8, 11, 14, 224)          # near-black, a touch of blue, mostly opaque
SUPERSAMPLE = 4                  # per axis, for the disc's anti-aliased edge

Image = tuple[int, int, list[bytearray]]   # width, height, rows of RGBA bytes


# ---- PNG in / out (8-bit RGBA, non-interlaced) ----------------------------------------------------

def read_png(path: Path) -> Image:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path.name}: not a PNG")
    pos = 8
    width = height = 0
    idat = bytearray()
    while pos < len(data):
        length, ctype = struct.unpack(">I4s", data[pos:pos + 8])
        chunk = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if ctype == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", chunk)
            if depth != 8 or color != 6 or interlace != 0:
                raise ValueError(f"{path.name}: need 8-bit RGBA, non-interlaced (depth {depth}, color type {color}, interlace {interlace})")
        elif ctype == b"IDAT":
            idat += chunk
        elif ctype == b"IEND":
            break
    raw = zlib.decompress(bytes(idat))
    stride = width * 4
    rows: list[bytearray] = []
    prev = bytearray(stride)
    at = 0
    for _ in range(height):
        filt = raw[at]
        line = bytearray(raw[at + 1:at + 1 + stride])
        at += 1 + stride
        unfilter(filt, line, prev, 4)
        rows.append(line)
        prev = line
    return width, height, rows


def unfilter(filt: int, line: bytearray, prev: bytearray, bpp: int) -> None:
    n = len(line)
    if filt == 0:
        return
    if filt == 1:
        for i in range(bpp, n):
            line[i] = (line[i] + line[i - bpp]) & 0xFF
    elif filt == 2:
        for i in range(n):
            line[i] = (line[i] + prev[i]) & 0xFF
    elif filt == 3:
        for i in range(n):
            left = line[i - bpp] if i >= bpp else 0
            line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
    elif filt == 4:
        for i in range(n):
            a = line[i - bpp] if i >= bpp else 0
            b = prev[i]
            c = prev[i - bpp] if i >= bpp else 0
            p = a + b - c
            pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
            pred = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
            line[i] = (line[i] + pred) & 0xFF
    else:
        raise ValueError(f"unknown PNG filter {filt}")


def png_bytes(image: Image) -> bytes:
    width, height, rows = image
    raw = b"".join(b"\x00" + bytes(row) for row in rows)

    def chunk(ctype: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + ctype + body + struct.pack(">I", zlib.crc32(ctype + body) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


# ---- the composition ---------------------------------------------------------------------------

def blend(dst: bytearray, x: int, r: int, g: int, b: int, a: int) -> None:
    """Source-over onto pixel x of a row."""
    if a <= 0:
        return
    i = x * 4
    da = dst[i + 3]
    out_a = a + da * (255 - a) // 255
    if out_a == 0:
        return
    for k, s in enumerate((r, g, b)):
        dst[i + k] = (s * a + dst[i + k] * da * (255 - a) // 255) // out_a
    dst[i + 3] = out_a


def disc_coverage(x: int, y: int, cx: float, cy: float, radius: float) -> float:
    """Fraction of pixel (x, y) inside the disc, by supersampling."""
    inside = 0
    n = SUPERSAMPLE
    for sy in range(n):
        py = y + (sy + 0.5) / n
        for sx in range(n):
            px = x + (sx + 0.5) / n
            if (px - cx) ** 2 + (py - cy) ** 2 <= radius * radius:
                inside += 1
    return inside / (n * n)


def compose(bot: Image, logo: Image) -> Image:
    width, height, rows = bot
    if (width, height) != (SIZE, SIZE):
        raise ValueError(f"the bot avatar must be {SIZE}x{SIZE}, is {width}x{height}")
    lw, lh, lrows = logo
    rows = [bytearray(r) for r in rows]
    radius = max(lw, lh) / 2 + RING
    cx = width - PAD - radius
    cy = height - PAD - radius
    x0, x1 = int(cx - radius) - 1, int(cx + radius) + 2
    y0, y1 = int(cy - radius) - 1, int(cy + radius) + 2
    for y in range(max(0, y0), min(height, y1)):
        for x in range(max(0, x0), min(width, x1)):
            cov = disc_coverage(x, y, cx, cy, radius)
            if cov > 0:
                blend(rows[y], x, DISC[0], DISC[1], DISC[2], round(DISC[3] * cov))
    # The logo's own alpha, and a circular mask on top in case its corners are opaque.
    corner_opaque = lrows[0][3] > 0
    lx0 = int(round(cx - lw / 2))
    ly0 = int(round(cy - lh / 2))
    for ly in range(lh):
        src = lrows[ly]
        for lx in range(lw):
            a = src[lx * 4 + 3]
            if corner_opaque:
                a = round(a * disc_coverage(lx, ly, lw / 2, lh / 2, min(lw, lh) / 2))
            blend(rows[ly0 + ly], lx0 + lx, src[lx * 4], src[lx * 4 + 1], src[lx * 4 + 2], a)
    return width, height, rows


def render() -> bytes:
    return png_bytes(compose(read_png(BOT), read_png(LOGO)))


def main() -> int:
    if "--probe" in sys.argv:
        for path in (BOT, LOGO):
            w, h, rows = read_png(path)
            px = lambda x, y: tuple(rows[y][x * 4:x * 4 + 4])  # noqa: E731
            print(f"{path.name}: {w}x{h} corner {px(0, 0)} edge-mid {px(0, h // 2)} center {px(w // 2, h // 2)}")
        return 0
    data = render()
    if "--check" in sys.argv:
        same = OUT.exists() and OUT.read_bytes() == data
        print(f"{OUT.relative_to(SRC.parent)}: {'up to date' if same else 'differs (run gen_thumbnail.py)'}")
        return 0 if same else 1
    OUT.write_bytes(data)
    print(f"wrote {OUT.relative_to(SRC.parent)} ({len(data)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
