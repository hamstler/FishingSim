#!/usr/bin/env python3
"""
Measures a cut-out reference photograph (cutout.py's <id>.webp next to its <id>.json) for the painted fish model and
adds the measurements to the JSON:
  palette  back, flank, belly, fin and tail colours (sRGB 0..1), from the middle of the body (no head, no tail
           stalk), each a trimmed mean that skips highlights and shadows, with the exposure evened out
  snout    where the tip of the snout is (fraction of the whole length)
  profile  32 pairs [back, belly]: heights above and below the body axis (the line from the tip of the snout to the
           middle of the tail stalk), in fractions of the whole length, evenly from the snout to the tail stalk
           (body_end), cleaned of fin bases and smoothed
The game lofts its own fish to this profile and paints it in these colours; the fins keep their per-species build
(src/Fishing/FishBody.cs). The photograph itself is not shipped.

Usage: python3 tools/fish_photos/measure.py <dir> [ids...]
"""
import glob
import json
import os
import sys

import numpy as np
from PIL import Image


def trimmed(pixels):
    """Mean colour of the middle half by brightness (no specular highlights, no dark creases)."""
    if len(pixels) == 0:
        return None
    lum = pixels @ np.array([0.3, 0.59, 0.11])
    lo, hi = np.percentile(lum, 25), np.percentile(lum, 75)
    keep = pixels[(lum >= lo) & (lum <= hi)]
    return (keep if len(keep) else pixels).mean(axis=0)


def smooth(v, sigma):
    k = np.exp(-0.5 * (np.arange(-3 * int(sigma + 1), 3 * int(sigma + 1) + 1) / sigma) ** 2)
    pad = len(k) // 2
    return np.convolve(np.pad(v, pad, mode="edge"), k / k.sum(), mode="valid")


def clean_edge(v, window):
    """
    A clean, game-like edge from a measured one: a 1-D opening cuts the bumps fin bases leave (narrower than the
    window), a blur rounds it, and it is made to rise from the snout to its highest point and fall from there to the
    tail stalk, like a fish's back or belly.
    """
    raw = np.maximum(v, 0)
    pad = window // 2
    lo = np.array([v[max(0, i - pad):i + pad + 1].min() for i in range(len(v))])
    v = np.maximum(np.array([lo[max(0, i - pad):i + pad + 1].max() for i in range(len(v))]), 0)
    v = smooth(v, 2.5)
    peak = int(np.argmax(v))
    for i in range(peak - 1, -1, -1):
        v[i] = min(v[i], v[i + 1])
    for i in range(peak + 1, len(v)):
        v[i] = min(v[i], v[i - 1])
    v = smooth(v, 1.5)
    # Blurring lifts the tip of the snout (it averages in the head behind it): the head keeps its measured curve.
    head = len(v) // 7
    v[:head] = np.minimum(v[:head], np.maximum.accumulate(raw[:head]))
    return v


def measure(path_json):
    meta = json.load(open(path_json, encoding="utf-8"))
    img = Image.open(path_json[:-5] + ".webp").convert("RGBA")
    img.thumbnail((1024, 1024))
    arr = np.asarray(img).astype(np.float32) / 255.0
    rgb, a = arr[..., :3], arr[..., 3]
    h, w = a.shape
    s, e = meta["body_start"], meta["body_end"]
    outline = np.array(meta["outline"])                       # (128, 2): top, bottom as image fractions
    n = len(outline)

    def edge_at(x):
        """Body top and bottom (pixel rows) at image column x, from the outline."""
        f = np.clip((x / w - s) / max(e - s, 1e-6), 0, 1) * (n - 1)
        i = int(np.floor(f))
        j = min(i + 1, n - 1)
        t = f - i
        top, bottom = outline[i] * (1 - t) + outline[j] * t
        return top * h, bottom * h

    depth = float((outline[:, 1] - outline[:, 0]).max())       # image fractions of the height
    solid = a > 0.9

    # The profile: back and belly heights above and below the body axis (fractions of the whole length) from the tip
    # of the snout to the tail stalk. The head comes from the full cut-out (the outline's opening rounds the snout
    # off), the body from the outline without fins; both cleaned to smooth, game-like edges.
    full = a > 0.5
    cols = np.nonzero(full.any(axis=0))[0]
    snout = cols.min() / w
    xs = np.linspace(snout, e, 96)
    tops, bottoms = [], []
    for x in xs:
        col = min(int(x * w), w - 1)
        rows = np.nonzero(full[:, col])[0]
        ft, fb = (rows.min(), rows.max()) if len(rows) else edge_at(col)
        bt, bb = edge_at(col) if x >= s else (ft, fb)
        k = float(np.clip((0.22 - (x - snout) / (e - snout)) / 0.1, 0, 1))
        tops.append(k * ft + (1 - k) * bt)
        bottoms.append(k * fb + (1 - k) * bb)
    tops, bottoms = np.array(tops), np.array(bottoms)
    # The body axis runs from the tip of the snout to the middle of the tail stalk, so both ends of the lofted body
    # close on it (the levelled photo may tilt the fish a few degrees off this line).
    centres = (tops + bottoms) / 2
    axis = np.linspace(centres[1:3].mean(), centres[-4:-1].mean(), len(xs))
    # The back carries the long dorsal fins, so its window is wider than the belly's.
    up, down = clean_edge((axis - tops) / w, 27), clean_edge((bottoms - axis) / w, 15)
    meta["snout"] = round(float(snout), 4)
    meta["profile"] = [[round(float(u), 4), round(float(d), 4)] for u, d in zip(up[::3], down[::3])]

    # Colours in the middle of the body: back, flank and belly bands.
    bands = {"back": (0.06, 0.22), "flank": (0.4, 0.6), "belly": (0.8, 0.94)}
    samples = {k: [] for k in bands}
    fins = []
    x0, x1 = int((s + (e - s) * 0.3) * w), int((s + (e - s) * 0.75) * w)
    for x in range(x0, x1):
        top, bottom = edge_at(x)
        for k, (f0, f1) in bands.items():
            for y in range(int(top + (bottom - top) * f0), int(top + (bottom - top) * f1)):
                if 0 <= y < h and solid[y, x]:
                    samples[k].append(rgb[y, x])
    # Fins: the solid cut-out above and below the body outline along the body.
    for x in range(int(s * w), int(e * w)):
        top, bottom = edge_at(x)
        col = np.nonzero(a[:, x] > 0.5)[0]
        if len(col) == 0:
            continue
        for y in list(range(int(col.min()), int(top) - 2)) + list(range(int(bottom) + 3, int(col.max()) + 1)):
            if a[y, x] > 0.8:
                fins.append(rgb[y, x])
    tail_px = [rgb[y, x] for x in range(int(e * w) + 2, w) for y in np.nonzero(a[:, x] > 0.8)[0]]

    palette = {k: trimmed(np.array(v)) for k, v in samples.items()}
    palette["fin"] = trimmed(np.array(fins)) if fins else palette["flank"]
    palette["tail"] = trimmed(np.array(tail_px)) if tail_px else palette["fin"]
    # A colour cast (aquarium light, a blue sky): fish bellies are never cyan or blue. When the belly's red falls well
    # below its green and blue, every colour is balanced so the belly is neutral.
    belly = palette["belly"]
    if belly is not None and belly[0] < min(belly[1], belly[2]) - 0.08:
        balance = belly.mean() / np.maximum(belly, 1e-3)
        palette = {k: (c * balance if c is not None else None) for k, c in palette.items()}
    # Even out the exposure: the brightest band at about 0.72.
    peak = max(float(np.max(c)) for c in palette.values() if c is not None)
    gain = float(np.clip(0.72 / max(peak, 1e-3), 0.8, 1.4))
    meta["palette"] = {k: [round(float(v), 3) for v in np.clip(c * gain, 0, 1)] for k, c in palette.items() if c is not None}

    for stale in ("dorsal", "anal", "tail"):
        meta.pop(stale, None)
    json.dump(meta, open(path_json, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    return meta


def main():
    d = sys.argv[1]
    ids = sys.argv[2:] or [os.path.basename(p)[:-5] for p in sorted(glob.glob(os.path.join(d, "*.webp")))]
    for sid in ids:
        m = measure(os.path.join(d, sid + ".json"))
        print(f"{sid}: {m['palette']}")


if __name__ == "__main__":
    main()
