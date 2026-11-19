#!/usr/bin/env python3
"""Grass clump cards from Poly Haven's grass_medium_01 (CC0, https://polyhaven.com/a/grass_medium_01).

    python3 -I tools/assets/grass_cards.py

The model's own atlas carries, below its blade strips, photographed grass clumps drawn as billboards with an alpha
mask. Four of them are cut out (CLUMPS, in the 2K atlas's pixels), each scaled to fit a 512 px cell of a 2x2 atlas
standing on the cell's bottom edge and centred, the colour bled out under the transparent parts (no dark fringes in
the mip levels), and written as assets/textures/grass_medium_01/cards.webp (RGBA) with info.json (credits and each
cell's used width and height as fractions of the cell: the game shapes the cards from them). Graphics/GrassField draws
the meadow with them (TreeFactory.MeadowCards + shaders/grass_card.gdshader).
"""
import io
import json
import os
import urllib.request

import numpy as np
from PIL import Image, ImageFilter

API = "https://api.polyhaven.com"
UA = {"User-Agent": "FishingSim-asset-fetch/1.0"}
ID = "grass_medium_01"
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "textures", ID)
CELL = 512
# x0, y0, x1, y1 of each clump in the 2K atlas (the strip of billboards at its bottom; the stalk columns under them
# are left out).
CLUMPS = [(403, 1520, 962, 1798), (1198, 1545, 1618, 1757), (488, 1832, 1032, 2000), (1176, 1818, 1622, 2040)]


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
        return r.read()


def bleed(rgba):
    """Spreads the colour of the opaque pixels into the transparent ones (wider and wider blurs, each filling only what
    is still empty), so the mip levels never average the blades with black."""
    a = np.asarray(rgba.getchannel("A"), dtype=np.float32) / 255.0
    rgb = np.asarray(rgba.convert("RGB"), dtype=np.float32)
    known = a > 0.1
    out = np.where(known[..., None], rgb, 0.0)
    for r in (2, 4, 8, 16, 32, 64, 128):
        w = known.astype(np.float32)
        def blur(ch):
            # 8-bit "L" pictures (Pillow blurs no float ones); the ratio below cancels the scale.
            return np.asarray(Image.fromarray(np.clip(ch, 0, 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(r)), dtype=np.float32)
        wb = blur(w * 255.0) / 255.0
        fill = (~known) & (wb > 0.004)
        for c in range(3):
            cb = blur(out[..., c] * w) / 255.0 * 255.0
            out[..., c] = np.where(fill, cb / np.maximum(wb, 1e-6), out[..., c])
        known = known | fill
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    img.putalpha(rgba.getchannel("A"))
    return img


def main():
    files = json.loads(get(f"{API}/files/{ID}"))
    info = json.loads(get(f"{API}/info/{ID}"))
    diff = Image.open(io.BytesIO(get(files["Diffuse"]["2k"]["png"]["url"]))).convert("RGB")
    alpha = Image.open(io.BytesIO(get(files["Alpha"]["2k"]["png"]["url"])))
    if alpha.mode.startswith("I"):
        alpha = alpha.point(lambda v: v / 256).convert("L")
    else:
        alpha = alpha.convert("L")
    atlas = Image.new("RGBA", (CELL * 2, CELL * 2), (0, 0, 0, 0))
    cells = []
    for i, box in enumerate(CLUMPS):
        clump = diff.crop(box).convert("RGBA")
        clump.putalpha(alpha.crop(box))
        w, h = clump.size
        s = min((CELL - 8) / w, (CELL - 4) / h)
        clump = clump.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
        cx, cy = (i % 2) * CELL, (i // 2) * CELL
        atlas.alpha_composite(clump, (cx + (CELL - clump.width) // 2, cy + CELL - clump.height))
        cells.append({"width": clump.width / CELL, "height": clump.height / CELL})
    atlas = bleed(atlas)
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "cards.webp")
    atlas.save(path, "WEBP", quality=92, method=6)
    with open(path + ".import", "w") as f:
        f.write('[remap]\n\nimporter="keep"\n')
    with open(os.path.join(OUT, "info.json"), "w") as f:
        json.dump({"id": ID, "name": info["name"], "authors": list(info["authors"]), "licence": "CC0 1.0 (public domain)",
                   "source": f"https://polyhaven.com/a/{ID}", "cells": cells}, f, indent=1)
    print(path, [f"{c['width']:.2f}x{c['height']:.2f}" for c in cells])


if __name__ == "__main__":
    main()
