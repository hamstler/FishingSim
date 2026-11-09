#!/usr/bin/env python3
"""
Turns the picked candidates into game assets: downloads each pick at 1920 px, cuts it out (cutout.py), adds the
credit to its JSON and rewrites assets/fish/CREDITS.md.

picks.json: {"species_id": {"index": 3, "grabcut": false, "flip": false, "box": "0.05,0.1,0.95,0.9", "threshold": 38}}
Usage: python3 tools/fish_photos/finalize.py <photos_dir> <picks.json> <assets_dir>
"""
import html
import json
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))
from fetch import get  # noqa: E402

photos, picks_file, assets = sys.argv[1], sys.argv[2], sys.argv[3]
cands = json.load(open(os.path.join(photos, "candidates.json"), encoding="utf-8"))
picks = json.load(open(picks_file, encoding="utf-8"))
here = os.path.dirname(os.path.abspath(__file__))
for sid, pick in picks.items():
    c = cands[sid][pick["index"]]
    src = os.path.join(photos, f"{sid}_full.jpg")
    if not (os.path.exists(src) and os.path.getsize(src) > 0):
        print(f"{sid}: downloading", flush=True)
        open(src, "wb").write(get(c["thumb"]))
        time.sleep(3)
    args = [sys.executable, os.path.join(here, "cutout.py"), src, assets, sid]
    if pick.get("grabcut"):
        args.append("--grabcut")
        if pick.get("box"):
            args += ["--box", pick["box"]]
    if pick.get("flip"):
        args.append("--flip")
    if pick.get("threshold"):
        args += ["--threshold", str(pick["threshold"])]
    subprocess.run(args, check=True)
    meta_path = os.path.join(assets, f"{sid}.json")
    meta = json.load(open(meta_path, encoding="utf-8"))
    meta.update({
        "title": c["title"],
        "author": re.sub(r"<[^>]+>", "", html.unescape(c.get("artist") or "")).strip() or "nieznany autor",
        "licence": c.get("licence") or "",
        "licence_url": c.get("licence_url") or "",
        "source": c.get("source") or "",
    })
    json.dump(meta, open(meta_path, "w", encoding="utf-8"), ensure_ascii=False, indent=0)

# Credits for every asset in the folder.
lines = ["# Fish photograph credits", "", "All photographs come from Wikimedia Commons. The cut-outs are adaptations of them.", ""]
for f in sorted(os.listdir(assets)):
    if f.endswith(".json"):
        m = json.load(open(os.path.join(assets, f), encoding="utf-8"))
        lines.append(f"- `{f[:-5]}`: \"{m.get('title', '')}\" by {m.get('author', '?')}, {m.get('licence', '?')} "
                     f"({m.get('licence_url', '')}), {m.get('source', '')}")
open(os.path.join(assets, "CREDITS.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("credits written")
