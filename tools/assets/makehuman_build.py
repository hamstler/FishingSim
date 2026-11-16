#!/usr/bin/env python3
"""Realistic villagers from MakeHuman (CC0), built with MPFB2 inside Blender 4.2 (the `bpy` module).

Everything that ships is CC0: the MakeHuman base mesh, targets and rig that come with MPFB2 (its LICENSE.ASSETS.md) and
the "makehuman_system_assets" pack (skins, eyes, eyebrows, eyelashes, hair, clothes, shoes, hats) from
files.makehumancommunity.org. MPFB2's code (GPL-3.0) only runs here, at build time.

Each character is made with MPFB (body by macros, skin, eyes, brows, lashes, hair, clothes, shoes, optional hat, the
"game_engine" rig), posed into the T-pose of the villagers' existing skeleton (the Quaternius outfit skeleton the
animations in assets/characters/anims_<sex>.glb were retargeted to: each MakeHuman bone swung onto the direction of the
same bone there), the pose baked into the meshes, and then written onto that very skeleton: the same 65 bones and rest
rotations, the joints moved to where this body has them. So the existing animations play on it unchanged.

Writes assets/characters/<id>.glb (meshes Head (the body), Eyes, Brows, Lashes, Hair_<style>, Outfit_*, Shoes,
Head_Hood (a hat, when the model has one), surfaces named by material slot), <id>.json (pictures per slot) and
textures/mh_*.webp (colour, OpenGL normal maps).

Setup (outside the repository):
  python3.11 -m venv /tmp/mh/bpyenv && /tmp/mh/bpyenv/bin/pip install bpy==4.2.0 pillow
  git clone --depth 1 https://github.com/makehumancommunity/mpfb2 /tmp/mh/mpfb2
  curl -o /tmp/mh/sys.zip https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip
  unzip -q /tmp/mh/sys.zip -d /tmp/mh/pack/sys
Run:
  /tmp/mh/bpyenv/bin/python tools/assets/makehuman_build.py [ids...]
"""
import argparse
import importlib
import json
import os
import shutil
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import quaternius as qt  # noqa: E402  (glTF reader/writer, tangents)

MH = os.environ.get("FISHSIM_MH", "/tmp/mh")
PKG = "bl_ext.user_default.mpfb"
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "assets", "characters"))
WORK = os.path.join(MH, "work")

# id -> body macros, skin, eyes, hair, clothes (+ shoes), hat
CHARACTERS = {
    "mh_male_worker": dict(sex="M", macros=dict(gender=1.0, age=0.62, muscle=0.62, weight=0.6, height=0.55),
                           skin="middleage_caucasian_male", eyes="brown", hair="short02",
                           clothes=["male_worksuit01"], shoes="shoes02", hat="fedora01"),
    "mh_male_casual": dict(sex="M", macros=dict(gender=1.0, age=0.45, muscle=0.55, weight=0.5, height=0.6),
                           skin="young_caucasian_male", eyes="bluegreen", hair="short04",
                           clothes=["male_casualsuit05"], shoes="shoes01", hat=None),
    "mh_male_denim": dict(sex="M", macros=dict(gender=1.0, age=0.5, muscle=0.5, weight=0.55, height=0.52),
                          skin="young_caucasian_male2", eyes="grey", hair="short03",
                          clothes=["male_casualsuit01"], shoes="shoes04", hat=None),
    "mh_male_old": dict(sex="M", macros=dict(gender=1.0, age=0.88, muscle=0.4, weight=0.62, height=0.45),
                        skin="old_caucasian_male", eyes="grey", hair="short01",
                        clothes=["male_casualsuit03"], shoes="shoes02", hat="fedora_cocked"),
    "mh_female_casual": dict(sex="F", macros=dict(gender=0.0, age=0.42, muscle=0.45, weight=0.48, height=0.68),
                             skin="young_caucasian_female", eyes="blue", hair="ponytail01",
                             clothes=["male_casualsuit01"], shoes="shoes03", hat=None),
    "mh_female_middle": dict(sex="F", macros=dict(gender=0.0, age=0.65, muscle=0.4, weight=0.6, height=0.62),
                             skin="middleage_caucasian_female", eyes="brownlight", hair="braid01",
                             clothes=["female_elegantsuit01"], shoes="shoes03", hat=None),
}

RENAME = {"Root": "root", "head": "Head"}


# ------------------------------------------------------------------ MPFB
def install_mpfb():
    """Installs MPFB2 as a Blender extension (from a zip of its src/mpfb) and copies the asset pack into its data dir."""
    zip_path = os.path.join(MH, "mpfb.zip")
    if not os.path.exists(zip_path):
        shutil.make_archive(zip_path[:-4], "zip", os.path.join(MH, "mpfb2", "src"), "mpfb")
    if PKG not in bpy.context.preferences.addons.keys():
        bpy.ops.extensions.package_install_files(filepath=zip_path, repo="user_default", enable_on_install=True)
    data = os.path.join(bpy.utils.extension_path_user(PKG, create=True), "data")
    if not os.path.exists(os.path.join(data, "clothes")):
        shutil.copytree(os.path.join(MH, "pack", "sys"), data, dirs_exist_ok=True)
    return data


def service(name, cls):
    return getattr(importlib.import_module(f"{PKG}.services.{name}"), cls)


def asset(subdir, name, ext):
    AssetService = service("assetservice", "AssetService")
    path = AssetService.find_asset_absolute_path(f"{name}.{ext}", asset_subdir=subdir)
    if path is None:
        raise RuntimeError(f"missing asset {subdir}/{name}.{ext}")
    return path


# ------------------------------------------------------------------ the target skeleton (glTF, Y up)
def target_skeleton(sex):
    g = qt.Gltf(os.path.join(OUT, f"anims_{'male' if sex == 'M' else 'female'}.glb"))
    nodes = g.j["nodes"]
    joints = g.j["skins"][0]["joints"]
    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get("children", []):
            parent[c] = i

    def local(n):
        t = np.array(n.get("translation", [0, 0, 0]), np.float64)
        q = n.get("rotation", [0, 0, 0, 1])
        return t, Quaternion((q[3], q[0], q[1], q[2]))

    world = {}

    def world_of(i):
        if i in world:
            return world[i]
        t, q = local(nodes[i])
        if i in parent:
            pt, pq = world_of(parent[i])
            wt = pt + np.array(pq @ Vector(t))
            wq = pq @ q
        else:
            wt, wq = t, q
        world[i] = (wt, wq)
        return world[i]

    bones = []
    for i in joints:
        wt, wq = world_of(i)
        bones.append(dict(index=i, name=nodes[i]["name"], parent=parent.get(i), pos=wt, rot=wq, local=local(nodes[i])))
    return g, bones


def gl_to_bl(v):
    return Vector((v[0], -v[2], v[1]))


def bl_to_gl(v):
    return np.array([v[0], v[2], -v[1]])


# ------------------------------------------------------------------ one character
def build(cid, spec):
    HumanService = service("humanservice", "HumanService")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)
    macro = service("targetservice", "TargetService").get_default_macro_info_dict()
    macro.update(spec["macros"])
    macro["race"] = {"asian": 0.0, "caucasian": 1.0, "african": 0.0}
    if spec["sex"] == "F":
        macro["cupsize"] = 0.5
    basemesh = HumanService.create_human(macro_detail_dict=macro)
    HumanService.set_character_skin(asset("skins", spec["skin"], "mhmat"), basemesh, skin_type="GAMEENGINE")
    HumanService.add_builtin_rig(basemesh, "game_engine")
    parts = [("eyes", "low-poly", "mhclo", "Eyes"), ("eyebrows", "eyebrow001", "mhclo", "Eyebrows"),
             ("eyelashes", "eyelashes01", "mhclo", "Eyelashes"), ("hair", spec["hair"], "mhclo", "Hair")]
    parts += [("clothes", c, "mhclo", "Clothes") for c in spec["clothes"]]
    parts.append(("clothes", spec["shoes"], "mhclo", "Clothes"))
    if spec.get("hat"):
        parts.append(("clothes", spec["hat"], "mhclo", "Clothes"))
    for subdir, name, ext, kind in parts:
        HumanService.add_mhclo_asset(asset(subdir, name, ext), basemesh, asset_type=kind, material_type="GAMEENGINE")
    eye_mat = asset("eyes/materials", spec["eyes"], "mhmat")
    rig = basemesh.parent
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    # Bake shape keys and helper masks into plain meshes; keep only the armature modifier.
    service("exportservice", "ExportService").bake_modifiers_remove_helpers(
        basemesh, bake_masks=True, bake_subdiv=False, remove_helpers=True, also_proxy=True)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    for o in meshes:
        bpy.context.view_layer.objects.active = o
        if o.data.shape_keys:
            o.shape_key_add(name="__mix", from_mix=True)
            keys = o.data.shape_keys.key_blocks
            for k in list(keys):
                if k.name != "__mix":
                    o.shape_key_remove(k)
            o.shape_key_remove(o.data.shape_keys.key_blocks["__mix"])
        for m in list(o.modifiers):
            if m.type == "ARMATURE":
                continue
            if m.show_viewport and m.type == "MASK":
                bpy.ops.object.modifier_apply(modifier=m.name)     # the body under the clothes goes
            else:
                o.modifiers.remove(m)
    return basemesh, rig, meshes, eye_mat


def pose_to_target(rig, bones):
    """Swings every bone onto the direction of the same bone in the target skeleton's rest (its local +Y)."""
    by_name = {b["name"]: b for b in bones}
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    order = []

    def walk(b):
        order.append(b)
        for c in b.children:
            walk(c)
    for b in rig.pose.bones:
        if b.parent is None:
            walk(b)
    for pb in order:
        name = RENAME.get(pb.name, pb.name)
        if name not in by_name or name == "root":
            continue
        want = gl_to_bl(np.array(by_name[name]["rot"] @ Vector((0, 1, 0)))).normalized()
        m = pb.matrix.copy()
        have = (m.to_3x3() @ Vector((0, 1, 0))).normalized()
        swing = have.rotation_difference(want)
        head = m.translation.copy()
        pb.matrix = Matrix.Translation(head) @ swing.to_matrix().to_4x4() @ m.to_3x3().to_4x4()
        bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_pose(rig, meshes):
    """Applies the pose to the meshes and makes it the rig's rest."""
    for o in meshes:
        mods = [m for m in o.modifiers if m.type == "ARMATURE"]
        if not mods:
            continue
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mods[0].name)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    # Skinned to the rig again, now in its new rest.
    for o in meshes:
        mod = o.modifiers.new("Armature", "ARMATURE")
        mod.object = rig


# ------------------------------------------------------------------ pictures
def save_picture(src, name, normal=False, size=2048):
    from PIL import Image
    os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)
    img = Image.open(src)
    img = img.convert("RGBA" if name.endswith("_t") else "RGB")
    if max(img.size) > size:
        img.thumbnail((size, size), Image.LANCZOS)
    path = os.path.join(OUT, "textures", f"{name}.webp")
    img.save(path, "WEBP", quality=88, method=6)
    qt.keep(path)
    return name


def material_pictures(obj):
    """(colour, normal) image paths of the object's first material (MPFB's GAMEENGINE Principled setup)."""
    colour = normal = None
    for slot in obj.material_slots:
        mat = slot.material
        if not mat or not mat.use_nodes:
            continue
        for node in mat.node_tree.nodes:
            if node.type != "TEX_IMAGE" or not node.image:
                continue
            path = bpy.path.abspath(node.image.filepath)
            links = [l.to_socket.name for l in node.outputs[0].links]
            lname = (node.label + node.name + path).lower()
            if "normal" in lname or any("Normal" in s for s in links):
                normal = normal or path
            elif "Base Color" in links or "diffuse" in lname or "color" in lname:
                colour = colour or path
    return colour, normal


# ------------------------------------------------------------------ writing onto the target skeleton
def write(cid, spec, rig, meshes, eye_mat, bones, g):
    """Exports the meshes, re-binds them to the target skeleton (moved to this body's joints) and writes the glb."""
    tmp = os.path.join(WORK, f"{cid}.glb")
    os.makedirs(WORK, exist_ok=True)
    slots = {}
    names = {}
    for o in meshes:
        kind = o.get("MPFB_GEN_object_type", "")
        src_name = os.path.basename(str(o.get("MPFB_GEN_asset_source", ""))).split(".")[0]
        if kind == "Basemesh":
            slot, mesh_name = "skin", "Head"
        elif kind == "Eyebrows":
            slot, mesh_name = "brows", "Brows"
        elif kind == "Eyelashes":
            slot, mesh_name = "lashes", "Lashes"
        elif kind == "Eyes":
            slot, mesh_name = "eye", "Eyes"
        elif kind == "Hair":
            slot, mesh_name = "hair1", f"Hair_{spec['hair']}"
        elif spec.get("hat") and src_name == spec["hat"]:
            slot, mesh_name = "hat", "Head_Hood"
        elif src_name.startswith("shoes"):
            slot, mesh_name = "shoes", "Shoes"
        else:
            slot, mesh_name = "outfit", "Outfit"
        o.name = mesh_name
        o.data.name = mesh_name
        colour, normal = material_pictures(o)
        if slot == "eye":
            colour = os.path.join(os.path.dirname(eye_mat), f"{spec['eyes']}_eye.png")
        slots[slot] = (colour, normal)
        names[mesh_name] = slot
        for ms in o.material_slots:
            if ms.material:
                ms.material.name = slot
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    for o in meshes:
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=tmp, use_selection=True, export_format="GLB", export_skins=True, export_animations=False,
                              export_materials="PLACEHOLDER", export_yup=True, export_apply=False, export_tangents=False)
    src = qt.Gltf(tmp)
    sn = src.j["nodes"]
    sjoints = src.j["skins"][0]["joints"]
    sibm = src.read(src.j["skins"][0]["inverseBindMatrices"]).reshape(-1, 4, 4)
    # This body's joint positions (world, glTF): from the inverse bind matrices (the rest pose).
    joint_pos = {}
    for k, ni in enumerate(sjoints):
        m = np.linalg.inv(sibm[k].T)
        joint_pos[RENAME.get(sn[ni]["name"], sn[ni]["name"])] = m[:3, 3]
    # The target skeleton moved to these joints: rest rotations kept, positions from this body, the rest (root, leaves)
    # placed by the target's own offsets.
    tgt = {b["name"]: b for b in bones}
    pos = {}
    for b in bones:
        if b["name"] in joint_pos:
            pos[b["name"]] = joint_pos[b["name"]]
    height_scale = (joint_pos["Head"][1] / tgt["Head"]["pos"][1]) if "Head" in joint_pos else 1.0
    for b in bones:
        if b["name"] not in pos:
            par = g.j["nodes"][b["parent"]]["name"] if b["parent"] is not None else None
            if par in pos:
                pos[b["name"]] = pos[par] + (b["pos"] - tgt[par]["pos"]) * height_scale
            else:
                pos[b["name"]] = b["pos"] * np.array([1, 0, 1])
    # glTF output: the target's joint hierarchy with new translations.
    w = qt.Writer()
    nodes = []
    index = {}
    for b in bones:
        index[b["index"]] = len(nodes)
        nodes.append({"name": b["name"]})
    for b in bones:
        n = nodes[index[b["index"]]]
        q = b["local"][1]
        n["rotation"] = [float(q.x), float(q.y), float(q.z), float(q.w)]
        if b["parent"] is not None and b["parent"] in index:
            pb = tgt[g.j["nodes"][b["parent"]]["name"]]
            rel = Vector(pos[b["name"]] - pos[pb["name"]])
            n["translation"] = [float(v) for v in pb["rot"].inverted() @ rel]
            nodes[index[b["parent"]]].setdefault("children", []).append(index[b["index"]])
        else:
            n["translation"] = [float(v) for v in pos[b["name"]]]
    ibm = []
    for b in bones:
        m = Matrix.Translation(Vector(pos[b["name"]])) @ b["rot"].to_matrix().to_4x4()
        ibm.append(np.array(m.inverted()).T.reshape(-1))
    joint_nodes = [index[b["index"]] for b in bones]
    skin = {"joints": joint_nodes, "inverseBindMatrices": w.add(np.array(ibm, np.float32), "MAT4", 5126),
            "skeleton": joint_nodes[0]}
    name_to_joint = {b["name"]: k for k, b in enumerate(bones)}
    remap = np.array([name_to_joint.get(RENAME.get(sn[ni]["name"], sn[ni]["name"]), 0) for ni in sjoints], np.int32)
    meshes_out, mesh_nodes = [], []
    for ni, n in enumerate(sn):
        if "mesh" not in n:
            continue
        m = src.j["meshes"][n["mesh"]]
        prims = []
        for p in m["primitives"]:
            a = p["attributes"]
            P = src.read(a["POSITION"]).astype(np.float32)
            N = src.read(a["NORMAL"]).astype(np.float32)
            UV = src.read(a["TEXCOORD_0"]).astype(np.float32)
            J = remap[src.read(a["JOINTS_0"]).astype(np.int32)].astype(np.uint8)
            W = src.read(a["WEIGHTS_0"]).astype(np.float32)
            W /= np.maximum(W.sum(axis=1, keepdims=True), 1e-6)
            I = src.read(p["indices"]).astype(np.uint32).reshape(-1)
            T = qt.tangents(P, N, UV, I.reshape(-1, 3))
            mat_name = names.get(n["name"], "outfit")      # the slot of this mesh (the export keeps no material names)
            prims.append({"attributes": {"POSITION": w.add(P, "VEC3", 5126, 34962, minmax=True),
                                         "NORMAL": w.add(N, "VEC3", 5126, 34962),
                                         "TANGENT": w.add(T.astype(np.float32), "VEC4", 5126, 34962),
                                         "TEXCOORD_0": w.add(UV, "VEC2", 5126, 34962),
                                         "JOINTS_0": w.add(J, "VEC4", 5121, 34962),
                                         "WEIGHTS_0": w.add(W, "VEC4", 5126, 34962)},
                          "indices": w.add(I, "SCALAR", 5125, 34963), "material": mat_name})
        meshes_out.append({"name": n["name"], "primitives": prims})
        mesh_nodes.append({"name": n["name"], "mesh": len(meshes_out) - 1, "skin": 0})
    materials = sorted({p["material"] for m in meshes_out for p in m["primitives"]})
    for m in meshes_out:
        for p in m["primitives"]:
            p["material"] = materials.index(p["material"])
    armature = {"name": "Armature", "children": [joint_nodes[0]]}
    for mn in mesh_nodes:
        nodes.append(mn)
        armature["children"].append(len(nodes) - 1)
    nodes.append(armature)
    gltf = {"asset": {"version": "2.0", "generator": "FishingSim tools/assets/makehuman_build.py"}, "scene": 0,
            "scenes": [{"name": cid, "nodes": [len(nodes) - 1]}], "nodes": nodes, "meshes": meshes_out, "skins": [skin],
            "materials": [{"name": m} for m in materials]}
    path = os.path.join(OUT, f"{cid}.glb")
    w.save(path, gltf)
    qt.keep(path)
    # Pictures and the model's description.
    info = {"id": cid, "sex": spec["sex"], "hair": [f"Hair_{spec['hair']}"], "materials": {},
            "author": "MakeHuman community (built with MPFB2)", "licence": "CC0 1.0 (public domain)",
            "sources": ["https://static.makehumancommunity.org/asset_packs/makehuman_system_assets.html",
                        "https://github.com/makehumancommunity/mpfb2 (LICENSE.ASSETS.md)"]}
    rough = {"skin": 0.55, "eye": 0.3, "brows": 0.9, "lashes": 0.9, "hair1": 0.75, "outfit": 0.88, "shoes": 0.6, "hat": 0.85}
    for slot, (colour, normal) in slots.items():
        entry = {"roughness_value": rough.get(slot, 0.85)}
        if colour and os.path.exists(colour):
            alpha = slot in ("brows", "lashes", "hair1")
            entry["albedo"] = [save_picture(colour, f"{cid}_{slot}_{'t' if alpha else 'a'}", size=2048 if slot in ("skin", "outfit") else 1024)]
        if normal and os.path.exists(normal):
            entry["normal"] = save_picture(normal, f"{cid}_{slot}_n", normal=True, size=1024)
        if slot in ("brows", "lashes", "hair1"):
            entry["alpha"] = True
        info["materials"][slot] = entry
    with open(os.path.join(OUT, f"{cid}.json"), "w") as f:
        json.dump(info, f, indent=1, ensure_ascii=False)
    print(f"{cid}: {len(meshes_out)} meshes, {os.path.getsize(path) / 1e6:.2f} MB, slots {sorted(slots)}, "
          f"head at {joint_pos.get('Head', [0, 0, 0])[1]:.2f} m", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("ids", nargs="*")
    args = ap.parse_args()
    ids = args.ids or list(CHARACTERS)
    install_mpfb()
    targets = {}
    for cid in ids:
        spec = CHARACTERS[cid]
        if spec["sex"] not in targets:
            targets[spec["sex"]] = target_skeleton(spec["sex"])
        g, bones = targets[spec["sex"]]
        basemesh, rig, meshes, eye_mat = build(cid, spec)
        pose_to_target(rig, bones)
        bake_pose(rig, meshes)
        write(cid, spec, rig, meshes, eye_mat, bones, g)


if __name__ == "__main__":
    main()
