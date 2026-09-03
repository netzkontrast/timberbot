"""Derive the Wardens' 2D art from Leaf Coats (Bobingabout), shifted to the "blue light of knowledge".

    uv run --with pillow --with numpy python wardens/tools/recolor_assets.py [--src <leafcoats version dir>]

Reads the loose PNGs of the Leaf Coats workshop mod (avatars, beaver + bot skins, banners,
carrying/zipline textures), recolors them (greens -> cyan-blue, browns -> slate, cool cyan
glow on highlights, slightly desaturated steel look) and writes them into wardens/src under
Wardens names. Also renders a before/after contact sheet for review.

Licensing: the sources are Bobingabout's work. Private derivation for this local project;
redistribution needs the author's permission (see wardens/src/Sprites/ATTRIBUTION.md).
"""
from __future__ import annotations

import argparse
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image

DEFAULT_SRC = Path("F:/Steam/steamapps/workshop/content/1062090/3552203634/version-1.1")
DST = Path(__file__).resolve().parents[1] / "src"
SHEET = Path(__file__).resolve().parents[1] / "tools" / "recolor-sheet.png"

# (source relative path, destination relative path)
FILES = [
    ("Sprites/Avatars/LeafCoatsAdult.png", "Sprites/Avatars/WardensAdult.png"),
    ("Sprites/Avatars/LeafCoatsChild.png", "Sprites/Avatars/WardensChild.png"),
    ("Sprites/Avatars/LeafCoatsBot.png", "Sprites/Avatars/WardensBot.png"),
    ("Sprites/Avatars/LeafCoatsContaminatedAdult.png", "Sprites/Avatars/WardensContaminatedAdult.png"),
    ("Sprites/Avatars/LeafCoatsContaminatedChild.png", "Sprites/Avatars/WardensContaminatedChild.png"),
    ("Sprites/Avatars/LeafCoatsFullNewGame.png", "Sprites/Avatars/WardensFullNewGame.png"),
    ("Sprites/Avatars/LeafCoatsLogo.png", "Sprites/Avatars/WardensLogo.png"),
    ("Materials/Bots/LeafCoats/Bot.LeafCoats.png", "Materials/Bots/Wardens/Bot.Wardens.png"),
    ("Materials/Textures/BeaverCarryingModels.LeafCoats.png", "Materials/Textures/BeaverCarryingModels.Wardens.png"),
    ("Materials/Textures/ZiplineCable.LeafCoats.png", "Materials/Textures/ZiplineCable.Wardens.png"),
] + [
    (f"Materials/Beavers/LeafCoats/Adult/BeaverAdult{i}.LeafCoats.png", f"Materials/Beavers/Wardens/Adult/BeaverAdult{i}.Wardens.png")
    for i in range(1, 6)
] + [
    (str(p.relative_to(DEFAULT_SRC)).replace(chr(92), "/"),
     "Materials/Banners/Wardens/" + p.name.replace("LeafCoats", "Wardens"))
    for p in sorted(DEFAULT_SRC.glob("Materials/Banners/**/*.png"))
]


def recolor(img: Image.Image) -> Image.Image:
    """Greens -> cyan-blue, warm browns -> slate, keep darks, add a cool glow to highlights."""
    rgba = np.asarray(img.convert("RGBA")).astype(np.float32) / 255.0
    rgb, a = rgba[..., :3], rgba[..., 3:4]
    hsv = np.asarray(Image.fromarray((rgb * 255).astype(np.uint8)).convert("HSV")).astype(np.float32)
    h, s, v = hsv[..., 0] * (360.0 / 255.0), hsv[..., 1] / 255.0, hsv[..., 2] / 255.0

    new_h = h.copy()
    new_s = s.copy()
    green = (h >= 55) & (h < 175)
    warm = (h >= 10) & (h < 55)
    red = (h < 10) | (h >= 330)
    # leaves/moss -> the data-light: cyan leaning to blue
    new_h[green] = 196 + (h[green] - 55) * (18.0 / 120.0)          # 196..214
    # wood/fur browns -> cold slate, much less saturated
    new_h[warm] = 212
    new_s[warm] = s[warm] * 0.35
    # reds/oranges -> muted blue-violet (warning stripes stay warm-ish via low saturation)
    new_h[red] = 225
    new_s[red] = s[red] * 0.5
    # everything else drifts a little toward 205
    other = ~(green | warm | red)
    new_h[other] = h[other] + (205 - h[other]) * 0.35
    new_s = np.clip(new_s * 0.9, 0, 1)

    hsv2 = np.stack([new_h * (255.0 / 360.0), new_s * 255.0, v * 255.0], axis=-1).astype(np.uint8)
    out = np.asarray(Image.fromarray(hsv2, "HSV").convert("RGB")).astype(np.float32) / 255.0

    # cool glow on highlights: blend bright pixels toward pale cyan
    glow = np.clip((v - 0.72) / 0.28, 0, 1)[..., None] * 0.28
    cyan = np.array([0.62, 0.95, 1.0], dtype=np.float32)
    out = out * (1 - glow) + cyan * glow
    # a touch more contrast in the shadows so the metal reads as metal
    out = np.clip((out - 0.5) * 1.06 + 0.5, 0, 1)

    result = np.concatenate([out, a], axis=-1)
    return Image.fromarray((result * 255).round().astype(np.uint8), "RGBA")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    args = ap.parse_args()

    pairs = []
    for src_rel, dst_rel in FILES:
        src = args.src / src_rel
        dst = DST / dst_rel
        if not src.exists():
            print("missing:", src)
            continue
        dst.parent.mkdir(parents=True, exist_ok=True)
        before = Image.open(src)
        after = recolor(before)
        after.save(dst, optimize=True)
        meta = src.with_name(src.name + ".meta.json")
        if meta.exists():
            shutil.copyfile(meta, dst.with_name(dst.name + ".meta.json"))
        pairs.append((dst_rel, before.convert("RGBA"), after))
        print(f"{src_rel} -> {dst_rel} {after.size}")

    # contact sheet: one row per file, before | after, thumbnails
    thumb = 160
    rows = len(pairs)
    sheet = Image.new("RGBA", (thumb * 2 + 20, rows * (thumb + 6) + 6), (30, 26, 34, 255))
    for i, (name, before, after) in enumerate(pairs):
        for j, im in enumerate((before, after)):
            t = im.copy()
            t.thumbnail((thumb, thumb))
            sheet.alpha_composite(t, (6 + j * (thumb + 8), 6 + i * (thumb + 6)))
    sheet.save(SHEET)
    print("sheet:", SHEET, "files:", len(pairs))

    attribution = DST / "Sprites" / "ATTRIBUTION.md"
    attribution.write_text(
        "# Attribution\n\n"
        "The Wardens' 2D art in Sprites/ and Materials/ is derived from the Leaf Coats mod by\n"
        "Bobingabout (Steam Workshop 3552203634), recolored by wardens/tools/recolor_assets.py.\n"
        "Private derivation for this project. Do not redistribute without the author's permission.\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
