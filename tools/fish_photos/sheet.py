#!/usr/bin/env python3
"""
Contact sheet of the downloaded previews (one row per species, candidate index and licence on each), for picking.
Usage: python3 tools/fish_photos/sheet.py <photos_dir> <out.jpg> [ids...]
"""
import glob
import json
import os
import sys

from PIL import Image, ImageDraw

photos, out = sys.argv[1], sys.argv[2]
only = sys.argv[3:]
cands = json.load(open(os.path.join(photos, "candidates.json"), encoding="utf-8"))
ids = only or sorted({os.path.basename(p).rsplit("_", 1)[0] for p in glob.glob(os.path.join(photos, "prev", "*.jpg"))})
W, H = 300, 150
rows = []
for sid in ids:
    files = sorted(glob.glob(os.path.join(photos, "prev", f"{sid}_*.jpg")), key=lambda p: int(p.rsplit("_", 1)[1][:-4]))
    files = [f for f in files if os.path.getsize(f) > 0]
    if files:
        rows.append((sid, files))
sheet = Image.new("RGB", (W * 6, H * max(len(rows), 1)), (25, 25, 25))
d = ImageDraw.Draw(sheet)
for r, (sid, files) in enumerate(rows):
    d.text((5, r * H + 5), sid, fill=(255, 255, 0))
    for c, f in enumerate(files[:5]):
        idx = int(f.rsplit("_", 1)[1][:-4])
        im = Image.open(f).convert("RGB")
        im.thumbnail((W - 6, H - 22))
        x, y = (c + 1) * W, r * H
        sheet.paste(im, (x + 3, y + 18))
        lic = (cands[sid][idx].get("licence") or "")[:14]
        d.text((x + 4, y + 3), f"#{idx} {lic}", fill=(180, 255, 180))
sheet.save(out, quality=90)
print(f"{len(rows)} species")
