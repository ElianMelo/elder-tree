"""
Elder Tree - Crocodile arena kit: a night swamp with a WWII-era military garrison (Blender 4.2+/5.x, headless).

    blender -b --factory-startup --python build_swamp.py -- <abs out_dir> [preview.png]

PSX rules (Docs/ART_DIRECTION.md): no bevels, 3-9 sided tubes, faceted shading, props 50-800 tris; the big
bald cypresses are landscape pieces (~1-2k tris, most of it in alpha cards). All surface detail comes from the
point-filtered, dithered Unity textures (TextureForge.BuildSwamp*).

Each prop is its own FBX with the origin at its base. Material slot names match the Unity materials:
  M_SwampBark   bald-cypress bark (grey-brown fibrous strips, moss at the foot)
  M_SwampFlora  cutout atlas, 2x2: moss strands (TL), cypress foliage (TR), lily pad (BL), reeds/fern (BR)
  M_LilyFlower  white water lily petals
  M_Sandbag     olive-tan burlap bags
  M_Canvas      olive-drab tent canvas
  M_OliveMetal  painted olive-green steel, chipped to rust
  M_Concrete    stained bunker concrete
  M_Mud         wet earth (mounds, bunker berms)
  M_Iron, M_Planks, M_Rope, M_DarkWood (existing)
  M_LampLens    cold white emissive lens (searchlight / floodlights)
  M_LanternGlow amber emissive (torch heads, burn barrel embers)
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector, Matrix, Euler, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(os.path.dirname(bpy.data.filepath or "."), "swamp")
PREVIEW = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
MATS = {}


def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    MATS[name] = m


for n, c in [("M_SwampBark", (0.30, 0.26, 0.21)), ("M_SwampFlora", (0.22, 0.30, 0.18)), ("M_LilyFlower", (0.9, 0.9, 0.85)),
             ("M_Sandbag", (0.45, 0.40, 0.28)), ("M_Canvas", (0.28, 0.30, 0.20)), ("M_OliveMetal", (0.25, 0.28, 0.18)),
             ("M_Concrete", (0.42, 0.42, 0.40)), ("M_Mud", (0.20, 0.17, 0.12)), ("M_Iron", (0.17, 0.17, 0.18)),
             ("M_Planks", (0.42, 0.29, 0.18)), ("M_Rope", (0.48, 0.39, 0.25)), ("M_DarkWood", (0.2, 0.15, 0.11)),
             ("M_LampLens", (1.0, 1.0, 0.92)), ("M_LanternGlow", (1.0, 0.7, 0.28))]:
    mat(n, c)

# atlas quadrants (u0, v0, u1, v1) of T_SwampFlora
Q_MOSS = (0.0, 0.5, 0.5, 1.0)
Q_LEAF = (0.5, 0.5, 1.0, 1.0)
Q_PAD = (0.0, 0.0, 0.5, 0.5)
Q_REED = (0.5, 0.0, 1.0, 0.5)


# ------------------------------------------------------------------ primitives
def new_obj(name, me):
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return o


def activate(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def box(name, size, loc=(0, 0, 0), rot=(0, 0, 0), m="M_Planks", jitter=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if jitter:
        for v in o.data.vertices:
            v.co += Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))) * jitter
    o.data.materials.append(MATS[m])
    return o


def cyl(name, r, depth, loc=(0, 0, 0), rot=(0, 0, 0), m="M_Planks", verts=8, r2=None, cap=True):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2 if r2 is not None else r, depth=depth,
                                    location=loc, rotation=rot, end_fill_type='NGON' if cap else 'NOTHING')
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(MATS[m])
    return o


def sphere(name, r, loc, scale=(1, 1, 1), m="M_Sandbag", seg=8, rings=5, lumpy=0.0, seed=0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if lumpy:
        for v in o.data.vertices:
            v.co += v.normal * noise.noise(v.co * 4.0 + Vector((seed, seed * 2, 0))) * lumpy
    o.data.materials.append(MATS[m])
    return o


def tube(name, pts, radii, sides, m, seed=0, jag=0.15, flute=0.0, flute_n=5, uv_len=1.2, cap=True):
    """Low-poly tube along pts with u-around / v-along UVs. flute (0..1) carves buttress ridges that fade upward."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    pts = [Vector(p) for p in pts]
    rings, prev = [], Vector((1, 0, 0))
    acc = [0.0]
    for i in range(1, len(pts)):
        acc.append(acc[-1] + (pts[i] - pts[i - 1]).length)
    for i, c in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        n = (prev - t * prev.dot(t))
        if n.length < 1e-4:
            n = Vector((0, 1, 0)) - t * t.y
        n.normalize()
        prev = n
        b = t.cross(n)
        ring = []
        fl = flute * max(0.0, 1.0 - i / max(1, len(pts) - 1) * 2.2)
        for j in range(sides):
            a = 2 * math.pi * j / sides
            k = 1 + jag * noise.noise(Vector((i * 1.7 + seed, j * 2.3, seed * 0.37)))
            k *= 1 + fl * (0.5 + 0.5 * math.cos(a * flute_n + seed))
            ring.append(bm.verts.new(c + (n * math.cos(a) + b * math.sin(a)) * radii[i] * k))
        rings.append(ring)
    reps = max(1, round(2 * math.pi * max(radii) / 0.7))
    for i in range(len(rings) - 1):
        for j in range(sides):
            f = bm.faces.new([rings[i][j], rings[i][(j + 1) % sides], rings[i + 1][(j + 1) % sides], rings[i + 1][j]])
            u0, u1 = j / sides * reps, (j + 1) / sides * reps
            v0, v1 = acc[i] / uv_len, acc[i + 1] / uv_len
            for loop, (uu, vv) in zip(f.loops, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]):
                loop[uv].uv = (uu, vv)
    if cap:
        bm.faces.new(list(reversed(rings[0])))
    bm.to_mesh(me)
    bm.free()
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    return o


def card(name, corners, q, m="M_SwampFlora"):
    """Quad from 4 corners (bl, br, tr, tl) mapped onto atlas quadrant q (with a 2% inset against bleeding)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    vs = [bm.verts.new(Vector(c)) for c in corners]
    f = bm.faces.new(vs)
    u0, v0, u1, v1 = q
    du, dv = (u1 - u0) * 0.02, (v1 - v0) * 0.02
    for loop, (uu, vv) in zip(f.loops, [(u0 + du, v0 + dv), (u1 - du, v0 + dv), (u1 - du, v1 - dv), (u0 + du, v1 - dv)]):
        loop[uv].uv = (uu, vv)
    bm.to_mesh(me)
    bm.free()
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    return o


def hang_card(name, top, width, length, yaw, sway=0.15, q=Q_MOSS):
    """A vertical card hanging from `top` (moss curtain); bottom swings out a little."""
    d = Vector((math.cos(yaw), math.sin(yaw), 0)) * width * 0.5
    out = Vector((-math.sin(yaw), math.cos(yaw), 0)) * sway
    t = Vector(top)
    return card(name, [t - d + out - Vector((0, 0, length)), t + d + out - Vector((0, 0, length)), t + d, t - d], q)


def clump(name, center, size, q=Q_LEAF, n=3, tilt=0.35, seed=0):
    """n crossed cards around a vertical axis, tilted outward: a PSX leaf clump."""
    rnd = random.Random(seed)
    c = Vector(center)
    parts = []
    for k in range(n):
        a = k * math.pi / n + rnd.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0)) * size * 0.5
        up = Vector((rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt), 1)).normalized() * size * 0.42
        parts.append(card(f"{name}{k}", [c - d - up, c + d - up, c + d + up, c - d + up], q))
    # a flat-ish cap card so the clump reads from below
    a = rnd.uniform(0, math.pi)
    d1 = Vector((math.cos(a), math.sin(a), 0.08)) * size * 0.55
    d2 = Vector((-math.sin(a), math.cos(a), -0.08)) * size * 0.55
    parts.append(card(f"{name}cap", [c - d1 - d2, c + d1 - d2, c + d1 + d2, c - d1 + d2], q))
    return parts


def join(objs, name):
    activate(objs[0])
    for o in objs:
        o.select_set(True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    return o


def finish(o, smooth_angle=30, uv_scale=1.0, project=True):
    set_origin(o, (0, 0, 0))        # geometry stays put; the object sits at the world origin
    activate(o)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    if project:
        bpy.ops.uv.cube_project(cube_size=1.0 / uv_scale, scale_to_bounds=False, correct_aspect=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle))
    except Exception:
        bpy.ops.object.shade_smooth()
    return o


def project_mat(o, names, uv_scale=1.0):
    """Cube-project only the faces whose material is in `names` (keeps hand-made card / tube UVs)."""
    activate(o)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    idx = {i for i, s in enumerate(o.material_slots) if s.material and s.material.name in names}
    for p in o.data.polygons:
        p.select = p.material_index in idx
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.uv.cube_project(cube_size=1.0 / uv_scale, scale_to_bounds=False, correct_aspect=True)
    bpy.ops.object.mode_set(mode='OBJECT')


def merge_slots(o):
    """join() leaves duplicate material slots per source object: collapse them so the FBX has one slot per material."""
    seen = {}
    remap = []
    for s in o.material_slots:
        n = s.material.name if s.material else ""
        if n not in seen:
            seen[n] = len(seen)
        remap.append(seen[n])
    target = [remap[p.material_index] for p in o.data.polygons]
    mats = list(seen.keys())
    o.data.materials.clear()          # (this resets every polygon's index, so re-apply after)
    for n in mats:
        o.data.materials.append(MATS[n] if n in MATS else None)
    for p, t in zip(o.data.polygons, target):
        p.material_index = t


def empty(name, loc, parent):
    """Named locator exported with the prop (light / flame anchors for the Unity builder)."""
    e = bpy.data.objects.new(name, None)
    scene.collection.objects.link(e)
    e.location = loc
    e.parent = parent
    e.matrix_parent_inverse = parent.matrix_world.inverted()
    return e


def export(objs, fname):
    for o in objs:
        for c in [o] + list(o.children_recursive):
            if c.type == 'MESH':
                merge_slots(c)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
        for c in o.children_recursive:
            c.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, fname), use_selection=True, object_types={'MESH', 'EMPTY'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                             use_tspace=False, add_leaf_bones=False, bake_anim=False)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == 'MESH')
    print(f"EXPORTED {fname} tris={tris}")


def set_origin(o, p):
    activate(o)
    scene.cursor.location = p
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    scene.cursor.location = (0, 0, 0)


groups = []

# ================================================================== flora


def build_cypress(name, height, lean, seed, leafy=1.0, mossy=1.0):
    """Bald cypress: fluted buttress foot, tall straight trunk, a crown of drooping limbs with feathery clumps,
    Spanish moss curtains hanging off every limb (reference photos 1-3)."""
    rnd = random.Random(seed)
    random.seed(seed)
    lx, ly = math.cos(lean[0]) * lean[1], math.sin(lean[0]) * lean[1]
    zs = [-0.4, 0.4, 1.3, 2.6, height * 0.35, height * 0.6, height * 0.82, height]
    pts = [(lx * max(0.0, z / height) ** 1.6 * height * 0.1, ly * max(0.0, z / height) ** 1.6 * height * 0.1, z) for z in zs]
    base_r = 0.55 + height * 0.02
    radii = [base_r * 2.6, base_r * 2.0, base_r * 1.35, base_r * 1.05, base_r * 0.85, base_r * 0.65, base_r * 0.45, base_r * 0.15]
    parts = [tube(f"{name}_trunk", pts, radii, 9, "M_SwampBark", seed=seed, jag=0.12, flute=0.55, flute_n=5, uv_len=2.0)]
    trunk_at = lambda z: Vector((lx * max(0.0, z / height) ** 1.6 * height * 0.1, ly * max(0.0, z / height) ** 1.6 * height * 0.1, z))
    # root flares running into the mud
    for k in range(5):
        a = k * 2 * math.pi / 5 + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        parts.append(tube(f"{name}_root{k}", [d * base_r * 1.6 + Vector((0, 0, 0.9)), d * base_r * 2.9 + Vector((0, 0, 0.15)),
                                             d * base_r * 4.0 + Vector((0, 0, -0.25))],
                          [base_r * 0.55, base_r * 0.35, base_r * 0.12], 4, "M_SwampBark", seed=seed + k, uv_len=2.0))
    # limbs
    n_limbs = 6 + rnd.randint(0, 2)
    for k in range(n_limbs):
        z = height * rnd.uniform(0.52, 0.9)
        a = k * 2 * math.pi / n_limbs + rnd.uniform(-0.35, 0.35)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = height * rnd.uniform(0.22, 0.34) * (1.15 - (z / height - 0.5))
        base = trunk_at(z)
        mid = base + d * L * 0.5 + Vector((0, 0, L * rnd.uniform(0.1, 0.3)))
        tip = base + d * L + Vector((0, 0, L * rnd.uniform(-0.15, 0.12)))
        parts.append(tube(f"{name}_limb{k}", [base, mid, tip], [base_r * 0.32, base_r * 0.2, base_r * 0.06], 4, "M_SwampBark",
                          seed=seed + 10 + k, uv_len=2.0))
        # feathery clumps along the outer half of the limb
        for c in range(int(round(rnd.uniform(1, 2.6) * leafy))):
            t = rnd.uniform(0.45, 1.05)
            p = mid.lerp(tip, (t - 0.5) * 2) if t > 0.5 else base.lerp(mid, t * 2)
            p += Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), rnd.uniform(0.2, 0.9)))
            parts += clump(f"{name}_lf{k}_{c}", p, rnd.uniform(1.8, 2.8) * (0.8 + 0.2 * leafy), seed=seed * 31 + k * 7 + c)
        # moss curtains
        for c in range(int(round(rnd.uniform(4, 7) * mossy))):
            t = rnd.uniform(0.25, 1.0)
            p = base.lerp(tip, t) + Vector((rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), -0.05))
            parts.append(hang_card(f"{name}_ms{k}_{c}", p, rnd.uniform(0.6, 1.1), rnd.uniform(2.6, 5.2) * (0.8 + 0.2 * mossy),
                                   a + math.pi / 2 + rnd.uniform(-0.6, 0.6), sway=rnd.uniform(0.05, 0.3)))
    # top clump crown
    parts += clump(f"{name}_crown", trunk_at(height) + Vector((0, 0, 0.6)), 2.4 * leafy + 0.8, seed=seed + 99, n=3)
    for c in range(int(3 * mossy)):
        p = trunk_at(height * rnd.uniform(0.55, 0.85))
        a = rnd.uniform(0, 2 * math.pi)
        p += Vector((math.cos(a), math.sin(a), 0)) * base_r * 0.8
        parts.append(hang_card(f"{name}_tm{c}", p, 0.8, rnd.uniform(1.8, 3.0), a + math.pi / 2, sway=0.1))
    t = join(parts, name)
    finish(t, smooth_angle=28, project=False)
    export([t], f"{name}.fbx")
    return [t]


def build_knees():
    """Cypress knees: knobby cones poking out of the water."""
    rnd = random.Random(5)
    parts = []
    for k in range(7):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0, 1.3)
        h = rnd.uniform(0.25, 0.8)
        c = cyl(f"knee{k}", rnd.uniform(0.09, 0.17), h, (math.cos(a) * r, math.sin(a) * r, h / 2 - 0.12), m="M_SwampBark",
                verts=5, r2=rnd.uniform(0.02, 0.05))
        parts.append(c)
    o = finish(join(parts, "CypressKnees"), uv_scale=2.0)
    export([o], "CypressKnees.fbx")
    return [o]


def build_fallen_log():
    rnd = random.Random(8)
    pts = [(-3.6, 0, 0.25), (-1.2, 0.15, 0.4), (1.0, -0.1, 0.42), (3.4, 0.2, 0.3)]
    parts = [tube("log", pts, [0.48, 0.44, 0.4, 0.3], 7, "M_SwampBark", seed=3, jag=0.18, uv_len=2.0)]
    # splintered broken end
    for k in range(5):
        a = k * 2 * math.pi / 5
        parts.append(cyl(f"spl{k}", 0.08, 0.5, (3.55, 0.2 + math.cos(a) * 0.18, 0.3 + math.sin(a) * 0.18), rot=(0, math.pi / 2, 0),
                         m="M_SwampBark", verts=3, r2=0.0))
    # stub branches
    for k, (x, a) in enumerate([(-2.0, 0.6), (0.3, 2.3), (1.9, -0.9)]):
        b = Vector((x, 0, 0.5))
        parts.append(tube(f"stub{k}", [b, b + Vector((0.3, math.cos(a) * 0.7, 0.6 + math.sin(a) * 0.2))], [0.1, 0.03], 4,
                          "M_SwampBark", seed=20 + k))
    for k in range(5):
        x = rnd.uniform(-3, 3)
        parts.append(hang_card(f"lm{k}", (x, rnd.choice([-0.42, 0.42]), 0.55), 0.7, rnd.uniform(0.4, 0.7), 0.0, sway=0.1))
    o = finish(join(parts, "FallenLog"), project=False)
    export([o], "FallenLog.fbx")
    return [o]


def build_stump():
    pts = [(0, 0, -0.3), (0, 0, 0.5), (0.05, 0.02, 1.3), (0.02, 0.05, 1.9)]
    parts = [tube("stump", pts, [0.75, 0.5, 0.42, 0.38], 7, "M_SwampBark", seed=12, jag=0.2, flute=0.6, uv_len=2.0)]
    # jagged snapped top
    for k in range(6):
        a = k * 2 * math.pi / 6
        h = 0.3 + 0.35 * ((k * 37) % 5) / 4
        parts.append(cyl(f"sp{k}", 0.13, h, (math.cos(a) * 0.3, math.sin(a) * 0.3, 1.9 + h / 2), m="M_SwampBark", verts=3, r2=0.0))
    parts.append(hang_card("sm0", (0.35, 0.2, 1.7), 0.6, 1.2, 1.2, sway=0.12))
    o = finish(join(parts, "SwampStump"), project=False)
    export([o], "SwampStump.fbx")
    return [o]


def build_lily_pads():
    rnd = random.Random(17)
    parts = []
    for k in range(11):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0, 1.6)
        s = rnd.uniform(0.35, 0.7)
        c = Vector((math.cos(a) * r, math.sin(a) * r, 0.02 + k * 0.002))
        rot = rnd.uniform(0, 2 * math.pi)
        d1 = Vector((math.cos(rot), math.sin(rot), 0)) * s
        d2 = Vector((-math.sin(rot), math.cos(rot), 0)) * s
        parts.append(card(f"pad{k}", [c - d1 - d2, c + d1 - d2, c + d1 + d2, c - d1 + d2], Q_PAD))
    # two water lilies: a ring of pointed petals around a gold heart
    for f, (x, y) in enumerate([(0.4, 0.2), (-0.9, -0.5)]):
        for ring, (n, rr, up) in enumerate([(7, 0.16, 0.06), (6, 0.11, 0.1)]):
            for k in range(n):
                a = k * 2 * math.pi / n + ring * 0.4
                base = Vector((x, y, 0.05))
                tip = base + Vector((math.cos(a) * rr, math.sin(a) * rr, up))
                side = Vector((-math.sin(a), math.cos(a), 0)) * 0.035
                me = bpy.data.meshes.new("petal")
                bm = bmesh.new()
                vs = [bm.verts.new(base + side * 0.5), bm.verts.new(base + (tip - base) * 0.5 + side), bm.verts.new(tip),
                      bm.verts.new(base + (tip - base) * 0.5 - side), bm.verts.new(base - side * 0.5)]
                bm.faces.new(vs)
                bm.to_mesh(me)
                bm.free()
                o = new_obj(f"petal{f}_{ring}_{k}", me)
                o.data.materials.append(MATS["M_LilyFlower"])
                parts.append(o)
    o = finish(join(parts, "LilyPads"), project=False)
    # petals get a planar UV (flat white material anyway); pads keep their atlas UVs
    export([o], "LilyPads.fbx")
    return [o]


def build_reeds(name, q_scale=1.0, seed=0, count=5, height=1.6):
    rnd = random.Random(seed)
    parts = []
    for k in range(count):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0, 0.6)
        c = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        h = height * rnd.uniform(0.7, 1.15)
        for j in range(2):
            b = rnd.uniform(0, math.pi) + j * math.pi / 2
            d = Vector((math.cos(b), math.sin(b), 0)) * 0.55 * q_scale
            lean = Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), 0))
            parts.append(card(f"{name}{k}_{j}", [c - d - Vector((0, 0, 0.1)), c + d - Vector((0, 0, 0.1)),
                                                c + d + Vector((0, 0, h)) + lean, c - d + Vector((0, 0, h)) + lean], Q_REED))
    o = finish(join(parts, name), project=False)
    export([o], f"{name}.fbx")
    return [o]


def build_bush(name, seed, n=7, radius=1.8, h=(0.5, 2.3)):
    """Swamp underbrush: a thicket of crossed leaf cards at eye height (+ a few reeds) that fills the gaps
    between the cypress trunks, so the arena edge reads as a wall of growth."""
    rnd = random.Random(seed)
    parts = []
    for k in range(n):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0, radius)
        z = rnd.uniform(*h)
        parts += clump(f"{name}{k}", (math.cos(a) * r, math.sin(a) * r, z), rnd.uniform(1.6, 2.6), seed=seed * 13 + k, tilt=0.5)
    for k in range(3):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0.3, radius)
        c = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        for j in range(2):
            bb = rnd.uniform(0, math.pi) + j * math.pi / 2
            d = Vector((math.cos(bb), math.sin(bb), 0)) * 0.6
            parts.append(card(f"{name}r{k}{j}", [c - d, c + d, c + d + Vector((0, 0, 1.8)), c - d + Vector((0, 0, 1.8))], Q_REED))
    o = finish(join(parts, name), project=False)
    export([o], f"{name}.fbx")
    return [o]


# ================================================================== military


def sandbag(name, loc, yaw, s=1.0, seed=0):
    rnd = random.Random(seed)
    o = sphere(name, 1.0, (0, 0, 0), scale=(0.3 * s, 0.17 * s, 0.085 * s), seg=6, rings=4, lumpy=0.0)
    for v in o.data.vertices:          # flat bottom + top, pinched (tied) ends
        v.co.z = max(v.co.z, -0.06 * s) if v.co.z < 0 else min(v.co.z, 0.075 * s)
        end = abs(v.co.x) / (0.3 * s)
        v.co.y *= 1.0 - 0.35 * end ** 3
        v.co.z *= 1.0 - 0.3 * end ** 3
        v.co += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * 0.012 * s
    o.rotation_euler = (rnd.uniform(-0.04, 0.04), rnd.uniform(-0.05, 0.05), yaw)
    o.location = loc
    activate(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
    return o


def build_sandbag_wall():
    """3 courses of staggered bags along a gentle arc (2.6 m), the ring segment around the arena."""
    parts, sd = [], 0
    for row in range(3):
        n = 5 if row % 2 == 0 else 4
        for k in range(n):
            x = (k - (n - 1) / 2) * 0.56
            y = 0.0006 * x * x * 40 - 0.05 * row
            parts.append(sandbag(f"sb{row}_{k}", (x, y, 0.07 + row * 0.15), math.atan2(0.0012 * 40 * x, 1) * 0.6, seed=sd))
            parts.append(sandbag(f"sbb{row}_{k}", (x + 0.12, y + 0.33, 0.07 + row * 0.15), 0.0, seed=sd + 500)) if row < 2 else None
            sd += 1
    parts = [p for p in parts if p]
    o = finish(join(parts, "SandbagWall"), smooth_angle=50, uv_scale=1.6)
    export([o], "SandbagWall.fbx")
    return [o]


def machine_gun(prefix, loc, yaw):
    """Water-cooled heavy MG on a tripod (olive steel)."""
    p = []
    R = Matrix.Rotation(yaw, 4, 'Z')
    T = Matrix.Translation(Vector(loc))
    p.append(box(prefix + "recv", (0.42, 0.14, 0.16), (0, 0, 0.78), m="M_OliveMetal"))
    p.append(cyl(prefix + "jacket", 0.065, 0.75, (0.58, 0, 0.8), rot=(0, math.pi / 2, 0), m="M_OliveMetal", verts=8))
    p.append(cyl(prefix + "muzzle", 0.028, 0.22, (1.05, 0, 0.8), rot=(0, math.pi / 2, 0), m="M_Iron", verts=6))
    p.append(box(prefix + "grip", (0.12, 0.05, 0.1), (-0.27, 0, 0.74), m="M_Iron"))
    p.append(box(prefix + "ammo", (0.2, 0.12, 0.17), (0.0, 0.17, 0.66), m="M_OliveMetal"))
    p.append(box(prefix + "belt", (0.03, 0.12, 0.02), (0.0, 0.1, 0.75), m="M_Iron"))
    p.append(cyl(prefix + "post", 0.04, 0.3, (0, 0, 0.58), m="M_Iron", verts=6))
    for k, a in enumerate([0.0, 2.3, -2.3]):
        d = Vector((math.cos(a), math.sin(a), 0))
        foot = d * 0.75 + Vector((0, 0, 0.0))
        top = Vector((0, 0, 0.47))
        mid = (foot + top) / 2
        L = (top - foot).length
        o = cyl(prefix + f"leg{k}", 0.025, L, mid, m="M_Iron", verts=4)
        o.rotation_euler = (top - foot).to_track_quat('Z', 'Y').to_euler()
        p.append(o)
    for o in p:
        o.matrix_world = T @ R @ o.matrix_world
    return p


def build_gun_nest():
    """Horseshoe of sandbags (2 courses, open at the back) around an MG on a tripod."""
    parts, sd = [], 100
    for row in range(3):
        n = 11 - row
        for k in range(n):
            a = -math.pi * 0.78 + k / (n - 1) * math.pi * 1.56 + (0.07 if row % 2 else 0)
            r = 1.35 - row * 0.04
            parts.append(sandbag(f"nb{row}_{k}", (math.cos(a) * r, math.sin(a) * r, 0.07 + row * 0.15), a + math.pi / 2, seed=sd))
            sd += 1
    parts = [finish(join(parts, "nestbags"), smooth_angle=50, uv_scale=1.6)]
    gun = machine_gun("mg", (0.1, 0, 0.0), 0.0)
    gun.append(box("ammoCrate", (0.5, 0.3, 0.28), (-0.6, -0.65, 0.14), (0, 0, 0.3), m="M_OliveMetal"))
    g = finish(join(gun, "nestgun"), uv_scale=3.0)
    o = join(parts + [g], "GunNest")
    export([o], "GunNest.fbx")
    return [o]


def build_field_gun():
    """Light field / AA gun: shield, long barrel with a muzzle brake, two spoked wheels, split trail."""
    p = []
    for s in (-1, 1):
        p.append(cyl(f"wheel{s}", 0.48, 0.14, (0, s * 0.78, 0.48), rot=(math.pi / 2, 0, 0), m="M_Iron", verts=10))
        p.append(cyl(f"hub{s}", 0.12, 0.22, (0, s * 0.78, 0.48), rot=(math.pi / 2, 0, 0), m="M_OliveMetal", verts=6))
        trail = box(f"trail{s}", (1.9, 0.12, 0.12), (-1.1, s * 0.32, 0.3), (0, 0.22, s * 0.18), m="M_OliveMetal")
        p.append(trail)
        p.append(box(f"spade{s}", (0.08, 0.3, 0.3), (-2.0, s * 0.5, 0.12), m="M_Iron"))
    p.append(cyl("axle", 0.05, 1.5, (0, 0, 0.48), rot=(math.pi / 2, 0, 0), m="M_Iron", verts=6))
    p.append(box("cradle", (0.9, 0.36, 0.22), (0.1, 0, 0.78), (0, -0.2, 0), m="M_OliveMetal"))
    brl = cyl("barrel", 0.065, 2.6, (1.45, 0, 1.25), rot=(0, math.pi / 2 - 0.35, 0), m="M_OliveMetal", verts=8, r2=0.055)
    p.append(brl)
    tip = Vector((1.45, 0, 1.25)) + Vector((math.cos(0.35), 0, math.sin(0.35))) * 1.3
    p.append(cyl("brake", 0.09, 0.22, tip, rot=(0, math.pi / 2 - 0.35, 0), m="M_Iron", verts=6))
    p.append(cyl("recoil", 0.09, 0.9, (0.45, 0, 1.0), rot=(0, math.pi / 2 - 0.35, 0), m="M_OliveMetal", verts=8))
    sh = box("shield", (0.06, 1.5, 0.95), (0.55, 0, 0.95), (0, -0.12, 0), m="M_OliveMetal")
    for v in sh.data.vertices:          # bent wings + a cut-down top corner
        v.co.x -= abs(v.co.y) ** 2 * 0.25
    p.append(sh)
    p.append(box("seat", (0.25, 0.25, 0.04), (-0.55, -0.45, 0.72), m="M_Iron"))
    o = finish(join(p, "FieldGun"), uv_scale=2.0)
    export([o], "FieldGun.fbx")
    return [o]


def build_hedgehog():
    """Czech hedgehog: three angle-iron beams, mutually perpendicular, standing on three points."""
    p = []
    L = 1.9
    for k, axis in enumerate([(1, 0, 0), (0, 1, 0), (0, 0, 1)]):
        sz = [0.11, 0.11, 0.11]
        sz[k] = L
        a = box(f"beam{k}", sz, (0, 0, 0), m="M_Iron")
        # angle-iron: carve an L by squeezing two edges of the section
        for v in a.data.vertices:
            for j in range(3):
                if j != k and v.co[j] > 0:
                    v.co[j] = 0.012
        p.append(a)
    o = join(p, "Hedgehog")
    q = Vector((1, 1, 1)).normalized().rotation_difference(Vector((0, 0, 1)))
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = q
    activate(o)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    zmin = min(v.co.z for v in o.data.vertices)
    for v in o.data.vertices:
        v.co.z -= zmin + 0.05
    finish(o, smooth_angle=10, uv_scale=2.5)
    export([o], "Hedgehog.fbx")
    return [o]


def build_stakes():
    """Cheval de frise: a log with sharpened stakes crossing through it, a row of spikes facing the arena."""
    p = [tube("beam", [(-1.6, 0, 0.55), (1.6, 0, 0.55)], [0.11, 0.1], 6, "M_DarkWood", seed=4, uv_len=1.0)]
    for k in range(7):
        x = -1.4 + k * 0.47
        for s in (-1, 1):
            a = s * 0.75
            d = Vector((0, math.sin(a), math.cos(a)))
            c = Vector((x, 0, 0.55))
            p.append(tube(f"st{k}{s}", [c - d * 0.75, c + d * 0.5, c + d * 0.95], [0.05, 0.045, 0.0], 4, "M_DarkWood",
                          seed=k * 3 + s, jag=0.08, uv_len=0.8))
    o = finish(join(p, "SpikeBarrier"), project=False)
    export([o], "SpikeBarrier.fbx")
    return [o]


def build_barbed_wire():
    """Concertina wire between two X-shaped picket posts (4 m)."""
    p = []
    for s in (-1, 1):
        for j in (-1, 1):
            p.append(box(f"post{s}{j}", (0.06, 0.06, 1.35), (s * 2.0, j * 0.2, 0.55), (j * 0.38, 0, 0), m="M_DarkWood"))
    pts, radii = [], []
    loops, per = 15, 7
    for i in range(loops * per + 1):
        t = i / (loops * per)
        a = t * loops * 2 * math.pi
        pts.append((-2.0 + 4.0 * t + 0.08 * math.cos(a), 0.42 * math.sin(a), 0.5 + 0.42 * math.cos(a)))
        radii.append(0.018)
    p.append(tube("coil", pts, radii, 3, "M_Iron", jag=0.0, uv_len=0.5, cap=False))
    for z in (0.15, 0.95):
        p.append(tube(f"strand{z}", [(-2.05, 0, z), (0, 0.05, z - 0.04), (2.05, 0, z)], [0.012] * 3, 3, "M_Iron", jag=0.0, cap=False))
    o = finish(join(p, "BarbedWire"), project=False, smooth_angle=60)
    project_mat(o, {"M_DarkWood"}, 2.0)
    export([o], "BarbedWire.fbx")
    return [o]


def build_tent():
    """Hooped olive canvas field tent (reference 4): half-barrel body, end walls, door flap, guy ropes."""
    me = bpy.data.meshes.new("tent")
    bm = bmesh.new()
    W, H, L, n = 2.1, 2.5, 5.2, 8
    rings = []
    for i in range(5):
        x = -L / 2 + L * i / 4
        sag = 0.06 if i % 2 else 0.0
        ring = []
        for j in range(n + 1):
            a = math.pi * j / n
            ring.append(bm.verts.new((x, math.cos(a) * W * (1 - sag * 0.4), math.sin(a) * H * (1 - sag) + 0.0)))
        rings.append(ring)
    for i in range(4):
        for j in range(n):
            bm.faces.new([rings[i][j], rings[i + 1][j], rings[i + 1][j + 1], rings[i][j + 1]])
    for i in (0, 4):
        c = bm.verts.new((rings[i][0].co.x, 0, 0))
        for j in range(n):
            f = bm.faces.new([c, rings[i][j], rings[i][j + 1]] if i == 4 else [c, rings[i][j + 1], rings[i][j]])
    bm.to_mesh(me)
    bm.free()
    t = new_obj("tentbody", me)
    t.data.materials.append(MATS["M_Canvas"])
    p = [t]
    # door flap rolled up + dark doorway
    p.append(box("door", (0.02, 1.1, 1.7), (L / 2 + 0.01, 0, 0.85), m="M_Iron"))
    p.append(cyl("roll", 0.1, 1.2, (L / 2 + 0.06, 0, 1.75), rot=(math.pi / 2, 0, 0), m="M_Canvas", verts=6))
    # ribs + skirt + ropes
    for i in range(5):
        x = -L / 2 + L * i / 4
        p.append(box(f"rib{i}", (0.08, 0.08, 0.08), (x, 0, H + 0.02), m="M_Canvas"))
    for i, x in enumerate([-L / 2 + 0.3, 0, L / 2 - 0.3]):
        for s in (-1, 1):
            top = Vector((x, s * W * 0.72, H * 0.7))
            peg = Vector((x, s * (W + 1.0), 0.0))
            p.append(tube(f"rope{i}{s}", [top, peg], [0.012, 0.012], 3, "M_Rope", jag=0, cap=False))
            p.append(box(f"peg{i}{s}", (0.05, 0.05, 0.25), peg + Vector((0, 0, 0.08)), m="M_DarkWood"))
    o = finish(join(p, "MilitaryTent"), smooth_angle=35, uv_scale=0.8)
    export([o], "MilitaryTent.fbx")
    return [o]


def build_pillbox():
    """Hexagonal concrete pillbox half sunk into an earth berm, a dark firing slit, sandbags on the roof (reference 5)."""
    p = [cyl("lower", 2.3, 1.0, (0, 0, 0.5), m="M_Concrete", verts=6)]
    p.append(cyl("slit", 2.05, 0.26, (0, 0, 1.13), m="M_Iron", verts=6))
    p.append(cyl("upper", 2.35, 0.35, (0, 0, 1.43), m="M_Concrete", verts=6))
    p.append(cyl("roof", 2.6, 0.22, (0, 0, 1.71), m="M_Concrete", verts=6, r2=2.45))
    # jagged shell damage on the roof edge
    for k in range(3):
        a = k * 2.1 + 0.4
        p.append(box(f"chunk{k}", (0.6, 0.5, 0.25), (math.cos(a) * 2.3, math.sin(a) * 2.3, 1.75), (0.3, 0.2, a), m="M_Concrete", jitter=0.08))
    berm = cyl("berm", 4.3, 1.2, (0, 0, 0.45), m="M_Mud", verts=10, r2=2.4)
    for v in berm.data.vertices:
        v.co += Vector((0, 0, noise.noise(v.co * 0.7) * 0.25))
    p.append(berm)
    for k in range(9):
        a = k * 2 * math.pi / 9
        p.append(sandbag(f"rb{k}", (math.cos(a) * 1.7, math.sin(a) * 1.7, 1.88), a + math.pi / 2, seed=300 + k))
    p.append(cyl("vent", 0.12, 0.5, (0.6, 0.4, 2.0), m="M_Iron", verts=6))
    o = finish(join(p, "Pillbox"), smooth_angle=20, uv_scale=1.0)
    export([o], "Pillbox.fbx")
    return [o]


def build_searchlight(name="Searchlight", with_tower=False):
    """Drum searchlight on a yoke. The head is a separate object ("<name>_Head", pivot on the yoke axis) so the
    game can sweep it; its lens faces local +X in Blender (= -Z/forward after the FBX axis swap is handled in Unity)."""
    base_parts = []
    z0 = 0.0
    if with_tower:
        Ht = 6.0
        for sx in (-1, 1):
            for sy in (-1, 1):
                p0, p1 = Vector((sx * 1.3, sy * 1.3, 0)), Vector((sx * 0.95, sy * 0.95, Ht))
                o = cyl(f"leg{sx}{sy}", 0.11, (p1 - p0).length, (p0 + p1) / 2, m="M_DarkWood", verts=5)
                o.rotation_euler = (p1 - p0).to_track_quat('Z', 'Y').to_euler()
                base_parts.append(o)
        for k, z in enumerate([1.6, 3.4, 5.0]):
            w = 1.3 - (1.3 - 0.95) * z / Ht
            for s in (-1, 1):
                base_parts.append(box(f"brx{k}{s}", (2 * w + 0.1, 0.07, 0.12), (0, s * w, z), (0.0, 0.5 if k % 2 else -0.5, 0), m="M_DarkWood"))
                base_parts.append(box(f"bry{k}{s}", (0.07, 2 * w + 0.1, 0.12), (s * w, 0, z), (0.5 if k % 2 else -0.5, 0, 0), m="M_DarkWood"))
        base_parts.append(box("deck", (2.6, 2.6, 0.12), (0, 0, Ht), m="M_Planks"))
        for s in (-1, 1):
            base_parts.append(box(f"railx{s}", (2.6, 0.06, 0.06), (0, s * 1.27, Ht + 0.95), m="M_DarkWood"))
            base_parts.append(box(f"raily{s}", (0.06, 2.6, 0.06), (s * 1.27, 0, Ht + 0.95), m="M_DarkWood"))
            for t in (-1, 1):
                base_parts.append(box(f"rp{s}{t}", (0.08, 0.08, 1.0), (s * 1.27, t * 1.27, Ht + 0.5), m="M_DarkWood"))
            base_parts.append(box(f"sandbags{s}", (2.4, 0.35, 0.35), (0, s * 1.05, Ht + 0.22), m="M_Sandbag", jitter=0.03))
        # ladder
        for s in (-1, 1):
            base_parts.append(box(f"lad{s}", (0.06, 0.06, Ht), (1.45, s * 0.22, Ht / 2), (0, -0.08, 0), m="M_DarkWood"))
        for k in range(10):
            base_parts.append(box(f"rung{k}", (0.04, 0.44, 0.04), (1.45 + 0.08 * (0.5 - k / 10) * 3, 0, 0.4 + k * 0.58), m="M_DarkWood"))
        # tin roof on 4 posts
        for sx in (-1, 1):
            for sy in (-1, 1):
                base_parts.append(box(f"rpost{sx}{sy}", (0.07, 0.07, 2.0), (sx * 1.15, sy * 1.15, Ht + 1.0), m="M_DarkWood"))
        base_parts.append(box("roof", (2.9, 2.9, 0.05), (0, 0, Ht + 2.05), (0.08, 0, 0), m="M_OliveMetal"))
        z0 = Ht + 0.06
    # pedestal + yoke
    base_parts.append(cyl("ped", 0.3, 0.12, (0, 0, z0 + 0.06), m="M_OliveMetal", verts=8))
    base_parts.append(cyl("col", 0.09, 0.6, (0, 0, z0 + 0.4), m="M_OliveMetal", verts=6))
    for s in (-1, 1):
        base_parts.append(box(f"yoke{s}", (0.08, 0.05, 0.55), (0, s * 0.42, z0 + 0.92), m="M_OliveMetal"))
    base_parts.append(box("yokebar", (0.1, 0.9, 0.08), (0, 0, z0 + 0.68), m="M_OliveMetal"))
    base = finish(join(base_parts, name), uv_scale=2.0)
    pivot = Vector((0, 0, z0 + 1.08))
    head = [cyl("drum", 0.4, 0.72, pivot + Vector((0.05, 0, 0)), rot=(0, math.pi / 2, 0), m="M_OliveMetal", verts=10)]
    head.append(cyl("lens", 0.36, 0.04, pivot + Vector((0.43, 0, 0)), rot=(0, math.pi / 2, 0), m="M_LampLens", verts=10))
    head.append(cyl("rim", 0.42, 0.08, pivot + Vector((0.4, 0, 0)), rot=(0, math.pi / 2, 0), m="M_Iron", verts=10, cap=False))
    head.append(cyl("back", 0.25, 0.2, pivot + Vector((-0.4, 0, 0)), rot=(0, math.pi / 2, 0), m="M_Iron", verts=8))
    head.append(box("handle", (0.3, 0.04, 0.04), pivot + Vector((-0.25, 0, 0.45)), m="M_Iron"))
    h = finish(join(head, name + "_Head"), uv_scale=2.0)
    set_origin(h, pivot)
    h.parent = base
    h.matrix_parent_inverse = base.matrix_world.inverted()
    empty(name + "_Lens", pivot + Vector((0.6, 0, 0)), h)
    export([base], name + ".fbx")
    return [base, h]


def build_floodlight():
    """Military lighting pole: a timber pole, a crossbar with two box lamps tilted down, a cable to a generator box."""
    p = [cyl("pole", 0.09, 5.4, (0, 0, 2.7), m="M_DarkWood", verts=6, r2=0.07)]
    p.append(box("bar", (0.08, 1.3, 0.08), (0.05, 0, 5.2), m="M_DarkWood"))
    for s in (-1, 1):
        lamp = [box(f"lampbox{s}", (0.34, 0.36, 0.3), (0.25, s * 0.5, 5.05), m="M_OliveMetal"),
                box(f"lens{s}", (0.02, 0.3, 0.24), (0.43, s * 0.5, 5.05), m="M_LampLens"),
                box(f"hood{s}", (0.18, 0.42, 0.03), (0.48, s * 0.5, 5.23), m="M_OliveMetal")]
        for o in lamp:
            o.rotation_euler = (0, 0.55, 0)
            activate(o)
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        p += lamp
    p.append(tube("cable", [(0.0, 0.09, 4.9), (0.06, 0.12, 2.0), (0.1, 0.2, 0.05), (0.9, 0.9, 0.02)], [0.02] * 4, 3, "M_Iron", jag=0, cap=False))
    gen = [box("gen", (0.9, 0.6, 0.55), (1.3, 1.1, 0.33), (0, 0, 0.4), m="M_OliveMetal"),
           cyl("exh", 0.04, 0.5, (1.55, 1.25, 0.8), m="M_Iron", verts=5)]
    for s in (-1, 1):
        gen.append(cyl(f"gw{s}", 0.14, 0.08, (1.3 + s * 0.2, 1.1 - s * 0.15, 0.12), rot=(math.pi / 2, 0, 0.4), m="M_Iron", verts=6))
    p += gen
    o = finish(join(p, "Floodlight"), uv_scale=2.0)
    for s in (-1, 1):
        empty(f"Lamp{(s + 1) // 2}", (0.5, s * 0.5, 5.0), o)
    export([o], "Floodlight.fbx")
    return [o]


def build_torch():
    """Swamp torch: a crooked stake with a pitch-soaked rag head in an iron cage (flames are particles in Unity)."""
    p = [tube("stake", [(0, 0, -0.3), (0.03, 0.02, 0.9), (0.0, 0.05, 1.75)], [0.055, 0.045, 0.04], 5, "M_DarkWood", seed=2, uv_len=0.8)]
    p.append(cyl("rag", 0.1, 0.3, (0.0, 0.05, 1.85), m="M_Rope", verts=6, r2=0.08))
    p.append(cyl("ember", 0.075, 0.05, (0.0, 0.05, 2.01), m="M_LanternGlow", verts=6))
    for k in range(4):
        a = k * math.pi / 2
        p.append(box(f"cage{k}", (0.02, 0.02, 0.38), (math.cos(a) * 0.12, 0.05 + math.sin(a) * 0.12, 1.88), (math.sin(a) * 0.25, -math.cos(a) * 0.25, 0), m="M_Iron"))
    p.append(cyl("band", 0.13, 0.04, (0.0, 0.05, 1.72), m="M_Iron", verts=6, cap=False))
    o = finish(join(p, "SwampTorch"), project=False)
    project_mat(o, {"M_Rope", "M_Iron", "M_LanternGlow"}, 3.0)
    empty("Flame", (0.0, 0.05, 2.05), o)
    export([o], "SwampTorch.fbx")
    return [o]


def lantern_parts(prefix, at):
    """Hurricane lantern: tin base, glowing glass globe, wire guard, chimney cap and bail handle."""
    at = Vector(at)
    p = [cyl(prefix + "base", 0.09, 0.06, at + Vector((0, 0, 0.03)), m="M_OliveMetal", verts=8),
         cyl(prefix + "glass", 0.065, 0.16, at + Vector((0, 0, 0.14)), m="M_LanternGlow", verts=8, r2=0.05),
         cyl(prefix + "cap", 0.075, 0.05, at + Vector((0, 0, 0.245)), m="M_OliveMetal", verts=8, r2=0.03),
         cyl(prefix + "chim", 0.025, 0.05, at + Vector((0, 0, 0.29)), m="M_Iron", verts=5)]
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        p.append(box(prefix + f"wire{k}", (0.012, 0.012, 0.18), at + Vector((math.cos(a) * 0.085, math.sin(a) * 0.085, 0.14)), m="M_Iron"))
    p.append(tube(prefix + "bail", [at + Vector((-0.08, 0, 0.22)), at + Vector((0, 0, 0.36)), at + Vector((0.08, 0, 0.22))],
                  [0.008] * 3, 3, "M_Iron", jag=0, cap=False))
    return p


def build_lanterns():
    """Lantern (stands on crates, sandbags, the pillbox) and TreeLantern (hung off a trunk on an iron bracket that
    sticks out along +X; the Unity builder turns the Flame locator away from the trunk)."""
    lan = finish(join(lantern_parts("l", (0, 0, 0)), "Lantern"), uv_scale=3.0)
    empty("Flame", (0, 0, 0.14), lan)
    export([lan], "Lantern.fbx")
    parts = [box("plate", (0.04, 0.16, 0.22), (0.02, 0, 0), m="M_Iron"),
             box("arm", (0.55, 0.03, 0.03), (0.29, 0, 0.06), m="M_Iron"),
             box("strut", (0.03, 0.03, 0.36), (0.16, 0, -0.06), (0, 0.85, 0), m="M_Iron"),
             tube("hook", [(0.55, 0, 0.06), (0.55, 0, -0.08)], [0.008, 0.008], 3, "M_Iron", jag=0, cap=False)]
    parts += lantern_parts("t", (0.55, 0, -0.45))
    tl = finish(join(parts, "TreeLantern"), uv_scale=3.0)
    empty("Flame", (0.55, 0, -0.31), tl)
    export([tl], "TreeLantern.fbx")
    return [lan, tl]


def build_burn_barrel():
    p = [cyl("drum", 0.3, 0.88, (0, 0, 0.44), m="M_OliveMetal", verts=8, cap=False)]
    for z in (0.2, 0.62):
        p.append(cyl(f"hoop{z}", 0.315, 0.04, (0, 0, z), m="M_Iron", verts=8, cap=False))
    p.append(cyl("bottom", 0.3, 0.02, (0, 0, 0.02), m="M_Iron", verts=8))
    p.append(cyl("coals", 0.28, 0.03, (0, 0, 0.78), m="M_LanternGlow", verts=8))
    o = finish(join(p, "BurnBarrel"), uv_scale=2.0)
    empty("Flame", (0, 0, 0.85), o)
    export([o], "BurnBarrel.fbx")
    return [o]


def build_crates():
    p = [box("c0", (0.9, 0.55, 0.5), (0, 0, 0.25), m="M_OliveMetal", jitter=0.01),
         box("c1", (0.9, 0.55, 0.5), (0.1, 0.05, 0.75), (0, 0, 0.2), m="M_OliveMetal", jitter=0.01),
         box("c2", (0.7, 0.45, 0.4), (0.0, 0.75, 0.2), (0, 0, -0.3), m="M_OliveMetal", jitter=0.01)]
    for k, (x, y, z, r) in enumerate([(0, 0, 0.5, 0), (0.1, 0.05, 1.0, 0.2), (0, 0.75, 0.4, -0.3)]):
        p.append(box(f"lid{k}", (0.92, 0.57, 0.04), (x, y, z + 0.005), (0, 0, r), m="M_DarkWood"))
    p.append(cyl("drum", 0.29, 0.88, (-0.85, 0.3, 0.44), m="M_OliveMetal", verts=8))
    p.append(cyl("drum2", 0.29, 0.88, (-0.6, -0.6, 0.29), rot=(math.pi / 2, 0, 0.6), m="M_Iron", verts=8))
    o = finish(join(p, "SupplyPile"), uv_scale=2.2)
    export([o], "SupplyPile.fbx")
    return [o]


def build_duckboard():
    p = []
    for s in (-1, 1):
        p.append(box(f"run{s}", (3.2, 0.1, 0.1), (0, s * 0.3, 0.05), m="M_DarkWood"))
    for k in range(13):
        p.append(box(f"slat{k}", (0.18, 0.85, 0.04), (-1.5 + k * 0.25, 0, 0.12), (0, 0, random.uniform(-0.05, 0.05)), m="M_Planks", jitter=0.008))
    o = finish(join(p, "Duckboard"), uv_scale=1.5)
    export([o], "Duckboard.fbx")
    return [o]


def build_radio_mast():
    """Triangular lattice radio mast (reference 6), a black silhouette above the canopy line."""
    p = []
    H, r0, r1 = 16.0, 0.9, 0.35
    corners = lambda z, r: [Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in (0, 2.094, 4.189)]
    for k in range(3):
        a0, a1 = corners(0, r0)[k], corners(H, r1)[k]
        o = cyl(f"leg{k}", 0.05, (a1 - a0).length, (a0 + a1) / 2, m="M_Iron", verts=4)
        o.rotation_euler = (a1 - a0).to_track_quat('Z', 'Y').to_euler()
        p.append(o)
    seg = 8
    for i in range(seg):
        z0, z1 = H * i / seg, H * (i + 1) / seg
        c0, c1 = corners(z0, r0 + (r1 - r0) * i / seg), corners(z1, r0 + (r1 - r0) * (i + 1) / seg)
        for k in range(3):
            a, b = c0[k], c1[(k + 1) % 3]
            o = cyl(f"br{i}{k}", 0.025, (b - a).length, (a + b) / 2, m="M_Iron", verts=3)
            o.rotation_euler = (b - a).to_track_quat('Z', 'Y').to_euler()
            p.append(o)
    p.append(cyl("ant", 0.03, 3.0, (0, 0, H + 1.5), m="M_Iron", verts=4))
    p.append(box("lamp", (0.18, 0.18, 0.18), (0, 0, H + 3.0), m="M_LanternGlow"))
    o = finish(join(p, "RadioMast"), uv_scale=3.0)
    empty("Beacon", (0, 0, 19.0), o)
    export([o], "RadioMast.fbx")
    return [o]


# ------------------------------------------------------------------ build all
groups.append(build_cypress("Cypress_A", 17.0, (0.6, 0.6), 101))
groups.append(build_cypress("Cypress_B", 13.5, (2.4, 1.6), 202, leafy=0.8, mossy=1.3))
groups.append(build_cypress("Cypress_C", 10.5, (4.0, 1.0), 303, leafy=0.45, mossy=1.5))
groups.append(build_knees())
groups.append(build_fallen_log())
groups.append(build_stump())
groups.append(build_lily_pads())
groups.append(build_reeds("Reeds", 1.0, seed=3, count=6, height=1.7))
groups.append(build_reeds("Ferns", 1.4, seed=9, count=4, height=0.7))
groups.append(build_bush("SwampBush_A", 41))
groups.append(build_bush("SwampBush_B", 57, n=9, radius=2.4, h=(0.8, 3.2)))
groups.append(build_sandbag_wall())
groups.append(build_gun_nest())
groups.append(build_field_gun())
groups.append(build_hedgehog())
groups.append(build_stakes())
groups.append(build_barbed_wire())
groups.append(build_tent())
groups.append(build_pillbox())
groups.append(build_searchlight("Searchlight"))
groups.append(build_searchlight("WatchTower", with_tower=True))
groups.append(build_floodlight())
groups.append(build_torch())
groups.append(build_burn_barrel())
groups.append(build_lanterns())
groups.append(build_crates())
groups.append(build_duckboard())
groups.append(build_radio_mast())
print("SWAMP DONE")

if PREVIEW:
    import math as _m
    roots = [g[0] for g in groups]
    cols = 7
    for i, o in enumerate(roots):
        big = o.name.startswith("Cypress") or o.name in ("RadioMast", "WatchTower")
        o.location = ((i % cols) * 6.5 - 20, (i // cols) * 7.0 + (6 if big else 0), 0)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    cam.location = (0, -38, 18)
    cam.rotation_euler = (Vector((0, 8, 3)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = 28
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 1800, 1000
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", PREVIEW)
