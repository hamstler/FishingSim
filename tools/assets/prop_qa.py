#!/usr/bin/env python3
"""Quality check of a static model (a Meshy building or prop): size, axes, triangles, pictures, and a contact sheet
of views from the front (+Z, glTF), the right, the back and above, rendered by Blender (Cycles) so the model can be
looked at before it goes into the game.

    python3 -I tools/assets/prop_qa.py MODEL.glb [MODEL.glb ...] --out DIR

Prints per model: triangles, size (x across, y up, z depth, in the file's units), the lowest point, the centre on the
ground, the pictures and their sizes; writes DIR/<name>.png (four views) and DIR/report.json.
"""
import argparse
import io
import json
import os
import struct
import subprocess
import sys
import tempfile

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import quaternius as qt  # noqa: E402

BLENDER = r'''
import bpy, sys, math
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
src, out = a[0], a[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scn = bpy.context.scene
scn.render.engine = "CYCLES"; scn.cycles.samples = 16; scn.cycles.use_denoising = False
scn.render.resolution_x = 480; scn.render.resolution_y = 400
w = bpy.data.worlds.new("w"); scn.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[1].default_value = 0.7
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sun.data.energy = 3.0
sun.rotation_euler = (math.radians(45), 0, math.radians(35)); scn.collection.objects.link(sun)
obs = [o for o in scn.objects if o.type == "MESH"]
pts = [o.matrix_world @ Vector(c) for o in obs for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
c = (lo + hi) / 2; size = max(hi - lo)
cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); cam.data.type = "ORTHO"; cam.data.ortho_scale = size * 1.15
scn.collection.objects.link(cam); scn.camera = cam
# Blender: glTF +Z (front) is -Y, glTF +X stays +X, up is +Z.
for name, d in (("front", Vector((0, -1, 0))), ("right", Vector((1, 0, 0))), ("back", Vector((0, 1, 0))), ("top", Vector((0, -0.001, 1)))):
    cam.location = c + d * size * 3
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scn.render.filepath = "%s_%s.png" % (out, name)
    bpy.ops.render.render(write_still=True)
'''


def inspect(path):
    g = qt.Gltf(path)
    j = g.j
    pts, tris = [], 0
    # World transforms of mesh nodes.
    parent = {}
    for i, n in enumerate(j["nodes"]):
        for c in n.get("children", []):
            parent[c] = i

    def local(n):
        m = np.eye(4)
        if "matrix" in n:
            return np.array(n["matrix"]).reshape(4, 4).T
        from scipy.spatial.transform import Rotation
        m[:3, :3] = Rotation.from_quat(n.get("rotation", [0, 0, 0, 1])).as_matrix() * np.array(n.get("scale", [1, 1, 1]))
        m[:3, 3] = n.get("translation", [0, 0, 0])
        return m

    def world(i):
        return local(j["nodes"][i]) if i not in parent else world(parent[i]) @ local(j["nodes"][i])
    for i, n in enumerate(j["nodes"]):
        if "mesh" not in n:
            continue
        W = world(i)
        for p in j["meshes"][n["mesh"]]["primitives"]:
            P = g.read(p["attributes"]["POSITION"]).astype(np.float64)
            pts.append(P @ W[:3, :3].T + W[:3, 3])
            tris += j["accessors"][p["indices"]]["count"] // 3 if "indices" in p else len(P) // 3
    P = np.vstack(pts)
    lo, hi = P.min(0), P.max(0)
    pics = []
    for img in j.get("images", []):
        bv = j["bufferViews"][img["bufferView"]]
        data = g.buf[bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
        try:
            pics.append([img.get("mimeType"), list(Image.open(io.BytesIO(bytes(data))).size)])
        except Exception:
            pics.append([img.get("mimeType"), None])
    return dict(triangles=int(tris), size=[round(float(v), 3) for v in hi - lo], lowest=round(float(lo[1]), 3),
                centre_xz=[round(float((lo[0] + hi[0]) / 2), 3), round(float((lo[2] + hi[2]) / 2), 3)], pictures=pics,
                megabytes=round(os.path.getsize(path) / 1e6, 2))


def render(path, out):
    with tempfile.TemporaryDirectory() as tmp:
        script = os.path.join(tmp, "r.py")
        open(script, "w").write(BLENDER)
        base = os.path.join(tmp, "v")
        subprocess.run(["blender", "-b", "--python", script, "--", path, base], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        views = [Image.open(f"{base}_{v}.png").convert("RGB") for v in ("front", "right", "back", "top")]
        sheet = Image.new("RGB", (480 * 4, 400), "white")
        for k, im in enumerate(views):
            sheet.paste(im, (480 * k, 0))
        sheet.save(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("models", nargs="+")
    ap.add_argument("--out", default=os.path.join(tempfile.gettempdir(), "prop_qa"))
    ap.add_argument("--no-render", action="store_true")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    report = {}
    for m in args.models:
        name = os.path.splitext(os.path.basename(m))[0]
        info = inspect(m)
        report[name] = info
        print(f"{name:28s} {info['triangles']:8d} tris  size {info['size']}  low {info['lowest']}  {info['megabytes']} MB  pics {info['pictures']}", flush=True)
        if not args.no_render:
            render(m, os.path.join(args.out, f"{name}.png"))
    with open(os.path.join(args.out, "report.json"), "w") as f:
        json.dump(report, f, indent=1)


if __name__ == "__main__":
    main()
