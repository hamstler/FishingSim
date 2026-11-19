"""blender -b --python-exit-code 1 -P tools/assets/meshy_bake.py -- HIGH.glb LOW.glb OUT.glb [SIZE] [AO_SAMPLES]

(baked at twice SIZE, kept at SIZE)

Bakes a Meshy model's detail into its game version: the original (HIGH, millions of triangles) is projected onto the
collapsed one (LOW, made by meshy_fish.py --file, which keeps the original's UV layout) with Cycles' selected-to-active
bake:
  - a tangent-space normal map (the original's surface, including its own normal map, on the low mesh's UVs): planks'
    gaps, thatch, stones, folds and seams light as if they were still geometry;
  - the colour, read off the original's surface (the low mesh gets a clean UV layout first: the collapse dragged the
    original's UVs with the merged vertices and the picture smeared);
  - ambient occlusion from the original's geometry, multiplied into the colour texture (lifted: 0.45 + 0.55 ao), so
    the creases under eaves, between beams and in corners stay dark.
The low mesh keeps its geometry and UVs; its material gets the baked colour and normal map; OUT is a .glb (pictures as
PNG: meshy_fish.py --file turns them into WebP and fills the gaps between the UV islands afterwards).
Not used for the shipped models (see CLAUDE.md): at a 1K texture limit the fresh UV atlas is softer than the collapsed
model's own UVs. Kept for props whose collapse smears their pictures badly.
The cage reaches 3 % of the model's size out and in (thatch and eaves stand far off the low surface) (the low surface lies within that of the original's).
"""
import sys

import bpy
import numpy as np
from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
high_path, low_path, out_path = a[0], a[1], a[2]
final = int(a[3]) if len(a) > 3 else 2048
size = final * 2
ao_samples = int(a[4]) if len(a) > 4 else 24

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.use_denoising = False


def import_joined(path, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    # Bake in world space: apply the import's transforms (glTF Y-up -> Blender Z-up rotation, scale).
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.name = name
    return obj


high = import_joined(high_path, "High")
low = import_joined(low_path, "Low")
dims = max(low.dimensions)
extrusion = 0.03 * dims
# A clean UV layout for the low mesh: the collapse kept the original's UVs but dragged them with the merged vertices,
# so the original picture smeared across the low surface. The colour is baked from the original instead.
bpy.ops.object.select_all(action="DESELECT")
low.select_set(True)
bpy.context.view_layer.objects.active = low
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
# glTF splits the mesh at every UV seam on import: weld it again first, or every triangle becomes its own island.
bpy.ops.mesh.remove_doubles(threshold=1e-5 * dims)
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.001, area_weight=0.0, correct_aspect=True, scale_to_bounds=True)
bpy.ops.uv.pack_islands(rotate=True, margin=0.001)
bpy.ops.object.mode_set(mode="OBJECT")
print(f"high {len(high.data.polygons)} faces, low {len(low.data.polygons)} faces, size {dims:.3f}", flush=True)

colour_img = bpy.data.images.new("baked_colour_raw", size, size, alpha=False, float_buffer=False)
normal_img = bpy.data.images.new("baked_normal", size, size, alpha=False, float_buffer=False)
normal_img.colorspace_settings.name = "Non-Color"
ao_img = bpy.data.images.new("baked_ao", size, size, alpha=False, float_buffer=False)
ao_img.colorspace_settings.name = "Non-Color"


def set_target(img):
    for slot in low.material_slots:
        nt = slot.material.node_tree
        node = nt.nodes.get("bake_target") or nt.nodes.new("ShaderNodeTexImage")
        node.name = "bake_target"
        node.image = img
        nt.nodes.active = node


bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
low.select_set(True)
bpy.context.view_layer.objects.active = low

set_target(colour_img)
scene.cycles.samples = 1
bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_selected_to_active=True, cage_extrusion=extrusion,
                    max_ray_distance=2 * extrusion, margin=16)
print("colour baked", flush=True)

set_target(normal_img)
scene.cycles.samples = 1
bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", use_selected_to_active=True, cage_extrusion=extrusion,
                    max_ray_distance=2 * extrusion, margin=16)
print("normal baked", flush=True)

set_target(ao_img)
scene.cycles.samples = ao_samples
scene.world = bpy.data.worlds.new("w")
scene.world.light_settings.distance = 0.06 * dims
bpy.ops.object.bake(type="AO", use_selected_to_active=True, cage_extrusion=extrusion, max_ray_distance=2 * extrusion,
                    margin=16)
print("ao baked", flush=True)

# The baked colour times the lifted AO -> the low mesh's colour; the baked normal map replaces its normal map.
col = np.empty(size * size * 4, np.float32)
colour_img.pixels.foreach_get(col)
col = col.reshape(size, size, 4)
ao = np.empty(size * size * 4, np.float32)
ao_img.pixels.foreach_get(ao)
ao = ao.reshape(size, size, 4)[..., 0]
col[..., :3] *= (0.45 + 0.55 * np.clip(ao, 0.0, 1.0))[..., None]
col[..., 3] = 1.0
out = bpy.data.images.new("baked_colour", size, size, alpha=True)
out.pixels.foreach_set(col.reshape(-1))
out.pack()
if final < size:     # baked large, kept smaller: sharper than baking at the final size
    out.scale(final, final)
    normal_img.scale(final, final)
for slot in low.material_slots:
    nt = slot.material.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    base_link = bsdf.inputs["Base Color"].links
    if base_link and base_link[0].from_node.type == "TEX_IMAGE":
        base_link[0].from_node.image = out
    else:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = out
        nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    nmap = next((n for n in nt.nodes if n.type == "NORMAL_MAP"), None)
    if nmap is None:
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    tex_link = nmap.inputs["Color"].links
    if tex_link:
        tex_link[0].from_node.image = normal_img
    else:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = normal_img
        nt.links.new(t.outputs["Color"], nmap.inputs["Color"])
    nt.nodes.remove(nt.nodes["bake_target"])
normal_img.pack()

bpy.ops.object.select_all(action="DESELECT")
low.select_set(True)
bpy.context.view_layer.objects.active = low
bpy.data.objects.remove(high, do_unlink=True)
bpy.ops.export_scene.gltf(filepath=out_path, export_format="GLB", use_selection=True, export_image_format="AUTO",
                          export_yup=True)
print("written", out_path, flush=True)
