"""Makes a Blender file for posing the angler's hands on the rod by hand, from a posing file of untex_grip.py:

    blender -b Test/Fisherman_Untex_chwyt/wedkarz_chwyt.blend --python tools/assets/prepare_manual_pose.py -- OUT.blend

* the pose the game uses now (the last key of the automatic posing) is the starting pose; the animation is removed, so
  nothing on the timeline overrides what you set;
* every bone may only rotate (location and scale locked: the rule for this figure);
* a camera "Widok_z_gry" at the eyes with the game's view (FOV 70 vertical, 16:9), set as the scene's camera:
  Numpad 0 shows what the player sees;
* the rod, reel and crank are moved by their axis handles (the meshes stay unselectable, so a click on the tackle
  never moves one piece of it alone);
* the skeleton is selected and in Pose Mode, its round handles shown; a text "INSTRUKCJA" in the file says how to
  pose and how to hand the file back.
Afterwards tools/assets/export_untex_grip.py turns the saved file into assets/characters/angler_untex.glb.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out = os.path.abspath(argv[0]) if argv else os.path.join(os.path.dirname(bpy.data.filepath), "do_ulozenia_dloni.blend")
scene = bpy.context.scene
figure = bpy.data.objects["Wedkarz"]
arm = bpy.data.objects["Szkielet"]

# the finished pose, kept as the pose itself (no keys left)
scene.frame_set(scene.frame_end)
bpy.context.view_layer.update()
pose_now = {pb.name: (pb.rotation_mode, pb.rotation_quaternion.copy(), pb.rotation_euler.copy()) for pb in arm.pose.bones}
if arm.animation_data:
    arm.animation_data_clear()
for pb in arm.pose.bones:
    mode, q, e = pose_now[pb.name]
    pb.rotation_mode = mode
    pb.rotation_quaternion = q
    pb.rotation_euler = e
    pb.location = (0.0, 0.0, 0.0)
    pb.scale = (1.0, 1.0, 1.0)
    pb.lock_location = (True, True, True)
    pb.lock_scale = (True, True, True)
    # barely visible wire beads, 1.5-6 mm (the shape's size is in metres here: use_custom_shape_bone_size is off, so
    # a "scale" of 0.15 made 15 cm spheres that hid the arm and the fingers being posed)
    r = min(0.006, max(0.0015, 0.08 * pb.bone.length))
    pb.custom_shape_scale_xyz = (r, r, r)
    pb.bone.show_wire = True
scene.frame_start = scene.frame_end = scene.frame_current = 1
# the skin follows the bones in the viewport (the automatic posing switched that off while it computed)
for m in figure.modifiers:
    if m.type == "ARMATURE":
        m.show_viewport = True
        m.show_render = True
bpy.context.view_layer.update()

# the game's view from the eyes (as export_untex_grip.py finds them: the head's middle just behind the face)
dg = bpy.context.evaluated_depsgraph_get()
ev = figure.evaluated_get(dg)
verts = [figure.matrix_world @ v.co for v in ev.data.vertices]
top = max(v.z for v in verts)
head = [v for v in verts if v.z > top - 0.28 and abs(v.x) < 0.12]
centre = sum(head, Vector()) / len(head)
eye = Vector((centre.x, min(v.y for v in head) + 0.05, top - 0.115))
cam_data = bpy.data.cameras.get("Widok_z_gry") or bpy.data.cameras.new("Widok_z_gry")
cam = bpy.data.objects.get("Widok_z_gry") or bpy.data.objects.new("Widok_z_gry", cam_data)
if cam.name not in scene.collection.objects:
    scene.collection.objects.link(cam)
cam_data.sensor_fit = "VERTICAL"
cam_data.sensor_height = 24.0
cam_data.lens = 12.0 / math.tan(math.radians(35.0))      # 70 degrees vertical
cam_data.clip_start = 0.01
# just in front of the face (from inside the head the cap's brim filled the view), looking down at the hands, as the
# angler looks at his own hands on the rod (in the game the arms are framed at the bottom of the view)
hands = (arm.matrix_world @ arm.pose.bones["dlon_P"].head + arm.matrix_world @ arm.pose.bones["dlon_L"].head) / 2
cam.location = eye + Vector((0.0, -0.09, 0.0))
cam.rotation_euler = (hands + Vector((0.0, -0.12, 0.0)) - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.camera = cam
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080

# daylight for rendering and the rendered viewport (the posing file had neither a world nor a light)
world = bpy.data.worlds.get("Swiat") or bpy.data.worlds.new("Swiat")
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
if bg is not None:
    bg.inputs[0].default_value = (0.55, 0.6, 0.66, 1.0)
    bg.inputs[1].default_value = 0.9
scene.world = world
if bpy.data.objects.get("Slonce") is None:
    sun = bpy.data.objects.new("Slonce", bpy.data.lights.new("Slonce", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
    scene.collection.objects.link(sun)
    sun.hide_select = True

# the tackle: moved and turned by its axis handles (Wedka = the whole rod, Kolowrotek = the reel, Korba = the crank);
# the meshes stay unselectable (a click on one blank section would move that piece alone)
for name in ("Wedka", "Kolowrotek", "Korba", "Galka"):
    o = bpy.data.objects.get(name)
    if o is None:
        continue
    for c in o.children_recursive:
        if c.type == "MESH":
            c.hide_select = True
    if name == "Galka":
        o.hide_select = True
        continue
    o.hide_select = False
    o.empty_display_type = "ARROWS"
    o.empty_display_size = {"Wedka": 0.12, "Kolowrotek": 0.06, "Korba": 0.035}[name]
    o.show_in_front = True
    o.show_name = True
    o.lock_scale = (True, True, True)

# ready to pose: the skeleton active, in Pose Mode
for o in scene.objects:
    o.select_set(False)
figure.hide_select = True
arm.hide_set(False)
arm.hide_select = False
arm.show_in_front = True
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="POSE")

txt = bpy.data.texts.get("INSTRUKCJA") or bpy.data.texts.new("INSTRUKCJA")
txt.clear()
txt.write("""Ukladanie dloni wedkarza na wedce (FishingSim)

1. Szkielet jest zaznaczony i w trybie Pose Mode. Kliknij kosc (okragly uchwyt), R obraca, R R obraca swobodnie,
   R X / R Y / R Z obraca wokol osi. Przesuwanie (G) i skalowanie (S) sa zablokowane: tylko obroty.
2. Kosci: ramie_P/L (ramie), przedramie_P/L, przedramie_skret_P/L (skret przedramienia), dlon_P/L (nadgarstek),
   srodrecze_P/L (dlon), palce: kciuk / wskazujacy / srodkowy / serdeczny / maly _01.._04 _r (prawa) / _l (lewa).
   _01 to nasada palca, _04 koniuszek. P / _r = prawa reka (na rekojesci), L / _l = lewa (na galce korbki).
3. Numpad 0: widok kamery "Widok_z_gry": z oczu wedkarza w dol na dlonie (jak patrzy na wlasne rece).
4. Wedke, kolowrotek i korbke ustawiasz strzalkami osi z napisami Wedka / Kolowrotek / Korba (w trybie Object
   Mode: Ctrl+Tab wychodzi z Pose Mode; klik w strzalki, G przesuwa, R obraca). Wedka niesie caly zestaw, Kolowrotek
   sam kolowrotek, Korba obraca korbke z galka. Siatki sa nieklikalne. Skalowanie zablokowane.
5. Alt+R na zaznaczonych kosciach wraca je do pozycji spoczynkowej (T-poza), Ctrl+Z cofa.
6. Zapisz (Ctrl+S) pod ta sama nazwa i napisz Claude'owi, ze gotowe: wyeksportuje to do gry
   (tools/assets/export_untex_grip.py -> assets/characters/angler_untex.glb).
""")
bpy.ops.wm.save_as_mainfile(filepath=out)
print("MANUAL saved", out, "eye", tuple(round(c, 3) for c in eye))
if "--render" in argv:
    # a check of the game's view (not saved into the file)
    bpy.ops.object.mode_set(mode="OBJECT")
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in {e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items} else "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 960, 540
    scene.render.filepath = os.path.splitext(out)[0] + "_widok.png"
    bpy.ops.render.render(write_still=True)
    print("MANUAL render", scene.render.filepath)
