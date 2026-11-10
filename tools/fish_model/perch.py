#!/usr/bin/env python3
"""
The European perch (Perca fluviatilis) as a game model, built from reference photographs and written as a .glb:
a lofted body with its head modelled (eye socket, the mouth's gape and lips, the gill cover's edge standing over the
body), separate eyes, and the fins as rayed membranes (spiny first dorsal with its black spot, soft second dorsal,
anal, forked tail, paired pectorals and pelvics splayed out from the body), each with a painted texture, and a
painted body skin with a scale normal map.

Shapes were traced on "Perche commune perca fluviatilis Alfort.jpg" (CC0), a side view with every fin raised; the
colours and the bars come from "Perca fluviatilis - Perche commune - European perch Cropped.jpg" (CC BY-SA 2.5) and
"Perca fluviatilis Aquarium Liege 30012016 1.jpg" (CC0), all on Wikimedia Commons. No photograph is copied into the
model: its skin is painted from the measured colours.

Model space (as src/Fishing/FishModel.cs expects): unit total length, nose at +z 0.5, tail tip at -0.5, y up, x to
the fish's right. Reference coordinates below are photo coordinates: x from the snout tip (0) to the tail tip (1),
y downwards, both in total lengths.

Usage: python3 tools/fish_model/perch.py assets/fish/models/okon.glb [<preview_dir>]
(also rewrites assets/fish/okon.json: the same outline and colours for the lofted perch in the lake, and the credit)
"""
import math
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb import Glb, vertex_normals  # noqa: E402

AXIS = 0.23           # photo y of the body axis (the snout tip and the tail stalk lie on it)
DEPTH_SCALE = 0.88    # the traced fish is a heavy old perch; the model is a medium one (FishModel grows it)

# Body outline without fins, traced every few percent of the length.
TOP = [(0.004, 0.216), (0.02, 0.19), (0.05, 0.165), (0.08, 0.155), (0.10, 0.150), (0.12, 0.145), (0.15, 0.130),
       (0.20, 0.110), (0.25, 0.098), (0.30, 0.095), (0.35, 0.093), (0.40, 0.095), (0.45, 0.105), (0.50, 0.114),
       (0.55, 0.122), (0.60, 0.130), (0.65, 0.145), (0.70, 0.165), (0.75, 0.183), (0.80, 0.193), (0.835, 0.194)]
BOTTOM = [(0.004, 0.240), (0.03, 0.247), (0.06, 0.265), (0.10, 0.285), (0.12, 0.300), (0.15, 0.320),
          (0.20, 0.345), (0.25, 0.370), (0.30, 0.390), (0.35, 0.400), (0.40, 0.405), (0.45, 0.405), (0.50, 0.400),
          (0.55, 0.373), (0.60, 0.338), (0.65, 0.313), (0.70, 0.293), (0.75, 0.276), (0.80, 0.268), (0.835, 0.266)]
X0, X1 = 0.004, 0.835          # snout tip, end of the tail stalk (the caudal fin's base)

# Width / depth of the body along it: a broad head, a laterally flattened body.
WIDTH = [(0.0, 0.85), (0.05, 0.76), (0.12, 0.68), (0.22, 0.60), (0.35, 0.47), (0.55, 0.44), (0.70, 0.44), (0.835, 0.5)]
N_TOP, N_BOTTOM = 1.75, 2.3    # cross-section: a narrow back (the dorsal fins' base), a full rounded belly

# Head.
EYE = (0.089, 0.188)
EYE_R = 0.0215 * 0.92
GAPE = [(0.004, 0.229), (0.03, 0.236), (0.06, 0.247), (0.088, 0.258)]
MAXILLA = [(0.035, 0.228), (0.06, 0.236), (0.09, 0.252)]
NOSTRILS = [(0.045, 0.183), (0.056, 0.180)]
PREOPERCLE = [(0.155, 0.165), (0.158, 0.21), (0.16, 0.25), (0.15, 0.275), (0.13, 0.288), (0.11, 0.292)]
OPERCLE = [(0.205, 0.140), (0.228, 0.165), (0.245, 0.19), (0.255, 0.205), (0.245, 0.225), (0.235, 0.245),
           (0.222, 0.265), (0.205, 0.284), (0.185, 0.297), (0.165, 0.303)]
LATERAL_LINE = [(0.215, 0.155), (0.26, 0.148), (0.30, 0.145), (0.40, 0.142), (0.50, 0.152), (0.60, 0.170),
                (0.70, 0.195), (0.78, 0.212), (0.835, 0.223)]

# Dark bars: centre at the back, half width at the back, how far down (fraction of the depth), forked at the top.
BARS = [(0.275, 0.020, 0.62, True), (0.375, 0.022, 0.70, False), (0.49, 0.022, 0.72, True),
        (0.61, 0.020, 0.70, False), (0.715, 0.017, 0.66, False), (0.795, 0.011, 0.52, False)]

# Fins: rays from points along the base to points along the outer edge (both front to back, or top to bottom).
FINS = {
    "FinDorsal1": dict(base=[(0.250, 0.104), (0.30, 0.099), (0.40, 0.099), (0.48, 0.112), (0.532, 0.122)],
                       tips=[(0.262, 0.055), (0.28, 0.025), (0.30, 0.010), (0.33, 0.006), (0.36, 0.012),
                             (0.39, 0.020), (0.42, 0.027), (0.45, 0.034), (0.48, 0.045), (0.505, 0.058),
                             (0.52, 0.080), (0.533, 0.108)], rays=15, kind="spiny"),
    "FinDorsal2": dict(base=[(0.555, 0.126), (0.60, 0.134), (0.65, 0.149), (0.70, 0.169), (0.722, 0.180)],
                       tips=[(0.575, 0.090), (0.60, 0.075), (0.64, 0.070), (0.665, 0.068), (0.685, 0.080),
                             (0.715, 0.110), (0.74, 0.140), (0.76, 0.162)], rays=14, kind="soft"),
    "FinAnal": dict(base=[(0.622, 0.318), (0.66, 0.304), (0.70, 0.289), (0.727, 0.279)],
                    tips=[(0.66, 0.345), (0.69, 0.356), (0.715, 0.362), (0.735, 0.345), (0.745, 0.315),
                          (0.746, 0.290)], rays=10, kind="soft", spines=2),
    "FinCaudal": dict(base=[(0.822, 0.196), (0.832, 0.215), (0.835, 0.231), (0.832, 0.248), (0.822, 0.266)],
                      tips=[(0.998, 0.162), (0.985, 0.185), (0.97, 0.21), (0.958, 0.232), (0.954, 0.244),
                            (0.958, 0.258), (0.966, 0.278), (0.977, 0.300), (0.977, 0.312)], rays=19, kind="caudal"),
    "FinPectoral": dict(base=[(0.243, 0.268), (0.245, 0.304)],
                        tips=[(0.36, 0.266), (0.38, 0.278), (0.39, 0.295), (0.38, 0.312), (0.35, 0.326),
                              (0.31, 0.332), (0.28, 0.325)], rays=13, kind="soft", splay=32.0),
    "FinPelvic": dict(base=[(0.256, 0.368), (0.272, 0.376)],
                      tips=[(0.300, 0.483), (0.32, 0.478), (0.34, 0.465), (0.356, 0.448), (0.366, 0.435)],
                      rays=6, kind="soft", spines=1, droop=25.0),
}

# Colours (sRGB), measured on the reference photos and evened out.
RIDGE, UPPER, MID, LOWER, BELLY = (0.17, 0.22, 0.12), (0.36, 0.43, 0.20), (0.56, 0.60, 0.30), (0.76, 0.77, 0.55), \
    (0.93, 0.92, 0.84)
MID2 = (0.66, 0.67, 0.40)
BAR = (0.10, 0.13, 0.08)
HEAD_TOP, CHEEK, THROAT, LIP = (0.22, 0.27, 0.13), (0.58, 0.56, 0.29), (0.88, 0.86, 0.74), (0.78, 0.70, 0.50)
FIN_COLOURS = {
    "FinDorsal1": dict(membrane=(0.36, 0.40, 0.36), ray=(0.20, 0.22, 0.18), alpha=0.86),
    "FinDorsal2": dict(membrane=(0.52, 0.54, 0.30), ray=(0.32, 0.34, 0.20), alpha=0.72),
    "FinAnal": dict(membrane=(0.93, 0.36, 0.12), ray=(0.78, 0.25, 0.08), alpha=0.82, spine=(0.95, 0.92, 0.82)),
    "FinCaudal": dict(membrane=(0.45, 0.46, 0.27), ray=(0.30, 0.30, 0.18), alpha=0.85, lower=(0.92, 0.40, 0.14)),
    "FinPectoral": dict(membrane=(0.93, 0.62, 0.22), ray=(0.80, 0.45, 0.14), alpha=0.72),
    "FinPelvic": dict(membrane=(0.94, 0.38, 0.13), ray=(0.80, 0.27, 0.08), alpha=0.85, spine=(0.96, 0.94, 0.86)),
}

UV_W, UV_Y0, UV_H = 0.86, 0.07, 0.36     # the body skin covers photo x 0..0.86, y 0.07..0.43


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
    return interp(WIDTH, x) * (interp(BOTTOM, x) - interp(TOP, x)) / 2


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
    n_exp = np.where(sn >= 0, N_TOP, N_BOTTOM)
    lat = hw[:, None] * np.sign(cs) * np.abs(cs) ** (2 / n_exp)
    up = hh[:, None] * np.sign(sn) * np.abs(sn) ** (2 / n_exp)
    px = np.repeat(rings_x[:, None], n_seg, axis=1)
    py = yc[:, None] - up
    side = np.abs(cs)[None, :] ** 0.5               # head details on the flanks, not on the crown or the throat

    # Eye socket: a hollow the eyeball sits in.
    d_eye = np.hypot(px - EYE[0], py - EYE[1]) / EYE_R
    lat -= np.sign(lat) * side * 0.0045 * (1 - smoothstep(1.0, 1.5, d_eye))
    # Mouth: the gape as a groove between two lips.
    dm = polyline_distance(px, py, GAPE)
    head = px < 0.1
    lat -= np.sign(lat) * side * head * (0.0024 * np.exp(-(dm / 0.0022) ** 2) - 0.0010 * np.exp(-((dm - 0.0055) / 0.0035) ** 2))
    # Gill cover: the body behind its rear edge sits lower, so the edge stands out as a ledge.
    ox = opercle_x(py)
    behind = np.nan_to_num(px - ox, nan=-1.0)
    lat -= np.sign(lat) * side * np.where(behind > 0, 0.003 * (1 - smoothstep(0.0, 0.035, behind)), 0.0)
    # Preopercle: a shallow groove.
    dp = polyline_distance(px, py, PREOPERCLE)
    lat -= np.sign(lat) * side * 0.0012 * np.exp(-(dp / 0.0025) ** 2)

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


def paint_fin(name, spec, w=1024, h=512):
    n = spec["rays"]
    col = FIN_COLOURS[name]
    v, u = np.mgrid[0:h, 0:w].astype(np.float64)
    u, v = (u + 0.5) / w, (v + 0.5) / h                 # u across the rays, v from the base (0) to the edge (1)
    f = u * (n - 1)
    ray_i = np.round(f)
    du = np.abs(f - ray_i)                               # 0 on a ray, 0.5 halfway between two
    spiny = spec["kind"] == "spiny"
    rng = np.random.default_rng(len(name))
    # The membrane stops short of the rays' tips: deep notches between the spines, a gentle scallop elsewhere.
    jitter = rng.uniform(-0.02, 0.02, n + 1)[ray_i.astype(int)]
    edge = 1.0 - (0.22 if spiny else 0.05) * np.sin(np.pi * du) ** (0.7 if spiny else 1.2) + jitter * (du < 0.5)
    membrane = smoothstep(edge, edge - 0.025, v)
    ray_w = (0.16 if spiny else 0.11) * (1 - 0.5 * v)
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
    base_c = np.array(col["membrane"])
    c = np.ones((h, w, 3)) * base_c
    if "lower" in col:                                    # tail: olive above, orange-red lower lobe
        c = c * (1 - smoothstep(0.35, 0.8, u))[..., None] + np.array(col["lower"]) * smoothstep(0.35, 0.8, u)[..., None]
    c = c * (0.82 + 0.18 * v)[..., None]                  # the membrane brightens towards the edge
    ray_c = np.array(col["ray"])
    c = c * (1 - ray[..., None]) + ray_c * ray[..., None]
    if spiny:
        # The perch's black spot at the back of the first dorsal fin, and a dusky edge.
        spot = smoothstep(1.0, 0.6, np.hypot((u - 0.88) / 0.13, (v - 0.42) / 0.33))
        c = c * (1 - 0.92 * spot[..., None]) + np.array([0.03, 0.03, 0.03]) * 0.92 * spot[..., None]
        c = c * (1 - 0.35 * smoothstep(0.7, 0.95, v))[..., None]
        membrane = np.maximum(membrane, spot * smoothstep(edge, edge - 0.03, v))
    for k in range(spec.get("spines", 0)):
        sp = smoothstep(0.14, 0.05, np.abs(f - k)) * smoothstep(1.0, 0.95, v)
        c = c * (1 - sp[..., None]) + np.array(col["spine"]) * sp[..., None]
        ray = np.maximum(ray, sp)
    c *= (1 + rng.normal(0, 0.025, (h, w)))[..., None]
    alpha = np.maximum(membrane * col["alpha"] * (0.9 + 0.1 * (1 - v)), ray)
    alpha *= smoothstep(0.0, 0.015, u) * smoothstep(1.0, 0.985, u) if spec["kind"] == "caudal" else 1
    rgba = np.dstack([np.clip(c, 0, 1), np.clip(alpha, 0, 1)])
    return Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")


# ------------------------------------------------------------------ body skin
def paint_body(w=3072):
    h = int(round(w * UV_H / UV_W))
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    x = (xx + 0.5) / w * UV_W
    y = UV_Y0 + (yy + 0.5) / h * UV_H
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
    # Head: dark crown, brassy cheeks and gill cover, a pale throat.
    hc = np.array(CHEEK) * np.ones((h, w, 3))
    hc = hc * (1 - smoothstep(0.35, 0.05, rel))[..., None] + np.array(HEAD_TOP) * smoothstep(0.35, 0.05, rel)[..., None]
    hc = hc * (1 - smoothstep(0.74, 0.95, rel))[..., None] + np.array(THROAT) * smoothstep(0.74, 0.95, rel)[..., None]
    gill = smoothstep(0.004, 0.0, np.abs(x - 0.5 * (np.nan_to_num(ox, nan=0.2) + 0.158)) - 0.5 * (np.nan_to_num(ox, nan=0.2) - 0.158))
    hc = hc * (1 + 0.08 * gill)[..., None]               # the gill cover a touch lighter, like polished brass
    c = c * (1 - head_w[..., None]) + hc * head_w[..., None]

    # Scales: staggered rows of small ctenoid scales whose free rear edges catch the light; tiny ones on the cheeks.
    for size, mask in ((0.0105, 1 - head_w), (0.0062, head_w * smoothstep(0.12, 0.17, x) * (rel > 0.15))):
        sy = size * 0.72
        ys = y - 0.16 * x                                  # scale rows run obliquely down towards the head
        row = np.floor(ys / sy)
        gx = x / size + 0.5 * (row % 2)
        fx, fy = gx - np.floor(gx), ys / sy - row
        r = np.hypot((fx - 0.35) / 0.75, (fy - 0.5) / 0.62)
        cell = (np.floor(gx) * 7.3 + row * 3.1) % 1.0          # each scale a little lighter or darker
        rim = smoothstep(0.62, 0.95, r) * (fx > 0.35)
        shade = 1 - 0.08 * rim + 0.04 * smoothstep(0.7, 0.0, r) + 0.05 * (cell - 0.5)
        c *= (1 + (shade - 1) * mask)[..., None]
        height += mask * (0.4 * smoothstep(1.0, 0.0, r) - 0.35 * rim)

    # Bars, darkest at the back, fading down the flank; the first and third fork at the top.
    bar = np.zeros((h, w))
    for cx, hw, end, fork in BARS:
        centre = cx + 0.012 * (rel - 0.3) + 0.004 * np.sin(rel * 9 + cx * 40)
        width = hw * (1 - 0.45 * np.clip(rel, 0, 1) / end)
        strength = smoothstep(end, end - 0.15, rel) * smoothstep(-0.05, 0.06, rel)
        if fork:
            f = smoothstep(0.34, 0.1, rel)
            d = np.minimum(np.abs(x - centre - 0.013 * f), np.abs(x - centre + 0.013 * f))
            width = width * (1 - 0.35 * f)
        else:
            d = np.abs(x - centre)
        bar = np.maximum(bar, smoothstep(width, width - 0.006, d + 0.002 * rng.normal(0, 1, (h, w))) * strength)
    bar *= 1 - head_w
    c = c * (1 - 0.85 * bar[..., None]) + np.array(BAR) * 0.85 * bar[..., None]

    # Lateral line: a row of pale, pored scales.
    ll = interp(LATERAL_LINE, x)
    on_line = smoothstep(0.0022, 0.0008, np.abs(y - ll)) * (x > 0.215) * (x < X1)
    pore = (np.sin(x / 0.0105 * 2 * np.pi) > 0.6)
    c = c * (1 + 0.18 * on_line * (1 - pore))[..., None] * (1 - 0.35 * on_line * pore)[..., None]
    height -= 0.6 * on_line * pore

    # Head details: the gape with pale lips, the maxilla, nostrils, the preopercle with its serrated edge, the gill
    # cover's dark rear edge with a light rim, its spine, and the dark socket round the eye.
    dm = polyline_distance(x, y, GAPE)
    lips = smoothstep(0.006, 0.002, dm)
    c = c * (1 - 0.6 * lips[..., None]) + np.array(LIP) * 0.6 * lips[..., None]
    c *= (1 - 0.75 * smoothstep(0.0016, 0.0006, dm))[..., None]
    height -= 0.8 * smoothstep(0.002, 0.0, dm)
    c *= (1 - 0.35 * smoothstep(0.0014, 0.0005, polyline_distance(x, y, MAXILLA)))[..., None]
    for nx, ny in NOSTRILS:
        pit = smoothstep(0.0028, 0.0012, np.hypot(x - nx, y - ny))
        c *= (1 - 0.7 * pit)[..., None]
        height -= 0.6 * pit
    dp = polyline_distance(x, y, PREOPERCLE)
    c *= (1 - 0.3 * smoothstep(0.0018, 0.0006, dp))[..., None]
    teeth = smoothstep(0.004, 0.0015, dp) * (np.sin((x + y) / 0.0035 * np.pi) > 0.7) * (y > 0.255)
    c *= (1 - 0.3 * teeth)[..., None]
    height -= 0.5 * smoothstep(0.002, 0.0, dp)
    do = polyline_distance(x, y, OPERCLE)
    behind = np.nan_to_num(x - ox, nan=-1)
    c *= (1 - 0.55 * smoothstep(0.0022, 0.0008, do) * (behind >= -0.001))[..., None]
    c *= (1 + 0.15 * smoothstep(0.005, 0.0015, do) * (behind < 0))[..., None]
    spine = smoothstep(0.004, 0.0015, np.hypot((x - 0.258) / 1.8, y - 0.205))
    c *= (1 - 0.7 * spine)[..., None]
    de = np.hypot(x - EYE[0], y - EYE[1]) / EYE_R
    c *= (1 - 0.5 * smoothstep(1.0, 1.08, de) * smoothstep(1.45, 1.15, de))[..., None]
    height += 0.6 * smoothstep(1.0, 1.15, de) * smoothstep(1.5, 1.2, de)

    # A touch of mottling, and the dorsal ridge darker.
    noise = np.asarray(Image.fromarray((rng.random((h // 16, w // 16)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)) / 255.0
    c *= (0.94 + 0.12 * noise)[..., None]
    c *= (1 - 0.25 * smoothstep(0.08, -0.05, rel))[..., None]
    albedo = Image.fromarray((np.clip(c, 0, 1) * 255).astype(np.uint8), "RGB")

    # Normal map from the heights (OpenGL convention, as Godot uses).
    hs = height * 1.0
    gx = (np.roll(hs, -1, axis=1) - np.roll(hs, 1, axis=1)) * 0.5
    gy = (np.roll(hs, -1, axis=0) - np.roll(hs, 1, axis=0)) * 0.5
    n = np.dstack([-gx * 2.2, gy * 2.2, np.ones_like(hs)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    normal = Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB")
    return albedo, normal


# ------------------------------------------------------------------ eye
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
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    r = np.hypot(xx + 0.5 - size / 2, yy + 0.5 - size / 2) / (size / 2)
    ang = np.arctan2(yy - size / 2, xx - size / 2)
    rng = np.random.default_rng(3)
    streaks = np.interp(ang, np.linspace(-np.pi, np.pi, 90), rng.random(90))
    gold = np.array([0.93, 0.72, 0.20]) * (0.8 + 0.3 * streaks)[..., None]
    c = gold * (1 - smoothstep(0.62, 0.86, r))[..., None] + np.array([0.35, 0.28, 0.10]) * smoothstep(0.62, 0.86, r)[..., None]
    c = c * (1 - smoothstep(0.86, 0.95, r))[..., None] + np.array([0.10, 0.11, 0.07]) * smoothstep(0.86, 0.95, r)[..., None]
    pupil = smoothstep(0.42, 0.38, r)
    c = c * (1 - pupil[..., None]) + np.array([0.01, 0.01, 0.015]) * pupil[..., None]
    c *= (1 - 0.25 * smoothstep(0.38, 0.5, r) * smoothstep(0.62, 0.5, r))[..., None]   # dark ring round the pupil
    return Image.fromarray((np.clip(c, 0, 1) * 255).astype(np.uint8), "RGB")


# ------------------------------------------------------------------ reference for the lake model
SOURCES = [
    ("File:Perche commune perca fluviatilis Alfort.jpg", "Jebulon", "CC0",
     "https://commons.wikimedia.org/wiki/File:Perche_commune_perca_fluviatilis_Alfort.jpg"),
    ("File:Perca fluviatilis - Perche commune - European perch Cropped.jpg", "Gilles San Martin", "CC BY-SA 2.5",
     "https://commons.wikimedia.org/wiki/File:Perca_fluviatilis_-_Perche_commune_-_European_perch_Cropped.jpg"),
    ("File:Perca fluviatilis Aquarium Liège 30012016 1.jpg", "Vassil", "CC0",
     "https://commons.wikimedia.org/wiki/File:Perca_fluviatilis_Aquarium_Li%C3%A8ge_30012016_1.jpg"),
]


def write_reference(path):
    """The same outline and colours for the lofted perch of the lake (FishReference), with the photos' credit."""
    import json
    xs = np.linspace(X0, X1, 32)
    up, down = (AXIS - interp(TOP, xs)) * DEPTH_SCALE, (interp(BOTTOM, xs) - AXIS) * DEPTH_SCALE
    meta = {
        "aspect": 1.0, "body_start": X0, "body_end": X1, "snout": X0,
        "outline": [[round(float(AXIS - u), 4), round(float(AXIS + d), 4)] for u, d in zip(up, down)],
        "profile": [[round(float(u), 4), round(float(d), 4)] for u, d in zip(up, down)],
        "palette": {"back": list(UPPER), "flank": list(MID), "belly": list(BELLY)},
        "title": "; ".join(t for t, _, _, _ in SOURCES),
        "author": "; ".join(a for _, a, _, _ in SOURCES),
        "licence": "; ".join(lic for _, _, lic, _ in SOURCES),
        "source": " ".join(u for _, _, _, u in SOURCES),
        "credit": "Model według zdjęć: " + ", ".join(f"{a} ({lic})" for _, a, lic, _ in SOURCES) + ", Wikimedia Commons",
    }
    json.dump(meta, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=0)


# ------------------------------------------------------------------ main
def main():
    out = sys.argv[1]
    preview = sys.argv[2] if len(sys.argv) > 2 else None
    g = Glb()
    pos, nrm, uv, idx = build_body()
    albedo, normal = paint_body()
    body_mat = g.material("Body", g.image(albedo, "body", jpeg=True), g.image(normal, "body_normal", jpeg=True), roughness=0.4)
    mesh = g.mesh("Body", pos, nrm, uv, idx, body_mat)
    g.doc["meshes"][mesh]["primitives"][0]["attributes"]["TANGENT"] = g._accessor(tangents(nrm), "VEC4", 34962)
    g.node("Body", mesh)

    for name, spec in FINS.items():
        fpos, fuv, fidx = build_fin(spec)
        tex = paint_fin(name, spec)
        mat = g.material(name, g.image(tex, name), blend=True, roughness=0.45)
        if preview:
            tex.save(os.path.join(preview, name + ".png"))
        if "splay" in spec or "droop" in spec:
            # Paired fins: the right one, and the left one mirrored across the fish.
            mirror = fpos * np.array([-1, 1, 1], dtype=np.float32)
            both = np.vstack([fpos, mirror])
            bidx = np.vstack([fidx, fidx[:, [0, 2, 1]] + len(fpos)])
            m = g.mesh(name, both, vertex_normals(both, bidx), np.vstack([fuv, fuv]), bidx, mat)
        else:
            m = g.mesh(name, fpos, vertex_normals(fpos, fidx), fuv, fidx, mat)
        g.node(name, m)

    epos, enrm, euv, eidx = build_eye()
    eye_tex = paint_eye()
    eye_mat = g.material("Eye", g.image(eye_tex, "iris"), roughness=0.15)
    em = g.mesh("EyeRight", epos, enrm, euv, eidx, eye_mat)
    # The left eye is a mirrored copy (not a negative scale, which would light it inside out).
    mirror = np.array([-1, 1, 1], dtype=np.float32)
    eml = g.mesh("EyeLeft", epos * mirror, enrm * mirror, euv, eidx[:, [0, 2, 1]], eye_mat)
    # The eye sits in its socket on the side of the head.
    lat = half_width(EYE[0]) * math.sqrt(max(1 - ((EYE[1] - (interp(TOP, EYE[0]) + interp(BOTTOM, EYE[0])) / 2)
                                                   / ((interp(BOTTOM, EYE[0]) - interp(TOP, EYE[0])) / 2)) ** 2, 0.05))
    centre = model(np.array(EYE[0]), np.array(EYE[1]), np.array(lat - 0.0045))
    g.node("EyeRight", em, translation=centre)
    g.node("EyeLeft", eml, translation=centre * np.array([-1, 1, 1]))

    g.save(out, extras={"species": "okon", "eye_radius": EYE_R * 1.08 * DEPTH_SCALE,
                        "sources": ["File:Perche commune perca fluviatilis Alfort.jpg",
                                    "File:Perca fluviatilis - Perche commune - European perch Cropped.jpg",
                                    "File:Perca fluviatilis Aquarium Liège 30012016 1.jpg"]})
    write_reference(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(out))), "okon.json"))
    if preview:
        albedo.save(os.path.join(preview, "body.jpg"), quality=90)
        normal.save(os.path.join(preview, "body_normal.png"))
        eye_tex.save(os.path.join(preview, "eye.png"))
    print(f"{out}: body {len(pos)} vertices, {os.path.getsize(out) // 1024} KB")


if __name__ == "__main__":
    main()
