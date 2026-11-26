"""The new fisherman (Test/Fisherman_Untex.glb: a SmartRig skeleton with four bones per finger) holding the game's rod and
reel, posed by measurement instead of by eye:

  * the rod and reel are put where the game frames them (GripArms.FrameRod: the reel seat SeatOnScreen from the eyes,
    shifted ShiftRight of the screen's width, the tip on the line of sight, FOV 70, 16:9);
  * the right hand takes the spinning grip: the handle diagonally across the palm (index side towards the tip), the
    reel's stem between the middle and ring fingers, the palm slid onto the cork until it touches, the thumb along the
    top of the handle;
  * the left hand pinches the crank's knob between the thumb and the index finger, the other fingers loosely curled;
  * every finger closes joint by joint until its own skin (the skinned vertices, linear blend skinning in numpy) is
    1 mm off the rod, the reel or the knob (bisection per joint, proximal first), never into them;
  * the arms reach the hands by two-bone IK from the shoulders, elbows out and down.

Writes Test/Fisherman_Untex_chwyt/wedkarz_chwyt.blend (bones named: ramie_P, przedramie_P, dlon_P, srodrecze_P,
kciuk_01_r.., wskazujacy_01_r, srodkowy_01_r, serdeczny_01_r, maly_01_r; left _L/_l; the rod "Wedka", reel
"Kolowrotek", crank "Korba", knob "Galka") and renders: the eye's view, both hands from four sides.

    blender -b --python tools/assets/untex_grip.py -- [--no-render]
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector, kdtree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Test", "Fisherman_Untex.glb")
OUT = os.path.join(ROOT, "Test", "Fisherman_Untex_chwyt")
ROD_ID, REEL_ID = "rods/float_ostoja", "reels/small_hokuto"
ROD_LENGTH = 2.4          # the starter rod (s_jesion_240): its tip decides the framing
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = "--no-render" not in argv
CLEAR = 0.0012            # skin to tackle when a finger stops (m)
os.makedirs(OUT, exist_ok=True)

if "--live" in argv and not bpy.app.background:
    # in the open window: the start-up scene emptied, the interface kept
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def log(*a):
    print("GRIP", *a, flush=True)


# ------------------------------------------------------------------ the figure
bpy.ops.import_scene.gltf(filepath=SRC)
# the file's Icosphere is the bones' custom shape (the round handles the user sees on every bone): kept, but in a
# hidden collection, so neither the shape mesh itself nor anything else shows in the scene or the renders
shapes = bpy.data.collections.new("Ksztalty kosci")
scene.collection.children.link(shapes)
for o in list(scene.objects):
    if o.type == "MESH" and o.name.startswith("Icosphere"):
        for c in list(o.users_collection):
            c.objects.unlink(o)
        shapes.objects.link(o)
        o.hide_render = True
shapes.hide_render = True
bpy.context.view_layer.layer_collection.children[shapes.name].exclude = True
arm = next(o for o in scene.objects if o.type == "ARMATURE")
body = next(o for o in scene.objects if o.type == "MESH" and not o.name.startswith("Icosphere"))
arm.name, body.name = "Szkielet", "Wedkarz"

# SmartRig's numbered bones -> names (the figure faces -Y: its right is -X)
NAMES = {
    "Bone_000": "biodra", "Bone_001": "miednica", "Bone_008": "kregoslup_01", "Bone_007": "kregoslup_02",
    "Bone_006": "kregoslup_03", "Bone_005": "kregoslup_04", "Bone_004": "kregoslup_05", "Bone_003": "klatka",
    "Bone_002": "szyja_dol", "Bone_019": "szyja", "Bone_018": "glowa", "Bone_017": "glowa_czubek",
    "Bone_024": "obojczyk_P", "Bone_023": "ramie_P", "Bone_022": "przedramie_P", "Bone_021": "dlon_P", "Bone_020": "srodrecze_P",
    "Bone_029": "obojczyk_L", "Bone_028": "ramie_L", "Bone_027": "przedramie_L", "Bone_026": "dlon_L", "Bone_025": "srodrecze_L",
    "Bone_012": "udo_P", "Bone_011": "lydka_P", "Bone_010": "stopa_P", "Bone_009": "palce_stopy_P",
    "Bone_016": "udo_L", "Bone_015": "lydka_L", "Bone_014": "stopa_L", "Bone_013": "palce_stopy_L",
}
FINGERS = {  # first bone of each chain (four bones each, parent to child)
    "r": {"kciuk": "Bone_033", "wskazujacy": "Bone_041", "srodkowy": "Bone_049", "serdeczny": "Bone_045", "maly": "Bone_037"},
    "l": {"kciuk": "Bone_053", "wskazujacy": "Bone_061", "srodkowy": "Bone_069", "serdeczny": "Bone_065", "maly": "Bone_057"},
}
for side, chains in FINGERS.items():
    for finger, first in chains.items():
        b = arm.data.bones[first]
        for i in range(4):
            NAMES[b.name] = f"{finger}_{i + 1:02d}_{side}"
            kids = list(b.children)
            if not kids:
                break
            b = kids[0]
# the game's grip code looks the middle finger's knuckle up as middle_01_<side>: named so too via an alias below
for old, new in NAMES.items():
    if old in arm.data.bones:
        arm.data.bones[old].name = new
for g in body.vertex_groups:
    if g.name in NAMES:
        g.name = NAMES[g.name]
log("bones renamed:", len(NAMES))

bpy.context.view_layer.update()
armmod = next(m for m in body.modifiers if m.type == "ARMATURE")
armmod.show_viewport = False      # bones only while posing (the skin is computed here in numpy)
pose = arm.pose.bones
AW = arm.matrix_world.copy()
AWI = AW.inverted()


def head(name):
    return AW @ pose[name].head


def tail(name):
    return AW @ pose[name].tail



# ------------------------------------------------------------------ the hands' skin weights, remade
# SmartRig's automatic weights mixed neighbouring fingers (ring with middle and little), the thumb into the forearm and
# the little finger into the palm: any curl tore the skin into shards. Each hand vertex now follows the nearest bone
# segment of its own finger (or the palm, or the forearm), blended only along that finger, across its joints, and the
# wrist blends softly into the forearm.
def remake_hand_weights(s, S):
    chain_bones = [f"przedramie_{S}", f"dlon_{S}", f"srodrecze_{S}"] + [f"{f}_{i:02d}_{s}" for f in FINGERS[s] for i in range(1, 5)]
    segs = []
    for nm in chain_bones:
        b = arm.data.bones[nm]
        segs.append((nm, np.array(AW @ b.head_local), np.array(AW @ b.tail_local)))
    parent = {nm: (arm.data.bones[nm].parent.name if arm.data.bones[nm].parent and arm.data.bones[nm].parent.name in chain_bones else None) for nm in chain_bones}
    child = {}
    for nm, p in parent.items():
        if p and not nm.startswith(f"srodrecze"):
            child.setdefault(p, []).append(nm)
    wrist = np.array(AW @ arm.data.bones[f"dlon_{S}"].head_local)
    fore_dir = wrist - np.array(AW @ arm.data.bones[f"przedramie_{S}"].head_local)
    fore_dir /= np.linalg.norm(fore_dir)
    H = np.array([s_[1] for s_ in segs]); T = np.array([s_[2] for s_ in segs])
    D = T - H
    L2 = np.maximum((D * D).sum(1), 1e-9)
    gidx = {g.name: g for g in body.vertex_groups}
    changed = 0
    for v in body.data.vertices:
        p = np.array(body.matrix_world @ v.co)
        sdist = float((p - wrist) @ fore_dir)
        if sdist < -0.025:
            continue
        if np.linalg.norm(p - wrist) > 0.25:
            continue
        t = np.clip(((p - H) * D).sum(1) / L2, 0, 1)
        d = np.linalg.norm(H + D * t[:, None] - p, axis=1)
        k = int(np.argmin(d))
        if d[k] > 0.035:
            continue
        nm, tk = segs[k][0], float(t[k])
        new = {nm: 1.0}
        L = math.sqrt(L2[k])
        # across a joint: share with the parent near the head, with the (only) child near the tail
        band = min(0.012, 0.35 * L)
        if parent[nm] and tk * L < band:
            share = 0.5 * (1 - tk * L / band)
            new = {nm: 1 - share, parent[nm]: share}
        elif len(child.get(nm, [])) == 1 and (1 - tk) * L < band:
            share = 0.5 * (1 - (1 - tk) * L / band)
            new = {nm: 1 - share, child[nm][0]: share}
        # the wrist: from the old weights (the sleeve, the forearm) into the new over 3.5 cm
        alpha = min(1.0, max(0.0, (sdist + 0.025) / 0.035))
        old = {body.vertex_groups[g.group].name: g.weight for g in v.groups}
        tot = sum(old.values()) or 1.0
        mix = {n_: w_ / tot * (1 - alpha) for n_, w_ in old.items()}
        for n_, w_ in new.items():
            mix[n_] = mix.get(n_, 0.0) + w_ * alpha
        for g in list(v.groups):
            body.vertex_groups[g.group].remove([v.index])
        for n_, w_ in mix.items():
            if w_ > 0.001 and n_ in gidx:
                gidx[n_].add([v.index], w_, "REPLACE")
        changed += 1
    log(f"hand {s}: weights remade on {changed} vertices")


remake_hand_weights("r", "P")
remake_hand_weights("l", "L")


# ------------------------------------------------------------------ forearm twist bones
# Turning the hand palm-up or palm-down is the forearm's two bones rolling over each other along its whole length. One
# forearm bone can only twist the skin at the elbow or at the wrist; a twist bone over the forearm carries a share of
# the turn growing from 0 at the elbow to 1 at the wrist, so the skin twists evenly as a real forearm's does.
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for S_ in ("P", "L"):
    fe = arm.data.edit_bones[f"przedramie_{S_}"]
    tb = arm.data.edit_bones.new(f"przedramie_skret_{S_}")
    tb.head, tb.tail, tb.roll = fe.head.copy(), fe.tail.copy(), fe.roll
    tb.parent = fe
    tb.use_deform = True
bpy.ops.object.mode_set(mode="OBJECT")
for S_ in ("P", "L"):
    fb = arm.data.bones[f"przedramie_{S_}"]
    h, t = np.array(AW @ fb.head_local), np.array(AW @ fb.tail_local)
    axis = t - h
    L2 = float(axis @ axis)
    g_f = body.vertex_groups[f"przedramie_{S_}"]
    g_t = body.vertex_groups.new(name=f"przedramie_skret_{S_}")
    moved = 0
    for v in body.data.vertices:
        w = next((g.weight for g in v.groups if g.group == g_f.index), 0.0)
        if w <= 0.0:
            continue
        u = float((np.array(body.matrix_world @ v.co) - h) @ axis) / L2
        k = min(1.0, max(0.0, u)) ** 1.2
        if k > 0.0:
            g_f.add([v.index], w * (1 - k), "REPLACE")
            g_t.add([v.index], w * k, "REPLACE")
            moved += 1
    log(f"forearm {S_}: {moved} vertices share the twist")
pose = arm.pose.bones
for S_ in ("P", "L"):
    # the twist bone shown like the forearm (the file's round handle), not as a bare octahedron
    src_pb, dst_pb = pose[f"przedramie_{S_}"], pose[f"przedramie_skret_{S_}"]
    dst_pb.custom_shape = src_pb.custom_shape
    dst_pb.custom_shape_scale_xyz = src_pb.custom_shape_scale_xyz * 0.8
    dst_pb.custom_shape_translation = src_pb.custom_shape_translation
    dst_pb.custom_shape_rotation_euler = src_pb.custom_shape_rotation_euler
    dst_pb.use_custom_shape_bone_size = src_pb.use_custom_shape_bone_size


# ------------------------------------------------------------------ the fingers parted, the wrist's weights smoothed
# The scan's remesh welded touching fingers with thin strips (edges of 0.5-3 mm from one finger to the next): any
# difference in their curl stretched the strips into shards. They are cut along the fingers (the webs at the knuckles
# stay). The cuff's edge is joined to the wrist's skin; its weights jumped from the hand to the forearm, so they are
# smoothed over the mesh's neighbours in a band round the wrist.
import bmesh


def finger_of(name):
    for f_ in ("kciuk", "wskazujacy", "srodkowy", "serdeczny", "maly"):
        if name.startswith(f_ + "_"):
            return f_ + name[-2:], int(name[-4:-2])
    return None, 0


me = body.data
bm = bmesh.new()
bm.from_mesh(me)
dl = bm.verts.layers.deform.active
names_by_index = {g.index: g.name for g in body.vertex_groups}


def dominant(v):
    d = v[dl]
    if not d:
        return None
    gi = max(d.keys(), key=lambda k_: d[k_])
    return names_by_index.get(gi)


dom = {}
for v in bm.verts:
    nm = dominant(v) or ""
    fid, lvl = finger_of(nm)
    if fid and lvl == 1:
        # how far down the first phalanx: the web between the fingers ends about a third of the way
        b = arm.data.bones[nm]
        h_, t_ = AW @ b.head_local, AW @ b.tail_local
        u = (body.matrix_world @ v.co - h_).dot(t_ - h_) / max((t_ - h_).length_squared, 1e-9)
        lvl = 2 if u > 0.4 else 1
    dom[v.index] = (fid, lvl)
bridges = []
for fc in bm.faces:
    owners = {dom[v.index][0] for v in fc.verts if dom[v.index][0]}
    level = max(dom[v.index][1] for v in fc.verts)
    if len(owners) >= 2 and level >= 2 and not any(o.startswith("kciuk") for o in owners):
        bridges.append(fc)
bmesh.ops.delete(bm, geom=bridges, context="FACES")
bm.to_mesh(me)
bm.free()
me.update()
log(f"fingers parted: {len(bridges)} faces bridging neighbouring fingers removed")


def smooth_wrist(S_, iterations=14):
    groups = [f"przedramie_{S_}", f"przedramie_skret_{S_}", f"dlon_{S_}", f"srodrecze_{S_}"]
    gi = {g: body.vertex_groups[g].index for g in groups if g in body.vertex_groups}
    wrist = np.array(AW @ arm.data.bones[f"dlon_{S_}"].head_local)
    fore = wrist - np.array(AW @ arm.data.bones[f"przedramie_{S_}"].head_local)
    fore /= np.linalg.norm(fore)
    co = np.array([body.matrix_world @ v.co for v in me.vertices])
    sd = (co - wrist) @ fore
    band = np.nonzero((sd > -0.008) & (sd < 0.06) & (np.linalg.norm(co - wrist, axis=1) < 0.11))[0]
    pos = {int(v): i for i, v in enumerate(band)}
    W = np.zeros((len(band), len(groups)))
    other = np.zeros(len(band))
    for i, vi in enumerate(band):
        for g in me.vertices[vi].groups:
            nm = names_by_index[g.group]
            if nm in gi:
                W[i, groups.index(nm)] = g.weight
            else:
                other[i] += g.weight
    nb = [[] for _ in band]
    for e in me.edges:
        a_, b_ = e.vertices
        if a_ in pos and b_ in pos:
            nb[pos[a_]].append(pos[b_])
            nb[pos[b_]].append(pos[a_])
    for _ in range(iterations):
        new = W.copy()
        for i, ns in enumerate(nb):
            if ns:
                new[i] = 0.5 * W[i] + 0.5 * W[ns].mean(0)
        W = new
    for i, vi in enumerate(band):
        tot = W[i].sum()
        if tot <= 0:
            continue
        W[i] *= max(0.0, 1.0 - other[i]) / tot
        for j, g in enumerate(groups):
            if g in gi:
                if W[i, j] > 0.001:
                    body.vertex_groups[g].add([int(vi)], float(W[i, j]), "REPLACE")
                else:
                    body.vertex_groups[g].remove([int(vi)])
    log(f"wrist {S_}: weights smoothed on {len(band)} vertices ({sum(1 for x in nb if x)} with neighbours, sd range {sd[band].min():.3f}..{sd[band].max():.3f})")


smooth_wrist("P")
smooth_wrist("L")

# ------------------------------------------------------------------ the skin (linear blend skinning in numpy)
rest = np.array([body.matrix_world @ v.co for v in body.data.vertices], dtype=np.float64)
rest_arm = (np.array(AWI) @ np.c_[rest, np.ones(len(rest))].T).T[:, :3]
gname = {g.index: g.name for g in body.vertex_groups}
HAND_BONES = {s: {f"dlon_{S}", f"srodrecze_{S}"} | {f"{f}_{i:02d}_{s}" for f in FINGERS[s] for i in range(1, 5)}
              for s, S in (("r", "P"), ("l", "L"))}
skin = {}
for s in ("r", "l"):
    idx, bones, weights = [], [], []
    for v in body.data.vertices:
        ws = [(gname[g.group], g.weight) for g in v.groups if g.group in gname and gname[g.group] in pose]
        if not any(n in HAND_BONES[s] and w > 0.01 for n, w in ws):
            continue
        tot = sum(w for _, w in ws) or 1.0
        idx.append(v.index)
        bones.append([n for n, _ in ws])
        weights.append([w / tot for _, w in ws])
    names = sorted({n for bl in bones for n in bl})
    col = {n: i for i, n in enumerate(names)}
    W = np.zeros((len(idx), len(names)))
    for r, (bl, wl) in enumerate(zip(bones, weights)):
        for n, w in zip(bl, wl):
            W[r, col[n]] += w
    owner = [names[int(np.argmax(W[r]))] for r in range(len(idx))]
    skin[s] = {"idx": np.array(idx), "names": names, "W": W, "owner": np.array(owner), "rest": rest_arm[idx]}
    log(f"hand {s}: {len(idx)} skinned vertices over {len(names)} bones")


def skinned(s, mask=None):
    """The hand's vertices as posed now (world), optionally only those owned by bones in mask."""
    k = skin[s]
    rows = np.arange(len(k["idx"])) if mask is None else np.nonzero(np.isin(k["owner"], list(mask)))[0]
    p = np.c_[k["rest"][rows], np.ones(len(rows))]
    out = np.zeros((len(rows), 3))
    for j, n in enumerate(k["names"]):
        w = k["W"][rows, j]
        if not np.any(w):
            continue
        m = np.array(pose[n].matrix @ arm.data.bones[n].matrix_local.inverted())
        out += w[:, None] * (m @ p.T).T[:, :3]
    return (np.array(AW) @ np.c_[out, np.ones(len(out))].T).T[:, :3]


# ------------------------------------------------------------------ the eyes, the rod and the reel where the game puts them
verts = rest
top = verts[:, 2].max()
headv = verts[verts[:, 2] > top - 0.28]
headv = headv[np.abs(headv[:, 0]) < 0.12]
eye = Vector((float(np.median(headv[:, 0])), float(headv[:, 1].min()) + 0.05, float(top) - 0.115))
log("eyes at", tuple(round(c, 3) for c in eye))
CAM_RIGHT, CAM_UP, CAM_FWD = Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0))


def cam(v):
    return CAM_RIGHT * v[0] + CAM_UP * v[1] + CAM_FWD * v[2]


# the rod at the hips (the user): the reel seat a hand's breadth over the hip joint, in front of the belly a little to
# the right, the blank rising 25 degrees ahead; the reel under it, its crank on the left within the left hand's reach
hips = AW @ arm.data.bones["biodra"].head_local
seat_world = hips + Vector((-0.08, -0.18, 0.10))
pitch = math.radians(25.0)
along = (Vector((0, -1, 0)) * math.cos(pitch) + Vector((0, 0, 1)) * math.sin(pitch)).normalized()
down = (Vector((0, 0, -1)) - along * along.dot(Vector((0, 0, -1)))).normalized()
xr = down.cross(along)
ROD = Matrix((xr, down, along)).transposed()          # columns: X (figure's right), Y (reel side, down), Z (towards tip)
rod_origin = seat_world - ROD @ Vector((0, 0, 0.1))
section = 0.3                       # the rod model's own sections (no stretching)
log("reel seat at", tuple(round(c, 3) for c in seat_world), f"({(seat_world.z - hips.z) * 100:.0f} cm over the hips), rod along", tuple(round(c, 3) for c in along))


def g2b(v):
    return Vector((v[0], -v[2], v[1]))


def load(path, coll):
    before = set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in scene.objects if o not in before]
    out = {}
    for o in new:
        if o.type == "MESH" and o.name.startswith("Icosphere"):
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        for c in list(o.users_collection):
            c.objects.unlink(o)
        coll.objects.link(o)
        if o.type == "MESH":
            out[o.name.split(".")[0]] = o
    return out


coll = bpy.data.collections.new("Wedka i kolowrotek")
scene.collection.children.link(coll)


def handle(name, shape, size):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type, e.empty_display_size, e.show_in_front = shape, size, True
    coll.objects.link(e)
    return e


rod = load(os.path.join(ROOT, "assets", "models", ROD_ID + ".glb"), coll)
wedka = handle("Wedka", "ARROWS", 0.15)
for name, o in rod.items():
    o.parent = wedka
    o.matrix_parent_inverse = Matrix()
    if name.startswith("Seg"):
        # the model as it is (no stretching, the user's rule): the sections at their own 0.3 m
        o.location = Vector((0, 0, 0.25 + 0.3 * int(name[3:])))
    o.hide_select = True
reel = load(os.path.join(ROOT, "assets", "models", REEL_ID + ".glb"), coll)
frames = json.load(open(os.path.join(ROOT, "assets", "models", REEL_ID + ".json"), encoding="utf-8"))
kolowrotek = handle("Kolowrotek", "ARROWS", 0.08)
kolowrotek.parent = wedka
for name, o in reel.items():
    o.parent = kolowrotek
    o.matrix_parent_inverse = Matrix()
    o.hide_select = True
reel["Rotor"].location = g2b(frames["rotor"])
if "Bail" in reel:
    reel["Bail"].parent = reel["Rotor"]
    reel["Bail"].location = g2b(frames["bail_pivot"])
korba = handle("Korba", "CIRCLE", 0.04)
korba.parent = kolowrotek
korba.location = g2b(frames["hub"])
korba.rotation_mode = "XYZ"
korba.lock_location = (True, True, True)
korba.lock_rotation = (False, True, True)
reel["Crank"].parent = korba
reel["Crank"].matrix_parent_inverse = Matrix()
reel["Crank"].location = Vector()
# the crank turned so the knob points back and down towards the angler's left hand (RodRig.KnobAt's angle)
CRANK_ANGLE = math.radians(float(next((a.split("=")[1] for a in argv if a.startswith("--crank=")), "35")))
korba.rotation_euler = (CRANK_ANGLE, 0, 0)
galka = handle("Galka", "SPHERE", 0.012)
galka.parent = korba
galka.location = Vector((frames["knob_out"], frames["arm"], 0.0))   # in the crank pivot's own frame (export_grip.py's KnobRef)
galka.lock_location = (True, True, True)
wedka.matrix_world = Matrix.Translation(rod_origin) @ ROD.to_4x4()
bpy.context.view_layer.update()

# the tackle's surface for the fingers: every vertex of the rod's handle and first section, the reel's parts
tree_pts = []
tree_obj = []
for o in list(rod.values()) + list(reel.values()):
    if o.name.startswith("Seg") and int(o.name[3:4] or 0) > 1:
        continue
    mw = o.matrix_world
    for v in o.data.vertices:
        tree_pts.append(mw @ v.co)
        tree_obj.append(o.name)
KD = kdtree.KDTree(len(tree_pts))
for i, p in enumerate(tree_pts):
    KD.insert(p, i)
KD.balance()
log("tackle surface:", len(tree_pts), "points")
_inv = wedka.matrix_world.inverted()
grip_pts = [p_ for p_, o_ in zip(tree_pts, tree_obj) if o_.split(".")[0] in ("Handle", "Seg0", "Seg1") or
            (o_.split(".")[0] == "Body" and math.hypot(*(_inv @ p_).xy) < 0.035)]
KD_GRIP = kdtree.KDTree(len(grip_pts))
for i, p_ in enumerate(grip_pts):
    KD_GRIP.insert(p_, i)
KD_GRIP.balance()
TREE = {"r": KD_GRIP, "l": KD}
log("right hand's grip surface:", len(grip_pts), "points")

# the handle's radius along the rod (cork and seat), from the handle's own vertices: a penetration guard
inv_rod = wedka.matrix_world.inverted()
hv = np.array([inv_rod @ (rod["Handle"].matrix_world @ v.co) for v in rod["Handle"].data.vertices])
zbins = np.arange(-0.4, 0.3, 0.005)
rprof = np.zeros(len(zbins))
for i, z in enumerate(zbins):
    sel = hv[np.abs(hv[:, 2] - z) < 0.004]
    rprof[i] = np.percentile(np.hypot(sel[:, 0], sel[:, 1]), 60) if len(sel) > 8 else 0.0
log("handle radius by z:", " ".join(f"{z:+.2f}:{r * 1000:.1f}" for z, r in zip(zbins[::6], rprof[::6])))
INV_ROD = np.array(inv_rod)


def clearance(points, tree=None):
    """Smallest distance (m) from points to the tackle; negative inside the handle."""
    if len(points) == 0:
        return 1.0
    loc = (INV_ROD @ np.c_[points, np.ones(len(points))].T).T[:, :3]
    r = np.hypot(loc[:, 0], loc[:, 1])
    rad = np.interp(loc[:, 2], zbins, rprof, left=0.0, right=0.0)
    inside = np.min(np.where(rad > 0, r - rad, 1.0))
    best = min((tree or KD).find(Vector(p))[2] for p in points[:: max(1, len(points) // 1500)])
    return min(best, inside)


def distances(points, tree=None):
    """Each point's distance (m) to the tackle; negative inside the handle."""
    loc = (INV_ROD @ np.c_[points, np.ones(len(points))].T).T[:, :3]
    r = np.hypot(loc[:, 0], loc[:, 1])
    rad = np.interp(loc[:, 2], zbins, rprof, left=0.0, right=0.0)
    inside = np.where(rad > 0, r - rad, 1.0)
    kd = np.array([(tree or KD).find(Vector(p))[2] for p in points])
    return np.minimum(kd, inside)


# ------------------------------------------------------------------ posing helpers
def update():
    bpy.context.view_layer.update()


def set_world_rotation(name, rot3, keep_head=True):
    """The bone's world turn (its head kept), through its armature-space matrix."""
    pb = pose[name]
    m = pb.matrix.copy()
    loc = m.to_translation()
    r_arm = AWI.to_3x3() @ rot3
    pb.matrix = Matrix.Translation(loc) @ r_arm.to_4x4()
    update()


def rotate_world(name, q, pivot=None):
    """Turns a bone (and so its children) by the world rotation q about its head."""
    pb = pose[name]
    m = AW @ pb.matrix
    piv = pivot if pivot is not None else m.to_translation()
    new = Matrix.Translation(piv) @ q.to_matrix().to_4x4() @ Matrix.Translation(-piv) @ m
    pb.matrix = AWI @ new
    update()


def two_bone_ik(upper, lower, hand, wrist_target, pole_dir):
    """Upper arm and forearm so the hand's head reaches the target, the elbow towards pole_dir."""
    a = head(upper)
    l1 = (head(lower) - a).length
    l2 = (head(hand) - head(lower)).length
    to = wrist_target - a
    d = min(max(to.length, 0.02), l1 + l2 - 0.001)
    dirn = to.normalized()
    cos = max(-1.0, min(1.0, (l1 * l1 + d * d - l2 * l2) / (2 * l1 * d)))
    ang = math.acos(cos)
    bend = (pole_dir - dirn * pole_dir.dot(dirn)).normalized()
    elbow = a + (dirn * math.cos(ang) + bend * math.sin(ang)) * l1
    cur = head(lower) - a
    rotate_world(upper, cur.rotation_difference(elbow - a))
    cur = head(hand) - head(lower)
    rotate_world(lower, cur.rotation_difference(a + dirn * d - head(lower)))
    if to.length > l1 + l2 + 0.005 and not getattr(two_bone_ik, "warned", False):
        two_bone_ik.warned = True
        log(f"  {hand}: target {to.length:.3f} m beyond reach {l1 + l2:.3f} m")
    return (head(hand) - wrist_target).length


def hand_frame(s, S):
    """The hand's axes now: f wrist -> knuckles, a little finger -> index knuckle, n palm normal (towards the palm side)."""
    w = head(f"dlon_{S}")
    idx, mid, ring, pinky = (head(f"{f}_01_{s}") for f in ("wskazujacy", "srodkowy", "serdeczny", "maly"))
    knuckles = (idx + mid + ring + pinky) / 4
    f = (knuckles - w).normalized()
    a = (idx - pinky)
    a = (a - f * a.dot(f)).normalized()
    n = f.cross(a)
    if s in PALM_SIGN:
        return f, a, n * PALM_SIGN[s], knuckles, w
    # the palm side: where the fingers' tips lie off the knuckles' line (they rest a little curled), or the thumb
    tips = [tail(f"{f_}_04_{s}") for f_ in ("wskazujacy", "srodkowy", "serdeczny", "maly")]
    off = sum((t - knuckles for t in tips), Vector()) / 4
    if off.dot(n) < 0 and (tail(f"kciuk_02_{s}") - w).dot(n) < 0:
        n = -n
    elif off.dot(n) < 0 and (tail(f"kciuk_02_{s}") - w).dot(n) >= 0:
        log(f"  {s}: palm side from the thumb (fingers rest straight)")
    return f, a, n, knuckles, w


PALM_SIGN = {}
_fr, _ar, _nr, _kr, _wr = hand_frame("r", "P")
PALM_SIGN["r"] = 1.0 if _nr.dot(_fr.cross(_ar)) > 0 else -1.0
_fl, _al, _nl, _kl, _wl = hand_frame("l", "L")
_mirror = Vector((-_nr.x, _nr.y, _nr.z))
PALM_SIGN["l"] = 1.0 if _fl.cross(_al).dot(_mirror) > 0 else -1.0
log(f"palm sides: right {PALM_SIGN['r']:+.0f}, left {PALM_SIGN['l']:+.0f} (the left mirrors the right)")


def basis(u1, u2):
    u1 = u1.normalized()
    u2 = (u2 - u1 * u2.dot(u1)).normalized()
    return Matrix((u1, u2, u1.cross(u2))).transposed()


# ------------------------------------------------------------------ fingers closing on contact
def chain(s, finger):
    return [f"{finger}_{i:02d}_{s}" for i in range(1, 5)]


def flex_axis(name, palm_n):
    d = (tail(name) - head(name)).normalized()
    return d.cross(palm_n).normalized()


def close_finger(s, finger, palm_n, limits=(85, 100, 80, 40), couple=(1.0, 1.1, 0.8, 0.4), start=0, extra=None, toward=None):
    """Closes the finger's joints (from joint `start`) until its skin is CLEAR off the tackle: all joints together,
    then from the second, then the last; each step by bisection. Returns the clearance at the end."""
    names = chain(s, finger)
    owned = set(names)
    total = [0.0] * 4

    def apply(angles):
        for i, n in enumerate(names):
            ax = flex_axis(n, (toward - head(n)).normalized() if toward is not None else palm_n)
            rotate_world(n, Quaternion(ax, math.radians(angles[i] - total[i])))
            total[i] = angles[i]

    # skin already touching at the start (a finger's side against the reel's stem) may stay touching, never go in
    base = distances(skinned(s, owned), TREE[s])
    stuck = base < CLEAR + 0.003

    def ok(angles):
        apply(angles)
        pts = skinned(s, owned)
        d = distances(pts, TREE[s])
        if extra is not None and extra(pts) < CLEAR:
            return False
        return bool(np.all(d[~stuck] >= CLEAR) and np.all(d[stuck] >= np.minimum(base[stuck], CLEAR) - 0.001))

    for first in range(start, 3):
        base_ang = list(total)
        lo, hi = 0.0, 1.0
        target = [base_ang[i] if i < first else min(limits[i], base_ang[i] + limits[i] * couple[i]) for i in range(4)]
        if ok(target):
            continue
        for _ in range(11):
            mid = (lo + hi) / 2
            ang = [base_ang[i] + (target[i] - base_ang[i]) * mid for i in range(4)]
            if ok(ang):
                lo = mid
            else:
                hi = mid
        apply([base_ang[i] + (target[i] - base_ang[i]) * lo for i in range(4)])
    pts = skinned(s, owned)
    c = clearance(pts, TREE[s])
    log(f"  {finger}_{s}: angles {', '.join(f'{a:.0f}' for a in total)} deg, clearance {c * 1000:.1f} mm")
    return c



def spread(s, finger, palm_n, towards, max_deg=16.0):
    """Spreads a finger sideways at its knuckle (about the palm's normal), towards `towards`, by the least angle that
    takes it off the tackle: the middle and ring fingers part round the reel's stem as a real hand's do."""
    nm = f"{finger}_01_{s}"
    d = (tail(nm) - head(nm)).normalized()
    sign = 1.0 if palm_n.cross(d).dot(towards) > 0 else -1.0
    owned = set(chain(s, finger))
    done = 0.0
    for step in range(1, int(max_deg) + 1):
        if clearance(skinned(s, owned), TREE[s]) >= CLEAR + 0.0005:
            break
        rotate_world(nm, Quaternion(palm_n, math.radians(sign)))
        done += 1.0
    log(f"  {finger}_{s}: spread {done:.0f} deg, clearance {clearance(skinned(s, owned), TREE[s]) * 1000:.1f} mm")

def untwist(lower, hand, share=1.0):
    """The hand's twist about the forearm goes into the forearm's twist bone (the skin twists evenly along the
    forearm), the hand's world turn kept: left all in the wrist it tore the cuff and the wrist into shards, put all in
    the forearm bone it twisted the elbow."""
    twist_bone = lower.replace("przedramie_", "przedramie_skret_")
    hand_world = (AW @ pose[hand].matrix).to_3x3().normalized()
    fore = (AW @ pose[lower].matrix).to_3x3().normalized()
    rest_rel = arm.data.bones[lower].matrix_local.to_3x3().inverted() @ arm.data.bones[hand].matrix_local.to_3x3()
    now_rel = fore.inverted() @ hand_world
    delta = (now_rel @ rest_rel.inverted()).to_quaternion()      # in the forearm's frame
    twist = Quaternion((delta.w, 0.0, delta.y, 0.0)).normalized()
    angle = 2 * math.atan2(twist.y, twist.w)
    axis_world = (tail(lower) - head(lower)).normalized()
    rotate_world(twist_bone, Quaternion(axis_world, angle * share))
    set_world_rotation(hand, hand_world)
    log(f"  {lower}: {math.degrees(angle * share):.0f} deg of the wrist's twist spread along the forearm")


POLE = {"P": Vector((-0.6, 0.1, -1)), "L": Vector((0.6, 0.1, -1))}


def straight_wrist_pole(S_, wrist_target, hand_rot, apply=True, step=10):
    """The elbow's direction (round the shoulder-to-wrist line) that leaves the wrist straightest for the hand's turn:
    the forearm then runs along the hand, as it does on a rod's handle; the elbow not above the arm's line and out
    from the body. Returns the wrist's bend (deg)."""
    upper, lower, hand = f"ramie_{S_}", f"przedramie_{S_}", f"dlon_{S_}"
    shoulder = head(upper)
    axis = (wrist_target - shoulder).normalized()
    ref = axis.orthogonal().normalized()
    hand_dir = (hand_rot @ Vector((0, 1, 0))).normalized()     # a bone's own +Y: from the wrist into the hand
    best = None
    for deg in range(0, 360, step):
        pole = Quaternion(axis, math.radians(deg)) @ ref
        two_bone_ik(upper, lower, hand, wrist_target, pole)
        elbow = head(lower)
        if elbow.z > (shoulder.z + wrist_target.z) / 2 + 0.02 or (elbow.x - shoulder.x) * (-1 if S_ == "P" else 1) < -0.08:
            continue
        if S_ == "P":
            # the rod's butt runs back under the forearm: its skin must stay on the cork, never in it
            if clearance(skinned("r", {"przedramie_P", "przedramie_skret_P"}), KD_GRIP) < 0.0008:
                continue
        bend = math.degrees((head(hand) - elbow).angle(hand_dir))
        if best is None or bend < best[0]:
            best = (bend, pole)
    if best is None:
        best = (180.0, POLE[S_])
    if apply:
        POLE[S_] = best[1]
        log(f"  {S_}: elbow placed for a straight wrist, bend {best[0]:.0f} deg")
        two_bone_ik(upper, lower, hand, wrist_target, POLE[S_])
        set_world_rotation(hand, hand_rot)
    return best[0]

def stages():
    """The posing, step by step (a generator: the live view redraws between steps)."""
    # ------------------------------------------------------------------ the right hand on the handle
    # Measured, not guessed (like the left hand on the knob): the four fingers curled to the hook that holds a cork
    # handle; the line through the hooks' insides is where the handle lies in this hand (it comes out across the palm,
    # from the heel towards the index, by itself). That line goes onto the rod's axis, the reel's stem between the middle
    # and ring fingers' hooks, the palm turned to the figure's right and a little under the handle, the elbow chosen for
    # the straightest wrist; then every finger closes until its skin touches the cork, the seat or the stem.
    S, s = "P", "r"
    hand_rest_l = (AW @ pose["dlon_L"].matrix).to_3x3().normalized()
    hand_rest_r = (AW @ pose["dlon_P"].matrix).to_3x3().normalized()
    Xr, Yr, Zr = ROD.col[0].copy(), ROD.col[1].copy(), ROD.col[2].copy()
    R_HANDLE = float(np.interp(0.1 - 0.06, zbins, rprof))     # the cork just behind the seat
    f, a, n, knuckles, w = hand_frame(s, S)
    four = ("wskazujacy", "srodkowy", "serdeczny", "maly")
    saved = {nm: pose[nm].matrix.copy() for f_ in four for nm in chain(s, f_)}
    for f_ in four:
        for i, ang in enumerate((40, 75, 50, 15)):
            nm = f"{f_}_{i + 1:02d}_r"
            rotate_world(nm, Quaternion(flex_axis(nm, n), math.radians(ang)))
    centres = {}
    for f_ in four:
        joints = [head(f"{f_}_{i:02d}_r") for i in (2, 3, 4)]
        mid = sum(joints, Vector()) / 3
        inward = ((head(f"{f_}_01_r") + n * 0.012) - mid).normalized()
        centres[f_] = mid + inward * (R_HANDLE + 0.008)
    for f_ in four:
        for nm in chain(s, f_):
            pose[nm].matrix = saved[nm]
            update()
    # the handle lies across the palm's diagonal: from inside the index finger's hook to the heel of the hand on the
    # little finger's side (the hypothenar), as a rod's handle does, so the forearm can run along the rod
    width = (head("wskazujacy_01_r") - head("maly_01_r")).length
    heel = w + (knuckles - w) * 0.3 + n * (R_HANDLE + 0.011) - a * (0.35 * width)
    u = (centres["wskazujacy"] - heel).normalized()
    log(f"  right grip line: {math.degrees(u.angle(a)):.0f} deg off the knuckles' line (the palm's diagonal)")
    mr = (centres["srodkowy"] + centres["serdeczny"]) / 2
    stem_local = heel + u * (mr - heel).dot(u) - w                 # the stem's place on that line, in the hand
    n_line = (n - u * n.dot(u)).normalized()
    n_want = (-Xr - 0.45 * Yr).normalized()
    seat_axial = (seat_world - rod_origin).dot(Zr)
    on_axis = rod_origin + Zr * seat_axial
    best = None
    for deg in range(-45, 46, 5):
        want = Quaternion(Zr, math.radians(deg)) @ n_want
        q = (basis(Zr, want) @ basis(u, n_line).inverted()).to_quaternion()
        wrist_new = on_axis - q @ stem_local
        if (wrist_new - head("ramie_P")).length > 0.5:
            continue
        bend = straight_wrist_pole("P", wrist_new, q.to_matrix() @ hand_rest_r, apply=False, step=20)
        score = -bend - 1.0 * abs(deg)
        if best is None or score > best[0]:
            best = (score, q, wrist_new, deg, bend)
    _, q, wrist_new, deg, bend = best
    straight_wrist_pole("P", wrist_new, q.to_matrix() @ hand_rest_r)
    yield "prawa reka siega do rekojesci"
    log(f"  right hand: palm turned {deg} deg round the handle, wrist bend {bend:.0f} deg, wrist off by {(head('dlon_P') - wrist_new).length * 1000:.1f} mm")
    # onto the cork: slid along the palm's normal until the palm and the straight fingers are just off the handle
    f, a, n, knuckles, w = hand_frame(s, S)
    hand_rot = (AW @ pose["dlon_P"].matrix).to_3x3().normalized()
    base_wrist = head("dlon_P")
    touch = {"dlon_P", "srodrecze_P"}


    def slide(off):
        two_bone_ik("ramie_P", "przedramie_P", "dlon_P", base_wrist - n * off, POLE["P"])
        set_world_rotation("dlon_P", hand_rot)
        return clearance(skinned(s, touch), KD_GRIP)


    lo_, hi_ = 0.0, 0.05            # lo touching or inside, hi clear: find the boundary
    if slide(lo_) >= CLEAR:
        lo_, hi_ = -0.05, 0.0
        lo_, hi_ = hi_, lo_
    for _ in range(14):
        mid = (lo_ + hi_) / 2
        if slide(mid) >= CLEAR:
            hi_ = mid
        else:
            lo_ = mid
    c = slide(hi_)
    log(f"  right palm on the cork: moved {hi_ * 1000:.1f} mm off the handle, clearance {c * 1000:.1f} mm")
    yield "dlon dosunieta do korka"
    f, a, n, knuckles, w = hand_frame(s, S)
    for finger in four:
        close_finger(s, finger, n)
        yield f"palec {finger}"
    # the thumb: turned to lie along the top of the handle towards the tip, then closed onto it
    tb = "kciuk_01_r"
    want = (Zr * 0.9 - Yr * 0.15 - Xr * 0.25).normalized()
    cur = (tail("kciuk_03_r") - head(tb)).normalized()
    rotate_world(tb, cur.rotation_difference(want))
    close_finger(s, "kciuk", (Yr - Xr * 0.4).normalized(), limits=(30, 60, 60, 30), couple=(0.6, 1.0, 0.9, 0.5), start=0)
    yield "prawy kciuk na rekojesci"
    untwist("przedramie_P", "dlon_P")
    fa = {"przedramie_P", "przedramie_skret_P"}
    log(f"  right forearm over the butt: {clearance(skinned('r', fa), KD_GRIP) * 1000:.1f} mm before the elbow's last choice")
    straight_wrist_pole("P", head("dlon_P"), (AW @ pose["dlon_P"].matrix).to_3x3().normalized())
    log(f"  right forearm over the butt: {clearance(skinned('r', fa), KD_GRIP) * 1000:.1f} mm")
    yield "skret przedramienia rozlozony"
    log("right hand placed")

    # ------------------------------------------------------------------ the left hand round the crank's knob
    # The knob held in the curled index and middle fingers (a small handle), the thumb pressing on it from the other
    # side; the ring and little fingers curl alongside. Placed in the hand's frame where the curled fingers' middle
    # phalanges would be, the knob's axis across the fingers; then every finger closes onto it by contact.
    S, s = "L", "l"
    knob = galka.matrix_world.to_translation()
    knob_axis = Xr.copy()                       # parallel to the crank's axle
    kv = np.array([reel["Crank"].matrix_world @ v.co for v in reel["Crank"].data.vertices])
    near = kv[np.linalg.norm(kv - np.array(knob), axis=1) < 0.03]
    ax = np.array(knob_axis)
    radial = near - np.array(knob) - np.outer((near - np.array(knob)) @ ax, ax)
    knob_r = float(np.percentile(np.linalg.norm(radial, axis=1), 95)) if len(near) else 0.01
    knob_len = float(np.ptp((near - np.array(knob)) @ ax)) if len(near) else 0.02
    log(f"  knob: radius {knob_r * 1000:.1f} mm, length {knob_len * 1000:.1f} mm")
    shoulder = head("ramie_L")
    f, a, n, knuckles, w = hand_frame(s, S)
    width = (head("wskazujacy_01_l") - head("maly_01_l")).length
    # where the knob sits in the hand: ahead of the knuckles by the proximal phalanx, off the palm by the knob's radius
    # and the finger's thickness, between the index and middle fingers across
    # measured, not guessed: the index and middle fingers curled to a hook, the knob's centre inside it, a finger's
    # half-thickness and the knob's radius in from their middle joints towards the palm; the fingers back straight after
    saved = {nm: pose[nm].matrix.copy() for f_ in ("wskazujacy", "srodkowy") for nm in chain(s, f_)}
    for f_ in ("wskazujacy", "srodkowy"):
        for i, ang in enumerate((55, 80, 45, 15)):
            nm = f"{f_}_{i + 1:02d}_l"
            rotate_world(nm, Quaternion(flex_axis(nm, n), math.radians(ang)))
    joints = [head(f"{f_}_{i:02d}_l") for f_ in ("wskazujacy", "srodkowy") for i in (3, 4)]
    ring_mid = sum(joints, Vector()) / len(joints)
    inward = ((knuckles + n * 0.012) - ring_mid).normalized()
    grasp = ring_mid + inward * (knob_r + 0.008)
    grip_local = grasp - w
    for f_ in ("wskazujacy", "srodkowy"):
        for nm in chain(s, f_):
            pose[nm].matrix = saved[nm]
            update()
    log(f"  left grasp point: {(grasp - knuckles).dot(f) * 1000:.0f} mm ahead of the knuckles, {(grasp - knuckles).dot(n) * 1000:.0f} mm off the palm")
    best = None
    for deg in range(0, 360, 12):
        # the knob's axis across the fingers (a), turned by deg about it; the wrist as straight as the elbow allows
        tgt = basis(knob_axis, Quaternion(knob_axis, math.radians(deg)) @ knob_axis.orthogonal())
        src = basis(a, n)
        q = (tgt @ src.inverted()).to_quaternion()
        wrist_new = knob - q @ grip_local
        if (wrist_new - shoulder).length > 0.5:
            continue
        bend = straight_wrist_pole("L", wrist_new, q.to_matrix() @ hand_rest_l, apply=False, step=20)
        # the palm away from the reel's body (on the outside of the knob)
        score = -bend + 25.0 * (q @ n).dot(Xr)
        if best is None or score > best[0]:
            best = (score, q, wrist_new, deg)
    _, q, wrist_new, deg = best
    straight_wrist_pole("L", wrist_new, (q.to_matrix() @ hand_rest_l))
    cur = (AW @ pose["dlon_L"].matrix).to_3x3().normalized()
    set_world_rotation("dlon_L", (q.to_matrix() @ hand_rest_l))
    log(f"  left hand: turned {deg} deg about the knob, wrist {tuple(round(c, 3) for c in head('dlon_L'))}, off by {(head('dlon_L') - wrist_new).length * 1000:.1f} mm")
    yield "lewa dlon przy galce"
    f, a, n, knuckles, w = hand_frame(s, S)
    spread(s, "wskazujacy", n, a)      # off the crank's arm, towards the thumb side
    for finger in ("wskazujacy", "srodkowy", "serdeczny", "maly"):
        close_finger(s, finger, n, limits=(90, 100, 80, 40), toward=knob if finger in ("wskazujacy", "srodkowy") else None)
        yield f"palec {finger}"
    # the thumb over the knob from the other side
    close_finger(s, "kciuk", n, limits=(40, 60, 60, 30), couple=(0.6, 1.0, 0.9, 0.5), start=0, toward=knob)
    yield "lewy kciuk na galce"
    untwist("przedramie_L", "dlon_L")
    yield "lewe przedramie"
    knob_gap = min(KD.find(Vector(p))[2] for p in skinned(s, {"kciuk_04_l", "wskazujacy_03_l", "srodkowy_03_l", "kciuk_03_l", "wskazujacy_02_l", "srodkowy_02_l"}))
    log(f"  left fingers on the knob: {knob_gap * 1000:.1f} mm")


    # ------------------------------------------------------------------ rotations only (the user's rule)
    # every bone keeps its rest length and place on its parent: no translation, no scale, only its rotation
    worst_loc = worst_scale = 0.0
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        worst_loc = max(worst_loc, pb.location.length)
        worst_scale = max(worst_scale, max(abs(c - 1.0) for c in pb.scale))
        pb.location = Vector((0, 0, 0))
        pb.scale = Vector((1, 1, 1))
        pb.rotation_quaternion = pb.rotation_quaternion.normalized()
    update()
    log(f"rotations only: largest bone translation removed {worst_loc * 1000:.2f} mm, scale off by {worst_scale:.4f}")
    for s_ in ("r", "l"):
        tree = TREE[s_]
        log(f"  hand {s_} after: skin {clearance(skinned(s_), tree) * 1000:.1f} mm off the tackle at its closest")
        _pts = skinned(s_)
        _d = distances(_pts, tree)
        _own = skin[s_]["owner"]
        for _b in sorted(set(_own)):
            _m = _own == _b
            if _d[_m].min() < 0.0005:
                log(f"    {_b}: {_d[_m].min() * 1000:.1f} mm ({int((_d[_m] < 0).sum())} vertices inside)")


    yield "gotowe"


def finish(render):
    # ------------------------------------------------------------------ the skin back on, the scene saved
    armmod.show_viewport = True
    update()
    for o in (body, arm):
        o.hide_select = False
    text = bpy.data.texts.new("INSTRUKCJA")
    text.write("""Wedkarz z wedka (tools/assets/untex_grip.py):
      palce ustawione automatycznie: kazdy staw zgina sie, az skora palca dotknie rekojesci / galki (1 mm odstepu).
      Kosci: ramie_P/L, przedramie_P/L, dlon_P/L, srodrecze_P/L, kciuk/wskazujacy/srodkowy/serdeczny/maly_01..04_r/l.
      Wedka, Kolowrotek, Korba, Galka - uchwyty jak wczesniej.
    """)
    blend = os.path.join(OUT, "wedkarz_chwyt.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    log("saved", blend)

    if render:
        scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in {e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items} else "BLENDER_EEVEE"
        scene.render.resolution_x, scene.render.resolution_y = 1280, 960
        world = bpy.data.worlds.new("Swiat")
        world.use_nodes = True
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.6, 0.66, 1)
        world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
        scene.world = world
        sun = bpy.data.objects.new("Slonce", bpy.data.lights.new("Slonce", "SUN"))
        sun.data.energy = 3.5
        sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
        scene.collection.objects.link(sun)
        skin_mat = bpy.data.materials.new("Skora")
        skin_mat.diffuse_color = (0.78, 0.6, 0.5, 1)
        skin_mat.use_nodes = True
        skin_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.78, 0.6, 0.5, 1)
        if not body.data.materials:
            body.data.materials.append(skin_mat)
        camo = bpy.data.objects.new("Kamera", bpy.data.cameras.new("Kamera"))
        scene.collection.objects.link(camo)
        scene.camera = camo

        def shot(name, at, look, lens=50):
            camo.data.lens = lens
            camo.data.clip_start = 0.01
            camo.location = at
            camo.rotation_euler = (look - at).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(OUT, name + ".png")
            bpy.ops.render.render(write_still=True)
            log("render", scene.render.filepath)

        rh = (head("dlon_P") + head("srodkowy_02_r")) / 2
        lh = (head("dlon_L") + head("srodkowy_02_l")) / 2
        Xr, Yr, Zr = ROD.col[0], ROD.col[1], ROD.col[2]
        # the eye's view: FOV 70 vertical (sensor 24 mm height -> lens), 16:9 like the game
        camo.data.sensor_fit = "VERTICAL"
        camo.data.sensor_height = 24
        scene.render.resolution_x, scene.render.resolution_y = 1280, 720
        shot("oczy", eye, eye + Vector((0, -1, 0)), lens=12 / math.tan(math.radians(35)))
        scene.render.resolution_x, scene.render.resolution_y = 1100, 900
        camo.data.sensor_fit = "AUTO"
        # close-ups in the rod's frame: from the outside of each hand, from ahead (the tip's side), from above, from below
        for nm, centre, outside in (("prawa", rh, Xr), ("lewa", lh, -Xr)):
            for view, d in (("bok", outside + Zr * 0.15 - Yr * 0.2), ("przod", Zr + outside * 0.35 - Yr * 0.3),
                            ("gora", -Yr + outside * 0.25 + Zr * 0.1), ("dol", Yr * 0.8 + outside * 0.7 - Zr * 0.3),
                            ("tyl", -Zr + outside * 0.5 - Yr * 0.4)):
                shot(f"{nm}_{view}", centre + d.normalized() * 0.3, centre, lens=45)
        shot("calosc", seat_world + Vector((-1.4, -2.0, 0.35)), seat_world + Vector((0, 0, 0.2)), lens=35)
        shot("calosc_bok", seat_world + Vector((-2.3, 0.2, 0.25)), seat_world + Vector((0, 0, 0.15)), lens=40)


# ------------------------------------------------------------------ run: at once, or live in Blender's window
LIVE = "--live" in argv
FRAME = [1]


def key(label):
    """Every bone's turn keyed at the next frame: the posing can be replayed on the timeline."""
    for pb in arm.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=FRAME[0], group=pb.name)
    scene.frame_end = FRAME[0]
    scene.frame_current = FRAME[0]      # the view (and the saved file) on the pose just made, not the first key
    log(f"step {FRAME[0]:3d}: {label}")
    FRAME[0] += 6


for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
key("poza poczatkowa")
if not LIVE:
    for label in stages():
        key(label)
    finish(RENDER)
else:
    # Blender's own window: the skin follows the bones between the steps (off while a step computes: thousands of
    # skin updates would take minutes), the view on the hands, half a second per step
    gen = stages()
    for area in bpy.context.screen.areas if bpy.context.screen else []:
        if area.type == "VIEW_3D":
            sp = area.spaces.active
            sp.shading.type = "MATERIAL"
            r3 = sp.region_3d
            r3.view_location = seat_world + Vector((0.0, 0.0, 0.05))
            r3.view_distance = 0.9
            r3.view_rotation = Vector((-0.45, -1.0, 0.25)).normalized().to_track_quat("Z", "Y")
    armmod.show_viewport = True

    def tick():
        armmod.show_viewport = False
        try:
            label = next(gen)
        except StopIteration:
            finish(False)
            log("live: done, saved")
            return None
        key(label)
        armmod.show_viewport = True
        return 0.6

    bpy.app.timers.register(tick, first_interval=2.0)
