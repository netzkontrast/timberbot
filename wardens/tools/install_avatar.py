"""Fit a generated character cutout into WardensFullNewGame.png (the New Game / faction-select
screen's big portrait, Timberborn.MainMenuPanels.NewGameFactionPanel -> FactionSpec.NewGameFullAvatar).

    python wardens/tools/install_avatar.py <source.png>

The panel is a fixed 210x280 (selected) / 156x210 (unselected) box with
`-unity-background-scale-mode: scale-to-fit` (Views/MainMenu/MainMenuMiscStyle.uss), so any aspect
ratio letterboxes safely, but the mod's own convention (matches the Leaf Coats file it started from)
is a 420x560 canvas (3:4), transparent background, character filling the frame edge to edge. This
script pads the source to 3:4 (adds transparent canvas, never crops into the character), resizes to
420x560 with alpha premultiplied (avoids black fringing from the fully-transparent-but-black source
pixels a lot of image generators emit), and writes the result plus its .meta.json sidecar.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image

DEST_DIR = Path(__file__).resolve().parents[1] / "src/Sprites/Avatars"
TARGET = (420, 560)   # 3:4, the mod's established NewGameFullAvatar size


def pad_to_ratio(im: Image.Image, ratio: float) -> Image.Image:
    w, h = im.size
    if w / h > ratio:
        new_h = round(w / ratio)
        canvas = Image.new("RGBA", (w, new_h), (0, 0, 0, 0))
        top = round((new_h - h) * 0.2)   # a little more room below the feet than above the head
        canvas.paste(im, (0, top), im)
    else:
        new_w = round(h * ratio)
        canvas = Image.new("RGBA", (new_w, h), (0, 0, 0, 0))
        canvas.paste(im, ((new_w - w) // 2, 0), im)
    return canvas


def premultiplied_resize(im: Image.Image, size: tuple[int, int]) -> Image.Image:
    # multiply each color channel by alpha/255, resize, then divide back out
    import numpy as np
    arr = np.asarray(im).astype("float32")
    rgb, alpha = arr[..., :3], arr[..., 3:4]
    premult_arr = np.concatenate([rgb * (alpha / 255.0), alpha], axis=-1)
    premult_img = Image.fromarray(premult_arr.round().astype("uint8"), "RGBA")
    resized = premult_img.resize(size, Image.LANCZOS)
    out = np.asarray(resized).astype("float32")
    rgb2, alpha2 = out[..., :3], out[..., 3:4]
    safe_alpha = np.clip(alpha2, 1, 255)
    rgb_un = np.clip(rgb2 / (safe_alpha / 255.0), 0, 255)
    rgb_un = np.where(alpha2 > 0, rgb_un, 0)
    final = np.concatenate([rgb_un, alpha2], axis=-1).round().astype("uint8")
    return Image.fromarray(final, "RGBA")


def main() -> None:
    src = Path(sys.argv[1])
    im = Image.open(src).convert("RGBA")
    padded = pad_to_ratio(im, TARGET[0] / TARGET[1])
    final = premultiplied_resize(padded, TARGET)

    dest = DEST_DIR / "WardensFullNewGame.png"
    final.save(dest)
    meta = {"isSprite": False, "isNormalMap": False, "linear": False, "generateMipmap": False,
            "width": TARGET[0], "height": TARGET[1]}
    (DEST_DIR / "WardensFullNewGame.png.meta.json").write_text(
        json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {dest} ({final.size[0]}x{final.size[1]})")


if __name__ == "__main__":
    main()
