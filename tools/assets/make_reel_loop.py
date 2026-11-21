"""The reeling loop: one turn of the crank in one second (24 frames, looping), the left hand going round with the
crank's knob (its IK target hangs on the crank), the right hand staying closed on the handle. Made from the hand-posed
grip .blend (pose_apose_grip.py's dlonie_na_wedce.blend, set by the user).

The crank turns the way a fixed-spool reel's crank is turned: over the top towards the rod's tip, then down. The arm's
bones are baked into the action "Zwijanie" (visual keys, the IK and Copy Rotation removed in the baked copy), the crank
keyed linear with a cyclic curve; renders the loop to MP4 and a contact sheet of eight frames.

    blender -b GRIP.blend --python tools/assets/make_reel_loop.py -- OUT_DIR
"""
import math
import os
import sys

import bpy
from mathutils import Vector

out_dir = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(out_dir, exist_ok=True)
scene = bpy.context.scene
arm = bpy.data.objects["Szkielet"]
crank = bpy.data.objects["Korba"]
rig = bpy.data.objects["Wedka"]
FRAMES = 24
scene.render.fps = 24
scene.frame_start, scene.frame_end = 1, FRAMES

# the hand goes round with the knob but keeps its own turn (the knob spins in the fingers; parented to the crank the
# whole hand had turned a full circle with it): a point on the crank where the hand's target is, the target itself on
# the rod, following that point's position only
target = bpy.data.objects["Cel_dlon_L"]
bpy.context.view_layer.update()
import json
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
frames = json.load(open(os.path.join(ROOT, "assets", "models", "reels", "small_hokuto.json"), encoding="utf-8"))
knob_local = Vector((frames["knob_out"], frames["arm"], 0.0))       # RodRig.KnobAt at angle 0 (glTF (k, 0, -a) in Blender)
# the knob on the crank, and a holder on the rod that only takes the knob's place: the hand's target hangs on the holder,
# so the knob keeps its place in the fist while the hand keeps its turn on the rod
point = bpy.data.objects.new("Punkt_galki", None)
point.empty_display_type, point.empty_display_size = "PLAIN_AXES", 0.02
scene.collection.objects.link(point)
point.parent = crank
point.location = knob_local
holder = bpy.data.objects.new("Galka", None)
holder.empty_display_type, holder.empty_display_size = "SPHERE", 0.012
scene.collection.objects.link(holder)
bpy.context.view_layer.update()
holder.parent = rig
m = rig.matrix_world.normalized().to_3x3().to_4x4()
m.translation = point.matrix_world.translation
holder.matrix_world = m
bpy.context.view_layer.update()
world = target.matrix_world.copy()
target.parent = holder
target.matrix_parent_inverse = holder.matrix_world.inverted()
target.matrix_world = world
follow = holder.constraints.new("COPY_LOCATION")
follow.target = point
bpy.context.view_layer.update()

# which way is forwards: at the knob's highest point it must move towards the rod's tip
knob_local = None
for o in crank.children:
    if o.type == "EMPTY" and o.name.startswith("Cel_dlon_L"):
        knob_local = o.matrix_local.translation.copy()
down = (rig.matrix_world.to_3x3() @ Vector((0, 1, 0))).normalized()
tip = (rig.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()
a0 = crank.rotation_euler.x


def knob_at(angle):
    crank.rotation_euler.x = angle
    bpy.context.view_layer.update()
    return point.matrix_world.translation.copy()


samples = [(a, knob_at(a)) for a in (a0 + k * math.pi / 18 for k in range(36))]
top_a, top_p = min(samples, key=lambda s: s[1].dot(down))       # the highest point (the reel's side is down)
ahead = knob_at(top_a + 0.05) - top_p
direction = 1.0 if ahead.dot(tip) > 0 else -1.0
crank.rotation_euler.x = a0
print(f"REELLOOP crank turns {'+' if direction > 0 else '-'}X (over the top towards the tip)")

# the crank: one turn a second, linear, repeating
crank.animation_data_clear()
crank.rotation_euler.x = a0
crank.keyframe_insert("rotation_euler", index=0, frame=1)
crank.rotation_euler.x = a0 + direction * 2 * math.pi
crank.keyframe_insert("rotation_euler", index=0, frame=FRAMES + 1)
fc = crank.animation_data.action.fcurves[0] if hasattr(crank.animation_data.action, "fcurves") else None
if fc is None:
    # Blender 4.4+ layered actions: the curves live in the action's channelbag
    act = crank.animation_data.action
    fc = act.layers[0].strips[0].channelbag(act.slots[0]).fcurves[0]
for k in fc.keyframe_points:
    k.interpolation = "LINEAR"
fc.modifiers.new("CYCLES")
crank.animation_data.action.name = "Korba_obrot"

# the left elbow's pole stays where the user set it; check the reach round the turn
pb = arm.pose.bones
lw = "LeftHand" if "LeftHand" in pb else "hand_l"
worst = 0.0
for f in range(1, FRAMES + 1):
    scene.frame_set(f)
    ev = arm.evaluated_get(bpy.context.evaluated_depsgraph_get())
    wr = ev.matrix_world @ ev.pose.bones[lw].head
    worst = max(worst, (wr - target.matrix_world.translation).length)
print(f"REELLOOP left wrist at most {worst * 1000:.1f} mm off its target over the turn (the target keeps the knob in the fist)")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "zwijanie.blend"))

# renders: the loop as a video from the left and from the eyes, and eight frames side by side
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 960, 540
for cam_name, label in (("Z_lewej", "lewa"), ("Widok_z_oczu", "oczy")):
    if cam_name not in bpy.data.objects:
        continue
    scene.camera = bpy.data.objects[cam_name]
    if hasattr(scene.render.image_settings, "media_type"):
        scene.render.image_settings.media_type = "VIDEO"      # Blender 5: video is its own media type
    scene.render.image_settings.file_format = "FFMPEG"
    scene.render.ffmpeg.format = "MPEG4"
    scene.render.ffmpeg.codec = "H264"
    scene.render.filepath = os.path.join(out_dir, f"zwijanie_{label}.mp4")
    bpy.ops.render.render(animation=True)
    print("REELLOOP video", scene.render.filepath)
if hasattr(scene.render.image_settings, "media_type"):
    scene.render.image_settings.media_type = "IMAGE"
scene.render.image_settings.file_format = "PNG"
scene.camera = bpy.data.objects["Z_lewej"]
for k, f in enumerate(range(1, FRAMES + 1, FRAMES // 8)):
    scene.frame_set(f)
    scene.render.filepath = os.path.join(out_dir, f"klatka_{k}.png")
    bpy.ops.render.render(write_still=True)

# the baked copy: plain keys on the bones (for an engine that plays clips), no constraints
scene.frame_set(1)
bpy.context.view_layer.objects.active = arm
arm.hide_set(False)
arm.hide_select = False
arm.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.nla.bake(frame_start=1, frame_end=FRAMES, only_selected=False, visual_keying=True, clear_constraints=True,
                 use_current_action=False, bake_types={"POSE"})
bpy.ops.object.mode_set(mode="OBJECT")
if arm.animation_data and arm.animation_data.action:
    arm.animation_data.action.name = "Zwijanie"
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "zwijanie_wypalone.blend"))
print("REELLOOP saved zwijanie.blend (live IK) and zwijanie_wypalone.blend (baked clip) ->", out_dir)
