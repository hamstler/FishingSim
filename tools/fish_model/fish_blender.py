"""
Builds a fish in Blender from its species file (tools/fish_model/species/<id>.json) and exports it for the game
(assets/fish/models/<id>.glb).

fishgen.py holds the shapes and paints the textures. Blender assembles the model:
  - the body as a quad mesh unwrapped once round the fish (the skin does not stretch over the back or the belly),
    with its relief (eye socket, gape and lips, the gill cover's ledge) and smooth shading;
  - every fin as a pleated membrane given real thickness (Solidify) with its rays as tapered tubes standing out of it,
    the spines of the first dorsal reaching past the membrane; the paired fins mirrored to the other side (Mirror);
  - the eyes, each an iris dome under a clear cornea; barbels as tapering, drooping tubes, on both sides;
  - materials with the painted skin, its normal map, the fin textures and the iris;
then exports glTF (with tangents for the normal map) and renders a Cycles preview.

Usage: blender -b --factory-startup --python tools/fish_model/fish_blender.py -- <species.json> <out.glb> [preview_dir]
(runs fishgen.py --textures first, with the system Python, to paint the textures).
"""
import math
import os
import subprocess
import sys
import tempfile

import bpy
import bmesh
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import fishgen as P  # noqa: E402

FIN_THICKNESS = 0.0011        # of the fish's length
RAY_RADIUS = {"spiny": 0.0026, "spines": 0.0030, "soft": 0.0010, "caudal": 0.0011, "adipose": 0.0}


def to_blender(p):
    """Model space (x right, y up, nose at +z) -> Blender (x right, z up, nose at -y); glTF export turns it back."""
    p = np.asarray(p, dtype=np.float64)
    return np.stack([p[..., 0], -p[..., 2], p[..., 1]], axis=-1)


def make_object(name, pos, uv, faces, material, recalc=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata(to_blender(pos).tolist(), [], [tuple(int(i) for i in f) for f in faces])
    me.update()
    layer = me.uv_layers.new(name="UVMap")
    vi = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", vi)
    uv = np.asarray(uv, dtype=np.float64)
    layer.data.foreach_set("uv", np.stack([uv[:, 0], 1.0 - uv[:, 1]], axis=1)[vi].astype(np.float32).ravel())
    if recalc:
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    me.materials.append(material)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def apply_modifiers(ob):
    for mod in list(ob.modifiers):
        with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
            bpy.ops.object.modifier_apply(modifier=mod.name)


def join(target, others):
    with bpy.context.temp_override(active_object=target, selected_editable_objects=[target] + others,
                                   selected_objects=[target] + others):
        bpy.ops.object.join()


def material(name, colour, normal=None, alpha=False, roughness=0.4, coat=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = colour
    tex.extension = "EXTEND"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if alpha:
        nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        m.blend_method = "BLEND"
        m.use_backface_culling = False
    if normal is not None:
        ntex = nt.nodes.new("ShaderNodeTexImage")
        ntex.image = normal
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = roughness
    if coat > 0 and "Coat Weight" in bsdf.inputs:
        bsdf.inputs["Coat Weight"].default_value = coat
    return m


def clear_material(name):
    """The cornea: clear and glossy."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.9, 0.95, 1.0, 1.0)
    bsdf.inputs["Alpha"].default_value = 0.1
    bsdf.inputs["Roughness"].default_value = 0.03
    m.blend_method = "BLEND"
    return m


def ray_tubes(grid, uvg, radius, reach=0.0, sides=6):
    """Each ray as a tube along the membrane, tapering to its tip (which may reach past the membrane's edge)."""
    n, m, _ = grid.shape
    pos, uv, faces = [], [], []
    for i in range(n):
        pts, uvs = grid[i], uvg[i]
        if reach > 0:
            tip = pts[-1] + (pts[-1] - pts[-2]) * reach * (m - 1)
            pts = np.vstack([pts, tip])
            uvs = np.vstack([uvs, [uvs[-1, 0], 1.0]])
        across = grid[i + 1 if i + 1 < n else i - 1].mean(axis=0) - grid[i].mean(axis=0)
        k_n = len(pts)
        start = len(pos)
        for k in range(k_n):
            d = pts[min(k + 1, k_n - 1)] - pts[max(k - 1, 0)]
            d /= max(np.linalg.norm(d), 1e-9)
            nf = np.cross(d, across)
            nf /= max(np.linalg.norm(nf), 1e-9)
            b = np.cross(d, nf)
            r = radius * (1.0 - 0.75 * k / max(k_n - 1, 1))
            if reach > 0 and k == k_n - 1:
                r = radius * 0.05
            for q in range(sides):
                a = 2 * math.pi * q / sides
                pos.append(pts[k] + r * (math.cos(a) * nf + math.sin(a) * b))
                uv.append(uvs[k])
        for k in range(k_n - 1):
            for q in range(sides):
                a0 = start + k * sides + q
                a1 = start + k * sides + (q + 1) % sides
                faces.append((a0, a1, a1 + sides, a0 + sides))
    return np.array(pos), np.array(uv), faces


def dome(radius, offset, polar_max=72.0, segments=48, rings=14):
    """A spherical cap facing +x (model space), centred on the eye; UVs project the iris onto it."""
    sm = math.sin(math.radians(polar_max))
    pos, uv = [[radius * (1 - offset), 0.0, 0.0]], [[0.5, 0.5]]
    for i in range(1, rings + 1):
        polar = math.radians(polar_max) * i / rings
        for j in range(segments):
            a = 2 * math.pi * j / segments
            y, z = math.sin(polar) * math.sin(a), math.sin(polar) * math.cos(a)
            pos.append([radius * (math.cos(polar) - offset), radius * y, radius * z])
            uv.append([0.5 - 0.5 * z / sm, 0.5 - 0.5 * y / sm])
    faces = [(0, 1 + (j + 1) % segments, 1 + j) for j in range(segments)]
    for i in range(rings - 1):
        for j in range(segments):
            a, b = 1 + i * segments + j, 1 + i * segments + (j + 1) % segments
            faces.append((a, b, b + segments, a + segments))
    return np.array(pos), np.array(uv), faces


def build(work):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = {name: bpy.data.images.load(os.path.join(work, name + ".png")) for name in P.FINS}
    albedo = bpy.data.images.load(os.path.join(work, "body_albedo.jpg"))
    normal = bpy.data.images.load(os.path.join(work, "body_normal.jpg"))
    normal.colorspace_settings.name = "Non-Color"
    iris = bpy.data.images.load(os.path.join(work, "iris.png"))
    objects = []

    # Body.
    pos, uv, faces = P.build_body_unwrapped()
    body = make_object("Body", pos, uv, faces, material("Body", albedo, normal, roughness=0.45, coat=0.2))
    objects.append(body)

    # Fins: membrane with thickness, rays standing out of it, paired fins mirrored.
    segs = 14
    for name, spec in P.FINS.items():
        fpos, fuv, fidx = P.build_fin(spec, segs=segs)
        mat = material(name, tex[name], alpha=True, roughness=0.45)
        fin = make_object(name, fpos, fuv, fidx, mat, recalc=False)
        solid = fin.modifiers.new("Thickness", "SOLIDIFY")
        solid.thickness = FIN_THICKNESS
        solid.offset = 0.0
        apply_modifiers(fin)
        grid = fpos.reshape(spec["rays"], segs + 1, 3)
        uvg = fuv.reshape(spec["rays"], segs + 1, 2)
        radius = RAY_RADIUS.get(spec["kind"], 0.001) * P.HS ** 0.5
        if radius > 0:
            reach = 0.12 if spec["kind"] == "spiny" else (0.2 if spec["kind"] == "spines" else 0.0)
            rpos, ruv, rfaces = ray_tubes(grid, uvg, radius, reach=reach)
            rays = make_object(name + "Rays", rpos, ruv, rfaces, mat, recalc=False)
            join(fin, [rays])
        if "splay" in spec or "droop" in spec:
            mirror = fin.modifiers.new("Pair", "MIRROR")
            mirror.use_axis = (True, False, False)
            apply_modifiers(fin)
        objects.append(fin)

    # Barbels: tapering tubes from the mouth, drooping and curling back, on both sides.
    if P.SPECIES.get("barbels"):
        bar_mat = material("FinBarbels", bpy.data.images.load(os.path.join(work, "FinBarbels.png")), roughness=0.5)
        pts_all, uv_all, faces_all = [], [], []
        for bb in P.SPECIES["barbels"]:
            for sign in (1.0, -1.0):
                root = np.array(bb["root"], dtype=float)
                rx, ry = root
                lat0 = P.half_width(rx) * bb.get("side", 0.8)
                d = np.array(bb["dir"], dtype=float)
                d /= np.linalg.norm(d)
                n_seg = 10
                path = []
                for i in range(n_seg + 1):
                    t = i / n_seg
                    L_ = bb["length"] * t
                    # photo x, photo y, lateral: start along dir, droop (gravity) and sweep back.
                    px = rx + d[0] * L_ + bb.get("back", 0.3) * bb["length"] * t * t
                    py = ry + d[1] * L_ + bb.get("droop", 0.35) * bb["length"] * t * t
                    lat = lat0 + d[2] * L_
                    path.append(P.model(np.array(px), np.array(py), np.array(lat * sign)))
                path = np.array(path)
                r0 = bb.get("radius", 0.0022)
                sides = 6
                start = len(pts_all)
                for i, pnt in enumerate(path):
                    tdir = path[min(i + 1, n_seg)] - path[max(i - 1, 0)]
                    tdir /= max(np.linalg.norm(tdir), 1e-9)
                    a1 = np.cross(tdir, [0, 1, 0])
                    if np.linalg.norm(a1) < 1e-6:
                        a1 = np.cross(tdir, [1, 0, 0])
                    a1 /= np.linalg.norm(a1)
                    a2 = np.cross(tdir, a1)
                    r = r0 * (1 - 0.8 * i / n_seg)
                    for q in range(sides):
                        ang = 2 * math.pi * q / sides
                        pts_all.append(pnt + r * (math.cos(ang) * a1 + math.sin(ang) * a2))
                        uv_all.append((i / n_seg, q / sides))
                for i in range(n_seg):
                    for q in range(sides):
                        a0 = start + i * sides + q
                        a1_ = start + i * sides + (q + 1) % sides
                        faces_all.append((a0, a1_, a1_ + sides, a0 + sides))
        if pts_all:
            ob = make_object("FinBarbels", np.array(pts_all), np.array(uv_all), faces_all, bar_mat)
            objects.append(ob)

    # Eyes: iris dome under a clear cornea, in the sockets on both sides of the head.
    centre = P.eye_centre()
    R = P.EYE_R * 1.08 * P.DEPTH_SCALE
    eye_mat = material("Eye", iris, roughness=0.15)
    cornea_mat = clear_material("Cornea")
    for side, sign in (("Right", 1.0), ("Left", -1.0)):
        for kind, (radius, offset, mat) in (("Eye", (R, 0.58, eye_mat)), ("Cornea", (R * 1.015, 0.56, cornea_mat))):
            dpos, duv, dfaces = dome(radius, offset)
            dpos = dpos * np.array([sign, 1.0, 1.0])
            if sign < 0:
                dfaces = [tuple(reversed(f)) for f in dfaces]
            ob = make_object(kind + side, dpos, duv, dfaces, mat, recalc=False)
            ob.location = tuple(to_blender(centre * np.array([sign, 1.0, 1.0])))
            objects.append(ob)
    return objects


def export(objects, out):
    for ob in bpy.context.scene.objects:
        ob.select_set(ob in objects)
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_apply=True,
                              export_tangents=True, export_yup=True, export_image_format="AUTO",
                              export_materials="EXPORT", export_cameras=False, export_lights=False)


def studio():
    """Cycles, a dark backdrop, key, fill and rim lights, and a camera looking at the fish."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    world = bpy.data.worlds.new("Studio")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.02, 0.03, 0.035, 1)
    scene.world = world
    for name, loc, energy, size in (("Key", (1.2, 0.4, 1.4), 60, 1.2), ("Fill", (-1.0, 0.6, 0.6), 18, 1.5),
                                    ("Rim", (0.0, -1.5, 0.8), 30, 0.8)):
        light = bpy.data.lights.new(name, "AREA")
        light.energy, light.size = energy, size
        lob = bpy.data.objects.new(name, light)
        lob.location = loc
        lob.rotation_euler = (0, 0, 0)
        constraint = lob.constraints.new("TRACK_TO")
        target = bpy.data.objects.new(name + "Target", None)
        bpy.context.scene.collection.objects.link(target)
        constraint.target = target
        bpy.context.scene.collection.objects.link(lob)
    cam = bpy.data.cameras.new("Camera")
    cam.lens = 50
    cob = bpy.data.objects.new("Camera", cam)
    bpy.context.scene.collection.objects.link(cob)
    target = bpy.data.objects.new("Look", None)
    target.location = (0.0, 0.02, 0.0)
    bpy.context.scene.collection.objects.link(target)
    c = cob.constraints.new("TRACK_TO")
    c.target = target
    scene.camera = cob


def preview(path, view):
    scene = bpy.context.scene
    scene.camera.location = view
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species, out = os.path.abspath(argv[0]), os.path.abspath(argv[1])
    previews = argv[2] if len(argv) > 2 else None
    sp = P.load_species(species)
    work = tempfile.mkdtemp(prefix="fish_")
    size = str(sp.get("texture_size", 2048))
    subprocess.run(["python3", os.path.join(HERE, "fishgen.py"), "--textures", species, work, size], check=True)
    objects = build(work)
    export(objects, out)
    print(f"exported {out} ({os.path.getsize(out) // 1024} KB)")
    if previews:
        os.makedirs(previews, exist_ok=True)
        studio()
        preview(os.path.join(previews, "blender_side.png"), (1.75, 0.0, 0.06))
        preview(os.path.join(previews, "blender_three_quarter.png"), (1.15, -0.9, 0.55))


main()
