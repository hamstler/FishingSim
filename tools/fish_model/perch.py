#!/usr/bin/env python3
"""
The European perch (Perca fluviatilis) as a game model, built from reference photographs and written as a .glb:
a lofted body with its head modelled (eye socket, the mouth's gape and lips, the gill cover's edge standing over the
body), separate eyes, and the fins as rayed membranes (spiny first dorsal with its black spot, soft second dorsal,
anal, forked tail, paired pectorals and pelvics splayed out from the body), each with a painted texture, and a
painted body skin with a scale normal map.

The proportions (body, head with the eye, the mouth reaching back under it and the gill cover, every fin, the bars)
were measured on the perch illustration of AtlasRyb.online (https://atlasryb.online, Perca fluviatilis); the colours
follow its description and the photographs "Perca fluviatilis - Perche commune - European perch Cropped.jpg"
(CC BY-SA 2.5) and "Perca fluviatilis Aquarium Liege 30012016 1.jpg" (CC0) on Wikimedia Commons. No picture is copied
into the model: the skin and fins are painted in code.

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

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb import Glb, vertex_normals  # noqa: E402

AXIS = 0.23           # photo y of the body axis (the snout tip and the tail stalk lie on it)
DEPTH_SCALE = 0.95    # the reference is a grown perch; the model is a medium one (FishModel grows it)

# Body outline without fins, traced every few percent of the length on the perch of AtlasRyb.online (proportions
# only; the illustration itself is not used).
TOP = [(0.004, 0.221), (0.02, 0.193), (0.05, 0.171), (0.10, 0.154), (0.15, 0.140), (0.20, 0.118), (0.25, 0.093),
       (0.30, 0.084), (0.35, 0.082), (0.40, 0.086), (0.45, 0.095), (0.50, 0.108), (0.55, 0.123), (0.60, 0.134),
       (0.65, 0.151), (0.70, 0.171), (0.75, 0.190), (0.80, 0.204), (0.845, 0.207)]
BOTTOM = [(0.004, 0.241), (0.02, 0.251), (0.05, 0.260), (0.10, 0.271), (0.15, 0.278), (0.20, 0.294), (0.25, 0.316),
          (0.30, 0.344), (0.35, 0.361), (0.40, 0.366), (0.45, 0.367), (0.50, 0.365), (0.55, 0.355), (0.60, 0.341),
          (0.65, 0.322), (0.70, 0.302), (0.75, 0.283), (0.80, 0.266), (0.845, 0.260)]
X0, X1 = 0.004, 0.845          # snout tip, end of the tail stalk (the caudal fin's base)

# Width / depth of the body along it: a broad head, a laterally flattened body.
# Half width of the body (total lengths): a broad, blunt snout and head seen from above, a flattened body.
HALF_WIDTH = [(0.0, 0.007), (0.012, 0.017), (0.03, 0.024), (0.06, 0.031), (0.10, 0.037), (0.15, 0.043),
              (0.22, 0.050), (0.30, 0.056), (0.40, 0.059), (0.50, 0.057), (0.60, 0.050), (0.70, 0.040), (0.80, 0.026),
              (0.845, 0.020)]
# Cross-section: a broad rounded crown on the head, a narrow back under the dorsal fins, a full rounded belly.
N_TOP_ALONG = [(0.0, 2.4), (0.2, 2.3), (0.32, 1.8), (1.0, 1.75)]
N_BOTTOM = 2.3

# Head.
EYE = (0.098, 0.187)
EYE_R = 0.018
GAPE = [(0.003, 0.231), (0.03, 0.237), (0.055, 0.244), (0.07, 0.250), (0.08, 0.257)]   # its corner under the eye
MAXILLA = [(0.03, 0.234), (0.06, 0.242), (0.083, 0.253)]
NOSTRILS = [(0.045, 0.186), (0.057, 0.183)]
PREOPERCLE = [(0.155, 0.155), (0.158, 0.21), (0.15, 0.242), (0.125, 0.265), (0.10, 0.272)]
OPERCLE = [(0.172, 0.142), (0.21, 0.155), (0.24, 0.175), (0.255, 0.190), (0.24, 0.215), (0.228, 0.235),
           (0.21, 0.255), (0.19, 0.270), (0.168, 0.280)]
LATERAL_LINE = [(0.25, 0.135), (0.30, 0.128), (0.40, 0.130), (0.50, 0.148), (0.60, 0.170), (0.70, 0.200),
                (0.78, 0.218), (0.845, 0.226)]

# Dark bars: centre at the back, half width at the back, how far down (fraction of the depth), forked at the top.
BARS = [(0.32, 0.025, 0.65, False), (0.415, 0.030, 0.75, True), (0.515, 0.030, 0.75, False),
        (0.605, 0.026, 0.72, True), (0.685, 0.022, 0.68, False), (0.765, 0.017, 0.60, False), (0.835, 0.011, 0.5, False)]

# Fins: rays from points along the base to points along the outer edge (both front to back, or top to bottom).
FINS = {
    "FinDorsal1": dict(base=[(0.255, 0.098), (0.30, 0.089), (0.40, 0.091), (0.50, 0.113), (0.555, 0.128)],
                       tips=[(0.262, 0.065), (0.276, 0.030), (0.29, 0.003), (0.318, -0.005), (0.357, -0.022),
                             (0.394, -0.033), (0.43, -0.029), (0.455, -0.014), (0.477, 0.008), (0.497, 0.030),
                             (0.515, 0.055), (0.53, 0.079), (0.553, 0.126)], rays=15, kind="spiny"),
    "FinDorsal2": dict(base=[(0.575, 0.137), (0.62, 0.147), (0.67, 0.164), (0.72, 0.182), (0.736, 0.188)],
                       tips=[(0.59, 0.105), (0.603, 0.093), (0.631, 0.098), (0.66, 0.110), (0.687, 0.123),
                             (0.71, 0.138), (0.732, 0.154), (0.755, 0.166), (0.774, 0.174)], rays=14, kind="soft"),
    "FinAnal": dict(base=[(0.605, 0.336), (0.65, 0.318), (0.70, 0.298), (0.745, 0.282)],
                    tips=[(0.635, 0.358), (0.668, 0.365), (0.698, 0.367), (0.724, 0.365), (0.749, 0.361),
                          (0.757, 0.335), (0.752, 0.306)], rays=10, kind="soft", spines=2),
    "FinCaudal": dict(base=[(0.836, 0.206), (0.845, 0.220), (0.848, 0.234), (0.845, 0.248), (0.836, 0.261)],
                      tips=[(0.992, 0.134), (0.972, 0.165), (0.95, 0.199), (0.933, 0.235), (0.95, 0.265),
                            (0.975, 0.299), (0.989, 0.330)], rays=19, kind="caudal"),
    "FinPectoral": dict(base=[(0.243, 0.266), (0.249, 0.288)],
                        tips=[(0.30, 0.242), (0.345, 0.245), (0.372, 0.256), (0.383, 0.271), (0.372, 0.288),
                              (0.34, 0.304), (0.30, 0.310), (0.27, 0.303)], rays=13, kind="soft", splay=30.0,
                        camber=0.004),
    "FinPelvic": dict(base=[(0.245, 0.313), (0.270, 0.324)],
                      tips=[(0.318, 0.435), (0.338, 0.425), (0.352, 0.410), (0.356, 0.385), (0.345, 0.362)],
                      rays=6, kind="soft", spines=1, droop=25.0),
}

# Colours (sRGB), measured on the reference photos and evened out.
RIDGE, UPPER, MID, LOWER, BELLY = (0.07, 0.10, 0.08), (0.16, 0.20, 0.12), (0.62, 0.54, 0.21), (0.84, 0.80, 0.60), \
    (0.94, 0.93, 0.88)
MID2 = (0.74, 0.66, 0.32)
BAR = (0.08, 0.11, 0.08)
HEAD_TOP, CHEEK, THROAT, LIP = (0.14, 0.17, 0.12), (0.42, 0.41, 0.24), (0.86, 0.85, 0.77), (0.64, 0.63, 0.50)
FIN_COLOURS = {
    "FinDorsal1": dict(membrane=(0.27, 0.25, 0.21), ray=(0.66, 0.60, 0.48), alpha=0.72),
    "FinDorsal2": dict(membrane=(0.46, 0.39, 0.24), ray=(0.25, 0.20, 0.12), alpha=0.62),
    "FinAnal": dict(membrane=(0.95, 0.42, 0.17), ray=(0.62, 0.16, 0.06), alpha=0.72, spine=(0.95, 0.92, 0.82)),
    "FinCaudal": dict(membrane=(0.92, 0.45, 0.20), ray=(0.58, 0.17, 0.07), alpha=0.75, lower=(0.95, 0.40, 0.16)),
    "FinPectoral": dict(membrane=(0.90, 0.72, 0.52), ray=(0.60, 0.36, 0.22), alpha=0.5),
    "FinPelvic": dict(membrane=(0.95, 0.42, 0.17), ray=(0.62, 0.16, 0.06), alpha=0.75, spine=(0.96, 0.94, 0.86)),
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
    return interp(HALF_WIDTH, x)


def n_top(x):
    return interp(N_TOP_ALONG, x)


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
    n_exp = np.where(sn[None, :] >= 0, n_top(xc)[:, None], N_BOTTOM)
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
    """How far the snout's surface is pushed back at the mouth: the gape opens across the front of the head."""
    dy = py - interp(GAPE, np.clip(px, GAPE[0][0], GAPE[-1][0]))
    return 0.008 * np.exp(-(dy / 0.003) ** 2) * smoothstep(X0 + 0.035, X0, px)


def mouth_open(px, py):
    """The jaws stand a little apart at the front, as a perch holds its mouth: upper lip up, lower lip down."""
    dy = py - interp(GAPE, np.clip(px, GAPE[0][0], GAPE[-1][0]))
    inside_cap = smoothstep(X0 - 0.0025, X0 + 0.002, px)      # the rounded tip in front of the first ring stays whole
    return 0.0022 * np.tanh(dy / 0.002) * np.exp(-(dy / 0.008) ** 2) * smoothstep(0.06, X0, px) * inside_cap


def sculpt_head(px, py, lat, cs):
    """The head's relief on the lofted surface: eye socket, gape and lips, the gill cover's ledge, the preopercle."""
    lat = lat.copy()
    side = np.abs(cs)[None, :] ** 0.5               # head details on the flanks, not on the crown or the throat
    # Eye socket: a hollow the eyeball sits in.
    d_eye = np.hypot(px - EYE[0], py - EYE[1]) / EYE_R
    lat -= np.sign(lat) * side * 0.006 * (1 - smoothstep(1.0, 1.45, d_eye))
    # Mouth: the gape as a groove between two lips.
    dm = polyline_distance(px, py, GAPE)
    head = smoothstep(0.1, 0.085, px)
    taper = 1.0 - 0.5 * np.clip(px / 0.08, 0, 1)                 # the gape is widest at the front
    lat -= np.sign(lat) * side * head * (0.0045 * taper * np.exp(-(dm / 0.0022) ** 2)
                                         - 0.0028 * np.exp(-((dm - 0.0055) / 0.0032) ** 2))
    # Gill cover: the body behind its rear edge sits lower, so the edge stands out as a ledge.
    ox = opercle_x(py)
    behind = np.nan_to_num(px - ox, nan=-1.0)
    lat -= np.sign(lat) * side * np.where(behind > 0, 0.0045 * (1 - smoothstep(0.0, 0.03, behind)), 0.0)
    # Preopercle: a shallow groove.
    dp = polyline_distance(px, py, PREOPERCLE)
    lat -= np.sign(lat) * side * 0.0012 * np.exp(-(dp / 0.0025) ** 2)
    return lat


def build_body_unwrapped(n_rings=150, n_seg=96):
    """
    The body for the Blender build: quads, the skin unwrapped once round the body (u from the snout to the tail
    stalk, v from the dorsal midline round the right flank, the belly and the left flank back to it), so the texture
    neither stretches over the back nor the belly. Returns model-space positions, UVs and faces (quads and triangles).
    """
    s = np.linspace(0, 1, n_rings)
    xs = X0 + (X1 - X0) * s ** 1.3
    cap = 6
    rings_x, rings_k = [], []
    for k in range(cap, 0, -1):
        phi = math.pi / 2 * k / cap
        rings_x.append(X0 - 0.0028 * math.sin(phi))
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
    n_exp = np.where(sn[None, :] >= 0, n_top(xc)[:, None], N_BOTTOM)
    lat = hw[:, None] * np.sign(cs) * np.abs(cs) ** (2 / n_exp)
    up = hh[:, None] * np.sign(sn) * np.abs(sn) ** (2 / n_exp)
    px = np.repeat(rings_x[:, None], n_seg + 1, axis=1)
    py = yc[:, None] - up
    lat = sculpt_head(px, py, lat, cs)
    pos = model(px + mouth_cleft(px, py), py + mouth_open(px, py), lat).reshape(-1, 3)
    u = np.clip((rings_x - X0) / (X1 - X0), 0, 1)
    uv = np.stack([np.repeat(u[:, None], n_seg + 1, axis=1), np.repeat(v[None, :], len(rings_x), axis=0)], axis=-1).reshape(-1, 2)
    m = n_seg + 1
    faces = []
    for i in range(len(rings_x) - 1):
        for j in range(n_seg):
            a = i * m + j
            faces.append((a, a + m, a + m + 1, a + 1))
    tip = len(pos)
    pos = np.vstack([pos, model(np.array([X0 - 0.003]), np.array([(interp(TOP, X0) + interp(BOTTOM, X0)) / 2]), np.zeros(1))])
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


def paint_fin(name, spec, w=640, h=320):
    from PIL import Image
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
    notch = 0.42 if spiny else (0.02 if spec["kind"] == "caudal" else 0.035)
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
    base_c = np.array(col["membrane"])
    c = np.ones((h, w, 3)) * base_c
    if "lower" in col:                                    # tail: olive above, orange-red lower lobe
        c = c * (1 - smoothstep(0.35, 0.8, u))[..., None] + np.array(col["lower"]) * smoothstep(0.35, 0.8, u)[..., None]
    c = c * (0.82 + 0.18 * v)[..., None]                  # the membrane brightens towards the edge
    if spec["kind"] == "caudal":                          # the tail's base is olive like the stalk
        c = c * (1 - smoothstep(0.25, 0.0, v))[..., None] + np.array([0.36, 0.36, 0.20]) * smoothstep(0.25, 0.0, v)[..., None]
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
    # Rays are opaque; the membrane between them thins out towards the edge.
    alpha = np.maximum(membrane * col["alpha"] * (1.0 - 0.3 * v), ray * 0.98)
    alpha *= smoothstep(0.0, 0.015, u) * smoothstep(1.0, 0.985, u) if spec["kind"] == "caudal" else 1
    rgba = np.dstack([np.clip(c, 0, 1), np.clip(alpha, 0, 1)])
    return Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")


# ------------------------------------------------------------------ body skin
def paint_body(w=3072):
    """The skin in side projection (u = photo x, v = photo y), for the model this script writes itself."""
    from PIL import Image
    h = int(round(w * UV_H / UV_W))
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    x = (xx + 0.5) / w * UV_W
    y = UV_Y0 + (yy + 0.5) / h * UV_H
    c, height = skin(x, y, y)
    albedo = Image.fromarray((np.clip(c, 0, 1) * 255).astype(np.uint8), "RGB")
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    gy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    n = np.dstack([-gx * 2.2, gy * 2.2, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    normal = Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB")
    return albedo, normal


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
    n_exp = np.where(sn[:, None] >= 0, n_top(x)[None, :], N_BOTTOM)
    lat = hw[None, :] * np.sign(cs)[:, None] * np.abs(cs)[:, None] ** (2 / n_exp)
    up = hh[None, :] * np.sign(sn)[:, None] * np.abs(sn)[:, None] ** (2 / n_exp)
    py = yc[None, :] - up
    step = np.hypot(np.diff(lat, axis=0), np.diff(up, axis=0))
    cum = np.vstack([np.zeros((1, w)), np.cumsum(step, axis=0)])
    total = cum[-1] + np.hypot(lat[0] - lat[-1], up[0] - up[-1])
    s = np.minimum(cum, total[None, :] - cum)
    ds = np.gradient(cum, axis=0)
    return np.broadcast_to(x[None, :], (h, w)), py, s, ds, np.broadcast_to(np.abs(cs)[:, None] ** 0.5, (h, w))


def write_textures(work, w=4096, h=2048):
    """Textures for the Blender build (perch_blender.py): the unwrapped skin and its normal map, fins, iris."""
    from PIL import Image
    os.makedirs(work, exist_ok=True)
    x, y, s, ds, side = unwrap_coords(w, h)
    c, height = skin(x, y, s, side)
    Image.fromarray((np.clip(c, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(work, "body_albedo.jpg"), quality=93)
    # Normal map from the heights, in real lengths along u and round the body (OpenGL convention: green up the image).
    k = 0.00062
    dx = (X1 - X0) / w
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 / dx
    gy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 / np.maximum(ds, 1e-6)
    n = np.dstack([-gx * k, gy * k, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB").resize((w // 2, h // 2), Image.LANCZOS) \
        .save(os.path.join(work, "body_normal.jpg"), quality=93)
    for name, spec in FINS.items():
        paint_fin(name, spec).save(os.path.join(work, name + ".png"))
    paint_eye().save(os.path.join(work, "iris.png"))
    write_reference(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "fish", "okon.json"))
    print(f"textures in {work}")


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
    # Head: dark crown, brassy cheeks and gill cover, a pale throat.
    hc = np.array(CHEEK) * np.ones((h, w, 3))
    hc = hc * (1 - smoothstep(0.35, 0.05, rel))[..., None] + np.array(HEAD_TOP) * smoothstep(0.35, 0.05, rel)[..., None]
    hc = hc * (1 - smoothstep(0.80, 0.97, rel))[..., None] + np.array(THROAT) * smoothstep(0.80, 0.97, rel)[..., None]
    gill = smoothstep(0.004, 0.0, np.abs(x - 0.5 * (np.nan_to_num(ox, nan=0.2) + 0.158)) - 0.5 * (np.nan_to_num(ox, nan=0.2) - 0.158))
    hc = hc * (1 + 0.08 * gill)[..., None]               # the gill cover a touch lighter, like polished brass
    c = c * (1 - head_w[..., None]) + hc * head_w[..., None]

    # Scales: small ctenoid scales, each showing its rounded free rear part with a dark, rough rim, a lighter, glinting
    # middle, its own tint and a few pigment cells; tiny ones on the cheeks and the upper gill cover.
    sc = {}
    for name, size, mask in (("body", 0.0092, 1 - head_w),
                             ("cheek", 0.0048, head_w * smoothstep(0.11, 0.15, x) * smoothstep(0.30, 0.42, rel)
                              * smoothstep(0.92, 0.75, rel))):
        f = scale_field(x, s, size)
        sc[name] = f
        h1, h2, h3 = _hash(f["i"], f["j"]), _hash(f["i"], f["j"], 1), _hash(f["i"], f["j"], 2)
        # The free edge darkens (pigment along the scale's rim, the shadow it casts on the scale behind).
        rim = smoothstep(0.78, 1.0, f["d"]) * smoothstep(-0.1, 0.45, f["dx"])
        glint = np.exp(-((f["dx"] - 0.1) ** 2 + f["dy"] ** 2) / 0.10)
        shade = 1 - 0.26 * rim + 0.07 * glint + 0.08 * (h1 - 0.5)
        tint = np.stack([1 + 0.05 * (h2 - 0.5), 1 + 0.02 * (h1 - 0.5), 1 - 0.06 * (h2 - 0.5)], axis=-1)
        c = c * (1 + (shade[..., None] * tint - 1) * mask[..., None])
        # Each scale a plate rising towards its free edge, which drops onto the scale behind it.
        height += mask * (0.25 + 0.45 * np.clip(f["dx"], -1, 1) - 0.6 * smoothstep(0.9, 1.0, f["d"]))

    # Bars, darkest at the back, fading down the flank; the first and third fork at the top.
    # The bars are made of dark scales: whether a scale is dark is decided at its centre, so the bars' edges follow the
    # scales, ragged; they fade down the flank and some fork at the top.
    def bars_at(xq):
        out = np.zeros((h, w))
        for cx, hw, end, fork in BARS:
            centre = cx + 0.014 * (rel - 0.3) + 0.004 * np.sin(rel * 9 + cx * 40)
            width = hw * (1 - 0.6 * np.clip(rel, 0, 1) / end)          # wedge-shaped: broad at the back
            strength = smoothstep(end, end - 0.25, rel) * smoothstep(-0.05, 0.06, rel)
            if fork:
                f = smoothstep(0.34, 0.1, rel)
                d = np.minimum(np.abs(xq - centre - 0.013 * f), np.abs(xq - centre + 0.013 * f))
                width = width * (1 - 0.35 * f)
            else:
                d = np.abs(xq - centre)
            out = np.maximum(out, smoothstep(width + 0.004, width - 0.006, d) * strength)
        return out
    b = sc["body"]
    jitter = 0.25 * (_hash(b["i"], b["j"], 3) - 0.5)
    # Mostly decided per scale (ragged, scale-by-scale edges), with a soft part so the bars shade into the flank.
    bar = np.clip(0.75 * np.clip(bars_at(b["cx"]) + jitter, 0, 1) + 0.25 * bars_at(x), 0, 1)
    bar *= 1 - head_w
    c = c * (1 - 0.82 * bar[..., None]) + np.array(BAR) * 0.82 * bar[..., None]
    # Fine dark speckles on the back.
    speck = (rng.random((h, w)) > 0.9965) * smoothstep(0.45, 0.1, rel)
    speck = np.asarray(Image.fromarray((speck * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))) / 255.0
    c *= (1 - 0.35 * speck)[..., None]

    # Lateral line: a row of pale, pored scales.
    # Lateral line: the scales along it each carry a short tube (a pale ridge with a dark pore at its end).
    ll = interp(LATERAL_LINE, x)
    near = smoothstep(0.004, 0.0015, np.abs(y - ll)) * (x > 0.25) * (x < X1) * (1 - head_w)
    tube = smoothstep(0.32, 0.12, np.abs(b["dy"])) * smoothstep(-0.55, -0.3, b["dx"]) * smoothstep(0.55, 0.3, b["dx"])
    pore = smoothstep(0.2, 0.08, np.hypot(b["dx"] - 0.45, b["dy"]))
    c = c * (1 + 0.16 * near * tube)[..., None] * (1 - 0.45 * near * pore)[..., None]
    height += near * (0.5 * tube - 0.6 * pore)

    # Head details: the gape with pale lips, the maxilla, nostrils, the preopercle with its serrated edge, the gill
    # cover's dark rear edge with a light rim, its spine, and the dark socket round the eye.
    dm = polyline_distance(x, y, GAPE)
    below = y > interp(GAPE, np.clip(x, GAPE[0][0], GAPE[-1][0]))
    lips = smoothstep(0.007, 0.0025, dm) * below * smoothstep(0.09, 0.07, x)
    c = c * (1 - 0.25 * lips[..., None]) + np.array(LIP) * 0.25 * lips[..., None]
    gape = 0.0026 - 0.0012 * np.clip(x / 0.06, 0, 1) + 0.0010 * smoothstep(0.06, 0.08, x)
    c *= (1 - 0.85 * smoothstep(gape + 0.001, gape - 0.0004, dm))[..., None]
    height -= 0.8 * smoothstep(0.002, 0.0, dm)
    c *= (1 - 0.35 * smoothstep(0.0014, 0.0005, polyline_distance(x, y, MAXILLA)))[..., None]
    for nx, ny in NOSTRILS:
        pit = smoothstep(0.0028, 0.0012, np.hypot(x - nx, y - ny)) * smoothstep(0.3, 0.55, side)
        c *= (1 - 0.7 * pit)[..., None]
        height -= 0.6 * pit
    dp = polyline_distance(x, y, PREOPERCLE)
    c *= (1 - 0.3 * smoothstep(0.0018, 0.0006, dp))[..., None]
    teeth = smoothstep(0.004, 0.0015, dp) * (np.sin((x + y) / 0.0035 * np.pi) > 0.7) * (y > 0.235)
    c *= (1 - 0.3 * teeth)[..., None]
    height -= 0.5 * smoothstep(0.002, 0.0, dp)
    do = polyline_distance(x, y, OPERCLE)
    behind = np.nan_to_num(x - ox, nan=-1)
    c *= (1 - 0.55 * smoothstep(0.0022, 0.0008, do) * (behind >= -0.001))[..., None]
    c *= (1 + 0.15 * smoothstep(0.005, 0.0015, do) * (behind < 0))[..., None]
    spine = smoothstep(0.004, 0.0015, np.hypot((x - 0.258) / 1.8, y - 0.190))
    c *= (1 - 0.7 * spine)[..., None]
    # The gill cover's bone: fine rays fanning out from its joint, on its lower, scaleless part.
    operc = (behind < 0) * smoothstep(0.155, 0.17, x) * smoothstep(0.19, 0.215, y)
    ang = np.arctan2(y - 0.165, x - 0.185)
    rays = 0.5 + 0.5 * np.sin(ang * 70 + 3 * np.sin(np.hypot(x - 0.185, y - 0.165) * 300))
    c *= (1 + 0.07 * (rays - 0.5) * operc)[..., None]
    height += 0.25 * (rays - 0.5) * operc
    # The red gill membrane showing under the lower rear edge of the gill cover.
    membrane = smoothstep(0.0040, 0.0018, do) * (behind >= -0.0005) * smoothstep(0.22, 0.25, y)
    c = c * (1 - 0.45 * membrane[..., None]) + np.array([0.70, 0.32, 0.28]) * 0.45 * membrane[..., None]
    # The upper jaw's bone (maxilla): a paler plate above the gape, widening to its rounded end under the eye, outlined.
    mx = np.clip(x, MAXILLA[0][0], MAXILLA[-1][0])
    gy_ = interp(GAPE, np.clip(x, GAPE[0][0], GAPE[-1][0]))
    plate_w = 0.0025 + 0.0075 * np.clip((x - 0.03) / 0.055, 0, 1)
    above = gy_ - y
    maxilla = smoothstep(-0.0005, 0.0008, above) * smoothstep(plate_w + 0.0008, plate_w - 0.0008, above) \
        * smoothstep(0.028, 0.036, x) * smoothstep(0.088, 0.082, x) * smoothstep(0.6, 0.8, side)
    c = c * (1 - 0.3 * maxilla[..., None]) + np.array([0.70, 0.66, 0.46]) * 0.3 * maxilla[..., None]
    outline = maxilla * (1 - smoothstep(0.0, 0.0012, np.minimum(above, plate_w - above)))
    c *= (1 - 0.4 * outline)[..., None]
    height += 0.5 * maxilla - 0.4 * outline
    # The gill cover: a bronze plate, darker towards its rim.
    plate = (behind < 0) * smoothstep(0.158, 0.17, x) * smoothstep(0.145, 0.16, y) * smoothstep(0.285, 0.27, y)
    c = c * (1 - 0.25 * plate[..., None]) + np.array([0.46, 0.38, 0.20]) * 0.25 * plate[..., None]
    c *= (1 - 0.25 * plate * smoothstep(0.012, 0.0, do))[..., None]
    # Mottling on the crown and cheeks.
    blot = np.asarray(Image.fromarray((rng.random((max(h // 40, 2), max(w // 40, 2))) * 255).astype(np.uint8))
                      .resize((w, h), Image.BICUBIC)) / 255.0
    fine = np.asarray(Image.fromarray((rng.random((max(h // 12, 2), max(w // 12, 2))) * 255).astype(np.uint8))
                      .resize((w, h), Image.BICUBIC)) / 255.0
    c *= (1 - 0.07 * head_w * (smoothstep(0.5, 0.85, blot) + smoothstep(0.55, 0.9, fine)))[..., None]
    # Sensory pores: rows of small pits along the preopercle, under the eye and along the lower jaw.
    pores = [(0.148, 0.165 + 0.012 * k) for k in range(8)] + \
            [(0.045 + 0.012 * k, 0.212 + 0.0015 * k) for k in range(7)] + \
            [(0.02 + 0.011 * k, 0.246 + 0.0019 * k) for k in range(5)]
    for px_, py_ in pores:
        pit = smoothstep(0.0019, 0.0008, np.hypot(x - px_, y - py_)) * smoothstep(0.72, 0.88, side)
        c *= (1 - 0.55 * pit)[..., None]
        height -= 0.5 * pit
    de = np.hypot(x - EYE[0], y - EYE[1]) / EYE_R
    c *= (1 - 0.5 * smoothstep(1.0, 1.08, de) * smoothstep(1.45, 1.15, de))[..., None]
    height += 0.6 * smoothstep(1.0, 1.15, de) * smoothstep(1.5, 1.2, de)

    # A touch of mottling, and the dorsal ridge darker.
    noise = np.asarray(Image.fromarray((rng.random((max(h // 16, 2), max(w // 16, 2))) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)) / 255.0
    c *= (0.94 + 0.12 * noise)[..., None]
    c *= (1 - 0.25 * smoothstep(0.08, -0.05, rel))[..., None]
    return c, height


# ------------------------------------------------------------------ eye
def eye_centre():
    """Where the right eye sits: in its socket on the side of the head (model space)."""
    yc = (interp(TOP, EYE[0]) + interp(BOTTOM, EYE[0])) / 2
    hh = (interp(BOTTOM, EYE[0]) - interp(TOP, EYE[0])) / 2
    lat = half_width(EYE[0]) * math.sqrt(max(1 - ((EYE[1] - yc) / hh) ** 2, 0.05))
    return model(np.array(EYE[0]), np.array(EYE[1]), np.array(lat - 0.0068))


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
    gold = np.array([0.96, 0.76, 0.22]) * (0.75 + 0.35 * streaks)[..., None]
    c = gold * (1 - smoothstep(0.62, 0.86, r))[..., None] + np.array([0.35, 0.28, 0.10]) * smoothstep(0.62, 0.86, r)[..., None]
    c = c * (1 - smoothstep(0.86, 0.95, r))[..., None] + np.array([0.10, 0.11, 0.07]) * smoothstep(0.86, 0.95, r)[..., None]
    pupil = smoothstep(0.46, 0.42, r)
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
        "credit": "Proporcje według ilustracji okonia z AtlasRyb.online; barwy według zdjęć: "
                  + ", ".join(f"{a} ({lic})" for _, a, lic, _ in SOURCES) + ", Wikimedia Commons",
    }
    json.dump(meta, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=0)


# ------------------------------------------------------------------ main
def main():
    if sys.argv[1] == "--textures":
        write_textures(sys.argv[2])
        return
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
    centre = eye_centre()
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
