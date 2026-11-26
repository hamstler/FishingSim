#!/usr/bin/env python3
"""The player's body: the user's Meshy fisherman (assets/models/meshy_fisherman.glb, a static posed figure, fists
closed in front of him) rigged onto the villagers' skeleton, so the game's arm IK and the shared animations move it.

    python3 -I tools/assets/meshy_player_rig.py [--show]

1. Fit: the MakeHuman worker (assets/characters/mh_male_worker.glb: the 65-bone Quaternius skeleton in T-pose with
   skinned body, outfit and shoes) is posed by forward kinematics (a rotation about each joint, a translation and a
   scale) until its surface lies on the fisherman's: least squares of the distances both ways between the two surfaces
   (nearest points, scipy), in stages (trunk and legs, then arms, then everything).
2. Weights: each fisherman vertex takes the blended skin weights of the nearest posed worker vertices facing the same
   way (finger weights go to the hand: the fists are closed for good).
3. Bind pose: the mesh keeps its own pose and the skin's inverse bind matrices are the fitted pose's (the worker's
   T-pose joint frames carried by the fit); the skeleton nodes are the worker's very nodes (rest rotations, joints), so
   the existing animations and World/Props/ArmIK move it unchanged, deforming it only by how far they stray from the
   modelled pose. (Unposed into the T-pose, the shirt Meshy fused to the inner arms stretched into a cape.) Triangles
   bridging an arm and the body below the armpit are cut.
4. Writes assets/characters/player_angler.glb (meshes Arms = upper arms, forearms and hands, shown in first person;
   Body = the rest, surface "body"), player_angler.json and textures/player_angler_body_a.webp / _n.webp.
"""
import argparse
import io
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy.optimize import least_squares
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import quaternius as qt  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ROOT, "assets", "models", "meshy_fisherman.glb")
REF = os.path.join(ROOT, "assets", "characters", "mh_male_worker.glb")
OUT = os.path.join(ROOT, "assets", "characters")
ID = "player_angler"
SCRATCH = os.environ.get("RIG_SCRATCH", "/tmp")

# Joints the fit turns (the others follow their parents).
FIT = ["pelvis", "spine_02", "neck_01", "Head", "clavicle_l", "clavicle_r", "upperarm_l", "upperarm_r", "lowerarm_l",
       "lowerarm_r", "hand_l", "hand_r", "thigh_l", "thigh_r", "calf_l", "calf_r", "foot_l", "foot_r"]
ARM = ("clavicle", "upperarm", "lowerarm", "hand")
FINGERS = ("thumb", "index", "middle", "ring", "pinky")
ARMS_MESH = ("upperarm", "lowerarm", "hand") + FINGERS


# ------------------------------------------------------------------ inputs
def load_reference():
    g = qt.Gltf(REF)
    nodes = g.j["nodes"]
    skin = g.j["skins"][0]
    joints = skin["joints"]
    ibm = g.read(skin["inverseBindMatrices"]).reshape(-1, 4, 4)
    names = [nodes[i]["name"] for i in joints]
    parent_node = {}
    for i, n in enumerate(nodes):
        for c in n.get("children", []):
            parent_node[c] = i
    jindex = {ni: k for k, ni in enumerate(joints)}
    parents = [jindex.get(parent_node.get(ni), -1) for ni in joints]
    rest = np.array([np.linalg.inv(m.T)[:3, 3] for m in ibm])
    P, N, J, W = [], [], [], []
    for n in nodes:
        if "mesh" not in n or n["name"] not in ("Head", "Outfit", "Shoes"):
            continue
        for p in g.j["meshes"][n["mesh"]]["primitives"]:
            a = p["attributes"]
            P.append(g.read(a["POSITION"]).astype(np.float64))
            N.append(g.read(a["NORMAL"]).astype(np.float64))
            J.append(g.read(a["JOINTS_0"]).astype(np.int64))
            w = g.read(a["WEIGHTS_0"]).astype(np.float64)
            W.append(w / np.maximum(w.sum(1, keepdims=True), 1e-9))
    return g, names, parents, rest, np.vstack(P), np.vstack(N), np.vstack(J), np.vstack(W)


def load_meshy():
    g = qt.Gltf(SRC)
    p = g.j["meshes"][0]["primitives"][0]
    a = p["attributes"]
    P = g.read(a["POSITION"]).astype(np.float64)
    N = g.read(a["NORMAL"]).astype(np.float64)
    UV = g.read(a["TEXCOORD_0"]).astype(np.float64)
    I = g.read(p["indices"]).astype(np.int64).reshape(-1, 3)
    images = []
    for img in g.j["images"]:
        bv = g.j["bufferViews"][img["bufferView"]]
        data = g.buf[bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
        images.append(Image.open(io.BytesIO(bytes(data))))
    P[:, 1] -= P[:, 1].min()          # feet on the ground
    return P, N, UV, I, images


# ------------------------------------------------------------------ forward kinematics
class Rig:
    def __init__(self, names, parents, rest):
        self.names, self.parents, self.rest = names, parents, rest
        self.fit = [names.index(n) for n in FIT]

    def globals(self, x):
        """World 4x4 per joint: x = rotation vectors of FIT (3 each), translation (3), log scale (1)."""
        n = len(self.names)
        local = np.tile(np.eye(4), (n, 1, 1))
        rv = x[: 3 * len(self.fit)].reshape(-1, 3)
        R = Rotation.from_rotvec(rv).as_matrix()
        for k, j in enumerate(self.fit):
            p = self.rest[j]
            m = np.eye(4)
            m[:3, :3] = R[k]
            m[:3, 3] = p - R[k] @ p
            local[j] = m
        G = np.empty_like(local)
        for j in range(n):
            pj = self.parents[j]
            G[j] = local[j] if pj < 0 else G[pj] @ local[j]
        t = x[3 * len(self.fit): 3 * len(self.fit) + 3]
        s = np.exp(x[-1])
        top = np.eye(4)
        top[:3, :3] *= s
        top[:3, 3] = t
        return top @ G

    def skin(self, G, P, J, W):
        M = np.einsum("vk,vkab->vab", W, G[J])
        return np.einsum("vab,vb->va", M[:, :3, :3], P) + M[:, :3, 3], M


def initial(rig):
    x = np.zeros(3 * len(FIT) + 4)

    def setj(name, rv):
        k = FIT.index(name)
        x[3 * k: 3 * k + 3] = rv
    # Arms from the T-pose down to the sides, forearms forward to the fists in front of the belly.
    setj("upperarm_l", [0, 0, -1.25])
    setj("upperarm_r", [0, 0, 1.25])
    setj("lowerarm_l", [0.0, -1.2, 0.0])
    setj("lowerarm_r", [0.0, 1.2, 0.0])
    return x


def fit(rig, ref_P, ref_N, ref_J, ref_W, Q, QN, show):
    rng = np.random.default_rng(1)
    sv = rng.choice(len(ref_P), min(4000, len(ref_P)), replace=False)
    sq = rng.choice(len(Q), min(5000, len(Q)), replace=False)
    Pv, Jv, Wv = ref_P[sv], ref_J[sv], ref_W[sv]
    Qs = Q[sq]
    qtree = cKDTree(Qs)
    x = initial(rig)
    # Put the reference's middle over the fisherman's.
    x[-4:-1] = [np.median(Q[:, 0]), 0.0, np.median(Q[:, 2]) - np.median(ref_P[:, 2])]

    def residual(xx, free):
        full = x.copy()
        full[free] = xx
        G = rig.globals(full)
        V, _ = rig.skin(G, Pv, Jv, Wv)
        d1, _ = qtree.query(V)
        vtree = cKDTree(V)
        d2, _ = vtree.query(Qs)
        # Robust (soft L1 in least_squares) both ways; a weak pull of each rotation to rest keeps twists sane.
        reg = full[: 3 * len(FIT)] * 0.02
        return np.concatenate([d1 / np.sqrt(len(d1)), d2 / np.sqrt(len(d2)), reg])

    def stage(names, extra=True):
        free = []
        for n in names:
            k = FIT.index(n)
            free += [3 * k, 3 * k + 1, 3 * k + 2]
        if extra:
            free += list(range(3 * len(FIT), 3 * len(FIT) + 4))
        free = np.array(free)
        r = least_squares(residual, x[free], args=(free,), loss="soft_l1", f_scale=0.02, diff_step=1e-3, max_nfev=60 * len(free))
        x[free] = r.x
        G = rig.globals(x)
        V, _ = rig.skin(G, Pv, Jv, Wv)
        print(f"  stage {names[:3]}...: mean distance {qtree.query(V)[0].mean() * 100:.2f} cm, "
              f"back {cKDTree(V).query(Qs)[0].mean() * 100:.2f} cm, scale {np.exp(x[-1]):.3f}", flush=True)
    stage(["pelvis", "spine_02", "thigh_l", "thigh_r", "calf_l", "calf_r"])
    stage(["clavicle_l", "upperarm_l", "lowerarm_l", "hand_l", "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r"], extra=False)
    stage(["neck_01", "Head", "foot_l", "foot_r"], extra=False)
    stage(FIT)
    return x


# ------------------------------------------------------------------ weights and rest pose
def transfer_weights(rig, G, ref_P, ref_N, ref_J, ref_W, P, N, names, I, fold_fingers=True):
    """Skin weights for the fisherman from the posed reference: inverse-distance blend of the nearest reference
    vertices whose normals face the same way (an arm's inner side never takes the torso's weights), smoothed over the
    welded mesh, fingers folded into the hand, four strongest kept."""
    V, M = rig.skin(G, ref_P, ref_J, ref_W)
    VN = np.einsum("vab,vb->va", M[:, :3, :3], ref_N)
    VN /= np.maximum(np.linalg.norm(VN, axis=1, keepdims=True), 1e-9)
    n = len(names)
    dense_ref = np.zeros((len(ref_P), n))
    np.put_along_axis(dense_ref, ref_J, ref_W, axis=1)
    # Which part each vertex belongs to (left arm, right arm, the rest): the bone axis it lies closest to measured in
    # that part's own thickness. Plain nearest points gave the loose shirt's sides under the armpits to the hanging
    # arms, which then lifted them like a cape.
    joints = np.array([G[j][:3, :3] @ rig.rest[j] + G[j][:3, 3] for j in range(n)])
    group_of_joint = np.array([0 if not nm.startswith(ARM[1:] + FINGERS) else (1 if nm.endswith("_l") else 2) for nm in names])
    ref_group = group_of_joint[ref_J[np.arange(len(ref_J)), np.argmax(ref_W, axis=1)]]
    segments = []
    for a_name, b_name in (("pelvis", "spine_02"), ("spine_02", "neck_01"), ("neck_01", "Head"), ("thigh_l", "calf_l"),
                           ("calf_l", "foot_l"), ("thigh_r", "calf_r"), ("calf_r", "foot_r"), ("pelvis", "thigh_l"),
                           ("pelvis", "thigh_r"), ("spine_02", "clavicle_l"), ("spine_02", "clavicle_r"),
                           ("upperarm_l", "lowerarm_l"), ("lowerarm_l", "hand_l"), ("hand_l", "middle_01_l"),
                           ("upperarm_r", "lowerarm_r"), ("lowerarm_r", "hand_r"), ("hand_r", "middle_01_r")):
        ia, ib = names.index(a_name), names.index(b_name)
        segments.append((joints[ia], joints[ib], group_of_joint[ib] if b_name.startswith(ARM[1:]) or a_name.startswith(ARM[1:]) else 0))

    def seg_dist(X, a, b):
        ab = b - a
        t = np.clip(((X - a) @ ab) / max(ab @ ab, 1e-9), 0.0, 1.0)
        return np.linalg.norm(X - (a + t[:, None] * ab), axis=1)
    # Each segment's thickness: the median distance of the reference surface that belongs to it (nearest segment).
    ref_d = np.stack([seg_dist(V, a, b) for a, b, _ in segments], 1)
    ref_seg = np.argmin(ref_d, 1)
    radius = np.array([max(np.median(ref_d[ref_seg == k, k]) if np.any(ref_seg == k) else 0.05, 0.03) for k in range(len(segments))])
    my_d = np.stack([seg_dist(P, a, b) for a, b, _ in segments], 1) / radius[None, :]
    my_group = np.array([g for _, _, g in segments])[np.argmin(my_d, 1)]
    # Majority of the neighbours over the welded mesh: stray patches (the forearms' undersides lie against the belly,
    # nearer the spine than their own bones by thickness) join the part around them; they had left holes in the arms.
    key = np.round(P * 1e5).astype(np.int64)
    _, wv = np.unique(key, axis=0, return_inverse=True)
    wv = wv.reshape(-1)
    nw = wv.max() + 1
    edges = np.concatenate([wv[I][:, [0, 1]], wv[I][:, [1, 2]], wv[I][:, [2, 0]]])
    edges = np.concatenate([edges, edges[:, ::-1]])
    lab = np.zeros(nw, np.int64)
    lab[wv] = my_group
    for _ in range(12):
        votes = np.zeros((nw, 3))
        np.add.at(votes, (edges[:, 0], lab[edges[:, 1]]), 1.0)
        votes[np.arange(nw), lab] += 1.0
        best = np.argmax(votes, 1)
        frac = votes[np.arange(nw), best] / np.maximum(votes.sum(1), 1)
        lab = np.where(frac > 0.6, best, lab)
    my_group = lab[wv]
    dense = np.zeros((len(P), n))
    for grp in (0, 1, 2):
        mine = np.where(my_group == grp)[0]
        theirs = np.where(ref_group == grp)[0]
        if len(mine) == 0 or len(theirs) == 0:
            continue
        tree = cKDTree(V[theirs])
        d, idx = tree.query(P[mine], k=12)
        idx = theirs[idx]
        facing = np.einsum("vkc,vc->vk", VN[idx], N[mine]) > 0.2
        facing[~facing.any(1), 0] = True          # nothing faces this way: the nearest one
        w = np.where(facing, 1.0 / np.maximum(d, 1e-4) ** 2, 0.0)
        w /= w.sum(1, keepdims=True)
        dense[mine] = np.einsum("vk,vkj->vj", w, dense_ref[idx])
    print(f"  parts: body {np.sum(my_group == 0)}, left arm {np.sum(my_group == 1)}, right arm {np.sum(my_group == 2)} vertices", flush=True)
    transfer_weights.group = my_group
    transfer_weights.armpit = min(joints[names.index("upperarm_l")][1], joints[names.index("upperarm_r")][1]) - 0.06
    # Fingers into the hand: the fists stay closed (an open-handed figure keeps its finger weights).
    for j, name in enumerate(names):
        if fold_fingers and name.startswith(FINGERS):
            side = name[-1]
            dense[:, names.index(f"hand_{side}")] += dense[:, j]
            dense[:, j] = 0.0
    # Weld the UV-split vertices by position and smooth twice over the triangles.
    key = np.round(P * 1e5).astype(np.int64)
    _, weld = np.unique(key, axis=0, return_inverse=True)
    weld = weld.reshape(-1)
    return dense, weld


def smooth(dense, weld, I, rounds=2):
    nw = weld.max() + 1
    for _ in range(rounds):
        acc = np.zeros((nw, dense.shape[1]))
        cnt = np.zeros(nw)
        np.add.at(acc, weld, dense)
        np.add.at(cnt, weld, 1.0)
        welded = acc / cnt[:, None]
        # Neighbours through the triangles (welded).
        tri = weld[I]
        nb = np.zeros_like(welded)
        nc = np.zeros(nw)
        for a, b in ((0, 1), (1, 2), (2, 0), (1, 0), (2, 1), (0, 2)):
            np.add.at(nb, tri[:, a], welded[tri[:, b]])
            np.add.at(nc, tri[:, a], 1.0)
        welded = 0.5 * welded + 0.5 * nb / np.maximum(nc, 1)[:, None]
        dense = welded[weld]
    return dense


def top4(dense):
    J = np.argsort(-dense, axis=1)[:, :4]
    W = np.take_along_axis(dense, J, axis=1)
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    return J, W


def unpose(G, P, N, J, W):
    """Back through each vertex's blended joint transform: the fisherman in the skeleton's T-pose."""
    M = np.einsum("vk,vkab->vab", W, G[J])
    inv = np.linalg.inv(M)
    R = np.einsum("vab,vb->va", inv[:, :3, :3], P) + inv[:, :3, 3]
    RN = np.einsum("vba,vb->va", M[:, :3, :3], N)          # inverse transpose of the inverse
    RN /= np.maximum(np.linalg.norm(RN, axis=1, keepdims=True), 1e-9)
    return R, RN


def fix_forearm_texels(G, rig, names, P, UV, I, J, picture):
    """Meshy baked bits of the trousers' colour onto the outer side of a bare forearm (the fists rested by the hips):
    triangles of the forearms and hands beyond the rolled sleeves whose texels are not skin-coloured take the UV of the
    nearest skin-coloured vertex of the same arm."""
    tex = np.asarray(picture.convert("RGB")).astype(np.float32)
    h, w = tex.shape[:2]
    UV = UV.copy()
    c = tex[np.clip((UV[:, 1] * h).astype(int), 0, h - 1), np.clip((UV[:, 0] * w).astype(int), 0, w - 1)]
    skin = (c[:, 0] > c[:, 1]) & (c[:, 1] > c[:, 2]) & (c[:, 0] - c[:, 2] > 20)
    olive = c[:, 1] >= c[:, 0] - 6          # the trousers' grey-green; skin (also its creases) is redder
    fixed = 0
    for side in "lr":
        jl, jh = names.index(f"lowerarm_{side}"), names.index(f"hand_{side}")
        elbow = G[jl][:3, :3] @ rig.rest[jl] + G[jl][:3, 3]
        wrist = G[jh][:3, :3] @ rig.rest[jh] + G[jh][:3, 3]
        along = ((P - elbow) @ (wrist - elbow)) / max(np.dot(wrist - elbow, wrist - elbow), 1e-9)
        arm = np.isin(J[:, 0], [jl, jh]) & (along > 0.3)
        good = np.where(arm & skin)[0]
        # On the hands every texel should be skin (Meshy put shirt checks on a few fingers); on the forearms only the
        # trousers' colour is wrong (skin creases and the sleeves' shade are not).
        wrong = np.where(J[:, 0] == jh, ~skin, olive)
        bad_tri = np.where(arm[I].all(1) & wrong[I].any(1))[0]
        if len(good) == 0 or len(bad_tri) == 0:
            continue
        tree = cKDTree(P[good])
        _, k = tree.query(P[I[bad_tri]].mean(1))
        for t, g in zip(bad_tri, good[k]):
            UV[I[t]] = UV[g]
        fixed += len(bad_tri)
    print(f"  {fixed} forearm/hand triangles moved off trouser-coloured texels", flush=True)
    return UV


# ------------------------------------------------------------------ output
def islands_mask(UV, I, size):
    """Where the mesh's UV islands lie in the picture (triangles rasterised, grown by 2 px)."""
    from PIL import ImageDraw, ImageFilter
    mask = Image.new("L", size, 0)
    d = ImageDraw.Draw(mask)
    w, h = size
    for t in I:
        d.polygon([(float(UV[k, 0] * w), float(UV[k, 1] * h)) for k in t], fill=255)
    return mask.filter(ImageFilter.MaxFilter(5))


def save_picture(img, name, normal=False, size=2048, mask=None):
    img = img.convert("RGB")
    if mask is not None:
        # The gaps between the UV islands take the islands' colour (push-pull, as for the Meshy fish): the mipmaps
        # drew dark green seams across the forearms in the game.
        import meshy_fish
        img = meshy_fish.fill_gaps(img, Image.merge("RGB", (mask.resize(img.size),) * 3))
    if not normal:
        # Skin a little darker and warmer: Meshy painted it pale, and the forearms right in front of the eyes, turned
        # up to the noon sun, read almost white in the game.
        a = np.asarray(img, dtype=np.float32)
        skin = (a[..., 0] > a[..., 1]) & (a[..., 1] > a[..., 2]) & (a[..., 0] - a[..., 2] > 25) & (a[..., 0] > 90)
        soft = skin.astype(np.float32)
        from PIL import ImageFilter
        soft = np.asarray(Image.fromarray((soft * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2)), np.float32)[..., None] / 255
        graded = a * np.array([0.84, 0.76, 0.7], np.float32)
        img = Image.fromarray(np.clip(a * (1 - soft) + graded * soft, 0, 255).astype(np.uint8))
    if max(img.size) > size:
        img = img.resize((size, size), Image.LANCZOS)
    path = os.path.join(OUT, "textures", f"{name}.webp")
    img.save(path, "WEBP", quality=88 if normal else 85, method=6)
    qt.keep(path)
    return name


ARMS_SRC = "mh_male_casual"       # long sleeves to the wrist (male_casualsuit05) and MakeHuman hands


def fp_arms(G):
    """mh_male_casual's hands and forearms (its skin) and its sleeves (outfit), moved onto the worker's skeleton
    (quaternius.rebind: same 65 joints, the worker's T-pose) and then into the fitted pose like the rest of the body.
    Yields ((positions, normals, uv, joints, weights, triangles), material index)."""
    ref = qt.Gltf(REF)
    dst_names, dst_ibm = ref.joints()
    src = qt.Gltf(os.path.join(OUT, f"{ARMS_SRC}.glb"))
    src_names, src_ibm = src.joints()
    rest = np.array([np.linalg.inv(m)[:3, 3] for m in dst_ibm])
    shoulder = {s: rest[dst_names.index(f"upperarm_{s}")] for s in "lr"}
    elbow = {s: rest[dst_names.index(f"lowerarm_{s}")] for s in "lr"}
    keep_joint = ("lowerarm", "hand") + FINGERS
    for node_name, material in (("Head", 1), ("Outfit", 2)):
        node = next(n for n in src.j["nodes"] if n.get("name") == node_name and "mesh" in n)     # (a joint is called Head too)
        prim = src.j["meshes"][node["mesh"]]["primitives"][0]
        a = prim["attributes"]
        P = src.read(a["POSITION"]).astype(np.float64)
        N = src.read(a["NORMAL"]).astype(np.float64)
        UV = src.read(a["TEXCOORD_0"]).astype(np.float64)
        J = src.read(a["JOINTS_0"]).astype(np.int64)
        W = src.read(a["WEIGHTS_0"]).astype(np.float64)
        W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
        I = src.read(prim["indices"]).astype(np.int64).reshape(-1, 3)
        P, N, J = qt.rebind(P, N, J, W, src_names, src_ibm, dst_names, dst_ibm)
        dom = [dst_names[j] for j in J[np.arange(len(J)), np.argmax(W, 1)]]
        keep = np.array([d.startswith(keep_joint) for d in dom])
        if material == 2:
            # The sleeve from just above the elbow (the shoulder end swings far under the IK, right by the camera).
            for side in "lr":
                up = np.array([d == f"upperarm_{side}" for d in dom])
                along = ((P - shoulder[side]) @ (elbow[side] - shoulder[side])) / np.dot(elbow[side] - shoulder[side], elbow[side] - shoulder[side])
                keep |= up & (along > 0.55)
        tri = I[keep[I].all(1)]
        used, inv = np.unique(tri.reshape(-1), return_inverse=True)
        tri = inv.reshape(-1, 3)
        P, N, UV, J, W = P[used], N[used], UV[used], J[used], W[used]
        # Into the fitted pose (the skin's bind pose).
        M = np.einsum("vk,vkab->vab", W, G[J])
        P = np.einsum("vab,vb->va", M[:, :3, :3], P) + M[:, :3, 3]
        N = np.einsum("vab,vb->va", M[:, :3, :3], N)
        N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
        yield (P, N, UV, J, W, tri), material


def flannel(name, meshy_picture, size=1024, repeats=32):
    """A tileable flannel check in the Meshy shirt's colours (navy ground, brown and rust bands, thin cream lines),
    woven: each thread colour shows where its warp or weft lies on top (a 2/2 twill), with a soft fibre noise."""
    navy, brown, rust, cream = (np.array(c, np.float32) for c in ((34, 38, 56), (70, 46, 34), (148, 78, 44), (150, 136, 112)))
    cell = size // repeats
    def stripes(n):
        t = (np.arange(n) % cell) / cell
        col = np.empty((n, 3), np.float32)
        col[:] = navy
        col[(t > 0.08) & (t < 0.38)] = brown
        col[(t > 0.18) & (t < 0.28)] = rust
        col[(np.abs(t - 0.62) < 0.012) | (np.abs(t - 0.7) < 0.012)] = cream
        col[(t > 0.78) & (t < 0.86)] = brown * 0.8 + navy * 0.2
        return col
    warp = stripes(size)[None, :, :].repeat(size, 0)
    weft = stripes(size)[:, None, :].repeat(size, 1)
    y, x = np.mgrid[0:size, 0:size]
    twill = ((x + y) // 2) % 2 == 0
    img = np.where(twill[..., None], warp, weft) * 0.62 + (warp + weft) * 0.19
    rng = np.random.default_rng(3)
    img *= (0.92 + 0.16 * rng.random((size, size, 1))).astype(np.float32)
    path = os.path.join(OUT, "textures", f"{name}.webp")
    Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(path, "WEBP", quality=88, method=6)
    qt.keep(path)
    return name


def write(names, G, R, RN, UV, I, J, W, images, group, armpit):
    """The mesh stays in its own pose; the skin's inverse bind matrices are the fitted pose's (each joint's T-pose frame
    carried by the fit), so the animations and the arm IK deform it only by how far they stray from that pose. Unposing
    it into the T-pose instead stretched the shirt Meshy fused to the inner arms into a cape."""
    ref = qt.Gltf(REF)
    w = qt.Writer()
    nodes, armature, remap = qt.skeleton_scene(ref)
    skin_src = ref.j["skins"][0]
    ibm = ref.read(skin_src["inverseBindMatrices"]).reshape(-1, 4, 4)          # stored column-major
    new_ibm = np.array([(m.T @ np.linalg.inv(G[k])).T for k, m in enumerate(ibm)], np.float32)
    skin = {"joints": [remap[i] for i in skin_src["joints"]], "inverseBindMatrices": w.add(new_ibm.reshape(-1, 16), "MAT4", 5126)}
    # Triangles bridging an arm and the body below the armpit (the shirt fused to the inner arm) are cut.
    tg = group[I]
    bridge = (tg.min(1) != tg.max(1)) & (R[I][:, :, 1].max(1) < armpit)
    print(f"  cut {int(bridge.sum())} arm-to-body bridging triangles", flush=True)
    I = I[~bridge]
    # Meshy's triangles wind either way (its material is two-sided); one-sided in the game the flipped ones were holes.
    # Wind each so its face normal agrees with its vertex normals (glTF counter-clockwise = front).
    fn = np.cross(R[I[:, 1]] - R[I[:, 0]], R[I[:, 2]] - R[I[:, 0]])
    flip = np.einsum("tc,tc->t", fn, RN[I].sum(1)) < 0
    I = I.copy()
    I[flip] = I[flip][:, ::-1]
    print(f"  {int(flip.sum())} of {len(I)} triangles turned to face out", flush=True)
    # Arms (shown in first person) and the rest of the body, split by each triangle's strongest joint.
    # Only the forearms, the hands and the elbow end of the upper arms: the shoulder end swings far from the modelled
    # pose under the arm IK (the rod held up in front) and stretched right beside the camera.
    arm_joint = np.array([n.startswith(ARMS_MESH) for n in names])
    vert_arm = arm_joint[J[:, 0]]
    rest_world = [np.linalg.inv(m.T) for m in ref.read(ref.j["skins"][0]["inverseBindMatrices"]).reshape(-1, 4, 4)]
    for side in "lr":
        ju, jl = names.index(f"upperarm_{side}"), names.index(f"lowerarm_{side}")
        shoulder = (G[ju] @ rest_world[ju])[:3, 3]
        elbow = (G[jl] @ rest_world[jl])[:3, 3]
        along = ((R - shoulder) @ (elbow - shoulder)) / max(np.dot(elbow - shoulder, elbow - shoulder), 1e-9)
        vert_arm &= ~((J[:, 0] == ju) & (along < 0.45))
    tri_arm = vert_arm[I].sum(1) >= 2
    meshes = []
    for mesh_name, sel in (("Arms_Meshy", tri_arm), ("Body", ~tri_arm)):
        tris = I[sel]
        used, inv = np.unique(tris.reshape(-1), return_inverse=True)
        tri = inv.reshape(-1, 3).astype(np.uint32)
        P, Nn, U = R[used].astype(np.float32), RN[used].astype(np.float32), UV[used].astype(np.float32)
        T = qt.tangents(P.astype(np.float64), Nn.astype(np.float64), U.astype(np.float64), tri.astype(np.int64))
        prim = {"attributes": {"POSITION": w.add(P, "VEC3", 5126, 34962, minmax=True),
                               "NORMAL": w.add(Nn, "VEC3", 5126, 34962),
                               "TANGENT": w.add(T.astype(np.float32), "VEC4", 5126, 34962),
                               "TEXCOORD_0": w.add(U, "VEC2", 5126, 34962),
                               "JOINTS_0": w.add(J[used].astype(np.uint8), "VEC4", 5121, 34962),
                               "WEIGHTS_0": w.add(W[used].astype(np.float32), "VEC4", 5126, 34962)},
                "indices": w.add(tri.reshape(-1), "SCALAR", 5125, 34963), "material": 0}
        meshes.append({"name": mesh_name, "primitives": [prim]})
        nodes.append({"name": mesh_name, "mesh": len(meshes) - 1, "skin": 0})
        nodes[armature].setdefault("children", []).append(len(nodes) - 1)
        print(f"  {mesh_name}: {len(used)} vertices, {len(tri)} triangles", flush=True)
    # The arms seen in first person: MakeHuman hands with real finger bones (the arm IK closes them round the handle
    # and the crank knob) in long flannel sleeves; Meshy's fists are closed for good and could not hold anything.
    prims = []
    for part, material in fp_arms(G):
        P, Nn, U, Jj, Ww, tri = part
        T = qt.tangents(P.astype(np.float64), Nn.astype(np.float64), U.astype(np.float64), tri.astype(np.int64))
        prims.append({"attributes": {"POSITION": w.add(P.astype(np.float32), "VEC3", 5126, 34962, minmax=True),
                                     "NORMAL": w.add(Nn.astype(np.float32), "VEC3", 5126, 34962),
                                     "TANGENT": w.add(T.astype(np.float32), "VEC4", 5126, 34962),
                                     "TEXCOORD_0": w.add(U.astype(np.float32), "VEC2", 5126, 34962),
                                     "JOINTS_0": w.add(Jj.astype(np.uint8), "VEC4", 5121, 34962),
                                     "WEIGHTS_0": w.add(Ww.astype(np.float32), "VEC4", 5126, 34962)},
                      "indices": w.add(tri.reshape(-1).astype(np.uint32), "SCALAR", 5125, 34963), "material": material})
        print(f"  Arms ({['body', 'skin', 'outfit'][material]}): {len(P)} vertices, {len(tri)} triangles", flush=True)
    meshes.append({"name": "Arms", "primitives": prims})
    nodes.append({"name": "Arms", "mesh": len(meshes) - 1, "skin": 0})
    nodes[armature].setdefault("children", []).append(len(nodes) - 1)
    roots = [i for i, n in enumerate(nodes) if n.get("name") == "Armature"]
    gltf = {"asset": {"version": "2.0", "generator": "FishingSim tools/assets/meshy_player_rig.py"}, "scene": 0,
            "scenes": [{"name": ID, "nodes": roots}], "nodes": nodes, "meshes": meshes, "skins": [skin],
            "materials": [{"name": "body"}, {"name": "skin"}, {"name": "outfit"}]}
    path = os.path.join(OUT, f"{ID}.glb")
    w.save(path, gltf)
    mask = islands_mask(UV, I, images[0].size)
    entry = {"roughness_value": 0.8, "albedo": [save_picture(images[0], f"{ID}_body_a", mask=mask)]}
    if len(images) > 1:
        entry["normal"] = save_picture(images[1], f"{ID}_body_n", normal=True, mask=mask)
    worker = json.load(open(os.path.join(OUT, "mh_male_worker.json")))
    casual = json.load(open(os.path.join(OUT, f"{ARMS_SRC}.json")))["materials"]
    sleeve = {"roughness_value": 0.9, "albedo": [flannel(f"{ID}_sleeve_a", images[0])]}
    if "normal" in casual["outfit"]:
        sleeve["normal"] = casual["outfit"]["normal"]
    info = {"id": ID, "sex": "M", "hair": [], "materials": {"body": entry, "skin": casual["skin"], "outfit": sleeve},
            "pelvis_scale": worker.get("pelvis_scale", 1.0),
            "author": "the FishingSim author (made with Meshy AI on a paid plan), rigged by tools/assets/meshy_player_rig.py",
            "licence": "All rights reserved, Krystian Naskręski (part of FishingSim)", "skeleton": "mh_male_worker (MakeHuman CC0 / Quaternius CC0)"}
    with open(os.path.join(OUT, f"{ID}.json"), "w") as f:
        json.dump(info, f, indent=1, ensure_ascii=False)
    print(f"{path}: {os.path.getsize(path) / 1e6:.2f} MB", flush=True)


# ------------------------------------------------------------------ main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--show", action="store_true", help="write fit pictures into RIG_SCRATCH")
    ap.add_argument("--reuse", action="store_true", help="reuse the last fit (RIG_SCRATCH/fit_x.npy)")
    args = ap.parse_args()
    g, names, parents, rest, ref_P, ref_N, ref_J, ref_W = load_reference()
    P, N, UV, I, images = load_meshy()
    rig = Rig(names, parents, rest)
    print(f"fisherman {len(P)} vertices, {len(I)} triangles, {P[:, 1].max():.2f} m; reference {len(ref_P)} vertices", flush=True)
    cached = os.path.join(SCRATCH, "fit_x.npy")
    if args.reuse and os.path.exists(cached):
        x = np.load(cached)
    else:
        x = fit(rig, ref_P, ref_N, ref_J, ref_W, P, N, args.show)
        np.save(cached, x)
    G = rig.globals(x)
    dense, weld = transfer_weights(rig, G, ref_P, ref_N, ref_J, ref_W, P, N, names, I)
    dense = smooth(dense, weld, I)
    J, W = top4(dense)
    UV = fix_forearm_texels(G, rig, names, P, UV, I, J, images[0])
    write(names, G, P, N, UV, I, J, W, images, transfer_weights.group, transfer_weights.armpit)


if __name__ == "__main__":
    main()
