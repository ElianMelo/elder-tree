"""
Look-dev rig for the Elder Tree (run after build_elder_tree.py in the same scene).
Relaxed A-pose, club in the right fist, PSX dark-fantasy night: indigo world, cold moon
key from behind-left, dim ember fill, cameras ET_CamBody / ET_CamFace / ET_CamBack.
"""
import bpy
import math
import os
from mathutils import Vector, Matrix

scene = bpy.data.scenes.get("ElderTree") or bpy.context.scene
arm = next(o for o in scene.objects if o.type == 'ARMATURE')
PROJECT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(globals().get("__file__", "."))), "..", ".."))

# ---- pose
for pb in arm.pose.bones:
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (0, 0, 0)
P = arm.pose.bones
P["LeftUpperArm"].rotation_euler = (math.radians(-62), 0, math.radians(10))
P["RightUpperArm"].rotation_euler = (math.radians(-64), 0, math.radians(-14))
P["LeftLowerArm"].rotation_euler = (0, 0, math.radians(22))
P["RightLowerArm"].rotation_euler = (0, 0, math.radians(-26))
P["Neck"].rotation_euler = (math.radians(8), 0, 0)
bpy.context.view_layer.update()

# ---- club in the right fist (if the legacy log scene's club exists, link a copy)
club = scene.objects.get("Preview_Club")
src = bpy.data.objects.get("LegacyLog_Club")
if club is None and src is not None:
    club = src.copy()
    club.name = "Preview_Club"
    scene.collection.objects.link(club)
if club is not None:
    club.parent = arm
    club.parent_type = 'BONE'
    club.parent_bone = "RightHand"
    bpy.context.view_layer.update()
    hm = arm.matrix_world @ arm.pose.bones["RightHand"].matrix
    grip = hm @ Vector((0, 0.066, 0))
    tip = Vector((-0.62, -0.45, 0.05))
    d = (tip - grip).normalized()
    club.matrix_world = Matrix.Translation(grip) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()

# ---- ground
if "ET_Floor" not in scene.objects:
    me = bpy.data.meshes.new("Floor")
    me.from_pydata([(-8, -8, 0), (8, -8, 0), (8, 8, 0), (-8, 8, 0)], [], [(0, 1, 2, 3)])
    fl = bpy.data.objects.new("ET_Floor", me)
    scene.collection.objects.link(fl)
    m = bpy.data.materials.new("M_PreviewFloorNight")
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (0.03, 0.035, 0.03, 1)
    b.inputs["Roughness"].default_value = 1.0
    me.materials.append(m)

# ---- world: deep indigo night
w = bpy.data.worlds.get("ElderNight") or bpy.data.worlds.new("ElderNight")
scene.world = w
w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs["Color"].default_value = (0.020, 0.018, 0.055, 1)
bg.inputs["Strength"].default_value = 1.0


def light(name, kind, loc, energy, color, size=1.0, target=(0, 0, 1.4)):
    ob = scene.objects.get(name)
    if ob is None:
        ob = bpy.data.objects.new(name, bpy.data.lights.new(name, kind))
        scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    ob.data.energy = energy
    ob.data.color = color
    if kind == 'AREA':
        ob.data.size = size
    return ob


light("ET_Moon", 'SUN', (-3, 3, 5), 2.2, (0.62, 0.70, 1.0))
light("ET_Front", 'AREA', (1.2, -3.0, 2.0), 120, (0.55, 0.6, 0.9), 3.0)
light("ET_Ember", 'AREA', (-1.5, -1.8, 0.6), 50, (1.0, 0.55, 0.22), 1.0, target=(0, 0, 1.2))


def camera(name, loc, target, lens):
    ob = scene.objects.get(name)
    if ob is None:
        ob = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    ob.data.lens = lens
    return ob


cam_body = camera("ET_CamBody", (1.6, -4.4, 1.9), (0, 0, 1.25), 38)
camera("ET_CamFace", (0.35, -1.55, 1.85), (0, -0.2, 1.72), 50)
camera("ET_CamBack", (-2.2, 3.6, 2.4), (0, 0, 1.4), 38)
scene.camera = cam_body
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        scene.render.engine = eng
        break
    except TypeError:
        pass
scene.render.resolution_x = 640
scene.render.resolution_y = 800
scene.view_settings.view_transform = 'Standard'
print("elder preview ready", scene.render.engine)
