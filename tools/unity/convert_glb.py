#!/usr/bin/env python3
"""Converts the game's .glb models for Unity: pictures stored as WebP (EXT_texture_webp, which Unity's glTF importer
cannot read) are re-encoded as JPEG (or PNG when they have transparency), at most --max px on a side; everything else is
copied unchanged.

    python3 -I tools/unity/convert_glb.py SRC_DIR DST_DIR [--max 2048] [--only REGEX]

Walks SRC_DIR for *.glb and writes the same relative paths under DST_DIR (skips files already converted and newer).
"""
import argparse
import io
import json
import os
import re
import struct
import sys

from PIL import Image


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        raise ValueError(f"{path}: not a glb")
    off = 12
    js, binary = None, b""
    while off < length:
        clen, ctype = struct.unpack_from("<II", data, off)
        chunk = data[off + 8: off + 8 + clen]
        if ctype == 0x4E4F534A:
            js = json.loads(chunk.decode("utf-8"))
        elif ctype == 0x004E4942:
            binary = chunk
        off += 8 + clen
    return js, binary


def write_glb(path, js, binary):
    jbytes = json.dumps(js, separators=(",", ":")).encode("utf-8")
    jbytes += b" " * ((4 - len(jbytes) % 4) % 4)
    binary += b"\0" * ((4 - len(binary) % 4) % 4)
    total = 12 + 8 + len(jbytes) + (8 + len(binary) if binary else 0)
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<II", len(jbytes), 0x4E4F534A))
        f.write(jbytes)
        if binary:
            f.write(struct.pack("<II", len(binary), 0x004E4942))
            f.write(binary)


def convert(src, dst, max_side):
    js, binary = read_glb(src)
    views = js.get("bufferViews", [])
    images = js.get("images", [])
    # every buffer view's bytes, so the binary can be rebuilt with new pictures
    pieces = []
    for v in views:
        start = v.get("byteOffset", 0)
        pieces.append(binary[start: start + v["byteLength"]])
    for img in images:
        if "bufferView" not in img:
            continue
        raw = pieces[img["bufferView"]]
        mime = img.get("mimeType", "")
        if mime not in ("image/webp", "image/png", "image/jpeg"):
            continue
        pic = Image.open(io.BytesIO(raw))
        pic.load()
        too_big = max(pic.size) > max_side
        if mime != "image/webp" and not too_big:
            continue
        if too_big:
            k = max_side / max(pic.size)
            pic = pic.resize((max(1, round(pic.width * k)), max(1, round(pic.height * k))), Image.LANCZOS)
        alpha = pic.mode in ("RGBA", "LA") and pic.getchannel("A").getextrema()[0] < 250
        out = io.BytesIO()
        if alpha:
            pic.convert("RGBA").save(out, "PNG", optimize=True)
            img["mimeType"] = "image/png"
        else:
            pic.convert("RGB").save(out, "JPEG", quality=90)
            img["mimeType"] = "image/jpeg"
        pieces[img["bufferView"]] = out.getvalue()
    for tex in js.get("textures", []):
        ext = tex.get("extensions", {})
        if "EXT_texture_webp" in ext:
            tex["source"] = ext.pop("EXT_texture_webp")["source"]
        if not ext:
            tex.pop("extensions", None)
    for key in ("extensionsUsed", "extensionsRequired"):
        if key in js:
            js[key] = [e for e in js[key] if e != "EXT_texture_webp"]
            if not js[key]:
                del js[key]
    # rebuild the binary: views back to back, 4-byte aligned
    out = bytearray()
    for v, piece in zip(views, pieces):
        out += b"\0" * ((4 - len(out) % 4) % 4)
        v["byteOffset"] = len(out)
        v["byteLength"] = len(piece)
        out += piece
    if js.get("buffers"):
        js["buffers"][0]["byteLength"] = len(out)
        js["buffers"] = js["buffers"][:1]
        for v in views:
            v["buffer"] = 0
    write_glb(dst, js, bytes(out))


COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def positions(js, binary):
    """All vertex positions of the model's meshes (node transforms ignored: the Meshy models are single meshes)."""
    import numpy as np
    out = []
    for mesh in js.get("meshes", []):
        for prim in mesh.get("primitives", []):
            acc = js["accessors"][prim["attributes"]["POSITION"]]
            view = js["bufferViews"][acc["bufferView"]]
            start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
            stride = view.get("byteStride", 12)
            raw = np.frombuffer(binary, dtype=np.uint8, count=stride * (acc["count"] - 1) + 12, offset=start)
            idx = (np.arange(acc["count"])[:, None] * stride + np.arange(12)[None, :]).ravel()
            out.append(raw[idx].view(np.float32).reshape(-1, 3))
    return np.concatenate(out) if out else np.zeros((0, 3), np.float32)


def fish_meta(path):
    """Which way a fish model's nose points (glTF space): along its longest axis, the end whose cross-section is
    wider across (x) than the tail fin, which is tall but thin (FishAsset.PrepareForeign's rule)."""
    import numpy as np
    js, binary = read_glb(path)
    p = positions(js, binary)
    ext = p.max(0) - p.min(0)
    axis = int(np.argmax(ext))
    across = 0 if axis != 0 else 2
    t = (p[:, axis] - p[:, axis].min()) / max(ext[axis], 1e-6)
    lo, hi = p[t < 0.15], p[t > 0.85]
    width = lambda q: float(q[:, across].max() - q[:, across].min()) if len(q) else 0.0
    return {"axis": axis, "nose": 1 if width(hi) >= width(lo) else -1,
            "min": [float(v) for v in p.min(0)], "max": [float(v) for v in p.max(0)]}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--max", type=int, default=2048)
    ap.add_argument("--only", default="")
    ap.add_argument("--skip", default="", help="leave out paths matching this")
    ap.add_argument("--fish", action="store_true", help="also write <name>.json with the nose's direction")
    a = ap.parse_args()
    pattern = re.compile(a.only) if a.only else None
    skip = re.compile(a.skip) if a.skip else None
    count = 0
    for root, _, files in os.walk(a.src):
        for name in sorted(files):
            if not name.endswith(".glb"):
                continue
            src = os.path.join(root, name)
            rel = os.path.relpath(src, a.src)
            if pattern and not pattern.search(rel) or skip and skip.search(rel):
                continue
            dst = os.path.join(a.dst, rel)
            if os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src):
                continue
            convert(src, dst, a.max)
            side = src[:-4] + ".json"
            if os.path.exists(side) and not a.fish:
                with open(side) as fi, open(dst[:-4] + ".json", "w") as fo:
                    fo.write(fi.read())
            if a.fish:
                with open(dst[:-4] + ".json", "w") as f:
                    json.dump(fish_meta(dst), f)
            count += 1
            print(f"{rel}: {os.path.getsize(src) // 1024} KB -> {os.path.getsize(dst) // 1024} KB", flush=True)
    print(f"{count} converted")


if __name__ == "__main__":
    sys.exit(main())
