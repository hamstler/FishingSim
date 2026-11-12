#!/usr/bin/env python3
"""
Contact sheet of the downloaded previews (species name, then its candidates with index and licence, 8 to a row),
for picking.
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
W, H, COLS = 250, 140, 8
rows = []                                           # (species, files) for each row of the sheet
for sid in ids:
    files = sorted(glob.glob(os.path.join(photos, "prev", f"{sid}_*.jpg")), key=lambda p: int(p.rsplit("_", 1)[1][:-4]))
    files = [f for f in files if os.path.basename(f).rsplit("_", 1)[0] == sid and os.path.getsize(f) > 0]
    for i in range(0, len(files), COLS):
        rows.append((sid, i == 0, files[i:i + COLS]))
sheet = Image.new("RGB", (W * (COLS + 1) - 150, H * max(len(rows), 1)), (25, 25, 25))
d = ImageDraw.Draw(sheet)
for r, (sid, first, files) in enumerate(rows):
    if first:
        d.text((5, r * H + 5), sid, fill=(255, 255, 0))
    for c, f in enumerate(files):
        idx = int(f.rsplit("_", 1)[1][:-4])
        try:
            im = Image.open(f).convert("RGB")
        except OSError:
            continue
        im.thumbnail((W - 6, H - 20))
        x, y = 100 + c * W, r * H
        sheet.paste(im, (x + 3, y + 17))
        lic = cands[sid][idx].get("licence") or ""
        d.text((x + 4, y + 3), f"#{idx} {lic[:16]}", fill=(180, 255, 180))
sheet.save(out, quality=90)
print(f"{len(rows)} rows")
