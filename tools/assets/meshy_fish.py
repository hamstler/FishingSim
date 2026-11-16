"""Fetch Meshy AI fish models from a shared Google Drive folder and shrink them for the game.

    python3 -I tools/assets/meshy_fish.py FOLDER_ID [--size 2048] [--triangles 30000] [--only id,id]

The folder holds one sub-folder per species (Meshy_AI_<Name>_3D_...), each with one .glb (~49 MB: three 4K PNGs).
Each model is rewritten as assets/fish/meshy/<id>.glb: a mesh over --triangles collapsed to it in Blender
(tools/assets/meshy_decimate.py), the colour and normal pictures
re-encoded as WebP (EXT_texture_webp) at --size, the unused metallic-roughness picture dropped.
"""
import io, json, re, shutil, struct, subprocess, sys, tempfile, unicodedata, urllib.request, os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "assets", "fish", "meshy")
ALIAS = {"northern_pike": "szczupak", "perch": "okon", "roach": "ploc", "glowacz_bialopletwy": "glowacz",
         "jesiotr_ostronosy": "jesiotr", "karas_zlocisty": "karas", "pstrag_potokowy": "pstrag",
         "strzebla_potokowa": "strzebla", "troc_jeziorowa": "troc", "bass_wielkogebowy": "bass",
         "amur_bialy": "amur", "glowacz_pregopletwy": "glowacz_pregoplety", "kielb_bialopletwy": "kielb_bialoplety"}


def fold(s):
    s = s.replace("ł", "l").replace("Ł", "L")
    return re.sub(r"[^a-z0-9]+", "_", unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode().lower()).strip("_")


def species_ids():
    d = os.path.join(ROOT, "data", "fish")
    out = {}
    for f in os.listdir(d):
        if f.endswith(".tres"):
            m = re.search(r'SpeciesName = "([^"]+)"', open(os.path.join(d, f), encoding="utf-8").read())
            if m:
                out[fold(m.group(1))] = f[:-5]
            out[f[:-5]] = f[:-5]
    return out


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=600) as r:
        return r.read()


def listing(folder):
    s = get(f"https://drive.google.com/embeddedfolderview?id={folder}").decode("utf-8")
    return re.findall(r'href="https://drive.google.com/(?:drive/folders/|file/d/)([\w-]+)[^"]*"[^>]*>.*?flip-entry-title">([^<]+)<', s, re.S)


def triangles(glb):
    n = struct.unpack("<I", glb[12:16])[0]
    j = json.loads(glb[20:20 + n])
    return sum(j["accessors"][p["indices"]]["count"] // 3 for m in j["meshes"] for p in m["primitives"])


def shrink(glb, size):
    n = struct.unpack("<I", glb[12:16])[0]
    j = json.loads(glb[20:20 + n])
    binoff = 20 + n + 8
    blob = glb[binoff:binoff + struct.unpack("<I", glb[20 + n:24 + n])[0]]
    views = j["bufferViews"]
    mat = j["materials"][0]
    keep = {"normal": mat.get("normalTexture", {}).get("index"),
            "colour": mat["pbrMetallicRoughness"].get("baseColorTexture", {}).get("index")}
    out = bytearray()
    newviews = []

    def add(data):
        while len(out) % 4:
            out.append(0)
        newviews.append({"buffer": 0, "byteOffset": len(out), "byteLength": len(data)})
        out.extend(data)
        return len(newviews) - 1

    remap = {}
    for a in j["accessors"]:
        bv = a["bufferView"]
        if bv not in remap:
            v = views[bv]
            o = v.get("byteOffset", 0)
            remap[bv] = add(blob[o:o + v["byteLength"]])
            for k in ("byteStride", "target"):
                if k in v:
                    newviews[-1][k] = v[k]
        a["bufferView"] = remap[bv]
    images, textures = [], []
    for role, tex in keep.items():
        if tex is None:
            continue
        im = j["images"][j["textures"][tex]["source"]]
        v = views[im["bufferView"]]
        o = v.get("byteOffset", 0)
        pic = Image.open(io.BytesIO(blob[o:o + v["byteLength"]])).convert("RGB")
        if max(pic.size) > size:
            pic = pic.resize((size, size * pic.size[1] // pic.size[0]), Image.LANCZOS)
        buf = io.BytesIO()
        pic.save(buf, "WEBP", quality=92 if role == "normal" else 88, method=6)
        images.append({"bufferView": add(buf.getvalue()), "mimeType": "image/webp",
                       "name": "body_normal" if role == "normal" else "body_albedo"})
        textures.append({"sampler": 0, "extensions": {"EXT_texture_webp": {"source": len(images) - 1}}})
        idx = len(textures) - 1
        if role == "normal":
            mat["normalTexture"] = {"index": idx}
        else:
            mat["pbrMetallicRoughness"]["baseColorTexture"] = {"index": idx}
    mat["pbrMetallicRoughness"].pop("metallicRoughnessTexture", None)
    mat["pbrMetallicRoughness"]["metallicFactor"] = 0.0
    mat["pbrMetallicRoughness"]["roughnessFactor"] = 0.45
    j["images"], j["textures"], j["bufferViews"] = images, textures, newviews
    j["buffers"] = [{"byteLength": len(out)}]
    j["extensionsUsed"] = sorted(set(j.get("extensionsUsed", [])) | {"EXT_texture_webp"})
    j["extensionsRequired"] = ["EXT_texture_webp"]
    js = json.dumps(j, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    while len(out) % 4:
        out.append(0)
    total = 12 + 8 + len(js) + 8 + len(out)
    return (b"glTF" + struct.pack("<II", 2, total) + struct.pack("<I", len(js)) + b"JSON" + js
            + struct.pack("<I", len(out)) + b"BIN\0" + bytes(out))


def main():
    args = sys.argv[1:]
    folder = args[0]
    size = int(args[args.index("--size") + 1]) if "--size" in args else 2048
    target = int(args[args.index("--triangles") + 1]) if "--triangles" in args else 30000
    only = set(args[args.index("--only") + 1].split(",")) if "--only" in args else None
    ids = species_ids()
    os.makedirs(OUT, exist_ok=True)
    for fid, title in listing(folder):
        m = re.match(r"Meshy_AI_(.+?)_(?:3D|Fish|Game)", title)
        if not m:
            print("skip", title)
            continue
        key = fold(m.group(1)).replace("_fish", "")
        sid = ALIAS.get(key) or ids.get(key)
        if not sid:
            print("no species for", title)
            continue
        if only and sid not in only:
            continue
        files = [f for f in listing(fid) if f[1].lower().endswith(".glb")]
        if not files:
            print("no glb in", title)
            continue
        raw = get(f"https://drive.usercontent.google.com/download?id={files[0][0]}&export=download&confirm=t")
        if raw[:4] != b"glTF":
            print("not a glb:", title)
            continue
        tris = triangles(raw)
        if tris > target:
            tmp = os.path.join(tempfile.mkdtemp(), "in.glb")
            open(tmp, "wb").write(raw)
            out = tmp.replace("in.glb", "out.glb")
            subprocess.run(["blender", "-b", "--python-exit-code", "1", "-P",
                            os.path.join(ROOT, "tools", "assets", "meshy_decimate.py"), "--", tmp, out, str(target)],
                           check=True, stdout=subprocess.DEVNULL)
            raw2 = open(out, "rb").read()
            shutil.rmtree(os.path.dirname(tmp))
        else:
            raw2 = raw
        small = shrink(raw2, size)
        open(os.path.join(OUT, sid + ".glb"), "wb").write(small)
        print(f"{sid:20s} {len(raw) / 1e6:6.1f} MB {tris:7d} tris -> {len(small) / 1e6:5.1f} MB {triangles(small):6d} tris ({title})", flush=True)


if __name__ == "__main__":
    main()
