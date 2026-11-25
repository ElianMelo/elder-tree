"""
Elder Tree - Fairy arena kit: "Starlit Grotto", a cavern of glowing blue and violet mushrooms under a ceiling of
glow-worm stars (Blender 4.2+/5.x, headless).

    blender -b --factory-startup --python build_cave.py -- <abs out_dir> [preview_prefix]

The cave shell itself (floor, walls, the starry dome with its moon-hole) is generated in Unity
(ElderTreeBuilder.Cave), so it can carry the star layer and the puddle dips. This script makes the props.

PSX rules (Docs/ART_DIRECTION.md): no bevels, 3-12 sided tubes and lathes, faceted shading, props 50-800 tris; the
giant mushrooms are landscape pieces (~1-2k tris). Surface detail comes from the point-filtered, dithered Unity
textures (ElderTreeBuilder.Cave BuildCaveArt). Saturation lives in the glows (spots, gills, flowers, lanterns);
the surfaces under them stay dark.

Each prop is its own FBX with the origin at its base (stalactites and glow threads: at the top, where they hang
from). Front-facing props (houses, gnomes, shelf fungi) face Blender -Y, which is Unity +Z.
  Gnome_*     child "Head" (pivot at the neck: looks at the player), empty "Front", "Lantern" (light) -> Gnome
  MushHouse   empties "Lamp", "WindowL", "WindowR" (warm lights)

Material slot names match the Unity materials:
  M_MushCap / M_MushCapAlt   cap with spots (planar UV, centred): Unity picks blue or violet per instance
  M_MushGill / M_MushGillAlt glowing gills under the cap (planar UV, radial)
  M_MushStalk                pale fibrous stalk
  M_CaveRock, M_CaveMoss     cave stone, blue-green moss on top faces
  M_CaveFlowers              cutout atlas 2x2: bluebells (TL), star blossoms (TR), bell spikes (BL), glow threads (BR)
  M_GnomeCoat, M_GnomeHat    wool (Unity tints per gnome)   M_GnomeCloth (trousers)  M_GnomeBoot  M_GnomeSkin
  M_GnomeNose, M_GnomeBeard  M_GnomeWood  M_GnomeWindow / M_GnomeLanternGlow (warm glow)  M_Iron (existing)
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector, Quaternion, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(os.path.dirname(bpy.data.filepath or "."), "cave")
PREVIEW = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
MATS = {}


def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    MATS[name] = m


for n, c in [("M_MushCap", (0.2, 0.3, 0.7)), ("M_MushCapAlt", (0.45, 0.22, 0.65)), ("M_MushGill", (0.6, 0.85, 1.0)),
             ("M_MushGillAlt", (0.85, 0.6, 1.0)), ("M_MushStalk", (0.7, 0.68, 0.78)), ("M_CaveRock", (0.2, 0.18, 0.25)),
             ("M_CaveMoss", (0.12, 0.3, 0.32)), ("M_CaveFlowers", (0.4, 0.3, 0.8)), ("M_GnomeCoat", (0.15, 0.2, 0.4)),
             ("M_GnomeHat", (0.4, 0.15, 0.5)), ("M_GnomeCloth", (0.25, 0.22, 0.2)), ("M_GnomeBoot", (0.12, 0.08, 0.06)),
             ("M_GnomeSkin", (0.8, 0.62, 0.52)), ("M_GnomeNose", (0.85, 0.5, 0.48)), ("M_GnomeBeard", (0.85, 0.85, 0.88)),
             ("M_GnomeWood", (0.3, 0.2, 0.12)), ("M_GnomeWindow", (1.0, 0.7, 0.3)), ("M_GnomeLanternGlow", (1.0, 0.8, 0.4)),
             ("M_Iron", (0.1, 0.1, 0.11))]:
    mat(n, c)

# flower atlas quadrants (u0, v0, u1, v1)
Q_BELL = (0.0, 0.5, 0.5, 1.0)
Q_STAR = (0.5, 0.5, 1.0, 1.0)
Q_SPIKE = (0.0, 0.0, 0.5, 0.5)
Q_THREAD = (0.5, 0.0, 1.0, 0.5)
FLOWERS = "M_CaveFlowers"
# names the shared primitives below use as defaults
LEAVES = FRONDS = FLOWERS
Q_LIANA = Q_THREAD
Q_CANOPY = Q_CANOPY_DK = Q_BROAD = Q_STAR


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


def turn(objs, ang):
    """Rotate objects about the world Z axis through the origin (their own rotation included)."""
    from mathutils import Matrix
    rz = Matrix.Rotation(ang, 4, 'Z')
    for o in objs:
        o.location = rz @ o.location
        o.rotation_euler.z += ang
    return objs


# ================================================================== mushrooms
def lathe(name, prof, sides, m, r_uv, seed=0, jag=0.0, hint=None, phase=0.0):
    """Surface of revolution about +Z from a profile [(r, z)], with planar top-down UVs centred on the axis
    (r_uv = the radius that fills the texture). hint(face centre) -> the way the face should look."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rings = []
    for i, (r, z) in enumerate(prof):
        if r < 1e-5:
            rings.append([bm.verts.new((0, 0, z))])
            continue
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides + phase
            k = 1 + jag * noise.noise(Vector((i * 1.3 + seed, j * 2.1, seed * 0.7)))
            dz = jag * 0.3 * r * noise.noise(Vector((j * 1.9, i * 0.7 + seed, 3.1)))
            ring.append(bm.verts.new((math.cos(a) * r * k, math.sin(a) * r * k, z + dz)))
        rings.append(ring)
    faces = []
    for i in range(len(rings) - 1):
        A, B = rings[i], rings[i + 1]
        for j in range(sides):
            if len(A) == 1:
                faces.append(bm.faces.new([A[0], B[j], B[(j + 1) % sides]]))
            elif len(B) == 1:
                faces.append(bm.faces.new([A[j], A[(j + 1) % sides], B[0]]))
            else:
                faces.append(bm.faces.new([A[j], A[(j + 1) % sides], B[(j + 1) % sides], B[j]]))
    for f in faces:
        f.normal_update()
        if hint is not None and f.normal.dot(hint(f.calc_center_median())) < 0:
            f.normal_flip()
        for loop in f.loops:
            co = loop.vert.co
            loop[uv].uv = (0.5 + co.x / (2 * r_uv), 0.5 + co.y / (2 * r_uv))
    bm.to_mesh(me)
    bm.free()
    o = new_obj(name, me)
    o.data.materials.append(MATS[m])
    return o


def cap_profile(shape, R):
    if shape == "flat":     # the giant umbrellas (reference 1)
        ch = 0.3 * R
        return ch, [(0, ch), (0.35 * R, 0.92 * ch), (0.7 * R, 0.62 * ch), (0.92 * R, 0.2 * ch), (R, -0.04 * ch), (0.94 * R, -0.16 * ch)]
    if shape == "cone":     # pointed caps (reference 3)
        ch = 1.0 * R
        return ch, [(0, ch), (0.22 * R, 0.78 * ch), (0.5 * R, 0.45 * ch), (0.8 * R, 0.16 * ch), (R, 0.0), (0.94 * R, -0.06 * ch)]
    if shape == "bell":
        ch = 0.8 * R
        return ch, [(0, ch), (0.4 * R, 0.9 * ch), (0.75 * R, 0.6 * ch), (0.95 * R, 0.2 * ch), (R, -0.05 * ch), (0.93 * R, -0.12 * ch)]
    ch = 0.55 * R           # dome (reference 2)
    return ch, [(0, ch), (0.35 * R, 0.95 * ch), (0.65 * R, 0.78 * ch), (0.88 * R, 0.45 * ch), (R, 0.08 * ch), (0.95 * R, -0.08 * ch)]


def cap_at(name, pos, axis, R, sr, shape, seed, alt=False, sides=10):
    """A cap (spotted top) and its glowing gills, centred on pos and facing along axis."""
    ch, prof = cap_profile(shape, R)
    cm = "M_MushCapAlt" if alt else "M_MushCap"
    gm = "M_MushGillAlt" if alt else "M_MushGill"
    zc = -R * 0.9
    cap = lathe(f"{name}_c", prof, sides, cm, R, seed, jag=0.06, hint=lambda c: c - Vector((0, 0, zc)), phase=seed * 0.37)
    last = prof[-1]
    gprof = [last, (0.55 * R, last[1] * 0.4 + 0.05 * ch), (sr * 1.1, 0.12 * ch)]
    gill = lathe(f"{name}_g", gprof, sides, gm, R, seed + 1, jag=0.04, hint=lambda c: Vector((0, 0, -1)), phase=seed * 0.37)
    q = Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized())
    for o in (cap, gill):
        o.rotation_mode = 'QUATERNION'
        o.rotation_quaternion = q
        o.location = Vector(pos) - Vector(axis).normalized() * 0.1 * ch
        activate(o)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
    return [cap, gill]


def mushroom(name, base, height, R, sr, seed, shape="dome", bend=0.1, sides=10, skirt=False, alt=False, tilt=0.4):
    """Stalk (bulbous foot, bending away from the vertical) + cap + gills (+ a drooping ring on the big ones)."""
    rnd = random.Random(seed)
    yaw = rnd.uniform(0, 2 * math.pi)
    d = Vector((math.cos(yaw), math.sin(yaw), 0))
    base = Vector(base)
    ts = [k / 4 for k in range(5)]
    pts = [base + d * bend * height * t * t + Vector((0, 0, height * t)) for t in ts]
    pts[0] = pts[0] - Vector((0, 0, 0.06 * height))
    radii = [sr * 1.55, sr * 1.08, sr, sr * 0.93, sr * 0.86]
    stalk = tube(f"{name}_s", pts, radii, max(5, sides - 3), "M_MushStalk", seed=seed, jag=0.12, uv_len=max(0.5, height * 0.35), cap=False)
    axis = ((pts[-1] - pts[-2]).normalized() * tilt + Vector((0, 0, 1)) * (1 - tilt)).normalized()
    parts = [stalk] + cap_at(name, pts[-1], axis, R, sr, shape, seed, alt, sides)
    if skirt:
        sp = lerp_path(pts, 0.72)
        sk = lathe(f"{name}_k", [(sr * 0.95, 0.0), (sr * 1.7, -0.05 * height), (sr * 1.95, -0.1 * height)], max(6, sides - 2), "M_MushStalk",
                   sr * 2, seed + 2, jag=0.12, hint=lambda c: Vector((c.x, c.y, 0.4)))
        sk.location = sp
        parts.append(sk)
    return parts


def export_group(parts, name):
    o = finish(join(parts, name), project_uv=False, smooth_angle=40)
    export([o], f"{name}.fbx")
    return [o]


def build_giant_a():
    """Reference 1's tall umbrella: an 11 m stalk leaning out, a broad shallow cap, two young ones at its foot."""
    parts = mushroom("g", (0, 0, 0), 11.0, 4.6, 0.55, 11, "flat", bend=0.14, sides=12, skirt=True)
    parts += mushroom("y1", (1.3, 0.5, 0), 2.2, 0.85, 0.13, 12, "cone", bend=0.2, sides=8, alt=True)
    parts += mushroom("y2", (-0.9, -0.9, 0), 1.3, 0.55, 0.09, 13, "dome", bend=0.25, sides=8, alt=True)
    return export_group(parts, "Mush_Giant_A")


def build_giant_b():
    """Reference 1's pagoda: shelf after shelf of caps up one stalk, the last a little cone."""
    rnd = random.Random(21)
    pts = [Vector((0, 0, -0.4)), Vector((0.1, 0, 2.5)), Vector((0.25, 0.1, 5.0)), Vector((0.3, 0.3, 7.4)), Vector((0.2, 0.45, 9.2))]
    parts = [tube("s", pts, [0.75, 0.5, 0.44, 0.38, 0.3], 8, "M_MushStalk", seed=21, jag=0.1, uv_len=3.0, cap=False)]
    for k, (t, R) in enumerate([(0.36, 2.7), (0.6, 2.1), (0.8, 1.6)]):
        p = lerp_path(pts, t)
        tilt = Vector((rnd.uniform(-0.12, 0.12), rnd.uniform(-0.12, 0.12), 1))
        parts += cap_at(f"t{k}", p, tilt, R, 0.42, "flat", 30 + k, sides=12)
    parts += cap_at("top", pts[-1], (0.1, 0.15, 1), 1.0, 0.3, "cone", 40, sides=10)
    parts += mushroom("y1", (1.1, -0.6, 0), 1.6, 0.6, 0.1, 41, "dome", bend=0.3, sides=8, alt=True)
    return export_group(parts, "Mush_Giant_B")


def build_single(name, height, R, sr, seed, shape, bend, skirt=False, sides=10):
    return export_group(mushroom("m", (0, 0, 0), height, R, sr, seed, shape, bend, sides, skirt), name)


def build_twin(name, seed):
    parts = mushroom("a", (0, 0, 0), 1.9, 0.8, 0.12, seed, "dome", bend=0.18, sides=9)
    parts += mushroom("b", (0.45, 0.25, 0), 1.15, 0.55, 0.08, seed + 1, "cone", bend=0.3, sides=8, alt=True)
    return export_group(parts, name)


def build_cluster(name, seed, n, h, R0, shapes=("dome", "cone", "bell"), spread=0.35):
    """A clump of small mushrooms; every other one takes the alternate colour."""
    rnd = random.Random(seed)
    parts = []
    for k in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        r = spread * math.sqrt(rnd.random()) if k else 0.0
        hh = rnd.uniform(*h) * (1.0 if k == 0 else rnd.uniform(0.6, 1.0))
        R = hh * rnd.uniform(*R0)
        parts += mushroom(f"c{k}", (math.cos(a) * r, math.sin(a) * r, 0), hh, R, max(0.015, R * 0.17), seed * 7 + k,
                          rnd.choice(shapes), bend=rnd.uniform(0.1, 0.4), sides=7, alt=(k % 2 == 1))
    return export_group(parts, name)


def build_shelf(name, seed, n=4):
    """Bracket fungi stepping up a wall (the wall is the Blender XZ plane; they stand out toward -Y = Unity +Z)."""
    rnd = random.Random(seed)
    parts = []
    for k in range(n):
        R = rnd.uniform(0.28, 0.6)
        p = Vector((rnd.uniform(-0.5, 0.5), -R * 0.3, k * 0.32 + rnd.uniform(-0.05, 0.05)))
        parts += cap_at(f"s{k}", p, (rnd.uniform(-0.15, 0.15), -0.35, 1), R, R * 0.12, "flat", seed + k, alt=(k % 2 == 1), sides=8)
    return export_group(parts, name)


def build_house(name, seed, alt=False):
    """A gnome house in a fat mushroom (reference 2): round-topped door, two warm windows, a lantern by the door,
    a stone chimney through the cap. Front = -Y (Unity +Z)."""
    H = 2.7
    pts = [(0, 0, -0.2), (0, 0, 0.6), (0, 0, 1.4), (0, 0, 2.1), (0, 0, H)]
    parts = [tube("trunk", pts, [1.45, 1.33, 1.26, 1.12, 0.95], 10, "M_MushStalk", seed=seed, jag=0.05, uv_len=1.2, cap=False)]
    parts.append(box("door", (0.62, 0.14, 1.0), (0, -1.3, 0.5), m="M_GnomeWood"))
    parts.append(cyl("doortop", 0.31, 0.14, (0, -1.3, 1.0), rot=(math.pi / 2, 0, 0), m="M_GnomeWood", verts=8))
    parts.append(box("plank", (0.04, 0.16, 1.2), (0, -1.31, 0.62), m="M_Iron"))
    parts.append(sphere("knob", 0.04, (0.2, -1.39, 0.55), m="M_Iron", seg=4, rings=3))
    parts.append(box("step", (0.95, 0.5, 0.14), (0, -1.6, 0.04), m="M_CaveRock", jitter=0.03))
    for s in (-1, 1):
        win = [cyl("glass", 0.24, 0.12, (0, -1.2, 1.7), rot=(math.pi / 2, 0, 0), m="M_GnomeWindow", verts=8),
               cyl("rim", 0.29, 0.08, (0, -1.17, 1.7), rot=(math.pi / 2, 0, 0), m="M_GnomeWood", verts=8),
               box("barH", (0.48, 0.05, 0.04), (0, -1.27, 1.7), m="M_GnomeWood"),
               box("barV", (0.04, 0.05, 0.48), (0, -1.27, 1.7), m="M_GnomeWood")]
        parts += turn(win, math.radians(44 * s))
    # lantern on a crooked post by the door
    lamp = Vector((0.78, -1.75, 1.15))
    parts.append(rod("post", (0.78, -1.5, -0.1), (0.78, -1.5, 1.38), 0.035, "M_GnomeWood", 5))
    parts.append(rod("hook", (0.78, -1.5, 1.34), (0.78, -1.75, 1.34), 0.015, "M_Iron", 3))
    parts.append(box("lampglass", (0.12, 0.12, 0.16), lamp, m="M_GnomeLanternGlow"))
    parts.append(cyl("lamplid", 0.09, 0.06, lamp + Vector((0, 0, 0.11)), m="M_Iron", verts=4, r2=0.015))
    parts.append(box("lampbase", (0.14, 0.14, 0.03), lamp - Vector((0, 0, 0.09)), m="M_Iron"))
    parts.append(cyl("chimney", 0.2, 1.1, (0.85, 0.55, H + 1.35), m="M_CaveRock", verts=6, r2=0.17))
    parts += cap_at("cap", (0, 0, H - 0.05), (0, 0.05, 1), 2.5, 0.95, "dome", seed, alt, sides=12)
    o = finish(join(parts, name), project_uv=False, smooth_angle=40)
    for nm, p in (("Lamp", lamp), ("WindowL", turn_point((0, -1.75, 1.7), math.radians(-44))), ("WindowR", turn_point((0, -1.75, 1.7), math.radians(44)))):
        empty(nm, p, o)
    export([o], f"{name}.fbx")
    return [o]


def turn_point(p, ang):
    from mathutils import Matrix
    return Matrix.Rotation(ang, 4, 'Z') @ Vector(p)


# ================================================================== stone
def build_spike(name, L, r, seed, up=False, extra=2):
    """A stalactite (hanging, origin at the ceiling) or stalagmite (up, origin on the floor), with a few small
    ones grown against it."""
    rnd = random.Random(seed)
    sgn = 1 if up else -1
    parts = []
    for e in range(1 + extra):
        LL, rr = (L, r) if e == 0 else (L * rnd.uniform(0.25, 0.5), r * rnd.uniform(0.3, 0.5))
        off = Vector((0, 0, 0)) if e == 0 else Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized() * r * rnd.uniform(0.7, 1.0)
        drift = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * 0.05 * LL
        pts, radii = [], []
        n = 6
        for k in range(n + 1):
            t = k / n
            pts.append(off + Vector((0, 0, sgn * (-0.6 + (LL + 0.6) * t))) + drift * t * t)
            radii.append(max(0.012, rr * (1 - t) ** 1.2 * (1.0 + 0.22 * math.sin(t * 9 + seed + e))))
        parts.append(tube(f"sp{e}", pts, radii, 6, "M_CaveRock", seed=seed + e, jag=0.22, uv_len=1.0, cap=True))
    o = finish(join(parts, name), project_uv=True, uv_scale=0.8, smooth_angle=35)
    if up:
        mossy_tops(o, "M_CaveMoss", 0.5)
    export([o], f"{name}.fbx")
    return [o]


def build_column(name, seed):
    """A floor-to-ceiling column, 11 m (Unity stretches it to the ceiling): flared at both ends, waisted between."""
    pts = [(0, 0, -0.6), (0.1, 0, 1.0), (0.2, 0.1, 3.5), (0.1, 0.2, 6.5), (0, 0.1, 9.0), (0, 0, 11.6)]
    o = tube(name, pts, [1.9, 1.15, 0.72, 0.68, 1.05, 2.0], 8, "M_CaveRock", seed=seed, jag=0.28, flute=0.5, flute_n=4, uv_len=1.2)
    o = finish(join([o], name), project_uv=True, uv_scale=0.6, smooth_angle=35)
    mossy_tops(o, "M_CaveMoss", 0.45)
    export([o], f"{name}.fbx")
    return [o]


def build_boulder(name, r, seed, squash=0.6, lump=0.35):
    o = sphere(name, r, (0, 0, r * squash * 0.4), (1.0, 0.85, squash), "M_CaveRock", seg=8, rings=5, lumpy=r * lump, seed=seed)
    o = finish(join([o], name), uv_scale=0.8, smooth_angle=25)
    mossy_tops(o, "M_CaveMoss", up=0.35)
    export([o], f"{name}.fbx")
    return [o]


# ================================================================== flowers, threads
def build_flowers(name, specs, seed):
    """Crossed cutout cards: (x, y, quadrant, width, height) per clump."""
    parts = []
    for k, (x, y, q, w, h) in enumerate(specs):
        parts += clump(f"f{k}", (x, y, 0), w, q, FLOWERS, n=3, tilt=0.18, seed=seed + k, h=h)
    o = finish(join(parts, name), project_uv=False, smooth_angle=0)
    export([o], f"{name}.fbx")
    return [o]


def build_threads(name, seed, n, length, spread):
    """Glow-worm threads hanging from the ceiling (origin at the top): silk lines beaded with light."""
    rnd = random.Random(seed)
    parts = []
    for k in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        r = spread * math.sqrt(rnd.random())
        top = (math.cos(a) * r, math.sin(a) * r, 0.15)
        parts += hang(f"t{k}", top, rnd.uniform(0.35, 0.55), rnd.uniform(*length), rnd.uniform(0, math.pi), sway=0.05, q=Q_THREAD, m=FLOWERS, segs=2)
    o = finish(join(parts, name), project_uv=False, smooth_angle=0)
    export([o], f"{name}.fbx")
    return [o]


# ================================================================== gnomes
def build_gnome(name, kind):
    """A cave gnome (~0.8 m to the hat tip), facing -Y (Unity +Z). kind: lantern (holds a lantern on a staff),
    pick (a pick over the shoulder), sit (sits with legs out, pipe glowing). Child "Head" (pivot at the neck)."""
    parts = []
    if kind == "sit":
        for s in (-1, 1):
            parts.append(rod(f"leg{s}", (0.055 * s, -0.02, 0.06), (0.065 * s, -0.18, 0.05), 0.042, "M_GnomeCloth", 6, r2=0.038))
            parts.append(box(f"boot{s}", (0.085, 0.07, 0.12), (0.067 * s, -0.22, 0.06), m="M_GnomeBoot"))
        z0 = -0.09
    else:
        for s in (-1, 1):
            parts.append(box(f"boot{s}", (0.085, 0.14, 0.06), (0.06 * s, -0.025, 0.03), m="M_GnomeBoot"))
            parts.append(cyl(f"leg{s}", 0.04, 0.12, (0.055 * s, 0, 0.11), m="M_GnomeCloth", verts=6))
        z0 = 0.0
    Z = lambda z: z + z0
    parts.append(sphere("body", 0.13, (0, 0, Z(0.25)), (1.0, 0.9, 1.15), "M_GnomeCoat", seg=8, rings=6))
    parts.append(cyl("belt", 0.127, 0.035, (0, 0, Z(0.2)), m="M_GnomeBoot", verts=8))
    parts.append(box("buckle", (0.05, 0.02, 0.04), (0, -0.122, Z(0.2)), m="M_Iron"))
    hands = {}
    for s in (-1, 1):
        sh = Vector((0.11 * s, 0, Z(0.32)))
        if kind == "lantern" and s == -1:
            hand = Vector((-0.17, -0.1, Z(0.3)))
        elif kind == "pick" and s == 1:
            hand = Vector((0.14, -0.08, Z(0.34)))
        elif kind == "sit":
            hand = Vector((0.12 * s, -0.09, Z(0.16)))
        else:
            hand = Vector((0.15 * s, -0.04, Z(0.19)))
        parts.append(rod(f"arm{s}", sh, hand, 0.036, "M_GnomeCoat", 5, r2=0.03))
        parts.append(sphere(f"hand{s}", 0.035, hand, m="M_GnomeSkin", seg=6, rings=4))
        hands[s] = hand
    glow = None
    if kind == "lantern":
        h = hands[-1]
        top = h + Vector((-0.03, -0.06, 0.26))
        parts.append(rod("staff", h + Vector((0.01, 0.02, -0.29)), top, 0.013, "M_GnomeWood", 4))
        tip = top + Vector((0, -0.1, 0))
        parts.append(rod("crook", top, tip, 0.009, "M_Iron", 3))
        glow = tip + Vector((0, 0, -0.1))
        parts.append(box("glass", (0.07, 0.07, 0.085), glow, m="M_GnomeLanternGlow"))
        parts.append(cyl("lid", 0.055, 0.035, glow + Vector((0, 0, 0.06)), m="M_Iron", verts=4, r2=0.01))
        parts.append(box("base", (0.08, 0.08, 0.015), glow - Vector((0, 0, 0.05)), m="M_Iron"))
    elif kind == "pick":
        h = hands[1]
        top = Vector((0.1, 0.12, Z(0.55)))
        parts.append(rod("haft", h + Vector((0.01, -0.06, -0.08)), top, 0.014, "M_GnomeWood", 4))
        parts.append(rod("pickA", top, top + Vector((-0.13, 0.02, -0.04)), 0.02, "M_Iron", 4, r2=0.004))
        parts.append(rod("pickB", top, top + Vector((0.13, 0.0, -0.05)), 0.02, "M_Iron", 4, r2=0.004))
    body = finish(join(parts, name), project_uv=True, uv_scale=6.0, smooth_angle=45)

    hc = Vector((0, -0.005, Z(0.45)))
    hp = [sphere("head", 0.092, hc, (1, 0.95, 1), "M_GnomeSkin", seg=8, rings=6),
          sphere("nose", 0.037, hc + Vector((0, -0.092, -0.008)), (1, 1, 0.9), "M_GnomeNose", seg=6, rings=4)]
    for s in (-1, 1):
        hp.append(sphere(f"eye{s}", 0.012, hc + Vector((0.036 * s, -0.08, 0.025)), m="M_Iron", seg=4, rings=3))
        hp.append(sphere(f"brow{s}", 0.022, hc + Vector((0.04 * s, -0.076, 0.047)), (1.4, 0.7, 0.5), "M_GnomeBeard", seg=5, rings=3))
        hp.append(rod(f"stache{s}", hc + Vector((0.005 * s, -0.1, -0.03)), hc + Vector((0.075 * s, -0.085, -0.055)), 0.017, "M_GnomeBeard", 4, r2=0.006))
        hp.append(sphere(f"ear{s}", 0.026, hc + Vector((0.09 * s, 0.0, 0.0)), (0.5, 1.0, 1.4), "M_GnomeSkin", seg=5, rings=3))
    beard = tube("beard", [hc + Vector((0, -0.055, -0.03)), hc + Vector((0, -0.085, -0.12)), hc + Vector((0, -0.095, -0.21)), hc + Vector((0, -0.08, -0.29))],
                 [0.08, 0.074, 0.05, 0.008], 6, "M_GnomeBeard", seed=3, jag=0.1, uv_len=0.3)
    for v in beard.data.vertices:
        v.co.y = -0.07 + (v.co.y + 0.07) * 0.6
    hp.append(beard)
    hp.append(cyl("brim", 0.102, 0.025, hc + Vector((0, 0, 0.055)), m="M_GnomeHat", verts=8))
    hp.append(tube("hat", [hc + Vector((0, 0, 0.05)), hc + Vector((0, 0.006, 0.17)), hc + Vector((0, 0.03, 0.27)), hc + Vector((0, 0.075, 0.34)),
                           hc + Vector((0, 0.125, 0.36))], [0.1, 0.078, 0.048, 0.02, 0.004], 7, "M_GnomeHat", seed=5, jag=0.04, uv_len=0.25))
    if kind == "sit":
        bowl = hc + Vector((0.07, -0.19, -0.06))
        hp.append(rod("pipe", hc + Vector((0.02, -0.1, -0.04)), bowl, 0.008, "M_GnomeWood", 3))
        hp.append(cyl("bowl", 0.018, 0.04, bowl + Vector((0, 0, 0.015)), m="M_GnomeWood", verts=5))
        hp.append(sphere("ember", 0.014, bowl + Vector((0, 0, 0.035)), m="M_GnomeLanternGlow", seg=4, rings=3))
        glow = bowl + Vector((0, 0, 0.04))
    head = join(hp, "Head")
    project(head, 6.0)
    set_origin(head, Vector((0, 0, Z(0.37))))
    shade(head, 45)
    parent_to(head, body)
    empty("Front", (0, -0.5, Z(0.3)), body)
    if glow is not None:
        empty("Lantern", glow, head if kind == "sit" else body)
    export([body], f"{name}.fbx")
    return [body]


# ------------------------------------------------------------------ build all
groups = []
groups.append(build_giant_a())
groups.append(build_giant_b())
groups.append(build_single("Mush_Tall_A", 5.5, 2.3, 0.3, 51, "dome", 0.16, skirt=True, sides=12))
groups.append(build_single("Mush_Tall_B", 4.0, 1.7, 0.24, 53, "bell", 0.28, skirt=True, sides=11))
groups.append(build_single("Mush_Tall_C", 6.5, 1.6, 0.26, 55, "cone", 0.1, sides=11))
groups.append(build_single("Mush_Med_A", 2.0, 0.85, 0.12, 61, "cone", 0.2, sides=9))
groups.append(build_single("Mush_Med_B", 2.6, 1.15, 0.16, 63, "dome", 0.15, sides=10))
groups.append(build_single("Mush_Med_C", 1.4, 0.75, 0.1, 65, "flat", 0.3, sides=9))
groups.append(build_twin("Mush_Twin", 67))
groups.append(build_cluster("Mush_Cluster_A", 71, 5, (0.35, 0.8), (0.35, 0.5)))
groups.append(build_cluster("Mush_Cluster_B", 73, 8, (0.12, 0.38), (0.3, 0.45), shapes=("cone", "bell"), spread=0.3))
groups.append(build_cluster("Mush_Cluster_C", 79, 3, (0.7, 1.1), (0.35, 0.5), spread=0.4))
groups.append(build_cluster("Mush_Tiny", 83, 3, (0.1, 0.22), (0.35, 0.5), spread=0.12))
groups.append(build_shelf("Mush_Shelf_A", 91))
groups.append(build_shelf("Mush_Shelf_B", 97, n=6))
groups.append(build_house("MushHouse_A", 101))
groups.append(build_house("MushHouse_B", 107, alt=True))
groups.append(build_spike("Stalactite_A", 4.5, 0.75, 201))
groups.append(build_spike("Stalactite_B", 2.6, 0.45, 203, extra=3))
groups.append(build_spike("Stalactite_C", 7.0, 1.1, 207, extra=2))
groups.append(build_spike("Stalagmite_A", 2.4, 0.6, 211, up=True))
groups.append(build_spike("Stalagmite_B", 4.2, 0.9, 213, up=True, extra=3))
groups.append(build_spike("Stalagmite_C", 1.2, 0.35, 217, up=True, extra=1))
groups.append(build_column("Column_A", 221))
groups.append(build_boulder("Boulder_A", 0.9, 231))
groups.append(build_boulder("Boulder_B", 1.6, 233, squash=0.5, lump=0.4))
groups.append(build_boulder("Boulder_C", 2.4, 237, squash=0.45, lump=0.45))
groups.append(build_flowers("Flowers_Bell", [(0, 0, Q_BELL, 0.5, 0.42), (0.25, 0.15, Q_BELL, 0.42, 0.34)], 301))
groups.append(build_flowers("Flowers_Star", [(0, 0, Q_STAR, 0.46, 0.34), (-0.22, 0.12, Q_STAR, 0.38, 0.28)], 311))
groups.append(build_flowers("Flowers_Spike", [(0, 0, Q_SPIKE, 0.5, 0.8), (0.2, -0.15, Q_SPIKE, 0.4, 0.6)], 321))
groups.append(build_flowers("Flowers_Bed", [(0, 0, Q_STAR, 0.5, 0.36), (0.45, 0.2, Q_BELL, 0.5, 0.42), (-0.4, 0.25, Q_SPIKE, 0.45, 0.7),
                                            (0.15, -0.45, Q_BELL, 0.45, 0.38), (-0.3, -0.35, Q_STAR, 0.42, 0.3)], 331))
groups.append(build_threads("GlowThreads_A", 401, 7, (1.2, 2.6), 0.8))
groups.append(build_threads("GlowThreads_B", 409, 12, (0.8, 4.0), 1.6))
groups.append(build_gnome("Gnome_Lantern", "lantern"))
groups.append(build_gnome("Gnome_Pick", "pick"))
groups.append(build_gnome("Gnome_Sit", "sit"))
print("CAVE DONE")

if PREVIEW:
    roots = [o for g in groups for o in g]
    big = [o for o in roots if o.name.startswith(("Mush_Giant", "Mush_Tall", "Column", "Stalactite_C", "MushHouse"))]
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

    shoot(big, 11, 5, (0, -48, 12), (0, 6, 5), 30, PREVIEW + "_big.png")
    shoot(small, 2.6, 9, (0, -16, 6), (0, 4, 0.6), 30, PREVIEW + "_props.png")
    gn = [o for o in roots if o.name.startswith("Gnome")]
    shoot(gn, 0.8, 3, (0.3, -2.4, 0.9), (0, 0, 0.4), 50, PREVIEW + "_gnomes.png")
