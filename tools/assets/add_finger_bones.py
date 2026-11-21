"""Gives the user's Meshy-rigged fisherman (Unity Humanoid skeleton, 23 bones: one bone per hand) finger bones, from
the same figure rigged with fingers by meshy_apose_rig.py (the villagers' skeleton fitted onto the untextured A-pose).

Both stand in the same A-pose at rest: the finger-rigged figure is laid onto the Meshy one (scale and shift, then each
hand on its own), its finger joints become new bones under LeftHand / RightHand (index, middle, ring, pinky, thumb; three
each; rolled so a turn about the bone's own X curls the finger towards the palm), and the Meshy hand's weight is shared
out to them as the nearest vertices of the finger-rigged figure share theirs. The animations are dropped; the file is
saved for posing (pose_apose_grip.py).

    blender -b --factory-startup --python tools/assets/add_finger_bones.py -- MESHY_RIGGED.glb FINGER_RIGGED.glb OUT.blend
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

FINGERS = ("thumb", "index", "middle", "ring", "pinky")
SIDE = {"l": "Left", "r": "Right"}


def load(path):
    scene = bpy.context.scene
    before = set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in scene.objects if o not in before]


def rest_points(obj):
    ev = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return [ev.matrix_world @ v.co for v in ev.data.vertices]


def kdtree(pts):
    k = KDTree(len(pts))
    for i, p in enumerate(pts):
        k.insert(p, i)
    k.balance()
    return k


def similarity(src, tree, s=1.0, t=Vector(), rounds=20, scale=True):
    for _ in range(rounds):
        pairs = [(p, tree.find(p * s + t)[0]) for p in src]
        cp = sum((a for a, _ in pairs), Vector()) / len(pairs)
        cq = sum((b for _, b in pairs), Vector()) / len(pairs)
        if scale:
            s = sum(((a - cp).dot(b - cq) for a, b in pairs)) / sum(((a - cp).length_squared for a, _ in pairs))
        t = cq - cp * s
    err = sum((tree.find(p * s + t)[2] for p in src)) / len(src)
    return s, t, err


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    meshy_path, finger_path, out = (os.path.abspath(a) for a in argv[:3])
    bpy.ops.wm.read_factory_settings(use_empty=True)

    meshy = load(meshy_path)
    m_arm = next(o for o in meshy if o.type == "ARMATURE")
    m_body = next(o for o in meshy if o.type == "MESH")
    m_arm.animation_data_clear()            # the walk and run clips (also as NLA tracks)
    for pb in m_arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    finger = load(finger_path)
    f_arm = next(o for o in finger if o.type == "ARMATURE")
    f_body = next(o for o in finger if o.type == "MESH" and o.name.startswith("Body"))
    for pb in f_arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    m_arm.data.pose_position = "REST"
    f_arm.data.pose_position = "REST"
    bpy.context.view_layer.update()

    M = rest_points(m_body)
    F = rest_points(f_body)
    m_tree = kdtree(M)
    # the whole figure, then each hand on its own (shift only)
    s, t, err = similarity(F[::40], m_tree)
    print(f"FINGERS figure onto the Meshy one: scale {s:.4f}, shift {tuple(round(v, 4) for v in t)}, {err * 1000:.1f} mm")
    fb = f_arm.data.bones
    fW = f_arm.matrix_world
    hand_fit = {}
    for side in "lr":
        wrist = fW @ fb[f"hand_{side}"].head_local
        near = [p for p in F if (p - wrist).length < 0.13][::3]
        hs, ht, herr = similarity(near, m_tree, s, t, rounds=15, scale=False)
        hand_fit[side] = (hs, ht)
        print(f"FINGERS {side} hand onto the Meshy hand: {herr * 1000:.1f} mm")

    def carry(side, p):
        hs, ht = hand_fit[side]
        return p * hs + ht

    # the palm's normal per hand: where the finger-rigged middle finger's tip goes when its first joint turns about X
    # (meshy_apose_rig's bones curl towards the palm that way)
    normals = {}
    for side in "lr":
        tip_bone = f_arm.pose.bones[f"middle_03_{side}"]
        tip0 = fW @ tip_bone.tail
        f_arm.data.pose_position = "POSE"
        b = f_arm.pose.bones[f"middle_01_{side}"]
        b.rotation_mode = "XYZ"
        b.rotation_euler = (0.4, 0, 0)
        bpy.context.view_layer.update()
        tip1 = fW @ tip_bone.tail
        b.rotation_euler = (0, 0, 0)
        f_arm.data.pose_position = "REST"
        bpy.context.view_layer.update()
        normals[side] = (tip1 - tip0).normalized()

    # new bones under the Meshy hands
    bpy.context.view_layer.objects.active = m_arm
    for o in bpy.context.selected_objects:
        o.select_set(False)
    m_arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = m_arm.data.edit_bones
    to_m = m_arm.matrix_world.inverted()
    made = []
    for side in "lr":
        hand = eb[f"{SIDE[side]}Hand"]
        for fname in FINGERS:
            parent = hand
            for k in (1, 2, 3):
                src = fb[f"{fname}_{k:02d}_{side}"]
                head = carry(side, fW @ src.head_local)
                nxt = fb.get(f"{fname}_{k + 1:02d}_{side}") or fb.get(f"{fname}_04_leaf_{side}")
                tail = carry(side, fW @ (nxt.head_local if nxt is not None else src.tail_local))
                bone = eb.new(f"{fname}_{k:02d}_{side}")
                bone.head, bone.tail = to_m @ head, to_m @ tail
                n_local = (to_m.to_3x3() @ normals[side]).normalized()
                bone.align_roll(n_local)          # the bone's Z towards the palm: a turn about +X curls the finger
                bone.parent = parent
                bone.use_connect = k > 1
                parent = bone
                made.append(bone.name)
    # Meshy's arm bones point anywhere (their tails metres away from the next joint): IK solves along the bones, so
    # each one is laid from its joint to the next (the mesh is bound at rest, so this changes no shape)
    for side in "lr":
        up, low, hand = (eb[f"{SIDE[side]}{n}"] for n in ("UpperArm", "LowerArm", "Hand"))
        up.tail = low.head.copy()
        low.tail = hand.head.copy()
        hand.tail = eb[f"middle_01_{side}"].head.copy()
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"FINGERS {len(made)} finger bones added")

    # weights: the Meshy hand's weight shared out as the nearest finger-rigged vertices share theirs
    f_ev = f_body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    fgroups = {g.index: g.name for g in f_body.vertex_groups}
    for side in "lr":
        hand_name = f"{SIDE[side]}Hand"
        hg = m_body.vertex_groups[hand_name].index
        # the finger-rigged figure's hand vertices, carried onto the Meshy hand
        wrist = fW @ fb[f"hand_{side}"].head_local
        src_idx = [i for i, v in enumerate(f_body.data.vertices) if (F[i] - wrist).length < 0.16]
        src_pts = [carry(side, F[i]) for i in src_idx]
        tree = kdtree(src_pts)
        shares = []
        for i in src_idx:
            v = f_body.data.vertices[i]
            total = 0.0
            d = {}
            for g in v.groups:
                name = fgroups[g.group]
                if name.endswith("_" + side) and (name.startswith(FINGERS) or name.startswith("hand")):
                    d[name] = d.get(name, 0.0) + g.weight
                    total += g.weight
            shares.append({k: w / total for k, w in d.items() if not k.startswith("hand")} if total > 1e-6 else {})
        groups = {name: m_body.vertex_groups.get(name) or m_body.vertex_groups.new(name=name) for name in made if name.endswith("_" + side)}
        hand_vg = m_body.vertex_groups[hand_name]
        changed = 0
        for v in m_body.data.vertices:
            w_hand = next((g.weight for g in v.groups if g.group == hg), 0.0)
            if w_hand < 0.02:
                continue
            p = m_body.matrix_world @ v.co if False else M[v.index]
            # a soft neighbourhood by distance (never along the mesh's edges: its UV seams split the vertices, and
            # anything smoothed over the edges tore the fingers apart at the seams)
            near = tree.find_range(p, 0.014) or tree.find_n(p, 4)
            acc, wsum = {}, 0.0
            for _, j, dist in near:
                wj = math.exp(-(dist / 0.006) ** 2) + 1e-6
                wsum += wj
                for name, sh in shares[j].items():
                    acc[name] = acc.get(name, 0.0) + sh * wj
            if not acc:
                continue
            given = 0.0
            for name, a in acc.items():
                w = w_hand * a / wsum
                if w > 0.01 and name in groups:
                    groups[name].add([v.index], w, "REPLACE")
                    given += w
            hand_vg.add([v.index], max(w_hand - given, 0.0), "REPLACE")
            changed += 1
        print(f"FINGERS {side}: {changed} hand vertices share their weight with the fingers")

    for o in finger:
        bpy.data.objects.remove(o, do_unlink=True)
    m_arm.data.pose_position = "POSE"
    m_arm.name, m_body.name = "Szkielet", "Wedkarz"
    bpy.ops.wm.save_as_mainfile(filepath=out)
    print("FINGERS saved", out)


main()
