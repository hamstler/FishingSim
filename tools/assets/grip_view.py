"""Renders the angler's grips on the rod as the game poses them, in Blender (background, no window).

The game's hands-only smoke test (FISHSIM_SMOKE_HANDS=1) writes build/local/shots/grip_pose.json: every bone of the
angler (assets/characters/player_angler.glb) at rest and as posed, and the rod's and the reel's parts, as 4x4 matrices in
the angler model's own space (Unity: left-handed, Y up). This script loads the same glb files, poses the skeleton and
places the parts exactly so, and renders close-ups of the right hand on the handle and the left hand on the crank's knob
from several sides, so the grips can be judged on the real meshes (fingers through the handle, hands in the air).

    blender -b --factory-startup --python tools/assets/grip_view.py -- build/local/shots/grip_pose.json OUT_DIR

Coordinates: glTFast imports glTF (right-handed, Y up) into Unity by negating X; Blender's importer turns glTF into
Z up as (x, -z, y). A Unity point (x, y, z) is therefore Blender's (-x, -z, y), and a Unity matrix M becomes C M C^-1.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
C = Matrix(((-1.0, 0.0, 0.0, 0.0), (0.0, 0.0, -1.0, 0.0), (0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0)))
C_INV = C.inverted()


def unity(m16):
    """A row-major Unity matrix from the dump, as a Blender world matrix."""
    m = Matrix([m16[0:4], m16[4:8], m16[8:12], m16[12:16]])
    return C @ m @ C_INV


def import_glb(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def world_verts(obj):
    """An object's evaluated (posed, deformed) vertices in world space."""
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    mesh = ev.to_mesh()
    pts = [ev.matrix_world @ v.co for v in mesh.vertices]
    ev.to_mesh_clear()
    return pts


def measure(arm, angler, pose):
    """Where the reel's stem stands on the handle against the right hand's knuckles (middle/ring gap), along the rod."""
    objs = {o.name.split(".")[0]: o for o in bpy.data.objects if o.type == "MESH"}
    handle = objs.get("Handle")
    body = objs.get("Body")
    if handle is None or body is None:
        print("GRIPVIEW measure: no Handle/Body")
        return
    # the rod's axis: the handle's long principal direction
    pts = world_verts(handle)
    centre = sum(pts, Vector()) / len(pts)
    cov = [[0.0] * 3 for _ in range(3)]
    for p in pts:
        d = p - centre
        for i in range(3):
            for j in range(3):
                cov[i][j] += d[i] * d[j]
    axis = Vector((1, 1, 1)).normalized()
    m = Matrix(cov)
    for _ in range(50):
        axis = (m @ axis).normalized()
    grip = unity(pose["grip"]).translation
    # towards the tip: from the handle to the blank's first section (the knob can lie either side of the grip point)
    seg0 = objs.get("Seg0")
    tip_ward = (seg0.matrix_world.translation if seg0 is not None else unity(pose["knob"]).translation) - centre
    if tip_ward.dot(axis) < 0:
        axis = -axis

    def along(p):
        return (p - grip).dot(axis)

    def off_axis(p):
        d = p - grip
        return (d - axis * d.dot(axis)).length

    radius = sorted(off_axis(p) for p in pts)[len(pts) // 2]
    foot = [p for p in world_verts(body) if off_axis(p) < radius + 0.02]
    stem = sum((along(p) for p in foot), 0.0) / max(len(foot), 1)
    pb = arm.pose.bones
    w = arm.matrix_world
    gap = (along(w @ pb["middle_01_r"].head) + along(w @ pb["ring_01_r"].head)) * 0.5
    index = along(w @ pb["index_01_r"].head)
    pinky = along(w @ pb["pinky_01_r"].head)
    print(f"GRIPVIEW handle radius {radius * 1000:.1f} mm, axis {tuple(round(v, 3) for v in axis)}")
    print(f"GRIPVIEW along the rod from the grip point: reel stem {stem * 1000:.0f} mm (foot {len(foot)} verts), "
          f"index knuckle {index * 1000:.0f}, middle/ring gap {gap * 1000:.0f}, little finger {pinky * 1000:.0f} mm")
    print(f"GRIPVIEW slide the hand {(stem - gap) * 1000:.0f} mm along the rod to put the stem between middle and ring")


FINGERS = ("index", "middle", "ring", "pinky", "thumb")

# the game's first-person skin tint (FirstPersonBody.SkinTint)
SKIN_TINT = (0.86, 0.74, 0.64)


def dress(angler, pose):
    """The arms' pictures as the game shows them (the glb carries none: assets/characters/<id>.json names them)."""
    info_path = os.path.join(ROOT, "assets", pose["angler"] + ".json")
    if not os.path.exists(info_path):
        return
    info = json.load(open(info_path, encoding="utf-8"))
    tex_dir = os.path.join(ROOT, "assets", "characters", "textures")
    for o in angler:
        if o.type != "MESH" or o.hide_render:
            continue
        for slot in o.material_slots:
            mat = slot.material
            if mat is None:
                continue
            spec = info["materials"].get(mat.name.split(".")[0])
            if spec is None:
                continue
            mat.use_nodes = True
            nodes, links = mat.node_tree.nodes, mat.node_tree.links
            bsdf = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
            if bsdf is None:
                continue
            albedo = os.path.join(tex_dir, spec["albedo"][0] + ".webp")
            if os.path.exists(albedo):
                img = nodes.new("ShaderNodeTexImage")
                img.image = bpy.data.images.load(albedo)
                if mat.name.startswith("skin"):
                    tint = nodes.new("ShaderNodeMix")
                    tint.data_type = "RGBA"
                    tint.blend_type = "MULTIPLY"
                    tint.inputs["Factor"].default_value = 1.0
                    links.new(img.outputs["Color"], tint.inputs[6])
                    tint.inputs[7].default_value = (*SKIN_TINT, 1.0)
                    links.new(tint.outputs[2], bsdf.inputs["Base Color"])
                    bsdf.inputs["Subsurface Weight"].default_value = 0.15
                else:
                    links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
            normal = os.path.join(tex_dir, spec.get("normal", "") + ".webp")
            if spec.get("normal") and os.path.exists(normal):
                nimg = nodes.new("ShaderNodeTexImage")
                nimg.image = bpy.data.images.load(normal)
                nimg.image.colorspace_settings.name = "Non-Color"
                nmap = nodes.new("ShaderNodeNormalMap")
                nmap.inputs["Strength"].default_value = 0.5   # the game reads them at 0.6 (NormalScale)
                links.new(nimg.outputs["Color"], nmap.inputs["Color"])
                links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
            bsdf.inputs["Roughness"].default_value = spec.get("roughness_value", 0.6)


def fit_fingers(arm, arms_mesh, targets, side, out, axis_fix, bones):
    """
    Closes each finger of one hand joint by joint towards its target until the finger's own (skinned) vertices touch the
    target's surface (1.5 mm) without going in: the knuckle first, then the middle and the tip joints. Writes each finger
    joint's local rotation in the game's convention (Unity, relative to its parent bone) into out[bone].
    """
    from mathutils.bvhtree import BVHTree
    deps = bpy.context.evaluated_depsgraph_get()
    trees = [BVHTree.FromObject(t, deps) for t in targets]
    # each bone's vertices (weight > 0.4) from the original mesh: the evaluated (deformed) copy keeps the vertices'
    # order but not their groups
    names_by_index = {g.index: g.name for g in arms_mesh.vertex_groups}
    by_bone = {}
    for v in arms_mesh.data.vertices:
        for g in v.groups:
            if g.weight > 0.4 and g.group in names_by_index:
                by_bone.setdefault(names_by_index[g.group], []).append(v.index)

    print("GRIPFIT groups", len(names_by_index), "e.g.", list(names_by_index.values())[:8],
          "finger verts", {k: len(v) for k, v in by_bone.items() if k.endswith("_" + side) and "_0" in k})
    print("GRIPFIT targets", [(t.name, len(t.data.vertices)) for t in targets],
          "nearest to the first target's origin", trees[0].find_nearest(targets[0].matrix_world.translation) if trees else None)

    # the trees are in each object's own space: queries go in through the inverse, hits come back out
    spaces = [(t.matrix_world.copy(), t.matrix_world.inverted(), t.matrix_world.to_3x3().inverted().transposed()) for t in targets]

    def nearest(p):
        best = None
        for tree, (to_world, to_local, normal_m) in zip(trees, spaces):
            hit = tree.find_nearest(to_local @ p)
            if hit[0] is None:
                continue
            loc = to_world @ hit[0]
            dist = (p - loc).length
            if best is None or dist < best[3]:
                best = (loc, (normal_m @ hit[1]).normalized(), hit[2], dist)
        return best

    def clearance(names, skip_near=None):
        """The closest a finger's vertices (of these bones, weight > 0.4) come to the target; negative when inside.
        Vertices within 12 mm of skip_near (the turning joint, already resting on the target) do not count."""
        deps = bpy.context.evaluated_depsgraph_get()
        ev = arms_mesh.evaluated_get(deps)
        mesh = ev.to_mesh()
        best = 1.0
        for name in names:
            for i in by_bone.get(name, ()):
                p = ev.matrix_world @ mesh.vertices[i].co
                if skip_near is not None and (p - skip_near).length < 0.012:
                    continue
                hit = nearest(p)
                if hit is not None:
                    d = hit[3]
                    if (p - hit[0]).dot(hit[1]) < 0:
                        d = -d
                    best = min(best, d)
        ev.to_mesh_clear()
        return best

    target_centre = sum((t.matrix_world.translation for t in targets), Vector()) / len(targets)
    w = arm.matrix_world
    for finger in FINGERS:
        chain = [arm.pose.bones.get(f"{finger}_0{i}_{side}") for i in (1, 2, 3)]
        if any(b is None for b in chain):
            continue
        # straight first (each joint at its rest turn under its parent as posed now): the game's fixed curl may already
        # have the finger through the handle
        for j, pb in enumerate(chain):
            if finger == "thumb" and j == 0:
                continue
            parent_node = w @ pb.parent.matrix @ axis_fix[pb.parent.name]
            want = parent_node @ unity(bones[pb.parent.name]["rest"]).inverted() @ unity(bones[pb.name]["rest"])
            pb.matrix = w.inverted() @ want @ axis_fix[pb.name].inverted()
            bpy.context.view_layer.update()
        for j, pb in enumerate(chain):
            if finger == "thumb" and j == 0:
                continue   # the thumb's root keeps the game's placing (along the handle / on the knob)
            names = {b.name for b in chain[j:]}
            head = w @ pb.head
            seg = (w @ pb.tail) - head
            hit = nearest(head)
            toward = (hit[0] - head) if hit is not None else (target_centre - head)
            axis = seg.cross(toward)
            if axis.length < 1e-6:
                continue
            axis.normalize()
            start = clearance(names, head)
            limit, step, turned = 100.0, 3.0, 0.0
            while turned < limit:
                rot = Matrix.Translation(head) @ Matrix.Rotation(math.radians(step), 4, axis) @ Matrix.Translation(-head)
                before = pb.matrix.copy()
                pb.matrix = w.inverted() @ rot @ w @ pb.matrix
                bpy.context.view_layer.update()
                c = clearance(names, head)
                if c < 0.0015 and not (start < 0.0015 and c >= start):
                    if c < 0:
                        pb.matrix = before   # went in: back to the last step outside
                        bpy.context.view_layer.update()
                    break
                turned += step
            print(f"GRIPFIT {pb.name}: {turned:.0f} deg, clearance {clearance(names) * 1000:.1f} mm")
        for pb in chain:
            # the joint's rotation relative to its parent, in the game's (Unity) frame: the glTF nodes' frames (Blender's
            # bones carry the importer's own axis turn, axis_fix, taken off here)
            parent_u = C_INV @ (w @ pb.parent.matrix @ axis_fix[pb.parent.name]) @ C
            mine_u = C_INV @ (w @ pb.matrix @ axis_fix[pb.name]) @ C
            local = (parent_u.inverted() @ mine_u).to_3x3().normalized().to_quaternion()
            # Unity quaternion (x, y, z, w); Blender's (w, x, y, z)
            out[pb.name] = [local.x, local.y, local.z, local.w]


def build_scene(pose):
    """The grips' scene as the game shows it: the posed arms (with their pictures), the rod's and the reel's parts.
    Returns (arm, angler objects, axis_fix, bones)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)

    # the angler: the arms' mesh only (the body and Meshy's fists are hidden in first person too)
    angler = import_glb(os.path.join(ROOT, "assets", pose["angler"] + ".glb"))
    arm = next(o for o in angler if o.type == "ARMATURE")
    for o in angler:
        if o.type == "MESH" and (o.name.startswith("Body") or o.name.startswith("Arms_Meshy") or o.parent is None):
            o.hide_render = True
            o.hide_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    arm_w = arm.matrix_world.copy()
    arm_w_inv = arm_w.inverted()
    bones = pose["bones"]

    def depth(pb):
        d, p = 0, pb.parent
        while p is not None:
            d, p = d + 1, p.parent
        return d

    # the importer's pose is the glTF nodes' own transforms, which is the game's rest (the edit bones' rest is the skin's
    # bind pose, meshy_player_rig.py's fitted pose: not the same); each bone's move from that pose to the game's
    imported = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
    # bone frame -> glTF node frame: the node's rest (the game's) against the imported bone's (armature space)
    axis_fix = {}
    for pb in arm.pose.bones:
        if pb.name in bones:
            axis_fix[pb.name] = (arm_w @ imported[pb.name]).inverted() @ unity(bones[pb.name]["rest"])
        else:
            axis_fix[pb.name] = Matrix.Identity(4)
    for pb in sorted(arm.pose.bones, key=depth):
        b = bones.get(pb.name)
        if b is None:
            continue
        rest_w, now_w = unity(b["rest"]), unity(b["now"])
        pb.matrix = arm_w_inv @ now_w @ rest_w.inverted() @ arm_w @ imported[pb.name]
        bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    for name in ("hand_r", "hand_l", "upperarm_r"):
        if name in arm.pose.bones and name in bones:
            got = arm_w @ arm.pose.bones[name].head
            want = unity(bones[name]["now"]).translation
            rest = unity(bones[name]["rest"]).translation
            print("GRIPVIEW bone", name, "posed", tuple(round(v, 3) for v in got), "game", tuple(round(v, 3) for v in want),
                  "rest game", tuple(round(v, 3) for v in rest), "rest blender", tuple(round(v, 3) for v in arm_w @ arm.data.bones[name].head_local))
    for o in angler:
        if o.type == "MESH":
            print("GRIPVIEW mesh", o.name, "hidden" if o.hide_render else "shown", "parent", o.parent.name if o.parent else None)

    # the rod's and the reel's parts where the game shows them
    for which in ("rod", "reel"):
        if not pose.get(which):
            continue
        objs = import_glb(os.path.join(ROOT, "assets", "models", pose[which] + ".glb"))
        by_name = {}
        for o in objs:
            by_name.setdefault(o.name.split(".")[0], o)
        placed = set()
        for part in pose["parts"]:
            if part["glb"] != which or part["node"] not in by_name:
                continue
            o = by_name[part["node"]]
            o.parent = None
            o.matrix_world = unity(part["m"])
            placed.add(o)
        for o in objs:
            if o.type == "MESH" and o not in placed:
                o.hide_render = True

    dress(angler, pose)
    return arm, angler, axis_fix, bones


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    pose_path, out_dir = os.path.abspath(argv[0]), os.path.abspath(argv[1])   # (render paths are not relative to the shell)
    os.makedirs(out_dir, exist_ok=True)
    pose = json.load(open(pose_path, encoding="utf-8"))
    arm, angler, axis_fix, bones = build_scene(pose)
    scene = bpy.context.scene
    measure(arm, angler, pose)

    # --fit OUT.json: close the fingers onto the handle (right) and the crank's knob (left) and save the joints
    if "--fit" in argv:
        fit_path = os.path.abspath(argv[argv.index("--fit") + 1])
        objs = {o.name.split(".")[0]: o for o in bpy.data.objects if o.type == "MESH" and not o.hide_render}
        arms_mesh = next(o for o in angler if o.type == "MESH" and o.name.split(".")[0] == "Arms")
        out = {}
        right = [objs[n] for n in ("Handle", "Body") if n in objs]
        left = [objs[n] for n in ("Crank",) if n in objs]
        if right:
            fit_fingers(arm, arms_mesh, right, "r", out, axis_fix, bones)
        if left:
            fit_fingers(arm, arms_mesh, left, "l", out, axis_fix, bones)
        json.dump({"fingers": out}, open(fit_path, "w", encoding="utf-8"), indent=1)
        print("GRIPFIT wrote", len(out), "joints to", fit_path)

    # light: a sun from above the left shoulder, a soft sky
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.7, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    scene.world = world
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x, scene.render.resolution_y = 1280, 960
    scene.render.film_transparent = False

    grip = unity(pose["grip"]).translation
    knob = unity(pose["knob"]).translation
    mid = (grip + knob) * 0.5
    # the camera's own frame (Blender world): the angler model faces Unity +Z = Blender -Y? read from the dump's eye
    up = Vector((0, 0, 1))
    fwd = (Vector(unity(bones["head"]["now"]).col[1][:3]) if "head" in bones else Vector((0, 1, 0)))
    views = {
        "right": (grip, Vector((-0.3, 0.0, 0.05))),
        "left": (knob, Vector((0.3, 0.0, 0.08))),
        "below": (mid, Vector((0.0, 0.0, -0.4))),
        "above": (mid, Vector((0.0, 0.02, 0.45))),
        "front": (mid, Vector((0.0, -0.5, 0.1))),
        "back": (mid, Vector((0.0, 0.45, 0.25))),
    }
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 40
    cam_data.clip_start = 0.005
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name, (target, offset) in views.items():
        cam.location = target + offset
        direction = target - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"blender_{name}.png")
        bpy.ops.render.render(write_still=True)
    print("GRIPVIEW rendered", ", ".join(views), "into", out_dir)
    print("GRIPVIEW grip", tuple(round(v, 3) for v in grip), "knob", tuple(round(v, 3) for v in knob))


if __name__ == "__main__":
    main()
