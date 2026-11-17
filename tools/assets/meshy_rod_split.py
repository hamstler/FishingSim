"""Turns the Meshy spinning rod (assets/models/meshy_spinning_rod.glb) into the player's rod for RodRig.

    python3 -I tools/assets/meshy_rod_split.py [IN.glb] [OUT.glb]

The model lies along X (butt at +X, tip at -X) with its reel hanging at -Y. It is laid into RodRig's space (the rod
along +Y, the reel side -Z): the butt at y = -0.33, the reel seat at RodRig.SeatCentre (0.1), the blank's start at
RodRig.BlankStart (0.25) and the tip-top at 0.25 + 10 x 0.3 m; its thickness scaled so the grip is as thick as
RodRig's own. The reel is cut away (the game keeps its own animated reel, crank and bail). The blank is cut into the
ten sections of RodRig's bending joints, each mesh in its section's own frame (its origin at the section's base), so
the game hangs every piece on its joint and the model bends with RodBendModel. Nodes: Handle, Seg0 .. Seg9.
"""
import json, os, struct, sys
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "assets", "models", "meshy_spinning_rod.glb")
DST = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "assets", "models", "meshy_rod_player.glb")

# RodRig's frame.
BUTT_Y, SEAT_Y, BLANK_START, JOINTS, SEGMENT = -0.33, 0.1, 0.25, 10, 0.3
GRIP_RADIUS = 0.0148          # RodRig's cork grip
# The model's landmarks along X (measured): the butt, the reel seat, where the blank leaves the fore grip, the tip.
X_BUTT, X_SEAT, X_BLANK = 0.951, 0.42, 0.22
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


def main():
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
    below = (P[:, 1] - ay < -r_handle * 1.08) & (P[:, 0] > REEL_X[0]) & (P[:, 0] < REEL_X[1])
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

    for p in range(-1, JOINTS):
        tris = T[piece == p]
        if len(tris) == 0:
            continue
        used, inverse = np.unique(tris, return_inverse=True)
        origin = np.array([0.0, 0.0, 0.0]) if p < 0 else np.array([0.0, BLANK_START + p * SEGMENT, 0.0])
        pos = (rig[used] - origin).astype("<f4")
        nor = n[used].astype("<f4")
        uv = UV[used].astype("<f4")
        ind = inverse.reshape(-1).astype("<u4")
        a0 = len(accessors)
        accessors.append({"bufferView": add(pos.tobytes(), 34962), "componentType": 5126, "count": len(pos), "type": "VEC3",
                          "min": pos.min(0).tolist(), "max": pos.max(0).tolist()})
        accessors.append({"bufferView": add(nor.tobytes(), 34962), "componentType": 5126, "count": len(nor), "type": "VEC3"})
        accessors.append({"bufferView": add(uv.tobytes(), 34962), "componentType": 5126, "count": len(uv), "type": "VEC2"})
        accessors.append({"bufferView": add(ind.tobytes(), 34963), "componentType": 5125, "count": len(ind), "type": "SCALAR"})
        name = "Handle" if p < 0 else f"Seg{p}"
        meshes.append({"name": name, "primitives": [{"attributes": {"POSITION": a0, "NORMAL": a0 + 1, "TEXCOORD_0": a0 + 2},
                                                     "indices": a0 + 3, "material": 0}]})
        nodes.append({"name": name, "mesh": len(meshes) - 1})
        print(f"{name}: {len(ind) // 3} triangles")

    # The pictures as they were (WebP), and the material.
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
    open(DST, "wb").write(glb)
    print(f"{DST}: {len(glb) / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
