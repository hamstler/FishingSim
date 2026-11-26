"""Adds the game's rod and reel to a figure .blend as separate models, each with its own handle for setting it by hand.

  * "Wedka" (arrows): the whole rod (handle and the ten blank sections, straight), moved and turned as one;
  * "Kolowrotek" (arrows): the reel, under the rod's reel seat and hanging on the rod (it follows the rod; Alt+P frees it);
  * "Korba" (circle): the crank's pivot on the reel, turns only about its own axis;
  * "Galka" (sphere): the knob's point on the crank, for hanging a hand or anything else on it (Child Of / Copy
    Location), going round with the crank.
The meshes themselves cannot be picked (only the handles), so nothing is moved by accident. The rod is laid in the
figure's right hand (its reel seat at the hand), pointing ahead and up.

    blender -b FIGURE.blend --python tools/assets/add_tackle.py -- [rod_id] [reel_id]
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROD_ID = argv[0] if argv else "rods/float_ostoja"
REEL_ID = argv[1] if len(argv) > 1 else "reels/small_hokuto"
scene = bpy.context.scene


def g2b(v):
    return Vector((v[0], -v[2], v[1]))


def load(path):
    before = set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in scene.objects if o not in before]
    for o in new:
        if o.type == "MESH" and o.name.startswith("Icosphere"):
            bpy.data.objects.remove(o, do_unlink=True)
    return {o.name.split(".")[0]: o for o in new if o.name in bpy.data.objects and bpy.data.objects[o.name].type == "MESH"}


def handle(name, shape, size, coll):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type, e.empty_display_size, e.show_in_front = shape, size, True
    coll.objects.link(e)
    return e


def move_to(obj, coll):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)


coll = bpy.data.collections.get("Wedka i kolowrotek") or bpy.data.collections.new("Wedka i kolowrotek")
if coll.name not in scene.collection.children:
    scene.collection.children.link(coll)

# the rod: its parts on one handle, in the rig's frame (blank along +Z, the reel's side +Y)
rod = load(os.path.join(ROOT, "assets", "models", ROD_ID + ".glb"))
wedka = handle("Wedka", "ARROWS", 0.15, coll)
for name, o in rod.items():
    move_to(o, coll)
    o.parent = wedka
    if name.startswith("Seg"):
        o.location = Vector((0, 0, 0.25 + 0.3 * int(name[3:])))
    o.hide_select = True

# the reel: its own handle under the seat, the rotor (with the bail), the crank on its pivot with the knob's point
reel = load(os.path.join(ROOT, "assets", "models", REEL_ID + ".glb"))
frames = json.load(open(os.path.join(ROOT, "assets", "models", REEL_ID + ".json"), encoding="utf-8"))
kolowrotek = handle("Kolowrotek", "ARROWS", 0.08, coll)
kolowrotek.parent = wedka
for name, o in reel.items():
    move_to(o, coll)
    o.parent = kolowrotek
    o.hide_select = True
reel["Rotor"].location = g2b(frames["rotor"])
if "Bail" in reel:
    reel["Bail"].parent = reel["Rotor"]
    reel["Bail"].location = g2b(frames["bail_pivot"])
korba = handle("Korba", "CIRCLE", 0.04, coll)
korba.parent = kolowrotek
korba.location = g2b(frames["hub"])
korba.rotation_mode = "XYZ"
korba.lock_location = (True, True, True)
korba.lock_rotation = (False, True, True)          # only about the crank's own axis
reel["Crank"].parent = korba
galka = handle("Galka", "SPHERE", 0.012, coll)
galka.parent = korba
galka.location = g2b((frames["knob_out"], 0.0, -frames["arm"]))   # RodRig.KnobAt at angle 0
galka.lock_location = (True, True, True)

# into the figure's right hand: the reel seat (0.1 m up the rig) at the hand, the blank ahead and up
arm = bpy.data.objects.get("Szkielet")
bone = "RightHand" if arm and "RightHand" in arm.pose.bones else None
if bone:
    hand = arm.matrix_world @ arm.pose.bones[bone].head
else:
    hand = Vector((-0.45, -0.1, 1.0))
axis = Vector((0.0, -0.8, 0.6)).normalized()
down = (Vector((0, 0, -1)) - axis * (-axis.z)).normalized()
x = down.cross(axis)
rot = Matrix((x, down, axis)).transposed()
origin = hand + Vector((0.0, -0.08, 0.0)) - rot @ Vector((0, 0, 0.1))
wedka.matrix_world = Matrix.Translation(origin) @ rot.to_4x4()
bpy.context.view_layer.update()

text = bpy.data.texts.get("INSTRUKCJA") or bpy.data.texts.new("INSTRUKCJA")
text.write('''
Wedka i kolowrotek (kolekcja "Wedka i kolowrotek"; same modele sa zablokowane, chwyta sie uchwyty):
  Wedka      - strzalki: cala wedka naraz (G przesuwa, R obraca).
  Kolowrotek - strzalki pod uchwytem: kolowrotek; wisi na wedce i idzie za nia (Alt+P odpina go od wedki).
  Korba      - kolko: obrot korby tylko wokol jej osi (R X).
  Galka      - kulka na korbie: punkt zaczepienia; kreci sie z korba. Dlon (albo cos innego) mozna do niej przypiac:
               zaznacz kosc / obiekt, Object/Bone Constraint > Child Of albo Copy Location, Target: Galka.
''')
print("TACKLE rod", sorted(rod), "reel", sorted(reel), "seat at", tuple(round(v, 3) for v in origin + rot @ Vector((0, 0, 0.1))))
bpy.ops.wm.save_mainfile()
print("TACKLE saved", bpy.data.filepath)
