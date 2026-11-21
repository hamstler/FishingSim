"""Renders a glb (e.g. a raw Meshy model) in Blender: the whole figure from the front and the side, and close-ups of given
points (world metres, Blender Z up), for judging a model before it goes into the game.

    blender -b --factory-startup --python tools/assets/render_model.py -- MODEL.glb OUT_DIR [x,y,z[,dist] ...]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
path, out_dir = os.path.abspath(argv[0]), os.path.abspath(argv[1])
points = [tuple(float(v) for v in a.split(",")) for a in argv[2:]]
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=path)
scene = bpy.context.scene
meshes = [o for o in scene.objects if o.type == "MESH"]
lo = Vector((1e9, 1e9, 1e9))
hi = -lo
for o in meshes:
    for c in o.bound_box:
        p = o.matrix_world @ Vector(c)
        lo = Vector(map(min, lo, p))
        hi = Vector(map(max, hi, p))
centre, size = (lo + hi) / 2, hi - lo
print("RENDERMODEL bounds", tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi))

sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.0
sun.rotation_euler = (math.radians(45), 0, math.radians(30))
scene.collection.objects.link(sun)
world = bpy.data.worlds.new("World")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.65, 0.7, 1.0)
scene.world = world
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 1280, 1280
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
cam.data.clip_start = 0.005
scene.collection.objects.link(cam)
scene.camera = cam


def shoot(name, target, offset, lens=50):
    cam.data.lens = lens
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERMODEL", name)


reach = max(size) * 1.6
shoot("front", centre, Vector((0, -reach, 0)))
shoot("side", centre, Vector((reach, 0, 0)))
shoot("front_left", centre, Vector((-reach * 0.7, -reach * 0.7, 0)))
for i, p in enumerate(points):
    d = p[3] if len(p) > 3 else 0.35
    t = Vector(p[:3])
    shoot(f"close_{i}_front", t, Vector((0, -d, 0.05)), 60)
    shoot(f"close_{i}_side", t, Vector((d if t.x > centre.x else -d, -d * 0.3, 0.05)), 60)
