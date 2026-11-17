"""
PSX dark-fantasy club for the Elder Tree (Docs/ART_DIRECTION.md).

    blender -b --factory-startup --python Tools/Blender/build_psx_weapons.py -- <out_dir>

ElderClub.fbx: a gnarled cudgel cut from a dead branch. Leather-wrapped grip, two rusted iron
bands, iron spikes driven through the head, knot stubs. ~250 tris. Same conventions as the old
bat so every socket keeps working: origin = grip point, the club runs along +Z (Unity +Y).
UVs: u around, v along (v < 0.3 = grip band of T_Club_Albedo). Materials: M_ClubWood, M_ClubIron.
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "export"))
os.makedirs(OUT, exist_ok=True)
if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
else:
    scene = bpy.data.scenes.get("PSXWeapons") or bpy.data.scenes.new("PSXWeapons")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)

Z0, Z1 = -0.12, 0.96          # pommel bottom .. head top
MATS = []


def mat(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    MATS.append(m)
    return len(MATS) - 1


WOOD = mat("M_ClubWood", (0.2, 0.15, 0.11))
IRON = mat("M_ClubIron", (0.12, 0.1, 0.1))

bm = bmesh.new()
uvl = bm.loops.layers.uv.new("UVMap")


def vcoord(z):
    return (z - Z0) / (Z1 - Z0)


def lathe(profile, sides, m, jag=0.0, seed=0, cap_bottom=True, cap_top=True, twist=0.0, uv_v=True):
    """profile: [(z, r)] bottom->top around the Z axis."""
    rings = []
    for i, (z, r) in enumerate(profile):
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides + twist * i
            k = 1 + jag * noise.noise(Vector((i * 1.3 + seed, j * 2.1, seed * 0.5)))
            ring.append(bm.verts.new((math.cos(a) * r * k, math.sin(a) * r * k, z)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for j in range(sides):
            f = bm.faces.new([rings[i][j], rings[i][(j + 1) % sides], rings[i + 1][(j + 1) % sides], rings[i + 1][j]])
            f.material_index = m
            v0, v1 = (vcoord(profile[i][0]), vcoord(profile[i + 1][0])) if uv_v else (0.5, 0.55)
            for loop, uv in zip(f.loops, [(j / sides, v0), ((j + 1) / sides, v0), ((j + 1) / sides, v1), (j / sides, v1)]):
                loop[uvl].uv = uv
    for ring, z, top in ((rings[0], profile[0][0], False), (rings[-1], profile[-1][0], True)):
        if (top and not cap_top) or (not top and not cap_bottom):
            continue
        c = bm.verts.new((0, 0, z + (0.02 if top else -0.012)))
        for j in range(len(ring)):
            a, b = ring[j], ring[(j + 1) % len(ring)]
            f = bm.faces.new([a, b, c] if top else [b, a, c])
            f.material_index = m
            for loop in f.loops:
                loop[uvl].uv = (j / len(ring), vcoord(z) if uv_v else 0.5)
    return rings


# pommel knob + grip + gnarled head (one wooden lathe)
wood_profile = [(Z0, 0.030), (-0.10, 0.040), (-0.075, 0.034), (-0.06, 0.027), (0.08, 0.029), (0.20, 0.031),
                (0.26, 0.036), (0.40, 0.045), (0.55, 0.058), (0.70, 0.068), (0.83, 0.074), (0.92, 0.062), (Z1, 0.035)]
lathe(wood_profile, 7, WOOD, jag=0.12, seed=3, twist=0.12)

# rusted iron bands
for zb, r in ((0.50, 0.058), (0.77, 0.077)):
    lathe([(zb - 0.022, r), (zb - 0.012, r + 0.008), (zb + 0.012, r + 0.008), (zb + 0.022, r)], 8, IRON,
          cap_bottom=False, cap_top=False, uv_v=False)

# iron spikes driven through the head (3-sided pyramids)
for n in range(7):
    a = n * 2 * math.pi / 7 + (0.4 if n % 2 else 0)
    z = 0.62 + 0.11 * (n % 3)
    out = Vector((math.cos(a), math.sin(a), 0.15)).normalized()
    side = Vector((-math.sin(a), math.cos(a), 0))
    base_c = Vector((0, 0, z)) + Vector((math.cos(a), math.sin(a), 0)) * 0.055
    tip = bm.verts.new(base_c + out * 0.075)
    b = [bm.verts.new(base_c + side * 0.014 * math.cos(k * 2.094) + Vector((0, 0, 0.014 * math.sin(k * 2.094))))
         for k in range(3)]
    for k in range(3):
        f = bm.faces.new([b[k], b[(k + 1) % 3], tip])
        f.material_index = IRON
        for loop in f.loops:
            loop[uvl].uv = (0.3 + 0.1 * k, 0.5)

# knot stubs (broken twigs)
for a, z in ((0.9, 0.42), (3.6, 0.66)):
    d = Vector((math.cos(a), math.sin(a), 0.6)).normalized()
    c0 = Vector((0, 0, z)) + Vector((math.cos(a), math.sin(a), 0)) * 0.035
    rings = []
    for i, (t, r) in enumerate(((0.0, 0.018), (0.05, 0.013), (0.085, 0.008))):
        c = c0 + d * t
        n1 = d.cross(Vector((0, 0, 1))).normalized()
        n2 = d.cross(n1)
        rings.append([bm.verts.new(c + (n1 * math.cos(k * 2.094) + n2 * math.sin(k * 2.094)) * r) for k in range(3)])
    for i in range(2):
        for k in range(3):
            f = bm.faces.new([rings[i][k], rings[i][(k + 1) % 3], rings[i + 1][(k + 1) % 3], rings[i + 1][k]])
            f.material_index = WOOD
            for loop, uv in zip(f.loops, [(k / 3, 0.55), ((k + 1) / 3, 0.55), ((k + 1) / 3, 0.6), (k / 3, 0.6)]):
                loop[uvl].uv = uv
    f = bm.faces.new(rings[2])
    f.material_index = WOOD

bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new("ElderClub")
bm.to_mesh(me)
bm.free()
for m in MATS:
    me.materials.append(m)
for p in me.polygons:
    p.use_smooth = True
try:
    me.set_sharp_from_angle(angle=math.radians(35))
except Exception:
    pass
club = bpy.data.objects.new("ElderClub", me)
scene.collection.objects.link(club)

for o in scene.objects:
    o.select_set(False)
club.select_set(True)
bpy.context.view_layer.objects.active = club
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "ElderClub.fbx"), use_selection=True, object_types={'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False,
    add_leaf_bones=False, bake_anim=False, colors_type='NONE')
print("EXPORTED ElderClub.fbx tris", sum(len(p.vertices) - 2 for p in me.polygons))
