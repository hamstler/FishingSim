"""blender -b --python-exit-code 1 -P tools/assets/meshy_decimate.py -- IN.glb OUT.glb TRIANGLES [seams]

Welds a Meshy model's seam-split mesh and collapses it to about TRIANGLES triangles (UVs kept) and writes it back as a
.glb with its pictures.

With "seams" the borders of the UV islands are kept where they are (a vertex group of every vertex on an edge whose two
faces disagree on the UVs, left out of the collapse): the collapse can then never drag a picture across a seam, so the
model keeps the original's texture exactly (the plain weld + collapse smeared the logs and planks of the buildings)."""
import sys, bpy, bmesh

a = sys.argv[sys.argv.index("--") + 1:]
src, dst, target = a[0], a[1], int(a[2])
keep_seams = len(a) > 3 and a[3] == "seams"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for ob in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if tris > target:
        # Meshy's meshes come split along their UV seams (each island its own shell): the collapse could not join
        # across them and tore the surface into shards. Weld first (the UVs live on the corners and stay as they are).
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6 * max(ob.dimensions) if keep_seams else 1e-5)
        seam = set()
        if keep_seams and bm.loops.layers.uv.active is not None:
            uv = bm.loops.layers.uv.active
            for e in bm.edges:
                if len(e.link_faces) != 2:
                    seam.update(v.index for v in e.verts)
                    continue
                f1, f2 = e.link_faces
                for v in e.verts:
                    u1 = next(l[uv].uv for l in f1.loops if l.vert == v)
                    u2 = next(l[uv].uv for l in f2.loops if l.vert == v)
                    if (u1 - u2).length > 1e-5:
                        seam.update(x.index for x in e.verts)
                        break
        bm.to_mesh(ob.data)
        bm.free()
        m = ob.modifiers.new("dec", "DECIMATE")
        m.ratio = target / tris
        m.use_collapse_triangulate = True
        if seam:
            ob.vertex_groups.new(name="seam").add(list(seam), 1.0, "REPLACE")
            m.vertex_group = "seam"
            m.invert_vertex_group = True       # only the vertices off the seams collapse
            m.vertex_group_factor = 1.0
            print("seam vertices", len(seam), "of", len(ob.data.vertices))
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier=m.name)
    print("triangles", tris, "->", sum(len(p.vertices) - 2 for p in ob.data.polygons))
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_image_format="AUTO", export_yup=True)
