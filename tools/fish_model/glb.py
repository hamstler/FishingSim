"""
A small glTF 2.0 binary (.glb) writer: meshes with positions, normals, UVs and indices, one node each (optionally
translated, rotated or scaled), materials with a base colour texture (and a normal map), images embedded as PNG or
JPEG. Enough for a game model built in code; numpy only.
"""
import io
import json
import struct

import numpy as np


class Glb:
    def __init__(self):
        self.bin = bytearray()
        self.doc = {"asset": {"version": "2.0", "generator": "FishingSim tools/fish_model"}, "scene": 0,
                    "scenes": [{"nodes": []}], "nodes": [], "meshes": [], "materials": [], "textures": [],
                    "images": [], "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 33071}],
                    "accessors": [], "bufferViews": [], "buffers": []}

    def _view(self, data, target=None):
        while len(self.bin) % 4:
            self.bin.append(0)
        view = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data)}
        if target:
            view["target"] = target
        self.bin.extend(data)
        self.doc["bufferViews"].append(view)
        return len(self.doc["bufferViews"]) - 1

    def _accessor(self, array, kind, target):
        array = np.ascontiguousarray(array)
        comp = 5126 if array.dtype == np.float32 else 5125
        acc = {"bufferView": self._view(array.tobytes(), target), "componentType": comp,
               "count": int(array.shape[0]), "type": kind}
        if kind == "VEC3" and comp == 5126:
            acc["min"] = array.min(axis=0).tolist()
            acc["max"] = array.max(axis=0).tolist()
        self.doc["accessors"].append(acc)
        return len(self.doc["accessors"]) - 1

    def image(self, pil_image, name, jpeg=False):
        buf = io.BytesIO()
        if jpeg:
            pil_image.convert("RGB").save(buf, "JPEG", quality=92)
        else:
            pil_image.save(buf, "PNG", optimize=True)
        self.doc["images"].append({"name": name, "bufferView": self._view(buf.getvalue()),
                                   "mimeType": "image/jpeg" if jpeg else "image/png"})
        self.doc["textures"].append({"sampler": 0, "source": len(self.doc["images"]) - 1})
        return len(self.doc["textures"]) - 1

    def material(self, name, colour_tex, normal_tex=None, blend=False, roughness=0.5):
        m = {"name": name, "doubleSided": True,
             "pbrMetallicRoughness": {"baseColorTexture": {"index": colour_tex}, "metallicFactor": 0.0,
                                      "roughnessFactor": roughness}}
        if normal_tex is not None:
            m["normalTexture"] = {"index": normal_tex}
        if blend:
            m["alphaMode"] = "BLEND"
        self.doc["materials"].append(m)
        return len(self.doc["materials"]) - 1

    def mesh(self, name, positions, normals, uvs, indices, material):
        prim = {"attributes": {"POSITION": self._accessor(positions.astype(np.float32), "VEC3", 34962),
                               "NORMAL": self._accessor(normals.astype(np.float32), "VEC3", 34962),
                               "TEXCOORD_0": self._accessor(uvs.astype(np.float32), "VEC2", 34962)},
                "indices": self._accessor(indices.astype(np.uint32).reshape(-1), "SCALAR", 34963),
                "material": material}
        self.doc["meshes"].append({"name": name, "primitives": [prim]})
        return len(self.doc["meshes"]) - 1

    def node(self, name, mesh, translation=None, scale=None):
        n = {"name": name, "mesh": mesh}
        if translation is not None:
            n["translation"] = [float(v) for v in translation]
        if scale is not None:
            n["scale"] = [float(v) for v in scale]
        self.doc["nodes"].append(n)
        self.doc["scenes"][0]["nodes"].append(len(self.doc["nodes"]) - 1)

    def save(self, path, extras=None):
        if extras:
            self.doc["asset"]["extras"] = extras
        while len(self.bin) % 4:
            self.bin.append(0)
        self.doc["buffers"] = [{"byteLength": len(self.bin)}]
        js = json.dumps(self.doc, separators=(",", ":")).encode()
        js += b" " * ((4 - len(js) % 4) % 4)
        total = 12 + 8 + len(js) + 8 + len(self.bin)
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, total))
            f.write(struct.pack("<II", len(js), 0x4E4F534A) + js)
            f.write(struct.pack("<II", len(self.bin), 0x004E4942) + bytes(self.bin))


def vertex_normals(positions, indices):
    """Smooth normals: the area-weighted face normals around each vertex."""
    p = positions.astype(np.float64)
    tri = indices.reshape(-1, 3)
    fn = np.cross(p[tri[:, 1]] - p[tri[:, 0]], p[tri[:, 2]] - p[tri[:, 0]])
    n = np.zeros_like(p)
    for k in range(3):
        np.add.at(n, tri[:, k], fn)
    length = np.linalg.norm(n, axis=1, keepdims=True)
    return (n / np.maximum(length, 1e-12)).astype(np.float32)
