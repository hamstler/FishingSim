"""Renders a saved grip .blend from each of its cameras (to check it, or the pose set in it by hand).

    blender -b FILE.blend --python tools/assets/render_blend.py -- OUT_DIR
"""
import os
import sys

import bpy

out_dir = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(out_dir, exist_ok=True)
scene = bpy.context.scene
scene.render.resolution_x, scene.render.resolution_y = 1280, 720
for cam in [o for o in bpy.data.objects if o.type == "CAMERA"]:
    scene.camera = cam
    scene.render.filepath = os.path.join(out_dir, f"blend_{cam.name}.png")
    bpy.ops.render.render(write_still=True)
    print("RENDERBLEND", cam.name, "->", scene.render.filepath)
