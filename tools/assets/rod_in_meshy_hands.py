"""The rod and the reel in the raw Meshy fisherman's real hands (the ~950k triangle figure with modelled fingers), posed and
rendered in Blender.

The raw figure is laid onto the game's rigged angler (assets/characters/player_angler.glb, whose Body is the same Meshy
figure collapsed to 50k and skinned by meshy_player_rig.py): scaled and moved onto it (ICP), and it takes the Body's bone
weights (nearest surface), so the game's skeleton moves it. In the modelled pose the fists are closed round nothing, the
hands 22 cm apart side by side; the grips are found on the mesh (the line through each fist with the most room round it,
enclosed on every side) and the hands are turned and moved onto the tackle with IK, the fingers kept as modelled:
  * right: the rod's reel seat in the right fist, the blank leaving it on the index finger's side, the thumb on top, so
    the reel's stem leaves the fist downwards between the fingers;
  * left: the crank's knob in the left fist, the index finger towards the reel, the crank at the angle that needs the
    least turn of the hand.

    blender -b --factory-startup --python tools/assets/rod_in_meshy_hands.py -- RAW.glb OUT_DIR [rod_id] [reel_id] [--no-render]

The saved wedka_w_dloniach.blend is for posing the grips by hand (its INSTRUKCJA text block): the hands' IK targets hang on
the rod and the crank, only the controls can be picked.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SEAT_CENTRE = 0.1
HANDLE_RADIUS = 0.0143
KNOB_RADIUS = 0.009
ROD_DIR = Vector((0.15, -0.72, 0.62)).normalized()   # the blank: forwards (-Y), up and a little towards the middle


def g2b(v):
    return Vector((v[0], -v[2], v[1]))


def fibonacci(n):
    pts = []
    for i in range(n):
        y = 1 - 2 * (i + 0.5) / n
        r = math.sqrt(1 - y * y)
        a = i * math.pi * (3 - math.sqrt(5))
        pts.append(Vector((math.cos(a) * r, y, math.sin(a) * r)))
    return pts


def load(path):
    scene = bpy.context.scene
    before = set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in scene.objects if o not in before]


def umeyama(src, dst):
    """Similarity (s, R, t) with dst ~ s R src + t."""
    ms, md = src.mean(0), dst.mean(0)
    a, b = src - ms, dst - md
    u, d, vt = np.linalg.svd(b.T @ a / len(src))
    sgn = np.eye(3)
    if np.linalg.det(u @ vt) < 0:
        sgn[2, 2] = -1
    r = u @ sgn @ vt
    s = (d * np.diag(sgn)).sum() / (a ** 2).sum(1).mean()
    return s, r, md - s * r @ ms


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    raw_path, out_dir = os.path.abspath(argv[0]), os.path.abspath(argv[1])
    ids = [a for a in argv[2:] if not a.startswith("--")]
    rod_id = ids[0] if ids else "rods/float_ostoja"
    reel_id = ids[1] if len(ids) > 1 else "reels/small_hokuto"
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    # the rigged angler in its rest (= the bind pose = the modelled pose): Body (the figure without its forearms) and
    # Arms_Meshy (Meshy's forearms and fists, skinned to the same skeleton)
    angler = load(os.path.join(ROOT, "assets", "characters", "player_angler.glb"))
    arm = next(o for o in angler if o.type == "ARMATURE")
    body = next(o for o in angler if o.name.startswith("Body"))
    fists = next(o for o in angler if o.name.startswith("Arms_Meshy"))
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()

    def points(o):
        ev = o.evaluated_get(deps)
        return np.array([ev.matrix_world @ v.co for v in ev.data.vertices])

    def kdtree(pts):
        k = KDTree(len(pts))
        for i, p in enumerate(pts):
            k.insert(p, i)
        k.balance()
        return k

    bverts, averts = points(body), points(fists)
    kd, kd_b, kd_a = kdtree(np.vstack([bverts, averts])), kdtree(bverts), kdtree(averts)
    allv = np.vstack([bverts, averts])

    raw = next(o for o in load(raw_path) if o.type == "MESH")
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(raw.data)
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)   # the UV seams' split vertices: one weight each, no cracks
    bm.to_mesh(raw.data)
    bm.free()
    print(f"MEESHYHANDS welded {before - len(raw.data.vertices)} seam vertices")
    n = len(raw.data.vertices)
    co = np.empty(n * 3)
    raw.data.vertices.foreach_get("co", co)
    mw = np.array(raw.matrix_world)
    rv = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    sample = rv[np.random.default_rng(1).choice(n, 8000, replace=False)]
    s = (allv[:, 2].max() - allv[:, 2].min()) / (rv[:, 2].max() - rv[:, 2].min())
    r = np.eye(3)
    t = allv.mean(0) - s * rv.mean(0)
    for it in range(40):
        moved = (s * (r @ sample.T)).T + t
        near = np.array([kd.find(Vector(p))[0] for p in moved])
        d = np.linalg.norm(near - moved, axis=1)
        keep = d < max(np.percentile(d, 85), 0.004)
        s, r, t = umeyama(sample[keep], near[keep])
        if it % 10 == 0:
            print(f"MEESHYHANDS icp {it}: mean {d.mean() * 1000:.1f} mm, scale {s:.4f}")
    m = Matrix([list(s * r[i]) + [t[i]] for i in range(3)] + [[0, 0, 0, 1]])
    raw.matrix_world = m @ raw.matrix_world
    bpy.context.view_layer.update()
    P = rv @ (s * r).T + t
    d_b = np.array([kd_b.find(Vector(p))[2] for p in P])
    d_a = np.array([kd_a.find(Vector(p))[2] for p in P])
    print(f"MEESHYHANDS raw onto the angler: scale {s:.4f}, mean {np.minimum(d_a, d_b).mean() * 1000:.1f} mm, "
          f"90% {np.percentile(np.minimum(d_a, d_b), 90) * 1000:.1f} mm")

    def transfer(src, mask=None):
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = raw
        raw.select_set(True)
        mod = raw.modifiers.new("Wagi", "DATA_TRANSFER")
        mod.object = src
        mod.use_object_transform = True
        mod.use_vert_data = True
        mod.data_types_verts = {"VGROUP_WEIGHTS"}
        mod.vert_mapping = "POLYINTERP_NEAREST"
        mod.layers_vgroup_select_src = "ALL"
        mod.layers_vgroup_select_dst = "NAME"
        if mask:
            mod.vertex_group = mask
        bpy.ops.object.datalayout_transfer(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)

    # the Body's weights everywhere, then Arms_Meshy's where the raw figure lies on the fists/forearms (a soft border)
    for g in list(raw.vertex_groups):
        raw.vertex_groups.remove(g)
    transfer(body)
    share = np.clip((d_b - d_a) / 0.012 + 0.5, 0, 1)
    mask = raw.vertex_groups.new(name="maska_rak")
    for w in np.unique(np.round(share, 2)):
        if w > 0:
            mask.add(np.nonzero(np.round(share, 2) == w)[0].tolist(), float(w), "REPLACE")
    transfer(fists, "maska_rak")
    raw.vertex_groups.remove(raw.vertex_groups["maska_rak"])
    bpy.ops.object.select_all(action="DESELECT")
    raw.select_set(True)
    bpy.context.view_layer.objects.active = raw
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
    bpy.ops.object.vertex_group_smooth(group_select_mode="ALL", factor=0.5, repeat=6)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    counts = {}
    for v in raw.data.vertices:
        for e in v.groups:
            if e.weight > 0.3:
                counts[e.group] = counts.get(e.group, 0) + 1
    names = {g.index: g.name for g in raw.vertex_groups}
    print("MEESHYHANDS weighted verts per bone:", sorted(((names[k], c) for k, c in counts.items()), key=lambda kc: -kc[1]))
    world_raw = raw.matrix_world.copy()
    raw.parent = arm
    raw.matrix_world = world_raw
    mod = raw.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    for o in angler:
        if o.type == "MESH":
            o.hide_render = True
            o.hide_set(True)
    arm.data.pose_position = "POSE"
    bpy.context.view_layer.update()

    # the fists' holes, on the raw mesh in the modelled pose
    tree = BVHTree.FromObject(raw, bpy.context.evaluated_depsgraph_get(), deform=False)
    to_local, to_world = raw.matrix_world.inverted(), raw.matrix_world

    def dist(p):
        hit = tree.find_nearest(to_local @ p)
        return (to_world @ hit[0] - p).length if hit[0] is not None else 1.0

    def ray_hits(p, d, reach):
        hit = tree.ray_cast(to_local @ p, (to_local.to_3x3() @ d).normalized(), reach / s)
        return hit[0] is not None

    def bone_world(name, tail=False):
        b = arm.data.bones[name]
        return arm.matrix_world @ (b.tail_local if tail else b.head_local)

    def hand_centre(side):
        hand = {raw.vertex_groups[nm].index for nm in raw.vertex_groups.keys() if nm.endswith("_" + side) and
                any(nm.startswith(f) for f in ("hand", "index", "middle", "ring", "pinky", "thumb"))}
        wrist = bone_world(f"hand_{side}")
        pts = []
        for v in list(raw.data.vertices)[::7]:
            if sum(e.weight for e in v.groups if e.group in hand) > 0.9:
                p = to_world @ v.co
                if (p - wrist).length > 0.05:
                    pts.append(p)
        return sum(pts, Vector()) / len(pts)

    dirs = [d for d in fibonacci(400) if d.z >= -0.05]

    def hole(centre, radius, span):
        best = (-1.0, None, None)
        steps = [i * 0.01 for i in range(-3, 4)]
        for ox in steps:
            for oy in steps:
                for oz in steps:
                    p = centre + Vector((ox, oy, oz))
                    if dist(p) < radius * 0.6:
                        continue
                    for d in dirs:
                        clear = min(dist(p + d * tt) for tt in (-span, -span / 2, 0.0, span / 2, span))
                        if clear <= best[0]:
                            continue
                        u = d.orthogonal().normalized()
                        v = d.cross(u)
                        around = sum(ray_hits(p, u * math.cos(a) + v * math.sin(a), 0.05)
                                     for a in (k * math.pi / 4 for k in range(8)))
                        if around >= 7:
                            best = (clear, p.copy(), d.copy())
        return best

    holes = {}
    for side, radius, span in (("r", HANDLE_RADIUS, 0.03), ("l", KNOB_RADIUS, 0.012)):
        clear, at, axis = hole(hand_centre(side), radius, span)
        index_dir = bone_world(f"index_01_{side}") - bone_world(f"pinky_01_{side}")
        if axis.dot(index_dir) < 0:
            axis = -axis                     # pinky side -> index side
        holes[side] = (at, axis)
        print(f"MEESHYHANDS hole {side}: room {clear * 1000:.1f} mm at {tuple(round(v, 3) for v in at)} along {tuple(round(v, 3) for v in axis)}")

    # the rod and the reel in their rig frame
    rod = {o.name.split(".")[0]: o for o in load(os.path.join(ROOT, "assets", "models", rod_id + ".glb")) if o.type == "MESH"}
    reel = {o.name.split(".")[0]: o for o in load(os.path.join(ROOT, "assets", "models", reel_id + ".glb")) if o.type == "MESH"}
    frames = json.load(open(os.path.join(ROOT, "assets", "models", reel_id + ".json"), encoding="utf-8"))
    rig = bpy.data.objects.new("RodRig", None)
    scene.collection.objects.link(rig)
    for name, o in rod.items():
        o.parent = rig
        if name.startswith("Seg"):
            o.location = Vector((0, 0, 0.25 + 0.3 * int(name[3:])))
    for o in reel.values():
        o.parent = rig
    reel["Rotor"].location = g2b(frames["rotor"])
    crank_pivot = bpy.data.objects.new("CrankPivot", None)
    scene.collection.objects.link(crank_pivot)
    crank_pivot.parent = rig
    crank_pivot.location = g2b(frames["hub"])
    reel["Crank"].parent = crank_pivot
    if "Bail" in reel:
        reel["Bail"].parent = reel["Rotor"]                 # the wire turns with the rotor (RodRig: ModelBail under ModelRotor)
        reel["Bail"].location = g2b(frames["bail_pivot"])
    knob_local = g2b((frames["knob_out"], 0.0, -frames["arm"]))   # RodRig.KnobAt at angle 0: hub + (KnobOut, 0, -arm)

    # the rod's frame: blank along ROD_DIR, the reel's side straight down across it, the seat in the right hole
    r_at, r_axis = holes["r"]
    axis = ROD_DIR
    down = (Vector((0, 0, -1)) - axis * (-axis.z)).normalized()
    x = down.cross(axis)
    rot = Matrix((x, down, axis)).transposed()
    origin = r_at - rot @ Vector((0, 0, SEAT_CENTRE))
    rig.matrix_world = Matrix.Translation(origin) @ rot.to_4x4()

    def turn_onto(a, b):
        return a.rotation_difference(b).to_matrix()

    def hand_target(side, centre_to, axis_to, cost_of):
        """The hand's world matrix that brings its hole to centre_to along axis_to, turned about that line to the least
        cost_of(matrix)."""
        h = (arm.matrix_world @ arm.data.bones[f"hand_{side}"].matrix_local).normalized()
        at, ax = holes[side]
        base = turn_onto(ax, axis_to)
        local = h.inverted() @ at
        best = None
        for deg in range(0, 360, 5):
            rr = Matrix.Rotation(math.radians(deg), 3, axis_to) @ base
            new = (rr @ h.to_3x3()).to_4x4()
            new.translation = centre_to - new.to_3x3() @ local
            cost = cost_of(new)
            if best is None or cost < best[0]:
                best = (cost, new, deg)
        return best[1], best[2], best[0]

    def wrist_bend(side, m):
        """How far the hand bends off the forearm (rad): the forearm from an elbow guessed below the shoulder (0.9 upper
        arm lengths down, as FirstPersonBody guesses it) to the wrist; the hand along its bone (Y)."""
        shoulder = bone_world(f"upperarm_{side}")
        upper = (bone_world(f"lowerarm_{side}") - shoulder).length
        elbow = shoulder + Vector((0.0, 0.0, -0.9 * upper))
        fore = (m.translation - elbow).normalized()
        return fore.angle(m.col[1].to_3d().normalized())

    # right: the thumb on top (its tip straight above the rod, across it), the wrist as straight as that allows
    thumb_local = (arm.matrix_world @ arm.data.bones["hand_r"].matrix_local).normalized().inverted() @ bone_world("thumb_03_r", tail=True)

    def right_cost(m):
        v = m.to_3x3() @ (thumb_local - (m.inverted() @ m.translation))
        v = (m @ thumb_local) - r_at
        v -= axis * v.dot(axis)
        return -v.normalized().dot(-down) + 0.6 * wrist_bend("r", m) / math.pi

    right_m, right_roll, _ = hand_target("r", r_at, axis, right_cost)

    # left: the knob in the fist, the index finger towards the reel; the crank's angle and the roll that keep the wrist
    # straightest within reach
    l_at, _ = holes["l"]
    shoulder_l = bone_world("upperarm_l")
    reach = ((bone_world("lowerarm_l") - shoulder_l).length + (bone_world("hand_l") - bone_world("lowerarm_l")).length) * 0.97
    hub = origin + rot @ g2b(frames["hub"])
    inward = rot @ Vector((-1, 0, 0)) if knob_local.x > 0 else rot @ Vector((1, 0, 0))
    best = None
    for ang in range(0, 360, 10):
        k = hub + rot @ (Matrix.Rotation(math.radians(ang), 3, "X") @ knob_local)
        for way in (inward, -inward):        # the index finger towards the reel, or the thumb
            m_left, roll, bend = hand_target("l", k, way, lambda m: wrist_bend("l", m))
            over = max(0.0, (m_left.translation - shoulder_l).length - reach)
            cost = bend + over * 20 + (k - l_at).length * 0.5
            if best is None or cost < best[0]:
                best = (cost, ang, m_left, roll, k, bend)
    _, ang, left_m, left_roll, knob, left_bend = best
    crank_pivot.rotation_euler = (math.radians(ang), 0, 0)
    print(f"MEESHYHANDS right hand rolled {right_roll} deg, wrist bent {math.degrees(wrist_bend('r', right_m)):.0f} deg; "
          f"crank at {ang} deg, knob {(knob - l_at).length * 1000:.0f} mm from the modelled left grip, left roll {left_roll}, "
          f"wrist bent {math.degrees(left_bend):.0f} deg")

    # IK: each hand's wrist to its target, the hand turned as the target. The targets hang on the tackle: the right hand's
    # on the rod ("Wedka"), the left hand's on the crank ("Korba"), so moving the rod moves both hands and turning the
    # crank turns the left hand with it
    rig.name = "Wedka"
    rig.empty_display_type, rig.empty_display_size = "ARROWS", 0.12
    crank_pivot.name = "Korba"
    crank_pivot.empty_display_type, crank_pivot.empty_display_size = "CIRCLE", 0.05
    crank_pivot.rotation_mode = "XYZ"
    crank_pivot.lock_location = (True, True, True)
    crank_pivot.lock_rotation = (False, True, True)       # only about the crank's own axis

    def empty(name, mat, shape, size, parent=None):
        e = bpy.data.objects.new(name, None)
        e.empty_display_type, e.empty_display_size = shape, size
        e.show_in_front = True
        scene.collection.objects.link(e)
        e.matrix_world = mat
        if parent is not None:
            bpy.context.view_layer.update()
            e.parent = parent
            e.matrix_parent_inverse = parent.matrix_world.inverted()
        return e

    bpy.context.view_layer.objects.active = arm
    labels = {"r": "P", "l": "L"}
    for side, mat, holder in (("r", right_m, rig), ("l", left_m, crank_pivot)):
        pb = arm.pose.bones
        elbow = bone_world(f"lowerarm_{side}")
        shoulder = bone_world(f"upperarm_{side}")
        target = empty(f"Cel_dlon_{labels[side]}", mat, "ARROWS", 0.06, holder)
        line = mat.translation - shoulder
        out = (elbow - shoulder) - line * ((elbow - shoulder).dot(line) / line.length_squared)
        out = (out.normalized() if out.length > 1e-4 else Vector((-1 if side == "r" else 1, 0, -1)).normalized())
        pole = empty(f"Cel_lokiec_{labels[side]}", Matrix.Translation(elbow + (out + Vector((0, 0, -0.6))).normalized() * 0.3),
                     "SPHERE", 0.03)
        ik = pb[f"hand_{side}"].constraints.new("IK")
        ik.target, ik.pole_target, ik.chain_count, ik.use_tail = target, pole, 3, False
        cr = pb[f"hand_{side}"].constraints.new("COPY_ROTATION")
        cr.target, cr.target_space, cr.owner_space = target, "WORLD", "WORLD"
        best = None
        for deg in range(-180, 180, 10):
            ik.pole_angle = math.radians(deg)
            bpy.context.view_layer.update()
            ev = arm.evaluated_get(bpy.context.evaluated_depsgraph_get())
            e = ev.matrix_world @ ev.pose.bones[f"lowerarm_{side}"].head
            score = (e - (elbow + out * 0.02)).length
            if best is None or score < best[0]:
                best = (score, deg)
        ik.pole_angle = math.radians(best[1])
        bpy.context.view_layer.update()
        ev = arm.evaluated_get(bpy.context.evaluated_depsgraph_get())
        wrist = ev.matrix_world @ ev.pose.bones[f"hand_{side}"].head
        print(f"MEESHYHANDS IK {side}: wrist {((wrist - mat.translation).length * 1000):.1f} mm off its target, pole {best[1]} deg")

    # what can be picked in the viewport: the controls only (the figure, rod and reel stay put)
    raw.name = "Wedkarz"
    raw.hide_select = True
    for o in list(rod.values()) + list(reel.values()):
        o.hide_select = True
    arm.hide_select = True
    arm.hide_set(True)
    for o in angler:                                   # the game's own (hidden) meshes of the angler
        o.hide_select = True

    # light, sky, cameras (one per view, the eyes' view active)
    sun = bpy.data.objects.new("Slonce", bpy.data.lights.new("Slonce", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(45), 0, math.radians(25))
    scene.collection.objects.link(sun)
    sun.hide_select = True
    world = bpy.data.worlds.new("Niebo")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.65, 0.72, 1.0)
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1200
    mid = (r_at + knob) / 2
    head = bone_world("Head" if "Head" in arm.data.bones else "head")
    eye = head + Vector((0.0, -0.24, 0.07))
    views = {
        "Widok_z_oczu": ("eyes", eye, mid + axis * 0.15, 22),
        "Z_przodu": ("front", mid + Vector((0.05, -0.5, 0.15)), mid, 50),
        "Z_prawej": ("right", mid + Vector((-0.45, -0.15, 0.1)), mid, 50),
        "Z_lewej": ("left", mid + Vector((0.45, -0.2, 0.05)), mid, 50),
        "Z_gory": ("above", mid + Vector((0.12, 0.2, 0.42)), mid, 50),
        "Z_dolu": ("below", mid + Vector((0.05, -0.25, -0.4)), mid, 50),
    }
    cams = {}
    for name, (short, at, look, lens) in views.items():
        cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        cam.data.clip_start, cam.data.lens = 0.005, lens
        scene.collection.objects.link(cam)
        cam.location = at
        cam.rotation_euler = (look - at).to_track_quat("-Z", "Y").to_euler()
        cams[name] = cam
        if "--no-render" not in argv:
            scene.camera = cam
            scene.render.filepath = os.path.join(out_dir, f"hands_{short}.png")
            bpy.ops.render.render(write_still=True)
    scene.camera = cams["Widok_z_oczu"]

    text = bpy.data.texts.new("INSTRUKCJA")
    text.write(INSTRUCTIONS)
    for screen in bpy.data.screens:
        for area in screen.areas:
            for space in area.spaces:
                if space.type == "VIEW_3D":
                    space.shading.type = "MATERIAL"
                    space.overlay.show_relationship_lines = False
    bpy.ops.object.select_all(action="DESELECT")
    for name in ("Cel_dlon_P", "Cel_dlon_L"):
        bpy.data.objects[name].select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects["Cel_dlon_L"]
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "wedka_w_dloniach.blend"))
    print("MEESHYHANDS saved wedka_w_dloniach.blend (and the renders) ->", out_dir)


INSTRUCTIONS = """Ustawianie chwytu (wedka_w_dloniach.blend)

Klikalne sa tylko uchwyty (reszta jest zablokowana, zeby nic sie nie przesunelo):
  Cel_dlon_P   - prawa dlon (strzalki). G przesuwa, R obraca; reka idzie za nim (IK), dlon przyjmuje jego obrot.
                 Siedzi na wedce: przesuniecie "Wedka" przenosi tez dlon.
  Cel_dlon_L   - lewa dlon. Siedzi na korbie: obrot "Korba" obraca dlon razem z galka.
  Cel_lokiec_P / Cel_lokiec_L - kule: w ktora strone ida lokcie.
  Wedka        - cala wedka z kolowrotkiem (i prawa dlon).
  Korba        - obrot korby tylko wokol jej osi (R X albo pole Rotation X w panelu N).
Palce zostaja takie, jak je wymodelowal Meshy (zacisniete piesci), dlatego tylko dlonie sie obraca i przesuwa.

Kamery: Widok_z_oczu (jak w grze), Z_przodu, Z_prawej, Z_lewej, Z_gory, Z_dolu.
Widok przez kamere: zaznacz ja w Outlinerze, Ctrl+Numpad0. Render: F12.
Model ma ~1 mln trojkatow: przy przesuwaniu widok moze chwile przycinac.
Gdy bedzie dobrze, zapisz plik (Ctrl+S) i daj znac: odczytam z niego ulozenie dloni wzgledem wedki i korby do gry.
"""


main()
