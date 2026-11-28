"""
Alligator Mask set, PSX dark fantasy (Docs/ART_DIRECTION.md). Everything the Crocodile reward needs:

    blender -b --factory-startup --python Tools/Blender/build_croc_form.py -- <abs out_dir>

CrocForm.fbx    - the humanoid alligator the player turns into (~2.3k tris). The skeleton is the Elder
                  Tree's (Human Mega Animations Pack HumanM x 0.95, same bone names and rest pose), so the
                  same Humanoid clips, grip sockets and IK numbers work. PS1-style rigid skinning: every
                  part is bound 100% to one bone and parts overlap at the joints. It wears the bomber's
                  look: croc hide, bone teeth, toxic eyes, a leather aviator cap with goggles, a riveted
                  aluminium pauldron and a bandolier of little drop bombs.
CrocHammer.fbx  - two-handed war hammer whose head is a finned drop bomb with a toothed croc nose
                  (explodes on every hit). Origin = grip, shaft along +Z (Unity +Y), like the club.
CrocCannon.fbx  - shoulder cannon built from a riveted engine nacelle with croc jaws for a muzzle and a
                  little three-blade prop at the back. Origin = grip, muzzle along -Y (Unity +Z).
                  Empty "Muzzle" marks where shells leave.
CrocMask.fbx    - the mask item: a carved croc face with teeth and glowing eyes. Origin = face centre,
                  front along -Y (Unity +Z).
Boulder.fbx, CrackedWall.fbx, RockChunks.fbx (RockChunk0-5) - breakable obstacles and their debris.

Materials (remapped by name in Unity): the boss set (M_CrocHide, M_CrocTeeth, M_CrocMouth, M_CrocEye,
M_BomberMetal, M_BomberGlass, M_BombIron, M_BombBand, M_Iron) plus M_CrocBelly, M_Leather,
M_CrackedStone. UVs are box-projected in metres (uniform texel density), like the boss.
Conventions: Z up, the character faces -Y, its LEFT side is +X.
"""
import bpy
import bmesh
import math
import os
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
    scene = bpy.data.scenes.get("CrocForm") or bpy.data.scenes.new("CrocForm")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    try:
        bpy.context.window.scene = scene
    except Exception:
        pass

MAT_RGB = {
    "M_CrocHide": (0.20, 0.25, 0.14), "M_CrocBelly": (0.42, 0.40, 0.26), "M_CrocMouth": (0.25, 0.08, 0.08),
    "M_CrocTeeth": (0.62, 0.57, 0.46), "M_CrocEye": (0.75, 0.70, 0.20), "M_BomberMetal": (0.30, 0.31, 0.29),
    "M_BomberGlass": (0.10, 0.09, 0.16), "M_Iron": (0.12, 0.11, 0.11), "M_BombIron": (0.17, 0.18, 0.13),
    "M_BombBand": (0.35, 0.09, 0.07), "M_Leather": (0.22, 0.14, 0.09), "M_CrackedStone": (0.30, 0.29, 0.30),
}
# metres per texture tile (box projection)
TILE = {"M_CrocHide": 0.9, "M_CrocBelly": 0.7, "M_CrocMouth": 0.6, "M_CrocTeeth": 0.4, "M_BomberMetal": 1.6,
        "M_Iron": 1.0, "M_BombIron": 0.6, "M_BombBand": 0.6, "M_Leather": 0.8, "M_CrackedStone": 2.4}
mask_shell.register(MAT_RGB, TILE)
FLAT = ("M_CrocEye", "M_BomberGlass")

# ----------------------------------------------------------------------------------------------- skeleton
# Identical to build_elder_tree.py (HumanM x 0.95). Keep these in sync: sockets in Unity rely on them.
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
    """One mesh piece assembled with bmesh. bone: rigid skin binding (100% to that bone)."""

    def __init__(self, name, bone=None, origin=V((0, 0, 0))):
        self.name, self.bone, self.origin = name, bone, V(origin)
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def v(self, co):
        return self.bm.verts.new(V(co) - self.origin)

    def face(self, verts, mat):
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = self.mi(mat)
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

    def box(self, c, size, mat, rot=None):
        c, (sx, sy, sz) = V(c), size
        corners = [V((x * sx / 2, y * sy / 2, z * sz / 2)) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
        if rot is not None:
            corners = [rot @ p for p in corners]
        vs = [self.v(c + p) for p in corners]
        for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.face([vs[i] for i in q], mat)

    def finish(self, smooth_angle=35):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        uvl = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            mat = self.mats[f.material_index]
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
            vg.add([v.index for v in me.vertices], 1.0, 'REPLACE')
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
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


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
    print("EXPORTED", path, "tris", tris([o for o in objs if o.type == 'MESH']))


def belly(sides, phase=0.0, front=-0.55):
    """Material callback for torso tubes along +Z: faces looking forward (-Y) get the pale belly scutes."""
    def fn(i, j):
        if i < 0:
            return "M_CrocHide"
        a = 2 * math.pi * (j + 0.5) / sides + phase
        return "M_CrocBelly" if math.sin(a) < front else "M_CrocHide"
    return fn


def scute_row(part, pts, height, width, lean=V((0, 0.35, 1))):
    """Osteoderm ridge: a row of little pyramids standing on the given points."""
    for k, p in enumerate(pts):
        p = V(p)
        h = height * (0.75 + 0.25 * ((k * 7) % 3) / 2)
        part.spike(p, p + lean.normalized() * h, V((1, 0, 0)), width, "M_CrocHide")


def bomb(part, c, axis, length, radius, sides=6, band=True, fins=True):
    """Little finned drop bomb (the boss's ordnance) along axis, centred on c."""
    c, ax = V(c), V(axis).normalized()
    prof = [(-0.5, 0.35), (-0.36, 0.8), (-0.1, 1.0), (0.22, 1.0), (0.4, 0.78), (0.5, 0.35), (0.54, 0.08)]
    pts = [c + ax * f * length for f, _ in prof]
    part_t = tube(part, pts, [r * radius for _, r in prof], sides, "M_BombIron", ref=ax.orthogonal())
    if band:
        tube(part, [c + ax * 0.05 * length, c + ax * 0.13 * length], [radius * 1.04] * 2, sides, "M_BombBand",
             ref=ax.orthogonal(), cap0=False, cap1=False)
    if fins:
        o1 = ax.orthogonal().normalized()
        o2 = ax.cross(o1).normalized()
        for d in (o1, o2, -o1, -o2):
            base = c - ax * 0.42 * length
            quad = [base + d * radius * 0.4, base - ax * 0.2 * length + d * radius * 0.4,
                    base - ax * 0.2 * length + d * radius * 1.35, base - ax * 0.05 * length + d * radius * 1.3]
            th = ax.cross(d).normalized() * 0.006
            part.loft([[q + th for q in quad], [q - th for q in quad]], "M_BombIron")
    return part_t


# =================================================================================================
# CROC FORM
# =================================================================================================
parts = []


def P(name, bone):
    p = Part(name, bone)
    parts.append(p)
    return p


# ---- torso: hips / spine / chest / neck, 10-sided, broad barrel chest, pale belly plates in front
TS = 10
torso = [
    ("Hips", [(0.76, 0.17, 0.15, 0.02), (0.84, 0.22, 0.19, 0.01), (0.95, 0.24, 0.205, 0.0), (1.05, 0.245, 0.205, 0.0)]),
    ("Spine", [(1.0, 0.245, 0.205, 0.0), (1.11, 0.255, 0.215, -0.01), (1.23, 0.275, 0.225, -0.015)]),
    ("Chest", [(1.19, 0.275, 0.225, -0.015), (1.30, 0.305, 0.245, -0.02), (1.40, 0.30, 0.235, -0.02),
               (1.48, 0.245, 0.20, -0.02), (1.53, 0.15, 0.145, -0.02)]),
    ("Neck", [(1.45, 0.155, 0.15, -0.03), (1.55, 0.135, 0.14, -0.04), (1.64, 0.125, 0.13, -0.05)]),
]
for bone, rings in torso:
    p = P("Croc_" + bone, bone)
    tube(p, [(0, y, z) for z, _, _, y in rings], [(rx, ry) for _, rx, ry, _ in rings], TS, "M_CrocHide",
         ref=V((1, 0, 0)), phase=math.pi / TS, mats=belly(TS, math.pi / TS), jag=0.04, seed=len(parts))


def torso_surface(z, side_angle):
    """Point on the torso surface at height z, angle around (0 = +X, -pi/2 = front)."""
    for bone, rings in torso:
        for (z0, rx0, ry0, y0), (z1, rx1, ry1, y1) in zip(rings, rings[1:]):
            if z0 <= z <= z1:
                t = (z - z0) / (z1 - z0)
                rx, ry, y = rx0 + (rx1 - rx0) * t, ry0 + (ry1 - ry0) * t, y0 + (y1 - y0) * t
                return V((math.cos(side_angle) * rx, y + math.sin(side_angle) * ry, z))
    return V((0, 0, z))


# back ridge: two rows of scutes down the spine
for bone, z0, z1, n in (("Chest", 1.24, 1.48, 4), ("Spine", 1.02, 1.2, 3), ("Hips", 0.84, 0.98, 2)):
    p = Part("Croc_Scutes" + bone, bone)
    parts.append(p)
    for s in (-1, 1):
        pts = []
        for k in range(n):
            z = z0 + (z1 - z0) * k / max(1, n - 1)
            q = torso_surface(z, math.pi / 2 + s * 0.28)
            pts.append(q - V((0, 0.01, 0)))
        scute_row(p, pts, 0.055, 0.03, V((s * 0.25, 1.0, 0.45)))

# belt + buckle (leather, rusted iron) and a bandolier of drop bombs across the chest
belt = P("Croc_Belt", "Hips")
tube(belt, [(0, 0.005, 0.90), (0, 0.005, 0.965)], [(0.252, 0.215)] * 2, TS, "M_Leather", ref=V((1, 0, 0)),
     phase=math.pi / TS, cap0=False, cap1=False)
belt.box((0, -0.222, 0.932), (0.085, 0.02, 0.07), "M_Iron")
strap = P("Croc_Bandolier", "Chest")
sp = [torso_surface(1.47, 0.35), torso_surface(1.38, -0.55), torso_surface(1.28, -1.25), torso_surface(1.20, -2.0),
      torso_surface(1.20, -2.6)]
sp = [q + (q - V((0, q.y * 0.0, q.z))).normalized() * 0.0 for q in sp]
for a, b in zip(sp, sp[1:]):
    na = V((a.x, a.y, 0)).normalized() * 0.018
    nb = V((b.x, b.y, 0)).normalized() * 0.018
    up = V((0, 0, 0.032))
    strap.loft([[a + na - up, a + na + up], [b + nb - up, b + nb + up]], "M_Leather", closed=False)
for k, (q0, q1) in enumerate(zip(sp[1:4], sp[2:5])):
    c = (q0 + q1) * 0.5
    c += V((c.x, c.y, 0)).normalized() * 0.055
    bomb(strap, c, V((0, 0, -1)), 0.15, 0.036, sides=6, fins=k == 1)

# ---- head: long croc skull on the Head bone, aviator cap + goggles (the bomber's pilot)
head = P("Croc_Head", "Head")
HY = [  # y (forward is -), z centre, vertical radius, horizontal radius
    (0.13, 1.66, 0.085, 0.10), (0.06, 1.685, 0.125, 0.135), (-0.04, 1.685, 0.13, 0.145), (-0.13, 1.665, 0.105, 0.135),
    (-0.22, 1.645, 0.07, 0.115), (-0.36, 1.635, 0.055, 0.095), (-0.50, 1.632, 0.048, 0.085), (-0.58, 1.634, 0.05, 0.088),
    (-0.63, 1.63, 0.035, 0.06)]
tube(head, [(0, y, z) for y, z, _, _ in HY], [(v, h) for _, _, v, h in HY], 8, "M_CrocHide", ref=V((0, 0, 1)),
     phase=math.pi / 8, jag=0.05, seed=31)
jaw = P("Croc_Jaw", "Head")
JY = [(0.09, 1.585, 0.06, 0.11), (-0.04, 1.565, 0.055, 0.12), (-0.2, 1.545, 0.04, 0.1), (-0.38, 1.53, 0.032, 0.085),
      (-0.52, 1.525, 0.03, 0.075), (-0.58, 1.53, 0.022, 0.05)]
tube(jaw, [(0, y, z) for y, z, _, _ in JY], [(v, h) for _, _, v, h in JY], 6, "M_CrocHide", ref=V((0, 0, 1)),
     phase=math.pi / 6, jag=0.04, seed=37)
# mouth interior (dark flesh) between the jaws
jaw.loft([[V((-0.1, 0.06, 1.59)), V((0.1, 0.06, 1.59))], [V((-0.07, -0.5, 1.565)), V((0.07, -0.5, 1.565))]],
         "M_CrocMouth", closed=False)
# teeth: interlocking, the 4th lower tooth sticks out like a real croc
for s in (-1, 1):
    for k in range(7):
        y = -0.14 - k * 0.065
        w = 0.08 - k * 0.003
        if k % 2 == 0:
            head.spike((s * w, y, 1.598), (s * (w + 0.008), y - 0.006, 1.552), V((0, 1, 0)), 0.011, "M_CrocTeeth")
        else:
            jaw.spike((s * (w - 0.006), y, 1.552), (s * (w + 0.004), y - 0.004, 1.598 + (0.02 if k == 3 else 0)),
                      V((0, 1, 0)), 0.011, "M_CrocTeeth")
    # brow ridge + glowing eye
    head.spike((s * 0.085, -0.10, 1.76), (s * 0.12, -0.02, 1.81), V((0, 1, 0)), 0.035, "M_CrocHide", sides=4)
    tube(head, [(s * 0.083, -0.135, 1.765), (s * 0.083, -0.16, 1.765)], [0.03, 0.022], 6, "M_CrocEye", ref=V((0, 0, 1)))
    # nostril bumps on the snout tip
    tube(head, [(s * 0.03, -0.55, 1.675), (s * 0.03, -0.6, 1.672)], [0.022, 0.015], 5, "M_CrocHide", ref=V((0, 0, 1)))
    # ear flaps of the cap
    head.loft([[V((s * 0.15, 0.04, 1.70)), V((s * 0.15, -0.06, 1.70))],
               [V((s * 0.165, 0.03, 1.58)), V((s * 0.16, -0.04, 1.6))]], "M_Leather", closed=False)
# aviator cap: leather shell over the cranium (open underneath)
cap_rings = []
for y, r in ((0.15, 0.09), (0.09, 0.14), (0.0, 0.155), (-0.08, 0.145)):
    cap_rings.append([V((math.cos(a) * r * 1.02, y, 1.685 + math.sin(a) * r * 0.97))
                      for a in (math.radians(d) for d in range(-25, 206, 23))])
head.loft(cap_rings, "M_Leather", closed=False)
# goggles pushed up on the cap (iron rims, dark glass) + strap
for s in (-1, 1):
    c = V((s * 0.055, -0.07, 1.825))
    d = V((0, -0.8, 0.6)).normalized()
    tube(head, [c - d * 0.02, c + d * 0.03], [0.034, 0.034], 8, "M_Iron", ref=V((1, 0, 0)), cap1=False)
    tube(head, [c + d * 0.025, c + d * 0.031], [0.027, 0.027], 8, "M_BomberGlass", ref=V((1, 0, 0)))
head.box((0, -0.07, 1.84), (0.04, 0.03, 0.02), "M_Iron")
# head scutes running back over the neck
scute_row(head, [(0, 0.12, 1.745), (0, 0.16, 1.70)], 0.04, 0.025, V((0, 1, 0.6)))

# ---- tail: tall and narrow, rigid on the hips, scute crest on top
tail = P("Croc_Tail", "Hips")
TP = [(0.14, 0.92, 0.15, 0.13), (0.36, 0.80, 0.13, 0.105), (0.58, 0.60, 0.105, 0.08), (0.80, 0.38, 0.08, 0.06),
      (0.98, 0.20, 0.055, 0.04), (1.12, 0.10, 0.032, 0.025), (1.22, 0.06, 0.012, 0.01)]
tube(tail, [(0, y, z) for y, z, _, _ in TP], [(v, h) for _, _, v, h in TP], 6, "M_CrocHide", ref=V((0, 0, 1)),
     phase=math.pi / 6, jag=0.06, seed=51, cap0=False)
for k in range(5):
    t = 0.15 + k * 0.17
    i = min(len(TP) - 2, int(t * (len(TP) - 1)))
    f = t * (len(TP) - 1) - i
    y = TP[i][0] + (TP[i + 1][0] - TP[i][0]) * f
    z = TP[i][1] + (TP[i + 1][1] - TP[i][1]) * f
    v = TP[i][2] + (TP[i + 1][2] - TP[i][2]) * f
    for s in (-1, 1):
        tail.spike((s * 0.025, y, z + v * 0.85), (s * 0.03, y + 0.05, z + v + 0.05 - k * 0.006), V((1, 0, 0)), 0.022,
                   "M_CrocHide")

# ---- arms
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    sh = P(f"Croc_{L}Shoulder", L + "Shoulder")
    if s > 0:
        # riveted bomber-aluminium pauldron on the left shoulder (the right one is bare: asymmetry reads)
        rings = []
        for x, r in ((0.17, 0.12), (0.25, 0.15), (0.33, 0.145), (0.39, 0.10)):
            rings.append([V((x, math.cos(a) * r * 0.95, SHOULDER_Z + 0.015 + math.sin(a) * r))
                          for a in (math.radians(d) for d in range(-35, 216, 28))])
        sh.loft(rings, "M_BomberMetal", closed=False)
        for k in range(3):
            sh.spike((0.24 + k * 0.05, 0.0, SHOULDER_Z + 0.155), (0.24 + k * 0.05, 0.02, SHOULDER_Z + 0.2),
                     V((1, 0, 0)), 0.014, "M_Iron")
    else:
        tube(sh, [(-0.16, 0, SHOULDER_Z), (-0.3, 0, SHOULDER_Z)], [(0.12, 0.11), (0.11, 0.1)], 6, "M_CrocHide",
             ref=V((0, 0, 1)), phase=math.pi / 6)
    ua = P(f"Croc_{L}UpperArm", L + "UpperArm")
    tube(ua, [(s * 0.27, 0, SHOULDER_Z), (s * 0.40, 0, SHOULDER_Z + 0.005), (s * 0.565, 0, SHOULDER_Z)],
         [(0.092, 0.088), (0.1, 0.092), (0.074, 0.07)], 6, "M_CrocHide", ref=V((0, 0, 1)), phase=math.pi / 6,
         jag=0.05, seed=60 + s)
    ua.spike((s * 0.42, 0.03, SHOULDER_Z + 0.09), (s * 0.45, 0.06, SHOULDER_Z + 0.13), V((1, 0, 0)), 0.025, "M_CrocHide")
    la = P(f"Croc_{L}LowerArm", L + "LowerArm")
    tube(la, [(s * 0.525, 0, SHOULDER_Z), (s * 0.64, 0, SHOULDER_Z), (s * 0.77, 0, SHOULDER_Z)],
         [(0.078, 0.074), (0.074, 0.068), (0.055, 0.05)], 6, "M_CrocHide", ref=V((0, 0, 1)), phase=math.pi / 6,
         jag=0.05, seed=70 + s)
    # leather bracer on the forearm
    tube(la, [(s * 0.66, 0, SHOULDER_Z), (s * 0.74, 0, SHOULDER_Z)], [(0.07, 0.066), (0.062, 0.058)], 6,
         "M_Leather", ref=V((0, 0, 1)), phase=math.pi / 6, cap0=False, cap1=False)
    for k in range(2):
        la.spike((s * (0.58 + k * 0.07), 0.04, SHOULDER_Z + 0.06), (s * (0.6 + k * 0.07), 0.08, SHOULDER_Z + 0.1),
                 V((1, 0, 0)), 0.02, "M_CrocHide")
    # palm: a chunky webbed block
    hand = P(f"Croc_{L}Hand", L + "Hand")
    tube(hand, [(s * 0.745, 0, SHOULDER_Z), (s * 0.79, 0, SHOULDER_Z - 0.003), (s * 0.84, 0.0, SHOULDER_Z - 0.005)],
         [(0.03, 0.05), (0.032, 0.058), (0.026, 0.056)], 6, "M_CrocHide", ref=V((0, 0, 1)), phase=math.pi / 6)
    # fingers: each phalanx is its own rigid piece, the distal one carries a bone claw
    for fname, chain in finger_chains(s).items():
        if fname == "Little":
            continue
        for k, (bn, h, t) in enumerate(chain):
            fp = P(f"Croc_{bn}", bn)
            h, t = V(h), V(t)
            d = (t - h)
            r = 0.019 if fname != "Thumb" else 0.021
            tube(fp, [h - d.normalized() * 0.006, t + d.normalized() * 0.004], [r * (1 - 0.15 * k), r * (0.85 - 0.15 * k)],
                 4, "M_CrocHide", ref=V((0, 0, 1)), phase=math.pi / 4)
            if k == 2:
                fp.spike(t, t + d.normalized() * 0.04 + V((0, 0, -0.012)), V((0, 0, 1)), 0.011, "M_CrocTeeth")

# ---- legs: thick croc thighs, scaled shins, splayed clawed feet
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    ul = P(f"Croc_{L}UpperLeg", L + "UpperLeg")
    tube(ul, [(s * HIP_X, 0.0, 0.96), (s * (HIP_X + 0.015), -0.005, 0.72), (s * HIP_X, 0, KNEE_Z - 0.03)],
         [(0.13, 0.135), (0.125, 0.12), (0.09, 0.088)], 7, "M_CrocHide", ref=V((1, 0, 0)), jag=0.05, seed=80 + s)
    ll = P(f"Croc_{L}LowerLeg", L + "LowerLeg")
    tube(ll, [(s * HIP_X, 0, KNEE_Z + 0.03), (s * HIP_X, 0.01, 0.31), (s * HIP_X, 0, ANKLE_Z - 0.01)],
         [(0.09, 0.09), (0.085, 0.08), (0.06, 0.058)], 6, "M_CrocHide", ref=V((1, 0, 0)), jag=0.05, seed=90 + s)
    ll.spike((s * (HIP_X + 0.03), -0.06, 0.5), (s * (HIP_X + 0.05), -0.1, 0.55), V((1, 0, 0)), 0.025, "M_CrocHide")
    ft = P(f"Croc_{L}Foot", L + "Foot")
    ft.loft([[V((s * HIP_X + dx, 0.07, dz)) for dx, dz in ((-0.06, 0.0), (0.06, 0.0), (0.05, 0.12), (-0.05, 0.12))],
             [V((s * HIP_X + dx, -0.04, dz)) for dx, dz in ((-0.085, 0.0), (0.085, 0.0), (0.07, 0.09), (-0.07, 0.09))],
             [V((s * HIP_X + dx, TOE_Y - 0.01, dz)) for dx, dz in ((-0.09, 0.0), (0.09, 0.0), (0.075, 0.05), (-0.075, 0.05))]],
            "M_CrocHide")
    toes = P(f"Croc_{L}Toes", L + "Toes")
    for c in (-1, 0, 1):
        b = V((s * HIP_X + c * 0.055, TOE_Y - 0.005, 0.025))
        tip = b + V((c * 0.025, -0.1, -0.01))
        tube(toes, [b, tip], [(0.026, 0.03), (0.018, 0.02)], 4, "M_CrocHide", ref=V((0, 0, 1)), phase=math.pi / 4)
        toes.spike(tip, tip + V((c * 0.01, -0.045, -0.015)), V((0, 0, 1)), 0.012, "M_CrocTeeth")

croc_objs = [p.finish(smooth_angle=40) for p in parts]

# join into one skinned mesh
select_only(croc_objs)
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "CrocForm"
body.data.name = "CrocForm_Mesh"

arm_data = bpy.data.armatures.new("CrocForm_Armature")
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
print("CrocForm tris", tris([body]), "bones", len(BONES))
export("CrocForm.fbx", [arm_obj, body], {'ARMATURE', 'MESH'})
OFFSET_X = 0.0


def place(objs, dx):
    """Live preview: line the props up next to the character (export positions stay at the origin)."""
    if EXPORT:
        return
    for o in objs:
        o.location.x += dx


# =================================================================================================
# HAMMER: shaft along +Z, origin at the grip. Head = finned drop bomb with a toothed croc nose.
# =================================================================================================
hm = Part("CrocHammer")
tube(hm, [(0, 0, -0.2), (0, 0, 0.18), (0, 0, 0.62), (0, 0, 0.98)], [0.032, 0.03, 0.03, 0.034], 6, "M_CrocHide",
     ref=V((1, 0, 0)), phase=math.pi / 6)
tube(hm, [(0, 0, -0.16), (0, 0, 0.16)], [0.037, 0.037], 6, "M_Leather", ref=V((1, 0, 0)), phase=math.pi / 6,
     cap0=False, cap1=False)
hm.spike((0, 0, -0.2), (0, 0, -0.3), V((1, 0, 0)), 0.04, "M_Iron", sides=4)       # pommel spike
tube(hm, [(0, 0, 0.88), (0, 0, 1.0)], [0.05, 0.05], 6, "M_Iron", ref=V((1, 0, 0)), phase=math.pi / 6)   # collar
HEAD_Z_H = 1.08
bomb(hm, (0.0, 0.0, HEAD_Z_H), V((1, 0, 0)), 0.66, 0.15, sides=8)                  # nose toward +X (strike face)
# croc teeth crown around the bomb nose and toxic eyes on top: it is alive, like the walkers
for k in range(8):
    a = k * math.pi / 4 + math.pi / 8
    d = V((0, math.cos(a), math.sin(a)))
    base = V((0.25, 0, HEAD_Z_H)) + d * 0.125
    hm.spike(base, base + V((0.07, 0, 0)) + d * 0.03, d, 0.018, "M_CrocTeeth")
for s in (-1, 1):
    tube(hm, [(0.13, s * 0.07, HEAD_Z_H + 0.13), (0.15, s * 0.075, HEAD_Z_H + 0.14)], [0.026, 0.02], 6, "M_CrocEye",
         ref=V((0, 0, 1)))
    hm.spike((0.06, s * 0.09, HEAD_Z_H + 0.12), (0.17, s * 0.1, HEAD_Z_H + 0.17), V((0, 0, 1)), 0.022, "M_CrocHide")
scute_row(hm, [(-0.05, 0, HEAD_Z_H + 0.15), (-0.12, 0, HEAD_Z_H + 0.14)], 0.05, 0.025, V((0, 0, 1)))
hammer = hm.finish(smooth_angle=30)
place([hammer], 1.4)
export("CrocHammer.fbx", [hammer], {'MESH'})

# =================================================================================================
# CANNON: muzzle along -Y, origin at the grip. A riveted engine nacelle with croc jaws for a muzzle.
# =================================================================================================
cn = Part("CrocCannon")
BZ = 0.13      # barrel axis height above the grip
NAC = [(0.58, 0.07), (0.50, 0.10), (0.30, 0.12), (-0.25, 0.12), (-0.48, 0.105), (-0.56, 0.11)]
tube(cn, [(0, y, BZ) for y, _ in NAC], [r for _, r in NAC], 8, "M_BomberMetal", ref=V((0, 0, 1)), phase=math.pi / 8,
     cap1=False)
tube(cn, [(0, -0.47, BZ), (0, -0.56, BZ)], [0.118, 0.118], 8, "M_Iron", ref=V((0, 0, 1)), phase=math.pi / 8,
     cap0=False, cap1=False)                                                        # cowling ring
tube(cn, [(0, -0.4, BZ), (0, -0.62, BZ)], [0.055, 0.055], 6, "M_Iron", ref=V((0, 0, 1)), cap0=False, cap1=False)  # bore
# croc jaws around the bore
upper = [(-0.52, 0.11, 0.1), (-0.66, 0.07, 0.085), (-0.8, 0.055, 0.06), (-0.86, 0.04, 0.04)]
tube(cn, [(0, y, BZ + 0.06 + 0.012 * k) for k, (y, _, _) in enumerate(upper)], [(v * 0.5, h) for _, v, h in upper], 6,
     "M_CrocHide", ref=V((0, 0, 1)), phase=math.pi / 6, jag=0.05, seed=7)
lower = [(-0.52, 0.09, 0.09), (-0.66, 0.05, 0.075), (-0.78, 0.04, 0.05)]
tube(cn, [(0, y, BZ - 0.07 - 0.02 * k) for k, (y, _, _) in enumerate(lower)], [(v * 0.45, h) for _, v, h in lower], 6,
     "M_CrocHide", ref=V((0, 0, 1)), phase=math.pi / 6, jag=0.05, seed=9)
for s in (-1, 1):
    for k in range(4):
        y = -0.6 - k * 0.055
        cn.spike((s * (0.07 - k * 0.006), y, BZ + 0.045), (s * (0.072 - k * 0.006), y, BZ + 0.0), V((0, 1, 0)), 0.01,
                 "M_CrocTeeth")
        if k < 3:
            cn.spike((s * (0.06 - k * 0.006), y - 0.02, BZ - 0.06 - 0.02 * k), (s * (0.06 - k * 0.006), y - 0.02, BZ - 0.02 - 0.02 * k),
                     V((0, 1, 0)), 0.01, "M_CrocTeeth")
    tube(cn, [(s * 0.065, -0.56, BZ + 0.12), (s * 0.065, -0.585, BZ + 0.125)], [0.026, 0.02], 6, "M_CrocEye",
         ref=V((0, 0, 1)))
    cn.spike((s * 0.07, -0.5, BZ + 0.11), (s * 0.085, -0.42, BZ + 0.17), V((0, 1, 0)), 0.024, "M_CrocHide")
# rivet strips + scute crest on top of the nacelle
for k in range(5):
    cn.spike((0, 0.35 - k * 0.15, BZ + 0.115), (0, 0.38 - k * 0.15, BZ + 0.16), V((1, 0, 0)), 0.02, "M_CrocHide")
# rear: exhaust stubs and a little three-blade prop on a spinner
for s in (-1, 1):
    tube(cn, [(s * 0.1, 0.42, BZ + 0.03), (s * 0.15, 0.52, BZ + 0.05)], [0.022, 0.02], 5, "M_Iron", ref=V((0, 0, 1)),
         cap1=False)
tube(cn, [(0, 0.58, BZ), (0, 0.64, BZ), (0, 0.67, BZ)], [0.05, 0.035, 0.005], 6, "M_Iron", ref=V((0, 0, 1)))
for k in range(3):
    a = k * 2 * math.pi / 3 + 0.3
    d = V((math.cos(a), 0, math.sin(a)))
    base = V((0, 0.63, BZ))
    cn.loft([[base + d * 0.03 + V((0, -0.008, 0)), base + d * 0.03 + V((0, 0.008, 0))],
             [base + d * 0.19 + V((0.01, -0.014, 0)), base + d * 0.19 + V((-0.01, 0.014, 0))]], "M_Iron", closed=False)
# grips: pistol grip (origin) and a fore grip, leather
cn.box((0, 0.0, 0.03), (0.04, 0.05, 0.14), "M_Leather")
cn.box((0, -0.3, 0.04), (0.035, 0.04, 0.12), "M_Leather")
tube(cn, [(0, 0.14, BZ - 0.02), (0, 0.14, BZ - 0.12), (0, 0.06, BZ - 0.13)], [0.012, 0.012, 0.012], 4, "M_Iron",
     ref=V((1, 0, 0)))                                                                 # trigger guard
cannon = cn.finish(smooth_angle=30)
muzzle = bpy.data.objects.new("Muzzle", None)
scene.collection.objects.link(muzzle)
muzzle.location = (0, -0.86, BZ)
muzzle.parent = cannon
place([cannon], 2.4)
export("CrocCannon.fbx", [cannon, muzzle])

# =================================================================================================
# MASK: the reward item. Front along -Y, origin at the face centre.
# =================================================================================================
mk = Part("CrocMask")
sh = mask_shell.Shell(profile=[(0.0, 0.065), (0.1, 0.125), (0.25, 0.162), (0.45, 0.186), (0.65, 0.196), (0.82, 0.188),
                               (0.93, 0.162), (1.0, 0.11)], y0=-0.06)
sh.build(mk, lambda x, z: "M_CrocBelly" if z < -0.16 else "M_CrocHide", strap="M_Leather")
# the snout juts out of the middle of the plate; the jaw line under it is an open mouth with a row of teeth
yS = sh.y(0.0, -0.03)
SN = [((0, yS + 0.02, -0.03), (0.072, 0.10)), ((0, yS - 0.08, -0.055), (0.06, 0.084)),
      ((0, yS - 0.16, -0.068), (0.047, 0.067)), ((0, yS - 0.215, -0.07), (0.03, 0.046))]
tube(mk, [p for p, _ in SN], [r for _, r in SN], 6, "M_CrocHide", ref=V((0, 0, 1)), phase=math.pi / 6, cap0=False)


def snout_at(f):
    k = min(int(f * (len(SN) - 1)), len(SN) - 2)
    t = f * (len(SN) - 1) - k
    (a, ra), (b, rb) = SN[k], SN[k + 1]
    return V(a).lerp(V(b), t), (ra[0] + (rb[0] - ra[0]) * t, ra[1] + (rb[1] - ra[1]) * t)


for s in (-1, 1):
    for k, f in enumerate((0.3, 0.48, 0.66, 0.84)):
        c, (rv, rh) = snout_at(f)
        base = c + V((s * rh * 0.82, 0, -rv * 0.5))
        mk.spike(base, base + V((s * 0.004, 0, -0.03 - 0.004 * (k % 2))), V((0, 1, 0)), 0.009, "M_CrocTeeth")
    c, (rv, rh) = snout_at(0.95)
    mk.spike(c + V((s * 0.018, 0, rv * 0.7)), c + V((s * 0.022, -0.004, rv * 0.7 + 0.022)), V((0, 1, 0)), 0.01, "M_CrocHide")
    # slit eyes high on the brow, under heavy ridges
    mask_shell.socket(mk, sh, s * 0.088, 0.078, 0.036, "M_CrocMouth", "M_CrocEye", eye_r=0.02, squash=0.55, tilt=s * 0.2)
    b0 = sh.at(s * 0.045, 0.118, 0.004)
    mk.spike(b0, b0 + V((s * 0.085, 0.03, 0.045)), V((0, 1, 0)), 0.026, "M_CrocHide", sides=4)
mouth = [(x, -0.138 - 0.06 * x * x / 0.017) for x in (-0.13, -0.09, -0.045, 0.0, 0.045, 0.09, 0.13)]
mask_shell.band(mk, sh, mouth, [0.016, 0.026, 0.032, 0.034, 0.032, 0.026, 0.016], "M_CrocMouth")
for k, x in enumerate((-0.11, -0.075, -0.04, 0.04, 0.075, 0.11)):
    z = -0.138 - 0.06 * x * x / 0.017 - 0.012
    p = sh.at(x, z, 0.004)
    mk.spike(p, p + V((0, -0.004, 0.026 + 0.006 * (k % 2))), V((0, 1, 0)), 0.008, "M_CrocTeeth")
# osteoderm spikes round the crown and down the cheeks (Majora's spikes, in croc armour)
mask_shell.rim_spikes(mk, sh, (0.5, 0.64, 0.77, 0.88, 0.96), 0.055, 0.017, "M_CrocHide", lean=0.25)
scute_row(mk, [sh.at(0, z, 0.002) for z in (0.13, 0.168)], 0.04, 0.018, V((0, 0.1, 1)))
mask = mk.finish(smooth_angle=35)
place([mask], 3.4)
export("CrocMask.fbx", [mask], {'MESH'})


# =================================================================================================
# BREAKABLES: a cracked boulder, a cracked wall section and six debris chunks
# =================================================================================================
def rock(part, c, size, seed, rings=5, sides=7, flat=0.85):
    c = V(c)
    rr = []
    for i in range(rings + 1):
        t = i / rings
        z = (t - 0.5) * size.z
        r = math.sin(math.pi * max(0.06, min(0.94, t))) * 0.5
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides + i * 0.37
            k = 1 + 0.28 * noise.noise(V((seed + i * 0.9, j * 1.3, seed * 0.5)))
            ring.append(c + V((math.cos(a) * size.x * r * k, math.sin(a) * size.y * r * k, z * (flat if t < 0.5 else 1))))
        rr.append(ring)
    part.loft(rr, "M_CrackedStone")


bp = Part("Boulder")
rock(bp, (0, 0, 0.72), V((1.9, 1.6, 1.55)), 3, rings=6, sides=9)
rock(bp, (0.55, -0.35, 0.35), V((0.9, 0.8, 0.7)), 8, rings=3, sides=6)
rock(bp, (-0.6, 0.3, 0.28), V((0.7, 0.8, 0.55)), 12, rings=3, sides=6)
boulder = bp.finish(smooth_angle=25)
place([boulder], -2.5)
export("Boulder.fbx", [boulder], {'MESH'})

wp = Part("CrackedWall")
# blocks in running bond, slightly irregular; a few already split
W, H, D = 3.4, 2.3, 0.55
rows = 5
for r in range(rows):
    h = H / rows
    z = r * h
    off = (r % 2) * 0.32
    x = -W / 2 - off
    k = 0
    while x < W / 2 - 0.05:
        w = 0.62 + 0.18 * noise.noise(V((r * 1.7, k * 2.1, 3.3)))
        x0, x1 = max(-W / 2, x), min(W / 2, x + w)
        if x1 - x0 > 0.08:
            jz = 0.02 * noise.noise(V((r, k, 9.0)))
            dy = 0.03 * noise.noise(V((k, r, 4.0)))
            wp.box(((x0 + x1) / 2, dy, z + h / 2 + jz), (x1 - x0 - 0.025, D - abs(dy), h - 0.025), "M_CrackedStone")
        x += w
        k += 1
# cap stones and rubble at the foot
wp.box((0, 0, H + 0.06), (W + 0.1, D + 0.12, 0.12), "M_CrackedStone")
for k in range(4):
    rock(wp, (-1.4 + k * 0.9, -0.42 + 0.1 * (k % 2), 0.12), V((0.35, 0.3, 0.24)), 20 + k, rings=2, sides=5)
wall = wp.finish(smooth_angle=15)
place([wall], -6.0)
export("CrackedWall.fbx", [wall], {'MESH'})

chunks = []
for i in range(6):
    cp = Part(f"RockChunk{i}")
    s = 0.28 + 0.12 * (i % 3)
    rock(cp, (0, 0, 0), V((s, s * 0.85, s * 0.8)), 40 + i * 3, rings=3, sides=5)
    o = cp.finish(smooth_angle=15)
    o.location.x += i * 0.6 if not EXPORT else 0
    chunks.append(o)
place(chunks, -9.0)
export("RockChunks.fbx", chunks, {'MESH'})
print("CROC SET OK", "out" if EXPORT else "(live, no export)", OUT)
