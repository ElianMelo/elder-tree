"""
Fairy Mask set, PSX dark fantasy (Docs/ART_DIRECTION.md). Everything the Fairy reward needs:

    blender -b --factory-startup --python Tools/Blender/build_fairy_form.py -- <abs out_dir>

FairyForm.fbx      - the humanoid fae the player turns into. Same skeleton as the Elder Tree and the other forms
                     (Human Mega Animations Pack HumanM x 0.95), PS1 rigid skinning. A human-sized echo of the Fairy
                     boss: pale skin, a dusky blue dress to the knee with a ragged hem, long white hair, pointed ears,
                     cold pale-blue eyes, a pale gem on the breast, bare feet.
FairyFormWings.fbx - four flat veined wing cards ("FWing_U_L/R", "FWing_D_L/R"), origin at the wing root on the upper
                     back, same space as the body in bind pose. Unity parents them to the chest and flutters them.
FairyDagger.fbx    - one small white dagger (the form holds two). Origin = grip centre, blade along +Z (Unity +Y):
                     a leaf-shaped bone-white blade, a crescent-moon guard, a pale blue wrapped grip, a gem pommel.
FairyChakram.fbx   - the ranged weapon: a pink crescent blade (after the user's crescent-moon references) with sharp
                     outer and inner edges, a thicker engraved band in the middle and a wrapped grip on the inner edge.
                     Origin = the centre of its circle, flat in the XY plane (it spins about +Z = Unity +Y).
                     "Grip" empty = where the hand holds it.
FairyMask.fbx      - the reward item: a pale porcelain fae face with pointed ears, glowing eyes, a white fringe and
                     two small wings at the temples.

Materials are remapped by name in Unity (shared with the Fairy boss): M_FairySkin, M_FairyDress, M_FairyHair,
M_FairyWing, M_FairyEye, M_FairyHollow, M_FairyTrim, M_FairyGem, plus M_FairyBlade, M_FairyGrip, M_FairyChakram.
Conventions: Z up, the character faces -Y, its LEFT side is +X.
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(globals().get("__file__", os.path.join("Tools", "Blender", "x.py")))))
sys.dont_write_bytecode = True
import importlib  # noqa: E402
import mask_shell  # noqa: E402  (shared Majora-style face plate for the reward mask)
importlib.reload(mask_shell)
# TS_ONLY=CrocMask.fbx,... exports just those files (the rest of the set is still built, not written)
ONLY = {n.strip() for n in os.environ.get("TS_ONLY", "").split(",") if n.strip()}

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = globals().get("TS_EXPORT", True)
OUT = os.path.abspath(argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(globals().get("__file__", "."))), "export"))
if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
else:
    scene = bpy.data.scenes.get("FairyForm") or bpy.data.scenes.new("FairyForm")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    try:
        bpy.context.window.scene = scene
    except Exception:
        pass

MAT_RGB = {
    "M_FairySkin": (0.78, 0.74, 0.7), "M_FairyDress": (0.18, 0.27, 0.45), "M_FairyHair": (0.86, 0.85, 0.82),
    "M_FairyWing": (0.9, 0.92, 0.95), "M_FairyEye": (0.6, 0.85, 1.0), "M_FairyHollow": (0.05, 0.04, 0.05),
    "M_FairyTrim": (0.6, 0.62, 0.66), "M_FairyGem": (0.55, 0.8, 1.0), "M_FairyBlade": (0.9, 0.89, 0.85),
    "M_FairyGrip": (0.3, 0.4, 0.55), "M_FairyChakram": (0.78, 0.45, 0.55),
}
TILE = {"M_FairySkin": 0.35, "M_FairyDress": 0.45, "M_FairyHair": 0.35, "M_FairyTrim": 0.2, "M_FairyHollow": 0.5,
        "M_FairyBlade": 0.25, "M_FairyGrip": 0.12}
mask_shell.register(MAT_RGB, TILE)
FLAT = ("M_FairyEye", "M_FairyGem")
rng = random.Random(5151)

# ----------------------------------------------------------------------------------------------- skeleton
# Identical to build_elder_tree.py / build_druid_form.py (HumanM x 0.95). Keep these in sync: sockets rely on them.
HIPS_Z, SPINE_Z, CHEST_Z, NECK_Z, HEAD_Z, HEAD_END_Z = 0.93, 1.01, 1.20, 1.455, 1.525, 1.95
SHOULDER_IN, SHOULDER_X, SHOULDER_Z = 0.07, 0.30, 1.38
ELBOW_X, WRIST_X = 0.545, 0.755
KNUCKLE_X = WRIST_X + 0.075
HIP_X, HIP_Z, KNEE_Z, ANKLE_Z = 0.12, 0.88, 0.49, 0.12
TOE_Y, TOE_Z, TOE_END_Y = -0.14, 0.03, -0.24
FINGERS = [("Index", -0.040, (0.046, 0.036, 0.030)), ("Middle", -0.013, (0.050, 0.039, 0.032)),
           ("Ring", 0.014, (0.047, 0.037, 0.030)), ("Little", 0.040, (0.040, 0.033, 0.027))]
THUMB_ROOT = (0.035, -0.036, -0.014)
THUMB_SEGS = [(0.040, -0.028, -0.018), (0.032, -0.019, -0.014), (0.027, -0.014, -0.011)]
PHALANX = ("Proximal", "Intermediate", "Distal")
BONES = {
    "Hips": ((0, 0.0, HIPS_Z), (0, 0.0, SPINE_Z), None),
    "Spine": ((0, 0.0, SPINE_Z), (0, 0.0, CHEST_Z), "Hips"),
    "Chest": ((0, 0.0, CHEST_Z), (0, 0.0, NECK_Z), "Spine"),
    "Neck": ((0, 0.0, NECK_Z), (0, 0.0, HEAD_Z), "Chest"),
    "Head": ((0, 0.0, HEAD_Z), (0, 0.0, HEAD_END_Z), "Neck"),
}


def finger_chains(s):
    L = "Left" if s > 0 else "Right"
    out = {}
    for name, y, lens in FINGERS:
        x, z, chain = KNUCKLE_X, SHOULDER_Z, []
        for k, ln in enumerate(lens):
            h = (x * s, y, z)
            x += ln
            z -= 0.002 * (k + 1)
            chain.append((L + name + PHALANX[k], h, (x * s, y, z)))
        out[name] = chain
    p = Vector((WRIST_X + THUMB_ROOT[0], THUMB_ROOT[1], SHOULDER_Z + THUMB_ROOT[2]))
    chain = []
    for k, d in enumerate(THUMB_SEGS):
        q = p + Vector(d)
        chain.append((L + "Thumb" + PHALANX[k], (p.x * s, p.y, p.z), (q.x * s, q.y, q.z)))
        p = q
    out["Thumb"] = chain
    return out


for _s in (1, -1):
    _L = "Left" if _s > 0 else "Right"
    BONES[_L + "Shoulder"] = ((SHOULDER_IN * _s, 0.0, SHOULDER_Z), (SHOULDER_X * _s, 0.0, SHOULDER_Z), "Chest")
    BONES[_L + "UpperArm"] = ((SHOULDER_X * _s, 0.0, SHOULDER_Z), (ELBOW_X * _s, 0.0, SHOULDER_Z), _L + "Shoulder")
    BONES[_L + "LowerArm"] = ((ELBOW_X * _s, 0.0, SHOULDER_Z), (WRIST_X * _s, 0.0, SHOULDER_Z), _L + "UpperArm")
    BONES[_L + "Hand"] = ((WRIST_X * _s, 0.0, SHOULDER_Z), (KNUCKLE_X * _s, 0.0, SHOULDER_Z), _L + "LowerArm")
    for _chain in finger_chains(_s).values():
        _parent = _L + "Hand"
        for _b, _h, _t in _chain:
            BONES[_b] = (_h, _t, _parent)
            _parent = _b
    BONES[_L + "UpperLeg"] = ((HIP_X * _s, 0.0, HIP_Z), (HIP_X * _s, 0.0, KNEE_Z), "Hips")
    BONES[_L + "LowerLeg"] = ((HIP_X * _s, 0.0, KNEE_Z), (HIP_X * _s, 0.0, ANKLE_Z), _L + "UpperLeg")
    BONES[_L + "Foot"] = ((HIP_X * _s, 0.0, ANKLE_Z), (HIP_X * _s, TOE_Y, TOE_Z), _L + "LowerLeg")
    BONES[_L + "Toes"] = ((HIP_X * _s, TOE_Y, TOE_Z), (HIP_X * _s, TOE_END_Y, TOE_Z), _L + "Foot")


# ----------------------------------------------------------------------------------------------- helpers
def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*MAT_RGB[name], 1)
    return m


V = Vector


class Part:
    """One mesh piece assembled with bmesh. bone: rigid skin binding (100% to that bone). Faces may carry their own UVs."""

    def __init__(self, name, bone=None, origin=V((0, 0, 0))):
        self.name, self.bone, self.origin = name, bone, V(origin)
        self.bm = bmesh.new()
        self.mats = []
        self.uvs = {}

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def v(self, co):
        return self.bm.verts.new(V(co) - self.origin)

    def face(self, verts, mat, uv=None):
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = self.mi(mat)
        if uv:
            self.uvs[f] = uv
        return f

    def loft(self, rings, mat, cap0=True, cap1=True, closed=True, mats=None):
        vr = [[self.v(p) for p in ring] for ring in rings]
        n = len(rings[0])
        segs = n if closed else n - 1
        for i in range(len(vr) - 1):
            for j in range(segs):
                m = mats(i, j) if mats else mat
                self.face([vr[i][j], vr[i][(j + 1) % n], vr[i + 1][(j + 1) % n], vr[i + 1][j]], m)
        if closed:
            for ring, cap, rev in ((vr[0], cap0, True), (vr[-1], cap1, False)):
                if not cap:
                    continue
                c = self.bm.verts.new(sum((vv.co for vv in ring), V()) / len(ring))
                for j in range(n):
                    a, b = ring[j], ring[(j + 1) % n]
                    self.face([b, a, c] if rev else [a, b, c], mats(-1, j) if mats else mat)
        return vr

    def spike(self, base, tip, side, width, mat, sides=3):
        base, tip = V(base), V(tip)
        axis = (tip - base).normalized()
        s1 = V(side) - axis * V(side).dot(axis)
        if s1.length < 1e-5:
            s1 = axis.orthogonal()
        s1.normalize()
        s2 = axis.cross(s1).normalized()
        ring = [self.v(base + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides)) * width)
                for k in range(sides)]
        t = self.v(tip)
        for k in range(sides):
            self.face([ring[k], ring[(k + 1) % sides], t], mat)
        self.face(list(reversed(ring)), mat)

    def disc(self, c, normal, r, mat, sides=6, depth=0.0, squash=1.0):
        n = V(normal).normalized()
        s1 = n.cross(V((0, 0, 1)) if abs(n.z) < 0.9 else V((1, 0, 0))).normalized()
        s2 = n.cross(s1).normalized()
        ring = [self.v(V(c) + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides) * squash) * r)
                for k in range(sides)]
        mid = self.v(V(c) + n * depth)
        for k in range(sides):
            self.face([ring[(k + 1) % sides], ring[k], mid], mat)

    def octa(self, c, r, mat, stretch=1.4, axis=2):
        c = V(c)
        e = [V((r, 0, 0)), V((0, r, 0)), V((0, 0, r))]
        up = e[axis] * stretch / r * r
        ring_axes = [e[i] for i in range(3) if i != axis]
        p = [self.v(c + d) for d in (ring_axes[0], ring_axes[1], -ring_axes[0], -ring_axes[1])]
        top, bot = self.v(c + up), self.v(c - up)
        for k in range(4):
            a, b = p[k], p[(k + 1) % 4]
            self.face([a, b, top], mat)
            self.face([b, a, bot], mat)

    def finish(self, smooth_angle=35, weld=True):
        bm = self.bm
        if weld and not self.uvs:
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        uvl = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            mat = self.mats[f.material_index]
            if f in self.uvs:
                for loop, uv in zip(f.loops, self.uvs[f]):
                    loop[uvl].uv = uv
                continue
            T = TILE.get(mat, 1.0)
            n = f.normal
            ax = max(range(3), key=lambda k: abs(n[k]))
            for loop in f.loops:
                co = loop.vert.co + self.origin
                uv = (co.y, co.z) if ax == 0 else (co.x, co.z) if ax == 1 else (co.x, co.y)
                if mat in FLAT:
                    uv = (0.5, 0.5)
                loop[uvl].uv = (uv[0] / T, uv[1] / T)
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for m in self.mats:
            me.materials.append(material(m))
        for p in me.polygons:
            p.use_smooth = True
        try:
            me.set_sharp_from_angle(angle=math.radians(smooth_angle))
        except Exception:
            pass
        ob = bpy.data.objects.new(self.name, me)
        ob.location = self.origin
        scene.collection.objects.link(ob)
        if self.bone:
            vg = ob.vertex_groups.new(name=self.bone)
            vg.add([vv.index for vv in me.vertices], 1.0, 'REPLACE')
        return ob


def frame(t, ref):
    t = t.normalized()
    n = ref - t * ref.dot(t)
    if n.length < 1e-5:
        n = V((1, 0, 0)) - t * t.x
    n.normalize()
    return t, n, t.cross(n).normalized()


def ring_pts(c, n, b, rx, ry, sides, phase=0.0, jag=0.0, seed=0):
    pts = []
    for j in range(sides):
        a = 2 * math.pi * j / sides + phase
        k = 1.0 + jag * noise.noise(V((seed * 1.7, j * 2.3, 0.37 * seed)))
        pts.append(c + n * math.cos(a) * rx * k + b * math.sin(a) * ry * k)
    return pts


def tube(part, pts, radii, sides, mat, ref=V((0, 0, 1)), cap0=True, cap1=True, phase=0.0, jag=0.0, seed=0, mats=None):
    """radii: float or (r_ref, r_side). r_ref runs along the reference direction projected off the axis."""
    pts = [V(p) for p in pts]
    n = len(pts)
    rings, prev = [], V(ref)
    for i, c in enumerate(pts):
        tg = (pts[1] - pts[0]) if i == 0 else (pts[-1] - pts[-2]) if i == n - 1 else (pts[i + 1] - pts[i - 1])
        _, nn, bb = frame(tg, prev)
        prev = nn
        r = radii[i]
        rx, ry = (r, r) if isinstance(r, (int, float)) else r
        rings.append(ring_pts(c, nn, bb, rx, ry, sides, phase, jag, seed + i))
    return part.loft(rings, mat, cap0, cap1, mats=mats)


def tris(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == 'MESH')


def select_only(objs):
    for o in scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def export(path, objs, types=frozenset({'MESH', 'EMPTY'})):
    if not EXPORT or (ONLY and path not in ONLY):
        return
    os.makedirs(OUT, exist_ok=True)
    select_only(objs)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, path), use_selection=True, object_types=set(types),
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
        armature_nodetype='NULL', bake_anim=False, colors_type='NONE')
    print("EXPORTED", path, "tris", tris(objs))


def place(objs, dx):
    """Live / preview only: spread the props out beside the body."""
    if EXPORT and not globals().get("TS_PREVIEW", False):
        return
    for o in objs:
        o.location.x += dx


def ellipse_ring(z, rx, ry, y, sides, a0=0.0, a1=2 * math.pi, closed=True, jag=0.0, seed=0, zjag=0.0):
    """Ring (or open arc a0..a1) around the vertical axis. a = 0 is the character's left (+X), -pi/2 the front (-Y)."""
    ring = []
    n = sides if closed else sides + 1
    for k in range(n):
        a = (a0 + (a1 - a0) * k / sides) + (math.pi / sides if closed else 0.0)
        kk = 1 + jag * noise.noise(V((z * 9.1 + seed, k * 1.3, seed * 0.4)))
        dz = zjag * (1 if k % 2 == 0 else -1) * (0.6 + 0.4 * abs(noise.noise(V((k * 0.7, seed, 1.3)))))
        ring.append(V((math.cos(a) * rx * kk, y + math.sin(a) * ry * kk, z + dz)))
    return ring


# =================================================================================================
# FAIRY FORM (body)
# =================================================================================================
parts = []


def P(name, bone, origin=V((0, 0, 0))):
    p = Part(name, bone, origin)
    parts.append(p)
    return p


# ---- torso: slim, a fitted bodice from the waist up, bare shoulders and collarbones above the neckline
hips = P("Fairy_Hips", "Hips")
hips.loft([ellipse_ring(z, rx, ry, 0.0, 8) for z, rx, ry in ((0.8, 0.13, 0.1), (0.9, 0.155, 0.115), (1.02, 0.13, 0.1))],
          "M_FairySkin")
spine = P("Fairy_Spine", "Spine")
spine.loft([ellipse_ring(z, rx, ry, y, 8) for z, rx, ry, y in ((1.0, 0.128, 0.098, 0.0), (1.1, 0.118, 0.09, 0.0),
                                                                (1.21, 0.135, 0.1, -0.005))], "M_FairyDress")
# a silver sash at the waist
spine.loft([ellipse_ring(1.04, 0.132, 0.102, 0.0, 8), ellipse_ring(1.08, 0.128, 0.098, 0.0, 8)], "M_FairyTrim",
           cap0=False, cap1=False)
CHEST = [(1.19, 0.137, 0.1, -0.005), (1.27, 0.16, 0.115, -0.015), (1.33, 0.165, 0.11, -0.012), (1.38, 0.19, 0.095, 0.0),
         (1.43, 0.16, 0.085, 0.005), (1.47, 0.055, 0.05, 0.0)]
chest = P("Fairy_Chest", "Chest")
chest.loft([ellipse_ring(z, rx, ry, y, 8) for z, rx, ry, y in CHEST], "M_FairyDress",
           mats=lambda i, j: "M_FairyDress" if i < 2 else "M_FairySkin")
for s in (-1, 1):   # straps over the bare shoulders
    tube(chest, [V((s * 0.075, -0.112, 1.33)), V((s * 0.1, -0.05, 1.42)), V((s * 0.1, 0.04, 1.42)), V((s * 0.08, 0.1, 1.33))],
         [0.011, 0.012, 0.012, 0.011], 4, "M_FairyDress")
chest.octa(V((0, -0.128, 1.29)), 0.018, "M_FairyGem", stretch=1.4, axis=2)
neck = P("Fairy_Neck", "Neck")
neck.loft([ellipse_ring(z, r, r, 0.0, 6) for z, r in ((1.44, 0.045), (1.52, 0.04), (1.58, 0.038))], "M_FairySkin")

# ---- the skirt: flares from the hips to a ragged hem at the knee, open underneath (double sided material)
SKIRT = [(1.02, 0.14, 0.108, 0.0, 0.0), (0.9, 0.18, 0.145, 0.0, 0.0), (0.74, 0.235, 0.2, 0.0, 0.0),
         (0.56, 0.285, 0.25, 0.005, 0.045)]
skirt = P("Fairy_Skirt", "Hips")
skirt.loft([ellipse_ring(z, rx, ry, y, 14, jag=0.05 if zj else 0.02, seed=7 + i, zjag=zj) for i, (z, rx, ry, y, zj) in enumerate(SKIRT)],
           "M_FairyDress", cap0=False, cap1=False)
for k in range(6):   # torn tatters past the hem
    a = 2 * math.pi * k / 6 + rng.uniform(-0.2, 0.2)
    top = V((math.cos(a) * 0.27, math.sin(a) * 0.235, 0.58))
    skirt.spike(top, top + V((math.cos(a) * 0.03, math.sin(a) * 0.03, -rng.uniform(0.08, 0.13))), V((-math.sin(a), math.cos(a), 0)),
                0.03, "M_FairyDress", sides=3)

# ---- head: a narrow fae face, almond eyes that glow, a fine nose, long pointed ears
HEAD = [(1.535, 0.028, 0.03, -0.035), (1.575, 0.06, 0.068, -0.012), (1.63, 0.078, 0.088, 0.0), (1.69, 0.083, 0.094, 0.004),
        (1.745, 0.075, 0.084, 0.008), (1.79, 0.042, 0.048, 0.008)]
head = P("Fairy_Head", "Head")
head.loft([ellipse_ring(z, rx, ry, y, 8) for z, rx, ry, y in HEAD], "M_FairySkin")
FACE_Y = -0.09
for s in (-1, 1):
    head.disc(V((s * 0.033, FACE_Y + 0.004, 1.672)), V((s * 0.35, -1, 0.05)), 0.021, "M_FairyHollow", sides=6, depth=0.002, squash=0.55)
    head.disc(V((s * 0.033, FACE_Y + 0.001, 1.672)), V((s * 0.35, -1, 0.05)), 0.014, "M_FairyEye", sides=5, depth=0.003, squash=0.6)
    head.spike(V((s * 0.075, -0.0, 1.66)), V((s * 0.16, 0.035, 1.76)), V((0, 1, 0)), 0.022, "M_FairySkin", sides=3)
head.spike(V((0, FACE_Y - 0.002, 1.662)), V((0, FACE_Y - 0.022, 1.632)), V((1, 0, 0)), 0.01, "M_FairySkin", sides=3)
m = [head.v(V((-0.016, FACE_Y + 0.006, 1.6))), head.v(V((0.016, FACE_Y + 0.006, 1.6))), head.v(V((0.011, FACE_Y + 0.004, 1.594))),
     head.v(V((-0.011, FACE_Y + 0.004, 1.594)))]
head.face([m[0], m[1], m[2], m[3]], "M_FairyHollow")

# ---- hair: a crown with a fringe on the head; the long fall down the back rides the chest (it follows the body)
crown = P("Fairy_Hair", "Head")
crown.loft([ellipse_ring(1.69, 0.092, 0.104, 0.004, 10, jag=0.05, seed=31, zjag=0.015), ellipse_ring(1.745, 0.093, 0.1, 0.006, 10, seed=32),
            ellipse_ring(1.795, 0.072, 0.078, 0.008, 10, seed=33), ellipse_ring(1.83, 0.036, 0.04, 0.008, 10, seed=34),
            ellipse_ring(1.842, 0.01, 0.01, 0.008, 10, seed=35)], "M_FairyHair", cap0=False)
# the back of the head down to the nape: an open shell round the back (head bone)
crown.loft([ellipse_ring(z, rx, ry, y, 9, 0.35, math.pi - 0.35, closed=False, jag=0.05, seed=40 + i)
            for i, (z, rx, ry, y) in enumerate(((1.75, 0.098, 0.106, 0.006), (1.66, 0.1, 0.11, 0.01), (1.57, 0.085, 0.09, 0.03)))],
           "M_FairyHair", closed=False)
for s in (-1, 1):   # locks framing the face, down onto the breast
    tube(crown, [V((s * 0.078, -0.06, 1.72)), V((s * 0.095, -0.06, 1.6)), V((s * 0.12, -0.07, 1.5))], [0.026, 0.03, 0.026], 4,
         "M_FairyHair", ref=V((0, 1, 0)), jag=0.1, seed=50 + s)
fall = P("Fairy_HairFall", "Chest")
FALL = [(1.58, 0.085, 0.07, 0.035, 0.0), (1.46, 0.12, 0.075, 0.06, 0.0), (1.32, 0.15, 0.07, 0.085, 0.0),
        (1.18, 0.14, 0.06, 0.1, 0.0), (1.06, 0.11, 0.05, 0.105, 0.04)]
fa0, fa1 = 0.25, math.pi - 0.25
fall.loft([ellipse_ring(z, rx, ry, y, 10, fa0, fa1, closed=False, jag=0.07, seed=60 + i, zjag=zj) for i, (z, rx, ry, y, zj) in enumerate(FALL)],
          "M_FairyHair", closed=False)
for s in (-1, 1):   # the front locks' ends lie on the breast (chest bone, so they don't swing through the dress)
    tube(fall, [V((s * 0.12, -0.075, 1.5)), V((s * 0.115, -0.11, 1.38)), V((s * 0.1, -0.12, 1.3))], [0.026, 0.022, 0.006], 4,
         "M_FairyHair", ref=V((0, 1, 0)), jag=0.1, seed=70 + s)

# ---- arms: slim and bare, a silver band at each wrist; long fingers
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    sh = P(f"Fairy_{L}Shoulder", L + "Shoulder")
    tube(sh, [(s * 0.13, 0.0, SHOULDER_Z + 0.0), (s * 0.22, 0.0, SHOULDER_Z + 0.012), (s * 0.31, 0.0, SHOULDER_Z)],
         [0.05, 0.052, 0.045], 6, "M_FairySkin", ref=V((0, 0, 1)))
    ua = P(f"Fairy_{L}UpperArm", L + "UpperArm")
    tube(ua, [(s * 0.29, 0, SHOULDER_Z), (s * 0.42, 0.0, SHOULDER_Z), (s * 0.555, 0, SHOULDER_Z)], [0.042, 0.037, 0.03], 6,
         "M_FairySkin", ref=V((0, 0, 1)))
    la = P(f"Fairy_{L}LowerArm", L + "LowerArm")
    tube(la, [(s * 0.54, 0, SHOULDER_Z), (s * 0.65, 0, SHOULDER_Z), (s * 0.76, 0, SHOULDER_Z)], [0.03, 0.026, 0.02], 6,
         "M_FairySkin", ref=V((0, 0, 1)))
    tube(la, [(s * 0.71, 0, SHOULDER_Z), (s * 0.74, 0, SHOULDER_Z)], [0.025, 0.024], 6, "M_FairyTrim", ref=V((0, 0, 1)))
    hand = P(f"Fairy_{L}Hand", L + "Hand")
    tube(hand, [(s * 0.75, 0, SHOULDER_Z), (s * 0.79, 0, SHOULDER_Z - 0.003), (s * 0.835, 0.0, SHOULDER_Z - 0.004)],
         [(0.016, 0.036), (0.017, 0.042), (0.014, 0.04)], 6, "M_FairySkin", ref=V((0, 0, 1)), phase=math.pi / 6)
    for fname, chain in finger_chains(s).items():
        for k, (bn, h, t) in enumerate(chain):
            fp = P(f"Fairy_{bn}", bn)
            h, t = V(h), V(t)
            d = (t - h)
            r = 0.0095 if fname != "Thumb" else 0.011
            tube(fp, [h - d.normalized() * 0.004, t + d.normalized() * 0.002], [r * (1 - 0.15 * k), r * (0.85 - 0.15 * k)],
                 4, "M_FairySkin", ref=V((0, 0, 1)), phase=math.pi / 4, cap0=False, cap1=k == 2)

# ---- legs: bare, slender, toes pointed a little
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    ul = P(f"Fairy_{L}UpperLeg", L + "UpperLeg")
    tube(ul, [(s * HIP_X, 0.0, 0.95), (s * HIP_X, -0.004, 0.72), (s * HIP_X, 0, KNEE_Z - 0.02)], [0.07, 0.06, 0.045], 6,
         "M_FairySkin", ref=V((1, 0, 0)))
    ll = P(f"Fairy_{L}LowerLeg", L + "LowerLeg")
    tube(ll, [(s * HIP_X, 0, KNEE_Z + 0.02), (s * HIP_X, 0.01, 0.33), (s * HIP_X, 0, ANKLE_Z + 0.01)], [0.045, 0.042, 0.027], 6,
         "M_FairySkin", ref=V((1, 0, 0)))
    ft = P(f"Fairy_{L}Foot", L + "Foot")
    X = s * HIP_X
    tube(ft, [V((X, 0.02, ANKLE_Z + 0.01)), V((X, -0.03, 0.05)), V((X, TOE_Y + 0.01, 0.022))], [(0.03, 0.03), (0.034, 0.03), (0.03, 0.016)],
         5, "M_FairySkin", ref=V((1, 0, 0)))
    toes = P(f"Fairy_{L}Toes", L + "Toes")
    tube(toes, [V((X, TOE_Y + 0.015, 0.022)), V((X, TOE_Y - 0.04, 0.014)), V((X, TOE_Y - 0.075, 0.008))], [(0.028, 0.014), (0.024, 0.011), (0.008, 0.005)],
         4, "M_FairySkin", ref=V((1, 0, 0)))

body_objs = [p.finish(smooth_angle=40) for p in parts]
select_only(body_objs)
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "FairyForm"
body.data.name = "FairyForm_Mesh"

arm_data = bpy.data.armatures.new("FairyForm_Armature")
arm_obj = bpy.data.objects.new("Armature", arm_data)
scene.collection.objects.link(arm_obj)
select_only([arm_obj])
bpy.ops.object.mode_set(mode='EDIT')
eb = {}
for name, (h, t, parent) in BONES.items():
    b = arm_data.edit_bones.new(name)
    b.head, b.tail, b.roll = V(h), V(t), 0.0
    eb[name] = b
for name, (h, t, parent) in BONES.items():
    if parent:
        eb[name].parent = eb[parent]
        eb[name].use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
body.parent = arm_obj
mod = body.modifiers.new("Armature", 'ARMATURE')
mod.object = arm_obj
print("FairyForm tris", tris([body]), "bones", len(BONES))
export("FairyForm.fbx", [arm_obj, body], {'ARMATURE', 'MESH'})

# =================================================================================================
# WINGS: flat veined cards fanned out from roots on the upper back (polar UVs like the boss's wings)
# =================================================================================================
WING_ROWS = (0.14, 0.42, 0.72, 1.0)


def wing(name, root, s, phi0, phi1, length, peak, tip_k, sweep, cup=0.05, cols=10):
    """s = +1 for the character's left (+X). phi: 0 straight out to the side, 90 up. Swept back by `sweep` degrees."""
    p = Part(name, None, root)
    sw = math.radians(sweep)
    out = V((s * math.cos(sw), math.sin(sw), 0))      # out to the side and back (+Y is behind)
    up = V((0, 0, 1))
    rv = p.v(root)
    grid = []
    for k in range(cols + 1):
        t = k / cols
        phi = math.radians(phi0 + (phi1 - phi0) * t)
        r = length * (0.32 + 0.68 * math.sin(math.pi * t) ** 0.75) * (1 + tip_k * math.exp(-((t - peak) / 0.1) ** 2))
        col = []
        for v in WING_ROWS:
            q = root + out * (math.cos(phi) * r * v) + up * (math.sin(phi) * r * v) + V((0, cup * v * v * math.sin(math.pi * t), 0))
            col.append(p.v(q))
        grid.append(col)
    for k in range(cols):
        t0, t1 = k / cols, (k + 1) / cols
        p.face([rv, grid[k][0], grid[k + 1][0]], "M_FairyWing", uv=[((t0 + t1) * 0.5, 0.0), (t0, WING_ROWS[0]), (t1, WING_ROWS[0])])
        for j in range(len(WING_ROWS) - 1):
            p.face([grid[k][j], grid[k][j + 1], grid[k + 1][j + 1], grid[k + 1][j]], "M_FairyWing",
                   uv=[(t0, WING_ROWS[j]), (t0, WING_ROWS[j + 1]), (t1, WING_ROWS[j + 1]), (t1, WING_ROWS[j])])
    return p.finish(smooth_angle=180, weld=False)


wing_objs = []
for s in (1, -1):
    L = "L" if s > 0 else "R"
    wing_objs.append(wing(f"FWing_U_{L}", V((s * 0.035, 0.1, 1.36)), s, -8, 82, 0.72, 0.6, 0.3, 24))
    wing_objs.append(wing(f"FWing_D_{L}", V((s * 0.035, 0.095, 1.28)), s, -80, -6, 0.5, 0.38, 0.45, 32, cup=0.04))
print("FairyFormWings tris", tris(wing_objs))
export("FairyFormWings.fbx", wing_objs)

# =================================================================================================
# DAGGER: grip at the origin, blade along +Z. A leaf-shaped bone-white blade, a crescent-moon guard.
# =================================================================================================
dag = Part("FairyDagger")
# blade: a flat diamond section that swells into a leaf, then tapers to a needle point
BL = [(0.045, 0.012, 0.004), (0.09, 0.019, 0.0055), (0.16, 0.023, 0.006), (0.23, 0.017, 0.005), (0.285, 0.008, 0.003),
      (0.315, 0.0015, 0.001)]
rings = []
for z, w, th in BL:
    rings.append([V((w, 0, z)), V((0, th, z)), V((-w, 0, z)), V((0, -th, z))])
dag.loft(rings, "M_FairyBlade", cap0=True, cap1=True)
# fuller: a pale blue groove running up the middle of each face
for sgn in (-1, 1):
    dag.loft([[V((-0.004, sgn * 0.0058, 0.06)), V((0.004, sgn * 0.0058, 0.06))], [V((-0.003, sgn * 0.0062, 0.2)), V((0.003, sgn * 0.0062, 0.2))]],
             "M_FairyGem", closed=False)
# guard: a small crescent moon, horns curling up toward the blade
gpts = [V((math.sin(a) * 0.05, 0, 0.04 - math.cos(a) * 0.016)) for a in (math.radians(d) for d in range(-80, 81, 20))]
tube(dag, gpts, [0.004, 0.006, 0.008, 0.009, 0.01, 0.009, 0.008, 0.006, 0.004], 4, "M_FairyTrim", ref=V((0, 1, 0)))
# grip: wrapped in pale blue cloth
tube(dag, [V((0, 0, 0.032)), V((0, 0, -0.03)), V((0, 0, -0.075))], [0.0115, 0.0125, 0.011], 6, "M_FairyGrip")
dag.octa(V((0, 0, -0.088)), 0.013, "M_FairyGem", stretch=1.3, axis=2)
dag_ob = dag.finish(smooth_angle=30)
export("FairyDagger.fbx", [dag_ob])
place([dag_ob], 0.9)

# =================================================================================================
# CHAKRAM: a pink crescent blade. Flat in XY (origin = circle centre), spins about +Z.
# =================================================================================================
ch = Part("FairyChakram")
R, WMAX, TH = 0.27, 0.075, 0.011
A0, A1 = math.radians(-145), math.radians(145)      # measured from straight down (-Y is "down" in its plane here)
N = 26
cross = []
for i in range(N + 1):
    t = i / N
    a = A0 + (A1 - A0) * t
    d = V((math.sin(a), -math.cos(a), 0))                # around the circle, starting from the bottom
    w = WMAX * (math.sin(math.pi * t) ** 0.85) + 0.002
    th = TH * (0.4 + 0.6 * math.sin(math.pi * t) ** 0.6)
    ro, ri = R, R - w
    rm = (ro + ri) * 0.5
    cross.append((t, [d * ro, d * rm + V((0, 0, th)), d * ri, d * rm - V((0, 0, th))]))
verts = [[ch.v(p) for p in pts] for _, pts in cross]
for i in range(N):
    t0, t1 = cross[i][0], cross[i + 1][0]
    for j in range(4):
        # polar UVs: u along the crescent, v across the band (outer edge 0 .. inner edge 0.5 .. back to the outer edge)
        v0, v1 = j / 4.0, (j + 1) / 4.0
        ch.face([verts[i][j], verts[i + 1][j], verts[i + 1][(j + 1) % 4], verts[i][(j + 1) % 4]], "M_FairyChakram",
                uv=[(t0, v0), (t1, v0), (t1, v1), (t0, v1)])
# a wrapped grip on the inner edge at the bottom middle, where the hand holds it
gc = V((0, -(R - WMAX - 0.004), 0))
tube(ch, [gc + V((-0.05, 0.004, 0)), gc + V((0, -0.004, 0)), gc + V((0.05, 0.004, 0))], [0.012, 0.013, 0.012], 6, "M_FairyGrip",
     ref=V((0, 0, 1)))
ch_ob = ch.finish(smooth_angle=35, weld=False)
grip = bpy.data.objects.new("Grip", None)
grip.location = gc
scene.collection.objects.link(grip)
grip.parent = ch_ob
export("FairyChakram.fbx", [ch_ob, grip])
place([ch_ob], 1.5)

# =================================================================================================
# MASK (reward item): a porcelain fae face, pointed ears, glowing eyes, a white fringe, little temple wings
# =================================================================================================
mask = Part("FairyMask")
sh = mask_shell.Shell(profile=[(0.0, 0.04), (0.1, 0.09), (0.25, 0.138), (0.45, 0.166), (0.65, 0.176), (0.82, 0.17),
                               (0.93, 0.146), (1.0, 0.096)], z0=-0.24, z1=0.2, y0=-0.06, crown=0.75)
sh.build(mask, "M_FairySkin", strap="M_FairyTrim")
for s in (-1, 1):
    mask_shell.socket(mask, sh, s * 0.068, 0.035, 0.044, "M_FairyHollow", "M_FairyEye", eye_r=0.024, squash=0.55,
                      tilt=s * 0.22)
    mask_shell.band(mask, sh, [(s * 0.072, 0.0), (s * 0.075, -0.035), (s * 0.08, -0.07)], [0.008, 0.006, 0.002], "M_FairyHollow")
    e = sh.rim(0.56, s)                                   # long pointed ears
    mask.spike(e + V((-s * 0.01, 0.01, 0)), e + V((s * 0.13, 0.05, 0.11)), V((0, 1, 0)), 0.03, "M_FairySkin", sides=3)
mask.spike(sh.at(0, 0.0, 0.002), sh.at(0, -0.04, 0.018), V((1, 0, 0)), 0.012, "M_FairySkin", sides=3)
mask_shell.band(mask, sh, [(-0.03, -0.135), (0.0, -0.128), (0.03, -0.135)], [0.008, 0.014, 0.008], "M_FairyChakram")
tube(mask, [sh.at(x, 0.155 - 1.4 * x * x, 0.012) for x in (-0.14, -0.1, -0.05, 0.0, 0.05, 0.1, 0.14)],
     [0.02, 0.026, 0.03, 0.032, 0.03, 0.026, 0.02], 4, "M_FairyHair", jag=0.14, seed=301)
mask.octa(sh.at(0, 0.098, 0.014), 0.016, "M_FairyGem", stretch=1.4, axis=2)
mask_ob = mask.finish(weld=False)
mwings = []
for s in (1, -1):
    L = "L" if s > 0 else "R"
    mwings.append(wing(f"MaskWing_{L}", sh.rim(0.78, s) + V((0, 0.02, 0)), s, 0, 80, 0.17, 0.6, 0.3, 30, cup=0.01, cols=6))
select_only([mask_ob] + mwings)
bpy.context.view_layer.objects.active = mask_ob
bpy.ops.object.join()
mask_ob = bpy.context.view_layer.objects.active
mask_ob.name = "FairyMask"
export("FairyMask.fbx", [mask_ob])
place([mask_ob], 2.1)
print("OUT", OUT if EXPORT else "(no export)")
