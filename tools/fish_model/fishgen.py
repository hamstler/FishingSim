#!/usr/bin/env python3
"""
The fish model generator: one species file (tools/fish_model/species/<id>.json) in, the textures for the Blender build
(fish_blender.py) out. A species file holds numbers only, measured on a side-view reference (the outline of the body
without its fins, each fin's base and outer edge, the eye) and placed from it (the mouth, lips, the upper jaw bone, the
gill cover, nostrils, the lateral line), with the colours, the skin pattern, scale size and barbels.

The body is lofted to the outline; its skin is unwrapped once round the fish and painted here: overlapping scales (or
smooth skin), the species' markings (bars, spots, stripes, marbling, scutes, mirror scales ...), the head's anatomy and
a normal map. Fins are rayed, pleated membranes with their own painted textures; the eyes are irises under corneas.

Photo coordinates: x from the snout tip (0) along the fish (the whole fish = 1), y downwards, in total lengths.
Model space (as src/Fishing/FishModel.cs expects): unit total length, nose at +z 0.5, y up, x to the fish's right.

Usage: python3 tools/fish_model/fishgen.py --textures <species.json> <work_dir> [<size>]
       python3 tools/fish_model/fishgen.py --reference <species.json> assets/fish/<id>.json   (the lake model's outline)
"""
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb import vertex_normals  # noqa: E402,F401

SPECIES = {}
FIN_TEX = (320, 160)


def load_species(path_or_dict):
    """Loads a species file and sets the generator's globals from it."""
    sp = json.load(open(path_or_dict, encoding="utf-8")) if isinstance(path_or_dict, str) else path_or_dict
    g = globals()
    g["SPECIES"] = sp
    tup = lambda pts: [tuple(p) for p in pts]
    g["AXIS"] = sp.get("axis", 0.0)
    g["DEPTH_SCALE"] = sp.get("depth_scale", 1.0)
    g["TOP"], g["BOTTOM"] = tup(sp["top"]), tup(sp["bottom"])
    g["X0"], g["X1"] = sp.get("x0", 0.0), sp["x1"]
    g["CAP_LENGTH"] = sp.get("cap_length", 0.0008)
    g["HALF_WIDTH"] = tup(sp["half_width"])
    g["N_TOP_ALONG"], g["N_BOTTOM_ALONG"] = tup(sp["n_top"]), tup(sp["n_bottom"])
    g["EYE"], g["EYE_R"] = tuple(sp["eye"]), sp["eye_r"]
    for key in ("gape", "upper_lip_fold", "lower_lip_fold", "maxilla_edge", "maxilla", "nostrils", "preopercle",
                "opercle", "lateral_line"):
        g[key.upper()] = tup(sp.get(key, []))
    g["FINS"] = sp["fins"]
    col = sp["colours"]
    for key in ("ridge", "upper", "mid", "mid2", "lower", "belly", "bar", "head_top", "cheek", "throat", "lip"):
        g[key.upper()] = tuple(col[key])
    g["IRIS"] = tuple(sp.get("iris", (0.85, 0.7, 0.3)))
    g["FIN_COLOURS"] = sp["fin_colours"]
    g["SCALE"] = sp.get("scale", 0.0)
    g["PATTERN"] = sp.get("pattern", {"type": "plain"})
    g["SILVER"] = sp.get("silver", 0.5)
    g["UV_W"], g["UV_Y0"], g["UV_H"] = sp.get("uv", (X1 + 0.02, -0.25, 0.5))
    # Landmarks of the head, from its outline.
    op = np.array(OPERCLE)
    g["OP_X"] = float(op[:, 0].max())
    g["OP_TOP"], g["OP_BOT"] = tuple(op[0]), tuple(op[-1])
    g["OP_MID_Y"] = float((op[0, 1] + op[-1, 1]) / 2)
    g["SPINE"] = tuple(op[int(np.argmax(op[:, 0]))])
    pre = np.array(PREOPERCLE)
    g["PREOP_X"] = float(pre[:, 0].mean())
    g["PREOP_MID_Y"] = float(pre[:, 1].mean())
    g["MOUTH_END"] = float(GAPE[-1][0])
    g["HS"] = float(np.clip(OP_X / 0.255, 0.5, 1.4))       # head size against the perch the details were drawn on
    return sp


# ------------------------------------------------------------------ helpers
def interp(points, x):
    p = np.array(points)
    return np.interp(x, p[:, 0], p[:, 1])


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def resample(points, n):
    """n points evenly spaced by arc length along a polyline."""
    p = np.array(points, dtype=np.float64)
    seg = np.linalg.norm(np.diff(p, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    t = np.linspace(0, s[-1], n)
    return np.stack([np.interp(t, s, p[:, 0]), np.interp(t, s, p[:, 1])], axis=1)


def polyline_distance(px, py, points):
    """Distance from each (px, py) to a polyline, in photo units."""
    p = np.array(points)
    best = np.full(np.shape(px), np.inf)
    for (ax, ay), (bx, by) in zip(p[:-1], p[1:]):
        dx, dy = bx - ax, by - ay
        t = np.clip(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1)
        best = np.minimum(best, np.hypot(px - (ax + t * dx), py - (ay + t * dy)))
    return best


def opercle_x(py, extend=False):
    """x of the gill cover's rear edge at photo height py (nan outside it, or carried on over the nape and the throat)."""
    p = np.array(OPERCLE)
    out = np.full(np.shape(py), np.nan)
    if extend:
        out = np.where(py < p[0, 1], p[0, 0] + (p[0, 1] - py) * 0.4, out)
        out = np.where(py > p[-1, 1], p[-1, 0] - (py - p[-1, 1]) * 0.6, out)
    for (ax, ay), (bx, by) in zip(p[:-1], p[1:]):
        lo, hi = min(ay, by), max(ay, by)
        m = (py >= lo) & (py <= hi)
        t = (py - ay) / (by - ay) if by != ay else 0
        out = np.where(m, ax + t * (bx - ax), out)
    return out


def half_width(x):
    return interp(HALF_WIDTH, x)


def n_top(x):
    return interp(N_TOP_ALONG, x)


def n_bottom(x):
    return interp(N_BOTTOM_ALONG, x)


def model(x, y, lateral):
    """Photo coordinates (+ lateral offset, photo units) -> model space."""
    return np.stack([lateral * DEPTH_SCALE, (AXIS - y) * DEPTH_SCALE, 0.5 - x], axis=-1)


# ------------------------------------------------------------------ body
def build_body(n_rings=190, n_seg=80):
    s = np.linspace(0, 1, n_rings)
    xs = X0 + (X1 - X0) * s ** 1.3                  # rings closer together at the head
    cap = 6                                          # a rounded snout in front of the first ring
    rings_x, rings_k = [], []
    for k in range(cap, 0, -1):
        phi = math.pi / 2 * k / cap
        rings_x.append(X0 - 0.0028 * math.sin(phi))
        rings_k.append(max(math.cos(phi), 0.02))
    rings_x += list(xs)
    rings_k += [1.0] * len(xs)
    rings_x, rings_k = np.array(rings_x), np.array(rings_k)
    xc = np.clip(rings_x, X0, X1)
    top, bottom, hw = interp(TOP, xc), interp(BOTTOM, xc), half_width(xc)
    yc, hh = (top + bottom) / 2, (bottom - top) / 2 * rings_k
    hw = hw * rings_k
    theta = np.linspace(0, 2 * math.pi, n_seg, endpoint=False)
    sn, cs = np.sin(theta), np.cos(theta)
    n_exp = np.where(sn[None, :] >= 0, n_top(xc)[:, None], n_bottom(xc)[:, None])
    lat = hw[:, None] * np.sign(cs) * np.abs(cs) ** (2 / n_exp)
    up = hh[:, None] * np.sign(sn) * np.abs(sn) ** (2 / n_exp)
    px = np.repeat(rings_x[:, None], n_seg, axis=1)
    py = yc[:, None] - up
    lat = sculpt_head(px, py, lat, cs)
    pos = model(px, py, lat).reshape(-1, 3)
    uv = np.stack([px / UV_W, (py - UV_Y0) / UV_H], axis=-1).reshape(-1, 2)
    idx = []
    nr = len(rings_x)
    for i in range(nr - 1):
        a = i * n_seg + np.arange(n_seg)
        b = i * n_seg + (np.arange(n_seg) + 1) % n_seg
        c = a + n_seg
        d = b + n_seg
        idx.append(np.stack([a, b, c, b, d, c], axis=1).reshape(-1, 3))
    return _close_body(pos, uv, idx, rings_x, n_seg, wrap=True)


def mouth_cleft(px, py):
    """
    How far the front of the head moves along the fish at the mouth: the crease between the lips is pushed back, the
    fleshy lips either side of it stand forward, so the snout ends in two rounded lips, not in a point.
    """
    dy = py - interp(GAPE, np.clip(px, GAPE[0][0], GAPE[-1][0]))
    front = smoothstep(X0 + 0.022, X0 + 0.004, px) * smoothstep(X0, X0 + 0.0025, px)
    lips = np.exp(-((np.abs(dy) - 0.0055) / 0.0035) ** 2)
    return (0.0016 * np.exp(-(dy / 0.0014) ** 2) - 0.0016 * lips) * front


def mouth_open(px, py):
    """The jaws stand a little apart at the front, as a perch holds its mouth: upper lip up, lower lip down."""
    dy = py - interp(GAPE, np.clip(px, GAPE[0][0], GAPE[-1][0]))
    inside_cap = smoothstep(X0 - 0.0025, X0 + 0.002, px)      # the rounded tip in front of the first ring stays whole
    return 0.0006 * np.tanh(dy / 0.002) * np.exp(-(dy / 0.008) ** 2) * smoothstep(0.75 * MOUTH_END, X0, px) * inside_cap


def maxilla_mask(px, py):
    """Inside the upper jaw bone's plate: below its upper edge, above the gape, between its front and rounded end."""
    if len(MAXILLA_EDGE) < 5:
        return np.zeros(np.shape(px))
    edge = np.array(MAXILLA_EDGE)
    top = np.interp(px, edge[:4, 0], edge[:4, 1])
    lower = interp(GAPE, np.clip(px, GAPE[0][0], GAPE[-1][0]))
    ex, ey = edge[3:, 0].mean(), edge[3:, 1].mean()
    er = 0.75 * np.max(np.hypot(edge[3:, 0] - ex, edge[3:, 1] - ey))
    end = np.hypot((px - ex) / 1.0, (py - ey) / 1.15)
    x_front = edge[0, 0]
    body = smoothstep(top - 0.0008, top + 0.0012, py) * smoothstep(lower + 0.012 * HS, lower + 0.009 * HS, py) \
        * smoothstep(x_front, x_front + 0.01, px) * smoothstep(ex, ex - 0.006, px)
    return np.maximum(body, smoothstep(er, er * 0.83, end) * smoothstep(ex - 0.009, ex - 0.004, px)) \
        * smoothstep(x_front - 0.005, x_front + 0.005, px)


def sculpt_head(px, py, lat, cs):
    """The head's relief on the lofted surface: eye socket, gape and lips, the gill cover's ledge, the preopercle."""
    lat = lat.copy()
    side = np.abs(cs)[None, :] ** 0.5               # head details on the flanks, not on the crown or the throat
    # Eye socket: a hollow the eyeball sits in.
    d_eye = np.hypot(px - EYE[0], py - EYE[1]) / EYE_R
    lat -= np.sign(lat) * side * 0.3 * EYE_R * (1 - smoothstep(1.0, 1.45, d_eye))
    # Mouth: the gape as a groove between two lips.
    dm = polyline_distance(px, py, GAPE)
    head = smoothstep(MOUTH_END + 0.032, MOUTH_END + 0.017, px)
    # Closed mouth: thick, rounded lips either side of a narrow crease that deepens into the corner.
    corner = smoothstep(MOUTH_END + 0.004, MOUTH_END - 0.004, px)
    lat -= np.sign(lat) * side * head * corner * (0.0022 * np.exp(-(dm / 0.0011) ** 2)
                                                  - 0.0034 * np.exp(-((dm - 0.0050) / 0.0032) ** 2))
    # The lips' folds as grooves; the upper jaw bone as a raised plate inside its outline, with a groove round it.
    for fold in (UPPER_LIP_FOLD, LOWER_LIP_FOLD, MAXILLA_EDGE):
        if len(fold) >= 2:
            lat -= np.sign(lat) * side * 0.0011 * np.exp(-(polyline_distance(px, py, fold) / 0.0012) ** 2)
    lat += np.sign(lat) * side * 0.0016 * maxilla_mask(px, py)
    # Gill cover: the body behind its rear edge sits lower, so the edge stands out as a ledge.
    ox = opercle_x(py)
    behind = np.nan_to_num(px - ox, nan=-1.0)
    lat -= np.sign(lat) * side * np.where(behind > 0, 0.0045 * (1 - smoothstep(0.0, 0.03, behind)), 0.0)
    # Preopercle: a shallow groove.
    dp = polyline_distance(px, py, PREOPERCLE)
    lat += np.sign(lat) * side * (0.0014 * np.exp(-((dp - 0.002) / 0.0025) ** 2) - 0.0010 * np.exp(-(dp / 0.0012) ** 2))
    return lat


def build_body_unwrapped(n_rings=None, n_seg=None):
    """
    The body for the Blender build: quads, the skin unwrapped once round the body (u from the snout to the tail
    stalk, v from the dorsal midline round the right flank, the belly and the left flank back to it), so the texture
    neither stretches over the back nor the belly. Returns model-space positions, UVs and faces (quads and triangles).
    """
    n_rings = n_rings or SPECIES.get("mesh", (110, 72))[0]
    n_seg = n_seg or SPECIES.get("mesh", (110, 72))[1]
    s = np.linspace(0, 1, n_rings)
    xs = X0 + (X1 - X0) * s ** 1.3
    cap = 10
    rings_x, rings_k = [], []
    for k in range(cap, 0, -1):
        phi = math.pi / 2 * k / cap
        rings_x.append(X0 - CAP_LENGTH * math.sin(phi))
        rings_k.append(max(math.cos(phi), 0.02))
    rings_x = np.array(rings_x + list(xs))
    rings_k = np.array(rings_k + [1.0] * len(xs))
    xc = np.clip(rings_x, X0, X1)
    top, bottom, hw = interp(TOP, xc), interp(BOTTOM, xc), half_width(xc) * rings_k
    yc, hh = (top + bottom) / 2, (bottom - top) / 2 * rings_k
    v = np.arange(n_seg + 1) / n_seg                  # the seam (v = 0 and 1) on the dorsal midline
    theta = np.pi / 2 - 2 * np.pi * v
    sn, cs = np.sin(theta), np.cos(theta)
    cs = np.where(np.abs(cs) < 1e-9, 0.0, cs)
    n_exp = np.where(sn[None, :] >= 0, n_top(xc)[:, None], n_bottom(xc)[:, None])
    lat = hw[:, None] * np.sign(cs) * np.abs(cs) ** (2 / n_exp)
    up = hh[:, None] * np.sign(sn) * np.abs(sn) ** (2 / n_exp)
    px = np.repeat(rings_x[:, None], n_seg + 1, axis=1)
    py = yc[:, None] - up
    # The rounded tip in front of the first ring closes over the mouth: it follows the head's relief and the lips less
    # the nearer it is to its centre, so the front stays smooth.
    weight = rings_k[:, None] ** 3
    lat = lat + (sculpt_head(px, py, lat, cs) - lat) * weight
    pos = model(px + mouth_cleft(px, py) * weight, py + mouth_open(px, py), lat).reshape(-1, 3)
    u = np.clip((rings_x - X0) / (X1 - X0), 0, 1)
    uv = np.stack([np.repeat(u[:, None], n_seg + 1, axis=1), np.repeat(v[None, :], len(rings_x), axis=0)], axis=-1).reshape(-1, 2)
    m = n_seg + 1
    faces = []
    for i in range(len(rings_x) - 1):
        for j in range(n_seg):
            a = i * m + j
            faces.append((a, a + m, a + m + 1, a + 1))
    tip = len(pos)
    pos = np.vstack([pos, model(np.array([X0 - CAP_LENGTH - 0.0002]), np.array([(interp(TOP, X0) + interp(BOTTOM, X0)) / 2]), np.zeros(1))])
    uv = np.vstack([uv, [[0.0, 0.5]]])
    faces += [(tip, j + 1, j) for j in range(n_seg)]
    end = len(pos)
    pos = np.vstack([pos, model(np.array([X1 + 0.002]), np.array([(interp(TOP, X1) + interp(BOTTOM, X1)) / 2]), np.zeros(1))])
    uv = np.vstack([uv, [[1.0, 0.5]]])
    last = (len(rings_x) - 1) * m
    faces += [(end, last + j, last + j + 1) for j in range(n_seg)]
    return pos.astype(np.float32), uv.astype(np.float32), faces


def _close_body(pos, uv, idx, rings_x, n_seg, wrap):
    nr = len(rings_x)
    # Close the snout tip and the tail stalk with a centre vertex each.
    tip = len(pos)
    pos = np.vstack([pos, model(np.array([X0 - 0.003]), np.array([interp(TOP, X0) / 2 + interp(BOTTOM, X0) / 2]), np.zeros(1))])
    uv = np.vstack([uv, [[X0 / UV_W, ((interp(TOP, X0) + interp(BOTTOM, X0)) / 2 - UV_Y0) / UV_H]]])
    ring0 = np.arange(n_seg)
    idx.append(np.stack([np.full(n_seg, tip), (ring0 + 1) % n_seg, ring0], axis=1))
    end = len(pos)
    yend = (interp(TOP, X1) + interp(BOTTOM, X1)) / 2
    pos = np.vstack([pos, model(np.array([X1 + 0.002]), np.array([yend]), np.zeros(1))])
    uv = np.vstack([uv, [[X1 / UV_W, (yend - UV_Y0) / UV_H]]])
    last = (nr - 1) * n_seg + ring0
    idx.append(np.stack([np.full(n_seg, end), last, (last - (nr - 1) * n_seg + 1) % n_seg + (nr - 1) * n_seg], axis=1))
    idx = np.vstack(idx).astype(np.uint32)
    # Faces must turn outwards: check one band and flip the winding if needed.
    nrm = vertex_normals(pos, idx)
    probe = n_seg * (nr // 2)                         # a vertex on the right flank (theta = 0)
    if nrm[probe][0] < 0:
        idx = idx[:, [0, 2, 1]]
        nrm = vertex_normals(pos, idx)
    return pos, nrm, uv, idx


def tangents(nrm):
    """Tangents along the skin's U (photo x = model -z) and the handedness for the mirrored side view."""
    t = np.array([0.0, 0.0, -1.0])
    tan = t[None, :] - nrm * (nrm @ t)[:, None]
    length = np.linalg.norm(tan, axis=1, keepdims=True)
    fallback = np.array([0.0, 1.0, 0.0])
    tan = np.where(length > 1e-4, tan / np.maximum(length, 1e-9), fallback)
    # The bitangent (N x T * w) must point up the texture (model +y) on both flanks.
    w = np.where(np.cross(nrm, tan)[:, 1] >= 0, 1.0, -1.0)
    return np.hstack([tan, w[:, None]]).astype(np.float32)


# ------------------------------------------------------------------ fins
def build_fin(spec, segs=10, pleat=0.0012):
    n = spec["rays"]
    base, tips = resample(spec["base"], n), resample(spec["tips"], n)
    k = np.linspace(0, 1, segs + 1)
    pts = base[:, None, :] + (tips - base)[:, None, :] * k[None, :, None]   # (rays, segs+1, 2)
    px, py = pts[..., 0], pts[..., 1]
    # Pleats: the membrane folds between the rays, so the fin catches the light ray by ray.
    lat = pleat * np.where(np.arange(n) % 2 == 0, 1.0, -1.0)[:, None] * np.sin(np.pi * k)[None, :] * 0.7
    # A fin is never a flat plate: it bows a little across its rays and towards its edge.
    across = np.linspace(0, 1, n)[:, None]
    lat = lat + spec.get("camber", 0.006) * np.sin(np.pi * across) * k[None, :] ** 1.5
    uv = np.stack([np.repeat(np.linspace(0, 1, n)[:, None], segs + 1, axis=1), np.repeat(k[None, :], n, axis=0)], axis=-1)
    idx = []
    for i in range(n - 1):
        for j in range(segs):
            a, b = i * (segs + 1) + j, (i + 1) * (segs + 1) + j
            idx += [(a, b, a + 1), (b, b + 1, a + 1)]
    idx = np.array(idx, dtype=np.uint32)
    if "splay" in spec:
        # Pectoral: on the flank behind the gill cover, swung out from the body towards the tail.
        bx, by = base.mean(axis=0)
        a = math.radians(spec["splay"])
        lat0 = half_width(bx) * 0.82
        dx = px - bx
        lat = lat0 + dx * math.sin(a) + lat
        px = bx + dx * math.cos(a)
    elif "droop" in spec:
        # Pelvic: under the belly, hanging down and a little outwards.
        bx, by = base.mean(axis=0)
        a = math.radians(spec["droop"])
        dy = py - by
        lat = 0.012 + dy * math.sin(a) + lat
        py = by + dy * math.cos(a)
    pos = model(px, py, lat).reshape(-1, 3)
    return pos, uv.reshape(-1, 2), idx



def paint_fin(name, spec, w=None, h=None):
    w, h = w or FIN_TEX[0], h or FIN_TEX[1]
    from PIL import Image
    n = spec["rays"]
    col = FIN_COLOURS[name]
    v, u = np.mgrid[0:h, 0:w].astype(np.float64)
    u, v = (u + 0.5) / w, (v + 0.5) / h                 # u across the rays, v from the base (0) to the edge (1)
    f = u * (n - 1)
    ray_i = np.round(f)
    du = np.abs(f - ray_i)                               # 0 on a ray, 0.5 halfway between two
    spiny = spec["kind"] in ("spiny", "spines")
    fleshy = spec["kind"] == "adipose"
    rng = np.random.default_rng(len(name))
    # The membrane stops short of the rays' tips: deep notches between the spines, a gentle scallop elsewhere.
    jitter = rng.uniform(-0.02, 0.02, n + 1)[ray_i.astype(int)]
    notch = (0.85 if spec["kind"] == "spines" else 0.42) if spiny else (0.02 if spec["kind"] == "caudal" else 0.035)
    edge = 1.0 - notch * np.sin(np.pi * du) ** (0.6 if spiny else 1.4) + jitter * (du < 0.5) * (1.0 if spiny else 0.3)
    membrane = smoothstep(edge, edge - 0.02, v)
    ray_w = (0.15 if spiny else 0.12) * (1 - 0.55 * v)
    if spec["kind"] == "soft" or spec["kind"] == "caudal":
        # Soft rays fork towards the edge and are segmented (fine light joints).
        sd = f - ray_i
        split = smoothstep(0.55, 0.9, v) * 0.13
        branch = np.minimum(np.abs(sd - split), np.abs(sd + split))
        ray = smoothstep(ray_w, ray_w * 0.4, branch)
        ray *= 1 - 0.45 * (np.sin(v * 70) > 0.85) * (v > 0.25)
    else:
        ray = smoothstep(ray_w, ray_w * 0.35, du)
    ray *= smoothstep(1.0, 0.97, v)
    if fleshy:                                            # the adipose fin: a fleshy flap, no rays
        ray *= 0.0
        membrane = smoothstep(1.0, 0.9, v)
    base_c = np.array(col["membrane"])
    c = np.ones((h, w, 3)) * base_c
    if "lower" in col:                                    # tail: olive above, orange-red lower lobe
        c = c * (1 - smoothstep(0.35, 0.8, u))[..., None] + np.array(col["lower"]) * smoothstep(0.35, 0.8, u)[..., None]
    c = c * (0.82 + 0.18 * v)[..., None]                  # the membrane brightens towards the edge
    if spec["kind"] == "caudal":                          # the tail's base takes the stalk's colour
        stalk = np.array(col.get("base", np.array(col["membrane"]) * 0.75))
        c = c * (1 - smoothstep(0.25, 0.0, v))[..., None] + stalk * smoothstep(0.25, 0.0, v)[..., None]
    ray_c = np.array(col["ray"])
    c = c * (1 - ray[..., None]) + ray_c * ray[..., None]
    if "edge" in col:                                     # a pale or dark margin (white-edged fins of chars)
        margin = smoothstep(0.82, 0.92, v) if spec["kind"] != "pelvic_front" else 0
        c = c * (1 - margin[..., None]) + np.array(col["edge"]) * margin[..., None]
    if col.get("lead"):                                   # a white leading edge (chars, brook trout)
        lead = smoothstep(0.06, 0.02, u)
        c = c * (1 - lead[..., None]) + np.array([0.97, 0.97, 0.95]) * lead[..., None]
    for sx, sy, sr, scol in spec.get("dots", []):         # spots painted on the fin (trout, grayling, ruffe)
        dot = smoothstep(sr, sr * 0.6, np.hypot((u - sx) * w / h, v - sy))
        c = c * (1 - dot[..., None]) + np.array(scol) * dot[..., None]
    if spec.get("spot"):
        # The perch's black spot at the back of the first dorsal fin, and a dusky edge.
        spot = smoothstep(1.0, 0.6, np.hypot((u - 0.88) / 0.13, (v - 0.42) / 0.33))
        c = c * (1 - 0.92 * spot[..., None]) + np.array([0.03, 0.03, 0.03]) * 0.92 * spot[..., None]
        c = c * (1 - 0.35 * smoothstep(0.7, 0.95, v))[..., None]
        membrane = np.maximum(membrane, spot * smoothstep(edge, edge - 0.03, v))
    for k in range(spec.get("spines", 0)):
        sp = smoothstep(0.14, 0.05, np.abs(f - k)) * smoothstep(1.0, 0.95, v)
        c = c * (1 - sp[..., None]) + np.array(col["spine"]) * sp[..., None]
        ray = np.maximum(ray, sp)
    c *= (1 + rng.normal(0, 0.012, (h, w)))[..., None]
    # Rays are opaque; the membrane between them thins out towards the edge.
    alpha = np.maximum(membrane * col["alpha"] * (1.0 - 0.3 * v), ray * 0.98)
    alpha *= smoothstep(0.0, 0.015, u) * smoothstep(1.0, 0.985, u) if spec["kind"] == "caudal" else 1
    rgba = np.dstack([np.clip(c, 0, 1), np.clip(alpha, 0, 1)])
    return Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")


# ------------------------------------------------------------------ body skin

def unwrap_coords(w, h):
    """
    The body's unwrapped skin (as the Blender model maps it): u from the snout to the tail stalk, v once round the
    body from the dorsal midline (0) down the right flank, under the belly (0.5) and up the left flank (1). For each
    texel: photo x and y of that point of the body (seen from the side) and its distance from the dorsal midline over
    the skin (s, for scale rows that do not stretch over the back and the belly), and the arc length per texel row.
    """
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    x = X0 + (X1 - X0) * u
    top, bottom, hw = interp(TOP, x), interp(BOTTOM, x), half_width(x)
    yc, hh = (top + bottom) / 2, (bottom - top) / 2
    theta = np.pi / 2 - 2 * np.pi * v
    sn, cs = np.sin(theta), np.cos(theta)
    n_exp = np.where(sn[:, None] >= 0, n_top(x)[None, :], n_bottom(x)[None, :])
    lat = hw[None, :] * np.sign(cs)[:, None] * np.abs(cs)[:, None] ** (2 / n_exp)
    up = hh[None, :] * np.sign(sn)[:, None] * np.abs(sn)[:, None] ** (2 / n_exp)
    py = yc[None, :] - up
    step = np.hypot(np.diff(lat, axis=0), np.diff(up, axis=0))
    cum = np.vstack([np.zeros((1, w)), np.cumsum(step, axis=0)])
    total = cum[-1] + np.hypot(lat[0] - lat[-1], up[0] - up[-1])
    s = np.minimum(cum, total[None, :] - cum)
    ds = np.gradient(cum, axis=0)
    return np.broadcast_to(x[None, :], (h, w)), py, s, ds, np.broadcast_to(np.abs(cs)[:, None] ** 0.5, (h, w))



def _hash(i, j, k=0):
    """A repeatable pseudo-random number in [0, 1) per scale."""
    v = np.sin(i * 127.1 + j * 311.7 + k * 74.7) * 43758.5453
    return v - np.floor(v)


def scale_field(x, s, a, rows=0.74, slope=0.22):
    """
    Overlapping fish scales on the skin. Scales sit on a staggered lattice whose rows run obliquely (down towards the
    tail); each is a disc a little larger than its cell, and where discs overlap the scale nearer the head lies on
    top (as on a fish, each scale covers the front of the one behind it), so what shows of each scale is its rounded
    free rear part. Returns per pixel: the visible scale's centre (x), its lattice indices, the position inside it
    (dx along the fish -1..1, distance d from its centre 0..1).
    """
    b = a * rows
    sp = s - slope * x
    j0 = np.floor(sp / b)
    shape = np.shape(x)
    best = np.full(shape, np.inf, dtype=np.float32)
    out = {k: np.zeros(shape, dtype=np.float32) for k in ("cx", "i", "j", "dx", "dy", "d")}
    rx, rs = 0.70 * a, 1.08 * b
    for dj in (-1, 0, 1):
        j = j0 + dj
        off = 0.5 * np.mod(j, 2)
        i0 = np.floor(x / a - off)
        for di in (-1, 0, 1, 2):
            i = i0 + di
            cx = (i + off) * a
            dx = (x - cx) / rx
            dy = (sp - (j + 0.5) * b) / rs
            d = np.hypot(dx, dy)
            take = (d < 1.0) & (cx < best)
            best = np.where(take, cx, best)
            for k, v in (("cx", cx), ("i", i), ("j", j), ("dx", dx), ("dy", dy), ("d", d)):
                out[k] = np.where(take, v, out[k])
    return out




def _noise(rng, h, w, cell):
    from PIL import Image
    small = (rng.random((max(h // cell, 2), max(w // cell, 2))) * 255).astype(np.uint8)
    return np.asarray(Image.fromarray(small).resize((w, h), Image.BICUBIC)) / 255.0


def _blend(c, colour, amount):
    return c * (1 - amount[..., None]) + np.array(colour) * amount[..., None]


def paint_pattern(c, height, x, y, s, rel, head_w, b, rng):
    """The species' markings over the scales (b: the body's scale field, or None for smooth skin)."""
    p = PATTERN
    kind = p.get("type", "plain")
    h, w = np.shape(x)
    body = (1 - head_w) * (x < X1 + 0.005)
    span0, span1 = OP_X + 0.01, X1 - 0.01
    xq = b["cx"] + 0.004 * (_hash(b["i"], b["j"], 3) - 0.5) if b is not None else x
    dark = np.array(BAR)

    def spots(count, size, r0, r1, colour, amount, seed, halo=None, aspect=1.0, x0=None, x1=None, mask=None):
        nonlocal c
        g = np.random.default_rng(seed)
        out = np.zeros((h, w))
        xa, xb = (x0 if x0 is not None else span0), (x1 if x1 is not None else span1)
        for _ in range(count):
            sx = g.uniform(xa, xb)
            sr = g.uniform(r0, r1)
            sz = size * g.uniform(0.7, 1.3)
            d = np.hypot((x - sx) / aspect, (rel - sr) * (interp(BOTTOM, np.clip(x, X0, X1)) - interp(TOP, np.clip(x, X0, X1))))
            out = np.maximum(out, smoothstep(sz, sz * 0.7, d))
            if halo is not None:
                ring = smoothstep(sz * 1.6, sz * 1.15, d) * (1 - smoothstep(sz, sz * 0.7, d))
                c = _blend(c, halo, 0.7 * ring * (mask if mask is not None else body))
        c = _blend(c, colour, amount * out * (mask if mask is not None else body))
        return out

    if kind in ("bars", "saddles"):
        n = p.get("count", 6)
        bars = p.get("bars") or [(span0 + (span1 - span0) * (i + 0.5) / n, 0.022 * (1 - 0.3 * i / max(n, 1)),
                                  0.38 if kind == "saddles" else 0.7, False) for i in range(n)]

        def bars_at(xx):
            out = np.zeros((h, w))
            for cx, hw, end, fork in bars:
                centre = cx + 0.014 * (rel - 0.3) + 0.004 * np.sin(rel * 9 + cx * 40)
                width = hw * (1 - 0.6 * np.clip(rel, 0, 1) / end)
                strength = smoothstep(end, end - 0.25, rel) * smoothstep(-0.05, 0.06, rel)
                if fork:
                    f = smoothstep(0.34, 0.1, rel)
                    d = np.minimum(np.abs(xx - centre - 0.013 * f), np.abs(xx - centre + 0.013 * f))
                    width = width * (1 - 0.35 * f)
                else:
                    d = np.abs(xx - centre)
                out = np.maximum(out, smoothstep(width + 0.004, width - 0.006, d) * strength)
            return out
        jitter = 0.25 * (_hash(b["i"], b["j"], 3) - 0.5) if b is not None else 0
        bar = np.clip(0.75 * np.clip(bars_at(xq) + jitter, 0, 1) + 0.25 * bars_at(x), 0, 1) * body
        c = _blend(c, dark, p.get("strength", 0.82) * bar)
    elif kind == "light_spots":               # pike: rows of pale bean-shaped spots
        spots(int(p.get("count", 420)), p.get("size", 0.0038), 0.12, 0.82, p.get("colour", (0.82, 0.80, 0.55)), 0.7, 11,
              aspect=1.9)
    elif kind == "red_spots":                 # brown trout: red spots with pale halos, black spots above
        spots(70, 0.0042, 0.05, 0.55, (0.08, 0.07, 0.06), 0.85, 12, x0=EYE[0], x1=span1)
        spots(45, 0.004, 0.35, 0.75, (0.78, 0.16, 0.10), 0.9, 13, halo=(0.92, 0.88, 0.80))
    elif kind == "dark_spots":                # salmon, trout, grayling, ruffe: black spots, mostly above the line
        spots(int(p.get("count", 90)), p.get("size", 0.0035), 0.05, p.get("depth", 0.55), dark * 0.6, 0.85, 14,
              x0=EYE[0] + 0.01, x1=span1)
    elif kind == "rainbow":                   # rainbow trout: a pink band along the flank, small black spots all over
        band = np.exp(-((rel - 0.48) / 0.12) ** 2) * body
        c = _blend(c, (0.86, 0.45, 0.55), 0.55 * band)
        c = _blend(c, (0.78, 0.40, 0.50), 0.35 * np.exp(-((rel - 0.5) / 0.14) ** 2) * head_w * (x > EYE[0]))
        spots(260, 0.0025, 0.0, 0.7, (0.06, 0.06, 0.05), 0.9, 15, x0=0.02, x1=X1 + 0.15)
    elif kind == "vermiculated":              # brook trout: pale worm-like lines on a dark back, red spots in blue halos
        n = _noise(rng, h, w, 18)
        worms = smoothstep(0.03, 0.0, np.abs(n - 0.5)) * smoothstep(0.55, 0.2, rel) * body
        c = _blend(c, (0.78, 0.74, 0.52), 0.7 * worms)
        spots(40, 0.0035, 0.35, 0.7, (0.86, 0.22, 0.12), 0.9, 16, halo=(0.35, 0.45, 0.75))
        spots(60, 0.003, 0.2, 0.7, (0.86, 0.82, 0.55), 0.6, 17)
    elif kind == "marbled":                   # catfish, burbot, sculpins, gobies: irregular dark blotches
        n = 0.6 * _noise(rng, h, w, 34) + 0.4 * _noise(rng, h, w, 12)
        blot = smoothstep(p.get("threshold", 0.55), p.get("threshold", 0.55) + 0.1, n) * smoothstep(0.95, 0.6, rel)
        c = _blend(c, dark, p.get("strength", 0.7) * blot * (body + 0.6 * head_w))
    elif kind == "stripe":                    # bleak, sunbleak, bitterling, smelt: a band along the flank
        band = np.exp(-((rel - p.get("at", 0.48)) / p.get("width", 0.07)) ** 2) * body
        band *= smoothstep(p.get("from", 0.0), p.get("from", 0.0) + 0.1, (x - OP_X) / (X1 - OP_X))
        c = _blend(c, p.get("colour", (0.70, 0.78, 0.82)), p.get("strength", 0.6) * band)
    elif kind == "stripes":                   # weatherfish: several dark bands along the body
        for at, wd in ((0.18, 0.035), (0.42, 0.06), (0.68, 0.03)):
            band = np.exp(-((rel - at) / wd) ** 2) * (body + head_w * 0.5)
            c = _blend(c, dark, 0.7 * band)
    elif kind == "spot_rows":                 # gudgeon, spined loach: a row of dark blotches along the side
        n = p.get("count", 10)
        for i in range(n):
            cx = span0 + (span1 - span0) * (i + 0.5) / n
            d = np.hypot((x - cx) / 1.4, (rel - 0.5) * 0.12)
            c = _blend(c, dark, 0.8 * smoothstep(0.012, 0.008, d) * body)
        spots(120, 0.002, 0.05, 0.45, dark, 0.6, 18)
    elif kind == "band":                      # largemouth bass: a dark, ragged band along the side
        n = _noise(rng, h, w, 16)
        band = smoothstep(0.1 + 0.05 * n, 0.04, np.abs(rel - 0.5)) * body
        c = _blend(c, dark, 0.65 * band)
    elif kind == "scutes":                    # sturgeons: rows of bony plates on smooth skin
        for at, n, size in ((0.0, 13, 0.016), (0.5, 34, 0.010), (0.92, 11, 0.010)):
            for i in range(n):
                cx = OP_X + 0.01 + (X1 - OP_X - 0.04) * (i + 0.5) / n
                d = np.hypot((x - cx) / 1.0, (rel - at) * (interp(BOTTOM, np.clip(x, X0, X1)) - interp(TOP, np.clip(x, X0, X1))))
                plate = smoothstep(size, size * 0.6, d)
                c = _blend(c, (0.88, 0.85, 0.75), 0.7 * plate * body)
                height += 1.2 * plate * body
    elif kind == "mirror":                    # mirror carp: a few big, glinting scales on bare skin
        g = np.random.default_rng(19)
        for row_rel, n in ((0.05, 9), (0.5, 7), (0.88, 4)):
            for i in range(n):
                if g.random() < 0.25:
                    continue
                cx = span0 + (span1 - span0) * (i + g.uniform(0.2, 0.8)) / n
                sz = g.uniform(0.016, 0.026)
                d = np.hypot((x - cx), (rel - row_rel - g.uniform(-0.05, 0.05)) * (interp(BOTTOM, np.clip(x, X0, X1)) - interp(TOP, np.clip(x, X0, X1))))
                plate = smoothstep(sz, sz * 0.8, d) * body
                rim = plate * smoothstep(sz * 0.8, sz, d + 0.004)
                c = _blend(c, np.array(MID) * 1.25, 0.6 * plate)
                c = c * (1 - 0.35 * rim)[..., None]
                height += 0.8 * plate - 0.6 * rim
    return c, height


def skin(x, y, s, side=None):
    """
    Colour and bump of the skin at photo points (x, y) of the side view; s runs over the skin across the body (the
    scale rows follow it).
    """
    from PIL import Image, ImageFilter
    h, w = np.shape(x)
    side = np.ones((h, w)) if side is None else side          # 1 on the flanks, 0 on the crown and the throat
    xc = np.clip(x, X0, X1)
    top, bottom = interp(TOP, xc), interp(BOTTOM, xc)
    rel = np.clip((y - top) / (bottom - top), -0.2, 1.2)
    stops = [(-0.2, RIDGE), (0.02, RIDGE), (0.14, UPPER), (0.42, MID), (0.62, MID2), (0.80, LOWER), (0.93, BELLY),
             (1.2, BELLY)]
    c = np.zeros((h, w, 3))
    for (r0, c0), (r1, c1) in zip(stops[:-1], stops[1:]):
        t = np.clip((rel - r0) / (r1 - r0), 0, 1)[..., None]
        m = ((rel >= r0) & (rel <= r1))[..., None]
        c = np.where(m, np.array(c0) * (1 - t) + np.array(c1) * t, c)
    height = np.zeros((h, w))
    rng = np.random.default_rng(7)

    ox = opercle_x(y)
    head_w = smoothstep(0.004, -0.004, x - opercle_x(y, extend=True))
    # Head: dark crown, cheeks and gill cover, a pale throat.
    hc = np.array(CHEEK) * np.ones((h, w, 3))
    hc = hc * (1 - smoothstep(0.35, 0.05, rel))[..., None] + np.array(HEAD_TOP) * smoothstep(0.35, 0.05, rel)[..., None]
    hc = hc * (1 - smoothstep(0.80, 0.97, rel))[..., None] + np.array(THROAT) * smoothstep(0.80, 0.97, rel)[..., None]
    gill = smoothstep(0.004, 0.0, np.abs(x - 0.5 * (np.nan_to_num(ox, nan=OP_X) + PREOP_X)) - 0.5 * (np.nan_to_num(ox, nan=OP_X) - PREOP_X))
    hc = hc * (1 + 0.08 * gill)[..., None]               # the gill cover a touch lighter
    # The head shares the body's colours to a degree (a fish's head is not a mask of its own colour).
    keep = SPECIES.get("head_blend", 0.0)
    hc = hc * (1 - keep) + c * keep
    c = c * (1 - head_w[..., None]) + hc * head_w[..., None]

    # Scales (or smooth skin), each showing its rounded free rear part with a dark rim, a glint and its own tint.
    b = None
    if SCALE > 0:
        cheek_size = min(SCALE * 0.52, 0.006)
        for name, size, mask in (("body", SCALE, 1 - head_w),
                                 ("cheek", cheek_size, head_w * smoothstep(EYE[0] + 1.2 * EYE_R, EYE[0] + 2.4 * EYE_R, x)
                                  * smoothstep(0.30, 0.42, rel) * smoothstep(0.92, 0.75, rel))):
            f = scale_field(x, s, size)
            if name == "body":
                b = f
            h1, h2 = _hash(f["i"], f["j"]), _hash(f["i"], f["j"], 1)
            rim = smoothstep(0.78, 1.0, f["d"]) * smoothstep(-0.1, 0.45, f["dx"])
            glint = np.exp(-((f["dx"] - 0.1) ** 2 + f["dy"] ** 2) / 0.10)
            shade = 1 - 0.26 * rim + (0.07 + 0.08 * SILVER) * glint + 0.08 * (h1 - 0.5)
            tint = np.stack([1 + 0.05 * (h2 - 0.5), 1 + 0.02 * (h1 - 0.5), 1 - 0.06 * (h2 - 0.5)], axis=-1)
            c = c * (1 + (shade[..., None] * tint - 1) * mask[..., None])
            height += mask * (0.25 + 0.45 * np.clip(f["dx"], -1, 1) - 0.6 * smoothstep(0.9, 1.0, f["d"]))
    else:
        # Smooth, slimy skin: a fine, soft grain and tiny pores.
        grain = _noise(rng, h, w, 6)
        c *= (0.96 + 0.08 * grain)[..., None]
        height += 0.15 * grain

    c, height = paint_pattern(c, height, x, y, s, rel, head_w, b, rng)

    # Fine dark speckles on the back.
    speck = (rng.random((h, w)) > 0.9965) * smoothstep(0.45, 0.1, rel)
    speck = np.asarray(Image.fromarray((speck * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))) / 255.0
    c *= (1 - 0.35 * speck)[..., None]

    # Lateral line: the scales along it each carry a short tube (a pale ridge with a dark pore); on smooth skin a row
    # of pores.
    if len(LATERAL_LINE) >= 2:
        ll = interp(LATERAL_LINE, x)
        near = smoothstep(0.004, 0.0015, np.abs(y - ll)) * (x > OP_X - 0.005) * (x < X1) * (1 - head_w)
        if b is not None:
            tube = smoothstep(0.32, 0.12, np.abs(b["dy"])) * smoothstep(-0.55, -0.3, b["dx"]) * smoothstep(0.55, 0.3, b["dx"])
            pore = smoothstep(0.2, 0.08, np.hypot(b["dx"] - 0.45, b["dy"]))
        else:
            tube = near * 0
            pore = (np.sin(x / 0.008 * 2 * np.pi) > 0.85) * smoothstep(0.0015, 0.0005, np.abs(y - ll))
        c = c * (1 + 0.16 * near * tube)[..., None] * (1 - 0.45 * near * pore)[..., None]
        height += near * (0.5 * tube - 0.6 * pore)

    # Head details: the mouth's lips and crease, nostrils, the preopercle with its serrated edge, the gill cover's dark
    # rear edge with a light rim, its spine, rays on its bone, the red gill membrane, the lips' folds and the upper jaw
    # bone, the gill cover's plate, mottling, the dark socket round the eye.
    dm = polyline_distance(x, y, GAPE)
    in_mouth = smoothstep(MOUTH_END + 0.006, MOUTH_END - 0.002, x)
    lips = smoothstep(0.0085 * HS, 0.0035 * HS, dm) * in_mouth
    c = c * (1 - 0.3 * lips[..., None]) + np.array(LIP) * 0.3 * lips[..., None]
    crease = 0.0007 + 0.0005 * smoothstep(MOUTH_END - 0.028, MOUTH_END - 0.002, x)
    c *= (1 - 0.8 * smoothstep(crease + 0.0007, crease - 0.0002, dm) * in_mouth)[..., None]
    height -= 0.8 * smoothstep(0.0015, 0.0, dm) * in_mouth
    height += 0.5 * smoothstep(0.009, 0.004, dm) * smoothstep(0.0012, 0.003, dm) * in_mouth
    for nx, ny in NOSTRILS:
        pit = smoothstep(0.0028 * HS, 0.0012 * HS, np.hypot(x - nx, y - ny)) * smoothstep(0.3, 0.55, side)
        c *= (1 - 0.7 * pit)[..., None]
        height -= 0.6 * pit
    dp = polyline_distance(x, y, PREOPERCLE)
    c *= (1 - 0.3 * smoothstep(0.0018, 0.0006, dp))[..., None]
    if SPECIES.get("serrated_preopercle", False):
        teeth = smoothstep(0.004, 0.0015, dp) * (np.sin((x + y) / 0.0035 * np.pi) > 0.7) * (y > PREOP_MID_Y)
        c *= (1 - 0.3 * teeth)[..., None]
    height -= 0.5 * smoothstep(0.002, 0.0, dp)
    do = polyline_distance(x, y, OPERCLE)
    behind = np.nan_to_num(x - ox, nan=-1)
    c *= (1 - 0.55 * smoothstep(0.0022, 0.0008, do) * (behind >= -0.001))[..., None]
    c *= (1 + 0.15 * smoothstep(0.005, 0.0015, do) * (behind < 0))[..., None]
    if SPECIES.get("opercle_spine", False):
        spine = smoothstep(0.004, 0.0015, np.hypot((x - SPINE[0]) / 1.8, y - SPINE[1]))
        c *= (1 - 0.7 * spine)[..., None]
    operc = (behind < 0) * smoothstep(PREOP_X, PREOP_X + 0.015, x) * smoothstep(OP_MID_Y - 0.02, OP_MID_Y + 0.005, y)
    ox0, oy0 = OP_X - 0.07 * HS, OP_TOP[1] + 0.023 * HS
    ang = np.arctan2(y - oy0, x - ox0)
    rays = 0.5 + 0.5 * np.sin(ang * 70 + 3 * np.sin(np.hypot(x - ox0, y - oy0) * 300))
    c *= (1 + 0.07 * (rays - 0.5) * operc)[..., None]
    height += 0.25 * (rays - 0.5) * operc
    membrane = smoothstep(0.0040, 0.0018, do) * (behind >= -0.0005) * smoothstep(OP_MID_Y + 0.01, OP_MID_Y + 0.04, y)
    c = c * (1 - 0.45 * membrane[..., None]) + np.array([0.70, 0.32, 0.28]) * 0.45 * membrane[..., None]
    for fold in (UPPER_LIP_FOLD, LOWER_LIP_FOLD, MAXILLA_EDGE):
        if len(fold) >= 2:
            groove = smoothstep(0.0016, 0.0005, polyline_distance(x, y, fold)) * smoothstep(0.5, 0.75, side)
            c *= (1 - 0.14 * groove)[..., None]
            height -= 0.6 * groove
    mxp = maxilla_mask(x, y) * smoothstep(0.5, 0.75, side)
    c = c * (1 - 0.15 * mxp[..., None]) + (np.array(CHEEK) * 1.4).clip(0, 1) * 0.15 * mxp[..., None]
    height += 0.3 * mxp
    plate = (behind < 0) * smoothstep(PREOP_X + 0.003, PREOP_X + 0.015, x) * smoothstep(OP_TOP[1], OP_TOP[1] + 0.015, y) \
        * smoothstep(OP_BOT[1] + 0.005, OP_BOT[1] - 0.01, y)
    c *= (1 - 0.18 * plate * smoothstep(0.012, 0.0, do))[..., None]
    blot = _noise(rng, h, w, 40)
    fine = _noise(rng, h, w, 12)
    c *= (1 - 0.07 * head_w * (smoothstep(0.5, 0.85, blot) + smoothstep(0.55, 0.9, fine)))[..., None]
    de = np.hypot(x - EYE[0], y - EYE[1]) / EYE_R
    c *= (1 - 0.5 * smoothstep(1.0, 1.08, de) * smoothstep(1.45, 1.15, de))[..., None]
    height += 0.6 * smoothstep(1.0, 1.15, de) * smoothstep(1.5, 1.2, de)

    # A touch of mottling, and the dorsal ridge darker.
    noise = _noise(rng, h, w, 16)
    c *= (0.94 + 0.12 * noise)[..., None]
    c *= (1 - 0.25 * smoothstep(0.08, -0.05, rel))[..., None]
    # The very front of the snout: every column of the skin meets at its tip, so it blends round the body there.
    tip = smoothstep(X0 + 0.004, X0, x)[..., None]
    k = max(h // 12, 1)
    soft = np.cumsum(np.pad(c, ((k, k), (0, 0), (0, 0)), mode="wrap"), axis=0)
    soft = (soft[2 * k:] - soft[:-2 * k]) / (2 * k)
    c = c * (1 - tip) + soft[:h] * tip
    height *= 1 - tip[..., 0]
    return c, height


# ------------------------------------------------------------------ eye
def eye_centre():
    """Where the right eye sits: in its socket on the side of the head (model space)."""
    yc = (interp(TOP, EYE[0]) + interp(BOTTOM, EYE[0])) / 2
    hh = (interp(BOTTOM, EYE[0]) - interp(TOP, EYE[0])) / 2
    u = min(abs(EYE[1] - yc) / hh, 0.97)
    n = n_top(EYE[0]) if EYE[1] < yc else n_bottom(EYE[0])
    lat = half_width(EYE[0]) * (1 - u ** n) ** (1 / n)       # the head's surface there (its superellipse section)
    return model(np.array(EYE[0]), np.array(EYE[1]), np.array(lat - 0.33 * EYE_R))


def build_eye(segments=40, rings=12):
    """A shallow dome (cornea over the iris), centred on the eye, facing +x."""
    R = EYE_R * 1.08 * DEPTH_SCALE
    pos, uv = [[R * 0.42, 0.0, 0.0]], [[0.5, 0.5]]
    for i in range(1, rings + 1):
        polar = math.radians(72) * i / rings
        for j in range(segments):
            a = 2 * math.pi * j / segments
            y, z = math.sin(polar) * math.sin(a), math.sin(polar) * math.cos(a)
            x = math.cos(polar)
            pos.append([R * (x - 0.58), R * y, R * z])
            uv.append([0.5 - 0.5 * z / math.sin(math.radians(72)), 0.5 - 0.5 * y / math.sin(math.radians(72))])
    idx = []
    for j in range(segments):
        idx.append((0, 1 + (j + 1) % segments, 1 + j))
    for i in range(rings - 1):
        for j in range(segments):
            a, b = 1 + i * segments + j, 1 + i * segments + (j + 1) % segments
            idx += [(a, b, a + segments), (b, b + segments, a + segments)]
    pos, idx = np.array(pos, dtype=np.float32), np.array(idx, dtype=np.uint32)
    nrm = vertex_normals(pos, idx)
    if nrm[0][0] < 0:
        idx = idx[:, [0, 2, 1]]
        nrm = vertex_normals(pos, idx)
    return pos, nrm, np.array(uv, dtype=np.float32), idx



def paint_eye(size=512):
    from PIL import Image
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    r = np.hypot(xx + 0.5 - size / 2, yy + 0.5 - size / 2) / (size / 2)
    ang = np.arctan2(yy - size / 2, xx - size / 2)
    rng = np.random.default_rng(3)
    streaks = np.interp(ang, np.linspace(-np.pi, np.pi, 90), rng.random(90))
    iris = np.array(IRIS) * (0.75 + 0.35 * streaks)[..., None]
    c = iris * (1 - smoothstep(0.62, 0.86, r))[..., None] + np.array(IRIS) * 0.38 * smoothstep(0.62, 0.86, r)[..., None]
    c = c * (1 - smoothstep(0.86, 0.95, r))[..., None] + np.array([0.10, 0.11, 0.07]) * smoothstep(0.86, 0.95, r)[..., None]
    pupil = smoothstep(0.46, 0.42, r)
    c = c * (1 - pupil[..., None]) + np.array([0.01, 0.01, 0.015]) * pupil[..., None]
    c *= (1 - 0.25 * smoothstep(0.38, 0.5, r) * smoothstep(0.62, 0.5, r))[..., None]   # dark ring round the pupil
    return Image.fromarray((np.clip(c, 0, 1) * 255).astype(np.uint8), "RGB")


def write_textures(work, w=2048, h=1024):
    """Textures for the Blender build: the unwrapped skin and its normal map, fins, iris, barbels."""
    from PIL import Image
    os.makedirs(work, exist_ok=True)
    x, y, s, ds, side = unwrap_coords(w, h)
    c, height = skin(x, y, s, side)
    Image.fromarray((np.clip(c, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(work, "body_albedo.jpg"),
                                                                          quality=92 if w > 2048 else 86)
    k = 0.00062
    dx = (X1 - X0) / w
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 / dx
    gy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 / np.maximum(ds, 1e-6)
    n = np.dstack([-gx * k, gy * k, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB").resize((w // 2, h // 2), Image.LANCZOS) \
        .save(os.path.join(work, "body_normal.jpg"), quality=88)
    for name, spec in FINS.items():
        paint_fin(name, spec).save(os.path.join(work, name + ".png"))
    paint_eye(256 if w <= 2048 else 512).save(os.path.join(work, "iris.png"))
    if SPECIES.get("barbels"):
        col = np.array(SPECIES.get("barbel_colour", np.array(LIP) * 0.9))
        img = np.ones((16, 64, 3)) * col
        img *= (0.8 + 0.2 * np.linspace(0, 1, 64))[None, :, None]
        Image.fromarray((img.clip(0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(work, "FinBarbels.png"))
    print(f"textures in {work}")


def write_reference(path):
    """The same outline and colours for the lofted fish of the lake (FishReference), with the reference's credit."""
    xs = np.linspace(X0, X1, 32)
    up, down = (AXIS - interp(TOP, xs)) * DEPTH_SCALE, (interp(BOTTOM, xs) - AXIS) * DEPTH_SCALE
    meta = {
        "aspect": 1.0, "body_start": X0, "body_end": X1, "snout": X0,
        "outline": [[round(float(AXIS - u), 4), round(float(AXIS + d), 4)] for u, d in zip(up, down)],
        "profile": [[round(float(u), 4), round(float(d), 4)] for u, d in zip(up, down)],
        "palette": {"back": list(UPPER), "flank": list(MID), "belly": list(BELLY)},
        "author": SPECIES.get("credit_author", "AtlasRyb.online"), "licence": SPECIES.get("credit_licence", ""),
        "source": SPECIES.get("credit_source", "https://atlasryb.online"),
        "credit": SPECIES.get("credit", "Proporcje według ilustracji z AtlasRyb.online"),
    }
    json.dump(meta, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=0)


def main():
    if sys.argv[1] == "--reference":
        load_species(sys.argv[2])
        write_reference(sys.argv[3])
        return
    if sys.argv[1] == "--textures":
        load_species(sys.argv[2])
        size = int(sys.argv[4]) if len(sys.argv) > 4 else 2048
        if size > 2048:
            globals()["FIN_TEX"] = (640, 320)
        write_textures(sys.argv[3], size, size // 2)
        return
    raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
