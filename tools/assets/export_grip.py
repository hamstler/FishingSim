"""Exports the angler's arms as posed in the grip .blend (rod_in_pose.py's wedka_w_pozie.blend, set by hand) for the game.

The figure's pose becomes the rest of the glb (the arms bent as set, the mesh baked into that shape, the bones' rest
moved to it), the figure is cut to its arms (faces with any arm weight: upper arms, forearms, hands), and everything is
laid in the rod's own frame (the "Wedka" empty = RodRig's frame: blank along +Y in glTF, the reel under it), so the game
hangs it on the rod as it hangs the rod's and reel's models. Helper nodes:
  * CrankRef   - the crank's pivot as it was turned in the file: the left hand is held at its offset from it, so the hand
                 follows the knob as the crank turns;
  * Eyes, EyesAhead, EyesUp - the figure's eyes, 1 m ahead of them and 1 m above: the game puts its camera there.

    blender -b FILE.blend --python tools/assets/export_grip.py -- OUT.glb
"""
import os
import sys

import bpy
from mathutils import Vector

out = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
scene = bpy.context.scene
figure = bpy.data.objects["Wedkarz"]
arm = bpy.data.objects["Szkielet"]
wedka = bpy.data.objects["Wedka"]
korba = bpy.data.objects["Korba"]
for o in (figure, arm):
    o.hide_set(False)
    o.hide_select = False
bpy.context.view_layer.update()

# the eyes: before the head joint of a rigged figure (pose_apose_grip.py's eye camera), else the head's middle, just
# behind the face
humanoid = "RightHand" in arm.data.bones
if humanoid:
    eye = arm.matrix_world @ arm.pose.bones["Head"].head + Vector((0.0, -0.16, 0.12))
else:
    verts = [figure.matrix_world @ v.co for v in figure.data.vertices]
    top = max(v.z for v in verts)
    head = [v for v in verts if v.z > top - 0.3]
    centre = sum(head, Vector()) / len(head)
    eye = Vector((centre.x, min(v.y for v in head) + 0.05, centre.z - 0.02))
skin_mod = next(m for m in figure.modifiers if m.type == "ARMATURE")

# bake the pose: the mesh into the posed shape, the bones' rest to the pose, no constraints left
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = figure
figure.select_set(True)
bpy.ops.object.modifier_apply(modifier=skin_mod.name)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.visual_transform_apply()
for pb in arm.pose.bones:
    for c in list(pb.constraints):
        pb.constraints.remove(c)
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")

# only the arms: drop the faces whose every corner belongs to the body alone
alone = [False] * len(figure.data.vertices)
if humanoid:
    # the Humanoid rig: what the arms' bones (from the upper arm down, the fingers too) move
    arm_bones = {g.index for g in figure.vertex_groups
                 if any(k in g.name for k in ("UpperArm", "LowerArm", "Hand")) or g.name.split("_")[0] in ("thumb", "index", "middle", "ring", "pinky")}
    for v in figure.data.vertices:
        alone[v.index] = sum(g.weight for g in v.groups if g.group in arm_bones) < 0.02
else:
    body = figure.vertex_groups["korpus"].index
    for v in figure.data.vertices:
        alone[v.index] = all(g.group == body and g.weight > 0.98 for g in v.groups) if v.groups else True
import bmesh
bm = bmesh.new()
bm.from_mesh(figure.data)
bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(alone[v.index] for v in f.verts)], context="FACES")
loose = [v for v in bm.verts if not v.link_faces]
bmesh.ops.delete(bm, geom=loose, context="VERTS")
bm.to_mesh(figure.data)
bm.free()
print(f"EXPORTGRIP arms: {len(figure.data.vertices)} vertices, {len(figure.data.polygons)} faces")
mod = figure.modifiers.new("Szkielet", "ARMATURE")
mod.object = arm
world = figure.matrix_world.copy()
figure.parent = arm
figure.matrix_world = world

# helper nodes
def empty(name, at, rot=None):
    e = bpy.data.objects.new(name, None)
    scene.collection.objects.link(e)
    e.matrix_world = rot if rot is not None else __import__("mathutils").Matrix.Translation(at)
    return e

# the knob where the hand holds it (RodRig.KnobAt at the crank's angle in the file): the game keeps it in the fist
import json
_frames = json.load(open(os.path.join(os.path.dirname(__file__), "..", "..", "assets", "models", "reels", "small_hokuto.json"), encoding="utf-8"))
knob_at = korba.matrix_world @ Vector((_frames["knob_out"], _frames["arm"], 0.0))
helpers = [empty("CrankRef", None, korba.matrix_world.copy()), empty("KnobRef", knob_at), empty("Eyes", eye),
           empty("EyesAhead", eye + Vector((0.0, -1.0, 0.0))), empty("EyesUp", eye + Vector((0.0, 0.0, 1.0)))]

# into the rod's frame
to_rod = wedka.matrix_world.inverted()
for o in [arm] + helpers:
    o.matrix_world = to_rod @ o.matrix_world
bpy.context.view_layer.update()

bpy.ops.object.select_all(action="DESELECT")
for o in [arm, figure] + helpers:
    o.select_set(True)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_yup=True,
                          export_skins=True, export_animations=False, export_apply=False)
print("EXPORTGRIP wrote", out, os.path.getsize(out) // 1024, "KB")
