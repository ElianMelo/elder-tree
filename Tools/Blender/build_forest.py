"""
Elder Tree - Druid arena kit: "Verdant Cathedral", an Amazonian forest clearing in the early morning
(Blender 4.2+/5.x, headless).

    blender -b --factory-startup --python build_forest.py -- <abs out_dir> [preview_prefix]

PSX rules (Docs/ART_DIRECTION.md): no bevels, 3-9 sided tubes, faceted shading, props 50-800 tris; the giant
kapoks are landscape pieces (~2-4k tris). All surface detail comes from the point-filtered, dithered Unity
textures (ElderTreeBuilder.Forest BuildForestArt).

Each prop is its own FBX with the origin at its base. Animated parts are child objects with their pivot at the joint:
  Kapok_*, Emergent_*   child "Crown" (limbs, leaf pads, lianas), pivot at the crown base   -> PalmSway (slow)
  AcaiPalm_*, TreeFern_* child "Crown", pivot at the trunk top                               -> PalmSway
  Frog_*                children "LegL"/"LegR" (pivot at the hip), "Throat", empty "Front"   -> Frog
  Toucan                children "WingL"/"WingR" (folded, pivot at the shoulder), empty "Beak" -> ForestBird
  Vulture               children "WingL"/"WingR" (spread), empty "Beak"                       -> SeaBird
  Perch0..n (empties)   spots on branches where a perched bird stands (Kapok_Mother, MidTree_*)

Material slot names match the Unity materials:
  M_KapokBark     pale grey kapok / emergent bark, lichen, moss streaks
  M_JungleBark    dark brown bark (mid trees, palms, tree ferns, logs)
  M_JungleLeaves  cutout atlas 2x2: canopy leaves (TL), shadowed canopy leaves (TR), broad understory leaves (BL), liana (BR)
  M_JungleFronds  cutout atlas 2x2: fern frond (TL), acai palm frond (TR), floor tuft (BL), hanging roots (BR)
  M_ForestMoss    moss (tops of rocks, logs, stones)     M_ForestStone  grey stone with carved druid spirals
  M_Fungus        pale shelf fungus                      M_FrogSkin / M_FrogDart / M_FrogBelly
  M_ToucanBeak, M_ToucanChest, M_BirdDark, M_BirdPale, M_Iron (existing)
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(os.path.dirname(bpy.data.filepath or "."), "forest")
PREVIEW = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
MATS = {}


def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    MATS[name] = m


for n, c in [("M_KapokBark", (0.52, 0.5, 0.44)), ("M_JungleBark", (0.24, 0.19, 0.14)), ("M_JungleLeaves", (0.2, 0.36, 0.16)),
             ("M_JungleFronds", (0.24, 0.4, 0.18)), ("M_ForestMoss", (0.22, 0.32, 0.12)), ("M_ForestStone", (0.36, 0.37, 0.35)),
             ("M_Fungus", (0.62, 0.55, 0.42)), ("M_FrogSkin", (0.22, 0.36, 0.14)), ("M_FrogDart", (0.08, 0.16, 0.3)),
             ("M_FrogBelly", (0.66, 0.6, 0.42)), ("M_ToucanBeak", (0.72, 0.42, 0.1)), ("M_ToucanChest", (0.78, 0.7, 0.4)),
             ("M_BirdDark", (0.08, 0.08, 0.09)), ("M_BirdPale", (0.6, 0.62, 0.62)), ("M_Iron", (0.1, 0.1, 0.11))]:
    mat(n, c)

# atlas quadrants (u0, v0, u1, v1)
Q_CANOPY = (0.0, 0.5, 0.5, 1.0)
Q_CANOPY_DK = (0.5, 0.5, 1.0, 1.0)
Q_BROAD = (0.0, 0.0, 0.5, 0.5)
Q_LIANA = (0.5, 0.0, 1.0, 0.5)
Q_FERN = (0.0, 0.5, 0.5, 1.0)
Q_PALM = (0.5, 0.5, 1.0, 1.0)
Q_TUFT = (0.0, 0.0, 0.5, 0.5)
Q_ROOTS = (0.5, 0.0, 1.0, 0.5)
LEAVES = "M_JungleLeaves"
FRONDS = "M_JungleFronds"


# ------------------------------------------------------------------ primitives
def new_obj(name, me):
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return o


def activate(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def box(name, size, loc=(0, 0, 0), rot=(0, 0, 0), m="M_ForestStone", jitter=0.0):
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


def cyl(name, r, depth, loc=(0, 0, 0), rot=(0, 0, 0), m="M_JungleBark", verts=8, r2=None, cap=True):
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


def sphere(name, r, loc, scale=(1, 1, 1), m="M_ForestStone", seg=8, rings=5, lumpy=0.0, seed=0):
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


def tube(name, pts, radii, sides, m, seed=0, jag=0.15, flute=0.0, flute_n=5, uv_len=1.4, cap=True):
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
        bm.faces.new(rings[-1])
    bm.to_mesh(me)
    bm.free()
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    return o


def card(name, corners, q, m=LEAVES):
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


def strip(name, pts, widths, sides, q, m=FRONDS):
    """A bending card strip (frond, liana): cross-sections at pts, `sides` = across vectors. u across, v along.
    Faces are turned up so fronds seen from below still catch the sky (the material is two-sided)."""
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
        if f.normal.z < 0:
            f.normal_flip()
        ta, tb = acc[i] / acc[-1], acc[i + 1] / acc[-1]
        va, vb = v0 + dv + (v1 - v0 - 2 * dv) * ta, v0 + dv + (v1 - v0 - 2 * dv) * tb
        loops = list(f.loops)
        # keep u across / v along whichever way the face was flipped
        for loop in loops:
            co = loop.vert.co
            along = vb if (co - pts[i + 1]).length < (co - pts[i]).length else va
            across = (u1 - du) if loop.vert in (rows[i][1], rows[i + 1][1]) else (u0 + du)
            loop[uv].uv = (across, along)
    bm.to_mesh(me)
    bm.free()
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    return o


def hang(name, top, width, length, yaw, sway=0.2, q=Q_LIANA, m=LEAVES, segs=3):
    """A vertical strip hanging from `top` (liana, roots), drifting out a little toward the bottom."""
    t = Vector(top)
    d = Vector((math.cos(yaw), math.sin(yaw), 0))
    out = Vector((-math.sin(yaw), math.cos(yaw), 0))
    pts, sides, widths = [], [], []
    for i in range(segs + 1):
        f = i / segs
        pts.append(t - Vector((0, 0, length * f)) + out * sway * f * f)
        sides.append(d)
        widths.append(width * (1.0 - 0.25 * f))
    # strip() faces up; a hanging strip is vertical, so build it as cards between rows instead
    parts = []
    for i in range(segs):
        f0, f1 = i / segs, (i + 1) / segs
        a, b = pts[i], pts[i + 1]
        wa, wb = d * widths[i] * 0.5, d * widths[i + 1] * 0.5
        u0, v0, u1, v1 = q
        sub = (u0, v1 - (v1 - v0) * f1, u1, v1 - (v1 - v0) * f0)    # top of the strip = top of the atlas cell
        parts.append(card(f"{name}{i}", [b - wb, b + wb, a + wa, a - wa], sub, m))
    return parts


def pad(name, c, size, seed, m=LEAVES, dark_under=True):
    """A canopy leaf pad: stacked near-horizontal cards (shadowed leaves under, lit leaves over) and two
    upright crossed cards so the edge still reads from the side."""
    rnd = random.Random(seed)
    c = Vector(c)
    parts = []
    layers = [(Q_CANOPY_DK, -size * 0.12), (Q_CANOPY, 0.0), (Q_CANOPY, size * 0.1)] if dark_under else [(Q_CANOPY, 0.0), (Q_CANOPY, size * 0.1)]
    for k, (q, dz) in enumerate(layers):
        a = rnd.uniform(0, math.pi)
        s = size * (1.0 - 0.14 * k) * rnd.uniform(0.85, 1.1)
        d1 = Vector((math.cos(a), math.sin(a), rnd.uniform(-0.18, 0.18))) * s * 0.5
        d2 = Vector((-math.sin(a), math.cos(a), rnd.uniform(-0.18, 0.18))) * s * 0.5
        cc = c + Vector((rnd.uniform(-0.15, 0.15) * size, rnd.uniform(-0.15, 0.15) * size, dz))
        parts.append(card(f"{name}h{k}", [cc - d1 - d2, cc + d1 - d2, cc + d1 + d2, cc - d1 + d2], q, m))
    for k in range(2):
        a = rnd.uniform(0, math.pi) + k * math.pi / 2
        d = Vector((math.cos(a), math.sin(a), 0)) * size * 0.45
        up = Vector((rnd.uniform(-0.1, 0.1), rnd.uniform(-0.1, 0.1), 1)).normalized() * size * 0.24
        parts.append(card(f"{name}v{k}", [c - d - up, c + d - up, c + d + up, c - d + up], Q_CANOPY if k else Q_CANOPY_DK, m))
    return parts


def clump(name, center, size, q=Q_BROAD, m=LEAVES, n=3, tilt=0.3, seed=0, h=None):
    """n crossed upright cards around a vertical axis, standing on `center` (bushes, tufts)."""
    rnd = random.Random(seed)
    c = Vector(center)
    h = h if h is not None else size * 0.85
    parts = []
    for k in range(n):
        a = k * math.pi / n + rnd.uniform(-0.25, 0.25)
        d = Vector((math.cos(a), math.sin(a), 0)) * size * 0.5
        lean = Vector((rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt), 0)) * h
        top = Vector((0, 0, h)) + lean
        parts.append(card(f"{name}{k}", [c - d, c + d, c + d + top, c - d + top], q, m))
    return parts


def frond(name, base, yaw, elev, length, droop, width, q, seed, segs=5, m=FRONDS):
    """A frond: a strip arching out of `base` and drooping toward the tip."""
    rnd = random.Random(seed)
    d = Vector((math.cos(yaw), math.sin(yaw), 0))
    side = Vector((-math.sin(yaw), math.cos(yaw), 0))
    pts, sides, widths = [], [], []
    p = Vector(base)
    e = elev
    for i in range(segs + 1):
        t = i / segs
        pts.append(p.copy())
        sides.append(side + Vector((0, 0, rnd.uniform(-0.15, 0.15))))
        widths.append(width * (0.3 + 2.4 * t * (1 - t)) if t < 0.95 else width * 0.12)
        p = p + (d * math.cos(e) + Vector((0, 0, math.sin(e)))) * (length / segs)
        e -= droop / segs
    return strip(name, pts, widths, sides, q, m)


def fin(name, yaw, r0, length, h, thick, seed, m="M_KapokBark"):
    """A kapok buttress: a thin, wavy plank of wood running from high on the trunk down into the ground."""
    d = Vector((math.cos(yaw), math.sin(yaw), 0))
    s = Vector((-math.sin(yaw), math.cos(yaw), 0))
    N = 7
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    ta, tb, ba, bb = [], [], [], []
    for i in range(N + 1):
        t = i / N
        r = r0 * 0.55 + (length + r0 * 0.45) * t
        z = h * (1 - t) ** 1.9 + 0.12
        wob = math.sin(t * 3.2 + seed) * 0.4 * t
        w = thick * (1 - 0.55 * t)
        c = d * r + s * wob
        ta.append(bm.verts.new(c + s * w / 2 + Vector((0, 0, z))))
        tb.append(bm.verts.new(c - s * w / 2 + Vector((0, 0, z))))
        ba.append(bm.verts.new(c + s * w / 2 + Vector((0, 0, -0.6))))
        bb.append(bm.verts.new(c - s * w / 2 + Vector((0, 0, -0.6))))
    for i in range(N):
        bm.faces.new([ba[i], ba[i + 1], ta[i + 1], ta[i]])
        bm.faces.new([bb[i + 1], bb[i], tb[i], tb[i + 1]])
        bm.faces.new([ta[i], ta[i + 1], tb[i + 1], tb[i]])
    bm.faces.new([ba[N], bb[N], tb[N], ta[N]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    return project(o, 0.7)


def join(objs, name):
    activate(objs[0])
    for o in objs:
        o.select_set(True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return o


def shade(o, angle=30):
    activate(o)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))
    except Exception:
        bpy.ops.object.shade_smooth()


def finish(o, smooth_angle=30, uv_scale=1.0, project_uv=True, origin=(0, 0, 0)):
    set_origin(o, origin)
    activate(o)
    if project_uv:
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


def mossy_tops(o, moss="M_ForestMoss", up=0.55):
    """Faces looking up get the moss material (rocks, logs, stones)."""
    if moss not in [s.material.name for s in o.material_slots if s.material]:
        o.data.materials.append(MATS[moss])
    idx = [s.material.name if s.material else "" for s in o.material_slots].index(moss)
    for p in o.data.polygons:
        if p.normal.z > up + noise.noise(p.center * 0.9) * 0.25:
            p.material_index = idx
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


def lerp_path(pts, t):
    """Point at t (0..1) along a polyline."""
    pts = [Vector(p) for p in pts]
    seg = min(int(t * (len(pts) - 1)), len(pts) - 2)
    f = t * (len(pts) - 1) - seg
    return pts[seg].lerp(pts[seg + 1], f)


groups = []


# ================================================================== giant trees
def limb(name, base, d, L, rise, r0, m, seed, sides=6):
    """A limb out of `base` along d, climbing then levelling: returns (tube, polyline)."""
    rnd = random.Random(seed)
    pts = [Vector(base)]
    for k, (f, up) in enumerate([(0.3, 0.42), (0.62, 0.72), (1.0, 0.82)]):
        side = Vector((-d.y, d.x, 0)) * rnd.uniform(-0.08, 0.08) * L
        pts.append(Vector(base) + d * L * f + Vector((0, 0, rise * up)) + side)
    radii = [r0, r0 * 0.68, r0 * 0.42, r0 * 0.14]
    return tube(name, pts, radii, sides, m, seed=seed, jag=0.1, uv_len=1.4), pts


def build_kapok(name, height, base_r, seed, fins=6, fin_h=5.0, fin_len=4.5, crown_r=12.0, limbs=6, low=(), lianas=8,
                perches=False, m="M_KapokBark"):
    """Samauma (kapok): a huge straight grey trunk standing on plank buttresses (reference 4), bare until the
    umbrella crown, whose thick limbs fan out level and carry flat layered leaf pads (references 1, 2, 5). Lianas
    and aerial roots hang from the limbs; epiphytes cling to the trunk. `low` adds limbs lower down
    [(yaw, z, length)], reaching out over the clearing, with perches for the birds."""
    rnd = random.Random(seed)
    random.seed(seed)
    zc = height * 0.74                       # crown base
    lean = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * height * 0.012
    zs = [-0.6, 0.8, 2.4, fin_h * 0.9, height * 0.3, height * 0.5, zc * 0.85, zc, zc + 2.0]
    trunk_at = lambda z: lean * (max(0.0, z) / height) ** 1.5 + Vector((0, 0, z))
    pts = [trunk_at(z) for z in zs]
    radii = [base_r * 1.35, base_r * 1.15, base_r * 1.0, base_r * 0.9, base_r * 0.78, base_r * 0.68, base_r * 0.58, base_r * 0.52, base_r * 0.38]
    parts = [tube(f"{name}_trunk", pts, radii, 9, m, seed=seed, jag=0.06, flute=0.35, flute_n=fins, uv_len=1.4)]
    for k in range(fins):
        a = k * 2 * math.pi / fins + rnd.uniform(-0.25, 0.25)
        parts.append(fin(f"{name}_fin{k}", a, base_r, fin_len * rnd.uniform(0.75, 1.2), fin_h * rnd.uniform(0.75, 1.1),
                         base_r * 0.16 + 0.12, seed * 3 + k, m))
    # epiphytes and aerial roots on the trunk
    for k in range(int(height / 5)):
        z = rnd.uniform(fin_h, zc - 1)
        a = rnd.uniform(0, 2 * math.pi)
        rr = base_r * (1.0 - 0.45 * z / height)
        p = trunk_at(z) + Vector((math.cos(a), math.sin(a), 0)) * rr * 0.95
        parts += clump(f"{name}_epi{k}", p, rnd.uniform(0.9, 1.6), Q_BROAD, n=2, tilt=0.5, seed=seed * 5 + k)
        if rnd.random() < 0.5:
            parts += hang(f"{name}_ar{k}", p + Vector((0, 0, 0.4)), rnd.uniform(0.5, 0.8), rnd.uniform(2.0, 4.5),
                          a + math.pi / 2, 0.1, Q_ROOTS, FRONDS, segs=2)
    # low limbs (part of the trunk mesh: they stay still) with leaf pads, lianas and perches
    perch_pts = []
    for k, (yaw, z, L) in enumerate(low):
        d = Vector((math.cos(yaw), math.sin(yaw), 0))
        base = trunk_at(z) + d * base_r * 0.5
        lt, lp = limb(f"{name}_low{k}", base, d, L, L * 0.18, base_r * 0.28, m, seed + 70 + k)
        # sag the tip down a little: low limbs droop
        parts.append(lt)
        for j in range(2):
            p = lerp_path(lp, rnd.uniform(0.35, 0.65))
            d2 = Vector((math.cos(yaw + (0.9 if j else -0.9)), math.sin(yaw + (0.9 if j else -0.9)), 0))
            sb, sp = limb(f"{name}_lowb{k}{j}", p, d2, L * 0.35, L * 0.12, base_r * 0.1, m, seed + 90 + k * 2 + j, sides=5)
            parts.append(sb)
            parts += pad(f"{name}_lowsp{k}{j}", sp[-1] + Vector((0, 0, 0.6)), rnd.uniform(3.2, 4.4), seed * 11 + k * 3 + j)
            perch_pts.append(lerp_path(sp, 0.5) + Vector((0, 0, base_r * 0.08)))
        for j in range(3):
            parts += pad(f"{name}_lowp{k}{j}", lerp_path(lp, 0.55 + j * 0.22) + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0.8)),
                         rnd.uniform(3.6, 5.0), seed * 13 + k * 5 + j)
        for f in (0.3, 0.55, 0.72, 0.88):
            perch_pts.append(lerp_path(lp, f) + Vector((0, 0, base_r * 0.28 * (1 - f * 0.6))))
        for j in range(6):
            p = lerp_path(lp, rnd.uniform(0.25, 1.0))
            parts += hang(f"{name}_lowli{k}{j}", p, rnd.uniform(0.5, 0.9), rnd.uniform(4.0, z - 1.5), yaw + math.pi / 2 + rnd.uniform(-0.5, 0.5),
                          rnd.uniform(0.1, 0.6), segs=3)
    trunk = join(parts, name)
    trunk = finish(trunk, project_uv=False, smooth_angle=35)

    # the crown: limbs fanning out level, sub-branches, layered pads, lianas
    cparts = []
    top = trunk_at(zc)
    for k in range(limbs):
        a = k * 2 * math.pi / limbs + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = crown_r * rnd.uniform(0.7, 1.0)
        lt, lp = limb(f"{name}_l{k}", top + Vector((0, 0, rnd.uniform(-1, 1.5))), d, L, L * rnd.uniform(0.35, 0.55), base_r * 0.36, m, seed + k)
        cparts.append(lt)
        for j in range(2):
            p = lerp_path(lp, rnd.uniform(0.4, 0.7))
            ya = a + (0.8 if j else -0.8) + rnd.uniform(-0.2, 0.2)
            d2 = Vector((math.cos(ya), math.sin(ya), 0))
            sb, sp = limb(f"{name}_b{k}{j}", p, d2, L * 0.45, L * 0.2, base_r * 0.14, m, seed + 30 + k * 2 + j, sides=5)
            cparts.append(sb)
            cparts += pad(f"{name}_sp{k}{j}", sp[-1] + Vector((0, 0, 0.8)), rnd.uniform(5.0, 7.0), seed * 7 + k * 3 + j)
        for j in range(2):
            cparts += pad(f"{name}_p{k}{j}", lerp_path(lp, 0.75 + j * 0.25) + Vector((0, 0, 1.0 + j * 0.6)), rnd.uniform(5.5, 7.5), seed * 9 + k * 5 + j)
        for j in range(int(lianas / limbs + rnd.random())):
            p = lerp_path(lp, rnd.uniform(0.3, 0.95))
            cparts += hang(f"{name}_li{k}{j}", p, rnd.uniform(0.5, 0.9), rnd.uniform(6.0, min(zc - 2, 22.0)),
                           a + math.pi / 2 + rnd.uniform(-0.5, 0.5), rnd.uniform(0.2, 1.2), segs=4)
        if rnd.random() < 0.6:
            p = lerp_path(lp, rnd.uniform(0.2, 0.5))
            cparts += hang(f"{name}_rt{k}", p, 0.8, rnd.uniform(3, 6), a + math.pi / 2, 0.2, Q_ROOTS, FRONDS, segs=2)
    cparts += pad(f"{name}_top", top + Vector((0, 0, crown_r * 0.45)), crown_r * 0.6, seed + 500)
    crown = join(cparts, "Crown")
    set_origin(crown, top)
    shade(crown, 35)
    parent_to(crown, trunk)
    if perches:
        for i, p in enumerate(perch_pts):
            empty(f"Perch{i}", p, trunk)
    export([trunk], f"{name}.fbx")
    return [trunk]


def build_emergent(name, height, base_r, seed, m="M_KapokBark"):
    """A tall, straight emergent (references 1 and 3): flared foot with root ridges, a clean pale column, then a
    fork into ascending limbs carrying leaf pads above the canopy. Mottled with lichen, strung with vines."""
    rnd = random.Random(seed)
    random.seed(seed)
    zf = height * rnd.uniform(0.62, 0.72)
    zs = [-0.5, 0.6, 1.8, height * 0.25, height * 0.45, zf, zf + 1.5]
    off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * 0.5
    at = lambda z: off * (z / height) ** 2 + Vector((0, 0, z))
    radii = [base_r * 1.9, base_r * 1.3, base_r * 1.05, base_r * 0.9, base_r * 0.78, base_r * 0.6, base_r * 0.5]
    parts = [tube(f"{name}_trunk", [at(z) for z in zs], radii, 8, m, seed=seed, jag=0.08, flute=0.5, flute_n=5, uv_len=1.4)]
    for k in range(5):
        a = k * 2 * math.pi / 5 + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        parts.append(tube(f"{name}_root{k}", [d * base_r * 1.2 + Vector((0, 0, 1.4)), d * base_r * 2.4 + Vector((0, 0, 0.3)),
                                             d * base_r * 3.6 + Vector((0, 0, -0.3))],
                          [base_r * 0.45, base_r * 0.3, base_r * 0.1], 4, m, seed=seed + k, uv_len=1.4))
    for k in range(int(height / 6)):
        z = rnd.uniform(2.5, zf - 1)
        a = rnd.uniform(0, 2 * math.pi)
        p = at(z) + Vector((math.cos(a), math.sin(a), 0)) * base_r * 0.9
        if rnd.random() < 0.55:
            parts += clump(f"{name}_epi{k}", p, rnd.uniform(0.7, 1.3), Q_BROAD, n=2, tilt=0.5, seed=seed * 5 + k)
        else:
            parts += hang(f"{name}_vn{k}", p + Vector((0, 0, rnd.uniform(1, 3))), rnd.uniform(0.4, 0.7), rnd.uniform(2.5, 5.0),
                          a + math.pi / 2, 0.05, segs=2)
    trunk = finish(join(parts, name), project_uv=False, smooth_angle=35)
    cparts = []
    top = at(zf)
    n = rnd.randint(3, 4)
    for k in range(n):
        a = k * 2 * math.pi / n + rnd.uniform(-0.4, 0.4)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = height * rnd.uniform(0.16, 0.24)
        lt, lp = limb(f"{name}_l{k}", top, d, L, L * rnd.uniform(0.8, 1.2), base_r * 0.42, m, seed + k)
        cparts.append(lt)
        for j in range(2):
            cparts += pad(f"{name}_p{k}{j}", lerp_path(lp, 0.7 + j * 0.3) + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0.8)),
                          rnd.uniform(4.0, 5.5), seed * 9 + k * 5 + j)
        if rnd.random() < 0.7:
            p = lerp_path(lp, rnd.uniform(0.3, 0.8))
            cparts += hang(f"{name}_li{k}", p, 0.6, rnd.uniform(5.0, 12.0), a + math.pi / 2, rnd.uniform(0.2, 0.8), segs=3)
    crown = join(cparts, "Crown")
    set_origin(crown, top)
    shade(crown, 35)
    parent_to(crown, trunk)
    export([trunk], f"{name}.fbx")
    return [trunk]


def build_midtree(name, height, seed):
    """Understorey tree (8-18 m): a thin dark trunk leaning for the light, a few branches with leaf pads, lianas,
    and perches on the branches where the toucans sit."""
    rnd = random.Random(seed)
    random.seed(seed)
    lean = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized() * height * rnd.uniform(0.04, 0.1)
    at = lambda z: lean * (max(0.0, z) / height) ** 1.4 + Vector((0, 0, z))
    zs = [-0.3, 1.0, height * 0.35, height * 0.65, height * 0.88, height]
    r = 0.16 + height * 0.012
    parts = [tube(f"{name}_trunk", [at(z) for z in zs], [r * 1.5, r * 1.1, r, r * 0.8, r * 0.55, r * 0.2], 6, "M_JungleBark",
                  seed=seed, jag=0.1, flute=0.3, uv_len=1.4)]
    perch = []
    n = rnd.randint(3, 5)
    for k in range(n):
        z = height * rnd.uniform(0.5, 0.88)
        a = k * 2 * math.pi / n + rnd.uniform(-0.4, 0.4)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = height * rnd.uniform(0.18, 0.3)
        lt, lp = limb(f"{name}_b{k}", at(z), d, L, L * rnd.uniform(0.3, 0.6), r * 0.5, "M_JungleBark", seed + k, sides=5)
        parts.append(lt)
        parts += pad(f"{name}_p{k}", lp[-1] + Vector((0, 0, 0.5)), rnd.uniform(2.6, 3.8), seed * 7 + k)
        if rnd.random() < 0.6:
            parts += pad(f"{name}_q{k}", lerp_path(lp, 0.55) + Vector((0, 0, 0.6)), rnd.uniform(2.0, 3.0), seed * 9 + k)
        if rnd.random() < 0.5:
            parts += hang(f"{name}_li{k}", lerp_path(lp, rnd.uniform(0.4, 0.9)), 0.5, rnd.uniform(2.5, z - 0.5),
                          a + math.pi / 2, rnd.uniform(0.1, 0.4), segs=3)
    # two bare, half-dead lower branches sticking out into the open: where the toucans sit, in plain view
    for k in range(2):
        z = height * rnd.uniform(0.3, 0.45)
        a = rnd.uniform(0, 2 * math.pi)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = rnd.uniform(1.8, 2.6)
        lt, lp = limb(f"{name}_snag{k}", at(z), d, L, L * 0.25, r * 0.4, "M_JungleBark", seed + 40 + k, sides=4)
        parts.append(lt)
        for f in (0.45, 0.78):
            perch.append(lerp_path(lp, f) + Vector((0, 0, r * 0.4 * (1 - f * 0.6))))
        if rnd.random() < 0.6:
            parts += clump(f"{name}_sn{k}", lp[-1], 0.7, Q_BROAD, n=2, tilt=0.4, seed=seed * 3 + k)
    parts += pad(f"{name}_top", at(height) + Vector((0, 0, 0.4)), rnd.uniform(3.0, 4.2), seed + 77)
    o = finish(join(parts, name), project_uv=False, smooth_angle=35)
    for i, p in enumerate(perch):
        empty(f"Perch{i}", p, o)
    export([o], f"{name}.fbx")
    return [o]


def build_acai(name, height, bend, seed, fronds=10):
    """Acai palm: a thin ringed trunk curving up through the canopy (reference 2), a green crownshaft and a crown
    of long fronds whose narrow leaflets hang straight down; a cluster of dark berries under the crown."""
    rnd = random.Random(seed)
    random.seed(seed)
    yaw = rnd.uniform(0, 2 * math.pi)
    dx, dy = math.cos(yaw), math.sin(yaw)
    pts, radii = [], []
    for i in range(10):
        t = i / 9
        o = bend * height * t ** 1.8
        pts.append((dx * o, dy * o, t * height))
        radii.append((0.13 - 0.03 * t) * (1.08 if i % 2 == 0 else 0.95))
    trunk = tube(f"{name}_trunk", pts, radii, 5, "M_JungleBark", seed=seed, jag=0.04, uv_len=0.9)
    top = Vector(pts[-1])
    shaft = tube(f"{name}_shaft", [top, top + Vector((0, 0, 1.3))], [0.12, 0.1], 5, "M_ForestMoss", seed=seed, jag=0.0)
    trunk = finish(join([trunk, shaft], name), project_uv=False, smooth_angle=50)
    cparts = []
    ctop = top + Vector((0, 0, 1.3))
    for k in range(fronds):
        a = k * 2 * math.pi / fronds + rnd.uniform(-0.25, 0.25)
        cparts.append(frond(f"{name}_fr{k}", ctop, a, math.radians(rnd.uniform(30, 60)), rnd.uniform(2.8, 3.6),
                            math.radians(rnd.uniform(80, 120)), rnd.uniform(1.2, 1.6), Q_PALM, seed * 13 + k))
    for k in range(3):
        a = rnd.uniform(0, 2 * math.pi)
        c = top + Vector((math.cos(a) * 0.2, math.sin(a) * 0.2, 0.2))
        cparts.append(rod(f"{name}_bunch{k}", c, c + Vector((math.cos(a) * 0.5, math.sin(a) * 0.5, -0.7)), 0.07, "M_Iron", 4, r2=0.02))
    crown = join(cparts, "Crown")
    set_origin(crown, top)
    shade(crown, 60)
    parent_to(crown, trunk)
    export([trunk], f"{name}.fbx")
    return [trunk]


def build_treefern(name, height, seed, fronds=11):
    """Tree fern (reference 6): a shaggy fibrous trunk and a crown of big lacy fronds arching out, a few dead
    ones hanging down against the trunk."""
    rnd = random.Random(seed)
    random.seed(seed)
    yaw = rnd.uniform(0, 2 * math.pi)
    pts = [(math.cos(yaw) * 0.2 * (i / 5) ** 2, math.sin(yaw) * 0.2 * (i / 5) ** 2, i / 5 * height) for i in range(6)]
    trunk = tube(f"{name}_trunk", pts, [0.24, 0.17, 0.16, 0.15, 0.15, 0.17], 6, "M_JungleBark", seed=seed, jag=0.22, uv_len=0.8)
    parts = [trunk]
    for k in range(4):
        z = rnd.uniform(0.3, height * 0.8)
        a = rnd.uniform(0, 2 * math.pi)
        p = Vector((0, 0, z)) + Vector((math.cos(a), math.sin(a), 0)) * 0.15
        parts += hang(f"{name}_sh{k}", p, 0.35, rnd.uniform(0.5, 1.1), a + math.pi / 2, 0.05, Q_ROOTS, FRONDS, segs=1)
    trunk = finish(join(parts, name), project_uv=False, smooth_angle=40)
    top = Vector(pts[-1])
    cparts = []
    for k in range(fronds):
        a = k * 2 * math.pi / fronds + rnd.uniform(-0.2, 0.2)
        cparts.append(frond(f"{name}_fr{k}", top + Vector((0, 0, 0.1)), a, math.radians(rnd.uniform(25, 55)), rnd.uniform(2.0, 2.7),
                            math.radians(rnd.uniform(60, 95)), rnd.uniform(0.9, 1.15), Q_FERN, seed * 13 + k))
    for k in range(3):
        a = rnd.uniform(0, 2 * math.pi)
        cparts.append(frond(f"{name}_dead{k}", top, a, math.radians(-65), rnd.uniform(1.2, 1.7), math.radians(20), 0.7, Q_ROOTS, seed * 17 + k))
    crown = join(cparts, "Crown")
    set_origin(crown, top)
    shade(crown, 60)
    parent_to(crown, trunk)
    export([trunk], f"{name}.fbx")
    return [trunk]


# ================================================================== understorey
def build_bush(name, seed, n=9, radius=1.4, h=(0.0, 0.8), size=(1.4, 2.4)):
    """Dense understorey: fans of broad leaves standing in crossed groups, a few canopy-leaf clumps between
    them, so the edge of the clearing reads as a wall of growth (reference 6)."""
    rnd = random.Random(seed)
    parts = []
    for k in range(n):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0, radius)
        z = rnd.uniform(*h)
        s = rnd.uniform(*size)
        parts += clump(f"{name}{k}", (math.cos(a) * r, math.sin(a) * r, z - 0.15), s, Q_BROAD, n=3, tilt=0.35, seed=seed * 13 + k)
    for k in range(max(1, n // 3)):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0, radius * 0.7)
        parts += pad(f"{name}c{k}", (math.cos(a) * r, math.sin(a) * r, rnd.uniform(0.8, 1.6) + h[1] * 0.6), rnd.uniform(1.4, 2.0), seed * 31 + k)
    o = join(parts, name)
    shade(o, 5)
    export([o], f"{name}.fbx")
    return [o]


def build_fern(name, seed, n=9, length=(0.9, 1.3)):
    """Ground fern: fronds arching out of a crown at the soil."""
    rnd = random.Random(seed)
    parts = []
    for k in range(n):
        a = k * 2 * math.pi / n + rnd.uniform(-0.3, 0.3)
        parts.append(frond(f"{name}_{k}", (0, 0, 0.05), a, math.radians(rnd.uniform(35, 70)), rnd.uniform(*length),
                           math.radians(rnd.uniform(50, 90)), rnd.uniform(0.45, 0.6), Q_FERN, seed * 7 + k, segs=4))
    o = join(parts, name)
    shade(o, 60)
    export([o], f"{name}.fbx")
    return [o]


def build_tuft(name, seed, n=4, radius=0.35, h=(0.3, 0.6)):
    rnd = random.Random(seed)
    parts = []
    for k in range(n):
        a, r = rnd.uniform(0, 2 * math.pi), rnd.uniform(0, radius)
        parts += clump(f"{name}{k}", (math.cos(a) * r, math.sin(a) * r, -0.04), rnd.uniform(0.45, 0.75), Q_TUFT, FRONDS, n=2,
                       tilt=0.25, seed=seed * 7 + k, h=rnd.uniform(*h))
    o = join(parts, name)
    shade(o, 5)
    export([o], f"{name}.fbx")
    return [o]


def build_canopy_wall(name, seed, width=22.0, height=26.0, depth=7.0):
    """Background forest: a deep band of overlapping leaf cards from the floor to the canopy, with trunks and
    lianas among them, placed in rings far out (40-95 m) so the gaps between the near trees show more forest,
    never sky or empty haze. The cards face Blender -Y (Unity +Z): the builder turns that toward the clearing."""
    rnd = random.Random(seed)
    parts = []
    for k in range(int(width / 5)):
        x = rnd.uniform(-width / 2, width / 2)
        y = rnd.uniform(depth * 0.3, depth)
        h = height * rnd.uniform(0.75, 1.1)
        r = rnd.uniform(0.25, 0.7)
        parts.append(tube(f"{name}_t{k}", [(x, y, -0.5), (x + rnd.uniform(-0.4, 0.4), y, h * 0.5), (x + rnd.uniform(-0.8, 0.8), y, h)],
                          [r * 1.4, r, r * 0.6], 5, "M_KapokBark" if rnd.random() < 0.4 else "M_JungleBark", seed=seed + k, jag=0.1))
    rows = int(height / 3.2)
    for row in range(rows):
        z = row * 3.2 + rnd.uniform(-0.6, 0.6)
        n = int(width / 3.2)
        for i in range(n):
            x = -width / 2 + (i + rnd.uniform(0, 1)) * width / n
            y = rnd.uniform(0, depth)
            sz = rnd.uniform(3.8, 6.0) * (1.15 if row > rows * 0.6 else 1.0)
            yaw = rnd.uniform(-0.6, 0.6)
            d = Vector((math.cos(yaw), math.sin(yaw), 0)) * sz * 0.5
            c = Vector((x, y, max(z, 0.0)))
            up = Vector((rnd.uniform(-0.15, 0.15), 0, 1)).normalized() * sz * 0.82
            if row == 0:
                q, m = Q_BROAD, LEAVES                           # understorey at the foot
            else:
                q, m = (Q_CANOPY if rnd.random() < (0.35 + 0.4 * row / rows) else Q_CANOPY_DK), LEAVES
            # corners bl, br, tr, tl with the normal toward -Y
            parts.append(card(f"{name}_c{row}_{i}", [c - d, c + d, c + d + up, c - d + up], q, m))
    for k in range(int(width / 3)):
        x = rnd.uniform(-width / 2, width / 2)
        top = (x, rnd.uniform(0, depth * 0.5), height * rnd.uniform(0.6, 0.95))
        parts += hang(f"{name}_li{k}", top, rnd.uniform(0.5, 0.9), rnd.uniform(6, top[2] - 1), 0.0, rnd.uniform(0.1, 0.6), segs=3)
    o = join(parts, name)
    # leaf cards: make every one face -Y (lit from the clearing side)
    for p in o.data.polygons:
        if abs(p.normal.z) < 0.5 and p.normal.y > 0.3 and o.material_slots[p.material_index].material.name == LEAVES:
            p.flip()
    o = finish(o, project_uv=False, smooth_angle=5)
    export([o], f"{name}.fbx")
    return [o]


def build_liana_curtain(name, seed, span=7.0, z=13.0, n=9):
    """A curtain of lianas hanging from a thin bough high between the trees."""
    rnd = random.Random(seed)
    pts = [(-span / 2, 0, z + 0.4), (-span / 6, 0.3, z - 0.3), (span / 6, -0.2, z - 0.1), (span / 2, 0, z + 0.6)]
    parts = [tube(f"{name}_bough", pts, [0.14, 0.1, 0.1, 0.14], 5, "M_JungleBark", seed=seed, jag=0.1)]
    for k in range(n):
        p = lerp_path(pts, (k + rnd.uniform(0.1, 0.9)) / n)
        parts += hang(f"{name}_{k}", p, rnd.uniform(0.45, 0.8), rnd.uniform(z * 0.45, z - 0.5), rnd.uniform(-0.3, 0.3),
                      rnd.uniform(0.0, 0.8), segs=4)
    for k in range(3):
        parts += pad(f"{name}_p{k}", lerp_path(pts, 0.15 + k * 0.35) + Vector((0, 0, 0.4)), rnd.uniform(2.0, 3.0), seed * 3 + k)
    o = finish(join(parts, name), project_uv=False, smooth_angle=40)
    export([o], f"{name}.fbx")
    return [o]


def build_log(name, seed, length=8.0, r=0.6):
    """A fallen trunk going back to the forest: moss on top, shelf fungus, ferns and seedlings growing out of it."""
    rnd = random.Random(seed)
    pts = [(-length / 2, 0, r * 0.7), (-length / 6, rnd.uniform(-0.3, 0.3), r * 0.75), (length / 6, rnd.uniform(-0.3, 0.3), r * 0.7),
           (length / 2, rnd.uniform(-0.4, 0.4), r * 0.55)]
    log = tube(f"{name}_log", pts, [r, r * 0.95, r * 0.85, r * 0.7], 7, "M_JungleBark", seed=seed, jag=0.14, uv_len=1.4)
    mossy_tops(log, up=0.45)
    parts = [log]
    root = Vector(pts[0])
    for k in range(5):           # the torn-up root plate
        a = k * 1.25 + rnd.uniform(-0.3, 0.3)
        tip = root + Vector((-rnd.uniform(0.4, 0.9), math.cos(a) * r * 1.8, r * 0.8 + math.sin(a) * r * 1.6))
        parts.append(tube(f"{name}_r{k}", [root, tip], [r * 0.35, r * 0.06], 4, "M_JungleBark", seed=seed + k))
    for k in range(4):
        p = lerp_path(pts, rnd.uniform(0.15, 0.9))
        a = rnd.choice((-1, 1))
        c = p + Vector((0, a * r * 0.9, rnd.uniform(-0.2, 0.25)))
        f = cyl(f"{name}_fg{k}", rnd.uniform(0.18, 0.3), 0.05, c, m="M_Fungus", verts=7)
        bm = bmesh.new(); bm.from_mesh(f.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y * a < -0.02], context='VERTS')
        bm.to_mesh(f.data); bm.free()
        parts.append(f)
    for k in range(2):
        p = lerp_path(pts, rnd.uniform(0.2, 0.8)) + Vector((0, 0, r * 0.8))
        for j in range(5):
            aa = j * 2 * math.pi / 5 + rnd.uniform(-0.3, 0.3)
            parts.append(frond(f"{name}_fe{k}{j}", p, aa, math.radians(rnd.uniform(35, 60)), rnd.uniform(0.7, 1.0),
                               math.radians(60), 0.45, Q_FERN, seed * 5 + k * 7 + j, segs=3))
    o = finish(join(parts, name), project_uv=False, smooth_angle=30)
    export([o], f"{name}.fbx")
    return [o]


def build_surface_root(name, seed, length=7.0, r=0.32):
    """A buttress root snaking over the forest floor, half sunk into the litter, forking once."""
    rnd = random.Random(seed)
    pts = [Vector((0, 0, 0.05))]
    yaw = 0.0
    for i in range(5):
        yaw += rnd.uniform(-0.45, 0.45)
        pts.append(pts[-1] + Vector((math.cos(yaw), math.sin(yaw), 0)) * length / 5 + Vector((0, 0, -0.03)))
    radii = [r * (1 - 0.8 * i / 5) for i in range(6)]
    parts = [tube(f"{name}_m", pts, radii, 5, "M_KapokBark", seed=seed, jag=0.18)]
    p = pts[2]
    y2 = yaw + rnd.choice((-1, 1)) * 0.9
    q = [p, p + Vector((math.cos(y2), math.sin(y2), 0)) * length * 0.3 + Vector((0, 0, -0.05)),
         p + Vector((math.cos(y2), math.sin(y2), 0)) * length * 0.5 + Vector((0, 0, -0.08))]
    parts.append(tube(f"{name}_b", q, [r * 0.55, r * 0.35, r * 0.12], 4, "M_KapokBark", seed=seed + 1, jag=0.15))
    o = join(parts, name)
    for v in o.data.vertices:      # sink it: nothing below the litter
        v.co.z = max(v.co.z, -0.15) - 0.1
    mossy_tops(o, up=0.7)
    o = finish(o, project_uv=False, smooth_angle=30)
    export([o], f"{name}.fbx")
    return [o]


# ================================================================== stones
def build_menhir(name, h, seed, w=0.9):
    """A standing stone of the druid circle: a tapering, faceted slab, moss on its shoulders and a fern at its
    foot. The carved spirals are in the stone texture."""
    rnd = random.Random(seed)
    random.seed(seed)
    zs = [-0.6, h * 0.3, h * 0.65, h * 0.9, h]
    pts = [(rnd.uniform(-0.05, 0.05) * z, rnd.uniform(-0.05, 0.05) * z, z) for z in zs]
    s = tube(f"{name}_s", pts, [w, w * 0.95, w * 0.82, w * 0.6, w * 0.25], 5, "M_ForestStone", seed=seed, jag=0.18, uv_len=1.4)
    for v in s.data.vertices:       # a slab, not a pillar
        v.co.y *= 0.55
    project(s, 0.8)
    mossy_tops(s, up=0.35)
    parts = [s]
    for j in range(4):
        a = j * 2 * math.pi / 4 + rnd.uniform(-0.3, 0.3)
        parts.append(frond(f"{name}_f{j}", (math.cos(a) * w * 0.8, math.sin(a) * w * 0.5, 0.05), a, math.radians(50), 0.9,
                           math.radians(70), 0.45, Q_FERN, seed * 3 + j, segs=3))
    o = finish(join(parts, name), project_uv=False, smooth_angle=20)
    export([o], f"{name}.fbx")
    return [o]


def build_altar():
    """A dolmen at the great kapok's foot: two uprights and a cracked capstone, swallowed by roots and moss."""
    random.seed(9)
    parts = []
    for k, x in enumerate((-1.1, 1.1)):
        b = box(f"up{k}", (0.6, 0.9, 1.3), (x, 0, 0.55), m="M_ForestStone", jitter=0.08)
        project(b, 0.8)
        parts.append(b)
    cap = box("cap", (3.2, 1.6, 0.4), (0.05, 0.05, 1.4), rot=(0, math.radians(3), math.radians(4)), m="M_ForestStone", jitter=0.08)
    project(cap, 0.8)
    mossy_tops(cap, up=0.5)
    parts.append(cap)
    for k in range(4):
        a = k * 1.7
        parts.append(tube(f"rt{k}", [(math.cos(a) * 1.6, math.sin(a) * 1.2, 1.7), (math.cos(a) * 2.0, math.sin(a) * 1.6, 0.9),
                                     (math.cos(a) * 2.7, math.sin(a) * 2.1, -0.1)], [0.16, 0.12, 0.05], 4, "M_KapokBark", seed=k))
    parts += hang("vine", (0.8, 0.82, 1.55), 0.5, 1.4, 0.0, 0.05, segs=2)
    o = finish(join(parts, "Altar"), project_uv=False, smooth_angle=20)
    export([o], "Altar.fbx")
    return [o]


def build_rock(name, r, seed, squash=0.6, lump=0.35):
    o = sphere(name, r, (0, 0, r * squash * 0.45), (1.0, 0.85, squash), "M_ForestStone", seg=8, rings=5, lumpy=r * lump, seed=seed)
    o = finish(join([o], name), uv_scale=0.8, smooth_angle=25)
    mossy_tops(o, up=0.3)
    export([o], f"{name}.fbx")
    return [o]


# ================================================================== critters
def build_frog(name, skin):
    """A big forest frog (~35 cm): squat body, wide head with bulging black eyes, a pale throat sac
    ("Throat"), short front legs, and folded back legs ("LegL"/"LegR", pivot at the hip) that kick out to hop."""
    S = 1.2
    V = lambda x, y, z: Vector((x, y, z)) * S
    body = [sphere("body", 0.12 * S, V(0, -0.02, 0.1), (1.0, 1.3, 0.7), skin, seg=8, rings=5),
            sphere("head", 0.095 * S, V(0, 0.12, 0.12), (1.25, 1.0, 0.7), skin, seg=8, rings=5)]
    for side in (-1, 1):
        body.append(sphere(f"eye{side}", 0.034 * S, V(0.07 * side, 0.15, 0.175), m="M_Iron", seg=6, rings=4))
        body.append(sphere(f"lid{side}", 0.038 * S, V(0.07 * side, 0.145, 0.168), (1, 1, 0.6), skin, seg=6, rings=3))
        body.append(rod(f"arm{side}", V(0.08 * side, 0.1, 0.07), V(0.12 * side, 0.17, 0.0), 0.022 * S, skin, 4, r2=0.016 * S))
        body.append(box(f"hand{side}", (0.06 * S, 0.05 * S, 0.012 * S), V(0.125 * side, 0.19, 0.005), m=skin))
    body.append(sphere("belly", 0.1 * S, V(0, 0.0, 0.065), (1.0, 1.2, 0.45), "M_FrogBelly", seg=6, rings=4))
    o = finish(join(body, name), project_uv=True, uv_scale=6.0, smooth_angle=45)
    empty("Front", V(0, 0.45, 0.1), o)
    throat = sphere("Throat", 0.05 * S, V(0, 0.17, 0.065), (1.2, 0.9, 0.6), "M_FrogBelly", seg=6, rings=4)
    set_origin(throat, V(0, 0.17, 0.08))
    shade(throat, 60)
    parent_to(throat, o)
    for side, nm in ((-1, "LegL"), (1, "LegR")):
        hip = V(0.08 * side, -0.09, 0.09)
        knee = V(0.18 * side, 0.02, 0.06)
        ankle = V(0.14 * side, -0.13, 0.025)
        toe = V(0.17 * side, -0.01, 0.0)
        leg = join([rod("th", hip, knee, 0.04 * S, skin, 5, r2=0.03 * S), rod("sh", knee, ankle, 0.028 * S, skin, 4, r2=0.02 * S),
                    rod("ft", ankle, toe, 0.02 * S, skin, 4, r2=0.01 * S), box("web", (0.06 * S, 0.07 * S, 0.01 * S), toe + V(0, 0.02, 0), m=skin)], nm)
        project(leg, 6.0)
        set_origin(leg, hip)
        shade(leg, 40)
        parent_to(leg, o)
    export([o], f"{name}.fbx")
    return [o]


def build_toucan():
    """A toucan perched upright (~55 cm with the bill): black body, pale-gold bib, huge dull-orange bill, long
    tail hanging below the perch. Wings lie folded along the sides ("WingL"/"WingR", pivot at the shoulder);
    ForestBird swings them out to fly. The origin is at the feet, on the branch."""
    parts = [tube("body", [(0, -0.14, 0.1), (0, -0.06, 0.2), (0, 0.0, 0.3), (0, 0.03, 0.37)], [0.04, 0.075, 0.075, 0.055], 6, "M_BirdDark", jag=0.0),
             sphere("head", 0.06, (0, 0.04, 0.41), (0.9, 1.1, 1.0), "M_BirdDark", seg=7, rings=5),
             sphere("bib", 0.052, (0, 0.06, 0.33), (0.9, 0.55, 1.2), "M_ToucanChest", seg=6, rings=4),
             tube("bill", [(0, 0.08, 0.42), (0, 0.18, 0.415), (0, 0.29, 0.385)], [0.034, 0.028, 0.008], 4, "M_ToucanBeak", jag=0.0),
             rod("billtip", (0, 0.26, 0.39), (0, 0.3, 0.38), 0.01, "M_Iron", 3, r2=0.002),
             sphere("eye", 0.018, (0, 0.075, 0.43), (2.6, 1, 1), "M_ToucanChest", seg=5, rings=3),
             card("tail", [(-0.035, -0.14, 0.1), (0.035, -0.14, 0.1), (0.03, -0.24, -0.12), (-0.03, -0.24, -0.12)], (0, 0, 1, 1), "M_BirdDark"),
             rod("legL", (-0.025, -0.02, 0.12), (-0.03, 0.0, 0.0), 0.012, "M_Iron", 3),
             rod("legR", (0.025, -0.02, 0.12), (0.03, 0.0, 0.0), 0.012, "M_Iron", 3)]
    body = finish(join(parts, "Toucan"), project_uv=False, smooth_angle=45)
    empty("Beak", (0, 0.32, 0.38), body)
    for side, nm in ((-1, "WingL"), (1, "WingR")):
        sh = Vector((0.06 * side, 0.02, 0.33))
        w = box("w", (0.022, 0.22, 0.1), sh + Vector((0.012 * side, -0.1, -0.06)), rot=(math.radians(-28), 0, 0), m="M_BirdDark")
        tip = box("t", (0.018, 0.08, 0.06), sh + Vector((0.01 * side, -0.22, -0.13)), rot=(math.radians(-35), 0, 0), m="M_Iron")
        wing = join([w, tip], nm)
        set_origin(wing, sh)
        shade(wing, 5)
        parent_to(wing, body)
    export([body], "Toucan.fbx")
    return [body]


def build_vulture():
    """A black vulture (urubu), 1.5 m span: broad flat wings with fingered tips, a short tail, a bare dark-grey
    head. Wings "WingL"/"WingR" (spread, pivot at the shoulder), empty "Beak"; circles with SeaBird."""
    parts = [tube("body", [(0, -0.3, 0.0), (0, -0.1, 0.02), (0, 0.1, 0.03), (0, 0.22, 0.05)], [0.03, 0.09, 0.085, 0.04], 5, "M_BirdDark", jag=0.0),
             sphere("head", 0.045, (0, 0.27, 0.07), (0.9, 1.3, 0.9), "M_Iron", seg=6, rings=4),
             rod("beak", (0, 0.31, 0.065), (0, 0.37, 0.05), 0.014, "M_BirdPale", 3, r2=0.004),
             card("tail", [(-0.09, -0.4, 0.0), (0.09, -0.4, 0.0), (0.05, -0.26, 0.01), (-0.05, -0.26, 0.01)], (0, 0, 1, 1), "M_BirdDark")]
    body = finish(join(parts, "Vulture"), project_uv=False, smooth_angle=50)
    empty("Beak", (0, 0.4, 0.05), body)
    for side, nm in ((-1, "WingL"), (1, "WingR")):
        sh = Vector((0.07 * side, 0.03, 0.04))
        inner = box("in", (0.32, 0.28, 0.014), sh + Vector((0.17 * side, -0.02, 0)), m="M_BirdDark")
        outer = box("out", (0.3, 0.24, 0.012), sh + Vector((0.47 * side, -0.03, 0.01)), m="M_BirdDark")
        fingers = [box(f"f{k}", (0.14, 0.035, 0.01), sh + Vector((0.68 * side, 0.06 - k * 0.055, 0.02)),
                       rot=(0, 0, math.radians((8 - k * 6) * side)), m="M_Iron") for k in range(4)]
        w = join([inner, outer] + fingers, nm)
        set_origin(w, sh)
        shade(w, 5)
        parent_to(w, body)
    export([body], "Vulture.fbx")
    return [body]


# ------------------------------------------------------------------ build all
groups.append(build_kapok("Kapok_Mother", 56.0, 2.7, 11, fins=8, fin_h=9.0, fin_len=8.0, crown_r=19.0, limbs=7, lianas=14,
                          low=[(math.radians(90), 17.0, 17.0), (math.radians(55), 21.0, 13.0), (math.radians(128), 19.5, 14.0)], perches=True))
groups.append(build_kapok("Kapok_A", 44.0, 2.0, 23, fins=6, fin_h=6.0, fin_len=5.5, crown_r=14.0, limbs=6, lianas=9))
groups.append(build_kapok("Kapok_B", 38.0, 1.7, 37, fins=5, fin_h=5.0, fin_len=4.5, crown_r=12.0, limbs=5, lianas=7))
groups.append(build_emergent("Emergent_A", 34.0, 0.8, 41))
groups.append(build_emergent("Emergent_B", 28.0, 0.65, 53))
groups.append(build_emergent("Emergent_C", 38.0, 0.95, 67))
groups.append(build_midtree("MidTree_A", 14.0, 71))
groups.append(build_midtree("MidTree_B", 11.0, 83))
groups.append(build_midtree("MidTree_C", 17.0, 97))
groups.append(build_acai("AcaiPalm_A", 18.0, 0.06, 101))
groups.append(build_acai("AcaiPalm_B", 14.0, 0.1, 113, fronds=9))
groups.append(build_treefern("TreeFern_A", 3.6, 127))
groups.append(build_treefern("TreeFern_B", 2.4, 139, fronds=9))
groups.append(build_bush("Bush_A", 151))
groups.append(build_bush("Bush_B", 163, n=13, radius=2.2, h=(0.0, 1.4), size=(1.6, 2.8)))
groups.append(build_bush("Bush_C", 177, n=6, radius=0.9, h=(0.0, 0.4), size=(1.0, 1.7)))
groups.append(build_bush("Thicket_Tall", 179, n=16, radius=2.4, h=(0.0, 4.5), size=(2.2, 3.6)))
groups.append(build_canopy_wall("CanopyWall_A", 401))
groups.append(build_canopy_wall("CanopyWall_B", 409, width=16.0, height=32.0, depth=8.0))
groups.append(build_fern("Fern_A", 181))
groups.append(build_fern("Fern_B", 193, n=12, length=(1.2, 1.7)))
groups.append(build_tuft("Tuft_A", 205))
groups.append(build_tuft("Tuft_B", 217, n=7, radius=0.6, h=(0.2, 0.45)))
groups.append(build_liana_curtain("LianaCurtain_A", 229))
groups.append(build_liana_curtain("LianaCurtain_B", 241, span=5.0, z=10.0, n=7))
groups.append(build_log("FallenLog_A", 251))
groups.append(build_log("FallenLog_B", 263, length=5.5, r=0.45))
groups.append(build_surface_root("SurfaceRoot_A", 271))
groups.append(build_surface_root("SurfaceRoot_B", 283, length=5.0, r=0.24))
groups.append(build_menhir("Menhir_A", 3.4, 301))
groups.append(build_menhir("Menhir_B", 2.6, 313, w=0.75))
groups.append(build_menhir("Menhir_C", 1.6, 327, w=0.8))
groups.append(build_altar())
groups.append(build_rock("MossRock_A", 0.9, 331))
groups.append(build_rock("MossRock_B", 1.5, 337, squash=0.5, lump=0.4))
groups.append(build_frog("Frog_Green", "M_FrogSkin"))
groups.append(build_frog("Frog_Dart", "M_FrogDart"))
groups.append(build_toucan())
groups.append(build_vulture())
print("FOREST DONE")

if PREVIEW:
    roots = [o for g in groups for o in g]
    big = [o for o in roots if o.name.startswith(("Kapok", "Emergent", "MidTree", "AcaiPalm"))]
    small = [o for o in roots if o not in big]
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1800, 1000

    def shoot(objs, spacing, cols, cam_loc, target, lens, path):
        for o in roots:
            o.hide_render = o not in objs
            for c in o.children_recursive:
                c.hide_render = o not in objs
        for i, o in enumerate(objs):
            o.location = ((i % cols) * spacing - (cols - 1) * spacing / 2, (i // cols) * spacing, 0)
        cam.location = cam_loc
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = lens
        cam.data.clip_end = 1000
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("PREVIEW", path)

    shoot(big, 34, 6, (0, -120, 30), (0, 20, 22), 30, PREVIEW + "_trees.png")
    shoot(small, 4.5, 9, (0, -22, 9), (0, 6, 0.5), 28, PREVIEW + "_props.png")
