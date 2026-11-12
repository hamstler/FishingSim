#!/usr/bin/env python3
"""Fetch CC0 textures from Poly Haven (https://polyhaven.com, public domain: free for any use, also commercial).

    python3 -I tools/assets/polyhaven.py [--res 2k] [--disp] [--grey] id [id ...]

For every id writes assets/textures/<id>/diff.webp (colour), nor.webp (OpenGL normal map), with --disp also
disp.webp (height, 1k), and info.json (name, authors, real-world size in metres, licence, source). --grey stores the
colour desaturated, so the game can tint it (painted boards, linen). A Godot "keep" .import file sits next to every
picture: the game decodes and block-compresses them itself (Graphics/PhotoTextures), so the editor must not import
them. assets/textures/CREDITS.md lists everything.
"""
import argparse
import io
import json
import os
import sys
import urllib.request

from PIL import Image, ImageOps

API = "https://api.polyhaven.com"
UA = {"User-Agent": "FishingSim-asset-fetch/1.0"}
ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "textures")


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
        return r.read()


def keep(path):
    with open(path + ".import", "w") as f:
        f.write('[remap]\n\nimporter="keep"\n')


def save_webp(img, path, quality):
    img.save(path, "WEBP", quality=quality, method=6)
    keep(path)


def fetch(tid, res, disp, grey):
    files = json.loads(get(f"{API}/files/{tid}"))
    info = json.loads(get(f"{API}/info/{tid}"))
    out = os.path.join(ROOT, tid)
    os.makedirs(out, exist_ok=True)
    diff = Image.open(io.BytesIO(get(files["Diffuse"][res]["jpg"]["url"]))).convert("RGB")
    if grey:
        # Keep the light and shade, drop the hue: the material colour paints it.
        g = ImageOps.grayscale(diff)
        lo, hi = g.getextrema()
        diff = Image.merge("RGB", (g, g, g))
    save_webp(diff, os.path.join(out, "diff.webp"), 88)
    nor = Image.open(io.BytesIO(get(files["nor_gl"][res]["jpg"]["url"]))).convert("RGB")
    save_webp(nor, os.path.join(out, "nor.webp"), 90)
    if disp:
        d = Image.open(io.BytesIO(get(files["Displacement"]["1k"]["jpg"]["url"]))).convert("L")
        save_webp(Image.merge("RGB", (d, d, d)), os.path.join(out, "disp.webp"), 90)
    dims = info.get("dimensions") or [2000, 2000]
    meta = {
        "id": tid,
        "name": info.get("name", tid),
        "authors": list((info.get("authors") or {}).keys()),
        "metres": [dims[0] / 1000.0, dims[1] / 1000.0],
        "licence": "CC0 1.0 (public domain)",
        "source": f"https://polyhaven.com/a/{tid}",
        "grey": grey,
    }
    with open(os.path.join(out, "info.json"), "w") as f:
        json.dump(meta, f, indent=1, ensure_ascii=False)
    return meta


def write_credits():
    rows = []
    for tid in sorted(os.listdir(ROOT)):
        p = os.path.join(ROOT, tid, "info.json")
        if os.path.exists(p):
            m = json.load(open(p))
            rows.append(f"| {m['id']} | {m['name']} | {', '.join(m['authors'])} | {m['licence']} | {m['source']} |")
    with open(os.path.join(ROOT, "CREDITS.md"), "w") as f:
        f.write("# Tekstury z Poly Haven\n\nWszystkie na licencji CC0 1.0 (domena publiczna): wolno ich używać w każdym celu, także komercyjnym, "
                "bez opłat i bez obowiązku podawania autora. Mimo to dziękujemy autorom:\n\n")
        f.write("| id | nazwa | autorzy | licencja | źródło |\n|---|---|---|---|---|\n")
        f.write("\n".join(rows) + "\n")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("ids", nargs="+")
    ap.add_argument("--res", default="2k")
    ap.add_argument("--disp", action="store_true")
    ap.add_argument("--grey", action="store_true")
    a = ap.parse_args()
    for tid in a.ids:
        m = fetch(tid, a.res, a.disp, a.grey)
        sizes = sum(os.path.getsize(os.path.join(ROOT, tid, f)) for f in os.listdir(os.path.join(ROOT, tid)))
        print(f"{tid}: {m['name']} {m['metres']} m, {sizes / 1e6:.1f} MB", flush=True)
    write_credits()


if __name__ == "__main__":
    sys.exit(main())
