"""Render transparent shop thumbnails with Blender. Run from the repository root."""
import json
import math
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path.cwd()
MODELS = ROOT / "assets/models"
OUT = ROOT / "build/local/inventory_thumbnails"
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 12
scene.cycles.use_denoising = True
scene.render.resolution_x = scene.render.resolution_y = 192
scene.render.resolution_percentage = 100
scene.render.image_settings.color_mode = "RGBA"
scene.render.film_transparent = True
scene.world = bpy.data.worlds.new("Studio")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (.37,.43,.53,1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = .7
camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"
camera.data.clip_start = .001

for name, position, energy, size in (
    ("Key",(2,-3,4),530,2.2),("Fill",(-2,-2,1),260,2.5),("Rim",(1,2,3),500,1.8)
):
    light = bpy.data.lights.new(name,"AREA")
    light.energy,light.size=energy,size
    obj=bpy.data.objects.new(name,light)
    scene.collection.objects.link(obj)
    obj.location=position
    obj.rotation_euler=(-obj.location).to_track_quat("-Z","Y").to_euler()
studio = set(scene.objects)

def model_vector(values):
    x,y,z=values
    return Vector((x,-z,y))

def render(key, direction, roll=0):
    objects=[o for o in scene.objects if o not in studio]
    bpy.context.view_layer.update()
    points=[o.matrix_world @ Vector(v) for o in objects if o.type in {"MESH","CURVE"} for v in o.bound_box]
    center=Vector(tuple((min(p[k] for p in points)+max(p[k] for p in points))*.5 for k in range(3)))
    for o in objects:
        if o.parent not in objects:
            o.location-=center
    camera.location=Vector(direction).normalized()*7
    camera.rotation_euler=(-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.rotation_euler.rotate_axis("Z",math.radians(roll))
    inv=camera.rotation_euler.to_matrix().transposed()
    local=[inv@(p-center) for p in points]
    camera.data.ortho_scale=max(max(p[i] for p in local)-min(p[i] for p in local) for i in (0,1))*1.15
    scene.render.filepath=str(OUT/(key.replace("/","_")+".png"))
    bpy.ops.render.render(write_still=True)
    for o in objects:
        bpy.data.objects.remove(o,do_unlink=True)

def surface(name,color,metal=0,rough=.36):
    mat=bpy.data.materials.new(name)
    mat.use_nodes=True
    p=mat.node_tree.nodes.get("Principled BSDF")
    p.inputs["Base Color"].default_value=(*color,1)
    p.inputs["Metallic"].default_value=metal
    p.inputs["Roughness"].default_value=rough
    return mat

silver=surface("Steel",(.47,.48,.48),.85)
dark=surface("Graphite",(.10,.135,.14),.45)
olive=surface("Braid",(.29,.43,.075),.12)
glow=surface("Glow stick",(.59,.85,.11),0,.28)
mono=surface("Monofilament",(.37,.61,.63),.16)
fluoro=surface("Fluorocarbon",(.68,.70,.63),.24)
worm=surface("Worm",(.33,.067,.043),0,.42)
corn=surface("Corn",(.86,.49,.025),0,.39)
berry=surface("Boilies",(.46,.115,.047),0,.64)

def assign(mat):
    o=bpy.context.object
    o.data.materials.append(mat)
    for face in o.data.polygons: face.use_smooth=True

def cylinder(radius, depth, at, mat):
    bpy.ops.mesh.primitive_cylinder_add(vertices=48,radius=radius,depth=depth,location=at)
    assign(mat)

def ball(at, scale, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=at)
    bpy.context.object.scale=scale
    assign(mat)

def stroke(points, radius, mat, closed=False):
    curve=bpy.data.curves.new("Profile","CURVE")
    curve.dimensions="3D"
    curve.resolution_u=16
    curve.bevel_depth=radius
    curve.bevel_resolution=4
    curve.use_fill_caps=True
    spline=curve.splines.new("BEZIER")
    spline.bezier_points.add(len(points)-1)
    for p,at in zip(spline.bezier_points,points):
        p.co=at
        p.handle_left_type=p.handle_right_type="AUTO"
    spline.use_cyclic_u=closed
    obj=bpy.data.objects.new("Profile",curve)
    scene.collection.objects.link(obj)
    curve.materials.append(mat)

for folder in ("rods","reels"):
    for path in sorted((MODELS/folder).glob("*.glb")):
        bpy.ops.import_scene.gltf(filepath=str(path))
        parts={o.name.split(".")[0]:o for o in scene.objects if o not in studio and o.type=="MESH"}
        if folder=="rods":
            for name,o in parts.items():
                if name.startswith("Seg"):
                    o.location=(0,0,.25+.30*int(name[3:]))
            render(folder+"/"+path.stem,(.12,-1,.01),-49)
        else:
            data=json.loads(path.with_suffix(".json").read_text(encoding="utf8"))
            for name,key in (("Rotor","rotor"),("Crank","hub"),("Bail","bail_pivot")):
                if name in parts and key in data:
                    parts[name].location=model_vector(data[key])
            if "Bail" in parts and "Rotor" in parts:
                parts["Bail"].location+=parts["Rotor"].location
            render(folder+"/"+path.stem,(-1,-1.3,.65),-18)

for key,mat in (("mono",mono),("braid",olive),("fluoro",fluoro)):
    cylinder(.34,.25,(0,0,0),mat)
    for z in (-.15,.15):
        cylinder(.39,.047,(0,0,z),dark)
        cylinder(.13,.027,(0,0,z*1.2),silver)
    for i in range(10):
        bpy.ops.mesh.primitive_torus_add(major_segments=40,minor_segments=8,major_radius=.337,minor_radius=.012,location=(0,0,-.107+i*.023))
        assign(mat)
    render("line_"+key,(.4,-1,1.25),-16)

stroke([(.18,0,.37),(.17,0,.14),(.19,0,-.09),(.08,0,-.25),(-.13,0,-.23),(-.21,0,-.08),(-.17,0,.095)],.026,silver)
stroke([(.18+.046*math.cos(i*math.tau/12),0,.415+.046*math.sin(i*math.tau/12)) for i in range(12)],.018,silver,True)
stroke([(-.20,0,-.01),(-.10,0,-.055)],.012,silver)
render("hook",(.16,-1,.36),-27)

stroke([(-.42,0,0),(-.25,-.15,.045),(-.08,.13,.034),(.09,.18,.045),(.27,-.05,.024),(.41,.07,.007)],.057,worm)
render("bait_worm",(.1,-.8,1.3),-8)
for key,mat,radius in (("bait_corn",corn,.115),("bait_carp",berry,.14)):
    for x,y,z in [(-.22,0,0),(0,.04,0),(.22,0,0),(-.12,-.13,.01),(.09,-.12,.01),(.13,.15,0),(-.03,.08,.15)]:
        ball((x,y,z),(radius,radius*.83,radius*.79),mat)
    render(key,(.35,-1,1.1),-10)

bpy.ops.import_scene.gltf(filepath=str(MODELS/"wooden_bucket_01.glb"))
render("wiadro",(.65,-1,1.05),-8)

ball((0,0,0),(.092,.092,.20),dark)
for z in (-.26,.26):
    stroke([(.083*math.cos(i*math.tau/16),0,z+.083*math.sin(i*math.tau/16)) for i in range(16)],.019,silver,True)
render("kretlik",(.13,-1,.15),-29)

cylinder(.061,.79,(0,0,0),glow)
for z in (-.39,.39):
    ball((0,0,z),(.061,.061,.061),glow)
render("swietlik",(.16,-1,.4),-40)

bpy.ops.mesh.primitive_cube_add(size=1)
stone=bpy.context.object
stone.scale=(.45,.68,.17)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bevel=stone.modifiers.new("Rounded edges","BEVEL")
bevel.width=.045
bevel.segments=4
assign(surface("Whetstone",(.25,.29,.27),0,.84))
render("oselka",(.7,-1,1.7),-12)
