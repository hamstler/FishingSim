"""Takes the rod, the line, the float and the reel out of a static angler figure (one fused mesh, e.g. a generated
"realistic angler" with its tackle modelled in) and puts the game's rod and reel in its hands instead, each on a handle
to be set by hand ("Wedka", "Kolowrotek", "Korba", "Galka" as in add_tackle.py).

The figure's own rod is found on the mesh: its blank's axis is fitted to the thin part ahead of the hands; along that
axis everything ahead of the hands (blank, guides, line, float) goes, the handle inside the fists goes (only within its
own radius, so the fingers stay), and the reel goes by the box the caller measured (REEL box in the rod's frame). The new
rod lies on the old one's axis, its reel seat where the old reel was.

    blender -b --factory-startup --python tools/assets/swap_angler_tackle.py -- FIGURE.glb OUT.blend [--preview]
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:]
fig_path, out = os.path.abspath(argv[0]), os.path.abspath(argv[1])
PREVIEW = "--preview" in argv

# measured on realistic_angler_prec.glb (t along the blank towards the tip from the fitted point, r off the axis,
# "below" towards the reel's side): the hands start at t = -0.65, the butt ends near -0.95
HANDS_FRONT = -0.65
BUTT = -0.97
HANDLE_R = 0.0135
REEL = dict(t=(-0.86, -0.6), below=(0.035, 0.2), side=(-0.08, 0.08))
BUTT_R = 0.021          # behind the fists (no fingers there) the butt and its cap, a little thicker

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=fig_path)
fig = next(o for o in scene.objects if o.type == "MESH")
fig.name = "Wedkarz"
n = len(fig.data.vertices)
co = np.empty(n * 3)
fig.data.vertices.foreach_get("co", co)
mw = np.array(fig.matrix_world)
P = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]

# the blank's axis: the middle of the thin blank in slices ahead of the hands (the top of each slice minus its radius)
cs = []
for y0 in np.arange(P[:, 1].min() + 0.04, -0.16, 0.02):
    s = P[(P[:, 1] >= y0) & (P[:, 1] < y0 + 0.02)]
    if len(s) < 10:
        continue
    top = s[:, 2].max()
    blank = s[s[:, 2] > top - 0.012]
    cs.append([blank[:, 0].mean(), y0 + 0.01, top - 0.006])
cs = np.array(cs)
c0 = cs.mean(0)
_, _, vt = np.linalg.svd(cs - c0)
d = vt[0] if vt[0][1] < 0 else -vt[0]
up = np.array([0.0, 0.0, 1.0]) - d * d[2]
up /= np.linalg.norm(up)
side = np.cross(d, up)
t = (P - c0) @ d
R = (P - c0) - np.outer(t, d)
r = np.linalg.norm(R, axis=1)
below = -(R @ up)
across = R @ side
print(f"SWAP blank axis through {c0.round(3)} towards {d.round(3)}")

ahead = (t > HANDS_FRONT) & (r < np.where(t > -0.45, 0.32, 0.17))
handle = ((t <= HANDS_FRONT) & (t > BUTT) & (r < HANDLE_R)) | ((t < -0.82) & (t > -1.25) & (r < BUTT_R))
reel = ((t > REEL["t"][0]) & (t < REEL["t"][1]) & (below > REEL["below"][0]) & (below < REEL["below"][1])
        & (across > REEL["side"][0]) & (across < REEL["side"][1]))
gone = ahead | handle | reel
print(f"SWAP removing {int(ahead.sum())} ahead of the hands, {int(handle.sum())} of the handle, {int(reel.sum())} of the reel")

bm = bmesh.new()
bm.from_mesh(fig.data)
bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(gone)[0]], context="VERTS")
# scraps left floating (bits of guides, line ends) go too: islands under 300 vertices
bm.verts.ensure_lookup_table()
seen = set()
small = []
for v in bm.verts:
    if v.index in seen:
        continue
    stack, comp = [v], []
    seen.add(v.index)
    while stack:
        a = stack.pop()
        comp.append(a)
        for e in a.link_edges:
            b = e.other_vert(a)
            if b.index not in seen:
                seen.add(b.index)
                stack.append(b)
    if len(comp) < 300:
        small.extend(comp)
bmesh.ops.delete(bm, geom=small, context="VERTS")
bm.to_mesh(fig.data)
bm.free()
print(f"SWAP {len(small)} vertices of loose scraps removed; the figure keeps {len(fig.data.vertices)}")
clay = bpy.data.materials.new("Glina")
clay.use_nodes = True
clay.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.78, 0.66, 0.56, 1.0)
fig.data.materials.clear()
fig.data.materials.append(clay)
fig.hide_select = True

# the game's rod and reel on handles (add_tackle.py), laid on the old rod's axis: blank +Z along d, the reel's side
# (+Y of the rig) down, the reel seat where the old reel was
sys.argv = [sys.argv[0], "--"]
coll_tackle = None
exec(compile(open(os.path.join(os.path.dirname(__file__), "add_tackle.py"), encoding="utf-8").read()
             .replace("bpy.ops.wm.save_mainfile()", "pass"), "add_tackle", "exec"),
     {"__name__": "add_tackle", "__file__": os.path.join(os.path.dirname(__file__), "add_tackle.py")})
wedka = bpy.data.objects["Wedka"]
axis = Vector(d.tolist())
downv = -Vector(up.tolist())
x = downv.cross(axis)
rot = Matrix((x, downv, axis)).transposed()
seat_t = 0.5 * (REEL["t"][0] + REEL["t"][1])
seat = Vector((c0 + d * seat_t).tolist())
wedka.matrix_world = Matrix.Translation(seat - rot @ Vector((0, 0, 0.1))) @ rot.to_4x4()
bpy.context.view_layer.update()

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
scene.render.resolution_x, scene.render.resolution_y = 1200, 900
mid = seat
views = {"Calosc": (mid + Vector((1.6, -1.8, 0.5)), mid + Vector((0, 0, -0.3)), 35),
         "Z_boku": (mid + Vector((0.55, -0.15, 0.08)), mid, 50),
         "Z_przodu": (mid + Vector((-0.3, -0.5, 0.1)), mid, 50),
         "Z_gory": (mid + Vector((0.05, 0.1, 0.5)), mid, 45)}
for name, (at, look, lens) in views.items():
    cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
    cam.data.lens, cam.data.clip_start = lens, 0.005
    scene.collection.objects.link(cam)
    cam.location = at
    cam.rotation_euler = (look - at).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = os.path.join(os.path.dirname(out), f"swap_{name}.png")
    bpy.ops.render.render(write_still=True)
scene.camera = bpy.data.objects["Z_boku"]
bpy.ops.wm.save_as_mainfile(filepath=out)
print("SWAP saved", out)
