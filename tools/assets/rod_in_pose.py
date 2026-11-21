"""Puts the rod and the reel into a static (unrigged) Meshy figure whose hands are modelled holding a rod, as the figure
holds them: the line through each fist with the most room round it is found on the mesh; the rod goes through both fists
(the blank's axis fitted to both holes), the reel seat in the upper fist, the reel hanging under the rod, the tip beyond
the upper fist. Renders the hands from several sides and saves a .blend (the rod is one "Wedka" empty to move by hand).

    blender -b --factory-startup --python tools/assets/rod_in_pose.py -- FIGURE.glb OUT_DIR [rod_id] [reel_id] [--no-render]
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SEAT_CENTRE = 0.1
HANDLE_RADIUS = 0.0143


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


def rig_arms(figure, verts, right_c, left_c, left_hole, rig, crank, frames):
    """Gives the static figure a skeleton for both arms (shoulder, elbow, wrist, hand, found on the mesh) with smooth
    weights, so a hand can be moved and turned from the shoulder (IK) while its modelled fingers stay as they are. The
    right hand's target stays where the hand is, on the rod; the left fist goes onto the crank's knob (the knob in the
    fist's hole, the crank turned to where the arm reaches it with the straightest wrist), its target on the crank."""
    import numpy as np
    scene = bpy.context.scene
    n = len(figure.data.vertices)
    co = np.empty(n * 3)
    figure.data.vertices.foreach_get("co", co)
    mw = np.array(figure.matrix_world)
    P = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    lo_y = P[:, 1].min()

    def joints(c, sgn):
        side = P[(sgn * P[:, 0] > sgn * c.x + 0.13) & (P[:, 2] > c.z + 0.03) & (P[:, 2] < c.z + 0.13)]
        e = Vector(side.mean(0))
        up = P[(sgn * P[:, 0] > 0.19) & (P[:, 2] > e.z + 0.29) & (P[:, 2] < e.z + 0.35)]
        s = Vector(up.mean(0)) - Vector((sgn * 0.03, 0, 0))
        w = c + (e - c).normalized() * 0.075
        return s, e, w, c + (c - w)

    arms = {"P": joints(right_c, -1), "L": joints(left_c, 1)}
    for k, (s, e, w, t) in arms.items():
        print(f"RODPOSE arm {k}: shoulder {tuple(round(v, 3) for v in s)} elbow {tuple(round(v, 3) for v in e)} "
              f"wrist {tuple(round(v, 3) for v in w)}")

    data = bpy.data.armatures.new("Szkielet")
    arm = bpy.data.objects.new("Szkielet", data)
    scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    root = data.edit_bones.new("korpus")
    root.head, root.tail = Vector((0, 0, -0.2)), Vector((0, 0, 0.2))
    for k, (s, e, w, t) in arms.items():
        u = data.edit_bones.new(f"ramie_{k}")
        u.head, u.tail, u.parent = s, e, root
        f = data.edit_bones.new(f"przedramie_{k}")
        f.head, f.tail, f.parent, f.use_connect = e, w, u, True
        h = data.edit_bones.new(f"dlon_{k}")
        h.head, h.tail, h.parent, h.use_connect = w, t, f, True
    bpy.ops.object.mode_set(mode="OBJECT")

    def seg(a, b):
        a, b = np.array(a), np.array(b)
        ab = b - a
        tt = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1)
        return np.linalg.norm(P - (a + tt[:, None] * ab), axis=1), tt

    groups = {}
    total = np.zeros(n)
    for k, (s, e, w, t) in arms.items():
        du, tu = seg(s, e)
        df, _ = seg(e, w)
        dh, _ = seg(w, t)
        dmin = np.minimum(np.minimum(du, df), dh)
        reach = np.where(dh <= np.minimum(du, df), 0.07, np.where(df <= du, 0.065, 0.075))
        sgn = 1 if k == "L" else -1
        outside = (sgn * P[:, 0] > sgn * s.x - 0.05).astype(float)
        on_upper = du <= np.minimum(df, dh)
        inside = np.clip((reach - dmin) / 0.02, 0, 1) * np.where(on_upper, outside, 1.0)
        inside *= np.where(on_upper, np.clip((tu - 0.05) / 0.2, 0, 1), 1.0)   # the shoulder blends into the body
        # the whole fist and wrist (the frontmost band of the figure, where only the hands are) belong to the arm
        band = (P[:, 1] < lo_y + 0.12) & (P[:, 2] > -0.05) & (P[:, 2] < 0.4) & (sgn * P[:, 0] >= 0)
        inside = np.maximum(inside, (band & (np.minimum(dh, df) < 0.1)).astype(float))
        wu, wf, wh = (np.exp(-(d - dmin) / 0.008) for d in (du, df, dh))
        norm = wu + wf + wh
        for name, wb in ((f"ramie_{k}", wu), (f"przedramie_{k}", wf), (f"dlon_{k}", wh)):
            groups[name] = inside * wb / norm
        total += inside
    groups["korpus"] = np.clip(1 - total, 0, 1)
    for name, wv in groups.items():
        g = figure.vertex_groups.new(name=name)
        q = np.round(wv, 2)
        for val in np.unique(q):
            if val > 0:
                g.add(np.nonzero(q == val)[0].tolist(), float(val), "REPLACE")
    bpy.ops.object.select_all(action="DESELECT")
    figure.hide_select = False
    figure.select_set(True)
    bpy.context.view_layer.objects.active = figure
    bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
    bpy.ops.object.vertex_group_smooth(group_select_mode="ALL", factor=0.5, repeat=8)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    figure.hide_select = True
    mod = figure.modifiers.new("Szkielet", "ARMATURE")
    mod.object = arm
    print("RODPOSE arms weighted:", {k: int((v > 0.3).sum()) for k, v in groups.items()})

    # the left fist on the knob
    l_at, l_axis = left_hole[1], left_hole[2]
    s, e, w, t = arms["L"]
    upper_len, fore_len = (e - s).length, (w - e).length
    hub = rig.matrix_world @ g2b(frames["hub"])
    rot = rig.matrix_world.to_3x3().normalized()
    knob_local = g2b((frames["knob_out"], 0.0, -frames["arm"]))
    knob_axis = rot @ Vector((1, 0, 0))
    elbow_guess = s + Vector((0, 0, -0.9 * upper_len))
    best = None
    for ang in range(0, 360, 15):
        k = hub + rot @ (Matrix.Rotation(math.radians(ang), 3, "X") @ knob_local)
        for way in (knob_axis, -knob_axis):
            base = l_axis.rotation_difference(way).to_matrix()
            for deg in range(0, 360, 10):
                R = Matrix.Rotation(math.radians(deg), 3, way) @ base
                w_new = k + R @ (w - l_at)
                over = max(0.0, (w_new - s).length - (upper_len + fore_len) * 0.97)
                bend = (w_new - elbow_guess).normalized().angle((R @ (t - w)).normalized())
                cost = bend + over * 30
                if best is None or cost < best[0]:
                    best = (cost, ang, R, w_new, k, bend, over)
    _, ang, R, w_new, knob, bend, over = best
    crank.rotation_euler = (math.radians(ang), 0, 0)
    reach_note = "within reach" if over == 0 else f"{over * 1000:.0f} mm beyond reach"
    print(f"RODPOSE left fist onto the knob: crank at {ang} deg, wrist bent {math.degrees(bend):.0f} deg, {reach_note}")

    def control(name, mat, parent, shape="ARROWS", size=0.06):
        o = bpy.data.objects.new(name, None)
        o.empty_display_type, o.empty_display_size, o.show_in_front = shape, size, True
        scene.collection.objects.link(o)
        o.matrix_world = mat
        bpy.context.view_layer.update()
        if parent is not None:
            o.parent = parent
            o.matrix_parent_inverse = parent.matrix_world.inverted()
        return o

    korpus = arm.pose.bones["korpus"]
    korpus.lock_ik_x = korpus.lock_ik_y = korpus.lock_ik_z = True   # the IK chain ends at the shoulder: the body stays
    bpy.context.view_layer.update()
    for k, holder in (("P", rig), ("L", crank)):
        bone = arm.data.bones[f"dlon_{k}"]
        rest = arm.matrix_world @ bone.matrix_local
        if k == "L":
            m = (R @ rest.to_3x3()).to_4x4()
            m.translation = w_new
        else:
            m = rest.copy()
        target = control(f"Cel_dlon_{k}", m, holder)
        s, e, w, t = arms[k]
        out = (e - s) - (w - s) * ((e - s).dot(w - s) / (w - s).length_squared)
        pole = control(f"Cel_lokiec_{k}", Matrix.Translation(e + out.normalized() * 0.3), None, "SPHERE", 0.03)
        pb = arm.pose.bones[f"dlon_{k}"]
        ik = pb.constraints.new("IK")
        ik.target, ik.pole_target, ik.chain_count, ik.use_tail = target, pole, 3, False
        cr = pb.constraints.new("COPY_ROTATION")
        cr.target, cr.target_space, cr.owner_space = target, "WORLD", "WORLD"
        want_elbow = e if k == "P" else e + (w_new - w) * 0.4
        best = None
        for deg in range(-180, 180, 5):
            ik.pole_angle = math.radians(deg)
            bpy.context.view_layer.update()
            ev = arm.evaluated_get(bpy.context.evaluated_depsgraph_get())
            el = ev.matrix_world @ ev.pose.bones[f"przedramie_{k}"].head
            score = (el - want_elbow).length
            if best is None or score < best[0]:
                best = (score, deg)
        ik.pole_angle = math.radians(best[1])
        bpy.context.view_layer.update()
        ev = arm.evaluated_get(bpy.context.evaluated_depsgraph_get())
        wr = ev.matrix_world @ ev.pose.bones[f"dlon_{k}"].head
        print(f"RODPOSE IK {k}: wrist {(wr - m.translation).length * 1000:.1f} mm off its target, elbow {best[0] * 1000:.0f} mm off")
    arm.hide_set(True)
    arm.hide_select = True
    return knob


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    fig_path, out_dir = os.path.abspath(argv[0]), os.path.abspath(argv[1])
    ids = [a for a in argv[2:] if not a.startswith("--")]
    rod_id = ids[0] if ids else "rods/float_ostoja"
    reel_id = ids[1] if len(ids) > 1 else "reels/small_hokuto"
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    figure = next(o for o in load(fig_path) if o.type == "MESH")
    figure.name = "Wedkarz"
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(figure.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)   # the UV seams' split vertices: no cracks when it bends
    bm.to_mesh(figure.data)
    bm.free()
    tree = BVHTree.FromObject(figure, bpy.context.evaluated_depsgraph_get())
    to_local, to_world = figure.matrix_world.inverted(), figure.matrix_world

    def dist(p):
        hit = tree.find_nearest(to_local @ p)
        return (to_world @ hit[0] - p).length if hit[0] is not None else 1.0

    def ray_hits(p, d, reach):
        return tree.ray_cast(to_local @ p, (to_local.to_3x3() @ d).normalized(), reach)[0] is not None

    verts = [to_world @ v.co for v in figure.data.vertices]
    lo_y = min(v.y for v in verts)
    top = max(v.z for v in verts)

    # the fists: the frontmost vertices in the hands' height band, split left / right of the middle
    band = [v for v in verts if -0.05 < v.z < 0.4 and v.y < lo_y + 0.12]
    right = [v for v in band if v.x < 0]
    left = [v for v in band if v.x >= 0]
    right_c = sum(right, Vector()) / len(right)
    left_c = sum(left, Vector()) / len(left)
    print(f"RODPOSE fists: right {len(right)} at {tuple(round(v, 3) for v in right_c)}, left {len(left)} at {tuple(round(v, 3) for v in left_c)}")

    dirs = [d for d in fibonacci(500) if d.z >= -0.05]

    def holes(centre, radius, span, keep=6):
        """The lines through a closed hand with the most room round it, enclosed on (nearly) every side (best first)."""
        found = []
        steps = [i * 0.01 for i in range(-3, 4)]
        for ox in steps:
            for oy in steps:
                for oz in steps:
                    p = centre + Vector((ox, oy, oz))
                    if dist(p) < radius * 0.6:
                        continue
                    for d in dirs:
                        clear = min(dist(p + d * t) for t in (-span, -span / 2, 0.0, span / 2, span))
                        if clear < radius * 0.6:
                            continue
                        u = d.orthogonal().normalized()
                        v = d.cross(u)
                        around = sum(ray_hits(p, u * math.cos(a) + v * math.sin(a), 0.05)
                                     for a in (k * math.pi / 4 for k in range(8)))
                        if around >= 7:
                            found.append((clear, p.copy(), d.copy()))
        found.sort(key=lambda f: -f[0])
        return found[:400]

    hr = holes(right_c, HANDLE_RADIUS, 0.03)
    hl = holes(left_c, HANDLE_RADIUS, 0.03)
    print(f"RODPOSE right hole {hr[0][0] * 1000:.1f} mm at {tuple(round(v, 3) for v in hr[0][1])} along {tuple(round(v, 3) for v in hr[0][2])}")
    print(f"RODPOSE left hole {hl[0][0] * 1000:.1f} mm at {tuple(round(v, 3) for v in hl[0][1])} along {tuple(round(v, 3) for v in hl[0][2])}")

    # the blank runs along the right forearm: its butt under the elbow, through the right fist, the tip aimed ahead at
    # eye height. The forearm's direction from the mesh (the principal axis of the arm's surface behind the fist); of
    # the lines through the right fist the one with room for the handle closest to that direction tilted up
    pr = hr[0][1]
    # the right elbow: the outside of the arm (beyond the torso) a little above the fist; the butt passes 5 cm under it
    side = [v for v in verts if v.x < right_c.x - 0.13 and right_c.z + 0.03 < v.z < right_c.z + 0.13]
    elbow = sum(side, Vector()) / len(side)
    butt_at = elbow - Vector((0.0, 0.0, 0.05))
    fore = (pr - butt_at).normalized()
    print(f"RODPOSE right elbow at {tuple(round(v, 3) for v in elbow)}")
    head = [v for v in verts if v.z > top - 0.3]
    eye_at = sum(head, Vector()) / len(head)
    aim_point = Vector((pr.x * 0.3, eye_at.y - 1.6, eye_at.z - 0.05))   # ahead of the eyes
    aim = (aim_point - pr).normalized()
    want = fore                                        # along the forearm (the user: under the elbow, along the arm)
    lift = math.radians(8)                             # the tip a little up, ahead of the eyes
    if want.z < math.sin(lift):
        flat = Vector((want.x, want.y, 0)).normalized()
        want = (flat * math.cos(lift) + Vector((0, 0, math.sin(lift)))).normalized()
    best = None
    for cr, p0, d in []:
        d = d if d.dot(want) > 0 else -d
        if math.degrees(d.angle(want)) > 12:
            continue
        if best is None or cr > best[1]:
            best = (0, cr, p0, d)
    if best is None:
        # no line through the fist that close: the forearm's own line through the point with the most room for it
        span = (-0.03, -0.015, 0.0, 0.015, 0.03)
        pts = [right_c + Vector((i * 0.005, j * 0.005, k * 0.005)) for i in range(-8, 9) for j in range(-8, 9) for k in range(-8, 9)]
        room = [(min(dist(q + want * t) for t in span), q) for q in pts]
        cr, p0 = max(room, key=lambda r: r[0])
        best = (0, cr, p0, want)
    _, cr, pr, axis = best
    upper = pr
    print(f"RODPOSE forearm {tuple(round(v, 3) for v in fore)}, aim {tuple(round(v, 3) for v in aim)}; blank {tuple(round(v, 3) for v in axis)} "
          f"({math.degrees(axis.angle(fore)):.0f} deg off the forearm, {math.degrees(math.asin(max(-1, min(1, axis.z)))):.0f} deg up), room {cr * 1000:.1f} mm")
    pl = hl[0][1]

    # the rod and the reel in their rig frame (blank +Z, the reel's side +Y)
    rod = {o.name.split(".")[0]: o for o in load(os.path.join(ROOT, "assets", "models", rod_id + ".glb")) if o.type == "MESH"}
    reel = {o.name.split(".")[0]: o for o in load(os.path.join(ROOT, "assets", "models", reel_id + ".glb")) if o.type == "MESH"}
    frames = json.load(open(os.path.join(ROOT, "assets", "models", reel_id + ".json"), encoding="utf-8"))
    rig = bpy.data.objects.new("Wedka", None)
    rig.empty_display_type, rig.empty_display_size = "ARROWS", 0.12
    rig.show_in_front = True
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
    for o in list(rod.values()) + list(reel.values()):
        o.hide_select = True
    figure.hide_select = True

    # the reel hangs straight down across the blank; slide the seat so that the stem leaves the upper fist between its
    # fingers (the fist's middle along the blank)
    down = (Vector((0, 0, -1)) - axis * (-axis.z)).normalized()
    x = down.cross(axis)
    rot = Matrix((x, down, axis)).transposed()
    fist = right
    # the fingers wrapped round the handle: the hand's surface within 3.5 cm of the blank, near the hole; the stem goes in
    # the middle of their span (between the middle and the ring finger)
    along = [(v - upper).dot(axis) for v in fist
             if ((v - upper) - axis * (v - upper).dot(axis)).length < 0.035 and abs((v - upper).dot(axis)) < 0.07]
    middle = upper + axis * ((min(along) + max(along)) / 2)
    along = [(min(along) + max(along)) / 2]
    # the reel's foot along the blank: the Body's vertices nearest the blank (the rig's +Y is the reel's side)
    body_pts = [v.co for v in reel["Body"].data.vertices]
    top_y = min(p.y for p in body_pts)
    foot = [p.z for p in body_pts if p.y < top_y + 0.012]
    stem = sum(foot) / len(foot)
    print(f"RODPOSE the reel's foot {stem * 1000:.0f} mm up the blank")
    origin = middle - rot @ Vector((0, 0, stem))
    print(f"RODPOSE the reel's stem at the fist's middle, {sum(along) / len(along) * 1000:.0f} mm from the hole found")
    rig.matrix_world = Matrix.Translation(origin) @ rot.to_4x4()
    bpy.context.view_layer.update()
    if "--no-arms" not in argv:
        rig_arms(figure, verts, right_c, left_c, hl[0], rig, crank, frames)

    # clay for an untextured figure
    if not figure.data.materials or not any(m and m.use_nodes and any(n.type == "TEX_IMAGE" for n in m.node_tree.nodes)
                                            for m in figure.data.materials):
        clay = bpy.data.materials.new("Glina")
        clay.use_nodes = True
        bsdf = clay.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (0.78, 0.62, 0.52, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.6
        figure.data.materials.clear()
        figure.data.materials.append(clay)

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
    mid = upper + axis * 0.05
    head = [v for v in verts if v.z > top - 0.3]
    head_c = sum(head, Vector()) / len(head)
    eye = Vector((head_c.x, min(v.y for v in head) + 0.05, head_c.z - 0.02))   # just behind the face, under the cap
    views = {
        "Widok_z_oczu": ("eyes", eye, mid, 24),
        "Z_przodu": ("front", mid + Vector((0.0, -0.6, 0.12)), mid, 50),
        "Z_prawej": ("right", mid + Vector((-0.5, -0.2, 0.1)), mid, 50),
        "Z_lewej": ("left", mid + Vector((0.5, -0.2, 0.1)), mid, 50),
        "Z_gory": ("above", mid + Vector((0.05, 0.1, 0.55)), mid, 50),
        "Z_dolu": ("below", mid + Vector((0.05, -0.3, -0.45)), mid, 50),
        "Calosc": ("whole", Vector((1.6, -2.6, 0.4)), Vector((0.2, -0.3, 0.2)), 50),
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
            scene.render.filepath = os.path.join(out_dir, f"pose_{short}.png")
            bpy.ops.render.render(write_still=True)
    scene.camera = cams["Z_prawej"]
    for screen in bpy.data.screens:
        for area in screen.areas:
            for space in area.spaces:
                if space.type == "VIEW_3D":
                    space.shading.type = "MATERIAL"
    text = bpy.data.texts.new("INSTRUKCJA")
    text.write(INSTRUCTIONS)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "wedka_w_pozie.blend"))
    print("RODPOSE saved wedka_w_pozie.blend ->", out_dir)


INSTRUCTIONS = """Ustawianie chwytu (wedka_w_pozie.blend)

Klikalne sa tylko uchwyty (sylwetka, wedka i kolowrotek sa zablokowane):
  Cel_dlon_P    - prawa dlon (strzalki). G przesuwa, R obraca; reka idzie za nia od barku (IK).
                  Siedzi na wedce: przesuniecie "Wedka" przenosi tez prawa dlon.
  Cel_dlon_L    - lewa dlon. Siedzi na korbie: obrot "Korba" (R X) obraca dlon razem z galka.
  Cel_lokiec_P / Cel_lokiec_L - kule: w ktora strone ida lokcie.
  Wedka         - cala wedka z kolowrotkiem.
  Korba         - obrot korby tylko wokol jej osi.
Palce zostaja takie, jak je wymodelowal Meshy: piesci sa w siatce jedna bryla, wiec
obraca sie i przesuwa cala dlon (od nadgarstka), nie pojedyncze palce.

Kamery: Z_prawej (aktywna), Widok_z_oczu, Z_przodu, Z_lewej, Z_gory, Z_dolu, Calosc.
Widok przez kamere: zaznacz ja w Outlinerze, Ctrl+Numpad0. Render: F12.
Gdy bedzie dobrze: Ctrl+S i daj znac, odczytam ulozenie dloni wzgledem wedki i korby.
"""


main()
