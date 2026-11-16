"""
Tung Sahur - environment & dummy props (Blender 4.2+/5.x, headless friendly).

    blender -b --factory-startup --python build_props.py -- <out_dir> [preview.png]

Each prop is exported as its own FBX with the origin at its base. Material slot names match the
Unity materials (M_Planks, M_Burlap, M_Rope, M_DummyPaint, M_Stone, M_Iron, M_LanternGlow,
M_Foliage, M_Bark) so the importer can remap them.
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.join(os.path.dirname(bpy.data.filepath or "."), "props")
PREVIEW = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)
random.seed(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

MATS = {}


def mat(name, rgb):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    MATS[name] = m
    return m


mat("M_Planks", (0.42, 0.29, 0.18)); mat("M_Burlap", (0.71, 0.6, 0.42)); mat("M_Rope", (0.48, 0.39, 0.25))
mat("M_DummyPaint", (0.48, 0.12, 0.12)); mat("M_Stone", (0.36, 0.37, 0.4)); mat("M_Iron", (0.17, 0.17, 0.18))
mat("M_LanternGlow", (1.0, 0.7, 0.28)); mat("M_Foliage", (0.12, 0.17, 0.12)); mat("M_Bark", (0.25, 0.19, 0.14))


def new_obj(name, me):
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return o


def activate(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def box(name, size, loc=(0, 0, 0), rot=(0, 0, 0), m="M_Planks", bevel=0.012, jitter=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if jitter:
        for v in o.data.vertices:
            v.co += Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))) * jitter
    if bevel:
        bm_ = o.modifiers.new("b", 'BEVEL'); bm_.width = bevel; bm_.segments = 2; bm_.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier="b")
    o.data.materials.append(MATS[m])
    return o


def cyl(name, r, depth, loc=(0, 0, 0), rot=(0, 0, 0), m="M_Planks", verts=16, bevel=0.01, r2=None):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2 if r2 is not None else r, depth=depth,
                                    location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    if bevel:
        b = o.modifiers.new("b", 'BEVEL'); b.width = bevel; b.segments = 2; b.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier="b")
    o.data.materials.append(MATS[m])
    return o


def sphere(name, r, loc, scale=(1, 1, 1), m="M_Burlap", seg=24, rings=14, lumpy=0.0, seed=0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if lumpy:
        for v in o.data.vertices:
            n = noise.noise(v.co * 4.0 + Vector((seed, seed * 2, 0)))
            v.co += v.normal * n * lumpy
    o.data.materials.append(MATS[m])
    return o


def join(objs, name):
    activate(objs[0])
    for o in objs:
        o.select_set(True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    return o


def finish(o, smooth_angle=40, uv_scale=1.0):
    activate(o)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.cube_project(cube_size=1.0 / uv_scale, scale_to_bounds=False, correct_aspect=True)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle))
    except Exception:
        bpy.ops.object.shade_smooth()
    return o


def set_origin_base(o):
    activate(o)
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')


def export(objs, fname):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, fname), use_selection=True, object_types={'MESH', 'EMPTY'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                             use_tspace=True, add_leaf_bones=False, bake_anim=False)
    print("EXPORTED", fname)


def hide(objs):
    for o in objs:
        o.hide_render = True
        o.hide_set(True)


all_preview = []

# ---------------------------------------------------------------- 1. Training dummy
def build_dummy():
    # base: crossed feet + short stub (static)
    parts = [box("f1", (1.0, 0.13, 0.1), (0, 0, 0.05), jitter=0.006),
             box("f2", (0.13, 1.0, 0.1), (0, 0, 0.05), jitter=0.006),
             cyl("stub", 0.075, 0.25, (0, 0, 0.2), verts=12)]
    for a in range(4):
        ang = a * math.pi / 2 + math.pi / 4
        parts.append(box(f"brace{a}", (0.06, 0.06, 0.34), (math.cos(ang) * 0.13, math.sin(ang) * 0.13, 0.17),
                         rot=(math.sin(ang) * 0.75, -math.cos(ang) * 0.75, 0), jitter=0.004))
    base = finish(join(parts, "Dummy_Base"))
    set_origin_base(base)

    # body (sprung part, origin at the spring point z=0.3)
    body = []
    body.append(cyl("post", 0.065, 1.45, (0, 0, 1.0), verts=12, r2=0.055))
    torso = sphere("torso", 0.27, (0, 0, 1.22), scale=(1.0, 0.82, 1.35), m="M_Burlap", lumpy=0.025, seed=1)
    body.append(torso)
    body.append(sphere("head", 0.17, (0, 0, 1.78), scale=(1, 0.92, 1.1), m="M_Burlap", lumpy=0.018, seed=3))
    # sack top knot
    body.append(sphere("knot", 0.06, (0, 0, 1.97), scale=(1, 1, 1.4), m="M_Burlap", lumpy=0.01, seed=5))
    # rope ties
    for z, r in ((1.6, 0.11), (0.88, 0.19), (1.93, 0.05)):
        bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=0.018, major_segments=24, minor_segments=8, location=(0, 0, z))
        t = bpy.context.active_object; t.data.materials.append(MATS["M_Rope"]); body.append(t)
    # arm bar with stuffed ends
    body.append(cyl("arms", 0.035, 1.25, (0, 0, 1.38), rot=(0, math.pi / 2, 0), verts=10))
    for s in (-1, 1):
        body.append(sphere(f"hand{s}", 0.09, (s * 0.62, 0, 1.38), scale=(1.3, 1, 1), m="M_Burlap", lumpy=0.012, seed=s + 9))
        bpy.ops.mesh.primitive_torus_add(major_radius=0.05, minor_radius=0.014, location=(s * 0.5, 0, 1.38), rotation=(0, math.pi / 2, 0))
        t = bpy.context.active_object; t.data.materials.append(MATS["M_Rope"]); body.append(t)
    # painted target on the chest (front = -Y)
    for i, (r, m) in enumerate(((0.15, "M_DummyPaint"), (0.105, "M_Burlap"), (0.06, "M_DummyPaint"))):
        bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=r, depth=0.01, location=(0, -0.222 - i * 0.004, 1.25),
                                            rotation=(math.pi / 2, 0, 0))
        d = bpy.context.active_object; d.data.materials.append(MATS[m]); body.append(d)
    # painted face: two X eyes + stitched mouth
    for s in (-1, 1):
        for a in (0.7, -0.7):
            body.append(box(f"eye{s}{a}", (0.06, 0.012, 0.014), (s * 0.06, -0.152, 1.81), rot=(0, a, 0), m="M_DummyPaint", bevel=0.003))
    body.append(box("mouth", (0.11, 0.012, 0.012), (0, -0.152, 1.71), m="M_DummyPaint", bevel=0.003))
    b = finish(join(body, "Dummy_Body"), smooth_angle=60)
    bpy.context.scene.cursor.location = (0, 0, 0.3)
    activate(b)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.context.scene.cursor.location = (0, 0, 0)
    export([base, b], "Dummy.fbx")
    return [base, b]


# ---------------------------------------------------------------- 2. Pos Ronda (night-watch hut)
def build_hut():
    p = []
    W, D, FH, RH = 2.8, 2.2, 0.55, 2.55
    # floor deck of planks
    for i in range(9):
        x = -W / 2 + 0.16 + i * (W - 0.32) / 8
        p.append(box(f"deck{i}", (0.3, D, 0.06), (x, 0, FH), jitter=0.006))
    # beams + legs
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(box(f"post{sx}{sy}", (0.14, 0.14, RH + 0.1), (sx * (W / 2 - 0.08), sy * (D / 2 - 0.08), (RH + 0.1) / 2), jitter=0.008))
    for sy in (-1, 1):
        p.append(box(f"beam{sy}", (W + 0.1, 0.16, 0.16), (0, sy * (D / 2 - 0.08), FH - 0.1)))
        p.append(box(f"top{sy}", (W + 0.3, 0.14, 0.14), (0, sy * (D / 2 - 0.08), RH)))
    # back wall (woven bamboo-ish panels approximated with planks) - half height
    for i in range(7):
        z = FH + 0.12 + i * 0.14
        p.append(box(f"wall{i}", (W - 0.2, 0.05, 0.12), (0, D / 2 - 0.08, z), jitter=0.004))
    # bench along back
    p.append(box("bench", (W - 0.5, 0.36, 0.06), (0, D / 2 - 0.35, FH + 0.42)))
    for sx in (-1, 1):
        p.append(box(f"benchleg{sx}", (0.06, 0.3, 0.42), (sx * (W / 2 - 0.45), D / 2 - 0.35, FH + 0.2)))
    # gable roof
    ridge = RH + 0.95
    for sy in (-1, 1):
        ang = math.atan2(0.95, D / 2 + 0.35)
        L = math.hypot(0.95, D / 2 + 0.35)
        for i in range(10):
            x = -W / 2 - 0.25 + i * (W + 0.5) / 9.0
            p.append(box(f"roof{sy}{i}", (0.34, L, 0.05),
                         (x, sy * (D / 2 + 0.35) / 2, RH + 0.475 + 0.02),
                         rot=(sy * ang, 0, 0), jitter=0.01))
    p.append(box("ridge", (W + 0.6, 0.16, 0.12), (0, 0, ridge + 0.03)))
    # gable triangles
    for sx in (-1, 1):
        for i in range(5):
            z = RH + 0.1 + i * 0.17
            w = (D + 0.2) * (1 - i / 5.2)
            p.append(box(f"gable{sx}{i}", (0.04, w, 0.15), (sx * (W / 2 - 0.02), 0, z)))
    hut = finish(join(p, "PosRonda"))

    # kentongan: hollow slit log drum hanging from the front beam
    k = []
    k.append(cyl("kdrum", 0.11, 0.62, (0.9, -D / 2 - 0.02, RH - 0.55), m="M_Bark", verts=20, bevel=0.02))
    k.append(box("kslit", (0.05, 0.02, 0.4), (0.9, -D / 2 - 0.13, RH - 0.55), m="M_DummyPaint", bevel=0.004))
    k.append(cyl("krope", 0.012, 0.3, (0.9, -D / 2 - 0.02, RH - 0.1), m="M_Rope", verts=8, bevel=0))
    kent = finish(join(k, "Kentongan"))
    # sign board "POS RONDA" (text is painted in Unity with a decal-free trick: a separate plate)
    s = [box("sign", (1.3, 0.05, 0.32), (0, -D / 2 - 0.12, RH + 0.32), m="M_Planks"),
         box("signpaint", (1.16, 0.01, 0.2), (0, -D / 2 - 0.15, RH + 0.32), m="M_DummyPaint", bevel=0.002)]
    sign = finish(join(s, "Sign"))
    export([hut, kent, sign], "PosRonda.fbx")
    return [hut, kent, sign]


# ---------------------------------------------------------------- 3. Lamp post with hanging lantern
def build_lamp():
    p = [cyl("pole", 0.075, 3.0, (0, 0, 1.5), verts=10, r2=0.06, m="M_Bark"),
         box("arm", (0.7, 0.07, 0.07), (0.3, 0, 2.85), m="M_Bark"),
         box("brace", (0.05, 0.05, 0.45), (0.14, 0, 2.66), rot=(0, -0.75, 0), m="M_Bark"),
         box("footing", (0.34, 0.34, 0.12), (0, 0, 0.06), m="M_Stone", jitter=0.01)]
    pole = finish(join(p, "LampPost"))
    lan = [cyl("hook", 0.008, 0.18, (0.58, 0, 2.74), m="M_Iron", verts=6, bevel=0),
           box("cap", (0.2, 0.2, 0.04), (0.58, 0, 2.63), m="M_Iron"),
           cyl("roofcone", 0.15, 0.1, (0.58, 0, 2.68), m="M_Iron", verts=4, r2=0.02, rot=(0, 0, math.pi / 4)),
           box("bottom", (0.18, 0.18, 0.03), (0.58, 0, 2.36), m="M_Iron")]
    for sx in (-1, 1):
        for sy in (-1, 1):
            lan.append(box(f"bar{sx}{sy}", (0.015, 0.015, 0.27), (0.58 + sx * 0.08, sy * 0.08, 2.495), m="M_Iron", bevel=0.002))
    frame = finish(join(lan, "LanternFrame"))
    glass = box("LanternGlow", (0.13, 0.13, 0.22), (0.58, 0, 2.495), m="M_LanternGlow", bevel=0.01)
    finish(glass)
    export([pole, frame, glass], "LampPost.fbx")
    return [pole, frame, glass]


# ---------------------------------------------------------------- 4. Small props
def build_crate():
    p = [box("core", (0.7, 0.7, 0.7), (0, 0, 0.35), bevel=0.02, jitter=0.004)]
    for ax in range(3):
        for s in (-1, 1):
            for e in (-1, 1):
                if ax == 0: p.append(box("t", (0.72, 0.08, 0.08), (0, s * 0.33, 0.35 + e * 0.33)))
                if ax == 1: p.append(box("t", (0.08, 0.72, 0.08), (s * 0.33, 0, 0.35 + e * 0.33)))
                if ax == 2: p.append(box("t", (0.08, 0.08, 0.72), (s * 0.33, e * 0.33, 0.35)))
    c = finish(join(p, "Crate"))
    export([c], "Crate.fbx")
    return [c]


def build_barrel():
    me = bpy.data.meshes.new("Barrel")
    bm = bmesh.new()
    segs = 24
    prof = []
    for k in range(13):
        z = 0.02 + 0.86 * k / 12
        t = (z - 0.45) / 0.45
        prof.append((z, 0.27 + 0.05 * (1 - t * t)))
    rings = []
    for z, r in prof:
        rings.append([bm.verts.new((math.cos(2 * math.pi * j / segs) * r, math.sin(2 * math.pi * j / segs) * r, z)) for j in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for j in range(segs):
            bm.faces.new([a[j], a[(j + 1) % segs], b[(j + 1) % segs], b[j]])
    bm.faces.new(list(reversed(rings[0])))
    top = bm.faces.new(rings[-1])
    bmesh.ops.inset_region(bm, faces=[top], thickness=0.025, depth=-0.03)
    bm.to_mesh(me)
    bm.free()
    o = new_obj("Barrel", me)
    o.data.materials.append(MATS["M_Planks"])
    parts = [o]
    for z in (0.14, 0.76):
        t_ = (z - 0.45) / 0.45
        bpy.ops.mesh.primitive_torus_add(major_radius=0.27 + 0.05 * (1 - t_ * t_) + 0.004, minor_radius=0.014, location=(0, 0, z))
        t = bpy.context.active_object; t.data.materials.append(MATS["M_Iron"]); parts.append(t)
    b = finish(join(parts, "Barrel"), smooth_angle=50)
    export([b], "Barrel.fbx")
    return [b]


def build_fence():
    p = []
    for x in (-1.2, 1.2):
        p.append(box("fp", (0.12, 0.12, 1.1), (x, 0, 0.55), jitter=0.01))
    for z in (0.4, 0.85):
        p.append(box("rail", (2.6, 0.06, 0.12), (0, 0.06, z), rot=(0, random.uniform(-0.03, 0.03), 0), jitter=0.008))
    f = finish(join(p, "Fence"))
    export([f], "Fence.fbx")
    return [f]


def build_wall():
    p = []
    z = 0.0
    for row in range(4):
        h = random.uniform(0.22, 0.3)
        x = -1.6 + (0.25 if row % 2 else 0)
        while x < 1.55:
            w = random.uniform(0.35, 0.6)
            w = min(w, 1.6 - x)
            if w < 0.12: break
            p.append(box("blk", (w - 0.03, 0.5 - row * 0.04, h - 0.02), (x + w / 2, 0, z + h / 2), m="M_Stone", bevel=0.03, jitter=0.02))
            x += w
        z += h
    w_ = finish(join(p, "StoneWall"))
    export([w_], "StoneWall.fbx")
    return [w_]


def build_rock(i, scale):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=0.5, location=(0, 0, 0.3))
    o = bpy.context.active_object
    o.name = f"Rock{i}"
    for v in o.data.vertices:
        n = noise.noise(v.co * 2.2 + Vector((i * 3.1, i, 0))) * 0.22 + noise.noise(v.co * 6 + Vector((i, 0, 0))) * 0.05
        v.co += v.co.normalized() * n
        v.co.z *= 0.65
    o.scale = scale
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(MATS["M_Stone"])
    finish(o, smooth_angle=35)
    export([o], f"Rock{i}.fbx")
    return [o]


def build_tree(i):
    p = [cyl("trunk", 0.16, 3.2, (0, 0, 1.6), m="M_Bark", verts=10, r2=0.09)]
    for k in range(5):
        ang = k * 2.4 + i
        h = 2.6 + k * 0.45
        r = 1.15 - k * 0.12
        p.append(sphere(f"leaf{k}", r, (math.cos(ang) * 0.4, math.sin(ang) * 0.4, h), scale=(1, 1, 0.75), m="M_Foliage",
                        seg=12, rings=8, lumpy=0.18, seed=k + i * 5))
    t = finish(join(p, f"Tree{i}"), smooth_angle=60)
    export([t], f"Tree{i}.fbx")
    return [t]


groups = [build_dummy(), build_hut(), build_lamp(), build_crate(), build_barrel(), build_fence(), build_wall(),
          build_rock(1, (1.4, 1.1, 1.0)), build_rock(2, (0.8, 0.9, 0.9)), build_tree(1), build_tree(2)]
print("PROPS DONE")

if PREVIEW:
    offsets = [(0, 0), (4, 2), (-3, 1), (2, -2), (3, -1), (-2, -3), (-5, -2), (6, -3), (7, 0), (-7, 4), (8, 5)]
    for g, (x, y) in zip(groups, offsets):
        for o in g:
            o.location.x += x
            o.location.y += y
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    cam.location = (2, -14, 6)
    cam.rotation_euler = (Vector((1, 0, 1)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 1400, 800
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", PREVIEW)
