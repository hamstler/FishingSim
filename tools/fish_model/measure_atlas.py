#!/usr/bin/env python3
"""
Measures a side-view fish illustration (a fish on a white background, as AtlasRyb.online draws them) for the model
generator (fishgen.py) and writes a species file (species/<id>.json) with numbers only: the body outline without its
fins, every fin's base and outer edge, the eye, the colours in bands along the body and of each fin. The illustration
itself is not copied anywhere.

How: the silhouette is the non-white part of the picture (thin barbels and ray tips trimmed off); the fish is turned
head to the left and levelled; along the body the height of the back above the snout-stalk line and the depth of the
belly below it are fitted from below with an asymmetric smoother, which follows the body and passes under the fins
(fins stand out of it as bumps); what stands out is the fins, each one a connected shape whose contact with the body
is its base and whose outer contour its edge. Everything behind the tail stalk is the tail fin. The eye is the darkest
round spot in the head. Pectoral fins usually lie over the body in a side view; they are placed from the head.

Species data the picture cannot give (dorsal kind, adipose fin, barbels, skin pattern, scale size, iris colour, mouth
size) comes from the game's own tables, dumped with FISHSIM_DUMP_FISH (see tests/GearSuite.cs).

Usage: python3 tools/fish_model/measure_atlas.py <fishdata.json> <id> <picture> <out.json> [overlay.png] [--flip]
"""
import json
import math
import sys

import cv2
import numpy as np

N = 480                     # samples along the fish
# Pictures that show more than one fish: the band of the picture (fractions of its height) holding the one to measure.
CROPS = {"ciernik": (0.08, 0.47)}


def silhouette(rgb):
    a = rgb.astype(np.int16)
    mn = a.min(axis=2)
    sat = a.max(axis=2) - mn
    mask = ((mn < 228) | (sat > 22)).astype(np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    big = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    mask = (lab == big).astype(np.uint8)
    # Fill holes (white highlights inside the fish).
    inv = 1 - mask
    n, lab, stats, _ = cv2.connectedComponentsWithStats(inv, 4)
    h, w = mask.shape
    for k in range(1, n):
        x, y, ww, hh, area = stats[k]
        if x > 0 and y > 0 and x + ww < w and y + hh < h:
            mask[lab == k] = 1
    return mask


def head_on_left(mask):
    """The tail fin makes the last tenth of the silhouette taller than the first (the head's front)."""
    cols = np.nonzero(mask.any(axis=0))[0]
    x0, x1 = cols.min(), cols.max()
    L = x1 - x0

    def height(xa, xb):
        h = []
        for x in range(xa, xb):
            r = np.nonzero(mask[:, x])[0]
            h.append(r.max() - r.min() if len(r) else 0)
        return np.max(h)
    return height(x0, x0 + int(0.1 * L)) < height(x1 - int(0.1 * L), x1)


def als(y, lam, p=0.02, iters=15, exclude=None):
    """
    A smooth curve fitted to y from below: points above it barely count (fins), points below it pull it down; points
    marked in `exclude` (known fins) do not count at all, so the curve bridges under them.
    """
    n = len(y)
    D = np.diff(np.eye(n), 2, axis=0)
    DD = lam * (D.T @ D)
    keep = np.ones(n) if exclude is None else (~exclude).astype(float)
    w = keep.copy()
    z = y.copy()
    for _ in range(iters):
        z = np.linalg.solve(np.diag(w) + DD, w * y)
        w = np.where(y > z, p, 1 - p) * keep
    return z


def fin_spans(prof, xs, start, end):
    """
    Long fins along the back or belly: a fin's front edge rises steeply out of the body and its rear end drops back to
    it; between them, however low the fin, it is not body.
    """
    k = np.exp(-0.5 * (np.arange(-3, 4) / 1.2) ** 2)
    sm = np.convolve(np.pad(prof, 3, mode="edge"), k / k.sum(), mode="valid")
    dx = xs[1] - xs[0]
    slope = np.gradient(sm, dx)
    spans = np.zeros(len(prof), dtype=bool)
    i = 0
    n = len(prof)
    while i < n:
        if start < xs[i] < end and slope[i] > 1.4:
            # Front edge found: the fin lasts until the profile drops steeply (its rear end) or comes back down.
            j = i + 1
            while j < n - 1 and xs[j] < end + 0.02 and not (slope[j] < -1.4 and sm[j] < sm[i] + 0.004) \
                    and not (xs[j] - xs[i] > 0.012 and sm[j] <= sm[max(i - 1, 0)] + 0.003):
                j += 1
            while j < n - 1 and slope[j] < -0.6:
                j += 1
            a = max(i - 2, 0)
            spans[a:j + 2] = True
            i = j + 1
        else:
            i += 1
    return spans


def smooth_body(body, xs, si):
    """Smooths a body profile lightly (in place), leaving the snout's tip as it is."""
    kern = np.exp(-0.5 * (np.arange(-4, 5) / 1.6) ** 2)
    kern /= kern.sum()
    sm = np.convolve(np.pad(body[:si + 1], 4, mode="edge"), kern, mode="valid")
    body[:si + 1] = np.where(xs[:si + 1] > 0.02, sm, body[:si + 1])


def measure(data, sid, picture, flip=None):
    sp = data[sid]
    rgb = cv2.cvtColor(cv2.imread(picture), cv2.COLOR_BGR2RGB)
    if sid in CROPS:
        h = rgb.shape[0]
        rgb = np.ascontiguousarray(rgb[int(CROPS[sid][0] * h):int(CROPS[sid][1] * h)])
    mask = silhouette(rgb)
    if flip is None:
        flip = not head_on_left(mask)
    if flip:
        rgb, mask = rgb[:, ::-1].copy(), mask[:, ::-1].copy()
    # Trim thin things (barbels, ray tips) for the outline; keep the full mask for the fins.
    ys, xs = np.nonzero(mask)
    L0 = xs.max() - xs.min()
    k = max(3, int(0.012 * L0) | 1)
    core = cv2.morphologyEx(mask, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k)))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(core, 8)
    core = (lab == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))).astype(np.uint8)
    # Level the fish: the line from the snout tip to the middle of the tail stalk should be horizontal.
    cols = np.nonzero(core.any(axis=0))[0]
    x0, x1 = cols.min(), cols.max()
    L = x1 - x0

    def column(m, x):
        r = np.nonzero(m[:, int(np.clip(x, 0, m.shape[1] - 1))])[0]
        return (r.min(), r.max()) if len(r) else (np.nan, np.nan)

    depth = np.array([np.subtract(*column(core, x)[::-1]) for x in range(x0, x1 + 1)], dtype=float)
    a, b = int(0.62 * L), int(0.93 * L)
    stalk_px = x0 + a + int(np.nanargmin(depth[a:b]))
    tip_rows = np.nonzero(core[:, x0:x0 + max(2, int(0.006 * L))].any(axis=1))[0]
    tip_y = tip_rows.mean()
    st_top, st_bot = column(core, stalk_px)
    angle = math.degrees(math.atan2((st_top + st_bot) / 2 - tip_y, stalk_px - x0))
    if abs(angle) > 0.8:
        M = cv2.getRotationMatrix2D((float(x0), float(tip_y)), angle, 1.0)
        size = (rgb.shape[1], rgb.shape[0])
        rgb = cv2.warpAffine(rgb, M, size, flags=cv2.INTER_CUBIC, borderValue=(255, 255, 255))
        mask = (cv2.warpAffine(mask * 255, M, size, flags=cv2.INTER_LINEAR) > 127).astype(np.uint8)
        core = (cv2.warpAffine(core * 255, M, size, flags=cv2.INTER_LINEAR) > 127).astype(np.uint8)
    cols = np.nonzero(core.any(axis=0))[0]
    x0, x1 = cols.min(), cols.max()
    L = float(x1 - x0)
    tip_rows = np.nonzero(core[:, x0:x0 + max(2, int(0.006 * L))].any(axis=1))[0]
    tip_y = float(tip_rows.mean())

    def P(x_px, y_px):              # pixels -> fish units (x from the snout, y down, in total lengths)
        return (x_px - x0) / L, (y_px - tip_y) / L

    # Profiles along the fish.
    xs_px = np.linspace(x0, x1, N)
    top = np.array([column(core, x)[0] for x in xs_px])
    bot = np.array([column(core, x)[1] for x in xs_px])
    depth = bot - top
    a, b = int(0.62 * N), int(0.93 * N)
    si = a + int(np.nanargmin(depth[a:b]))
    stalk_x = (xs_px[si] - x0) / L
    axis_y = np.interp(np.arange(N), [0, si], [tip_y, (top[si] + bot[si]) / 2])
    up = (axis_y - top) / L
    down = (bot - axis_y) / L
    xs = (xs_px - x0) / L
    body_up, body_down = up.copy(), down.copy()
    lam = 2.0e4
    for prof, body, start in ((up, body_up, 0.22), (down, body_down, 0.14)):
        p, xb = prof[:si + 1], xs[:si + 1]
        spans = fin_spans(p, xb, start, xs[si] - 0.03)
        z = als(p, lam, exclude=spans)
        fin = ((p - z > 0.004) | spans) & (xb > start)
        # Grow the fin spans a little so their bases are bridged too.
        fin = np.convolve(fin.astype(float), np.ones(5), mode="same") > 0
        body[:si + 1] = np.where(fin, np.minimum(z, p), p)
        smooth_body(body, xs, si)

    def find_fins():
        # Body mask (in pixels) from the body profiles; fins are the rest of the full mask.
        H, W = mask.shape
        body_mask = np.zeros_like(mask)
        for i in range(si + 1):
            xa = int(round(xs_px[i]))
            xb = int(round(xs_px[min(i + 1, N - 1)])) + 1
            ya = int(round(axis_y[i] - body_up[i] * L)) + 1
            yb = int(round(axis_y[i] + body_down[i] * L))
            body_mask[max(ya, 0):max(yb, 0), xa:xb] = 1
        fins_mask = (mask > 0) & (body_mask == 0)
        stalk_px = xs_px[si]
        fins_mask[:, :int(x0 + 0.005 * L)] = False

        def body_edge(xf, which):
            i = np.clip((xf * L) / (L / (N - 1)), 0, N - 1)
            u = np.interp(i, np.arange(N), body_up if which == "top" else body_down)
            ay = np.interp(i, np.arange(N), axis_y)
            return ((ay - u * L) if which == "top" else (ay + u * L) - tip_y) / L if False else \
                (((ay - u * L) - tip_y) / L if which == "top" else ((ay + u * L) - tip_y) / L)

        # Fin shapes.
        fm = fins_mask.astype(np.uint8)
        kk = max(3, int(0.006 * L) | 1)
        fm = cv2.morphologyEx(fm, cv2.MORPH_OPEN, np.ones((kk, kk), np.uint8))
        n, lab, stats, cents = cv2.connectedComponentsWithStats(fm, 8)
        comps = []
        for k in range(1, n):
            area = stats[k, cv2.CC_STAT_AREA] / L ** 2
            if area < 0.0004:
                continue
            cx, cy = P(*cents[k])
            ys_, xs_ = np.nonzero(lab == k)
            fx, fy = (xs_ - x0) / L, (ys_ - tip_y) / L
            side = "tail" if cx > stalk_x - 0.01 and fx.max() > stalk_x + 0.02 else \
                ("top" if cy < (body_edge(cx, "top") + body_edge(cx, "bottom")) / 2 else "bottom")
            # How far the shape stands out of the body: low bumps along the back (the nape) are not fins.
            if side == "top":
                stand = float(np.max(body_edge(fx, "top") - fy))
            elif side == "bottom":
                stand = float(np.max(fy - body_edge(fx, "bottom")))
            else:
                stand = 1.0
            if stand < 0.014 and not (sp["Adipose"] and side == "top" and cx > 0.6):
                continue
            comps.append(dict(k=k, area=area, cx=cx, cy=cy, side=side, fx=fx, fy=fy))

        rays_by = {"FinDorsal1": 12, "FinDorsal2": 12, "FinAdipose": 6, "FinAnal": 10, "FinCaudal": 19, "FinPectoral": 12,
                   "FinPelvic": 7}

        def fin_from(c, name, kind):
            fx, fy = c["fx"], c["fy"]
            which = "top" if c["side"] == "top" else "bottom"
            # Contact with the body: fin pixels next to the body edge.
            edge_y = body_edge(fx, which)
            near = np.abs(fy - edge_y) < 0.006
            if near.sum() < 3:
                near = np.abs(fy - edge_y) < 0.015
            bx = fx[near] if near.any() else fx
            xa, xb = np.percentile(bx, 1), np.percentile(bx, 99)
            if xb - xa < 0.01:
                xb = xa + 0.01
            base = [(float(x), float(body_edge(x, which))) for x in np.linspace(xa, xb, 6)]
            # Outer edge: the extreme pixel per column, from the front of the base to the rear end of the fin.
            cols_ = np.round(fx * 400).astype(int)
            tips = []
            for cc in range(cols_.min(), cols_.max() + 1):
                sel = cols_ == cc
                if not sel.any():
                    continue
                y_out = fy[sel].min() if which == "top" else fy[sel].max()
                tips.append((cc / 400.0, float(y_out)))
            tips = np.array(tips)
            tips = tips[tips[:, 0] >= xa - 0.004]
            # Smooth the edge a touch (ray tips), keep 14 points.
            if len(tips) > 5:
                kern = np.ones(3) / 3
                tips[:, 1] = np.convolve(np.pad(tips[:, 1], 1, mode="edge"), kern, mode="valid")
            idx = np.linspace(0, len(tips) - 1, min(14, len(tips))).round().astype(int)
            return dict(base=[list(map(lambda v: round(v, 4), p)) for p in base],
                        tips=[[round(float(x), 4), round(float(y), 4)] for x, y in tips[idx]],
                        rays=rays_by.get(name, 10), kind=kind)

        fins = {}
        top_c = sorted([c for c in comps if c["side"] == "top"], key=lambda c: c["cx"])
        bot_c = sorted([c for c in comps if c["side"] == "bottom"], key=lambda c: c["cx"])
        tail_c = [c for c in comps if c["side"] == "tail"]
        spiny = sp["Dorsal"] in ("Spiny", "Spines")
        if sp["Adipose"] and len(top_c) >= 2 and top_c[-1]["area"] < 0.004 and top_c[-1]["cx"] > 0.6:
            fins["FinAdipose"] = fin_from(top_c.pop(), "FinAdipose", "adipose")
        if sp["Dorsal"] == "Spines" and len(top_c) >= 2:
            # Stickleback: free spines in front of the soft dorsal.
            soft = max(top_c, key=lambda c: c["area"])
            spines = [c for c in top_c if c is not soft and c["cx"] < soft["cx"]]
            if spines:
                fx = np.concatenate([c["fx"] for c in spines])
                fy = np.concatenate([c["fy"] for c in spines])
                fins["FinDorsal1"] = fin_from(dict(fx=fx, fy=fy, side="top"), "FinDorsal1", "spines")
                fins["FinDorsal1"]["rays"] = len(spines) * 3
            fins["FinDorsal2"] = fin_from(soft, "FinDorsal2", "soft")
        elif top_c:
            two = spiny or sid in ("mietus", "babka_bycza", "glowacz", "glowacz_pregoplety")
            if not two:
                # One dorsal fin: the biggest shape on the back (pieces of it close together are joined).
                main_c = max(top_c, key=lambda c: c["area"])
                parts = [c for c in top_c if abs(c["cx"] - main_c["cx"]) < 0.08]
                fx = np.concatenate([c["fx"] for c in parts])
                fy = np.concatenate([c["fy"] for c in parts])
                fins["FinDorsal1"] = fin_from(dict(fx=fx, fy=fy, side="top", cx=main_c["cx"]), "FinDorsal1", "soft")
            else:
                fins["FinDorsal1"] = fin_from(top_c[0], "FinDorsal1", "spiny" if spiny else "soft")
                if spiny:
                    fins["FinDorsal1"]["rays"] = 14
                if len(top_c) >= 2:
                    fins["FinDorsal2"] = fin_from(top_c[1], "FinDorsal2", "soft")
        if sp["Adipose"] and "FinAdipose" not in fins:
            # Too small to see: a small fleshy fin a little before the tail stalk.
            xa = stalk_x - 0.11
            base = [(round(xa + 0.012 * i, 4), round(float(body_edge(xa + 0.012 * i, "top")), 4)) for i in range(5)]
            tips = [(round(x + 0.01, 4), round(y - 0.018 * math.sin(math.pi * (0.15 + 0.7 * i / 4)), 4)) for i, (x, y) in enumerate(base)]
            fins["FinAdipose"] = dict(base=base, tips=tips, rays=6, kind="adipose")
        # Under the body: the anal fin is the last one; a fin near the head is a pectoral showing below the belly.
        if bot_c:
            anal = max(bot_c, key=lambda c: c["cx"])
            if anal["cx"] > 0.45:
                fins["FinAnal"] = fin_from(anal, "FinAnal", "soft")
                bot_c.remove(anal)
            if bot_c:
                pel = [c for c in bot_c if c["cx"] > 0.2]
                if pel:
                    fins["FinPelvic"] = fin_from(max(pel, key=lambda c: c["area"]), "FinPelvic", "soft")
                    fins["FinPelvic"]["droop"] = 20.0
        if tail_c:
            fx = np.concatenate([c["fx"] for c in tail_c])
            fy = np.concatenate([c["fy"] for c in tail_c])
            ys_, xs_ = fy, fx
            st_t, st_b = body_edge(stalk_x, "top"), body_edge(stalk_x, "bottom")
            rows = np.round(fy * 400).astype(int)
            tips = []
            for r in range(rows.min(), rows.max() + 1):
                sel = rows == r
                if sel.any():
                    tips.append((float(fx[sel].max()), r / 400.0))
            tips = np.array(tips)
            idx = np.linspace(0, len(tips) - 1, min(16, len(tips))).round().astype(int)
            base = [(round(stalk_x - 0.008 * math.sin(math.pi * t), 4), round(st_t + (st_b - st_t) * t, 4)) for t in np.linspace(0, 1, 5)]
            fins["FinCaudal"] = dict(base=[list(p) for p in base], tips=[[round(x, 4), round(y, 4)] for x, y in tips[idx]],
                                     rays=19, kind="caudal")
        return fins, comps

    fins, comps = find_fins()
    # Second pass: the fins found, the back and the belly under each of them are fitted again. A fin's first rays are
    # short, so the silhouette leaves the body a little before the fin's steep front edge, and its last rays hang over
    # the body behind its base; left in, they make a hump at the fin and a step behind it. So the outline is left out
    # from a little before each fin to a little behind it and bridged there by the smoother (the body's own curve
    # carried on under the fin), and the fins are found again against that body.
    for which, prof, body in (("top", up, body_up), ("bottom", down, body_down)):
        bases = sorted((f["base"][0][0], f["base"][-1][0]) for name, f in fins.items()
                       if name not in ("FinCaudal", "FinPectoral")
                       and (which == "top") == (name.startswith("FinDorsal") or name == "FinAdipose"))
        if which == "top" and sp["Dorsal"] == "Long" and "FinDorsal1" in fins:
            # A long dorsal fin (carp, crucian) runs back as a low band of rays over the back almost to the tail stalk,
            # where its last ray drops steeply to the body: the fin's base ends there, not where its high front ends.
            fa, fb_ = fins["FinDorsal1"]["base"][0][0], fins["FinDorsal1"]["base"][-1][0]
            k = np.exp(-0.5 * (np.arange(-3, 4) / 1.2) ** 2)
            sm = np.convolve(np.pad(prof[:si + 1], 3, mode="edge"), k / k.sum(), mode="valid")
            slope = np.gradient(sm, xs[1] - xs[0])
            look = np.nonzero((xs[:si + 1] > fb_ + 0.03) & (xs[:si + 1] < xs[si] - 0.003))[0]
            if len(look) and slope[look].min() < -1.2:
                rear = float(xs[look[int(np.argmin(slope[look]))]])
                bases = [(a_, rear if abs(a_ - fa) < 1e-6 else b_) for a_, b_ in bases]
        spans = [[fa - min(0.012 + 0.3 * (fb_ - fa), 0.05), fb_ + min(0.01 + 0.25 * (fb_ - fa), 0.04)] for fa, fb_ in bases]
        # Fins that stand apart (pelvic and anal fins) keep some of the body between them as it is drawn; where two fins
        # meet (a perch's two dorsals), the body is not lowered at the notch between them, so they stay two fins.
        joints = []
        for k in range(len(bases) - 1):
            if bases[k + 1][0] - bases[k][1] <= 0.035:
                joints.append((bases[k][1] - 0.012, bases[k + 1][0] + 0.012))
            else:
                mid = 0.5 * (bases[k][1] + bases[k + 1][0])
                spans[k][1] = min(spans[k][1], mid - 0.012)
                spans[k + 1][0] = max(spans[k + 1][0], mid + 0.012)
        gap = np.zeros(si + 1, dtype=bool)
        for ga, gb in spans:
            gap |= (xs[:si + 1] > ga) & (xs[:si + 1] < gb)
        gap &= (xs[:si + 1] > 0.06) & (xs[:si + 1] < xs[si] - 0.02)
        if gap.any():
            first = body[:si + 1].copy()
            z = als(first.copy(), lam, p=0.5, iters=1, exclude=gap)
            z = np.minimum(z, prof[:si + 1] + 0.004)
            for ja, jb in joints:
                at = (xs[:si + 1] > ja) & (xs[:si + 1] < jb)
                z = np.where(at, np.maximum(z, first), z)
            body[:si + 1] = np.where(gap, z, first)
            smooth_body(body, xs, si)
    fins, comps = find_fins()

    # The eye: the darkest round spot in the upper head.
    lum = cv2.cvtColor(rgb, cv2.COLOR_RGB2GRAY).astype(np.float32)
    body_len = stalk_x
    r_px = max(2.0, 0.58 * sp["Eye"] * body_len * L)
    inner = cv2.GaussianBlur(lum, (0, 0), max(0.6, 0.45 * r_px))
    outer = cv2.GaussianBlur(lum, (0, 0), 1.6 * r_px)
    resp = outer - inner
    # Search around where the game's table puts the eye (along the body and up the head), favouring the middle.
    ex0 = sp["EyeAt"] * body_len
    region = np.zeros_like(lum, dtype=bool)
    prior = np.zeros_like(lum)
    for i in range(N):
        x = xs[i]
        if 0.5 * ex0 < x < 1.7 * ex0 + 0.01:
            xa = int(round(xs_px[i]))
            ya = int(round(axis_y[i] - body_up[i] * L + 1.1 * r_px))
            yb = int(round(axis_y[i] + 0.3 * body_down[i] * L))
            region[ya:yb, xa:xa + 2] = True
            ey0 = axis_y[i] - sp["EyeHigh"] * body_up[i] * L if sp["EyeHigh"] >= 0 else axis_y[i] - sp["EyeHigh"] * body_down[i] * L
            yy = np.arange(ya, max(yb, ya + 1))
            prior[ya:yb, xa:xa + 2] = (-((x - ex0) / (0.5 * ex0 + 0.01)) ** 2
                                       - ((yy - ey0) / (0.45 * (body_up[i] + body_down[i]) * L + 1e-6)) ** 2)[:, None]
    spread = np.percentile(resp[region], 99) - np.percentile(resp[region], 50) if region.any() else 1.0
    resp = resp + 0.5 * spread * prior
    resp[~region] = -1e9
    ey, ex = np.unravel_index(int(np.argmax(resp)), resp.shape)
    eye = P(ex, ey)
    eye_r = r_px / L

    # Colours in bands along the middle of the body, skipping dark markings and highlights.
    def band(r0, r1, xa=0.35, xb=0.65):
        px = []
        for i in range(N):
            if not (xa < xs[i] < xb):
                continue
            ytop, ybot = axis_y[i] - body_up[i] * L, axis_y[i] + body_down[i] * L
            xp = int(round(xs_px[i]))
            for y in range(int(ytop + r0 * (ybot - ytop)), int(ytop + r1 * (ybot - ytop))):
                if mask[y, xp]:
                    px.append(rgb[y, xp])
        px = np.array(px, dtype=float) / 255.0
        if len(px) == 0:
            return [0.5, 0.5, 0.5]
        l = px @ np.array([0.3, 0.59, 0.11])
        lo, hi = np.percentile(l, 20), np.percentile(l, 75)
        keep = px[(l >= lo) & (l <= hi)]
        return [round(float(v), 3) for v in (keep if len(keep) else px).mean(axis=0)]

    colours = {"ridge": band(0.0, 0.08), "upper": band(0.1, 0.28), "mid": band(0.38, 0.55), "mid2": band(0.55, 0.7),
               "lower": band(0.72, 0.84), "belly": band(0.86, 0.97), "head_top": band(0.05, 0.3, 0.03, 0.12),
               "cheek": band(0.45, 0.7, eye[0] + eye_r * 1.5, eye[0] + eye_r * 4), "throat": band(0.85, 0.97, 0.05, 0.15)}
    fin_cols = {}
    for name, f in fins.items():
        comp = [c for c in comps if any(abs(c["cx"] - np.mean([p[0] for p in f["tips"]])) < 0.08 for _ in [0])]
    for c in comps:
        px = rgb[np.round(c["fy"] * L + tip_y).astype(int), np.round(c["fx"] * L + x0).astype(int)] / 255.0
        l = px @ np.array([0.3, 0.59, 0.11])
        membrane = px[(l >= np.percentile(l, 25)) & (l <= np.percentile(l, 70))].mean(axis=0)
        ray = px[l <= np.percentile(l, 12)].mean(axis=0)
        c["col"] = (membrane, ray)
    for name, f in fins.items():
        fxm = np.mean([p[0] for p in f["base"]])
        cand = [c for c in comps if (c["side"] == "tail") == (name == "FinCaudal")]
        if cand:
            c = min(cand, key=lambda c: abs(c["cx"] - fxm) + (0 if (c["side"] == "top") == (name.startswith("FinDorsal") or name == "FinAdipose") else 1))
            mem, ray = c["col"]
            fin_cols[name] = dict(membrane=[round(float(v), 3) for v in mem], ray=[round(float(v), 3) for v in ray])
    # The outline is measured from the snout-to-stalk axis; the fins and the eye from the level of the snout's tip. When the
    # picture was not turned level (the fish lay within 0.8 degrees of it), the two differ along the fish: give the fins
    # and the eye in the axis' frame too, so that they sit on the body as drawn.
    tilt = (axis_y[si] - tip_y) / L / max(stalk_x, 1e-6)
    for f in fins.values():
        for key in ("base", "tips"):
            f[key] = [[px, round(py - tilt * px, 4)] for px, py in f[key]]
    eye = (eye[0], eye[1] - tilt * eye[0])
    return dict(L=L, x0=x0, tip_y=tip_y, flip=flip, rgb=rgb, stalk_x=stalk_x, xs=xs, body_up=body_up,
                body_down=body_down, fins=fins, eye=eye, eye_r=eye_r, colours=colours, fin_colours=fin_cols, tilt=tilt)


def overlay(m, path):
    rgb = m["rgb"].copy()
    L, x0, tip_y = m["L"], m["x0"], m["tip_y"]

    def Q(x, y):                    # fish units (the axis' frame) -> pixels
        return int(round(x * L + x0)), int(round((y + m["tilt"] * x) * L + tip_y))
    xs = m["xs"]
    si = int(np.searchsorted(xs, m["stalk_x"]))
    axis = np.zeros_like(xs)
    pts_t = [Q(x, a - u) for x, a, u in zip(xs[:si], axis[:si], m["body_up"][:si])]
    pts_b = [Q(x, a + d) for x, a, d in zip(xs[:si], axis[:si], m["body_down"][:si])]
    cv2.polylines(rgb, [np.array(pts_t)], False, (255, 0, 0), 2)
    cv2.polylines(rgb, [np.array(pts_b)], False, (255, 0, 0), 2)
    colours = {"FinDorsal1": (0, 160, 255), "FinDorsal2": (0, 220, 120), "FinAnal": (255, 0, 255), "FinCaudal": (255, 140, 0),
               "FinPelvic": (120, 60, 255), "FinAdipose": (0, 0, 0), "FinPectoral": (0, 120, 0)}
    for name, f in m["fins"].items():
        col = colours.get(name, (0, 0, 0))
        cv2.polylines(rgb, [np.array([Q(*p) for p in f["base"]])], False, col, 3)
        cv2.polylines(rgb, [np.array([Q(*p) for p in f["tips"]])], False, col, 2)
    ex, ey = Q(*m["eye"])
    cv2.circle(rgb, (ex, ey), max(2, int(m["eye_r"] * L)), (0, 255, 255), 2)
    cv2.imwrite(path, cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR))


def main():
    data = json.load(open(sys.argv[1], encoding="utf-8"))
    sid, picture, out = sys.argv[2], sys.argv[3], sys.argv[4]
    ov = sys.argv[5] if len(sys.argv) > 5 and not sys.argv[5].startswith("--") else None
    flip = True if "--flip" in sys.argv else (False if "--noflip" in sys.argv else None)
    m = measure(data, sid, picture, flip)
    if ov:
        overlay(m, ov)
    xs = m["xs"]
    si = int(np.searchsorted(xs, m["stalk_x"]))
    sel = np.unique(np.concatenate([np.linspace(0, 12, 7).astype(int), np.linspace(12, si, 64).astype(int)]))
    res = {
        "id": sid, "source": "AtlasRyb.online", "picture": picture.split("/")[-1],
        "x1": round(float(m["stalk_x"]), 4),
        "up": [[round(float(xs[i]), 4), round(float(m["body_up"][i]), 4)] for i in sel],
        "down": [[round(float(xs[i]), 4), round(float(m["body_down"][i]), 4)] for i in sel],
        "eye": [round(float(m["eye"][0]), 4), round(float(m["eye"][1]), 4)], "eye_r": round(float(m["eye_r"]), 4),
        "fins": m["fins"], "colours": m["colours"], "fin_colours": m["fin_colours"],
    }
    json.dump(res, open(out, "w", encoding="utf-8"), indent=1)
    print(f"{sid}: x1 {res['x1']}, fins {sorted(res['fins'])}, eye {res['eye']} r {res['eye_r']}")


if __name__ == "__main__":
    main()
