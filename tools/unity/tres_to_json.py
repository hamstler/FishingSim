#!/usr/bin/env python3
"""Converts the game's Godot data resources (data/**/*.tres) to JSON for the Unity port.

    python3 -I tools/unity/tres_to_json.py [--out unity/Assets/StreamingAssets/data]

Each [resource] section becomes one JSON object: "$type" = the script_class, then every property. Godot values map as
Color(r,g,b,a) -> {"R","G","B","A"}, Vector2(x,y) -> {"X","Y"}, Packed*Array(...) -> a list, {..} dictionaries and plain
numbers/strings/bools as they are; ExtResource script references are dropped. The Unity side (FishingSim.Compat.DataStore)
reads res://data/<path>.tres as StreamingAssets/data/<path>.json.
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "unity", "Assets", "StreamingAssets", "data")
if "--out" in sys.argv:
    OUT = sys.argv[sys.argv.index("--out") + 1]

CALL = re.compile(r"^([A-Za-z0-9]+)\((.*)\)$", re.S)


def value(text):
    text = text.strip()
    m = CALL.match(text)
    if m:
        kind, args = m.group(1), m.group(2)
        if kind == "ExtResource":
            return None
        if kind.startswith("Packed") and kind.endswith("Array"):
            return json.loads("[" + args + "]") if args.strip() else []
        nums = [float(a) for a in args.split(",")] if args.strip() else []
        if kind == "Color":
            return dict(zip("RGBA", nums + [1.0] * (4 - len(nums))))
        if kind in ("Vector2", "Vector2i"):
            return {"X": nums[0], "Y": nums[1]}
        if kind in ("Vector3", "Vector3i"):
            return {"X": nums[0], "Y": nums[1], "Z": nums[2]}
        raise ValueError(f"unknown value {kind}")
    if text in ("true", "false"):
        return text == "true"
    if text.startswith('"') and text.endswith('"'):
        # A string: everything between the outer quotes (some descriptions hold bare inner quotes, which Godot reads).
        return text[1:-1].replace('\\"', '"').replace("\\n", "\n").replace("\\t", "\t")
    return json.loads(text)          # numbers, strings (with \" escapes), {...} dictionaries, [...] arrays


def convert(path):
    src = open(path, encoding="utf-8").read()
    head = re.search(r'script_class="([^"]+)"', src)
    body = src.split("[resource]", 1)[1]
    obj = {"$type": head.group(1) if head else ""}
    # key = value, where a value may run over several lines (strings with newlines, dictionaries).
    for m in re.finditer(r"^([A-Za-z_][A-Za-z0-9_]*) = (.*?)(?=^[A-Za-z_][A-Za-z0-9_]* = |\Z)", body, re.S | re.M):
        key, raw = m.group(1), m.group(2).strip()
        if key == "script":
            continue
        v = value(raw)
        if v is not None:
            obj[key] = v
    return obj


def main():
    n = 0
    for base, _, files in os.walk(os.path.join(ROOT, "data")):
        for f in files:
            if not f.endswith(".tres"):
                continue
            src = os.path.join(base, f)
            rel = os.path.relpath(src, os.path.join(ROOT, "data"))
            dst = os.path.join(OUT, rel[:-5] + ".json")
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with open(dst, "w", encoding="utf-8") as fh:
                json.dump(convert(src), fh, ensure_ascii=False, indent=1)
            n += 1
    print(f"{n} resources -> {OUT}")


if __name__ == "__main__":
    main()
