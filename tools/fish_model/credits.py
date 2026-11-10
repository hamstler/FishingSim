#!/usr/bin/env python3
"""Writes assets/fish/CREDITS.md: where every species model's proportions and colours come from."""
import glob
import json
import os

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
lines = ["# Fish model references", "",
         "Every fish is modelled and painted in code (tools/fish_model). Its proportions were measured on the reference",
         "named here; no picture is copied into the game. AtlasRyb.online illustrations were used for measurements only.", ""]
for path in sorted(glob.glob(os.path.join(root, "tools", "fish_model", "species", "*.json"))):
    sp = json.load(open(path, encoding="utf-8"))
    src = sp.get("credit_source", "")
    lines.append(f"- `{sp['id']}` ({sp.get('name', '')}): {sp.get('credit', '')}" + (f" ({src})" if src else ""))
open(os.path.join(root, "assets", "fish", "CREDITS.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("credits written")
