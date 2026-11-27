"""Build the close-range tree meshes from the supplied Meshy GLBs using Blender."""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

source, output = map(Path, sys.argv[sys.argv.index("--") + 1:])
output.mkdir(parents=True, exist_ok=True)
kinds = {"brzoza":"birch", "dab":"oak", "sosna":"pine", "swierk":"spruce", "wierzba":"willow"}
for name, kind in kinds.items():
    dst = output / (kind + ".glb")
    if dst.exists():
        continue
    path, = source.glob(f"*_tree_{name}_*.glb")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path), merge_vertices=True)
    mesh, = (obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    count = len(mesh.data.polygons)
    modifier = mesh.modifiers.new("Close-range tree budget", "DECIMATE")
    modifier.ratio = min(1.0, 9500 / count)
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    coords = [Vector(p) for p in mesh.bound_box]
    # Unit-height asset with a ground pivot. The forest instancer applies the species height.
    low = Vector(tuple(min(p[i] for p in coords) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in coords) for i in range(3)))
    ground = Vector(((low.x+high.x)*.5,(low.y+high.y)*.5,low.z))
    height = high.z-low.z
    for vertex in mesh.data.vertices:
        vertex.co = (vertex.co-ground)/height
    mesh.data.update()
    mesh.name = kind
    bpy.ops.export_scene.gltf(filepath=str(dst), export_format="GLB", use_selection=True, export_yup=True,
                              export_animations=False, export_skins=False)
    print(f"TREE {kind}: {count:,} -> {len(mesh.data.polygons):,} faces", flush=True)
