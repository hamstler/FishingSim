"""Refines the finger rig of a figure .blend (add_finger_bones.py's): the finger bones' joints are moved into the middle
of the fingers and the fingers' weights are worked out again from the mesh, so a bent finger keeps its shape and its
skin (with joints at the skin, a bend sheared the surface and the picture slid over it).

For each hand, a few rounds of: every vertex past a finger's knuckle and near its bones goes to the finger whose bone
line it lies nearest; each joint moves (across its finger, not along it) to the middle of that finger's vertices in a
thin slab round it. Then each finger vertex's weight: the finger takes over from the hand just past the knuckle, and
along the finger each phalanx blends into the next over a few millimetres round the joint. The armature deforms with
Preserve Volume (dual quaternions: a bent joint does not collapse).

    blender -b FIGURE.blend --python tools/assets/refine_fingers.py -- [sides, default "rl"]
"""
import sys

import bpy
from mathutils import Vector

FINGERS = ("thumb", "index", "middle", "ring", "pinky")
SIDE = {"l": "Left", "r": "Right"}
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SIDES = argv[0] if argv else "rl"

arm = bpy.data.objects["Szkielet"]
body = bpy.data.objects["Wedkarz"]
W, MW = arm.matrix_world, body.matrix_world
to_arm = W.inverted()
verts = [MW @ v.co for v in body.data.vertices]
names = {g.index: g.name for g in body.vertex_groups}


def smooth(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def chain_joints(side, f):
    bones = [arm.data.bones[f"{f}_{k:02d}_{side}"] for k in (1, 2, 3)]
    return [W @ b.head_local for b in bones] + [W @ bones[-1].tail_local]


def project(p, J):
    """(distance to the polyline, index of the segment, fraction along it, distance along the chain from J[0])."""
    best = None
    run = 0.0
    for k in range(3):
        a, b = J[k], J[k + 1]
        ab = b - a
        L = ab.length
        u = (p - a).dot(ab) / max(L * L, 1e-12)
        uc = min(max(u, 0.0), 1.0)
        d = (p - (a + ab * uc)).length
        along = run + u * L if (k == 0 and u < 0) or (k == 2 and u > 1) or 0 <= u <= 1 else run + uc * L
        if best is None or d < best[0]:
            best = (d, k, u, along)
        run += L
    return best


for side in SIDES:
    hand_name = f"{SIDE[side]}Hand"
    hand_bones = {hand_name} | {f"{f}_{k:02d}_{side}" for f in FINGERS for k in (1, 2, 3)}
    region = [i for i, v in enumerate(body.data.vertices)
              if sum(g.weight for g in v.groups if names[g.group] in hand_bones) > 0.5]
    joints = {f: chain_joints(side, f) for f in FINGERS}
    assign = {}
    for rnd in range(5):
        assign = {f: [] for f in FINGERS}
        for i in region:
            p = verts[i]
            best = None
            for f, J in joints.items():
                d, k, u, along = project(p, J)
                if along < -0.004 or d > 0.014:
                    continue
                if best is None or d < best[0]:
                    best = (d, f)
            if best:
                assign[best[1]].append(i)
        moved = 0.0
        for f, J in joints.items():
            pts = [verts[i] for i in assign[f]]
            if len(pts) < 20:
                continue
            new = []
            for j, c in enumerate(J):
                if j == 0:
                    d = (J[1] - J[0]).normalized()
                elif j == 3:
                    d = (J[3] - J[2]).normalized()
                else:
                    d = ((J[j + 1] - J[j]).normalized() + (J[j] - J[j - 1]).normalized()).normalized()
                slab = [q for q in pts if abs((q - c).dot(d)) < 0.0035 and ((q - c) - d * (q - c).dot(d)).length < 0.016]
                if j == 3:
                    # the tip: the last few millimetres of the finger's own vertices along the chain
                    far = max(((q - J[2]).dot(d) for q in pts), default=(J[3] - J[2]).length)
                    c = J[2] + d * max(far - 0.004, 0.008)
                    slab = [q for q in pts if abs((q - c).dot(d)) < 0.004]
                if len(slab) >= 8:
                    m = sum(slab, Vector()) / len(slab)
                    off = (m - c) - d * (m - c).dot(d)
                    c = c + off
                new.append(c)
            moved = max(moved, max((a - b).length for a, b in zip(new, J)))
            joints[f] = new
        print(f"FINGERFIX {side} round {rnd}: joints moved up to {moved * 1000:.1f} mm, vertices per finger "
              f"{ {f: len(v) for f, v in assign.items()} }")
        if moved < 0.0005:
            break

    # the bones onto the new joints, each keeping its Z axis (the palm's side: a turn about +X still curls it)
    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    for f, J in joints.items():
        for k in range(3):
            b = eb[f"{f}_{k + 1:02d}_{side}"]
            z = b.z_axis.copy()
            b.head = to_arm @ J[k]
            b.tail = to_arm @ J[k + 1]
            b.align_roll(z)
    bpy.ops.object.mode_set(mode="OBJECT")

    # the weights again: the hand's share stays where the hand was; the finger takes over just past its knuckle and
    # its phalanges blend round each joint
    groups = {n: body.vertex_groups[n] for n in hand_bones}
    hand_vg = groups[hand_name]
    for f, idx in assign.items():
        J = joints[f]
        lens = [(J[k + 1] - J[k]).length for k in range(3)]
        bounds = [0.0, lens[0], lens[0] + lens[1], sum(lens)]
        for i in idx:
            v = body.data.vertices[i]
            total = sum(g.weight for g in v.groups if names[g.group] in hand_bones)
            d, k, u, along = project(verts[i], J)
            share = smooth(-0.006, 0.006, along) if f != "thumb" else smooth(-0.004, 0.012, along)
            w = [0.0, 0.0, 0.0]
            blend = 0.005
            w1 = smooth(bounds[1] - blend, bounds[1] + blend, along)
            w2 = smooth(bounds[2] - blend, bounds[2] + blend, along)
            w[0], w[1], w[2] = 1.0 - w1, w1 - w2, w2
            for n in hand_bones:
                groups[n].remove([i])
            hand_vg.add([i], max(total * (1.0 - share), 0.0), "REPLACE") if share < 1.0 else None
            for kk in range(3):
                if w[kk] * share > 0.001:
                    groups[f"{f}_{kk + 1:02d}_{side}"].add([i], total * share * w[kk], "REPLACE")
    print(f"FINGERFIX {side}: bones recentred, weights redone for {sum(len(v) for v in assign.values())} finger vertices")

for m in body.modifiers:
    if m.type == "ARMATURE":
        m.use_deform_preserve_volume = True
bpy.ops.wm.save_mainfile()
print("FINGERFIX saved", bpy.data.filepath)
