#!/usr/bin/env python3
"""Quality checks for a rigged character (assets/characters/<id>.glb), e.g. the player's Meshy fisherman built by
tools/assets/meshy_player_rig.py.

    python3 -I tools/assets/rig_qa.py [id] [--out DIR] [--no-render]

Plays the shared animations (assets/characters/anims_<sex>.glb, the pelvis scaled by the json's pelvis_scale like the
game's PelvisFit) on the model with numpy (glTF skinning: joint world matrix x inverse bind matrix) and checks:

  mesh       NaN/inf positions, degenerate triangles, vertices no triangle uses, weights that do not sum to 1 or point
             at joints outside the skin
  rig        the joints are the villagers' skeleton (same names and parents as mh_male_worker), inverse bind matrices
             invertible, bone lengths sane
  alignment  standing on the ground in the modelled pose and in the standing animations, facing +Z, height
  weights    per pose, triangles with an edge over 1.8x or under 0.45x its modelled length and changed by more than
             2 cm (cloth torn by bad weights), per mesh and the worst bones
  animation  vertices thrown far from the body (> 0.6 m outside the skeleton's reach) in any sampled frame
  collision  the arms' vertices inside the body (more than 2 cm under the nearest body surface) per pose, against
             the modelled pose's share
  grip       the fists' channel (closed fists must leave 12-50 mm for a rod handle to sink into); the real grip on
             the handle is checked in the game with FISHSIM_IK_DEBUG=1 (GRIP lines)

and renders previews (Blender, Cycles): the modelled pose and every sampled pose from the front, the side and the
back, into one contact sheet. Writes DIR/report.json, DIR/report.md and DIR/sheet.png; exit code 1 when a check fails.
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile

import numpy as np
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import quaternius as qt  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
CHARS = os.path.join(ROOT, "assets", "characters")

# (animation, time as a fraction of its length)
POSES = [("Idle_Loop", 0.25), ("Walk_Loop", 0.25), ("Walk_Loop", 0.75), ("Sitting_Idle_Loop", 0.5), ("Interact", 0.5),
         ("TreeChopping_Loop", 0.4), ("Farm_Watering", 0.5), ("Idle_FoldArms_Loop", 0.5), ("Crouch_Idle_Loop", 0.5)]
STANDING = {"Idle_Loop", "Walk_Loop", "Interact", "TreeChopping_Loop", "Farm_Watering", "Idle_FoldArms_Loop"}
STRETCH, SHRINK = 1.8, 0.45
LIMITS = {"weights_bad_share": 0.03, "inside_share": 0.08, "thrown": 0, "ground": 0.06}


# ------------------------------------------------------------------ loading
def quat(q):
    return Rotation.from_quat(q).as_matrix()


def trs(n):
    m = np.eye(4)
    if "matrix" in n:
        return np.array(n["matrix"], np.float64).reshape(4, 4).T
    r = quat(n.get("rotation", [0, 0, 0, 1]))
    s = np.array(n.get("scale", [1, 1, 1]), np.float64)
    m[:3, :3] = r * s[None, :]
    m[:3, 3] = n.get("translation", [0, 0, 0])
    return m


class Model:
    def __init__(self, cid):
        self.id = cid
        self.g = qt.Gltf(os.path.join(CHARS, f"{cid}.glb"))
        self.info = json.load(open(os.path.join(CHARS, f"{cid}.json")))
        j = self.g.j
        self.nodes = j["nodes"]
        self.parent = {}
        for i, n in enumerate(self.nodes):
            for c in n.get("children", []):
                self.parent[c] = i
        skin = j["skins"][0]
        self.joints = skin["joints"]
        self.names = [self.nodes[i]["name"] for i in self.joints]
        self.ibm = self.g.read(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1).astype(np.float64)
        self.meshes = []
        for ni, n in enumerate(self.nodes):
            if "mesh" not in n:
                continue
            for p in j["meshes"][n["mesh"]]["primitives"]:
                a = p["attributes"]
                self.meshes.append(dict(
                    name=n["name"], node=ni,
                    P=self.g.read(a["POSITION"]).astype(np.float64), N=self.g.read(a["NORMAL"]).astype(np.float64),
                    UV=self.g.read(a["TEXCOORD_0"]).astype(np.float64) if "TEXCOORD_0" in a else None,
                    J=self.g.read(a["JOINTS_0"]).astype(np.int64), W=self.g.read(a["WEIGHTS_0"]).astype(np.float64),
                    I=self.g.read(p["indices"]).astype(np.int64).reshape(-1, 3)))
        self.rest_local = [trs(n) for n in self.nodes]

    def world(self, local):
        out = [None] * len(self.nodes)

        def w(i):
            if out[i] is None:
                out[i] = local[i] if i not in self.parent else w(self.parent[i]) @ local[i]
            return out[i]
        for i in range(len(self.nodes)):
            w(i)
        return out

    def skin_matrices(self, local):
        W = self.world(local)
        return np.array([W[ni] @ self.ibm[k] for k, ni in enumerate(self.joints)]), W

    def posed(self, mats, m):
        M = np.einsum("vk,vkab->vab", m["W"] / np.maximum(m["W"].sum(1, keepdims=True), 1e-9), mats[m["J"]])
        P = np.einsum("vab,vb->va", M[:, :3, :3], m["P"]) + M[:, :3, 3]
        N = np.einsum("vab,vb->va", M[:, :3, :3], m["N"])
        N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
        return P, N

    def bind_local(self):
        """Node transforms that reproduce the bind pose (each joint's world = inverse of its IBM)."""
        local = [m.copy() for m in self.rest_local]
        world_bind = {ni: np.linalg.inv(self.ibm[k]) for k, ni in enumerate(self.joints)}
        W = self.world(self.rest_local)
        for ni in self.joints:
            par = self.parent.get(ni)
            pw = world_bind.get(par, W[par] if par is not None else np.eye(4))
            local[ni] = np.linalg.inv(pw) @ world_bind[ni]
        return local


class Animations:
    def __init__(self, sex):
        self.g = qt.Gltf(os.path.join(CHARS, f"anims_{'male' if sex == 'M' else 'female'}.glb"))
        self.by_name = {a["name"]: a for a in self.g.j["animations"]}

    def sample(self, model, name, frac, pelvis_scale):
        a = self.by_name[name]
        local = [m.copy() for m in model.rest_local]
        index = {n["name"]: i for i, n in enumerate(model.nodes)}
        for c in a["channels"]:
            s = a["samplers"][c["sampler"]]
            t = self.g.read(s["input"])
            v = self.g.read(s["output"])
            tt = frac * t.max()
            k = int(np.clip(np.searchsorted(t, tt), 1, len(t) - 1))
            f = (tt - t[k - 1]) / max(t[k] - t[k - 1], 1e-9)
            node_name = self.g.j["nodes"][c["target"]["node"]]["name"]
            if node_name not in index:
                continue
            ni = index[node_name]
            m = local[ni]
            if c["target"]["path"] == "rotation":
                r = slerp(v[k - 1], v[k], f)
                scale = np.linalg.norm(m[:3, :3], axis=0)
                m[:3, :3] = quat(r) * scale[None, :]
            elif c["target"]["path"] == "translation":
                tr = v[k - 1] * (1 - f) + v[k] * f
                if node_name == "pelvis":
                    tr = tr * np.array([1.0, pelvis_scale, 1.0])
                m[:3, 3] = tr
        return local


def slerp(a, b, f):
    a = np.asarray(a, np.float64)
    b = np.asarray(b, np.float64)
    if np.dot(a, b) < 0:
        b = -b
    d = np.clip(np.dot(a, b), -1, 1)
    if d > 0.9995:
        q = a + f * (b - a)
    else:
        th = np.arccos(d)
        q = (np.sin((1 - f) * th) * a + np.sin(f * th) * b) / np.sin(th)
    return q / np.linalg.norm(q)


# ------------------------------------------------------------------ checks
def check_mesh(model):
    out = {}
    ok = True
    for m in model.meshes:
        P, I, W, J = m["P"], m["I"], m["W"], m["J"]
        bad_pos = int((~np.isfinite(P)).any(1).sum())
        area = 0.5 * np.linalg.norm(np.cross(P[I[:, 1]] - P[I[:, 0]], P[I[:, 2]] - P[I[:, 0]]), axis=1)
        degenerate = int((area < 1e-10).sum())
        unused = int(len(P) - len(np.unique(I)))
        wsum = W.sum(1)
        bad_w = int((np.abs(wsum - 1) > 0.01).sum())
        bad_j = int((J >= len(model.joints)).any(1).sum())
        out[m["name"]] = dict(vertices=len(P), triangles=len(I), nan=bad_pos, degenerate=degenerate, unused=unused,
                              weight_sum_off=bad_w, joint_out_of_range=bad_j)
        ok &= bad_pos == 0 and bad_j == 0 and bad_w == 0 and degenerate <= 0.002 * len(I)
    return ok, out


def check_rig(model):
    ref = Model("mh_male_worker")
    same_names = model.names == ref.names
    par = lambda md: [md.nodes[md.parent[ni]]["name"] if ni in md.parent else None for ni in md.joints]  # noqa: E731
    same_parents = par(model) == par(ref)
    det = np.linalg.det(model.ibm[:, :3, :3])
    bind = [np.linalg.inv(m)[:3, 3] for m in model.ibm]
    lengths = {}
    for k, ni in enumerate(model.joints):
        pi = model.parent.get(ni)
        if pi in model.joints and model.nodes[pi]["name"] != "root":     # root -> pelvis is the hips' height
            lengths[model.names[k]] = float(np.linalg.norm(bind[k] - bind[model.joints.index(pi)]))
    longest = max(lengths.values())
    ok = same_names and same_parents and bool(np.all(np.abs(det) > 1e-6)) and longest < 0.7
    return ok, dict(joints=len(model.joints), same_names_as_villagers=same_names, same_parents=same_parents,
                    min_abs_det=float(np.abs(det).min()), longest_bone=round(longest, 3))


def dominant_bone(model, m, verts):
    j = m["J"][verts, np.argmax(m["W"][verts], 1)]
    names, counts = np.unique([model.names[x] for x in j], return_counts=True)
    order = np.argsort(-counts)
    return {str(names[i]): int(counts[i]) for i in order[:4]}


def run(cid, out_dir, render):
    os.makedirs(out_dir, exist_ok=True)
    model = Model(cid)
    report = {"id": cid, "checks": {}}
    ok_mesh, mesh = check_mesh(model)
    ok_rig, rig = check_rig(model)
    report["checks"]["mesh"] = dict(ok=ok_mesh, **mesh)
    report["checks"]["rig"] = dict(ok=ok_rig, **rig)
    pelvis = float(model.info.get("pelvis_scale", 1.0))
    anims = Animations(model.info.get("sex", "M"))
    bind_local = model.bind_local()
    bind_mats, _ = model.skin_matrices(bind_local)
    bind = {m["name"]: model.posed(bind_mats, m)[0] for m in model.meshes}
    poses = [("modelled", None, bind_local)] + [(f"{a}@{f:.2f}", a, anims.sample(model, a, f, pelvis)) for a, f in POSES]
    pose_results = {}
    previews = []
    all_ok = {"weights": True, "animation": True, "collision": True, "alignment": True}
    for label, anim, local in poses:
        mats, world = model.skin_matrices(local)
        posed = {m["name"]: model.posed(mats, m) for m in model.meshes}
        res = {}
        joint_pos = np.array([world[ni][:3, 3] for ni in model.joints])
        reach = cKDTree(joint_pos)
        for m in model.meshes:
            P, _ = posed[m["name"]]
            B = bind[m["name"]]
            I = m["I"]
            e_now = np.stack([np.linalg.norm(P[I[:, a]] - P[I[:, b]], axis=1) for a, b in ((0, 1), (1, 2), (2, 0))], 1)
            e_bind = np.stack([np.linalg.norm(B[I[:, a]] - B[I[:, b]], axis=1) for a, b in ((0, 1), (1, 2), (2, 0))], 1)
            ratio = e_now / np.maximum(e_bind, 1e-6)
            grow = np.abs(e_now - e_bind)
            # Torn cloth: an edge both much longer/shorter than modelled and changed by more than 2 cm (tiny edges
            # between near-coincident vertices change ratio wildly without anything showing).
            bad = (((ratio > STRETCH) | (ratio < SHRINK)) & (grow > 0.02)).any(1)
            share = float(bad.mean())
            far, _ = reach.query(P)
            thrown = int((far > 0.6).sum())
            res[m["name"]] = dict(stretched_share=round(share, 4), max_stretch=round(float(ratio.max()), 2),
                                  thrown=thrown,
                                  worst_bones=dominant_bone(model, m, np.unique(I[bad].reshape(-1))) if bad.any() else {})
            if label != "modelled":
                all_ok["weights"] &= share <= LIMITS["weights_bad_share"]
            all_ok["animation"] &= thrown <= LIMITS["thrown"]
        # Arms inside the body.
        if "Arms" in posed and "Body" in posed:
            AP, _ = posed["Arms"]
            BP, BN = posed["Body"]
            d, k = cKDTree(BP).query(AP)
            depth = np.einsum("vc,vc->v", AP - BP[k], BN[k])
            inside = float(((depth < -0.02) & (d < 0.1)).mean())
            res["arms_inside_body_share"] = round(inside, 4)
            if label == "modelled":
                base_inside = inside
            else:
                # Measured against the modelled pose, where the fists already rest on the belly.
                all_ok["collision"] &= inside - base_inside <= LIMITS["inside_share"]
        # Ground.
        lowest = float(min(posed[m["name"]][0][:, 1].min() for m in model.meshes))
        res["lowest_point_m"] = round(lowest, 3)
        if label == "modelled" or anim in STANDING:
            all_ok["alignment"] &= abs(lowest) <= LIMITS["ground"]
        pose_results[label] = res
        previews.append((label, {m["name"]: posed[m["name"]][0] for m in model.meshes}))
    # Facing: the toes ahead of the heels (+Z) and the nose ahead of the head joint.
    allP = np.vstack([b for b in bind.values()])
    height = float(allP[:, 1].max() - allP[:, 1].min())
    hj = model.names.index("Head")
    head = np.linalg.inv(model.ibm[hj])[:3, 3]
    face = allP[(allP[:, 1] > head[1]) & (allP[:, 1] < head[1] + 0.15)]
    facing_z = bool(face[:, 2].max() - head[2] > head[2] - face[:, 2].min())
    report["checks"]["alignment"] = dict(ok=all_ok["alignment"] and facing_z and 1.5 < height < 2.1,
                                         height_m=round(height, 3), facing_plus_z=facing_z,
                                         lowest_per_pose={k: v["lowest_point_m"] for k, v in pose_results.items()})
    report["checks"]["weights"] = dict(ok=all_ok["weights"], limit_share=LIMITS["weights_bad_share"],
                                       per_pose={k: {mn: v[mn]["stretched_share"] for mn in v if isinstance(v[mn], dict)}
                                                 for k, v in pose_results.items()})
    report["checks"]["animation"] = dict(ok=all_ok["animation"], per_pose={k: {mn: v[mn]["thrown"] for mn in v if isinstance(v[mn], dict)}
                                                                           for k, v in pose_results.items()})
    report["checks"]["collision"] = dict(ok=all_ok["collision"], limit_share=LIMITS["inside_share"],
                                         per_pose={k: v.get("arms_inside_body_share") for k, v in pose_results.items()})
    report["checks"]["grip"] = check_grip(model, bind)
    report["poses"] = pose_results
    report["ok"] = all(c["ok"] for c in report["checks"].values())
    with open(os.path.join(out_dir, "report.json"), "w") as f:
        json.dump(report, f, indent=1)
    write_md(report, os.path.join(out_dir, "report.md"))
    if render:
        render_sheet(model, previews, out_dir)
    return report


def check_grip(model, bind):
    """Each closed fist must leave a channel a rod handle (2.5-4 cm across) fits: the hole through the fist, measured
    as the largest empty circle across the hand's vertices around the hand-to-middle-finger axis."""
    if "Arms" not in bind:
        return dict(ok=True, note="no Arms mesh")
    m = next(x for x in model.meshes if x["name"] == "Arms")
    P = bind["Arms"]
    res = {}
    ok = True
    for side in "lr":
        jh = model.names.index(f"hand_{side}")
        sel = m["J"][np.arange(len(P)), np.argmax(m["W"], 1)] == jh
        H = P[sel]
        if len(H) < 50:
            res[side] = "too few hand vertices"
            continue
        c = H.mean(0)
        # The fist's longest extent (knuckles across) is the handle's direction through it.
        u, s, vt = np.linalg.svd(H - c)
        axis = vt[0]
        flat = (H - c) - np.outer((H - c) @ axis, axis)
        # Largest empty circle near the centre, in the plane across the axis.
        e1 = vt[1]
        e2 = vt[2]
        q = np.stack([flat @ e1, flat @ e2], 1)
        tree = cKDTree(q)
        grid = np.stack(np.meshgrid(np.linspace(-0.03, 0.03, 31), np.linspace(-0.03, 0.03, 31)), -1).reshape(-1, 2)
        d, _ = tree.query(grid)
        best = float(d.max() * 2)
        res[side] = dict(hand_vertices=int(sel.sum()), fist_size_m=[round(float(x), 3) for x in s[:3] / np.sqrt(len(H)) * 4],
                         channel_m=round(best, 3))
        ok &= 0.012 <= best <= 0.05
    res["ok"] = ok
    res["note"] = "fists are modelled closed (a channel of 12+ mm is accepted: the handle sinks into the fist); the grip on the real handle: run the game screenshot with FISHSIM_IK_DEBUG=1 and read the GRIP lines"
    return res


def write_md(report, path):
    lines = [f"# Rig QA: {report['id']} — {'PASS' if report['ok'] else 'FAIL'}", ""]
    for name, c in report["checks"].items():
        lines.append(f"## {name}: {'ok' if c['ok'] else 'FAIL'}")
        lines.append("```")
        lines.append(json.dumps({k: v for k, v in c.items() if k != 'ok'}, indent=1)[:3000])
        lines.append("```")
    with open(path, "w") as f:
        f.write("\n".join(lines) + "\n")


# ------------------------------------------------------------------ previews (Blender)
BLENDER_SCRIPT = r'''
import bpy, sys, numpy as np, math
from mathutils import Vector
args = sys.argv[sys.argv.index("--") + 1:]
data = np.load(args[0], allow_pickle=True)
tex, out = args[1], args[2]
labels = list(data["labels"])
bpy.ops.wm.read_factory_settings(use_empty=True)
scn = bpy.context.scene
scn.render.engine = "CYCLES"; scn.cycles.samples = 12; scn.cycles.use_denoising = False
scn.render.resolution_x = 360; scn.render.resolution_y = 520
w = bpy.data.worlds.new("w"); scn.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sun.data.energy = 3.5
sun.rotation_euler = (math.radians(50), 0, math.radians(30)); scn.collection.objects.link(sun)
mat = bpy.data.materials.new("m"); mat.use_nodes = True
t = mat.node_tree.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(tex)
mat.node_tree.links.new(t.outputs[0], mat.node_tree.nodes["Principled BSDF"].inputs[0])
cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); cam.data.type = "ORTHO"; cam.data.ortho_scale = 2.3
scn.collection.objects.link(cam); scn.camera = cam
for li, label in enumerate(labels):
    for o in [o for o in scn.objects if o.type == "MESH"]:
        bpy.data.objects.remove(o)
    parts = data["pose_%d" % li]
    for k in range(len(parts) // 3):
        P, I, UV = parts[3 * k], parts[3 * k + 1], parts[3 * k + 2]
        verts = [(p[0], -p[2], p[1]) for p in P]
        me = bpy.data.meshes.new("m"); me.from_pydata(verts, [], [tuple(t) for t in I]); me.update()
        uvl = me.uv_layers.new()
        loops = np.array([l.vertex_index for l in me.loops])
        uvl.data.foreach_set("uv", np.stack([UV[loops, 0], 1 - UV[loops, 1]], 1).reshape(-1))
        me.materials.append(mat)
        o = bpy.data.objects.new("m", me); scn.collection.objects.link(o)
    for view, d in (("front", Vector((0, -1, 0))), ("side", Vector((1, 0, 0))), ("back", Vector((0, 1, 0)))):
        cam.location = Vector((0, 0, 0.95)) + d * 5
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        scn.render.filepath = "%s/p%02d_%s.png" % (out, li, view)
        bpy.ops.render.render(write_still=True)
'''


def render_sheet(model, previews, out_dir):
    from PIL import Image, ImageDraw
    tmp = tempfile.mkdtemp()
    arrays = {"labels": np.array([p[0] for p in previews])}
    for li, (_, meshes) in enumerate(previews):
        parts = []
        for m in model.meshes:
            parts += [meshes[m["name"]], m["I"], m["UV"] if m["UV"] is not None else np.zeros((len(m["P"]), 2))]
        arrays[f"pose_{li}"] = np.array(parts, dtype=object)
    np.savez(os.path.join(tmp, "poses.npz"), **arrays)
    script = os.path.join(tmp, "render.py")
    with open(script, "w") as f:
        f.write(BLENDER_SCRIPT)
    mat = model.info["materials"]
    slot = next(iter(mat.values()))
    tex = os.path.join(CHARS, "textures", slot["albedo"][0] + ".webp")
    subprocess.run(["blender", "-b", "--python", script, "--", os.path.join(tmp, "poses.npz"), tex, tmp],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    cols, w, h = 3, 360, 520
    sheet = Image.new("RGB", (cols * w, len(previews) * (h + 20)), "white")
    d = ImageDraw.Draw(sheet)
    for li, (label, _) in enumerate(previews):
        for ci, view in enumerate(("front", "side", "back")):
            im = Image.open(os.path.join(tmp, f"p{li:02d}_{view}.png")).convert("RGB")
            sheet.paste(im, (ci * w, li * (h + 20) + 20))
        d.text((6, li * (h + 20) + 4), label, fill=(0, 0, 0))
    sheet.save(os.path.join(out_dir, "sheet.png"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("id", nargs="?", default="player_angler")
    ap.add_argument("--out", default=os.path.join(tempfile.gettempdir(), "rig_qa"))
    ap.add_argument("--no-render", action="store_true")
    args = ap.parse_args()
    report = run(args.id, args.out, not args.no_render)
    for name, c in report["checks"].items():
        print(f"{name:10s} {'ok' if c['ok'] else 'FAIL'}")
    print(f"{'PASS' if report['ok'] else 'FAIL'}: {args.out}/report.md")
    sys.exit(0 if report["ok"] else 1)


if __name__ == "__main__":
    main()
