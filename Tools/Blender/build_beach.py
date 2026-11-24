"""
Elder Tree - Shark arena kit: "Bleached Atoll", a grey-blue day on a crescent island with a wooden dock
(Blender 4.2+/5.x, headless).

    blender -b --factory-startup --python build_beach.py -- <abs out_dir> [preview.png]

PSX rules (Docs/ART_DIRECTION.md): no bevels, 3-8 sided tubes, faceted shading, props 50-800 tris; the
whale skeleton and the shipwreck are landscape pieces (~1-2k tris). All surface detail comes from the
point-filtered, dithered Unity textures (ElderTreeBuilder.Beach BuildBeachArt).

Each prop is its own FBX with the origin at its base (dock pieces: at deck level). Animated parts are child
objects with their pivot at the joint, so Unity can move them:
  Palm_*    child "Crown" (fronds + coconuts), pivot at the trunk top          -> PalmSway
  Crab      children "Leg0".."Leg5" (pivot at the hip), "ClawL"/"ClawR", empty "Front"  -> Crab
  SeaBird   children "WingL"/"WingR" (pivot at the shoulder), empty "Beak"     -> SeaBird
  Pennant   child "Flag" (pivot at the pole)                                    -> PalmSway

Material slot names match the Unity materials:
  M_PalmBark    ringed palm trunk
  M_BeachFlora  cutout atlas, 2x2: green frond (TL), dead frond (TR), dune grass (BL), fishing net (BR)
  M_Coconut     fibrous husk          M_Bone       bleached bone / coconut meat
  M_DockPlanks  sea-bleached deck     M_DockPile   tarred piling, barnacles, weed line
  M_SeaRock     wet black basalt      M_Driftwood  silver-grey driftwood
  M_Sail        tattered black sailcloth (two-sided)
  M_CrabShell   dark red carapace     M_BirdDark / M_BirdPale  gull plumage
  M_Iron, M_Rope, M_DarkWood (existing)
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector, Matrix, Euler, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(os.path.dirname(bpy.data.filepath or "."), "beach")
PREVIEW = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
MATS = {}


def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    MATS[name] = m


for n, c in [("M_PalmBark", (0.36, 0.31, 0.25)), ("M_BeachFlora", (0.22, 0.32, 0.2)), ("M_Coconut", (0.32, 0.22, 0.13)),
             ("M_Bone", (0.78, 0.76, 0.68)), ("M_DockPlanks", (0.46, 0.40, 0.33)), ("M_DockPile", (0.17, 0.15, 0.13)),
             ("M_SeaRock", (0.15, 0.16, 0.17)), ("M_Driftwood", (0.55, 0.53, 0.5)), ("M_Sail", (0.12, 0.12, 0.13)),
             ("M_CrabShell", (0.45, 0.12, 0.07)), ("M_BirdDark", (0.16, 0.17, 0.19)), ("M_BirdPale", (0.62, 0.64, 0.66)),
             ("M_Iron", (0.17, 0.17, 0.18)), ("M_Rope", (0.48, 0.39, 0.25)), ("M_DarkWood", (0.2, 0.15, 0.11))]:
    mat(n, c)

# atlas quadrants (u0, v0, u1, v1) of T_BeachFlora
Q_FROND = (0.0, 0.5, 0.5, 1.0)
Q_DEAD = (0.5, 0.5, 1.0, 1.0)
Q_GRASS = (0.0, 0.0, 0.5, 0.5)
Q_NET = (0.5, 0.0, 1.0, 0.5)


# ------------------------------------------------------------------ primitives
def new_obj(name, me):
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return o


def activate(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def box(name, size, loc=(0, 0, 0), rot=(0, 0, 0), m="M_DockPlanks", jitter=0.0):
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


def cyl(name, r, depth, loc=(0, 0, 0), rot=(0, 0, 0), m="M_DockPile", verts=8, r2=None, cap=True):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2 if r2 is not None else r, depth=depth,
                                    location=loc, rotation=rot, end_fill_type='NGON' if cap else 'NOTHING')
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(MATS[m])
    return o


def rod(name, a, b, r, m, verts=4, r2=None):
    """Cylinder from point a to point b."""
    a, b = Vector(a), Vector(b)
    o = cyl(name, r, (b - a).length, (a + b) / 2, m=m, verts=verts, r2=r2)
    o.rotation_euler = (b - a).to_track_quat('Z', 'Y').to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return o


def sphere(name, r, loc, scale=(1, 1, 1), m="M_SeaRock", seg=8, rings=5, lumpy=0.0, seed=0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if lumpy:
        for v in o.data.vertices:
            v.co += v.normal * noise.noise(v.co * 1.7 + Vector((seed, seed * 2, 0))) * lumpy
    o.data.materials.append(MATS[m])
    return o


def tube(name, pts, radii, sides, m, seed=0, jag=0.15, uv_len=1.2, cap=True):
    """Low-poly tube along pts with u-around / v-along UVs."""
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
        for j in range(sides):
            a = 2 * math.pi * j / sides
            k = 1 + jag * noise.noise(Vector((i * 1.7 + seed, j * 2.3, seed * 0.37)))
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
        bm.faces.new(rings[-1])
    bm.to_mesh(me)
    bm.free()
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    return o


def card(name, corners, q, m="M_BeachFlora"):
    """Quad from 4 corners (bl, br, tr, tl) mapped onto atlas quadrant q (2% inset against bleeding)."""
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


def strip(name, pts, widths, sides, q, m="M_BeachFlora"):
    """A bending card strip (palm frond, flag): cross-sections at pts, `sides` = across vectors. u across, v along."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    u0, v0, u1, v1 = q
    du, dv = (u1 - u0) * 0.02, (v1 - v0) * 0.02
    pts = [Vector(p) for p in pts]
    acc = [0.0]
    for i in range(1, len(pts)):
        acc.append(acc[-1] + (pts[i] - pts[i - 1]).length)
    rows = []
    for p, w, s in zip(pts, widths, sides):
        s = Vector(s).normalized() * max(w, 0.02) * 0.5
        rows.append((bm.verts.new(p - s), bm.verts.new(p + s)))
    for i in range(len(rows) - 1):
        f = bm.faces.new([rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]])
        f.normal_update()
        if f.normal.z < 0:            # face up: the material is two-sided but lit by this normal, so fronds seen from
            f.normal_flip()           # below still catch the sky instead of going black
        ta, tb = acc[i] / acc[-1], acc[i + 1] / acc[-1]
        va, vb = v0 + dv + (v1 - v0 - 2 * dv) * ta, v0 + dv + (v1 - v0 - 2 * dv) * tb
        for loop, (uu, vv) in zip(f.loops, [(u0 + du, va), (u1 - du, va), (u1 - du, vb), (u0 + du, vb)]):
            loop[uv].uv = (uu, vv)
    bm.to_mesh(me)
    bm.free()
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    return o


def clump(name, center, size, q=Q_GRASS, n=3, tilt=0.3, seed=0, h=None):
    """n crossed upright cards around a vertical axis, standing on `center` (grass tufts)."""
    rnd = random.Random(seed)
    c = Vector(center)
    h = h if h is not None else size * 0.8
    parts = []
    for k in range(n):
        a = k * math.pi / n + rnd.uniform(-0.25, 0.25)
        d = Vector((math.cos(a), math.sin(a), 0)) * size * 0.5
        lean = Vector((rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt), 0)) * h
        top = Vector((0, 0, h)) + lean
        parts.append(card(f"{name}{k}", [c - d, c + d, c + d + top, c - d + top], q))
    return parts


def join(objs, name):
    activate(objs[0])
    for o in objs:
        o.select_set(True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    # bake the active part's rotation / scale into the mesh: Unity places props by overwriting the root rotation
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return o


def shade(o, angle=30):
    activate(o)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))
    except Exception:
        bpy.ops.object.shade_smooth()


def finish(o, smooth_angle=30, uv_scale=1.0, project=True, origin=(0, 0, 0)):
    set_origin(o, origin)
    activate(o)
    if project:
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.cube_project(cube_size=1.0 / uv_scale, scale_to_bounds=False, correct_aspect=True)
        bpy.ops.object.mode_set(mode='OBJECT')
    shade(o, smooth_angle)
    return o


def project(o, uv_scale=1.0, du=0.0, dv=0.0):
    """Cube-project one object's UVs (before joining), then shift them (per-piece variety in a tiling texture)."""
    activate(o)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.cube_project(cube_size=1.0 / uv_scale, scale_to_bounds=False, correct_aspect=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    if du or dv:
        for loop in o.data.uv_layers.active.data:
            loop.uv = (loop.uv[0] + du, loop.uv[1] + dv)
    return o


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


def parent_to(child, parent):
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()


def empty(name, loc, parent):
    """Named locator exported with the prop (anchors for the Unity builder / scripts)."""
    e = bpy.data.objects.new(name, None)
    scene.collection.objects.link(e)
    e.location = loc
    parent_to(e, parent)
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
    tris = 0
    for o in objs:
        for c in [o] + list(o.children_recursive):
            if c.type == 'MESH':
                tris += sum(len(p.vertices) - 2 for p in c.data.polygons)
    print(f"EXPORTED {fname} tris={tris}")


def set_origin(o, p):
    activate(o)
    scene.cursor.location = p
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    scene.cursor.location = (0, 0, 0)


groups = []


# ================================================================== palms
def frond(name, top, yaw, elev, length, droop, width, q, seed):
    """A palm frond: a strip arching out of the crown and drooping toward the tip, slightly V-folded."""
    rnd = random.Random(seed)
    segs = 5
    d = Vector((math.cos(yaw), math.sin(yaw), 0))
    side = Vector((-math.sin(yaw), math.cos(yaw), 0))
    pts, sides, widths = [], [], []
    p = Vector(top)
    e = elev
    for i in range(segs + 1):
        t = i / segs
        pts.append(p.copy())
        twist = rnd.uniform(-0.15, 0.15)
        sides.append(side + Vector((0, 0, twist)))
        widths.append(width * (0.25 + 1.6 * t * (1 - t) * 1.5 if t < 0.95 else 0.08))
        step = length / segs
        p = p + (d * math.cos(e) + Vector((0, 0, math.sin(e)))) * step
        e -= droop / segs
    return strip(name, pts, widths, sides, q)


def build_palm(name, height, bend, seed, fronds=11, dead=3, nuts=5, lean_yaw=0.0):
    """Coconut palm: a ringed trunk that curves as it rises (reference 4), a crown of arching fronds, a few dead
    brown fronds hanging against the trunk and a cluster of coconuts under the crown."""
    rnd = random.Random(seed)
    random.seed(seed)
    n = 11
    dx, dy = math.cos(lean_yaw), math.sin(lean_yaw)
    pts, radii = [], []
    for i in range(n):
        t = i / (n - 1)
        off = bend * height * (t ** 1.7)
        pts.append((dx * off, dy * off, t * height))
        r = 0.2 - 0.07 * t + (0.1 * max(0.0, 0.25 - t) * 4 if t < 0.25 else 0)   # flared foot
        radii.append(r * (1.07 if i % 2 == 0 else 0.95))                          # leaf-scar rings
    trunk = tube(f"{name}_trunk", pts, radii, 6, "M_PalmBark", seed=seed, jag=0.05, uv_len=0.9)
    top = Vector(pts[-1])
    crown_parts = [sphere(f"{name}_head", 0.24, top + Vector((0, 0, 0.05)), (1, 1, 1.3), "M_PalmBark", seg=6, rings=4)]
    for k in range(fronds):
        yaw = k * 2 * math.pi / fronds + rnd.uniform(-0.2, 0.2)
        elev = math.radians(rnd.uniform(18, 48))
        crown_parts.append(frond(f"{name}_fr{k}", top + Vector((0, 0, 0.15)), yaw, elev, rnd.uniform(3.6, 4.6),
                                 math.radians(rnd.uniform(75, 115)), rnd.uniform(1.1, 1.4), Q_FROND, seed * 13 + k))
    for k in range(dead):
        yaw = rnd.uniform(0, 2 * math.pi)
        crown_parts.append(frond(f"{name}_dead{k}", top + Vector((0, 0, -0.1)), yaw, math.radians(-55), rnd.uniform(1.8, 2.4),
                                 math.radians(30), 0.75, Q_DEAD, seed * 17 + k))
    for k in range(nuts):
        a = k * 2 * math.pi / nuts + rnd.uniform(-0.3, 0.3)
        c = top + Vector((math.cos(a) * 0.24, math.sin(a) * 0.24, -0.22 - rnd.uniform(0, 0.12)))
        crown_parts.append(sphere(f"{name}_nut{k}", 0.13, c, (1, 1, 1.15), "M_Coconut", seg=6, rings=4))
    crown = join(crown_parts, "Crown")
    set_origin(crown, top)
    shade(crown, 50)
    trunk = finish(join([trunk], name), project=False, smooth_angle=50)
    parent_to(crown, trunk)
    export([trunk], f"{name}.fbx")
    return [trunk]


def coconut(name, loc, seed, s=1.0):
    o = sphere(name, 0.13 * s, loc, (1, 1.12, 0.95), "M_Coconut", seg=7, rings=5, lumpy=0.012, seed=seed)
    return project(o, 4.0, random.random(), random.random())


def build_coconuts():
    random.seed(77)
    o = finish(join([coconut("c", (0, 0, 0.12), 1)], "Coconut"), project=False, smooth_angle=60)
    export([o], "Coconut.fbx")
    parts = [coconut(f"c{k}", (math.cos(k * 2.1) * 0.22 * (k > 0), math.sin(k * 2.1) * 0.22 * (k > 0), 0.11), k, random.uniform(0.9, 1.1))
             for k in range(4)]
    parts.append(coconut("top", (0.08, 0.05, 0.32), 9))
    # a split husk: outer shell + bleached meat
    half = sphere("half", 0.14, (0.55, 0.15, 0.0), (1, 1.1, 1), "M_Coconut", seg=7, rings=6)
    bm = bmesh.new()
    bm.from_mesh(half.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z > 0.01], context='VERTS')
    bm.to_mesh(half.data)
    bm.free()
    half.rotation_euler = (math.radians(180), 0, 0)
    half.location.z = 0.13
    project(half, 4.0)
    meat = cyl("meat", 0.12, 0.02, (0.55, 0.15, 0.125), m="M_Bone", verts=7)
    pile = finish(join(parts + [half, meat], "CoconutPile"), project=False, smooth_angle=60)
    export([pile], "CoconutPile.fbx")
    return [o, pile]


# ================================================================== critters
def build_crab():
    """Shore crab, ~0.5 m across: flat carapace, stalk eyes, two claws and six legs (separate, pivots at the hip)."""
    random.seed(5)
    body_parts = [sphere("cara", 0.16, (0, 0, 0.13), (1.35, 1.0, 0.42), "M_CrabShell", seg=8, rings=5)]
    # front ridge + rim spikes
    for k in (-1, 1):
        body_parts.append(rod(f"eye{k}", (0.05 * k, 0.12, 0.15), (0.06 * k, 0.15, 0.23), 0.012, "M_CrabShell", 3))
        body_parts.append(sphere(f"ball{k}", 0.022, (0.06 * k, 0.15, 0.235), m="M_Iron", seg=4, rings=3))
        for s in range(2):
            body_parts.append(rod(f"spk{k}{s}", (0.17 * k, 0.04 - s * 0.06, 0.13), (0.24 * k, 0.07 - s * 0.06, 0.15), 0.014, "M_CrabShell", 3, r2=0.0))
    body_parts.append(sphere("belly", 0.12, (0, -0.01, 0.09), (1.2, 0.9, 0.35), "M_BirdPale", seg=6, rings=3))
    body = finish(join(body_parts, "Crab"), project=False, smooth_angle=40)
    front = empty("Front", (0, 0.4, 0.1), body)
    # legs: hip on the carapace rim -> knee up and out -> foot on the ground
    for i in range(6):
        side = -1 if i < 3 else 1
        j = i % 3
        y = 0.06 - j * 0.075
        hip = Vector((0.15 * side, y, 0.12))
        knee = Vector((0.3 * side, y - 0.01 + (j - 1) * 0.03, 0.2))
        foot = Vector((0.42 * side, y - 0.03 + (j - 1) * 0.07, 0.0))
        leg = join([rod("a", hip, knee, 0.022, "M_CrabShell", 3), rod("b", knee, foot, 0.016, "M_CrabShell", 3, r2=0.006)], f"Leg{i}")
        set_origin(leg, hip)
        shade(leg, 20)
        parent_to(leg, body)
    for side, nm in ((-1, "ClawL"), (1, "ClawR")):
        sh = Vector((0.1 * side, 0.12, 0.12))
        el = Vector((0.2 * side, 0.22, 0.17))
        wr = Vector((0.14 * side, 0.32, 0.15))
        parts = [rod("arm1", sh, el, 0.024, "M_CrabShell", 4), rod("arm2", el, wr, 0.026, "M_CrabShell", 4),
                 sphere("palm", 0.06, wr + Vector((0, 0.04, 0)), (0.8, 1.2, 0.7), "M_CrabShell", seg=6, rings=4),
                 rod("fing1", wr + Vector((0.0, 0.09, 0.01)), wr + Vector((-0.02 * side, 0.19, 0.0)), 0.022, "M_CrabShell", 3, r2=0.004),
                 rod("fing2", wr + Vector((0.03 * side, 0.09, -0.01)), wr + Vector((0.01 * side, 0.17, -0.02)), 0.018, "M_Iron", 3, r2=0.003)]
        claw = join(parts, nm)
        set_origin(claw, sh)
        shade(claw, 20)
        parent_to(claw, body)
    export([body], "Crab.fbx")
    return [body]


def build_bird():
    """A black-backed gull, 1.4 m wingspan: a tapered body, pale head, hooked beak, and two wings (pivot at the
    shoulder) with swept, dark-tipped outer halves."""
    parts = [tube("body", [(0, -0.32, 0.0), (0, -0.12, 0.01), (0, 0.08, 0.02), (0, 0.2, 0.04)], [0.02, 0.07, 0.075, 0.045], 5, "M_BirdPale", jag=0.0),
             sphere("head", 0.055, (0, 0.24, 0.07), (0.9, 1.15, 0.95), "M_BirdPale", seg=6, rings=4),
             rod("beak", (0, 0.29, 0.065), (0, 0.38, 0.05), 0.016, "M_Iron", 3, r2=0.003),
             card("tail", [(-0.07, -0.42, 0.0), (0.07, -0.42, 0.0), (0.04, -0.28, 0.01), (-0.04, -0.28, 0.01)], (0, 0, 1, 1), "M_BirdDark"),
             box("back", (0.1, 0.3, 0.02), (0, -0.04, 0.07), m="M_BirdDark")]
    body = finish(join(parts, "SeaBird"), project=False, smooth_angle=50)
    empty("Beak", (0, 0.4, 0.05), body)
    for side, nm in ((-1, "WingL"), (1, "WingR")):
        sh = Vector((0.06 * side, 0.03, 0.04))
        inner = box("in", (0.3, 0.2, 0.012), sh + Vector((0.16 * side, -0.02, 0)), m="M_BirdDark")
        outer = box("out", (0.36, 0.14, 0.01), sh + Vector((0.47 * side, -0.07, 0)), rot=(0, 0, math.radians(-12 * side)), m="M_BirdDark")
        tip = box("tip", (0.12, 0.09, 0.012), sh + Vector((0.68 * side, -0.12, 0)), rot=(0, 0, math.radians(-20 * side)), m="M_Iron")
        w = join([inner, outer, tip], nm)
        set_origin(w, sh)
        shade(w, 5)
        parent_to(w, body)
    export([body], "SeaBird.fbx")
    return [body]


# ================================================================== the dock
def build_dock_section(name, seed, du=0.0):
    """A 4 x 4 m deck module, top at z = 0: one slab whose texture carries the planks (separate plank boxes crack
    open under the PSX vertex snap), three joists, a pile at each corner driven 6 m down into the lagoon bed.
    Neighbouring slabs share their edge positions exactly, so they snap together."""
    rnd = random.Random(seed)
    random.seed(seed)
    slab = box("deck", (4.0, 4.0, 0.08), (0, 0, -0.04), m="M_DockPlanks")
    parts = [project(slab, 0.5, du, 0.0)]
    for x in (-1.75, 0.0, 1.75):
        parts.append(project(box(f"j{x}", (0.14, 4.0, 0.22), (x, 0, -0.17), m="M_DockPile"), 1.0))
    for (x, y) in ((-1.8, -1.8), (1.8, -1.8), (-1.8, 1.8), (1.8, 1.8)):
        parts.append(tube(f"pile{x}{y}", [(x, y, -6.0), (x, y, -2.0), (x, y, -0.06)], [0.15, 0.14, 0.13], 6, "M_DockPile", seed=int(x * 10 + y), jag=0.08, uv_len=1.5))
    o = finish(join(parts, name), project=False, smooth_angle=10)
    export([o], f"{name}.fbx")
    return [o]


def build_dock_hole(name, seed, hole, du=0.0):
    """A deck module with a rotten hole broken through it (hole = x0, x1, y0, y1, edges on board seams in y).
    The slab is one mesh with the hole left out (shared verts: the PSX snap can't open cracks), splintered board
    stubs stick into the gap, one broken board hangs down toward the water, and the joist under the hole is gone.
    Empties "HoleCorner0" / "HoleCorner1" mark the opening for the Unity hazard and colliders."""
    rnd = random.Random(seed)
    random.seed(seed)
    x0, x1, y0, y1 = hole
    xs, ys = [-2.0, x0, x1, 2.0], [-2.0, y0, y1, 2.0]
    me = bpy.data.meshes.new(name + "_slab")
    bm = bmesh.new()
    top = [[bm.verts.new((x, y, 0.0)) for x in xs] for y in ys]
    bot = [[bm.verts.new((x, y, -0.08)) for x in xs] for y in ys]
    for j in range(3):
        for i in range(3):
            if i == 1 and j == 1:
                continue
            bm.faces.new([top[j][i], top[j][i + 1], top[j + 1][i + 1], top[j + 1][i]])
            bm.faces.new([bot[j + 1][i], bot[j + 1][i + 1], bot[j][i + 1], bot[j][i]])
    def wall(a, b, c, d):
        bm.faces.new([a, b, c, d])
    for i in range(3):          # outer rim, south (y=-2) and north (y=2)
        wall(top[0][i + 1], top[0][i], bot[0][i], bot[0][i + 1])
        wall(top[3][i], top[3][i + 1], bot[3][i + 1], bot[3][i])
    for j in range(3):          # outer rim, west and east
        wall(top[j][0], top[j + 1][0], bot[j + 1][0], bot[j][0])
        wall(top[j + 1][3], top[j][3], bot[j][3], bot[j + 1][3])
    # the hole's own walls, facing into the gap
    wall(top[1][1], top[1][2], bot[1][2], bot[1][1])
    wall(top[2][2], top[2][1], bot[2][1], bot[2][2])
    wall(top[2][1], top[1][1], bot[1][1], bot[2][1])
    wall(top[1][2], top[2][2], bot[2][2], bot[1][2])
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    slab = new_obj(name + "_slab", me)
    slab.data.materials.append(MATS["M_DockPlanks"])
    parts = [project(slab, 0.5, du, 0.0)]
    # splintered board ends: every cut board pokes a ragged end into the gap from the west and east edges,
    # split into two slivers of different lengths (a sawtooth outline instead of a clean rectangle)
    rows = int(round((y1 - y0) / 0.4))
    for k in range(rows):
        yc = y0 + 0.2 + k * 0.4
        for side, xe in ((1, x0), (-1, x1)):
            for half in (-1, 1):
                ln = rnd.uniform(0.05, 0.32)
                droop = rnd.uniform(0.04, 0.3)
                st = box(f"st{k}{side}{half}", (ln, 0.18, 0.05), (xe + side * ln * 0.5, yc + half * 0.095, -0.028 - droop * ln * 0.5),
                         rot=(0, side * droop, rnd.uniform(-0.1, 0.1)), m="M_DockPlanks" if rnd.random() < 0.7 else "M_DarkWood")
                parts.append(project(st, 0.5, rnd.random(), 0.0))
                if rnd.random() < 0.6:
                    sp = cyl(f"sp{k}{side}{half}", 0.04, 0.2, (xe + side * (ln + 0.07), yc + half * 0.095, -0.035 - droop * ln),
                             rot=(0, math.radians(90) * side, 0), m="M_DockPlanks", verts=3, r2=0.0)
                    parts.append(project(sp, 0.5, rnd.random(), 0.0))
    # slivers left along the long edges where the neighbouring boards tore away
    for side, ye in ((1, y0), (-1, y1)):
        x = x0 + rnd.uniform(0.05, 0.2)
        while x < x1 - 0.2:
            ln = rnd.uniform(0.18, 0.5)
            ln = min(ln, x1 - x - 0.05)
            w = rnd.uniform(0.05, 0.14)
            sl = box(f"sl{side}{x:.2f}", (ln, w, 0.04), (x + ln * 0.5, ye + side * w * 0.5, -0.03 - rnd.uniform(0.0, 0.05)),
                     rot=(side * rnd.uniform(0.05, 0.35), 0, rnd.uniform(-0.08, 0.08)), m="M_DarkWood" if rnd.random() < 0.5 else "M_DockPlanks")
            parts.append(project(sl, 0.5, rnd.random(), 0.0))
            x += ln + rnd.uniform(0.08, 0.35)
    # one broken board hanging from the south edge down toward the water
    hx = rnd.uniform(x0 + 0.3, x1 - 0.3)
    hang = box("hang", (0.36, 0.9, 0.05), (hx, y0 + 0.3, -0.42), rot=(math.radians(62), 0, rnd.uniform(-0.2, 0.2)), m="M_DarkWood")
    parts.append(project(hang, 0.5, 0.3, 0.1))
    for x in (-1.75, 0.0, 1.75):
        if x0 - 0.1 < x < x1 + 0.1:
            # the joist under the hole rotted through: two stumps either side of the gap
            for (ya, yb) in ((-2.0, y0 - 0.05), (y1 + 0.05, 2.0)):
                if yb - ya > 0.1:
                    parts.append(project(box(f"js{x}{ya}", (0.14, yb - ya, 0.22), (x, (ya + yb) / 2, -0.17), m="M_DockPile"), 1.0))
            continue
        parts.append(project(box(f"j{x}", (0.14, 4.0, 0.22), (x, 0, -0.17), m="M_DockPile"), 1.0))
    for (x, y) in ((-1.8, -1.8), (1.8, -1.8), (-1.8, 1.8), (1.8, 1.8)):
        parts.append(tube(f"pile{x}{y}", [(x, y, -6.0), (x, y, -2.0), (x, y, -0.06)], [0.15, 0.14, 0.13], 6, "M_DockPile", seed=int(x * 10 + y), jag=0.08, uv_len=1.5))
    o = finish(join(parts, name), project=False, smooth_angle=10)
    empty("HoleCorner0", (x0, y0, 0.0), o)
    empty("HoleCorner1", (x1, y1, 0.0), o)
    export([o], f"{name}.fbx")
    return [o]


def build_dock_edge():
    """4 m fascia beam for an outer deck edge (along X, outer face at y = 0)."""
    o = project(box("f", (4.0, 0.08, 0.32), (0, -0.04, -0.13), m="M_DockPile"), 1.0)
    o = finish(join([o], "DockEdge"), project=False, smooth_angle=5)
    export([o], "DockEdge.fbx")
    return [o]


def build_dock_post():
    """A mooring piling rising 1.3 m above the deck (reference 1): tarred below, a cap and a rope wrap."""
    parts = [tube("p", [(0, 0, -6.0), (0, 0, -1.0), (0, 0, 1.25)], [0.19, 0.18, 0.17], 7, "M_DockPile", seed=3, jag=0.06, uv_len=1.5),
             cyl("cap", 0.2, 0.18, (0, 0, 1.33), m="M_Iron", verts=7, r2=0.02)]
    for k in range(3):
        parts.append(cyl(f"rope{k}", 0.2, 0.05, (0, 0, 0.55 + k * 0.055), m="M_Rope", verts=7))
    o = finish(join(parts, "DockPost"), project=False, smooth_angle=30)
    export([o], "DockPost.fbx")
    return [o]


def build_dock_rail():
    """One 4 m bay of railing along +X: a square post at x = 0, a wooden top rail and two sagging ropes (reference 2)."""
    parts = [project(box("post", (0.13, 0.13, 1.08), (0, 0, 0.54), m="M_DockPlanks"), 1.0, 0.3, 0.6),
             project(box("rail", (4.0, 0.16, 0.06), (2.0, 0, 1.11), m="M_DockPlanks"), 1.0, 0.1, 0.2)]
    for z in (0.42, 0.76):
        parts.append(tube(f"rope{z}", [(0.06, 0, z), (1.0, 0, z - 0.06), (2.0, 0, z - 0.08), (3.0, 0, z - 0.06), (3.94, 0, z)],
                          [0.022] * 5, 3, "M_Rope", jag=0.0, uv_len=0.5, cap=False))
    o = finish(join(parts, "DockRail"), project=False, smooth_angle=10)
    export([o], "DockRail.fbx")
    return [o]


def build_mooring():
    """Cast-iron bollard and a coil of rope."""
    parts = [cyl("b0", 0.13, 0.42, (0, 0, 0.21), m="M_Iron", verts=8, r2=0.1),
             cyl("b1", 0.17, 0.08, (0, 0, 0.45), m="M_Iron", verts=8)]
    b = finish(join(parts, "Bollard"), project=False, smooth_angle=40)
    export([b], "Bollard.fbx")
    bpy.ops.mesh.primitive_torus_add(major_segments=9, minor_segments=4, major_radius=0.3, minor_radius=0.06, location=(0, 0, 0.06))
    t1 = bpy.context.active_object
    t1.data.materials.append(MATS["M_Rope"])
    bpy.ops.mesh.primitive_torus_add(major_segments=8, minor_segments=4, major_radius=0.24, minor_radius=0.055, location=(0.02, 0.01, 0.16))
    t2 = bpy.context.active_object
    t2.data.materials.append(MATS["M_Rope"])
    tail = tube("tail", [(0.3, 0, 0.06), (0.6, 0.2, 0.03), (0.9, 0.15, 0.03)], [0.05, 0.05, 0.04], 4, "M_Rope", jag=0.0)
    c = finish(join([t1, t2, tail], "RopeCoil"), uv_scale=4.0, smooth_angle=60)
    export([c], "RopeCoil.fbx")
    return [b, c]


def build_crab_pot():
    """A slatted wooden crab pot with netted sides."""
    parts = []
    W, D, H = 0.8, 0.6, 0.45
    for (x, y) in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        parts.append(box(f"c{x}{y}", (0.05, 0.05, H), (x * W / 2, y * D / 2, H / 2), m="M_DarkWood"))
    for z in (0.02, H - 0.02):
        for y in (-1, 1):
            parts.append(box(f"sx{z}{y}", (W, 0.05, 0.04), (0, y * D / 2, z), m="M_DarkWood"))
        for x in (-1, 1):
            parts.append(box(f"sy{z}{x}", (0.05, D, 0.04), (x * W / 2, 0, z), m="M_DarkWood"))
    for k in range(4):
        parts.append(box(f"top{k}", (W, 0.06, 0.03), (0, -D / 2 + 0.08 + k * 0.15, H), m="M_DockPlanks"))
    parts.append(box("floor", (W, D, 0.02), (0, 0, 0.01), m="M_DarkWood"))
    o = join(parts, "CrabPot")
    o = finish(o, uv_scale=2.0, smooth_angle=5)
    nets = []
    for y in (-1, 1):
        nets.append(card(f"n{y}", [(-W / 2, y * D / 2, 0), (W / 2, y * D / 2, 0), (W / 2, y * D / 2, H), (-W / 2, y * D / 2, H)], Q_NET))
    for x in (-1, 1):
        nets.append(card(f"n{x}x", [(x * W / 2, -D / 2, 0), (x * W / 2, D / 2, 0), (x * W / 2, D / 2, H), (x * W / 2, -D / 2, H)], Q_NET))
    o = join([o] + nets, "CrabPot")
    shade(o, 5)
    export([o], "CrabPot.fbx")
    return [o]


def build_net_rack():
    """Two driftwood poles and a crossbar with fishing nets hung to dry, cork floats along the top."""
    parts = [tube("p0", [(-1.6, 0, -0.3), (-1.62, 0.02, 2.2)], [0.08, 0.06], 5, "M_Driftwood", seed=1, uv_len=1.0),
             tube("p1", [(1.6, 0, -0.3), (1.58, -0.03, 2.25)], [0.08, 0.06], 5, "M_Driftwood", seed=2, uv_len=1.0),
             tube("bar", [(-1.8, 0, 2.05), (0, 0, 2.0), (1.8, 0, 2.08)], [0.05, 0.05, 0.05], 4, "M_Driftwood", seed=3, uv_len=1.0)]
    for k in range(7):
        x = -1.5 + k * 0.5
        parts.append(sphere(f"f{k}", 0.05, (x, 0.03, 1.98), (1, 0.7, 0.7), "M_Coconut", seg=5, rings=3))
    for i, (x0, x1, drop) in enumerate(((-1.55, 0.1, 1.5), (-0.2, 1.55, 1.75))):
        sag = 0.25
        corners = [(x0, 0.02 + i * 0.03, 2.0 - drop), (x1, 0.02 + i * 0.03, 2.0 - drop - 0.1), (x1, 0.02 + i * 0.03, 2.0), (x0, 0.02 + i * 0.03, 2.0)]
        parts.append(card(f"net{i}", corners, Q_NET))
        parts.append(card(f"netb{i}", [(x0 + 0.2, 0.1 + i * 0.03, 2.0 - drop + sag), (x1 - 0.2, 0.12 + i * 0.03, 2.0 - drop + sag - 0.05),
                                       (x1 - 0.1, 0.05 + i * 0.03, 1.9), (x0 + 0.1, 0.05 + i * 0.03, 1.9)], Q_NET))
    o = finish(join(parts, "NetRack"), project=False, smooth_angle=30)
    export([o], "NetRack.fbx")
    return [o]


def build_shack():
    """A fisherman's shack on the dock: plank walls, a sagging lean-to roof, a black doorway and window."""
    parts = []
    W, D, H = 3.2, 2.6, 2.3
    rnd = random.Random(21)
    for side in (-1, 1):
        for k in range(8):   # vertical boards, front and back
            x = -W / 2 + 0.2 + k * 0.4
            h = H + (0.35 if side > 0 else 0) + rnd.uniform(-0.04, 0.04)
            parts.append(project(box(f"b{side}{k}", (0.38, 0.05, h), (x, side * D / 2, h / 2), m="M_DockPlanks"), 1.0, rnd.random(), 0))
    for side in (-1, 1):
        parts.append(project(box(f"w{side}", (0.05, D, H + 0.1), (side * W / 2, 0, (H + 0.1) / 2), m="M_DockPlanks"), 1.0, 0.4, 0.1))
    # roof: two layers of boards sloping front to back, overhanging
    for k in range(9):
        x = -W / 2 - 0.2 + k * 0.45
        parts.append(project(box(f"r{k}", (0.42, D + 0.8, 0.05), (x, 0, H + 0.2), rot=(math.radians(-8), 0, rnd.uniform(-0.03, 0.03)), m="M_DarkWood"), 1.0, rnd.random(), 0))
    parts.append(box("door", (0.85, 0.06, 1.8), (-0.6, -D / 2 - 0.01, 0.9), m="M_Iron"))
    parts.append(box("win", (0.6, 0.06, 0.45), (0.8, -D / 2 - 0.01, 1.45), m="M_Iron"))
    parts.append(box("sill", (0.75, 0.12, 0.05), (0.8, -D / 2 - 0.05, 1.2), m="M_DockPile"))
    o = finish(join(parts, "FishingShack"), project=False, smooth_angle=5)
    export([o], "FishingShack.fbx")
    return [o]


def hull(name, length, beam, depth, sections, m, open_from=None, seed=0):
    """Lofted round-bilged hull along Y (bow at +Y); planked both sides. Sections past open_from are left as bare ribs."""
    rnd = random.Random(seed)
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    prof = [(-1.0, 1.0), (-0.92, 0.45), (-0.6, 0.12), (0.0, 0.0), (0.6, 0.12), (0.92, 0.45), (1.0, 1.0)]
    rows, back = [], []
    for s in range(sections + 1):
        t = s / sections
        y = -length / 2 + t * length
        w = beam / 2 * (math.sin(math.pi * min(1.0, 0.18 + t * 0.92)) ** 0.7)   # pointed bow, broad stern
        w = max(w, 0.08)
        ring, ring2 = [], []
        for (px, pz) in prof:
            j = rnd.uniform(-0.03, 0.03) * depth
            co = (px * w, y, pz * depth + (0 if abs(px) < 0.99 else (t - 0.5) ** 2 * depth * 0.6) + j)
            ring.append(bm.verts.new(co))
            ring2.append(bm.verts.new(co))          # separate verts for the inside skin
        rows.append(ring)
        back.append(ring2)
    for s in range(sections):
        if open_from is not None and s >= open_from:
            continue
        for k in range(len(prof) - 1):
            for flip in (False, True):
                rr = back if flip else rows
                vs = [rr[s][k], rr[s][k + 1], rr[s + 1][k + 1], rr[s + 1][k]]
                f = bm.faces.new(list(reversed(vs)) if flip else vs)
                for loop, (uu, vv) in zip(f.loops, [(0, 0), (0, 1), (1, 1), (1, 0)]):
                    loop[uv].uv = (uu + s * 0.37, vv + k * 0.5)
    coords = [[v.co.copy() for v in r] for r in rows]
    bm.to_mesh(me)
    bm.free()
    rows = coords
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    ribs = []
    if open_from is not None:
        for s in range(open_from, sections + 1):
            pts = list(rows[s])
            ribs.append(tube(f"{name}_rib{s}", pts, [depth * 0.035] * len(pts), 4, m, jag=0.1, cap=False))
    return o, ribs, rows


def build_rowboat():
    """A clinker rowboat moored to the dock: lofted hull, two thwarts, a pair of oars."""
    o, _, _ = hull("hull", 3.6, 1.3, 0.55, 6, "M_DarkWood", seed=4)
    parts = [o, box("t0", (1.05, 0.22, 0.04), (0, -0.4, 0.42), m="M_DockPlanks"), box("t1", (1.0, 0.22, 0.04), (0, 0.6, 0.42), m="M_DockPlanks"),
             box("oar0", (0.06, 2.4, 0.04), (0.3, 0.0, 0.5), rot=(0, 0, math.radians(8)), m="M_Driftwood"),
             box("oar1", (0.06, 2.4, 0.04), (-0.25, -0.1, 0.5), rot=(0, 0, math.radians(-5)), m="M_Driftwood")]
    o = finish(join(parts, "Rowboat"), uv_scale=1.0, smooth_angle=20)
    export([o], "Rowboat.fbx")
    return [o]


def build_pennant():
    """A pole with a long, tattered black pennant (child "Flag", pivot at the pole)."""
    pole = tube("pole", [(0, 0, -0.2), (0, 0, 3.6)], [0.05, 0.035], 5, "M_Driftwood", uv_len=1.0)
    pole = finish(join([pole], "Pennant"), project=False, smooth_angle=40)
    pts, sides, widths = [], [], []
    for i in range(6):
        t = i / 5
        pts.append(Vector((0, t * 1.8, 3.25 - t * 0.35 + 0.08 * math.sin(t * 6))))
        sides.append(Vector((0, 0.15 * math.cos(t * 5), 1)))
        widths.append(0.6 * (1 - t * 0.7))
    flag = strip("Flag", pts, widths, sides, (0, 0, 1, 1), m="M_Sail")
    set_origin(flag, (0, 0, 3.25))
    shade(flag, 60)
    parent_to(flag, pole)
    export([pole], "Pennant.fbx")
    return [pole]


# ================================================================== the beach
def build_rock(name, r, seed, squash=0.6, lump=0.35, spire=False):
    if spire:
        parts = [sphere(f"{name}_b", r, (0, 0, r * 0.6), (1.0, 0.8, 1.1), "M_SeaRock", seg=7, rings=5, lumpy=r * 0.4, seed=seed),
                 sphere(f"{name}_m", r * 0.7, (r * 0.15, 0.1 * r, r * 1.9), (0.9, 0.8, 1.5), "M_SeaRock", seg=6, rings=5, lumpy=r * 0.3, seed=seed + 1),
                 sphere(f"{name}_t", r * 0.42, (-r * 0.1, 0, r * 3.1), (0.8, 0.7, 1.6), "M_SeaRock", seg=6, rings=4, lumpy=r * 0.25, seed=seed + 2),
                 cyl(f"{name}_s", r * 0.22, r * 1.1, (0.05 * r, 0, r * 4.0), m="M_SeaRock", verts=4, r2=0.0)]
        o = join(parts, name)
    else:
        o = sphere(name, r, (0, 0, r * squash * 0.55), (1.0, 0.85, squash), "M_SeaRock", seg=8, rings=5, lumpy=r * lump, seed=seed)
    o = finish(join([o], name), uv_scale=0.8, smooth_angle=25)
    export([o], f"{name}.fbx")
    return [o]


def build_grass(name, seed, n=5, radius=0.5, h=(0.5, 0.95)):
    rnd = random.Random(seed)
    parts = []
    for k in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0, radius)
        parts += clump(f"{name}_{k}", (math.cos(a) * r, math.sin(a) * r, -0.05), rnd.uniform(0.55, 0.9), Q_GRASS, n=2, tilt=0.25,
                       seed=seed * 7 + k, h=rnd.uniform(*h))
    o = join(parts, name)
    shade(o, 5)
    export([o], f"{name}.fbx")
    return [o]


def build_driftwood(name, seed, length=3.2):
    rnd = random.Random(seed)
    pts = [(-length / 2, 0, 0.12), (-length / 6, rnd.uniform(-0.2, 0.2), 0.16), (length / 6, rnd.uniform(-0.25, 0.25), 0.14), (length / 2, rnd.uniform(-0.3, 0.3), 0.08)]
    parts = [tube("log", pts, [0.17, 0.15, 0.12, 0.05], 5, "M_Driftwood", seed=seed, jag=0.18, uv_len=1.4)]
    root = Vector(pts[0])
    for k in range(4):
        a = k * 1.6 + rnd.uniform(-0.3, 0.3)
        tip = root + Vector((-0.35, math.cos(a) * 0.5, 0.1 + math.sin(a) * 0.35))
        parts.append(tube(f"r{k}", [root, tip], [0.08, 0.02], 4, "M_Driftwood", seed=seed + k, uv_len=1.0))
    stub = Vector(pts[2])
    parts.append(tube("stub", [stub, stub + Vector((0.3, 0.45, 0.35))], [0.06, 0.02], 4, "M_Driftwood", seed=seed + 9, uv_len=1.0))
    o = finish(join(parts, name), project=False, smooth_angle=30)
    export([o], f"{name}.fbx")
    return [o]


def build_whale():
    """A leviathan's skeleton half-buried in the sand: spine, two rows of ribs arching up and over, a huge skull
    with the jawbones splayed. Bleached bone; the birds circle it."""
    rnd = random.Random(88)
    parts = []
    L = 15.0
    for i in range(12):   # vertebrae, sinking into the sand toward the tail
        y = -L / 2 + i * L / 12
        s = 0.55 * (0.5 + 0.5 * math.sin(math.pi * (0.25 + 0.75 * i / 12)))
        parts.append(cyl(f"v{i}", s * 0.6, s * 0.7, (0, y, 0.15 + s * 0.4 - (0.6 if i < 3 else 0)), rot=(math.radians(90), 0, 0), m="M_Bone", verts=6))
        parts.append(box(f"vs{i}", (0.12, s * 0.4, s * 1.2), (0, y, 0.3 + s * 1.0 - (0.6 if i < 3 else 0)), m="M_Bone"))
    for i in range(8):    # ribs: rising off the spine, arching out, the tips curling back in over the top
        y = -L / 2 + 4.5 + i * 0.95
        size = 1.0 - abs(i - 2.5) * 0.08
        for side in (-1, 1):
            if (i == 6 and side > 0) or (i == 3 and side < 0):
                continue      # broken ones
            lean = rnd.uniform(-0.15, 0.15)
            pts = [(0.3 * side, y, 0.6), (2.0 * side * size, y + lean, 2.0 * size), (2.5 * side * size, y + lean * 2, 3.9 * size),
                   (1.7 * side * size, y + lean * 3, 5.2 * size), (0.6 * side * size, y + lean * 3.5, 5.5 * size - rnd.uniform(0, 0.6))]
            if i == 7:
                pts = pts[:3]
            parts.append(tube(f"rib{i}{side}", pts, [0.17, 0.15, 0.13, 0.1, 0.05][:len(pts)], 5, "M_Bone", seed=i * 3 + side, jag=0.08, uv_len=1.5))
    skull = sphere("skull", 1.0, (0, L / 2 + 1.0, 0.7), (1.5, 2.6, 0.95), "M_Bone", seg=8, rings=6, lumpy=0.18, seed=3)
    parts.append(skull)
    parts.append(sphere("rost", 0.7, (0, L / 2 + 3.4, 0.4), (0.9, 2.2, 0.55), "M_Bone", seg=7, rings=5, lumpy=0.1, seed=5))
    for side in (-1, 1):
        parts.append(tube(f"jaw{side}", [(1.1 * side, L / 2 + 0.2, 0.2), (2.4 * side, L / 2 + 2.8, 0.25), (2.9 * side, L / 2 + 5.4, 0.15), (2.6 * side, L / 2 + 7.0, 0.05)],
                          [0.28, 0.25, 0.2, 0.1], 5, "M_Bone", seed=40 + side, jag=0.1, uv_len=1.5))
    o = finish(join(parts, "WhaleBones"), uv_scale=1.0, smooth_angle=35)
    export([o], "WhaleBones.fbx")
    return [o]


def build_shipwreck():
    """A broken galleon hull heeled over on the reef: planked fore half, bare ribs aft, a snapped mast trailing a
    tattered black sail. A landscape silhouette."""
    hl, ribs, rows = hull("hull", 20.0, 6.0, 4.0, 9, "M_DarkWood", open_from=6, seed=11)
    parts = [hl] + ribs
    for k in range(3):        # deck beams
        y = -6 + k * 4
        parts.append(box(f"beam{k}", (5.4, 0.25, 0.25), (0, y, 3.6), m="M_DarkWood"))
    parts.append(tube("mast", [(0, -1.0, 1.0), (0.4, -1.3, 7.0), (0.6, -1.5, 10.5)], [0.3, 0.26, 0.2], 6, "M_DarkWood", seed=2, uv_len=2.0))
    parts.append(tube("yard", [(-3.0, -1.4, 9.0), (3.5, -1.5, 9.4)], [0.12, 0.12], 5, "M_DarkWood", seed=3, uv_len=2.0))
    # tattered sail hanging off the yard: a jagged strip, two-sided material
    pts, sides, widths = [], [], []
    for i in range(6):
        t = i / 5
        pts.append(Vector((-2.6 + t * 5.6, -1.6 + 0.4 * math.sin(t * 3), 9.0 - 0.5 * t)))
        sides.append(Vector((0.0, 0.25, 1.0)))
        widths.append(random.Random(i).uniform(2.0, 4.2))
    sail = strip("sail", pts, widths, sides, (0, 0, 1, 1), m="M_Sail")
    for v in sail.data.vertices:                     # hang the whole strip below the yard
        v.co.z -= 1.6
    parts.append(sail)
    o = join(parts, "Shipwreck")
    o.rotation_euler = (0, math.radians(-58), math.radians(12))   # heeled over onto its side
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    o = finish(o, project=False, smooth_angle=20)
    export([o], "Shipwreck.fbx")
    return [o]


# ------------------------------------------------------------------ build all
groups.append(build_palm("Palm_A", 9.5, 0.28, 101, fronds=12, dead=3, nuts=5))
groups.append(build_palm("Palm_B", 7.5, 0.3, 202, fronds=10, dead=4, nuts=4, lean_yaw=0.6))
groups.append(build_palm("Palm_C", 5.2, 0.18, 303, fronds=9, dead=2, nuts=3, lean_yaw=2.0))
groups.append(build_palm("Palm_D", 11.0, 0.24, 404, fronds=11, dead=5, nuts=2, lean_yaw=4.0))
groups.append(build_coconuts())
groups.append(build_crab())
groups.append(build_bird())
groups.append(build_dock_section("DockSection", 7))
groups.append(build_dock_section("DockSection_B", 19, du=0.37))
groups.append(build_dock_hole("DockSection_HoleA", 31, (-1.1, 0.9, -0.8, 0.4), du=0.21))
groups.append(build_dock_hole("DockSection_HoleB", 43, (0.0, 1.4, -1.2, 0.8), du=0.63))
groups.append(build_dock_edge())
groups.append(build_dock_post())
groups.append(build_dock_rail())
groups.append(build_mooring())
groups.append(build_crab_pot())
groups.append(build_net_rack())
groups.append(build_shack())
groups.append(build_rowboat())
groups.append(build_pennant())
groups.append(build_rock("Rock_A", 1.0, 1))
groups.append(build_rock("Rock_B", 1.6, 2, squash=0.5, lump=0.45))
groups.append(build_rock("Rock_C", 0.6, 3, squash=0.8))
groups.append(build_rock("ReefSpire", 1.4, 4, spire=True))
groups.append(build_grass("DuneGrass", 31))
groups.append(build_grass("DuneGrass_B", 47, n=8, radius=0.9, h=(0.7, 1.25)))
groups.append(build_driftwood("Driftwood", 61))
groups.append(build_driftwood("Driftwood_B", 73, length=2.2))
groups.append(build_whale())
groups.append(build_shipwreck())
print("BEACH DONE")

if PREVIEW:
    roots = [g[0] for g in groups for _ in [0]]
    roots = [o for g in groups for o in g]
    cols = 8
    for i, o in enumerate(roots):
        big = o.name in ("WhaleBones", "Shipwreck")
        o.location = ((i % cols) * 5.5 - 20, (i // cols) * 6.5 + (14 if big else 0), 0)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    cam.location = (0, -34, 16)
    cam.rotation_euler = (Vector((0, 8, 2)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = 26
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 1800, 1000
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", PREVIEW)
