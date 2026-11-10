#!/usr/bin/env python3
"""
Puts a species file together (tools/fish_model/species/<id>.json) for fishgen.py / fish_blender.py from:
  - a measurement of a side-view reference (measure_atlas.py), or, for a fish drawn curled up or with no reference, an
    outline built from the game's own body table (FishBody: depth, hump, peak, snout, tail stalk, fin positions);
  - the game's species data (FishBody, skin style, iris, mouth size, colours), dumped with FISHSIM_DUMP_FISH;
  - the perch (species/okon.json) as the template of the head's anatomy: its mouth, lips, upper jaw bone, nostrils,
    gill cover, preopercle, lateral line and paired fins are carried over through the head's landmarks (snout tip,
    eye, end of the gill cover) and the outline of the body.
A species without its own reference borrows a close relative's measurement ("like") and keeps its own colours, skin
pattern and fins from the game's data.

Usage: python3 tools/fish_model/compose_species.py <fishdata.json> <measure_dir> <out_dir> [ids...]
"""
import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))

# The reference each species is measured on (an atlas picture id), or a relative's, or None (built from the table).
REFERENCE = {
    "amur": 4, "bolen": 53, "brzana": 58, "certa": 77, "cierniczek": 82, "ciernik": 86, "glowacica": 110,
    "jazgarz": 122, "jaz": 123, "orfa": 123, "jelec": 124, "jesiotr": 136, "sterlet": 136, "karas": 142,
    "karas_srebrzysty": 142, "karp": 144, "sazan": 144, "karp_lustrzen": 144, "kielb_bialoplety": 150, "kielb": 154,
    "klen": 157, "koza": 170, "sliz": 170, "piskorz": 170, "krap": 181, "rozanka": 181, "leszcz": 190, "sapa": 190,
    "rozpior": 190, "lin": 192, "lipien": 193, "losos": 197, "pstrag": 197, "troc": 197, "troc_wedrowna": 197,
    "palia": 197, "pstrag_zrodlany": 197, "mietus": 202, "okon": 222, "bass": 222, "ploc": 254, "pstrag_teczowy": 264,
    "sandacz": 309, "sandacz_wolzanski": 309, "czop": 309, "sieja": 322, "sielawa": 323, "stynka": 323,
    "slonecznica": 334, "szczupak": 358, "swinka": 379, "ukleja": 402, "ciosa": 402, "wzdrega": 431, "brzana_": 58,
    "brzanka": 58, "strzebla": 124, "strzebla_blotna": 124, "tolpyga_biala": 4, "tolpyga_pstra": 4,
    # Drawn curled up, or no relative in the atlas: built from the game's body table.
    "sum": None, "wegorz": None, "sumik_karlowaty": None, "sumik_kanalowy": None, "glowacz": None,
    "glowacz_pregoplety": None, "babka_bycza": None,
}
# Species whose own reference is the picture (their colours are measured there too).
OWN = {"amur", "bolen", "brzana", "certa", "cierniczek", "ciernik", "glowacica", "jazgarz", "jaz", "jelec", "karas",
       "karp", "kielb_bialoplety", "kielb", "klen", "koza", "krap", "leszcz", "lin", "lipien", "losos", "mietus", "okon",
       "ploc", "pstrag_teczowy", "sandacz", "sieja", "sielawa", "slonecznica", "szczupak", "swinka", "ukleja", "wzdrega"}
# How a relative's shape is adjusted: body depth, head length.
# Own pictures may be corrected too: the atlas crucian is drawn at the slender end of the species, the roach at the deep
# end, and in the game a crucian has to read as round next to a roach.
ADJUST = {"karas": dict(depth=1.1), "ploc": dict(depth=0.92), "karas_srebrzysty": dict(depth=0.88),
          "ciernik": dict(eye=0.78), "cierniczek": dict(eye=0.8),      # the table's sticklebacks have outsize eyes "sapa": dict(depth=0.85), "rozpior": dict(depth=0.82),
          "pstrag": dict(depth=1.02), "troc": dict(depth=0.98), "troc_wedrowna": dict(depth=0.95),
          "palia": dict(depth=0.95), "pstrag_zrodlany": dict(depth=1.0), "ciosa": dict(depth=0.95),
          "strzebla": dict(depth=0.95), "strzebla_blotna": dict(depth=1.05), "rozanka": dict(depth=0.95),
          "piskorz": dict(depth=0.9), "sliz": dict(depth=1.05), "sterlet": dict(depth=0.85), "czop": dict(depth=0.7),
          "sandacz_wolzanski": dict(depth=1.05), "stynka": dict(depth=0.9), "bass": dict(depth=1.08),
          "tolpyga_biala": dict(depth=1.12), "tolpyga_pstra": dict(depth=1.12), "orfa": dict(depth=1.0),
          "sazan": dict(depth=0.9), "karp_lustrzen": dict(depth=1.05), "brzanka": dict(depth=1.0)}
PERCIDS = {"okon", "sandacz", "sandacz_wolzanski", "jazgarz", "czop", "bass"}
SALMONIDS = {"losos", "pstrag", "pstrag_teczowy", "pstrag_zrodlany", "troc", "troc_wedrowna", "palia", "glowacica",
             "lipien", "sieja", "sielawa", "stynka"}
WITH_MAXILLA = PERCIDS | SALMONIDS | {"szczupak", "mietus", "glowacz", "glowacz_pregoplety", "babka_bycza", "sumik_karlowaty",
                                      "sumik_kanalowy", "sum", "wegorz"}
SPINY_GILLS = PERCIDS | {"glowacz", "glowacz_pregoplety"}
CURLED = {"sum", "wegorz"}
PATTERNS = {"Plain": "plain", "Bars": "bars", "LightSpots": "light_spots", "RedSpots": "red_spots",
            "DarkSpots": "dark_spots", "Stripe": "stripe", "Stripes": "stripes", "Scutes": "scutes",
            "Marbled": "marbled", "Rainbow": "rainbow", "Vermiculated": "vermiculated", "Mirror": "mirror",
            "Band": "band", "Saddles": "saddles", "SpotRows": "spot_rows"}
PATTERN_EXTRA = {
    "ukleja": dict(at=0.5, width=0.08, colour=(0.82, 0.86, 0.9), strength=0.45),
    "slonecznica": dict(at=0.48, width=0.06, colour=(0.55, 0.65, 0.78), strength=0.6),
    "rozanka": dict(at=0.5, width=0.035, colour=(0.35, 0.65, 0.62), strength=0.75, **{"from": 0.45}),
    "stynka": dict(at=0.5, width=0.06, colour=(0.78, 0.82, 0.86), strength=0.5),
    "ciosa": dict(at=0.5, width=0.1, colour=(0.85, 0.88, 0.9), strength=0.3),
    "sum": dict(threshold=0.5, strength=0.6), "mietus": dict(threshold=0.5, strength=0.75),
    "lipien": dict(count=40, size=0.003, depth=0.45), "jazgarz": dict(count=160, size=0.0028, depth=0.75),
    "losos": dict(count=50, size=0.003, depth=0.4), "troc": dict(count=70, size=0.0035, depth=0.5),
    "troc_wedrowna": dict(count=55, size=0.0032, depth=0.45), "glowacica": dict(count=110, size=0.0032, depth=0.6),
    "brzanka": dict(count=90, size=0.004, depth=0.8), "strzebla_blotna": dict(count=120, size=0.003, depth=0.8),
    "sumik_kanalowy": dict(count=40, size=0.004, depth=0.7),
}
# Barbels: (where on the head as a fraction of the mouth's length from the snout, how far down the lips (0 upper lip,
# 1 chin), length as a fraction of the game's barbel length, direction).
BARBELS = {
    "karp": [(0.2, 0.0, 0.55, (0.3, 0.6, 0.2)), (1.0, 0.35, 1.0, (0.2, 0.8, 0.25))],
    "sazan": [(0.2, 0.0, 0.55, (0.3, 0.6, 0.2)), (1.0, 0.35, 1.0, (0.2, 0.8, 0.25))],
    "karp_lustrzen": [(0.2, 0.0, 0.55, (0.3, 0.6, 0.2)), (1.0, 0.35, 1.0, (0.2, 0.8, 0.25))],
    "brzana": [(0.1, 0.1, 1.0, (-0.1, 0.9, 0.2)), (0.95, 0.4, 1.0, (0.2, 0.85, 0.3))],
    "brzanka": [(0.1, 0.1, 1.0, (-0.1, 0.9, 0.2)), (0.95, 0.4, 1.0, (0.2, 0.85, 0.3))],
    "lin": [(1.0, 0.3, 1.0, (0.15, 0.85, 0.2))], "kielb": [(1.0, 0.3, 1.0, (0.2, 0.85, 0.3))],
    "kielb_bialoplety": [(1.0, 0.3, 1.0, (0.2, 0.85, 0.3))],
    "koza": [(0.1, 0.4, 1.0, (-0.2, 0.9, 0.15)), (0.5, 0.5, 0.9, (0.0, 0.9, 0.3)), (0.95, 0.5, 0.8, (0.2, 0.8, 0.4))],
    "sliz": [(0.1, 0.4, 1.0, (-0.2, 0.9, 0.15)), (0.5, 0.5, 0.9, (0.0, 0.9, 0.3)), (0.95, 0.5, 0.8, (0.2, 0.8, 0.4))],
    "piskorz": [(0.05, 0.3, 1.0, (-0.3, 0.9, 0.15)), (0.3, 0.4, 0.9, (-0.1, 0.9, 0.3)), (0.6, 0.5, 0.8, (0.1, 0.9, 0.35)),
                (0.85, 0.6, 0.6, (0.2, 0.9, 0.3)), (0.95, 0.8, 0.5, (0.2, 0.9, 0.2))],
    "sum": [(0.6, 0.0, 1.0, (-0.1, 0.25, 0.9)), (0.4, 1.0, 0.3, (0.0, 0.9, 0.35)), (0.7, 1.0, 0.25, (0.1, 0.9, 0.4))],
    "sumik_karlowaty": [(0.9, 0.1, 1.0, (0.2, 0.5, 0.8)), (0.1, -0.4, 0.5, (-0.2, -0.5, 0.6)), (0.3, 1.0, 0.5, (0.0, 0.9, 0.3)),
                        (0.6, 1.0, 0.5, (0.1, 0.9, 0.4))],
    "sumik_kanalowy": [(0.9, 0.1, 1.0, (0.2, 0.5, 0.8)), (0.1, -0.4, 0.5, (-0.2, -0.5, 0.6)), (0.3, 1.0, 0.5, (0.0, 0.9, 0.3)),
                       (0.6, 1.0, 0.5, (0.1, 0.9, 0.4))],
    "mietus": [(0.4, 1.3, 1.0, (0.1, 0.95, 0.02))],
    "jesiotr": [(-0.6, 0.6, 1.0, (0.05, 1.0, 0.05)), (-0.45, 0.6, 1.0, (0.05, 1.0, 0.15))],
    "sterlet": [(-0.6, 0.6, 1.0, (0.05, 1.0, 0.05)), (-0.45, 0.6, 1.0, (0.05, 1.0, 0.15))],
}
VENTRAL = {"brzana", "brzanka", "swinka", "kielb", "kielb_bialoplety", "koza", "sliz", "piskorz", "jesiotr", "sterlet",
           "certa"}


def interp(points, x):
    p = np.array(points)
    return np.interp(x, p[:, 0], p[:, 1])


def smooth_profile(points, n=96):
    p = np.array(points)
    xs = np.linspace(p[0, 0], p[-1, 0], n)
    return np.stack([xs, np.interp(xs, p[:, 0], p[:, 1])], axis=1)


def table_outline(fb, x1=0.84):
    """The game's own envelope of the body (as src/Fishing/FishModel.cs lofts it) at total length 1."""
    snout, peak, ped, hump = fb["Snout"], fb["PeakAt"], fb["Peduncle"], fb["Hump"]
    eh = 0.62 + 0.6 * snout

    def env(t):
        if t <= peak:
            return max(math.sin(math.pi * 0.5 * t / peak), 0) ** eh
        if t > 0.94:
            return ped * (1 + (t - 0.94) / 0.06 * 0.22)
        u = min(max((t - peak) / (0.94 - peak), 0), 1)
        return ped + (1 - ped) * max(math.cos(math.pi * 0.5 * u), 0) ** 1.25

    def bump(t):
        return max(math.sin(math.pi * min(max(t / 0.9, 0), 1)), 0) ** 1.5

    def flat(t):
        return 1 - fb["FlatHead"] * 0.45 * math.exp(-((t - 0.07) / 0.13) ** 2)
    ts = np.linspace(0, 1, 60)
    raw_top = np.array([env(t) * (1 + hump * bump(t)) * flat(t) for t in ts])
    raw_bot = np.array([env(t) * max(1 - 0.35 * hump * bump(t), 0.2) * flat(t) for t in ts])
    k = fb["BodyDepth"] * x1 / max((raw_top + raw_bot).max(), 1e-6)
    xs = ts * x1
    return [[float(x), float(-k * a)] for x, a in zip(xs, raw_top)], [[float(x), float(k * a)] for x, a in zip(xs, raw_bot)]


def table_fins(fb, top, bottom, x1):
    """Fins placed from the game's body table, for fish without a usable side view."""
    def edge(points, x):
        return float(interp(points, x))
    depth = max(edge(bottom, x) - edge(top, x) for x in np.linspace(0.1, x1, 40))
    fins = {}
    dat = fb["DorsalAt"] if fb["DorsalAt"] >= 0 else min(max(fb["DorsalPos"], 0.25), 0.8)
    d0, d1 = (dat - fb["DorsalLength"] / 2) * x1, (dat + fb["DorsalLength"] / 2) * x1
    dh = fb["DorsalHeight"] * depth
    kind = fb["Dorsal"]
    if kind == "Continuous":
        d1 = x1 - 0.004
    def top_fin(a, b, height, k, rays, shape=lambda t: math.sin(math.pi * t ** 0.6)):
        base = [[a + (b - a) * t, edge(top, a + (b - a) * t)] for t in np.linspace(0, 1, 6)]
        tips = [[a + (b - a) * t + 0.25 * height * shape(t), edge(top, a + (b - a) * t) - height * shape(t)]
                for t in np.linspace(0, 1, 12)]
        return dict(base=base, tips=tips, rays=rays, kind=k)
    if kind == "Spiny":
        m = d0 + 0.45 * (d1 - d0)
        fins["FinDorsal1"] = top_fin(d0, m, dh, "spiny", 9)
        fins["FinDorsal2"] = top_fin(m + 0.01, d1, dh * 0.85, "soft", 12)
    elif kind == "Continuous":
        fins["FinDorsal1"] = top_fin(d0, d1, dh * 0.5, "soft", 40, lambda t: min(1, t * 6) * 0.8)
    elif kind == "Tiny":
        fins["FinDorsal1"] = top_fin(d0, d1, dh * 0.6, "soft", 6)
    else:
        fins["FinDorsal1"] = top_fin(d0, d1, dh, "soft", 11)
    if fb["Adipose"]:
        fins["FinAdipose"] = top_fin(0.84 * x1, 0.9 * x1, 0.12 * depth, "adipose", 6)
    a0 = (fb["AnalAt"] - fb["AnalLength"] / 2) * x1
    a1 = x1 - 0.004 if kind == "Continuous" else (fb["AnalAt"] + fb["AnalLength"] / 2) * x1
    ah = 0.24 * depth * (0.6 if kind == "Continuous" else 1.0)
    fins["FinAnal"] = dict(base=[[a0 + (a1 - a0) * t, edge(bottom, a0 + (a1 - a0) * t)] for t in np.linspace(0, 1, 6)],
                           tips=[[a0 + (a1 - a0) * t + 0.2 * ah * t, edge(bottom, a0 + (a1 - a0) * t) + ah * math.sin(math.pi * t ** 0.55)]
                                 for t in np.linspace(0, 1, 12)], rays=30 if kind == "Continuous" else 10, kind="soft")
    st_t, st_b = edge(top, x1), edge(bottom, x1)
    tail_len = (1 - x1) * (0.6 if kind == "Continuous" else 1.0)
    tail_h = fb["TailSize"] * min(depth, 0.3)
    fork = fb["TailFork"]
    tips = []
    for a in np.linspace(0, 1, 15):
        ln = tail_len * ((1 - fork) * (0.82 + 0.18 * math.sin(math.pi * a)) + fork * (1 - 0.72 * math.exp(-((a - 0.5) / 0.17) ** 2)))
        y = (st_t + st_b) / 2 + (a - 0.5) * tail_h
        if a < 0.5 and fb["TailUpper"] > 1:
            ln *= 1 + (fb["TailUpper"] - 1) * (1 - 2 * a)
        tips.append([x1 + ln, y])
    fins["FinCaudal"] = dict(base=[[x1 - 0.004 * math.sin(math.pi * t), st_t + (st_b - st_t) * t] for t in np.linspace(0, 1, 5)],
                             tips=tips, rays=19, kind="caudal")
    return fins


class HeadMap:
    """Carries points of the perch's head over to another fish through snout, eye, end of the gill cover and outline."""

    def __init__(self, tpl, top, bottom, eye_x, op_x, x1):
        self.t = tpl
        self.top, self.bottom = top, bottom
        op = np.array(tpl["opercle"])
        self.src = [0.0, tpl["eye"][0], float(op[:, 0].max()), tpl["x1"]]
        self.dst = [0.0, eye_x, op_x, x1]

    def x(self, px):
        return float(np.interp(px, self.src, self.dst, right=self.dst[-1] + (px - self.src[-1])))

    def rel(self, px, py):
        top, bot = interp(self.t["top"], px), interp(self.t["bottom"], px)
        return (py - top) / max(bot - top, 1e-6)

    def __call__(self, px, py, x_override=None):
        x = self.x(px) if x_override is None else x_override
        r = self.rel(px, py)
        top, bot = interp(self.top, x), interp(self.bottom, x)
        return [round(float(x), 5), round(float(top + r * (bot - top)), 5)]


def compose(sid, data, measure_dir, tpl):
    fb = data[sid]
    ref = REFERENCE.get(sid)
    adjust = ADJUST.get(sid, {})
    meas = None
    if ref is not None and sid not in CURLED:
        path = os.path.join(measure_dir, f"a{ref}.json")
        if os.path.exists(path):
            meas = json.load(open(path, encoding="utf-8"))
    if sid == "wegorz":
        # The eel: a long, round body of nearly even depth, a rounded head, the fins run round the tail as one.
        x1 = 0.96
        xs_ = [0.0, 0.004, 0.012, 0.025, 0.05, 0.09, 0.15, 0.3, 0.45, 0.6, 0.75, 0.88, 0.96]
        ups = [0.0, 0.007, 0.013, 0.018, 0.023, 0.027, 0.03, 0.032, 0.032, 0.029, 0.023, 0.015, 0.008]
        dns = [0.0, 0.006, 0.011, 0.016, 0.021, 0.026, 0.03, 0.033, 0.033, 0.029, 0.022, 0.014, 0.008]
        top = [[x, -u] for x, u in zip(xs_, ups)]
        bottom = [[x, d] for x, d in zip(xs_, dns)]
        def ribbon(a, b, h, sign, rays):
            base = [[a + (b - a) * t, float(sign * interp(top if sign < 0 else bottom, a + (b - a) * t) * -sign) * -sign]
                    for t in np.linspace(0, 1, 8)]
            base = [[x, float(interp(top, x)) if sign < 0 else float(interp(bottom, x))] for x, _ in base]
            tips = [[x + 0.01, y + sign * h * min(1, (x - a) / 0.05)] for x, y in base]
            return dict(base=base, tips=tips, rays=rays, kind="soft")
        fins = {"FinDorsal1": ribbon(0.33, 0.955, 0.016, -1, 60), "FinAnal": ribbon(0.45, 0.955, 0.014, 1, 50)}
        fins["FinCaudal"] = dict(base=[[0.955, -0.008], [0.957, 0.0], [0.955, 0.008]],
                                 tips=[[0.975, -0.022], [0.995, -0.01], [1.0, 0.0], [0.995, 0.01], [0.975, 0.02]],
                                 rays=12, kind="caudal")
        eye = [fb["EyeAt"] * x1, -0.008]
        eye_r = 0.58 * fb["Eye"] * x1
    elif meas is not None:
        x1 = meas["x1"]
        depth_k = adjust.get("depth", 1.0)
        top = [[x, -u * depth_k] for x, u in meas["up"]]
        bottom = [[x, d * depth_k] for x, d in meas["down"]]
        fins = json.loads(json.dumps(meas["fins"]))
        if depth_k != 1.0:
            for f in fins.values():
                for key in ("base", "tips"):
                    f[key] = [[x, y * depth_k] for x, y in f[key]]
        eye = list(meas["eye"])
        eye[1] *= depth_k
        eye_r = meas["eye_r"]
    else:
        x1 = 0.84 if fb["Dorsal"] != "Continuous" else 0.95
        top, bottom = table_outline(fb, x1)
        fins = table_fins(fb, top, bottom, x1)
        eye_x = fb["EyeAt"] * x1
        ht, hb = interp(top, eye_x), interp(bottom, eye_x)
        eh = fb["EyeHigh"]
        eye = [eye_x, (ht * eh) if eh >= 0 else (hb * -eh)]
        eye_r = 0.58 * fb["Eye"] * x1
    # Eye size from the game's table when it differs much (small eyes are hard to measure on a picture).
    table_r = 0.58 * fb["Eye"] * x1
    if meas is None or sid not in OWN or abs(eye_r - table_r) > 0.5 * table_r:
        eye_r = table_r
    eye_r *= adjust.get("eye", 1.0)
    if sid not in OWN and meas is not None:
        eye[0] = fb["EyeAt"] * x1
        ht, hb = interp(top, eye[0]), interp(bottom, eye[0])
        eye[1] = ht * fb["EyeHigh"] if fb["EyeHigh"] >= 0 else hb * -fb["EyeHigh"]
    # Keep the eye inside the head, below the crown.
    ht, hb = interp(top, eye[0]), interp(bottom, eye[0])
    eye[1] = float(np.clip(eye[1], ht + 1.25 * eye_r, hb - 1.25 * eye_r))

    # The head: where the gill cover ends.
    ratio = 1.2 if fb["Snout"] > 0.65 else (1.0 if fb["Snout"] > 1.0 else 1.75)
    op_x = float(np.clip(eye[0] * (1 + ratio), 0.10, 0.32))
    if fb["Dorsal"] == "Continuous":
        op_x = float(np.clip(eye[0] * 2.4, 0.08, 0.16))
    hm = HeadMap(tpl, top, bottom, eye[0], op_x, x1)
    mp = lambda pts: [hm(px, py) for px, py in pts]

    # The mouth: the perch's, stretched to the species' mouth size; under the snout for bottom feeders.
    gape_end = 0.068 * (fb["Mouth"] / 0.07) * (x1 / 0.845)
    tpl_end = tpl["gape"][-1][0]
    if sid in VENTRAL:
        ht0, hb0 = interp(top, 0.03), interp(bottom, 0.03)
        start = 0.02 if fb["Snout"] < 1 else 0.06
        gape = []
        for t in np.linspace(0, 1, 5):
            x = start + t * gape_end * 0.7
            ht_, hb_ = interp(top, x), interp(bottom, x)
            gape.append([round(x, 5), round(ht_ + (0.86 - 0.08 * t) * (hb_ - ht_), 5)])
        upper_fold, lower_fold = [], []
    else:
        gape = [hm(px, py, x_override=px * gape_end / tpl_end) for px, py in tpl["gape"]]
        upper_fold = [hm(px, py, x_override=px * gape_end / tpl_end) for px, py in tpl["upper_lip_fold"]]
        lower_fold = [hm(px, py, x_override=px * gape_end / tpl_end) for px, py in tpl["lower_lip_fold"]]
    maxilla_edge = [hm(px, py, x_override=px * gape_end / tpl_end) for px, py in tpl["maxilla_edge"]] \
        if sid in WITH_MAXILLA and sid not in VENTRAL else []
    sp = {
        "id": sid, "name": fb["Name"], "axis": 0.0, "depth_scale": 1.0, "x0": 0.0, "x1": round(x1, 4),
        "cap_length": 0.0008,
        "top": [[round(x, 5), round(y, 5)] for x, y in top], "bottom": [[round(x, 5), round(y, 5)] for x, y in bottom],
        "eye": [round(float(eye[0]), 5), round(float(eye[1]), 5)], "eye_r": round(float(eye_r), 5),
        "gape": gape, "upper_lip_fold": upper_fold, "lower_lip_fold": lower_fold, "maxilla_edge": maxilla_edge,
        "maxilla": [], "nostrils": mp(tpl["nostrils"]), "preopercle": mp(tpl["preopercle"]),
        "opercle": mp(tpl["opercle"]), "lateral_line": mp(tpl["lateral_line"]),
        "serrated_preopercle": sid in SPINY_GILLS, "opercle_spine": sid in SPINY_GILLS,
        "cheek_scales": sid in PERCIDS,                         # perches have scaled cheeks, carps and others bare ones
    }
    # Half width: the game's width-to-height ratio of the body, broader heads where the table says so.
    hw = []
    for x in np.concatenate([[0.0, 0.0012, 0.003, 0.006, 0.012], np.linspace(0.02, x1, 30)]):
        d = interp(bottom, x) - interp(top, x)
        t = x / x1
        broad = 1 + fb["FlatHead"] * 0.7 * math.exp(-((t - 0.1) / 0.14) ** 2) + fb["HeadBroad"] * math.exp(-((t - 0.14) / 0.14) ** 2)
        w = fb["Width"] * 0.5 * d * broad * (1.0 + 0.25 * math.exp(-((t - 0.1) / 0.1) ** 2))
        hw.append([round(float(x), 5), round(float(max(w, 0.003)), 5)])
    sp["half_width"] = hw
    sp["n_top"] = [[0.0, 2.4], [op_x * 0.8, 2.3], [op_x * 1.25, 1.8], [1.0, 1.8 + 0.6 * max(fb["Width"] - 0.6, 0)]]
    nb = 2.3 + 1.2 * fb["BellyFlat"]
    sp["n_bottom"] = [[0.0, 1.85], [op_x * 0.6, 1.95], [op_x * 1.2, nb], [1.0, nb]]

    # Paired fins from the perch, scaled; pelvic fins from the picture when it shows them.
    pec = tpl["fins"]["FinPectoral"]
    size = fb["PectoralSize"]
    base = mp(pec["base"])
    bx, by = np.mean(base, axis=0)
    tips = []
    for px, py in pec["tips"]:
        q = hm(px, py)
        tips.append([round(bx + (q[0] - bx) * size, 5), round(by + (q[1] - by) * size, 5)])
    fins["FinPectoral"] = dict(base=base, tips=tips, rays=13, kind="soft", splay=30.0, camber=0.004)
    if "FinPelvic" not in fins:
        pel = tpl["fins"]["FinPelvic"]
        fins["FinPelvic"] = dict(base=mp(pel["base"]), tips=mp(pel["tips"]), rays=7, kind="soft", droop=25.0)
    if fb["Dorsal"] == "Spiny" and "FinDorsal1" in fins:
        fins["FinDorsal1"]["kind"] = "spiny"
    if sid == "okon":
        fins["FinDorsal1"]["spot"] = True

    # Colours: measured on the species' own picture, otherwise from the game's colours.
    back, belly, fin = np.array(fb["Back"]), np.array(fb["Belly"]), np.array(fb["FinColor"])
    if meas is not None and sid in OWN:
        def rich(c, k=1.3):
            c = np.array(c)
            grey = c @ np.array([0.3, 0.59, 0.11])
            return np.clip(grey + (c - grey) * k, 0, 1)
        cm = {k: rich(v) for k, v in meas["colours"].items()}
        cm["ridge"] = cm["ridge"] * 0.8
        cm["upper"] = cm["upper"] * 0.9
    else:
        # The game's back and belly colours, with a flank between them that leans to the back's colour (silvery fish
        # lighter), so the belly's colour stays on the belly.
        silver = fb["Silver"]
        flank = back * (0.75 - 0.35 * silver) + belly * (0.25 + 0.35 * silver) + np.array([0.06, 0.06, 0.07]) * silver
        cm = {"ridge": back * 0.6, "upper": back * 0.9, "mid": flank, "mid2": flank * 0.6 + belly * 0.4,
              "lower": belly * 0.9 + flank * 0.1, "belly": np.minimum(belly * 1.05, 1), "head_top": back * 0.75,
              "cheek": flank * 0.8 + belly * 0.2, "throat": belly}
    cm["lip"] = cm["cheek"] * 0.6 + cm["belly"] * 0.4
    cm["bar"] = cm["upper"] * 0.32
    sp["colours"] = {k: [round(float(c), 3) for c in np.clip(v, 0, 1)] for k, v in cm.items()}
    sp["iris"] = fb["Iris"]
    alpha = {"FinDorsal1": 0.72 if fins.get("FinDorsal1", {}).get("kind") == "spiny" else 0.65, "FinDorsal2": 0.62,
             "FinAnal": 0.75, "FinCaudal": 0.8, "FinPectoral": 0.55, "FinPelvic": 0.75, "FinAdipose": 0.95}
    fcols = {}
    for name in fins:
        mc = meas["fin_colours"].get(name) if (meas is not None and sid in OWN) else None
        if mc:
            mem, ray = np.array(mc["membrane"]), np.array(mc["ray"])
        else:
            mem, ray = fin, fin * 0.6
        if name == "FinAdipose" and not mc:
            mem = cm["upper"]
        if name == "FinPectoral" and not (mc and name == "FinPectoral"):
            mem = mem * 0.6 + cm["belly"] * 0.4
        fcols[name] = {"membrane": [round(float(v), 3) for v in np.clip(mem * 1.05, 0, 1)],
                       "ray": [round(float(v), 3) for v in np.clip(ray, 0, 1)], "alpha": alpha.get(name, 0.7)}
        if name == "FinCaudal":
            fcols[name]["base"] = [round(float(v), 3) for v in (cm["upper"] * 0.6 + cm["mid"] * 0.4)]
    if sid in ("palia", "pstrag_zrodlany"):
        for name in ("FinPelvic", "FinAnal", "FinPectoral"):
            fcols[name]["lead"] = True
    # Every fin's base sits on the outline, a touch inside the body (so no fin stands off it), its tips moved with it.
    for name, f in fins.items():
        if name in ("FinCaudal", "FinPectoral"):
            continue
        on_top = name.startswith("FinDorsal") or name == "FinAdipose"
        edge = top if on_top else bottom
        inset = 0.0015 if on_top else -0.0015
        shift = [interp(edge, x) + inset - y for x, y in f["base"]]
        xs_b = [x for x, _ in f["base"]]
        f["base"] = [[round(x, 5), round(y + d, 5)] for (x, y), d in zip(f["base"], shift)]
        f["tips"] = [[round(x, 5), round(y + float(np.interp(x, xs_b, shift)), 5)] for x, y in f["tips"]]
    sp["fins"] = fins
    sp["fin_colours"] = fcols
    # Skin.
    sp["scale"] = round(0.9 * fb["Scale"] * x1, 5)
    pat = {"type": PATTERNS.get(fb["Pattern"], "plain")}
    if fb["Count"]:
        pat["count"] = fb["Count"]
    pat.update(PATTERN_EXTRA.get(sid, {}))
    if sid == "okon":
        pat = tpl["pattern"]
    sp["pattern"] = pat
    sp["silver"] = fb["Silver"]
    # Spots on the fins of spotted species.
    if pat["type"] in ("dark_spots", "red_spots", "rainbow", "light_spots") and "FinDorsal1" in fins:
        g = np.random.default_rng(len(sid))
        dots = [(float(g.uniform(0.1, 0.9)), float(g.uniform(0.15, 0.85)), 0.035, [0.08, 0.07, 0.06]) for _ in range(12)]
        fins["FinDorsal1"]["dots"] = dots
        if pat["type"] in ("rainbow", "light_spots"):
            fins["FinCaudal"]["dots"] = [(float(g.uniform(0.05, 0.95)), float(g.uniform(0.2, 0.9)), 0.03, [0.08, 0.07, 0.06]) for _ in range(16)]
    # Barbels.
    if fb["BarbelPairs"] > 0 and sid in BARBELS:
        out = []
        blen = fb["BarbelLength"] * x1
        if sid == "sum":
            blen = 0.22
        for frac, down_k, len_k, d in BARBELS[sid][:max(fb["BarbelPairs"], len(BARBELS[sid]))]:
            gx = max(gape[0][0] + frac * (gape[-1][0] - gape[0][0]), 0.004)
            gy = float(interp(gape, np.clip(gx, gape[0][0], gape[-1][0])))
            hb_ = interp(bottom, gx)
            y = gy + down_k * (hb_ - gy) * 0.8 if down_k >= 0 else gy + down_k * 0.01
            item = {"root": [round(float(gx), 5), round(float(y), 5)], "dir": list(d), "length": round(blen * len_k, 5),
                    "radius": round(min(max(0.0012, 0.035 * blen * len_k), 0.004), 5), "side": 0.75}
            if blen * len_k > 0.08:          # long whiskers (catfish) sweep back and droop only a little
                item.update(droop=0.12, back=0.35)
            out.append(item)
        sp["barbels"] = out
    sp["head_blend"] = 0.3
    sp["credit"] = ("Proporcje według ilustracji z AtlasRyb.online" if sid in OWN else
                    "Proporcje według ilustracji pokrewnego gatunku z AtlasRyb.online" if meas is not None else
                    "Kształt według tabeli budowy gatunku")
    sp["credit_author"] = "AtlasRyb.online" if meas is not None else ""
    sp["credit_licence"] = "proporcje (pomiar), bez kopiowania ilustracji" if meas is not None else ""
    sp["credit_source"] = "https://atlasryb.online" if meas is not None else ""
    return sp


def main():
    data = json.load(open(sys.argv[1], encoding="utf-8"))
    measure_dir, out_dir = sys.argv[2], sys.argv[3]
    ids = sys.argv[4:] or [k for k in data if k != "okon"]
    tpl = json.load(open(os.path.join(HERE, "species", "okon.json"), encoding="utf-8"))
    os.makedirs(out_dir, exist_ok=True)
    for sid in ids:
        sp = compose(sid, data, measure_dir, tpl)
        json.dump(sp, open(os.path.join(out_dir, f"{sid}.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"{sid}: {sp['credit']}, fins {sorted(sp['fins'])}, pattern {sp['pattern']['type']}")


if __name__ == "__main__":
    main()
