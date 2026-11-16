"""
Look-dev rig for the Tung Sahur model (run after build_tung_sahur.py in the same scene).
Poses the arms down with the bat hanging from the right fist, adds a floor, warm
lamp lighting similar to the reference, and two cameras (full body / face).
"""
import bpy
import math
from mathutils import Vector, Matrix

scene = bpy.data.scenes.get("TungSahur") or bpy.context.scene
arm = scene.objects["Armature"]
club = scene.objects["TungSahur_Club"]

# ---- pose
for pb in arm.pose.bones:
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (0, 0, 0)
P = arm.pose.bones
P["LeftUpperArm"].rotation_euler = (math.radians(-74), math.radians(0), math.radians(8))
P["RightUpperArm"].rotation_euler = (math.radians(-72), math.radians(0), math.radians(-14))
P["LeftLowerArm"].rotation_euler = (math.radians(0), math.radians(0), math.radians(18))
P["RightLowerArm"].rotation_euler = (math.radians(0), math.radians(0), math.radians(-24))
P["RightHand"].rotation_euler = (math.radians(0), math.radians(-20), math.radians(0))
P["LeftFoot"].rotation_euler = (0, 0, 0)
P["RightFoot"].rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()

# ---- bat: knob in the right fist, barrel leaning forward-out to the floor
club.parent = arm
club.parent_type = 'BONE'
club.parent_bone = "RightHand"
bpy.context.view_layer.update()
hm = arm.matrix_world @ arm.pose.bones["RightHand"].matrix
grip = hm @ Vector((0, 0.066, 0))
tip = Vector((-0.52, -0.42, 0.06))
d = (tip - grip).normalized()
rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
club.matrix_world = Matrix.Translation(grip) @ rot

# ---- floor
if "Floor" not in scene.objects:
    me = bpy.data.meshes.new("Floor")
    me.from_pydata([(-6, -6, 0), (6, -6, 0), (6, 6, 0), (-6, 6, 0)], [], [(0, 1, 2, 3)])
    fl = bpy.data.objects.new("Floor", me)
    scene.collection.objects.link(fl)
    m = bpy.data.materials.new("M_PreviewFloor")
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (0.11, 0.085, 0.065, 1)
    b.inputs["Roughness"].default_value = 0.85
    me.materials.append(m)

# ---- world: dark warm-blue night
w = scene.world or bpy.data.worlds.new("PreviewWorld")
scene.world = w
w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs["Color"].default_value = (0.035, 0.03, 0.03, 1)
bg.inputs["Strength"].default_value = 1.0


def light(name, kind, loc, energy, color, size=1.0, target=(0, 0, 1.4)):
    ob = scene.objects.get(name)
    if ob is None:
        ld = bpy.data.lights.new(name, kind)
        ob = bpy.data.objects.new(name, ld)
        scene.collection.objects.link(ob)
    ob.location = loc
    d = Vector(target) - Vector(loc)
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    ob.data.energy = energy
    ob.data.color = color
    if kind == 'AREA':
        ob.data.size = size
    else:
        ob.data.shadow_soft_size = size
    return ob


light("Key", 'AREA', (-1.6, -2.4, 2.6), 160, (1.0, 0.78, 0.52), 1.2)
light("Fill", 'AREA', (2.2, -2.0, 1.4), 35, (0.85, 0.75, 0.7), 2.0)
light("Rim", 'AREA', (1.2, 2.2, 2.4), 120, (1.0, 0.7, 0.45), 1.0)


def camera(name, loc, target, lens):
    ob = scene.objects.get(name)
    if ob is None:
        ob = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    ob.data.lens = lens
    return ob


cam_body = camera("CamBody", (0.9, -3.3, 2.2), (-0.1, 0, 1.0), 40)
cam_face = camera("CamFace", (0.18, -1.35, 1.78), (0.0, 0, 1.56), 55)
cam_feet = camera("CamFeet", (0.5, -1.1, 0.45), (0, -0.1, 0.06), 45)
cam_hand = camera("CamHand", (-0.9, -1.0, 1.0), (-0.42, -0.05, 0.62), 50)
scene.camera = cam_body
try:
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
except TypeError:
    try:
        scene.render.engine = 'BLENDER_EEVEE'
    except TypeError:
        pass
scene.render.resolution_x = 720
scene.render.resolution_y = 900
scene.view_settings.view_transform = 'Standard'
print("preview rig ready", scene.render.engine)
