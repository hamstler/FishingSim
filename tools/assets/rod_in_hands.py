"""Puts the rod and the reel into the hands of the raw Meshy fisherman (assets on the user's Drive: the ~950k triangle
figure whose fists are modelled holding a rod) and renders the grips, in Blender.

The right fist's hole is found on the mesh: the line through the fist along which the fingers stay furthest away while
the hand still closes round it on every side. The rod's handle goes along it (the reel seat in the fist); the rod's turn
about that line, its slide along it and the crank's angle are chosen to bring the crank's knob into the left hand's grip
(the same search on the left hand, a smaller hole). Rod and reel come from the game's own glb files, laid out as
RodRig lays them (meshy_rod_split.py: rig frame, blank along +Y, reel under the seat, the moving parts in their pivots'
frames with assets/models/reels/<id>.json).

    blender -b --factory-startup --python tools/assets/rod_in_hands.py -- RAW.glb OUT_DIR [rod_id] [reel_id]
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SEAT_CENTRE = 0.1          # RodRig: the reel seat's middle along the blank (m)
HANDLE_RADIUS = 0.0143     # the Meshy rod's handle at the grip
KNOB_RADIUS = 0.009


def g2b(v):
    """A glTF point (Y up) in Blender's frame (Z up): (x, -z, y)."""
    return Vector((v[0], -v[2], v[1]))


def fibonacci(n):
    pts = []
    for i in range(n):
        y = 1 - 2 * (i + 0.5) / n
        r = math.sqrt(1 - y * y)
        a = i * math.pi * (3 - math.sqrt(5))
        pts.append(Vector((math.cos(a) * r, y, math.sin(a) * r)))
    return pts


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    raw, out_dir = os.path.abspath(argv[0]), os.path.abspath(argv[1])
    rod_id = argv[2] if len(argv) > 2 else "rods/float_ostoja"
    reel_id = argv[3] if len(argv) > 3 else "reels/small_hokuto"
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    bpy.ops.import_scene.gltf(filepath=raw)
    figure = next(o for o in scene.objects if o.type == "MESH")
    deps = bpy.context.evaluated_depsgraph_get()
    tree = BVHTree.FromObject(figure, deps)
    to_local = figure.matrix_world.inverted()
    to_world = figure.matrix_world

    def dist(p):
        hit = tree.find_nearest(to_local @ p)
        return (to_world @ hit[0] - p).length if hit[0] is not None else 1.0

    def ray_hits(p, d, reach):
        o, dl = to_local @ p, (to_local.to_3x3() @ d).normalized()
        hit = tree.ray_cast(o, dl, reach)
        return hit[0] is not None

    verts = [to_world @ v.co for v in figure.data.vertices]
    lo_y = min(v.y for v in verts)

    def cluster(x0, x1):
        pts = [v for v in verts if x0 < v.x < x1 and 0.0 < v.z < 0.35 and v.y < lo_y + 0.14]
        return sum(pts, Vector()) / max(len(pts), 1), len(pts)

    right_c, nr = cluster(-0.25, 0.02)
    left_c, nl = cluster(0.04, 0.3)
    print(f"RODHANDS right fist {nr} verts at {tuple(round(v, 3) for v in right_c)}, left hand {nl} at {tuple(round(v, 3) for v in left_c)}")

    dirs = [d for d in fibonacci(500) if d.z >= -0.05]   # a line has two ends: the half that does not point down

    def hole(centre, radius, span, grid, near=None, reach=0.0):
        """The line through a closed hand with the most room round it, enclosed on (nearly) every side; with `near`,
        only lines passing within `reach` of that point (the crank's knob can be at most ~13 cm off the rod's axis)."""
        best = (-1.0, None, None)
        steps = [i * 0.01 for i in range(-grid, grid + 1)]
        for ox in steps:
            for oy in steps:
                for oz in steps:
                    p = centre + Vector((ox, oy, oz))
                    if dist(p) < radius * 0.6:
                        continue
                    for d in dirs:
                        if near is not None:
                            to = near - p
                            if (to - d * to.dot(d)).length > reach:
                                continue
                        clear = min(dist(p + d * t) for t in (-span, -span / 2, 0.0, span / 2, span))
                        if clear <= best[0]:
                            continue
                        u = d.orthogonal().normalized()
                        v = d.cross(u)
                        around = sum(ray_hits(p, u * math.cos(a) + v * math.sin(a), 0.05)
                                     for a in (k * math.pi / 4 for k in range(8)))
                        if around >= 7:
                            best = (clear, p.copy(), d.copy())
        return best

    l_clear, l_at, l_axis = hole(left_c, KNOB_RADIUS, 0.012, 3)
    r_clear, r_at, r_axis = hole(right_c, HANDLE_RADIUS, 0.03, 3, l_at, 0.13)
    print(f"RODHANDS right hole: room {r_clear * 1000:.1f} mm at {tuple(round(v, 3) for v in r_at)} along {tuple(round(v, 3) for v in r_axis)}")
    print(f"RODHANDS left grip: room {l_clear * 1000:.1f} mm at {tuple(round(v, 3) for v in l_at)} along {tuple(round(v, 3) for v in l_axis)}")

    # the rod and the reel in their rig frame (Blender: blank +Z, the reel's side +Y)
    def load(path):
        before = set(scene.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        return {o.name.split(".")[0]: o for o in scene.objects if o not in before and o.type == "MESH"}

    rod = load(os.path.join(ROOT, "assets", "models", rod_id + ".glb"))
    reel = load(os.path.join(ROOT, "assets", "models", reel_id + ".glb"))
    frames = json.load(open(os.path.join(ROOT, "assets", "models", reel_id + ".json"), encoding="utf-8"))
    rig = bpy.data.objects.new("RodRig", None)
    scene.collection.objects.link(rig)
    section = 0.3
    for name, o in rod.items():
        o.parent = rig
        if name.startswith("Seg"):
            o.location = Vector((0, 0, 0.25 + section * int(name[3:])))   # each section on its joint (BlankStart 0.25)
    for name, o in reel.items():
        o.parent = rig
    reel["Rotor"].location = g2b(frames["rotor"])
    crank_pivot = bpy.data.objects.new("CrankPivot", None)
    scene.collection.objects.link(crank_pivot)
    crank_pivot.parent = rig
    crank_pivot.location = g2b(frames["hub"])
    reel["Crank"].parent = crank_pivot
    if "Bail" in reel:
        reel["Bail"].location = g2b(frames["bail_pivot"])
    knob_local = g2b((-frames["knob_out"], -frames["arm"], 0.0))   # in the crank's frame at angle 0 (knob down)

    # the rod's place: the seat in the right fist, the blank along the hole (towards the front and up), turned about
    # it, slid along it, the crank at the angle that puts the knob in the left hand
    # the tip goes from the right fist past the left hand (the reel sits ahead of the fist, its crank by the left hand)
    axis = r_axis if r_axis.dot(l_at - r_at) > 0 else -r_axis
    u0 = axis.orthogonal().normalized()
    best = None
    for roll in range(0, 360, 5):
        a = math.radians(roll)
        down = (u0 * math.cos(a) + axis.cross(u0) * math.sin(a)).normalized()   # the reel's side
        if down.z > -0.3:
            continue    # the reel hangs under the rod
        x = down.cross(axis)
        rot = Matrix((x, down, axis)).transposed()
        for slide in [i * 0.005 for i in range(-10, 11)]:
            origin = r_at - rot @ Vector((0, 0, SEAT_CENTRE + slide))
            hub = origin + rot @ g2b(frames["hub"])
            for ang in range(0, 360, 10):
                k = hub + rot @ (Matrix.Rotation(math.radians(ang), 3, "X") @ knob_local)
                d = (k - l_at).length
                if best is None or d < best[0]:
                    best = (d, roll, slide, ang, rot.copy(), origin.copy())
    d, roll, slide, ang, rot, origin = best
    rig.matrix_world = Matrix.Translation(origin) @ rot.to_4x4()
    crank_pivot.rotation_euler = (math.radians(ang), 0, 0)
    bpy.context.view_layer.update()
    print(f"RODHANDS rod turned {roll} deg, slid {slide * 1000:.0f} mm, crank at {ang} deg: knob {d * 1000:.0f} mm from the left grip")

    # light, sky, cameras
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(45), 0, math.radians(25))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.65, 0.72, 1.0)
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1200
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.clip_start = 0.005
    scene.collection.objects.link(cam)
    scene.camera = cam
    mid = (r_at + l_at) / 2
    top = max(v.z for v in verts)
    eye = Vector((0.0, lo_y + 0.2, top - 0.12))
    views = {
        "eyes": (eye, mid, 28),
        "front": (mid + Vector((0.0, -0.55, 0.12)), mid, 50),
        "right": (mid + Vector((-0.5, -0.2, 0.08)), mid, 50),
        "left": (mid + Vector((0.5, -0.2, 0.08)), mid, 50),
        "above": (mid + Vector((0.0, -0.1, 0.5)), mid, 50),
        "below": (mid + Vector((0.0, -0.25, -0.4)), mid, 50),
    }
    for name, (at, look, lens) in views.items():
        cam.data.lens = lens
        cam.location = at
        cam.rotation_euler = (look - at).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"hands_{name}.png")
        bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "wedka_w_dloniach.blend"))
    print("RODHANDS rendered", ", ".join(views), "and saved wedka_w_dloniach.blend into", out_dir)


main()
