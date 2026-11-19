"""blender -b --python-exit-code 1 -P tools/assets/meshy_decimate.py -- IN.glb OUT.glb TRIANGLES

Welds a Meshy model's seam-split mesh and collapses it to about TRIANGLES triangles (UVs kept) and writes it back as a .glb with its pictures."""
import sys, bpy

a = sys.argv[sys.argv.index("--") + 1:]
src, dst, target = a[0], a[1], int(a[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
import bmesh
for ob in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if tris > target:
        # Meshy's meshes come split along their UV seams (each island its own shell): the collapse could not join
        # across them and tore the surface into shards. Weld first (the UVs live on the corners and stay as they are).
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bm.to_mesh(ob.data)
        bm.free()
        m = ob.modifiers.new("dec", "DECIMATE")
        m.ratio = target / tris
        m.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier=m.name)
    print("triangles", tris, "->", sum(len(p.vertices) - 2 for p in ob.data.polygons))
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_image_format="AUTO", export_yup=True)
