"""Reduce redundant leaf geometry in the normalized intermediate tree GLBs."""
import sys
from pathlib import Path

import bmesh
import bpy

source, output = map(Path, sys.argv[sys.argv.index("--") + 1:])
output.mkdir(parents=True, exist_ok=True)
for path in sorted(source.glob("*.glb")):
    dest = output / path.name
    if dest.exists() and dest.stat().st_mtime >= path.stat().st_mtime:
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path), merge_vertices=True)
    obj, = (obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    start = len(obj.data.polygons)
    for threshold in (.0018, .003, .0045):
        count = len(obj.data.polygons)
        if count <= 30000:
            break
        mesh = bmesh.new()
        mesh.from_mesh(obj.data)
        bmesh.ops.remove_doubles(mesh, verts=list(mesh.verts), dist=threshold)
        bmesh.ops.delete(mesh, geom=[v for v in mesh.verts if not v.link_faces], context="VERTS")
        mesh.to_mesh(obj.data)
        mesh.free()
        count = len(obj.data.polygons)
        if count > 28000:
            decimate = obj.modifiers.new("Reduce leaf clusters", "DECIMATE")
            decimate.ratio = 25000 / count
            decimate.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=decimate.name)
    if len(obj.data.polygons) > 32000:
        mesh = bmesh.new()
        mesh.from_mesh(obj.data)
        visited = set()
        components = []
        for vertex in mesh.verts:
            if vertex in visited:
                continue
            vertices = [vertex]
            visited.add(vertex)
            for current in vertices:
                for edge in current.link_edges:
                    other = edge.other_vert(current)
                    if other not in visited:
                        visited.add(other)
                        vertices.append(other)
            faces = {face for v in vertices for face in v.link_faces}
            components.append((sum(face.calc_area() for face in faces), len(faces), vertices))
        remaining = len(mesh.faces)
        remove = []
        # Loose chips and tiny isolated leaves set the floor of collapse decimation.
        for _, faces, vertices in sorted(components, key=lambda entry: entry[0])[:-1]:
            if remaining <= 32000:
                break
            remaining -= faces
            remove.extend(vertices)
        if remove:
            bmesh.ops.delete(mesh, geom=remove, context="VERTS")
        mesh.to_mesh(obj.data)
        mesh.free()
    obj.data.validate(clean_customdata=False)
    obj.data.update()
    bpy.ops.export_scene.gltf(filepath=str(dest), export_format="GLB", use_selection=True, export_yup=True,
                              export_animations=False, export_skins=False)
    print(f"TREE compact {path.stem}: {start:,} -> {len(obj.data.polygons):,}", flush=True)
