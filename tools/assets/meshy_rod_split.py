"""Turns the user's Meshy rod and reel into the player's rod and reel for RodRig.

    python3 -I tools/assets/meshy_rod_split.py IN.glb OUT.glb [--butt X --seat X --blank X] [--no-reel]
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
NO_REEL = "--no-reel" in ARGS
if NO_REEL:
    ARGS.remove("--no-reel")
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


def place_reel():
    """A Meshy reel (foot on top at +Y, the rod along Z, the spool's front at +Z, the crank at +X) into RodRig's space:
    the foot's top under the reel seat, the spool's front towards the tip, the crank on the left; scaled so the spool's
    axis lies as far under the foot as the built reel's. Prints where the crank's knob ends up."""
    j, blob, d = read(SRC)
    P, N, UV, T = d["P"], d["N"], d["UV"], d["I"]
    top = P[:, 1].max()
    foot = P[P[:, 1] > top - 0.05]
    fx, fz = foot[:, 0].mean(), foot[:, 2].mean()
    front = P[P[:, 2] > P[:, 2].max() - 0.05]                 # the spool's front (drag knob): the spool's axis
    spool_y = front[:, 1].mean()
    k = FOOT_TO_SPOOL / (top - spool_y)
    rig = np.stack([-(P[:, 0] - fx), P[:, 2] - fz, P[:, 1] - top], axis=1) * k + np.array([0.0, SEAT_Y, -SEAT_RADIUS])
    n = np.stack([-N[:, 0], N[:, 2], N[:, 1]], axis=1)
    write(j, blob, split_reel(rig, n, UV, T), DST)


# The parts of the placed reel that move (rig space, measured on the Meshy reel by slicing it): the crank (everything
# left of the gearbox's side cover in front of the bail's pivot) turns about the hub where its arm leaves the cover;
# the rotor (the perforated cup behind the spool with its two arms, and in front of it what stands out past the spool's lip: the bail's
# arms, wire and roller) turns about the spool's axis. The spool, the body, the foot stay. RodRig mirrors these numbers.
CRANK_LEFT, CRANK_FRONT = -0.042, 0.14
CRANK_HUB = np.array([-0.045, 0.089, -0.0965])
SPOOL_AXIS_XZ = np.array([-0.0023, -0.0879])
ROTOR_BACK, SPOOL_BACK, ROTOR_RADIUS, SPOOL_RADIUS = 0.115, 0.146, 0.037, 0.0315
ARMS_BACK, STEM_FRONT = 0.106, 0.134
BAIL_FRONT, BAIL_RADIUS, BAIL_FROM, BAIL_TO = 0.158, 0.044, 75.0, 222.0          # the rotor's two arms reach back past the cup; the foot's stem ends here


def split_reel(rig, n, UV, T):
    """-> Body (rig space), Rotor (about its axis point at the rotor's back), Crank (about the hub, turned so the knob
    hangs straight down at crank angle 0 as RodRig.KnobAt expects)."""
    c = rig[T].mean(axis=1)                                        # triangle centres
    r = np.hypot(c[:, 0] - SPOOL_AXIS_XZ[0], c[:, 2] - SPOOL_AXIS_XZ[1])
    crank = (c[:, 0] < CRANK_LEFT) & (c[:, 1] < CRANK_FRONT)
    up = np.degrees(np.arctan2(c[:, 2] - SPOOL_AXIS_XZ[1], c[:, 0] - SPOOL_AXIS_XZ[0]))   # 90 = towards the rod
    stem = (c[:, 1] < STEM_FRONT) & (np.abs(up - 90) < 25)
    rotor = ~crank & (((c[:, 1] >= ROTOR_BACK) & (c[:, 1] < SPOOL_BACK) & (r < ROTOR_RADIUS))
                      | ((c[:, 1] >= ARMS_BACK) & (c[:, 1] < SPOOL_BACK) & (r >= 0.03) & (r < 0.065) & ~stem)
                      | ((c[:, 1] >= SPOOL_BACK) & (r >= SPOOL_RADIUS)))
    knob_v = rig[rig[:, 0] < -0.082]
    knob = (knob_v.min(axis=0) + knob_v.max(axis=0)) / 2
    rel = knob - CRANK_HUB
    angle0 = np.arctan2(rel[1], -rel[2])                           # KnobAt: (sin a, -cos a) in (y, z)
    ca, sa = np.cos(-angle0), np.sin(-angle0)
    def unturn(v):                                                 # about X by -angle0
        return np.stack([v[:, 0], v[:, 1] * ca - v[:, 2] * sa, v[:, 1] * sa + v[:, 2] * ca], axis=1)
    arm = np.hypot(rel[1], rel[2])
    print(f"crank hub {CRANK_HUB}, knob out {rel[0]:.4f}, arm {arm:.4f}, drawn at {np.degrees(angle0):.1f} deg")
    rotor_pivot = np.array([SPOOL_AXIS_XZ[0], ROTOR_BACK, SPOOL_AXIS_XZ[1]])
    # The bail's wire: the half ring round the back of the spool from the roller's arm (~75 deg) over the crank side
    # to the other arm (~-140 deg). It swings over about the line through its two ends (the pivots).
    around = np.where(up < 0, up + 360, up)
    bail = rotor & (c[:, 1] >= SPOOL_BACK) & (c[:, 1] < BAIL_FRONT) & (r >= BAIL_RADIUS) \
        & (around >= BAIL_FROM) & (around <= BAIL_TO)
    rotor &= ~bail
    bv = c[bail]
    ba = around[bail]
    lo, hi = np.percentile(ba, 4), np.percentile(ba, 96)
    end_a, end_b = bv[ba <= lo].mean(axis=0), bv[ba >= hi].mean(axis=0)
    axis = (end_b - end_a) / np.linalg.norm(end_b - end_a)
    middle = bv[np.abs(ba - (lo + hi) / 2) < 10].mean(axis=0) - end_a
    middle -= axis * middle.dot(axis)
    sign = 1.0 if np.cross(axis, middle)[1] > 0 else -1.0       # a positive quarter turn would carry it forwards
    print(f"bail pivot {end_a.round(4)} (rotor local {(end_a - rotor_pivot).round(4)}), axis {axis.round(4)}, "
          f"opens by {sign:+.0f} x angle; {bail.sum()} triangles")
    pieces = []
    for name, mask, origin, turn in (("Body", ~crank & ~rotor & ~bail, np.zeros(3), False),
                                     ("Rotor", rotor, rotor_pivot, False),
                                     ("Bail", bail, end_a, False),
                                     ("Crank", crank, CRANK_HUB, True)):
        tris = T[mask]
        used, inverse = np.unique(tris, return_inverse=True)
        v, nn = rig[used] - origin, n[used]
        if turn:
            v, nn = unturn(v), unturn(nn)
        pieces.append((name, v, nn, UV[used], inverse.reshape(-1, 3)))
        print(f"{name}: {len(tris)} triangles")
    return pieces


def main():
    if REEL_MODE:
        place_reel()
        return
    j, blob, d = read(SRC)
    P, N, UV, T = d["P"], d["N"], d["UV"], d["I"]
    # The blank's axis (y, z as a line in x), fitted on the bare blank between the guides.
    xs = np.linspace(-0.9, 0.15, 30)
    cy, cz, cx = [], [], []
    for a, b2 in zip(xs[:-1], xs[1:]):
        s = P[(P[:, 0] > a) & (P[:, 0] <= b2)]
        if len(s) < 20 or np.abs(s[:, 2]).max() > 0.02:      # a guide: skip
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
