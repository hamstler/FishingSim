#!/usr/bin/env python3
"""Villagers from Quaternius's free (Standard) packs, all CC0 1.0 (public domain, free for any use, commercial too):

  * Universal Base Characters         https://quaternius.com/packs/universalbasecharacters.html (heads, eyes, hair)
  * Modular Character Outfits Fantasy https://quaternius.com/packs/modularcharacteroutfitsfantasy.html (Peasant, Ranger)
  * Universal Animation Library 1, 2  https://quaternius.com/packs/universalanimationlibrary.html (and ...library2.html)

    python3 -I tools/assets/quaternius.py [--src DIR]

The packs are pay-what-you-want with a minimum of 0 on itch.io; missing ones are fetched the way the page's "No thanks,
just take me to the downloads" link does (hosts quaternius.itch.io and itch.io's file server) and unpacked into --src.

Writes assets/characters/:
  <model>.glb      male/female peasant/ranger: the outfit's skinned parts, the base character's head (the body cut away
                   below the collar: the outfit covers it), eyes, brows and every hairstyle of that sex as its own mesh
                   (the game shows one), all re-bound to the outfit's skeleton. Surfaces are named by material: skin,
                   eye, hair1, hair2, outfit. Tangents are computed for the normal maps. No pictures inside.
  anims_<sex>.glb  the outfit's skeleton and the animations the villagers use, retargeted from the libraries'
                   mannequin to that skeleton (each bone's rotation relative to its rest pose, the hips' height scaled),
                   shared by every model of that sex.
  textures/*.webp  the pictures (colour, OpenGL normal map, roughness or ORM), decoded and block compressed by the game
                   (World/Props/CharacterAsset), with a "keep" .import file each.
  <model>.json     which pictures each material takes and which meshes are hairstyles; CREDITS.md.
"""
import argparse
import glob
import http.cookiejar
import io
import json
import os
import re
import struct
import sys
import urllib.parse
import urllib.request
import zipfile

import numpy as np
from PIL import Image

UA = {"User-Agent": "FishingSim-asset-fetch/1.0"}
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "characters")

PACKS = {
    "universal-base-characters": "Universal Base Characters",
    "modular-character-outfits-fantasy": "Modular Character Outfits - Fantasy",
    "universal-animation-library": "Universal Animation Library",
    "universal-animation-library-2": "Universal Animation Library 2",
}

MODELS = {
    "male_peasant": ("M", "Male_Peasant", ["Male_Peasant_Arms", "Male_Peasant_Body", "Male_Peasant_Legs", "Male_Peasant_Feet"]),
    "female_peasant": ("F", "Female_Peasant", ["Female_Peasant_Arms", "Female_Peasant_Body", "Female_Peasant_Legs", "Female_Peasant_Feet"]),
    "male_ranger": ("M", "Male_Ranger", ["Male_Ranger_Arms", "Male_Ranger_Arms_Bracer", "Male_Ranger_Body", "Male_Ranger_Body_Belt_1",
                                         "Male_Ranger_Body_Belt_2", "Male_Ranger_Legs", "Male_Ranger_Feet_Boots", "Male_Ranger_Head_Hood"]),
    "female_ranger": ("F", "Female_Ranger", ["Female_Ranger_Arms", "Female_Ranger_Arms_Bracer", "Female_Ranger_Body", "Female_Ranger_Body_Belt_1",
                                             "Female_Ranger_Body_Belt_2", "Female_Ranger_Legs", "Female_Ranger_Feet", "Female_Ranger_Head_Hood"]),
}
BASE = {"M": ("Superhero_Male_FullBody", "SuperHero_Male"), "F": ("Superhero_Female_FullBody", "Superhero_Female")}
HAIR = {"M": ["Hair_SimpleParted", "Hair_Buzzed", "Hair_Long", "Hair_Beard"], "F": ["Hair_Long", "Hair_Buns", "Hair_BuzzedFemale"]}
# The head is what the base body's skin weights give to these bones, plus the upper chest down to CHEST_DROP below the
# neck (seen in the open collar), pulled CHEST_INSET inward so the shirt always covers it.
HEAD_BONES = {"Head", "neck_01"}
CHEST_BONES = {"spine_03", "clavicle_l", "clavicle_r"}
CHEST_DROP = 0.10
CHEST_INSET = 0.01

MATERIALS = {"MI_Superhero_Male": "skin", "MI_Superhero_Female": "skin", "MI_Regular_Male": "skin", "MI_Regular_Female": "skin",
             "MI_Eyes": "eye", "MI_Hair_1": "hair1", "MI_Hair_2": "hair2", "MI_Peasant": "outfit", "MI_Ranger": "outfit"}

# name -> (source picture, size, kind); kind: colour, normal, grey.
TEXTURES = {
    "skin_m_a": ("T_Regular_Male_Dark_BaseColor.png", 2048, "colour"), "skin_m_n": ("T_Regular_Male_Normal.png", 2048, "normal"),
    "skin_m_r": ("T_Regular_Male_Roughness.png", 1024, "grey"),
    "skin_f_a": ("T_Regular_Female_Dark_BaseColor.png", 2048, "colour"), "skin_f_n": ("T_Regular_Female_Normal.png", 2048, "normal"),
    "skin_f_r": ("T_Regular_Female_Roughness.png", 1024, "grey"),
    "hair1_a": ("T_Hair_1_BaseColor.png", 2048, "colour"), "hair1_n": ("T_Hair_1_Normal.png", 2048, "normal"),
    "hair2_a": ("T_Hair_2_BaseColor.png", 2048, "colour"), "hair2_n": ("T_Hair_2_Normal.png", 2048, "normal"),
    "eye_a": ("T_Eye_Brown.png", 256, "colour"), "eye_n": ("T_Eye_Normal.png", 256, "normal"),
    "peasant_a": ("T_Peasant_BaseColor.png", 4096, "colour"), "peasant2_a": ("T_Peasant_2_BaseColor.png", 4096, "colour"),
    "peasant_n": ("T_Peasant_Normal.png", 4096, "normal"), "peasant_orm": ("T_Peasant_ORM.png", 2048, "colour"),
    "ranger_a": ("T_Ranger_BaseColor.png", 4096, "colour"), "ranger3_a": ("T_Ranger_3_BaseColor.png", 4096, "colour"),
    "ranger_n": ("T_Ranger_Normal.png", 4096, "normal"), "ranger_orm": ("T_Ranger_ORM.png", 2048, "colour"),
}

ANIMS = {
    "UAL1_Standard": ["Idle_Loop", "Idle_Talking_Loop", "Interact", "PickUp_Table", "Sitting_Idle_Loop", "Sitting_Talking_Loop",
                      "Walk_Loop", "Fixing_Kneeling", "Idle_Torch_Loop", "Crouch_Idle_Loop"],
    "UAL2_Standard": ["Idle_FoldArms_Loop", "Idle_Lantern_Loop", "Idle_No_Loop", "Idle_Rail_Loop", "Idle_Rail_Call", "Farm_Harvest",
                      "Farm_PlantSeed", "Farm_Watering", "TreeChopping_Loop", "Walk_Carry_Loop", "Yes", "Consume", "Chest_Open"],
}


# ------------------------------------------------------------------ fetching
def fetch_packs(src):
    jar = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

    def req(url, data=None):
        body = urllib.parse.urlencode(data).encode() if data is not None else None
        with op.open(urllib.request.Request(url, data=body, headers=UA), timeout=600) as r:
            return r.read()

    def token(page):
        return re.search(rb'name="csrf_token" value="([^"]+)"', page).group(1).decode()

    for slug, title in PACKS.items():
        if glob.glob(os.path.join(src, "**", f"{title}[[]Standard[]]"), recursive=True):
            continue
        game = f"https://quaternius.itch.io/{slug}"
        page = req(game + "/purchase")
        page = req(json.loads(req(game + "/download_url", {"csrf_token": token(page)}))["url"])
        for uid, name in re.findall(rb'data-upload_id="(\d+)".*?<strong[^>]*title="([^"]+)"', page, re.S):
            if b"Standard" not in name:
                continue
            url = json.loads(req(f"{game}/file/{uid.decode()}?as_props=1&source=game_download", {"csrf_token": token(page)}))["url"]
            data = req(url)
            zipfile.ZipFile(io.BytesIO(data)).extractall(src)
            print(f"fetched {name.decode()} ({len(data) / 1e6:.0f} MB)", flush=True)


# ------------------------------------------------------------------ glTF reading
class Gltf:
    COMP = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
    NUM = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}

    def __init__(self, path):
        raw = open(path, "rb").read()
        if raw[:4] == b"glTF":
            n = struct.unpack_from("<I", raw, 12)[0]
            self.j = json.loads(raw[20:20 + n])
            self.buf = raw[28 + n:]
        else:
            self.j = json.loads(raw)
            self.buf = open(os.path.join(os.path.dirname(path), self.j["buffers"][0]["uri"]), "rb").read()
        self.path = path

    def read(self, i):
        a = self.j["accessors"][i]
        v = self.j["bufferViews"][a["bufferView"]]
        n = self.NUM[a["type"]]
        dt = np.dtype("<" + self.COMP[a["componentType"]])
        off = v.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = v.get("byteStride", 0) or dt.itemsize * n
        if stride == dt.itemsize * n:
            arr = np.frombuffer(self.buf, dt, a["count"] * n, off).copy()
        else:
            arr = np.array([np.frombuffer(self.buf, dt, n, off + k * stride) for k in range(a["count"])]).reshape(-1)
        if a.get("normalized") and dt.kind in "ui":
            arr = arr.astype(np.float64) / np.iinfo(dt).max
        return arr.reshape(a["count"], n) if n > 1 else arr

    def node(self, name):
        return next(n for n in self.j["nodes"] if n.get("name") == name)

    def joints(self):
        skin = self.j["skins"][0]
        names = [self.j["nodes"][i]["name"] for i in skin["joints"]]
        ibm = self.read(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1).astype(np.float64)  # column-major
        return names, ibm


def find(src, pattern):
    hits = sorted(glob.glob(os.path.join(src, "**", pattern), recursive=True))
    hits = [h for h in hits if "FBX" not in h and "Unity" not in h]
    if not hits:
        raise SystemExit(f"missing in {src}: {pattern}")
    return hits[0]


# ------------------------------------------------------------------ geometry
def rebind(pos, nrm, joints, weights, src_names, src_ibm, dst_names, dst_ibm):
    """Moves a skinned mesh bound to another copy of the skeleton into the target's bind pose and joint order."""
    remap = np.array([dst_names.index(n) for n in src_names])
    per_joint = np.linalg.inv(dst_ibm)[remap] @ src_ibm  # (joints, 4, 4)
    m = np.einsum("vk,vkij->vij", weights, per_joint[joints])
    p = np.einsum("vij,vj->vi", m[:, :3, :3], pos) + m[:, :3, 3]
    n = np.einsum("vij,vj->vi", m[:, :3, :3], nrm)
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    return p, n, remap[joints]


def tangents(pos, nrm, uv, tri):
    """Per-vertex tangents (xyz + handedness) from the UVs, for the normal maps."""
    t = np.zeros_like(pos)
    b = np.zeros_like(pos)
    p0, p1, p2 = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
    u0, u1, u2 = uv[tri[:, 0]], uv[tri[:, 1]], uv[tri[:, 2]]
    e1, e2 = p1 - p0, p2 - p0
    d1, d2 = u1 - u0, u2 - u0
    r = d1[:, 0] * d2[:, 1] - d2[:, 0] * d1[:, 1]
    r = np.where(np.abs(r) < 1e-12, 1e-12, r)
    sdir = (e1 * d2[:, 1:2] - e2 * d1[:, 1:2]) / r[:, None]
    tdir = (e2 * d1[:, 0:1] - e1 * d2[:, 0:1]) / r[:, None]
    for k in range(3):
        np.add.at(t, tri[:, k], sdir)
        np.add.at(b, tri[:, k], tdir)
    t = t - nrm * np.sum(nrm * t, axis=1, keepdims=True)
    bad = np.linalg.norm(t, axis=1) < 1e-9
    t[bad] = np.cross(nrm[bad], [0.0, 1.0, 0.0001])
    t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-12)
    w = np.where(np.sum(np.cross(nrm, t) * b, axis=1) < 0.0, -1.0, 1.0)
    return np.concatenate([t, w[:, None]], axis=1)


class Writer:
    def __init__(self):
        self.bin = bytearray()
        self.views, self.accessors = [], []

    def add(self, arr, kind, comp, target=None, minmax=False):
        while len(self.bin) % 4:
            self.bin.append(0)
        data = np.ascontiguousarray(arr).tobytes()
        view = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data)}
        if target:
            view["target"] = target
        self.bin.extend(data)
        self.views.append(view)
        acc = {"bufferView": len(self.views) - 1, "componentType": comp, "count": int(arr.shape[0]), "type": kind}
        if minmax:
            acc["min"] = [float(x) for x in np.atleast_1d(arr.min(axis=0))]
            acc["max"] = [float(x) for x in np.atleast_1d(arr.max(axis=0))]
        self.accessors.append(acc)
        return len(self.accessors) - 1

    def save(self, path, gltf):
        while len(self.bin) % 4:
            self.bin.append(0)
        gltf["accessors"], gltf["bufferViews"] = self.accessors, self.views
        gltf["buffers"] = [{"byteLength": len(self.bin)}]
        js = json.dumps(gltf, separators=(",", ":")).encode()
        while len(js) % 4:
            js += b" "
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(self.bin)))
            f.write(struct.pack("<II", len(js), 0x4E4F534A) + js)
            f.write(struct.pack("<II", len(self.bin), 0x004E4942) + bytes(self.bin))
        keep(path)


def keep(path):
    with open(path + ".import", "w") as f:
        f.write('[remap]\n\nimporter="keep"\n')


def skeleton_scene(target):
    """The target file's nodes without its meshes: (nodes, index of the Armature node, old -> new node index)."""
    j = target.j
    remap, nodes = {}, []
    for i, n in enumerate(j["nodes"]):
        if "mesh" not in n:
            remap[i] = len(nodes)
            nodes.append({k: v for k, v in n.items() if k in ("name", "children", "translation", "rotation", "scale")})
    for n in nodes:
        if "children" in n:
            n["children"] = [remap[c] for c in n["children"] if c in remap]
            if not n["children"]:
                del n["children"]
    armature = next(i for i, n in enumerate(nodes) if n.get("name") == "Armature")
    return nodes, armature, remap


def write_skin(w, target, remap):
    skin = target.j["skins"][0]
    ibm = target.read(skin["inverseBindMatrices"]).astype(np.float32)
    return {"joints": [remap[i] for i in skin["joints"]], "inverseBindMatrices": w.add(ibm, "MAT4", 5126)}


def build_model(model, src):
    sex, outfit, parts = MODELS[model]
    target = Gltf(find(src, f"{outfit}.gltf"))
    dst_names, dst_ibm = target.joints()
    nodes, armature, remap = skeleton_scene(target)
    w = Writer()
    materials, mat_index, meshes, hair_meshes = [], {}, [], []

    def add_mesh(g, node_name, out_name, keep_tri=None):
        node = g.node(node_name)
        src_names, src_ibm = g.joints()
        prims = []
        for prim in g.j["meshes"][node["mesh"]]["primitives"]:
            a = prim["attributes"]
            mat = MATERIALS[g.j["materials"][prim["material"]]["name"]]
            tri = g.read(prim["indices"]).astype(np.int64).reshape(-1, 3)
            joints = g.read(a["JOINTS_0"]).astype(np.int64)
            weights = g.read(a["WEIGHTS_0"]).astype(np.float64)
            weights /= np.maximum(weights.sum(axis=1, keepdims=True), 1e-9)
            if keep_tri is not None:
                tri = tri[keep_tri(joints, weights, src_names, g.read(a["POSITION"]).astype(np.float64), src_ibm)[tri].all(axis=1)]
                if len(tri) == 0:
                    continue
            used = np.unique(tri)
            back = np.full(len(joints), -1)
            back[used] = np.arange(len(used))
            tri = back[tri]
            pos, nrm, uv = g.read(a["POSITION"])[used], g.read(a["NORMAL"])[used], g.read(a["TEXCOORD_0"])[used]
            joints, weights = joints[used], weights[used]
            if keep_tri is not None:
                # The chest under the collar sits a little inside the shirt.
                dominant = joints[np.arange(len(joints)), weights.argmax(axis=1)]
                chest = np.array([n not in HEAD_BONES for n in src_names])[dominant]
                pos = pos - nrm * (CHEST_INSET * chest)[:, None]
            pos, nrm, joints = rebind(pos.astype(np.float64), nrm.astype(np.float64), joints, weights, src_names, src_ibm, dst_names, dst_ibm)
            tan = tangents(pos, nrm, uv.astype(np.float64), tri)
            if mat not in mat_index:
                mat_index[mat] = len(materials)
                materials.append({"name": mat, "doubleSided": mat != "eye",
                                  "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "metallicFactor": 0.0, "roughnessFactor": 1.0}})
            attrs = {
                "POSITION": w.add(pos.astype(np.float32), "VEC3", 5126, 34962, minmax=True),
                "NORMAL": w.add(nrm.astype(np.float32), "VEC3", 5126, 34962),
                "TANGENT": w.add(tan.astype(np.float32), "VEC4", 5126, 34962),
                "TEXCOORD_0": w.add(uv.astype(np.float32), "VEC2", 5126, 34962),
                "JOINTS_0": w.add(joints.astype(np.uint8), "VEC4", 5121, 34962),
                "WEIGHTS_0": w.add(weights.astype(np.float32), "VEC4", 5126, 34962),
            }
            ind = tri.reshape(-1).astype(np.uint16 if len(pos) < 65536 else np.uint32)
            prims.append({"attributes": attrs, "material": mat_index[mat],
                          "indices": w.add(ind, "SCALAR", 5123 if ind.dtype == np.uint16 else 5125, 34963)})
        if not prims:
            return
        meshes.append({"name": out_name, "primitives": prims})
        nodes.append({"name": out_name, "mesh": len(meshes) - 1, "skin": 0})
        nodes[armature].setdefault("children", []).append(len(nodes) - 1)

    base_file, base_mesh = BASE[sex]
    base = Gltf(find(src, f"{base_file}.gltf"))

    def head_only(joints, weights, names, pos, ibm):
        dominant = joints[np.arange(len(joints)), weights.argmax(axis=1)]
        head = np.array([n in HEAD_BONES for n in names])[dominant]
        neck_y = np.linalg.inv(ibm[names.index("neck_01")])[1, 3]
        chest = np.array([n in CHEST_BONES for n in names])[dominant] & (pos[:, 1] > neck_y - CHEST_DROP)
        return head | chest

    add_mesh(base, base_mesh, "Head", head_only)
    add_mesh(base, "Eyes", "Eyes")
    add_mesh(base, "Eyebrows", "Brows")
    for hair in HAIR[sex]:
        g = Gltf(find(src, os.path.join("Rigged to Head Bone", "glTF*", f"{hair}.gltf")))
        add_mesh(g, hair, hair)
        hair_meshes.append(hair)
    for part in parts:
        add_mesh(target, part, part.split("_", 2)[2])
    gltf = {"asset": {"version": "2.0", "generator": "FishingSim tools/assets/quaternius.py"}, "scene": 0,
            "scenes": [{"name": model, "nodes": [remap[i] for i in target.j["scenes"][0]["nodes"]]}],
            "nodes": nodes, "meshes": meshes, "materials": materials, "skins": [write_skin(w, target, remap)]}
    path = os.path.join(OUT, f"{model}.glb")
    w.save(path, gltf)
    s = "m" if sex == "M" else "f"
    kind = "peasant" if "peasant" in model else "ranger"
    info = {
        "id": model, "sex": sex, "hair": hair_meshes,
        "materials": {
            "skin": {"albedo": [f"skin_{s}_a"], "normal": f"skin_{s}_n", "roughness": f"skin_{s}_r"},
            "eye": {"albedo": ["eye_a"], "normal": "eye_n", "roughness_value": 0.35},
            "hair1": {"albedo": ["hair1_a"], "normal": "hair1_n", "roughness_value": 0.82},
            "hair2": {"albedo": ["hair2_a"], "normal": "hair2_n", "roughness_value": 0.82},
            "outfit": {"albedo": [f"{kind}_a", "peasant2_a" if kind == "peasant" else "ranger3_a"], "normal": f"{kind}_n", "orm": f"{kind}_orm"},
        },
        "author": "Quaternius", "licence": "CC0 1.0 (public domain)",
        "sources": ["https://quaternius.com/packs/universalbasecharacters.html",
                    "https://quaternius.com/packs/modularcharacteroutfitsfantasy.html"],
    }
    with open(os.path.join(OUT, f"{model}.json"), "w") as f:
        json.dump(info, f, indent=1)
    verts = sum(w.accessors[p["attributes"]["POSITION"]]["count"] for m in meshes for p in m["primitives"])
    print(f"{model}: {verts} vertices in {len(meshes)} meshes, {os.path.getsize(path) / 1e6:.2f} MB", flush=True)


# ------------------------------------------------------------------ animations
def qmul(a, b):
    ax, ay, az, aw = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    bx, by, bz, bw = b[..., 0], b[..., 1], b[..., 2], b[..., 3]
    return np.stack([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz], axis=-1)


def qinv(q):
    return q * np.array([-1.0, -1.0, -1.0, 1.0])


def build_anims(sex, src):
    target = Gltf(find(src, f"{'Male' if sex == 'M' else 'Female'}_Peasant.gltf"))
    nodes, armature, remap = skeleton_scene(target)
    dst = {target.j["nodes"][i]["name"]: target.j["nodes"][i] for i in target.j["skins"][0]["joints"]}
    dst_index = {target.j["nodes"][i]["name"]: remap[i] for i in target.j["skins"][0]["joints"]}
    w = Writer()
    # A one-triangle mesh on the skeleton, so every importer makes the skeleton.
    tri = np.array([[0, 0, 0], [0.01, 0, 0], [0, 0.01, 0]], np.float32)
    mesh = {"name": "Stub", "primitives": [{"attributes": {
        "POSITION": w.add(tri, "VEC3", 5126, 34962, minmax=True),
        "JOINTS_0": w.add(np.zeros((3, 4), np.uint8), "VEC4", 5121, 34962),
        "WEIGHTS_0": w.add(np.tile(np.array([1, 0, 0, 0], np.float32), (3, 1)), "VEC4", 5126, 34962)}}]}
    nodes.append({"name": "Stub", "mesh": 0, "skin": 0})
    nodes[armature].setdefault("children", []).append(len(nodes) - 1)
    anims = []
    for lib, names in ANIMS.items():
        g = Gltf(find(src, f"{lib}.glb"))
        src_rest = {g.j["nodes"][i]["name"]: g.j["nodes"][i] for i in g.j["skins"][0]["joints"]}
        hip_scale = np.linalg.norm(dst["pelvis"].get("translation", [0, 1, 0])) / np.linalg.norm(src_rest["pelvis"].get("translation", [0, 1, 0]))
        for a in g.j["animations"]:
            if a["name"] not in names:
                continue
            samplers, channels = [], []
            for c in a["channels"]:
                bone = g.j["nodes"][c["target"]["node"]]["name"]
                path = c["target"]["path"]
                if bone not in dst or path == "scale" or (path == "translation" and bone != "pelvis"):
                    continue
                s = a["samplers"][c["sampler"]]
                times = g.read(s["input"]).astype(np.float32).reshape(-1)
                vals = g.read(s["output"]).astype(np.float64)
                if path == "rotation":
                    rs = np.array(src_rest[bone].get("rotation", [0, 0, 0, 1]), np.float64)
                    rt = np.array(dst[bone].get("rotation", [0, 0, 0, 1]), np.float64)
                    vals = qmul(rt, qmul(qinv(rs), vals))
                    vals /= np.linalg.norm(vals, axis=1, keepdims=True)
                    kind = "VEC4"
                else:
                    ts = np.array(src_rest[bone].get("translation", [0, 0, 0]), np.float64)
                    tt = np.array(dst[bone].get("translation", [0, 0, 0]), np.float64)
                    vals = tt + (vals - ts) * hip_scale
                    kind = "VEC3"
                samplers.append({"input": w.add(times, "SCALAR", 5126, minmax=True), "interpolation": s.get("interpolation", "LINEAR"),
                                 "output": w.add(vals.astype(np.float32), kind, 5126)})
                channels.append({"sampler": len(samplers) - 1, "target": {"node": dst_index[bone], "path": path}})
            anims.append({"name": a["name"], "samplers": samplers, "channels": channels})
    gltf = {"asset": {"version": "2.0", "generator": "FishingSim tools/assets/quaternius.py"}, "scene": 0,
            "scenes": [{"name": f"anims_{sex}", "nodes": [remap[i] for i in target.j["scenes"][0]["nodes"]]}],
            "nodes": nodes, "meshes": [mesh], "skins": [write_skin(w, target, remap)], "animations": anims}
    path = os.path.join(OUT, f"anims_{'male' if sex == 'M' else 'female'}.glb")
    w.save(path, gltf)
    print(f"{os.path.basename(path)}: {len(anims)} animations, {os.path.getsize(path) / 1e6:.2f} MB", flush=True)


# ------------------------------------------------------------------ pictures
def build_textures(src):
    out = os.path.join(OUT, "textures")
    os.makedirs(out, exist_ok=True)
    for name, (picture, size, kind) in TEXTURES.items():
        hits = [h for h in sorted(glob.glob(os.path.join(src, "**", picture), recursive=True)) if "Normals-UnrealEngine" not in h]
        if not hits:
            raise SystemExit(f"missing picture {picture}")
        img = Image.open(hits[0])
        img = img.convert("L" if kind == "grey" else "RGB")
        if img.width != size:
            img = img.resize((size, size), Image.LANCZOS)
        if kind == "grey":
            img = Image.merge("RGB", (img, img, img))
        path = os.path.join(out, f"{name}.webp")
        img.save(path, "WEBP", quality=90 if kind == "normal" else 86, method=6)
        keep(path)
        print(f"textures/{name}.webp {size}px {os.path.getsize(path) / 1e6:.2f} MB", flush=True)


def write_credits():
    with open(os.path.join(OUT, "CREDITS.md"), "w") as f:
        f.write("# Postacie (assets/characters)\n\n"
                "Mieszkańcy wiosek są złożeni z darmowych (Standard) pakietów Quaterniusa na licencji CC0 1.0 (domena publiczna: "
                "wolno ich używać w każdym celu, także komercyjnym, bez opłat i bez obowiązku podawania autora). Dziękujemy autorowi!\n\n"
                "| pakiet | co z niego jest | źródło |\n|---|---|---|\n"
                "| Universal Base Characters | głowy, oczy, brwi, fryzury, tekstury skóry i włosów | https://quaternius.com/packs/universalbasecharacters.html |\n"
                "| Modular Character Outfits - Fantasy | stroje Peasant i Ranger (mężczyzna, kobieta) z teksturami | https://quaternius.com/packs/modularcharacteroutfitsfantasy.html |\n"
                "| Universal Animation Library | animacje (stanie, rozmowa, siedzenie, chód, praca) | https://quaternius.com/packs/universalanimationlibrary.html |\n"
                "| Universal Animation Library 2 | animacje (założone ręce, oparcie o barierkę, prace w polu, rąbanie drewna, przytaknięcie) | https://quaternius.com/packs/universalanimationlibrary2.html |\n\n"
                "Modele połączono, przypięto do szkieletu stroju i przeniesiono na niego animacje narzędziem `tools/assets/quaternius.py`.\n")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=os.path.join(os.path.expanduser("~"), ".cache", "fishingsim", "quaternius"))
    ap.add_argument("--no-textures", action="store_true")
    a = ap.parse_args()
    os.makedirs(a.src, exist_ok=True)
    fetch_packs(a.src)
    os.makedirs(OUT, exist_ok=True)
    for model in MODELS:
        build_model(model, a.src)
    for sex in ("M", "F"):
        build_anims(sex, a.src)
    if not a.no_textures:
        build_textures(a.src)
    write_credits()


if __name__ == "__main__":
    sys.exit(main())
