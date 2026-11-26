"""Reads the grip set by hand in a rod_in_pose.py .blend (wedka_w_pozie.blend) into JSON: the rod's frame ("Wedka"),
the crank's pivot ("Korba"), each arm's shoulder, elbow and wrist as posed (IK evaluated), and a sample of the figure's
torso for aligning another figure to this one.

    blender -b FILE.blend --python tools/assets/read_grip_pose.py -- OUT.json
"""
import json
import os
import random
import sys

import bpy

out = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
deps = bpy.context.evaluated_depsgraph_get()
arm = bpy.data.objects["Szkielet"].evaluated_get(deps)
fig = bpy.data.objects["Wedkarz"]


def mat(m):
    return [list(r) for r in m]


def head(bone):
    return list(arm.matrix_world @ arm.pose.bones[bone].head)


pose = {"wedka": mat(bpy.data.objects["Wedka"].matrix_world), "korba": mat(bpy.data.objects["Korba"].matrix_world),
        "korba_angle": bpy.data.objects["Korba"].rotation_euler.x}
for k in ("P", "L"):
    pose[k] = {"shoulder": head(f"ramie_{k}"), "elbow": head(f"przedramie_{k}"), "wrist": head(f"dlon_{k}"),
               "hand": mat(arm.matrix_world @ arm.pose.bones[f"dlon_{k}"].matrix)}
# the torso of the figure as modelled (rest), for aligning a new figure: between the hips and the shoulders, inside
# the shoulders' width
random.seed(1)
pts = [fig.matrix_world @ v.co for v in fig.data.vertices]
torso = [list(p) for p in pts if -0.05 < p.z < 0.55 and abs(p.x) < 0.17]
pose["torso"] = random.sample(torso, min(6000, len(torso)))
json.dump(pose, open(out, "w"), indent=1)
print("READGRIP wrote", out, "torso points", len(pose["torso"]))
