"""Turns the user's Meshy rod and reel into the player's rod and reel for RodRig.

    python3 -I tools/assets/meshy_rod_split.py IN.glb OUT.glb [--butt X --seat X --blank X] [--no-reel]
    python3 -I tools/assets/meshy_rod_split.py IN.glb OUT.glb --auto      (a reel-less rod: landmarks found, see auto_landmarks)
    python3 -I tools/assets/meshy_rod_split.py --reel IN.glb OUT.glb     (a reel model on its own, see place_reel)

The player's rod now: IN = the reel-less Meshy rod (meshy_fish.py --file ... --keep-mr), OUT =
assets/models/meshy_rod2_player.glb with --butt 0.951 --seat 0.45 --blank 0.32 --no-reel; the reel:
--reel IN assets/models/meshy_reel.glb.

A rod model lies along X (butt at +X, tip at -X) with any reel hanging at -Y. It is laid into RodRig's space (the rod
along +Y, the reel side -Z): the butt at y = -0.33, the reel seat at RodRig.SeatCentre (0.1), the blank's start at
RodRig.BlankStart (0.25) and the tip-top at 0.25 + 10 x 0.3 m; its thickness scaled so the grip is as thick as
RodRig's own. A reel on it is cut away unless --no-reel. The blank is cut into the ten sections of RodRig's bending
joints, each mesh in its section's own frame (its origin at the section's base), so the game hangs every piece on its
joint and the model bends with RodBendModel. Nodes: Handle, Seg0 .. Seg9.
"""
import json, os, struct, sys
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ARGS = [a for a in sys.argv[1:]]
def _opt(name, default):
    if name in ARGS:
        i = ARGS.index(name)
        v = ARGS[i + 1]
        del ARGS[i:i + 2]
        return float(v)
    return default
X_BUTT_ARG, X_SEAT_ARG, X_BLANK_ARG = _opt("--butt", 0.951), _opt("--seat", 0.42), _opt("--blank", 0.22)
AUTO = "--auto" in ARGS
if AUTO:
    ARGS.remove("--auto")
NO_REEL = "--no-reel" in ARGS or AUTO
if "--no-reel" in ARGS:
    ARGS.remove("--no-reel")
SHOW = "--show" in ARGS
if SHOW:
    ARGS.remove("--show")
REEL_MODE = "--reel" in ARGS
if REEL_MODE:
    ARGS.remove("--reel")
if len(ARGS) != 2:
    sys.exit(__doc__)
SRC, DST = ARGS

# RodRig's frame.
BUTT_Y, SEAT_Y, BLANK_START, JOINTS, SEGMENT = -0.33, 0.1, 0.25, 10, 0.3
GRIP_RADIUS = 0.0148          # RodRig's cork grip
# The model's landmarks along X (measured): the butt, the reel seat, where the blank leaves the fore grip, the tip.
X_BUTT, X_SEAT, X_BLANK = X_BUTT_ARG, X_SEAT_ARG, X_BLANK_ARG
REEL_X = (0.25, 0.64)         # the reel hangs under the seat here


def read(path):
    b = open(path, "rb").read()
    n = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + n])
    blob = b[28 + n:]
    def acc(i, dtype, width):
        a = j["accessors"][i]
        v = j["bufferViews"][a["bufferView"]]
        o = v.get("byteOffset", 0) + a.get("byteOffset", 0)
        count = a["count"] * width
        return np.frombuffer(blob[o:o + count * np.dtype(dtype).itemsize], dtype=dtype).reshape(a["count"], width) \
            if width > 1 else np.frombuffer(blob[o:o + count * np.dtype(dtype).itemsize], dtype=dtype)
    prim = j["meshes"][0]["primitives"][0]
    attr = prim["attributes"]
    ia = j["accessors"][prim["indices"]]
    idx_type = {5125: "<u4", 5123: "<u2"}[ia["componentType"]]
    data = dict(P=acc(attr["POSITION"], "<f4", 3).astype(np.float64), N=acc(attr["NORMAL"], "<f4", 3).astype(np.float64),
                UV=acc(attr["TEXCOORD_0"], "<f4", 2), I=acc(prim["indices"], idx_type, 1).astype(np.int64).reshape(-1, 3))
    return j, blob, data


def write(j, blob, pieces, dst):
    """pieces: [(name, positions, normals, uvs, triangle indices)] -> a .glb with the source's material and pictures."""
    out = bytearray()
    views, accessors, meshes, nodes = [], [], [], []

    def add(data, target=None):
        while len(out) % 4:
            out.append(0)
        view = {"buffer": 0, "byteOffset": len(out), "byteLength": len(data)}
        if target:
            view["target"] = target
        views.append(view)
        out.extend(data)
        return len(views) - 1

    for name, pos, nor, uv, ind in pieces:
        pos, nor, uv, ind = pos.astype("<f4"), nor.astype("<f4"), uv.astype("<f4"), ind.reshape(-1).astype("<u4")
        a0 = len(accessors)
        accessors.append({"bufferView": add(pos.tobytes(), 34962), "componentType": 5126, "count": len(pos), "type": "VEC3",
                          "min": pos.min(0).tolist(), "max": pos.max(0).tolist()})
        accessors.append({"bufferView": add(nor.tobytes(), 34962), "componentType": 5126, "count": len(nor), "type": "VEC3"})
        accessors.append({"bufferView": add(uv.tobytes(), 34962), "componentType": 5126, "count": len(uv), "type": "VEC2"})
        accessors.append({"bufferView": add(ind.tobytes(), 34963), "componentType": 5125, "count": len(ind), "type": "SCALAR"})
        meshes.append({"name": name, "primitives": [{"attributes": {"POSITION": a0, "NORMAL": a0 + 1, "TEXCOORD_0": a0 + 2},
                                                     "indices": a0 + 3, "material": 0}]})
        nodes.append({"name": name, "mesh": len(meshes) - 1})
    images = []
    for im in j["images"]:
        v = j["bufferViews"][im["bufferView"]]
        o = v.get("byteOffset", 0)
        images.append({"bufferView": add(blob[o:o + v["byteLength"]]), "mimeType": im["mimeType"], "name": im.get("name", "")})
    doc = {
        "asset": {"version": "2.0", "generator": "FishingSim meshy_rod_split.py"},
        "scene": 0, "scenes": [{"nodes": list(range(len(nodes)))}], "nodes": nodes, "meshes": meshes,
        "materials": j["materials"], "textures": j["textures"], "images": images, "samplers": j.get("samplers", [{}]),
        "accessors": accessors, "bufferViews": views, "buffers": [{"byteLength": len(out)}],
        "extensionsUsed": j.get("extensionsUsed", []), "extensionsRequired": j.get("extensionsRequired", []),
    }
    while len(out) % 4:
        out.append(0)
    js = json.dumps(doc, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    glb = b"glTF" + struct.pack("<II", 2, 28 + len(js) + len(out)) + struct.pack("<I", len(js)) + b"JSON" + js \
        + struct.pack("<I", len(out)) + b"BIN\0" + bytes(out)
    open(dst, "wb").write(glb)
    print(f"{dst}: {len(glb) / 1e6:.1f} MB")


# RodRig's reel seat: the foot's top touches the seat's underside here (rig space), and the built reel's spool axis
# lies this far below it.
SEAT_RADIUS, FOOT_TO_SPOOL = 0.0125, 0.0755


# Per reel (the output's name): where its moving parts are, in rig metres after placing (measured by slicing each
# model; `--reel IN OUT --show` prints the slices). crank_left: the gearbox's
# side cover (everything left of it and behind crank_front is the crank); rotor_back/spool_back: the rotor's cup between
# them turns, as do the parts standing out past spool_radius ahead of spool_back; arms_back: the rotor's arms reach back
# to here; stem_front: the foot's stem ends here (above the axis it is not rotor); bail_*: the wire's half ring behind
# the spool (front edge, least radius, angle range round the spool's axis, 90 = towards the rod, 180 = the crank side;
# from > to passes 0); no_bail: the wire stays on the rotor; lip: ahead of it anything off the axis is rotor (a wire
# drawn in front of the spool).
# turn: degrees about the vertical before anything else, for a reel drawn with its spool's front elsewhere than +Z.
REEL_DEFAULT = dict(crank_left=-0.042, crank_front=0.14, rotor_back=0.115, spool_back=0.146, rotor_radius=0.037,
                    spool_radius=0.0315, arms_back=0.106, stem_front=0.134, bail_front=0.158, bail_radius=0.044,
                    bail_from=75.0, bail_to=222.0)
REELS = {
    "meshy_reel": {},
    "medium_hokuto": dict(turn=-90.0, crank_left=-0.046, no_bail=True, lip=0.165),
    "small_hokuto": dict(crank_left=-0.034, bail_back=0.140, bail_front=0.166, bail_radius=0.034, bail_from=40.0, bail_to=265.0),
    "small_fjordline": dict(crank_left=-0.046, bail_back=0.140, bail_front=0.170, bail_radius=0.038, bail_from=315.0, bail_to=220.0),
    "baitrunner_kaminari": dict(crank_left=-0.050, bail_back=0.150, bail_front=0.170, bail_radius=0.037,
                                bail_from=100.0, bail_to=290.0),
    "big_ironwood": dict(crank_left=-0.034, bail_from=300.0, bail_to=235.0),
    "baitrunner_fjordline": dict(crank_left=-0.050, crank_front=0.14, rotor_back=0.106, spool_back=0.150, rotor_radius=0.045,
                                 spool_radius=0.031, arms_back=0.106, stem_front=0.134, bail_back=0.152, bail_front=0.168,
                                 bail_radius=0.037, bail_from=120.0, bail_to=330.0),
}


def place_reel():
    """A Meshy reel (foot on top at +Y, the rod along Z, the spool's front at +Z, the crank on either side) into RodRig's
    space: the foot's top under the reel seat, the spool's front towards the tip, the crank on the left (a reel drawn
    with its crank at -X is mirrored); scaled so the spool's axis lies as far under the foot as the built reel's.
    Writes OUT (Body, Rotor, Bail, Crank) and OUT's .json frame for RodRig."""
    j, blob, d = read(SRC)
    P, N, UV, T = d["P"], d["N"], d["UV"], d["I"]
    name = os.path.splitext(os.path.basename(DST))[0]
    prm = dict(REEL_DEFAULT, **REELS.get(name, {}))
    if prm.get("turn"):                                        # a reel drawn with its spool's front elsewhere than +Z
        t = np.radians(prm["turn"])
        R = np.array([[np.cos(t), 0, np.sin(t)], [0, 1, 0], [-np.sin(t), 0, np.cos(t)]])
        P, N = P @ R.T, N @ R.T
    top = P[:, 1].max()
    foot = P[P[:, 1] > top - 0.05]
    fx, fz = foot[:, 0].mean(), foot[:, 2].mean()
    if fx - P[:, 0].min() > P[:, 0].max() - fx:            # the knob reaches furthest to -X: mirror it to +X
        P = P * np.array([-1.0, 1.0, 1.0]); N = N * np.array([-1.0, 1.0, 1.0]); fx = -fx
        T = T[:, [0, 2, 1]]
        print("mirrored (crank drawn at -X)")
    front = P[P[:, 2] > P[:, 2].max() - 0.05]                 # the spool's front (drag knob): the spool's axis
    spool_y = front[:, 1].mean()
    # Meshy sometimes squashes a reel sideways: the spool's section just behind the front is made round again.
    ring = P[(P[:, 2] > P[:, 2].max() - 0.35) & (P[:, 2] < P[:, 2].max() - 0.15) & (np.abs(P[:, 1] - spool_y) < 0.6)]
    wide = np.ptp(ring[:, 0]); tall = 2 * np.percentile(np.abs(ring[:, 1] - spool_y), 98)
    if abs(tall / wide - 1) > 0.12:
        # Across the body (within the spool's half width of its axis) stretched by kx; beyond it (the crank) only
        # moved out by what the body grew, so the crank keeps its size.
        kx = tall / wide
        cx = front[:, 0].mean()
        half = wide / 2
        dx = P[:, 0] - cx
        P = P.copy()
        P[:, 0] = cx + np.where(np.abs(dx) < half, dx * kx, dx + np.sign(dx) * half * (kx - 1))
        inside = np.abs(dx) < half
        N = N.copy()
        N[inside, 0] /= kx
        N /= np.linalg.norm(N, axis=1, keepdims=True)
        fx = cx + (fx - cx) * kx
        print(f"spool made round: across x {kx:.2f}")
    k = FOOT_TO_SPOOL / (top - spool_y)
    rig = np.stack([-(P[:, 0] - fx), P[:, 2] - fz, P[:, 1] - top], axis=1) * k + np.array([0.0, SEAT_Y, -SEAT_RADIUS])
    n = np.stack([-N[:, 0], N[:, 2], N[:, 1]], axis=1)
    axis_xz = np.array([-(front[:, 0].mean() - fx) * k, (spool_y - top) * k - SEAT_RADIUS])
    if SHOW:
        show_reel(rig, axis_xz)
        return
    pieces, frame = split_reel(rig, n, UV, T, axis_xz, prm)
    write(j, blob, pieces, DST)
    json.dump(frame, open(os.path.splitext(DST)[0] + ".json", "w"))
    print(json.dumps(frame))


def show_reel(rig, axis_xz):
    """Slices of a placed reel for choosing its REELS entry: x (left/right) and, round the spool's axis, the largest
    radius per 15 degrees for each 3 mm along the rod."""
    x = rig[:, 0]
    print(f"spool axis x {axis_xz[0]:.4f} z {axis_xz[1]:.4f}; x {x.min():.3f}..{x.max():.3f}; "
          f"y {rig[:, 1].min():.3f}..{rig[:, 1].max():.3f}")
    for a in np.arange(np.floor(x.min() * 250) / 250, 0.0, 0.004):
        s = (x >= a) & (x < a + 0.004)
        if s.any():
            q = rig[s]
            print(f"x {a:+.3f} n {s.sum():5d} y {q[:, 1].min():.3f}..{q[:, 1].max():.3f} z {q[:, 2].min():.3f}..{q[:, 2].max():.3f}")
    r = np.hypot(rig[:, 0] - axis_xz[0], rig[:, 2] - axis_xz[1])
    ang = np.degrees(np.arctan2(rig[:, 2] - axis_xz[1], rig[:, 0] - axis_xz[0]))
    print("y \\ angle from -180 by 15: largest radius in mm (r >= 20 mm)")
    for y in np.arange(rig[:, 1].min(), rig[:, 1].max(), 0.003):
        s = (rig[:, 1] >= y) & (rig[:, 1] < y + 0.003) & (r >= 0.02) & (r < 0.08)
        row = ""
        for a in range(-180, 180, 15):
            t = s & (ang >= a) & (ang < a + 15)
            row += f"{int(r[t].max() * 1000):3d}" if t.any() else "  ."
        print(f"{y:.3f}", row)


def split_reel(rig, n, UV, T, axis_xz, prm):
    """-> pieces Body (rig space), Rotor (about its axis point at the rotor's back), Bail (about its first end), Crank
    (about the hub, turned so the knob hangs straight down at crank angle 0 as RodRig.KnobAt expects), and the frame."""
    c = rig[T].mean(axis=1)                                        # triangle centres
    r = np.hypot(c[:, 0] - axis_xz[0], c[:, 2] - axis_xz[1])
    crank = (c[:, 0] < prm["crank_left"]) & (c[:, 1] < prm["crank_front"])
    up = np.degrees(np.arctan2(c[:, 2] - axis_xz[1], c[:, 0] - axis_xz[0]))   # 90 = towards the rod
    stem = (c[:, 1] < prm["stem_front"]) & (np.abs(up - 90) < 25)
    rotor = ~crank & (((c[:, 1] >= prm["rotor_back"]) & (c[:, 1] < prm["spool_back"]) & (r < prm["rotor_radius"])
                       & ~(stem & (r > 0.03)))
                      | ((c[:, 1] >= prm["arms_back"]) & (c[:, 1] < prm["spool_back"]) & (r >= 0.03) & (r < 0.065) & ~stem)
                      | ((c[:, 1] >= prm["spool_back"]) & (r >= prm["spool_radius"]))
                      | ((c[:, 1] >= prm.get("lip", 9.0)) & (r >= 0.018)))
    # The hub: the middle of the crank's slice right outside the side cover; the knob: the crank's far end.
    cv = rig[(rig[:, 0] < prm["crank_left"]) & (rig[:, 0] > prm["crank_left"] - 0.006) & (rig[:, 1] < prm["crank_front"])]
    hub = np.array([prm["crank_left"] - 0.003, (cv[:, 1].min() + cv[:, 1].max()) / 2, (cv[:, 2].min() + cv[:, 2].max()) / 2])
    xmin = rig[:, 0].min()
    knob_v = rig[rig[:, 0] < xmin + 0.3 * (prm["crank_left"] - xmin)]
    knob = (knob_v.min(axis=0) + knob_v.max(axis=0)) / 2
    rel = knob - hub
    angle0 = np.arctan2(rel[1], -rel[2])                           # KnobAt: (sin a, -cos a) in (y, z)
    ca, sa = np.cos(-angle0), np.sin(-angle0)
    def unturn(v):                                                 # about X by -angle0
        return np.stack([v[:, 0], v[:, 1] * ca - v[:, 2] * sa, v[:, 1] * sa + v[:, 2] * ca], axis=1)
    arm = np.hypot(rel[1], rel[2])
    print(f"crank hub {hub.round(4)}, knob out {rel[0]:.4f}, arm {arm:.4f}, drawn at {np.degrees(angle0):.1f} deg")
    rotor_pivot = np.array([axis_xz[0], prm["rotor_back"], axis_xz[1]])
    # The angle from bail_from on round to bail_to (the range may pass 0/360).
    around = (up - prm["bail_from"]) % 360
    bail = rotor & (c[:, 1] >= prm.get("bail_back", prm["spool_back"])) & (c[:, 1] < prm["bail_front"]) & (r >= prm["bail_radius"]) \
        & (around <= (prm["bail_to"] - prm["bail_from"]) % 360)
    if prm.get("no_bail") or bail.sum() < 20:
        bail[:] = False                                            # the wire stays on the rotor (it does not open)
        end_a, axis = rotor_pivot, np.array([1.0, 0.0, 0.0])
        print("no bail cut")
    else:
        rotor &= ~bail
        bv = c[bail]
        ba = around[bail]
        lo, hi = np.percentile(ba, 4), np.percentile(ba, 96)
        end_a, end_b = bv[ba <= lo].mean(axis=0), bv[ba >= hi].mean(axis=0)
        axis = (end_b - end_a) / np.linalg.norm(end_b - end_a)
        mid = bv[np.abs(ba - (lo + hi) / 2) < 15]
        middle = (mid.mean(axis=0) if len(mid) else bv.mean(axis=0)) - end_a
        middle -= axis * middle.dot(axis)
        if np.cross(axis, middle)[1] < 0:                          # a positive turn must carry the wire forwards
            axis = -axis
        print(f"bail pivot {end_a.round(4)}, axis {axis.round(4)}; {bail.sum()} triangles")
    if os.environ.get("REEL_VIEW"):
        view_parts(rig, T, [(~crank & ~rotor & ~bail, (150, 150, 150)), (rotor, (60, 120, 230)), (bail, (230, 60, 60)),
                            (crank, (60, 190, 80))], os.environ["REEL_VIEW"])
    pieces = []
    for name, mask, origin, turn in (("Body", ~crank & ~rotor & ~bail, np.zeros(3), False),
                                     ("Rotor", rotor, rotor_pivot, False),
                                     ("Bail", bail, end_a, False),
                                     ("Crank", crank, hub, True)):
        tris = T[mask]
        if len(tris) == 0:
            continue
        used, inverse = np.unique(tris, return_inverse=True)
        v, nn = rig[used] - origin, n[used]
        if turn:
            v, nn = unturn(v), unturn(nn)
        pieces.append((name, v, nn, UV[used], inverse.reshape(-1, 3)))
        print(f"{name}: {len(tris)} triangles")
    f = lambda v: [round(float(t), 4) for t in v]
    frame = {"hub": f(hub), "arm": round(float(arm), 4), "knob_out": round(float(rel[0]), 4), "rotor": f(rotor_pivot),
             "bail_pivot": f(end_a - rotor_pivot), "bail_axis": f(axis),
             "body": f([axis_xz[0], prm["rotor_back"] - 0.027, axis_xz[1]])}
    return pieces, frame


def view_parts(rig, T, parts, path):
    """A check picture of a cut reel (needs Pillow): from the left (crank side), from the front and from below, each
    triangle in its part's colour (grey body, blue rotor, red bail, green crank), nearer ones drawn last."""
    from PIL import Image, ImageDraw
    S = 360
    img = Image.new("RGB", (S * 3, S), (255, 255, 255))
    dr = ImageDraw.Draw(img)
    colour = np.zeros((len(T), 3), int)
    for mask, col in parts:
        colour[mask] = col
    lo, hi = rig.min(axis=0), rig.max(axis=0)
    sc = (S - 20) / (hi - lo).max()
    c = rig[T].mean(axis=1)
    # (u axis, sign, v axis, sign, depth axis, sign towards the viewer)
    for vi, (u, su, v, sv, w, sw) in enumerate([(1, 1, 2, 1, 0, -1), (0, -1, 2, 1, 1, 1), (1, 1, 0, -1, 2, -1)]):
        for t in np.argsort(c[:, w] * sw):
            pts = rig[T[t]]
            xy = [(vi * S + S / 2 + su * (p[u] - (lo[u] + hi[u]) / 2) * sc, S / 2 - sv * (p[v] - (lo[v] + hi[v]) / 2) * sc) for p in pts]
            shade = 0.55 + 0.45 * (c[t, w] * sw - (lo[w] if sw > 0 else -hi[w])) / (hi[w] - lo[w] + 1e-9)
            dr.polygon(xy, fill=tuple(int(k * shade) for k in colour[t]))
    img.save(path)


def auto_landmarks(P):
    """The butt, the reel seat and the blank's start along X for a reel-less rod (butt at +X): the handle is the
    run of thick slices from the butt (guides are one or two thick slices: smoothed away by a median of five); the seat
    sits where RodRig's does, 26 % of the handle back from the blank."""
    x = P[:, 0]
    lo, hi = x.min(), x.max()
    n = 120
    th = np.zeros(n)
    for k in range(n):
        s = P[(x >= lo + (hi - lo) * k / n) & (x < lo + (hi - lo) * (k + 1) / n)]
        if len(s):
            th[k] = np.percentile(s[:, 2], 95) - np.percentile(s[:, 2], 5)     # across (guides hang in -Y)
    sm = np.array([np.median(th[max(0, k - 2):k + 3]) for k in range(n)])
    grip = np.median(sm[-12:])
    k = n - 1
    while k > 0 and sm[k - 1] > 0.55 * grip:
        k -= 1
    x_blank = lo + (hi - lo) * k / n
    x_butt = hi
    x_seat = x_blank + 0.26 * (x_butt - x_blank)
    print(f"auto: butt {x_butt:.3f}, seat {x_seat:.3f}, blank {x_blank:.3f} (grip {grip:.4f})")
    return x_butt, x_seat, x_blank


def main():
    global X_BUTT, X_SEAT, X_BLANK
    if REEL_MODE:
        place_reel()
        return
    j, blob, d = read(SRC)
    P, N, UV, T = d["P"], d["N"], d["UV"], d["I"]
    if AUTO:
        X_BUTT, X_SEAT, X_BLANK = auto_landmarks(P)
    # The blank's axis (y, z as a line in x), fitted on the bare blank between the guides (a slice much taller than
    # wide holds a guide hanging under it: skipped).
    xs = np.linspace(P[:, 0].min() + 0.05, X_BLANK - 0.03, 40)
    cy, cz, cx = [], [], []
    for a, b2 in zip(xs[:-1], xs[1:]):
        s = P[(P[:, 0] > a) & (P[:, 0] <= b2)]
        if len(s) < 20:
            continue
        hy, hz = np.ptp(s[:, 1]), np.ptp(s[:, 2])
        if hy > 1.5 * hz + 0.004 or hz > 0.03:
            continue
        cx.append((a + b2) / 2); cy.append((s[:, 1].max() + s[:, 1].min()) / 2); cz.append((s[:, 2].max() + s[:, 2].min()) / 2)
    fy = np.polyfit(cx, cy, 1); fz = np.polyfit(cx, cz, 1)
    ay = np.polyval(fy, P[:, 0]); az = np.polyval(fz, P[:, 0])
    # The handle's radius at the seat, for the thickness.
    hs = P[(P[:, 0] > X_SEAT + 0.12) & (P[:, 0] < X_BUTT - 0.05)]
    r_handle = np.median(np.hypot(hs[:, 1] - np.polyval(fy, hs[:, 0]), hs[:, 2] - np.polyval(fz, hs[:, 0])))
    kr = GRIP_RADIUS / r_handle
    print(f"axis y {fy}, z {fz}; handle radius {r_handle:.4f} -> radial scale {kr:.3f}")

    # Cut the reel away: triangles with every corner below the handle's surface over the reel's span.
    below = (P[:, 1] - ay < -r_handle * 1.08) & (P[:, 0] > REEL_X[0]) & (P[:, 0] < REEL_X[1]) & (not NO_REEL)
    keep = ~below[T].all(axis=1)
    T = T[keep]
    print(f"reel cut: {int((~keep).sum())} triangles")

    # Along the rod: piecewise linear from the model's X to RodRig's Y.
    tip_y = BLANK_START + JOINTS * SEGMENT
    x_tip = P[:, 0].min()
    knots_x = np.array([X_BUTT, X_SEAT, X_BLANK, x_tip])
    knots_y = np.array([BUTT_Y, SEAT_Y, BLANK_START, tip_y])
    y = np.interp(-P[:, 0], -knots_x, knots_y)
    slope = np.interp(-P[:, 0], -knots_x[:-1] - 1e-6, np.diff(knots_y) / np.diff(-knots_x))   # metres per model unit
    slope = np.where(-P[:, 0] > -knots_x[2], (tip_y - BLANK_START) / (X_BLANK - x_tip), slope)
    rig = np.stack([-(P[:, 2] - az) * kr, y, (P[:, 1] - ay) * kr], axis=1)
    # Normals under the uneven scale: the inverse transpose (along / slope, across / kr).
    n = np.stack([-N[:, 2] / kr, -N[:, 0] / slope, N[:, 1] / kr], axis=1)
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12

    # Pieces: the handle below the blank's start, then the ten sections by each triangle's middle.
    mid_y = rig[T].mean(axis=1)[:, 1]
    piece = np.where(mid_y < BLANK_START, -1, np.clip(((mid_y - BLANK_START) // SEGMENT).astype(int), 0, JOINTS - 1))

    pieces = []
    for p in range(-1, JOINTS):
        tris = T[piece == p]
        if len(tris) == 0:
            continue
        used, inverse = np.unique(tris, return_inverse=True)
        origin = np.array([0.0, 0.0, 0.0]) if p < 0 else np.array([0.0, BLANK_START + p * SEGMENT, 0.0])
        name = "Handle" if p < 0 else f"Seg{p}"
        pieces.append((name, rig[used] - origin, n[used], UV[used], inverse.reshape(-1, 3)))
        print(f"{name}: {len(tris)} triangles")
    write(j, blob, pieces, DST)


if __name__ == "__main__":
    main()
