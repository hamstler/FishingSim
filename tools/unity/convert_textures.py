#!/usr/bin/env python3
"""Converts loose WebP pictures (assets/characters/textures) for Unity: PNG when they carry transparency (the *_t
pictures), JPEG otherwise, at most --max px on a side; Unity reads them from Resources.

    python3 -I tools/unity/convert_textures.py SRC_DIR DST_DIR [--max 1024]
"""
import argparse
import os
import sys

from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--max", type=int, default=1024)
    a = ap.parse_args()
    os.makedirs(a.dst, exist_ok=True)
    n = 0
    for name in sorted(os.listdir(a.src)):
        if not name.endswith(".webp"):
            continue
        pic = Image.open(os.path.join(a.src, name))
        pic.load()
        if max(pic.size) > a.max:
            k = a.max / max(pic.size)
            pic = pic.resize((round(pic.width * k), round(pic.height * k)), Image.LANCZOS)
        base = os.path.join(a.dst, name[:-5])
        alpha = pic.mode in ("RGBA", "LA") and pic.getchannel("A").getextrema()[0] < 250
        if alpha:
            pic.convert("RGBA").save(base + ".png", optimize=True)
        else:
            pic.convert("RGB").save(base + ".jpg", quality=90)
        n += 1
    print(f"{n} pictures")


if __name__ == "__main__":
    sys.exit(main())
