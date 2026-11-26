"""Builds a .blend for posing the angler's grips by hand: the game's arms (with their pictures) and the rod and reel exactly
where the game shows them, ready to pose in Blender's interface.

    blender -b --factory-startup --python tools/assets/make_grip_blend.py -- build/local/shots/grip_pose.json OUT.blend

In the file:
  * "Cel_dlon_P" / "Cel_dlon_L" (empties): move and turn them; the right / left arm follows (IK on the forearm and the
    upper arm), the hand takes the empty's turn (Copy Rotation). "Cel_lokiec_P" / "Cel_lokiec_L": where the elbows point.
  * The fingers: Pose Mode on the "Armature", rotate the bones index_01_r .. pinky_03_r, thumb_01_r .. (and _l).
  * The rod and the reel are locked (not selectable), so they stay where the game has them.
  * The camera "Widok_z_oczu" sits where the game's camera is; the sun and the sky are as in the renders.
The pose is read back from this file by the grip exporter (the hands against the rod, the finger joints).
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import grip_view  # noqa: E402  (the scene as the game shows it)

C = grip_view.C


def add_empty(name, matrix, size=0.03, shape="ARROWS"):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = shape
    e.empty_display_size = size
    bpy.context.scene.collection.objects.link(e)
    e.matrix_world = matrix
    return e


def evaluated_head(arm, bone):
    """A bone's head in world space with its constraints (IK) applied."""
    ev = arm.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return ev.matrix_world @ ev.pose.bones[bone].head


def best_pole_angle(arm, lower, ik, elbow_before):
    """The IK's pole angle that keeps the elbow where it was (Blender's pole angle depends on the bones' rolls)."""
    target = elbow_before
    best, best_angle = None, 0.0
    for deg in range(-180, 180, 5):
        ik.pole_angle = math.radians(deg)
        bpy.context.view_layer.update()
        d = (evaluated_head(arm, lower) - target).length
        if best is None or d < best:
            best, best_angle = d, math.radians(deg)
    ik.pole_angle = best_angle
    bpy.context.view_layer.update()
    return math.degrees(best_angle), best


def rig_arm(arm, side, label):
    pb = arm.pose.bones
    upper, lower, hand = pb[f"upperarm_{side}"], pb[f"lowerarm_{side}"], pb[f"hand_{side}"]
    w = arm.matrix_world
    hand_w = w @ hand.matrix
    elbow_w = w @ lower.head
    shoulder_w = w @ upper.head
    # the hand's target where the hand is, with its turn; the elbow's 25 cm out from the elbow, away from the arm's line
    target = add_empty(f"Cel_dlon_{label}", hand_w.normalized(), 0.05)
    line = (w @ hand.head) - shoulder_w
    out = (elbow_w - shoulder_w) - line * ((elbow_w - shoulder_w).dot(line) / max(line.length_squared, 1e-6))
    pole = add_empty(f"Cel_lokiec_{label}", Matrix.Translation(elbow_w + out.normalized() * 0.25), 0.04, "SPHERE")
    # on the hand, without its tail: the chain is the forearm and the upper arm, and the hand's head (the wrist) goes to
    # the target (on the forearm without the tail it pulled the elbow there, and the shoulder moved with it)
    ik = hand.constraints.new("IK")
    ik.target = target
    ik.pole_target = pole
    ik.chain_count = 2
    ik.use_tail = False
    rot = hand.constraints.new("COPY_ROTATION")
    rot.target = target
    rot.target_space = "WORLD"
    rot.owner_space = "WORLD"
    bpy.context.view_layer.update()
    print(f"GRIPBLEND {label} shoulder: pose {tuple(round(v, 3) for v in shoulder_w)} evaluated "
          f"{tuple(round(v, 3) for v in evaluated_head(arm, upper.name))}; hand target {tuple(round(v, 3) for v in target.matrix_world.translation)}")
    angle, err = best_pole_angle(arm, lower.name, ik, elbow_w.copy())
    print(f"GRIPBLEND {label}: pole angle {angle:.0f} deg, elbow moved {err * 1000:.1f} mm, "
          f"wrist off its target {(evaluated_head(arm, hand.name) - target.matrix_world.translation).length * 1000:.1f} mm")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    pose_path, out_path = os.path.abspath(argv[0]), os.path.abspath(argv[1])
    pose = json.load(open(pose_path, encoding="utf-8"))
    arm, angler, axis_fix, bones = grip_view.build_scene(pose)
    scene = bpy.context.scene

    # the rod and the reel stay put
    for o in bpy.data.objects:
        if o.type == "MESH" and o not in angler:
            o.hide_select = True

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    rig_arm(arm, "r", "P")
    rig_arm(arm, "l", "L")
    bpy.ops.object.mode_set(mode="OBJECT")

    # light and sky as in the renders
    sun = bpy.data.objects.new("Slonce", bpy.data.lights.new("Slonce", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("Niebo")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.7, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    scene.world = world
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue

    # the game's camera: the angler's eyes (AnglerArms: neck + 12 cm up, 13 cm ahead, in the model's own frame)
    grip = grip_view.unity(pose["grip"]).translation
    knob = grip_view.unity(pose["knob"]).translation
    mid = (grip + knob) * 0.5
    neck = bones.get("neck_01")
    cam_data = bpy.data.cameras.new("Widok_z_oczu")
    cam_data.lens = 24
    cam_data.clip_start = 0.01
    cam = bpy.data.objects.new("Widok_z_oczu", cam_data)
    scene.collection.objects.link(cam)
    if neck is not None:
        n = neck["now"]
        eye_u = Vector((n[3], n[7] + 0.12, n[11] + 0.13))
        cam.location = (C @ eye_u.to_4d()).to_3d()
    else:
        cam.location = mid + Vector((0, 0.35, 0.3))
    cam.rotation_euler = (mid - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080

    # a close-up camera on the grips too
    close_data = bpy.data.cameras.new("Zblizenie")
    close_data.lens = 40
    close_data.clip_start = 0.005
    close = bpy.data.objects.new("Zblizenie", close_data)
    scene.collection.objects.link(close)
    close.location = mid + Vector((0.0, 0.02, 0.45))
    close.rotation_euler = (mid - close.location).to_track_quat("-Z", "Y").to_euler()

    # open in Pose Mode on the armature, the hands' targets selected
    bpy.context.view_layer.objects.active = arm
    for o in bpy.data.objects:
        o.select_set(o.name.startswith("Cel_dlon"))
    bpy.ops.wm.save_as_mainfile(filepath=out_path)
    print("GRIPBLEND saved", out_path)


if __name__ == "__main__":
    main()
