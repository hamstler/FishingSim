"""Exports the new fisherman's arms (Test/Fisherman_Untex_chwyt/wedkarz_chwyt.blend, made by untex_grip.py) for the game:
assets/characters/angler_untex.glb, read by GripArms.

The pose at the last frame of the posing becomes the rest (the mesh baked into it, the bones' rest moved to it: rotations
only were used, so nothing is stretched), the figure is cut to its arms (faces any arm bone moves: upper arms, forearms
with their twist bones, hands, fingers), the hands get a skin material and the sleeves a cloth one (the scan has no
pictures), and everything is laid in the rod's own frame (the "Wedka" empty = RodRig's frame), with the helper nodes
GripArms reads: KnobRef (the knob where the left hand holds it), CrankRef, Eyes, EyesAhead, EyesUp.

    blender -b Test/Fisherman_Untex_chwyt/wedkarz_chwyt.blend --python tools/assets/export_untex_grip.py
"""
import json
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "characters", "angler_untex.glb")
scene = bpy.context.scene
figure = bpy.data.objects["Wedkarz"]
arm = bpy.data.objects["Szkielet"]
wedka = bpy.data.objects["Wedka"]
korba = bpy.data.objects["Korba"]

# the finished pose (the last key of the posing), then no animation left to override it
scene.frame_set(scene.frame_end)
bpy.context.view_layer.update()
for o in (figure, arm):
    o.hide_set(False)
    o.hide_select = False
# the bones' round handles (the file's Icosphere) are for Blender only: the exporter wrote the 2 m sphere into the game
for pb in arm.pose.bones:
    pb.custom_shape = None
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o, do_unlink=True)
pose_now = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
if arm.animation_data:
    arm.animation_data_clear()
for pb in arm.pose.bones:
    pb.matrix_basis = pose_now[pb.name]
bpy.context.view_layer.update()

# the eyes: the head's middle just behind the face (as export_grip.py does for figures without a Head bone)
verts = [figure.matrix_world @ v.co for v in figure.data.vertices]
top = max(v.z for v in verts)
head = [v for v in verts if v.z > top - 0.28 and abs(v.x) < 0.12]
centre = sum(head, Vector()) / len(head)
eye = Vector((centre.x, min(v.y for v in head) + 0.05, top - 0.115))

# bake the pose
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = figure
figure.select_set(True)
dg = bpy.context.evaluated_depsgraph_get()
posed = bpy.data.meshes.new_from_object(figure.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
figure.modifiers.clear()
figure.data = posed
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")

# only the arms; the hands skin, the sleeves cloth
ARM_KEYS = ("ramie", "przedramie", "dlon", "srodrecze", "kciuk", "wskazujacy", "srodkowy", "serdeczny", "maly")
HAND_KEYS = ("dlon", "srodrecze", "kciuk", "wskazujacy", "srodkowy", "serdeczny", "maly")
gname = {g.index: g.name for g in figure.vertex_groups}
arm_w, hand_w = [], []
for v in figure.data.vertices:
    a = h = 0.0
    for g in v.groups:
        n = gname.get(g.group, "")
        if any(n.startswith(k) for k in ARM_KEYS):
            a += g.weight
        if any(n.startswith(k) for k in HAND_KEYS):
            h += g.weight
    arm_w.append(a)
    hand_w.append(h)
me = figure.data
print("EXPORTUNTEX debug: max hand weight", max(hand_w), "max arm weight", max(arm_w), "groups", len(gname), sorted(gname.values())[:8])
me.materials.clear()
skin = bpy.data.materials.new("skora")
skin.use_nodes = True
bsdf = skin.node_tree.nodes["Principled BSDF"]
bsdf.inputs["Base Color"].default_value = (0.42, 0.25, 0.18, 1)     # linear (sRGB about 0.68, 0.54, 0.47)
bsdf.inputs["Roughness"].default_value = 0.55
cloth = bpy.data.materials.new("rekaw")
cloth.use_nodes = True
bsdf = cloth.node_tree.nodes["Principled BSDF"]
bsdf.inputs["Base Color"].default_value = (0.08, 0.1, 0.07, 1)      # a dark olive shirt (linear)
bsdf.inputs["Roughness"].default_value = 0.85
me.materials.append(skin)
me.materials.append(cloth)
bm = bmesh.new()
bm.from_mesh(me)
bm.faces.ensure_lookup_table()
bm.verts.ensure_lookup_table()
# the materials while the vertices still carry their mesh indices (the deletion leaves them stale)
skin_faces = 0
for f in bm.faces:
    f.material_index = 0 if sum(hand_w[v.index] for v in f.verts) / len(f.verts) > 0.5 else 1
    skin_faces += f.material_index == 0
# only what follows the arms: a face goes when any corner is mostly the body's (the shirt round the shoulders showed)
bmesh.ops.delete(bm, geom=[f for f in bm.faces if any(arm_w[v.index] < 0.5 for v in f.verts)], context="FACES")
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(me)
bm.free()
print(f"EXPORTUNTEX arms: {len(me.vertices)} vertices, {len(me.polygons)} faces, {skin_faces} of them skin")
mod = figure.modifiers.new("Szkielet", "ARMATURE")
mod.object = arm
# the skin hung on the skeleton first (its place kept), so moving the skeleton below carries it (export_grip.py's order)
world_fig = figure.matrix_world.copy()
figure.parent = arm
figure.matrix_world = world_fig


def empty(name, at=None, mat=None):
    e = bpy.data.objects.new(name, None)
    scene.collection.objects.link(e)
    e.matrix_world = mat if mat is not None else Matrix.Translation(at)
    return e


frames = json.load(open(os.path.join(ROOT, "assets", "models", "reels", "small_hokuto.json"), encoding="utf-8"))
knob_at = korba.matrix_world @ Vector((frames["knob_out"], frames["arm"], 0.0))
helpers = [empty("CrankRef", mat=korba.matrix_world.copy()), empty("KnobRef", knob_at), empty("Eyes", eye),
           empty("EyesAhead", eye + Vector((0.0, -1.0, 0.0))), empty("EyesUp", eye + Vector((0.0, 0.0, 1.0)))]
# into the rod's frame
to_rod = wedka.matrix_world.inverted()
for o in [arm] + helpers:
    o.matrix_world = to_rod @ o.matrix_world
bpy.context.view_layer.update()
bpy.ops.object.select_all(action="DESELECT")
for o in [arm, figure] + helpers:
    o.select_set(True)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_yup=True,
                          export_skins=True, export_animations=False, export_apply=False)
print("EXPORTUNTEX wrote", OUT, os.path.getsize(OUT) // 1024, "KB")
