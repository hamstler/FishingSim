#!/usr/bin/env python3
"""Rigs an open-handed Meshy figure in A-pose (the user's new fisherman: no textures, fingers apart) onto the villagers'
skeleton, fingers included, so its hands can close round the rod's handle and the crank's knob.

The same fit as meshy_player_rig.py (the MakeHuman worker posed by forward kinematics onto the figure's surface), started
from the A-pose and followed by a stage that bends every finger joint of the worker's hands onto the figure's fingers;
the weights keep the fingers' own bones (meshy_player_rig folds them into the hand for its closed fists). The mesh stays
in its own pose and the skin's inverse bind matrices are the fitted pose's, as in meshy_player_rig.

    python -I tools/assets/meshy_apose_rig.py FIGURE.glb OUT.glb [--reuse]

RIG_SCRATCH holds the fit (apose_fit_x.npy) for --reuse.
"""
import argparse
import os
import sys

import numpy as np
from scipy.optimize import least_squares
from scipy.spatial import cKDTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import meshy_player_rig as mpr  # noqa: E402
import quaternius as qt  # noqa: E402

FINGER_JOINTS = [f"{f}_{k:02d}_{s}" for s in "lr" for f in mpr.FINGERS for k in (1, 2, 3)]


def load_figure(path):
    g = qt.Gltf(path)
    p = g.j["meshes"][0]["primitives"][0]
    a = p["attributes"]
    P = g.read(a["POSITION"]).astype(np.float64)
    N = g.read(a["NORMAL"]).astype(np.float64)
    I = g.read(p["indices"]).astype(np.int64).reshape(-1, 3)
    P[:, 1] -= P[:, 1].min()
    return P, N, I


class FingerRig(mpr.Rig):
    """mpr.Rig turning the fingers' joints too."""

    def __init__(self, names, parents, rest, fit_names):
        self.names, self.parents, self.rest = names, parents, rest
        self.fit_names = fit_names
        self.fit = [names.index(n) for n in fit_names]


def fit(rig, ref_P, ref_J, ref_W, Q):
    rng = np.random.default_rng(1)
    sv = rng.choice(len(ref_P), min(6000, len(ref_P)), replace=False)
    sq = rng.choice(len(Q), min(8000, len(Q)), replace=False)
    Pv, Jv, Wv = ref_P[sv], ref_J[sv], ref_W[sv]
    Qs = Q[sq]
    qtree = cKDTree(Qs)
    names = rig.fit_names
    x = np.zeros(3 * len(names) + 4)

    def setj(name, rv):
        k = names.index(name)
        x[3 * k: 3 * k + 3] = rv
    # from the worker's T-pose: the arms about 45 degrees down (the A-pose)
    setj("upperarm_l", [0, 0, -0.8])
    setj("upperarm_r", [0, 0, 0.8])
    x[-4:-1] = [np.median(Q[:, 0]), 0.0, np.median(Q[:, 2]) - np.median(ref_P[:, 2])]

    def residual(xx, free, pv, jv, wv, qs, tree):
        full = x.copy()
        full[free] = xx
        G = rig.globals(full)
        V, _ = rig.skin(G, pv, jv, wv)
        d1, _ = tree.query(V)
        d2, _ = cKDTree(V).query(qs)
        reg = full[: 3 * len(names)] * 0.02
        return np.concatenate([d1 / np.sqrt(len(d1)), d2 / np.sqrt(len(d2)), reg])

    def stage(stage_names, extra=True, hands=None):
        free = []
        for n in stage_names:
            k = names.index(n)
            free += [3 * k, 3 * k + 1, 3 * k + 2]
        if extra:
            free += list(range(3 * len(names), 3 * len(names) + 4))
        free = np.array(free)
        pv, jv, wv, qs, tree = Pv, Jv, Wv, Qs, qtree
        if hands is not None:
            # only the hands: the worker's hand and finger vertices and the figure's points near the fitted hands
            G = rig.globals(x)
            hand_j = [rig.names.index(n) for n in rig.names if n.startswith(("hand",) + mpr.FINGERS)]
            mine = np.isin(ref_J[np.arange(len(ref_J)), np.argmax(ref_W, 1)], hand_j)
            pv, jv, wv = ref_P[mine], ref_J[mine], ref_W[mine]
            V, _ = rig.skin(G, pv, jv, wv)
            near = cKDTree(V).query(Q)[0] < 0.03
            qs = Q[near][rng.choice(int(near.sum()), min(9000, int(near.sum())), replace=False)]
            pick = rng.choice(len(pv), min(6000, len(pv)), replace=False)
            pv, jv, wv = pv[pick], jv[pick], wv[pick]
            tree = cKDTree(qs)
        r = least_squares(residual, x[free], args=(free, pv, jv, wv, qs, tree), loss="soft_l1", f_scale=0.01,
                          diff_step=1e-3, max_nfev=40 * len(free))
        x[free] = r.x
        G = rig.globals(x)
        V, _ = rig.skin(G, pv, jv, wv)
        print(f"  stage {stage_names[:3]}...: mean distance {tree.query(V)[0].mean() * 100:.2f} cm, "
              f"back {cKDTree(V).query(qs)[0].mean() * 100:.2f} cm, scale {np.exp(x[-1]):.3f}", flush=True)

    stage(["pelvis", "spine_02", "thigh_l", "thigh_r", "calf_l", "calf_r"])
    stage(["clavicle_l", "upperarm_l", "lowerarm_l", "hand_l", "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r"], extra=False)
    stage(["neck_01", "Head", "foot_l", "foot_r"], extra=False)
    stage(mpr.FIT)
    for side in "lr":
        stage([f"hand_{side}"] + [n for n in FINGER_JOINTS if n.endswith(side)], extra=False, hands=side)
    return x


def write(path, names, G, P, N, I, J, W):
    ref = qt.Gltf(mpr.REF)
    w = qt.Writer()
    nodes, armature, remap = qt.skeleton_scene(ref)
    skin_src = ref.j["skins"][0]
    ibm = ref.read(skin_src["inverseBindMatrices"]).reshape(-1, 4, 4)
    new_ibm = np.array([(m.T @ np.linalg.inv(G[k])).T for k, m in enumerate(ibm)], np.float32)
    skin = {"joints": [remap[i] for i in skin_src["joints"]], "inverseBindMatrices": w.add(new_ibm.reshape(-1, 16), "MAT4", 5126)}
    fn = np.cross(P[I[:, 1]] - P[I[:, 0]], P[I[:, 2]] - P[I[:, 0]])
    flip = np.einsum("tc,tc->t", fn, N[I].sum(1)) < 0
    I = I.copy()
    I[flip] = I[flip][:, ::-1]
    prim = {"attributes": {"POSITION": w.add(P.astype(np.float32), "VEC3", 5126, 34962, minmax=True),
                           "NORMAL": w.add(N.astype(np.float32), "VEC3", 5126, 34962),
                           "JOINTS_0": w.add(J.astype(np.uint8), "VEC4", 5121, 34962),
                           "WEIGHTS_0": w.add(W.astype(np.float32), "VEC4", 5126, 34962)},
            "indices": w.add(I.reshape(-1).astype(np.uint32), "SCALAR", 5125, 34963), "material": 0}
    nodes.append({"name": "Body", "mesh": 0, "skin": 0})
    nodes[armature].setdefault("children", []).append(len(nodes) - 1)
    roots = [i for i, n in enumerate(nodes) if n.get("name") == "Armature"]
    gltf = {"asset": {"version": "2.0", "generator": "FishingSim tools/assets/meshy_apose_rig.py"}, "scene": 0,
            "scenes": [{"name": "angler", "nodes": roots}], "nodes": nodes, "meshes": [{"name": "Body", "primitives": [prim]}],
            "skins": [skin], "materials": [{"name": "body", "pbrMetallicRoughness": {"baseColorFactor": [0.8, 0.65, 0.55, 1], "metallicFactor": 0, "roughnessFactor": 0.7}}]}
    w.save(path, gltf)
    print(f"{path}: {os.path.getsize(path) / 1e6:.2f} MB", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("figure")
    ap.add_argument("out")
    ap.add_argument("--reuse", action="store_true")
    args = ap.parse_args()
    g, names, parents, rest, ref_P, ref_N, ref_J, ref_W = mpr.load_reference()
    P, N, I = load_figure(args.figure)
    rig = FingerRig(names, parents, rest, mpr.FIT + FINGER_JOINTS)
    print(f"figure {len(P)} vertices, {len(I)} triangles, {P[:, 1].max():.2f} m; reference {len(ref_P)} vertices", flush=True)
    cached = os.path.join(mpr.SCRATCH, "apose_fit_x.npy")
    if args.reuse and os.path.exists(cached):
        x = np.load(cached)
    else:
        x = fit(rig, ref_P, ref_J, ref_W, P)
        np.save(cached, x)
    G = rig.globals(x)
    dense, weld = mpr.transfer_weights(rig, G, ref_P, ref_N, ref_J, ref_W, P, N, names, I, fold_fingers=False)
    dense = mpr.smooth(dense, weld, I)
    J, W = mpr.top4(dense)
    write(args.out, names, G, P, N, I, J, W)


if __name__ == "__main__":
    main()
