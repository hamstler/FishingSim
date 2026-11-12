#!/usr/bin/env python3
"""
Turns the picked reference photos into the game's measurements: downloads each pick at 1920 px, cuts it out into the
work folder (cutout.py: <id>.webp and the body outline in <id>.json), measures its colours (measure.py), adds the
credit, copies the JSON into the assets folder and rewrites its CREDITS.md. The photographs stay in the work folder.

picks.json: {"species_id": {"index": 3, "from": "other_species_id", "isnet": true, "grabcut": false, "flip": false, "box": "0.05,0.1,0.95,0.9",
                            "threshold": 38}}
Usage: ISNET_MODEL=isnet-general-use.onnx python3 tools/fish_photos/finalize.py <photos_dir> <picks.json> <work_dir> <assets_dir>
"""
import html
import json
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))
from fetch import get, thumb_url  # noqa: E402

photos, picks_file, work, assets = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
os.makedirs(work, exist_ok=True)
cands = json.load(open(os.path.join(photos, "candidates.json"), encoding="utf-8"))
picks = json.load(open(picks_file, encoding="utf-8"))
here = os.path.dirname(os.path.abspath(__file__))
for sid, pick in picks.items():
    origin = pick.get("from", sid)                     # a photo found under another species' search
    c = cands[origin][pick["index"]]
    src = os.path.join(photos, f"{origin}_{pick['index']}_full.jpg")
    if not (os.path.exists(src) and os.path.getsize(src) > 0):
        print(f"{sid}: downloading", flush=True)
        open(src, "wb").write(get(thumb_url(c, 1920)))
        time.sleep(3)
    args = [sys.executable, os.path.join(here, "cutout.py"), src, work, sid]
    if pick.get("isnet"):
        args += ["--isnet", os.environ["ISNET_MODEL"]]
    elif pick.get("grabcut"):
        args.append("--grabcut")
    if pick.get("box"):
        args += ["--box", pick["box"]]
    if pick.get("open"):
        args += ["--open", str(pick["open"])]
    if pick.get("flip"):
        args.append("--flip")
    if pick.get("threshold"):
        args += ["--threshold", str(pick["threshold"])]
    subprocess.run(args, check=True)
    subprocess.run([sys.executable, os.path.join(here, "measure.py"), work, sid], check=True)
    meta_path = os.path.join(work, f"{sid}.json")
    meta = json.load(open(meta_path, encoding="utf-8"))
    meta.update({
        "title": c["title"],
        "author": re.sub(r"<[^>]+>", "", html.unescape(c.get("artist") or "")).strip() or "nieznany autor",
        "licence": c.get("licence") or "",
        "licence_url": c.get("licence_url") or "",
        "source": c.get("source") or "",
    })
    json.dump(meta, open(meta_path, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    json.dump(meta, open(os.path.join(assets, f"{sid}.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)

# Credits for every species measured.
lines = ["# Reference photographs", "",
         "The painted fish of these species are lofted to the body outline and painted in the colours measured from",
         "these photographs on Wikimedia Commons. The photographs themselves are not part of the game.", ""]
for f in sorted(os.listdir(assets)):
    if f.endswith(".json"):
        m = json.load(open(os.path.join(assets, f), encoding="utf-8"))
        lines.append(f"- `{f[:-5]}`: \"{m.get('title', '')}\" by {m.get('author', '?')}, {m.get('licence', '?')} "
                     f"({m.get('licence_url', '')}), {m.get('source', '')}")
open(os.path.join(assets, "CREDITS.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("credits written")
