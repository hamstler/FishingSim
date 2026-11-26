"""Puts the rigged A-pose fisherman's real hands on the rod and the crank, after the grip the user set by hand.

Inputs: the rigged figure (meshy_apose_rig.py: the villagers' 65-bone skeleton, fingers included) and the grip read
from the hand-posed .blend (read_grip_pose.py: the rod's frame, the crank, each arm's shoulder, elbow and wrist).

1. The figure is laid onto the posed one (its torso: scale and shift), so its shoulders stand where those were.
2. Each hand is placed round its bar by a search: the palm against the bar from every side round it, the bar slanting
   10-30 degrees across the palm at the base of the fingers, the index finger towards the rod's tip (right hand: the
   fingers running down under the handle, the thumb's side on top, the reel's stem between the middle and the ring
   finger) or towards the reel (left hand on the crank's knob); the way with the wrist nearest the posed wrist and the
   straightest wrist is taken. The arm reaches it by IK (the shoulder kept, the elbow towards the posed one) and the
   hand takes that turn.
3. The fingers close: each joint turns towards the palm until its bone touches the bar (a cylinder: the handle 14.3 mm,
   the knob 10 mm, plus the finger's own thickness); the thumb is laid on top of the handle / against the knob.
4. Renders the hands from several sides and saves a .blend in which the hands' targets ("Cel_dlon_P/L", on the rod and
   on the crank), the elbows' poles and the crank can still be moved by hand.

    blender -b --factory-startup --python tools/assets/pose_apose_grip.py -- RIGGED.glb GRIP.json OUT_DIR [--no-render]
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ROD_ID, REEL_ID = "rods/float_ostoja", "reels/small_hokuto"
HANDLE_R, KNOB_R = 0.0143, 0.010
FINGER_R = 0.0085              # a finger's half thickness: the bone runs through its middle
FINGERS = ("index", "middle", "ring", "pinky")


def g2b(v):
    return Vector((v[0], -v[2], v[1]))


def load(path):
    scene = bpy.context.scene
    before = set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in scene.objects if o not in before]


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    rigged, grip_path, out_dir = (os.path.abspath(a) for a in argv[:3])
    os.makedirs(out_dir, exist_ok=True)
    grip = json.load(open(grip_path))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    if rigged.endswith(".blend"):
        # the user's Meshy-rigged fisherman with finger bones added (add_finger_bones.py)
        bpy.ops.wm.open_mainfile(filepath=rigged)
        scene = bpy.context.scene
        arm, body = bpy.data.objects["Szkielet"], bpy.data.objects["Wedkarz"]
        arm.animation_data_clear()          # the walk and run clips (NLA tracks) would pose it over the IK
    else:
        figure_objs = load(rigged)
        arm = next(o for o in figure_objs if o.type == "ARMATURE")
        body = next(o for o in figure_objs if o.type == "MESH" and o.name.startswith("Body"))
        for o in figure_objs:
            if o.type == "MESH" and o is not body:
                o.hide_render = True
                o.hide_set(True)
        body.name = "Wedkarz"
        arm.name = "Szkielet"
    # the villagers' bone names, or the Unity Humanoid ones of the Meshy rig
    humanoid = "RightHand" in arm.data.bones
    NAMES = {f"{a}_{s}": f"{'Right' if s == 'r' else 'Left'}{b}" for s in "lr"
             for a, b in (("hand", "Hand"), ("lowerarm", "LowerArm"), ("upperarm", "UpperArm"), ("clavicle", "Shoulder"))} if humanoid else {}
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
        pb.rotation_mode = "QUATERNION"
    bpy.context.view_layer.update()

    # 1. onto the posed figure: scale and shift from its torso (nearest points, a few rounds)
    deps = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(deps)
    mine = [ev.matrix_world @ v.co for v in ev.data.vertices]
    old_all = [Vector(p) for p in grip["torso"]]
    # first the heights' middles together (the rig stands on the ground, the posed figure was centred), then the torso
    lift = -(min(p.z for p in mine) + max(p.z for p in mine)) / 2
    mine = [p + Vector((0, 0, lift)) for p in mine]
    torso = [p for p in mine if -0.05 < p.z < 0.55 and abs(p.x) < 0.17][::7]
    old = old_all
    kd = KDTree(len(old))
    for i, p in enumerate(old):
        kd.insert(p, i)
    kd.balance()
    s, t = 1.0, Vector()
    for _ in range(25):
        moved = [p * s + t for p in torso]
        pairs = [(p, kd.find(q)[0]) for p, q in zip(torso, moved)]
        cp = sum((a for a, _ in pairs), Vector()) / len(pairs)
        cq = sum((b for _, b in pairs), Vector()) / len(pairs)
        num = sum(((a - cp).dot(b - cq) for a, b in pairs))
        den = sum(((a - cp).length_squared for a, _ in pairs))
        s = num / den
        t = cq - cp * s
    err = sum((kd.find(p * s + t)[2] for p in torso)) / len(torso)
    t = t + Vector((0, 0, lift)) * s
    arm.matrix_world = Matrix.Translation(t) @ Matrix.Scale(s, 4) @ arm.matrix_world
    bpy.context.view_layer.update()
    print(f"POSEGRIP figure onto the posed one: scale {s:.3f}, shift {tuple(round(v, 3) for v in t)}, torso {err * 1000:.1f} mm")

    # the rod and the reel where the user put them
    rod = {o.name.split(".")[0]: o for o in load(os.path.join(ROOT, "assets", "models", ROD_ID + ".glb")) if o.type == "MESH"}
    reel = {o.name.split(".")[0]: o for o in load(os.path.join(ROOT, "assets", "models", REEL_ID + ".glb")) if o.type == "MESH"}
    frames = json.load(open(os.path.join(ROOT, "assets", "models", REEL_ID + ".json"), encoding="utf-8"))
    rig = bpy.data.objects.new("Wedka", None)
    rig.empty_display_type, rig.empty_display_size, rig.show_in_front = "ARROWS", 0.12, True
    scene.collection.objects.link(rig)
    for name, o in rod.items():
        o.parent = rig
        if name.startswith("Seg"):
            o.location = Vector((0, 0, 0.25 + 0.3 * int(name[3:])))
    for o in reel.values():
        o.parent = rig
    reel["Rotor"].location = g2b(frames["rotor"])
    if "Bail" in reel:
        reel["Bail"].parent = reel["Rotor"]
        reel["Bail"].location = g2b(frames["bail_pivot"])
    crank = bpy.data.objects.new("Korba", None)
    crank.empty_display_type, crank.empty_display_size = "CIRCLE", 0.05
    crank.lock_location = (True, True, True)
    crank.lock_rotation = (False, True, True)
    scene.collection.objects.link(crank)
    crank.parent = rig
    crank.location = g2b(frames["hub"])
    reel["Crank"].parent = crank
    rig.matrix_world = Matrix(grip["wedka"])
    crank.rotation_euler = (grip["korba_angle"], 0, 0)
    bpy.context.view_layer.update()
    for o in list(rod.values()) + list(reel.values()):
        o.hide_select = True

    rot = rig.matrix_world.to_3x3().normalized()
    blank = (rot @ Vector((0, 0, 1))).normalized()
    down = (rot @ Vector((0, 1, 0))).normalized()                  # the reel's side
    # the reel's foot along the blank (as rod_in_pose.py measures it): the stem between the middle and ring fingers
    body_pts = [v.co for v in reel["Body"].data.vertices]
    top_y = min(p.y for p in body_pts)
    foot = [p.z for p in body_pts if p.y < top_y + 0.012]
    stem_at = rig.matrix_world @ Vector((0, 0, sum(foot) / len(foot)))
    knob_local = g2b((frames["knob_out"], 0.0, -frames["arm"]))
    knob_at = crank.matrix_world @ knob_local
    knob_axis = (crank.matrix_world.to_3x3().normalized() @ Vector((1, 0, 0))).normalized()
    inward = knob_axis if frames["knob_out"] < 0 else -knob_axis    # from the knob towards the reel

    body.hide_viewport = True          # no mesh evaluation while the bones are searched

    class Bones:
        def __getitem__(self, name):
            return arm.pose.bones[NAMES.get(name, name)]
    pb = Bones()
    W = arm.matrix_world

    def head(name):
        return W @ pb[name].head

    def update():
        bpy.context.view_layer.update()

    def hand_frame(side):
        """The hand at rest: wrist, fingers direction, palm normal (where the fingers curl to), index side, and the
        middle-ring knuckles' middle."""
        wrist = head(f"hand_{side}")
        mid = head(f"middle_01_{side}")
        f = (mid - wrist).normalized()
        c = (head(f"index_01_{side}") - head(f"pinky_01_{side}"))
        c = (c - f * c.dot(f)).normalized()
        tip_bone = pb[f"middle_03_{side}"]
        tip0 = W @ tip_bone.tail
        b = pb[f"middle_01_{side}"]
        b.rotation_quaternion = Matrix.Rotation(0.5, 4, "X").to_quaternion()
        update()
        tip1 = W @ tip_bone.tail
        b.rotation_quaternion = (1, 0, 0, 0)
        update()
        n = tip1 - tip0
        n = (n - f * n.dot(f) - c * n.dot(c)).normalized()
        knuck = (mid + head(f"ring_01_{side}")) * 0.5
        return wrist, f, n, c, knuck

    def place(side, axis_pt, axis_dir, index_dir, radius, want_wrist, elbow, fingers_down=None):
        """The best way round the bar: (hand world matrix, wrist, score)."""
        wrist, f, n, c, knuck = hand_frame(side)
        sign = 1.0 if n.cross(c).dot(f) > 0 else -1.0
        grip_l = knuck + n * (0.012 + radius) - f * 0.012      # the bar's middle against the palm by the knuckles
        rest_hand = (W @ pb[f"hand_{side}"].matrix).normalized()
        L = Matrix((f, n, c)).transposed()
        u0 = axis_dir.orthogonal().normalized()
        best = None
        for deg in range(0, 360, 6):
            a = math.radians(deg)
            sdir = (u0 * math.cos(a) + axis_dir.cross(u0) * math.sin(a)).normalized()
            nw = -sdir
            for slant in (10, 20, 30, -10, -20):
                cw = Matrix.Rotation(math.radians(slant), 3, nw) @ index_dir
                cw = (cw - nw * cw.dot(nw)).normalized()
                fw = sign * nw.cross(cw)
                if fingers_down is not None and fw.dot(fingers_down) < 0.2:
                    continue
                R = Matrix((fw, nw, cw)).transposed() @ L.transposed()
                wr = axis_pt - R @ (grip_l - wrist)
                fore = (wr - elbow).normalized()
                bend = fore.angle(fw)
                cost = (wr - want_wrist).length * 8 + bend
                if best is None or cost < best[0]:
                    best = (cost, R, wr, deg, slant, bend)
        cost, R, wr, deg, slant, bend = best
        m = (R @ rest_hand.to_3x3()).to_4x4()
        m.translation = wr
        print(f"POSEGRIP {side}: palm from {deg} deg round the bar, slant {slant} deg, wrist {(wr - want_wrist).length * 1000:.0f} mm "
              f"from the posed wrist, bent {math.degrees(bend):.0f} deg")
        return m

    tgt = {"r": grip["P"], "l": grip["L"]}
    handle_pt = stem_at
    right_m = place("r", handle_pt, blank, blank, HANDLE_R, Vector(tgt["r"]["wrist"]), Vector(tgt["r"]["elbow"]), fingers_down=down)
    left_m = place("l", knob_at, knob_axis, inward, KNOB_R, Vector(tgt["l"]["wrist"]), Vector(tgt["l"]["elbow"]))

    # 2. IK: the wrists to their targets (on the rod / on the crank), the hands turned as the targets
    def control(name, mat, parent, shape="ARROWS", size=0.06):
        o = bpy.data.objects.new(name, None)
        o.empty_display_type, o.empty_display_size, o.show_in_front = shape, size, True
        scene.collection.objects.link(o)
        o.matrix_world = mat
        update()
        if parent is not None:
            o.parent = parent
            o.matrix_parent_inverse = parent.matrix_world.inverted()
        return o

    for side, label, m, holder in (("r", "P", right_m, rig), ("l", "L", left_m, crank)):
        pb[f"clavicle_{side}"].lock_ik_x = pb[f"clavicle_{side}"].lock_ik_y = pb[f"clavicle_{side}"].lock_ik_z = True
        target = control(f"Cel_dlon_{label}", m, holder)
        elbow_want = Vector(tgt[side]["elbow"])
        shoulder = head(f"upperarm_{side}")
        line = m.translation - shoulder
        out = (elbow_want - shoulder) - line * ((elbow_want - shoulder).dot(line) / line.length_squared)
        pole = control(f"Cel_lokiec_{label}", Matrix.Translation(elbow_want + out.normalized() * 0.25), None, "SPHERE", 0.03)
        ik = pb[f"hand_{side}"].constraints.new("IK")
        ik.target, ik.pole_target, ik.chain_count, ik.use_tail = target, pole, 3, False
        cr = pb[f"hand_{side}"].constraints.new("COPY_ROTATION")
        cr.target, cr.target_space, cr.owner_space = target, "WORLD", "WORLD"
        best = None
        for deg in range(-180, 180, 10):
            ik.pole_angle = math.radians(deg)
            update()
            e = head(f"lowerarm_{side}")
            if best is None or (e - elbow_want).length < best[0]:
                best = ((e - elbow_want).length, deg)
        ik.pole_angle = math.radians(best[1])
        update()
        print(f"POSEGRIP IK {side}: wrist {(head(f'hand_{side}') - m.translation).length * 1000:.1f} mm off, "
              f"elbow {best[0] * 1000:.0f} mm from the posed elbow")

    # 3. the fingers close round the bars
    def bar_dist(p, pt, d):
        v = p - pt
        return (v - d * v.dot(d)).length

    def close(side, pt, d, radius, half_len):
        for fname in FINGERS:
            bones = [pb[f"{fname}_{k:02d}_{side}"] for k in (1, 2, 3)]
            angles = [0.0, 0.0, 0.0]
            done = [False, False, False]
            for step in range(40):
                for k in range(3):
                    if done[k]:
                        continue
                    angles[k] += math.radians(3)
                    bones[k].rotation_quaternion = Matrix.Rotation(angles[k], 4, "X").to_quaternion()
                update()
                for k in range(3):
                    if done[k]:
                        continue
                    a, b = W @ bones[k].head, W @ bones[k].tail
                    touch = any(bar_dist(a.lerp(b, x), pt, d) < radius + FINGER_R and abs((a.lerp(b, x) - pt).dot(d)) < half_len
                                for x in (0.5, 0.75, 1.0))
                    if touch or angles[k] > math.radians(100):
                        done[k] = True
                        if touch:   # back off the step that went in
                            angles[k] -= math.radians(3)
                            bones[k].rotation_quaternion = Matrix.Rotation(angles[k], 4, "X").to_quaternion()
                if all(done):
                    break
            update()
            print(f"POSEGRIP {side} {fname}: {', '.join(f'{math.degrees(x):.0f}' for x in angles)} deg")

    close("r", handle_pt, blank, HANDLE_R, 0.09)
    close("l", knob_at, knob_axis, KNOB_R, 0.03)

    # the thumbs: on top of the handle a little ahead of the hand / against the knob from the far side
    def thumb(side, goal, pt, d, radius):
        b1, b2, b3 = (pb[f"thumb_{k:02d}_{side}"] for k in (1, 2, 3))
        best = None
        for ax in range(-60, 61, 10):
            for az in range(-60, 61, 10):
                for curl in (0, 20, 40):
                    b1.rotation_quaternion = (Matrix.Rotation(math.radians(ax), 4, "X") @ Matrix.Rotation(math.radians(az), 4, "Z")).to_quaternion()
                    b2.rotation_quaternion = Matrix.Rotation(math.radians(curl), 4, "X").to_quaternion()
                    b3.rotation_quaternion = Matrix.Rotation(math.radians(curl), 4, "X").to_quaternion()
                    update()
                    tip = W @ b3.tail
                    inside = min(bar_dist(W @ b.tail, pt, d) for b in (b2, b3)) < radius + FINGER_R * 0.8
                    cost = (tip - goal).length + (1.0 if inside else 0.0)
                    if best is None or cost < best[0]:
                        best = (cost, ax, az, curl)
        _, ax, az, curl = best
        b1.rotation_quaternion = (Matrix.Rotation(math.radians(ax), 4, "X") @ Matrix.Rotation(math.radians(az), 4, "Z")).to_quaternion()
        b2.rotation_quaternion = b3.rotation_quaternion = Matrix.Rotation(math.radians(curl), 4, "X").to_quaternion()
        update()
        print(f"POSEGRIP {side} thumb: tip {best[0] * 1000:.0f} mm from its place")

    thumb("r", handle_pt + blank * 0.05 - down * (HANDLE_R + FINGER_R), handle_pt, blank, HANDLE_R)
    thumb("l", knob_at - (head("hand_l") - knob_at).normalized() * (KNOB_R + FINGER_R), knob_at, knob_axis, KNOB_R)
    body.hide_viewport = False
    update()

    # 4. light, cameras, renders, the file
    sun = bpy.data.objects.new("Slonce", bpy.data.lights.new("Slonce", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(45), 0, math.radians(25))
    sun.hide_select = True
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("Niebo")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.65, 0.72, 1.0)
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1200
    body.hide_select = True
    arm.hide_select = True
    mid = (handle_pt + knob_at) / 2
    # the eyes: in front of the head joint (the Meshy rig's "headfront" marks the face), under the cap's peak
    if "headfront" in arm.data.bones:
        eye = head("Head") + Vector((0.0, -0.16, 0.12))
    else:
        eye = head("Head") + Vector((0.0, -0.12, 0.08))
    views = {
        "Widok_z_oczu": ("eyes", eye, mid, 24),
        "Z_przodu": ("front", mid + Vector((0.0, -0.45, 0.1)), mid, 50),
        "Z_prawej": ("right", mid + Vector((-0.4, -0.15, 0.08)), mid, 50),
        "Z_lewej": ("left", mid + Vector((0.4, -0.2, 0.05)), mid, 50),
        "Z_gory": ("above", mid + Vector((0.05, 0.1, 0.45)), mid, 50),
        "Z_dolu": ("below", mid + Vector((0.05, -0.25, -0.4)), mid, 50),
    }
    for name, (short, at, look, lens) in views.items():
        cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        cam.data.clip_start, cam.data.lens = 0.005, lens
        scene.collection.objects.link(cam)
        cam.location = at
        cam.rotation_euler = (look - at).to_track_quat("-Z", "Y").to_euler()
        if "--no-render" not in argv:
            scene.camera = cam
            scene.render.filepath = os.path.join(out_dir, f"grip_{short}.png")
            bpy.ops.render.render(write_still=True)
    scene.camera = bpy.data.objects["Z_prawej"]
    for screen in bpy.data.screens:
        for area in screen.areas:
            for space in area.spaces:
                if space.type == "VIEW_3D":
                    space.shading.type = "SOLID"
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "dlonie_na_wedce.blend"))
    print("POSEGRIP saved dlonie_na_wedce.blend ->", out_dir)


main()
