"""
Druid Mask set, PSX dark fantasy (Docs/ART_DIRECTION.md). Everything the Druid reward needs:

    blender -b --factory-startup --python Tools/Blender/build_druid_form.py -- <abs out_dir>

DruidForm.fbx       - the humanoid druid the player turns into. Same skeleton as the Elder Tree, the alligator and the
                      shark (Human Mega Animations Pack HumanM x 0.95), PS1 rigid skinning. A small cousin of the Druid
                      boss: pale weathered wood, a bark face with the boss's long drooping (Patapim) nose, a scowling
                      brow, sunken green eyes, an antler crown, twig fingers, root feet, a twisted root belt with glowing
                      seed charms and a seed necklace.
DruidFormLeaves.fbx - the leaf cover: "DLeaf_<HumanBone>_<n>" clumps (3 cutout cards, origin where the clump grows,
                      same space as the body in bind pose). Unity parents each one to its bone and sways it. They cover
                      ~80% of the body like a robe: mantle, chest, back, a skirt to the knees, arms, the crown and a hood.
                      Cards use the shared 2x2 atlas T_DruidLeaf: bottom row light leaves, top row dark (shadow) leaves.
DruidStaff.fbx      - gnarled pale-wood staff. Origin = grip, shaft along +Z (Unity +Y); three branches twist up into a
                      crook that cradles a glowing seed ("Focus" empty at its centre: spells leave from there).
DruidOrb.fbx        - the ranged ball: a faceted glowing core ("Core") and two leafy vine rings ("Ring0", "Ring1").
DruidMask.fbx       - the reward item: a bark face with the drooping nose, green eyes and a rim of leaves.

Materials are remapped by name in Unity (shared with the Druid boss): M_DruidBark, M_DruidRoot, M_DruidLeaf,
M_DruidEye, M_DruidHollow, plus M_DruidGlow (the seed / orb core). UVs are box-projected in metres, leaf cards use
the atlas. Conventions: Z up, the character faces -Y, its LEFT side is +X.
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = globals().get("TS_EXPORT", True)
OUT = os.path.abspath(argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(globals().get("__file__", "."))), "export"))
if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
else:
    scene = bpy.data.scenes.get("DruidForm") or bpy.data.scenes.new("DruidForm")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    try:
        bpy.context.window.scene = scene
    except Exception:
        pass

MAT_RGB = {
    "M_DruidBark": (0.55, 0.46, 0.36), "M_DruidRoot": (0.47, 0.38, 0.29), "M_DruidLeaf": (0.4, 0.52, 0.22),
    "M_DruidEye": (0.55, 0.75, 0.25), "M_DruidHollow": (0.05, 0.04, 0.035), "M_DruidGlow": (0.5, 0.9, 0.3),
}
# metres per texture tile (box projection): finer than the giant's 2.2 m so the grain reads at human scale
TILE = {"M_DruidBark": 0.75, "M_DruidRoot": 0.55, "M_DruidHollow": 0.5, "M_DruidGlow": 0.3}
FLAT = ("M_DruidEye",)
rng = random.Random(4242)

# ----------------------------------------------------------------------------------------------- skeleton
# Identical to build_elder_tree.py / build_shark_form.py (HumanM x 0.95). Keep these in sync: sockets rely on them.
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

    def disc(self, c, normal, r, mat, sides=6, depth=0.0):
        n = V(normal).normalized()
        s1 = n.cross(V((0, 0, 1)) if abs(n.z) < 0.9 else V((1, 0, 0))).normalized()
        s2 = n.cross(s1).normalized()
        ring = [self.v(V(c) + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides)) * r)
                for k in range(sides)]
        mid = self.v(V(c) + n * depth)
        for k in range(sides):
            self.face([ring[(k + 1) % sides], ring[k], mid], mat)

    def ico(self, c, r, mat, jag=0.0, seed=0):
        """Faceted low-poly ball (icosahedron, 20 tris)."""
        t = (1 + 5 ** 0.5) / 2
        pts = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t),
               (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
        vs = []
        for k, p in enumerate(pts):
            d = V(p).normalized()
            kk = 1 + jag * noise.noise(V((k * 1.7 + seed, k * 0.3, seed)))
            vs.append(self.v(V(c) + d * r * kk))
        tris_ = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6),
                 (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10),
                 (8, 6, 7), (9, 8, 1)]
        for a, b, c_ in tris_:
            self.face([vs[a], vs[b], vs[c_]], mat)

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
    if not EXPORT:
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
    """Live preview only: spread the props out beside the body."""
    if EXPORT:
        return
    for o in objs:
        o.location.x += dx


# ----------------------------------------------------------------------------------------------- leaf cards
CELLS = [(0, 0), (0.5, 0), (0, 0.5), (0.5, 0.5)]
LIGHT_CELLS, DARK_CELLS = CELLS[:2], CELLS[2:]   # T_DruidLeaf: bottom row light, top row dark (shadow) leaves


def card(part, c, right, down, w, h, dark=False):
    cu, cv = (DARK_CELLS if dark else LIGHT_CELLS)[rng.randrange(2)]
    vs = [part.v(c - right * w + down * h), part.v(c + right * w + down * h), part.v(c + right * w - down * h * 0.2),
          part.v(c - right * w - down * h * 0.2)]
    part.face(vs, "M_DruidLeaf", uv=[(cu, cv), (cu + 0.5, cv), (cu + 0.5, cv + 0.5), (cu, cv + 0.5)])


def clump_cards(part, p, nrm, size, hang=1.0, shade=None):
    """A spray of leaves growing at p: a dark shingle lying along the body (the shadow under it) and two light cards
    fanned out from it. hang > 1 makes it longer (robe / skirt). shade forces the fans dark too."""
    n = V(nrm).normalized()
    down = V((0, 0, -1)) - n * (-n.z)
    if down.length < 0.3:
        down = n.cross(V((1, 0, 0)))
    down.normalize()
    right = down.cross(n).normalized()
    root = p + n * size * 0.08
    if shade is None:
        shade = n.z < -0.3 or rng.random() < 0.2
    card(part, root, right, (down * 0.88 + n * 0.25).normalized(), size * 0.55, size * 0.95 * hang, dark=True)
    for sgn in (-1, 1):
        r2 = (right * math.cos(0.85) + n * math.sin(0.85) * 0.6 * sgn).normalized()
        d2 = (down * 0.72 + n * 0.5 + right * 0.22 * sgn).normalized()
        card(part, root + n * size * 0.07, r2, d2, size * 0.45, size * 0.8 * hang, dark=shade)


# =================================================================================================
# DRUID FORM
# =================================================================================================
parts = []
leaf_jobs = []    # (bone, point, normal, size, hang, shade)


def P(name, bone):
    p = Part(name, bone)
    parts.append(p)
    return p


def sec_point(secs, z, a):
    """Point + outward normal on stacked ellipse sections (z, rx, ry, y) at height z, angle a (-pi/2 = front)."""
    s = sorted(secs)
    z = min(max(z, s[0][0]), s[-1][0])
    for s0, s1 in zip(s, s[1:]):
        if s0[0] <= z <= s1[0]:
            t = (z - s0[0]) / max(1e-4, s1[0] - s0[0])
            rx, ry, y = (s0[1] + (s1[1] - s0[1]) * t, s0[2] + (s1[2] - s0[2]) * t, s0[3] + (s1[3] - s0[3]) * t)
            break
    p = V((math.cos(a) * rx, y + math.sin(a) * ry, z))
    n = V((math.cos(a) / max(rx, 0.02), math.sin(a) / max(ry, 0.02), 0)).normalized()
    return p, n


def ellipse_tube(part, secs, sides, mat, jag=0.05, seed=0, cap0=True, cap1=True):
    rings = []
    for i, (z, rx, ry, y) in enumerate(secs):
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides + math.pi / sides
            k = 1 + jag * noise.noise(V((z * 9.1 + seed, j * 1.3, seed * 0.4)))
            ring.append(V((math.cos(a) * rx * k, y + math.sin(a) * ry * k, z)))
        rings.append(ring)
    part.loft(rings, mat, cap0, cap1)


# ---- trunk: a slender pale-wood body, the chest a little barrel-like (the boss in miniature)
TORSO = {
    "Hips": [(0.80, 0.155, 0.125, 0.0), (0.88, 0.175, 0.135, 0.0), (0.97, 0.17, 0.13, 0.0), (1.04, 0.165, 0.125, 0.0)],
    "Spine": [(1.0, 0.16, 0.125, 0.0), (1.12, 0.175, 0.135, -0.005), (1.23, 0.205, 0.145, -0.01)],
    "Chest": [(1.19, 0.205, 0.145, -0.01), (1.30, 0.245, 0.16, -0.012), (1.39, 0.265, 0.155, -0.01),
              (1.46, 0.215, 0.135, 0.0), (1.52, 0.11, 0.095, 0.0)],
    "Neck": [(1.46, 0.075, 0.07, 0.0), (1.55, 0.066, 0.066, -0.006), (1.61, 0.066, 0.066, -0.012)],
}
ALL_TORSO = TORSO["Hips"] + TORSO["Spine"][1:] + TORSO["Chest"][1:]
for bone, secs in TORSO.items():
    ellipse_tube(P("Druid_" + bone, bone), secs, 8 if bone != "Neck" else 6, "M_DruidBark", jag=0.06, seed=len(parts),
                 cap0=bone == "Hips", cap1=bone == "Neck")

chest = parts[2]
# bark knots and broken twig stubs on the chest and back (silhouette over smoothness)
for (a, z, L) in ((-1.2, 1.33, 0.09), (-2.1, 1.25, 0.07), (1.4, 1.36, 0.1), (0.4, 1.28, 0.06)):
    p, n = sec_point(TORSO["Chest"], z, a)
    tube(chest, [p - n * 0.02, p + n * L * 0.6 + V((0, 0, 0.02)), p + n * L + V((0, 0, 0.05))], [0.022, 0.014, 0.004], 4,
         "M_DruidBark")
# seed necklace: a cord of three glowing seeds across the chest
for k, lat in enumerate((-0.07, 0.0, 0.07)):
    z = 1.36 - (0.07 - abs(lat)) * 0.6
    p, n = sec_point(TORSO["Chest"], z, -math.pi / 2 + lat * 3)
    chest.ico(p + n * 0.02, 0.017 if k != 1 else 0.022, "M_DruidGlow", jag=0.15, seed=k)
tube(chest, [sec_point(TORSO["Chest"], 1.43, -math.pi / 2 + a)[0] + V((0, -0.008, 0)) for a in (-1.0, -0.5, 0.0, 0.5, 1.0)],
     [0.006] * 5, 3, "M_DruidRoot", cap0=False, cap1=False)

# root belt: a twisted root band around the hips with three seed charms hanging in front
belt = P("Druid_Belt", "Hips")
for k in range(1):
    pts = []
    for j in range(13):
        a = 2 * math.pi * j / 12
        p, n = sec_point(TORSO["Hips"], 0.92 + 0.012 * math.sin(a * 3 + k * math.pi), a)
        pts.append(p + n * 0.012)
    tube(belt, pts, [0.016] * 13, 4, "M_DruidRoot", cap0=False, cap1=False, phase=k * 0.6)
for k, a in enumerate((-math.pi / 2 - 0.45, -math.pi / 2, -math.pi / 2 + 0.45)):
    p, n = sec_point(TORSO["Hips"], 0.91, a)
    q = p + n * 0.02 + V((0, 0, -0.07 - 0.02 * (k == 1)))
    tube(belt, [p + n * 0.015, q], [0.004, 0.004], 3, "M_DruidRoot", cap0=False, cap1=False)
    belt.ico(q + V((0, 0, -0.015)), 0.02, "M_DruidGlow", jag=0.2, seed=10 + k)

# ---- head: an oval bark head with the boss's long drooping nose, a scowling brow and sunken green eyes
HEAD = [(1.56, 0.085, 0.09, -0.02), (1.62, 0.112, 0.118, -0.03), (1.70, 0.122, 0.128, -0.035), (1.78, 0.114, 0.12, -0.03),
        (1.85, 0.086, 0.092, -0.02), (1.895, 0.04, 0.045, -0.01)]
head = P("Druid_Head", "Head")
ellipse_tube(head, HEAD, 9, "M_DruidBark", jag=0.05, seed=21)
FACE_Y = -0.035 - 0.128
brow = []
for lat in (-0.11, -0.055, 0.0, 0.055, 0.11):
    zc = 1.748 - (0.11 - abs(lat)) * 0.18
    f = FACE_Y - 0.012 - (0.11 - abs(lat)) * 0.16
    brow.append([V((lat, f + 0.03, zc + 0.022)), V((lat, f - 0.012, zc + 0.008)), V((lat, f - 0.004, zc - 0.016)),
                 V((lat, f + 0.035, zc - 0.012))])
head.loft(brow, "M_DruidBark")
for s in (-1, 1):
    c = V((s * 0.052, FACE_Y + 0.006, 1.712))
    nrm = V((s * 0.35, -1, 0.05))
    head.disc(c, nrm, 0.03, "M_DruidHollow", sides=6, depth=0.008)
    head.disc(c + nrm.normalized() * 0.004, nrm, 0.012, "M_DruidEye", sides=5, depth=0.004)
    # cheek burls either side of the nose
    cc = V((s * 0.068, FACE_Y + 0.012, 1.655))
    tube(head, [cc + V((-s * 0.018, 0.02, 0.012)), cc, cc + V((s * 0.014, 0.02, -0.014))], [0.022, 0.03, 0.018], 5,
         "M_DruidBark", jag=0.1, seed=31 + s)
# the nose: long, drooping, bulbous at the end (Patapim's proboscis)
NOSE = [V((0, FACE_Y + 0.01, 1.712)), V((0, FACE_Y - 0.045, 1.695)), V((0, FACE_Y - 0.082, 1.648)),
        V((0, FACE_Y - 0.09, 1.588)), V((0, FACE_Y - 0.075, 1.552))]
tube(head, NOSE, [0.028, 0.032, 0.041, 0.043, 0.022], 6, "M_DruidBark", ref=V((1, 0, 0)), jag=0.08, seed=41)
for s in (-1, 1):
    head.disc(V((s * 0.016, FACE_Y - 0.085, 1.566)), V((s * 0.3, -0.5, -1)), 0.008, "M_DruidHollow", sides=4, depth=0.006)
# mouth: a dark slit half hidden under the nose, with a thick lower lip
head.loft([[V((-0.042, FACE_Y + 0.016, 1.612)), V((-0.042, FACE_Y + 0.016, 1.6)), V((-0.042, FACE_Y + 0.045, 1.6)),
            V((-0.042, FACE_Y + 0.045, 1.612))],
           [V((0.042, FACE_Y + 0.016, 1.612)), V((0.042, FACE_Y + 0.016, 1.6)), V((0.042, FACE_Y + 0.045, 1.6)),
            V((0.042, FACE_Y + 0.045, 1.612))]], "M_DruidHollow")
tube(head, [V((-0.045, FACE_Y + 0.022, 1.594)), V((0, FACE_Y + 0.008, 1.588)), V((0.045, FACE_Y + 0.022, 1.594))],
     [0.008, 0.011, 0.008], 4, "M_DruidBark")
# antler crown: twisted branches forking up and out of the skull (the boss's crown, druid-sized)
for k, (lat, f, lean) in enumerate(((-0.05, 0.0, -0.55), (0.055, 0.005, 0.55), (-0.02, 0.05, -0.18), (0.025, -0.03, 0.2),
                                    (0.0, 0.06, 0.0))):
    b = V((lat, f, 1.86))
    back = 0.05 if abs(lean) < 0.3 else 0.0
    m = b + V((lean * 0.17, rng.uniform(-0.02, 0.03) + back, 0.11))
    t = m + V((lean * 0.22, rng.uniform(-0.03, 0.04) + back * 1.5, 0.07))
    tube(head, [b, m, t], [0.016, 0.01, 0.003], 4, "M_DruidBark", jag=0.12, seed=50 + k)
    fork = m + (t - m) * 0.45
    tube(head, [fork, fork + V((-lean * 0.05, rng.uniform(-0.04, 0.04), 0.07))], [0.007, 0.002], 3, "M_DruidBark", seed=60 + k)

# ---- arms: gnarled branches, burls over the shoulders, a root bracer, twig fingers
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    sh = P(f"Druid_{L}Shoulder", L + "Shoulder")
    tube(sh, [(s * 0.17, 0.0, SHOULDER_Z + 0.01), (s * 0.27, 0.0, SHOULDER_Z + 0.03), (s * 0.35, 0.0, SHOULDER_Z + 0.005)],
         [0.075, 0.09, 0.07], 6, "M_DruidBark", ref=V((0, 0, 1)), jag=0.12, seed=70 + s)
    ua = P(f"Druid_{L}UpperArm", L + "UpperArm")
    tube(ua, [(s * 0.28, 0, SHOULDER_Z), (s * 0.41, 0.004, SHOULDER_Z + 0.006), (s * 0.565, 0, SHOULDER_Z)],
         [0.062, 0.058, 0.048], 6, "M_DruidBark", ref=V((0, 0, 1)), jag=0.1, seed=80 + s)
    ua.spike((s * 0.45, 0.02, SHOULDER_Z + 0.04), (s * 0.5, 0.05, SHOULDER_Z + 0.1), V((1, 0, 0)), 0.012, "M_DruidBark")
    la = P(f"Druid_{L}LowerArm", L + "LowerArm")
    tube(la, [(s * 0.525, 0, SHOULDER_Z), (s * 0.65, -0.004, SHOULDER_Z), (s * 0.77, 0, SHOULDER_Z)],
         [0.05, 0.046, 0.036], 6, "M_DruidBark", ref=V((0, 0, 1)), jag=0.12, seed=90 + s)
    # root bracer: a twisted root wound around the wrist
    pts = []
    for j in range(10):
        a = j * 1.3
        pts.append(V((s * (0.66 + j * 0.0095), math.cos(a) * 0.046, SHOULDER_Z + math.sin(a) * 0.046)))
    tube(la, pts, [0.011] * 10, 3, "M_DruidRoot", cap0=False, cap1=False)
    hand = P(f"Druid_{L}Hand", L + "Hand")
    tube(hand, [(s * 0.745, 0, SHOULDER_Z), (s * 0.79, 0, SHOULDER_Z - 0.003), (s * 0.835, 0.0, SHOULDER_Z - 0.005)],
         [(0.024, 0.044), (0.026, 0.05), (0.022, 0.047)], 6, "M_DruidBark", ref=V((0, 0, 1)), phase=math.pi / 6)
    for fname, chain in finger_chains(s).items():
        if fname == "Little":
            continue
        for k, (bn, h, t) in enumerate(chain):
            fp = P(f"Druid_{bn}", bn)
            h, t = V(h), V(t)
            d = (t - h)
            r = 0.013 if fname != "Thumb" else 0.015
            tube(fp, [h - d.normalized() * 0.005, t + d.normalized() * 0.003], [r * (1 - 0.18 * k), r * (0.82 - 0.18 * k)],
                 4, "M_DruidBark", ref=V((0, 0, 1)), phase=math.pi / 4, cap0=False, cap1=k != 2)
            if k == 2:   # twig tips
                fp.spike(t, t + d.normalized() * 0.035 + V((0, 0, -0.008)), V((0, 0, 1)), 0.007, "M_DruidBark")

# ---- legs: trunk-like thighs and shins, knee burls, root feet splayed into the dirt
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    ul = P(f"Druid_{L}UpperLeg", L + "UpperLeg")
    tube(ul, [(s * HIP_X, 0.0, 0.96), (s * (HIP_X + 0.01), -0.004, 0.72), (s * HIP_X, 0, KNEE_Z - 0.03)],
         [0.092, 0.084, 0.068], 7, "M_DruidBark", ref=V((1, 0, 0)), jag=0.08, seed=100 + s)
    ll = P(f"Druid_{L}LowerLeg", L + "LowerLeg")
    tube(ll, [(s * HIP_X, 0, KNEE_Z + 0.03), (s * HIP_X, 0.008, 0.31), (s * HIP_X, 0, ANKLE_Z + 0.03)],
         [0.068, 0.06, 0.052], 6, "M_DruidBark", ref=V((1, 0, 0)), jag=0.1, seed=110 + s)
    tube(ll, [(s * HIP_X, -0.04, KNEE_Z + 0.05), (s * HIP_X, -0.07, KNEE_Z), (s * HIP_X, -0.045, KNEE_Z - 0.06)],
         [0.03, 0.04, 0.025], 5, "M_DruidBark", ref=V((1, 0, 0)), jag=0.15, seed=120 + s)   # knee burl
    ft = P(f"Druid_{L}Foot", L + "Foot")
    X = s * HIP_X
    # ankle flare: the shin spreads into a root mass
    ft.loft([[V((X + math.cos(a) * r, -0.02 + math.sin(a) * r * 0.9, z)) for a in (2 * math.pi * j / 7 for j in range(7))]
             for z, r in ((0.15, 0.055), (0.08, 0.072), (0.02, 0.085))], "M_DruidRoot", cap0=False)
    # root toes: three forward (continued by the Toes bone), one to the outside, a heel root
    # (angle from forward, + toward the outside of this foot)
    for k, (deg, L_, r0) in enumerate(((0, 0.11, 0.026), (-28, 0.1, 0.022), (28, 0.1, 0.022), (70, 0.09, 0.018),
                                       (180, 0.1, 0.022))):
        t = math.radians(deg)
        d = V((s * math.sin(t), -math.cos(t), 0))
        p0 = V((X, -0.02, 0.04)) + d * 0.04
        tube(ft, [p0, p0 + d * L_ * 0.5 + V((0, 0, -0.015)), p0 + d * L_ + V((0, 0, -0.03))], [r0, r0 * 0.7, r0 * 0.4], 4,
             "M_DruidRoot", jag=0.15, seed=130 + k + s * 10)
    toes = P(f"Druid_{L}Toes", L + "Toes")
    for k, deg in enumerate((0, -24, 24)):
        t = math.radians(deg)
        d = V((-math.sin(t), -math.cos(t), 0))
        p0 = V((X, TOE_Y + 0.03, 0.012)) + V((-math.sin(t) * 0.045, 0, 0))
        tube(toes, [p0, p0 + d * 0.06 + V((0, 0, -0.004)), p0 + d * 0.11 + V((0, 0, -0.008))], [0.016, 0.011, 0.003], 4,
             "M_DruidRoot", jag=0.15, seed=150 + k + s * 10)

# ---- leaf cover (~80%): mantle, chest and back, a skirt to the knees, arms, crown, hood, collar
for i in range(32):                                     # chest + back
    a = 2 * math.pi * i / 32 * 2 + rng.uniform(-0.15, 0.15)
    z = 1.22 + (i * 0.618 % 1.0) * 0.24
    p, n = sec_point(TORSO["Chest"], z, a)
    leaf_jobs.append(("Chest", p, (n + V((0, 0, 0.3))).normalized(), rng.uniform(0.2, 0.25), 1.0, None))
for i in range(12):                                     # waist
    a = 2 * math.pi * i / 12 + rng.uniform(-0.2, 0.2)
    p, n = sec_point(TORSO["Spine"], rng.uniform(1.04, 1.2), a)
    leaf_jobs.append(("Spine", p, (n + V((0, 0, 0.25))).normalized(), rng.uniform(0.2, 0.24), 1.1, None))
for i in range(16):                                     # robe skirt: long sprays hanging off the hips
    a = 2 * math.pi * i / 16 + rng.uniform(-0.12, 0.12)
    p, n = sec_point(TORSO["Hips"], rng.uniform(0.86, 0.95), a)
    leaf_jobs.append(("Hips", p + n * 0.02, (n * 1.4 + V((0, 0, -0.2))).normalized(), rng.uniform(0.24, 0.28), 1.6, None))
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    for i in range(8):                                  # thighs: the outer, front and back faces
        a = rng.uniform(-1.9, 1.9) if s > 0 else math.pi + rng.uniform(-1.9, 1.9)
        z = rng.uniform(0.62, 0.84)
        n = V((math.cos(a), math.sin(a), 0))
        leaf_jobs.append((L + "UpperLeg", V((s * HIP_X, 0, z)) + n * 0.08, (n + V((0, 0, 0.1))).normalized(),
                          rng.uniform(0.18, 0.22), 1.25, None))
    leaf_jobs.append((L + "LowerLeg", V((s * HIP_X, -0.06, 0.5)), V((s * 0.3, -1, 0.3)).normalized(), 0.17, 1.0, None))
    leaf_jobs.append((L + "LowerLeg", V((s * (HIP_X + 0.05), 0.02, 0.46)), V((s, 0.2, 0.2)).normalized(), 0.15, 1.0, True))
    for k in range(5):                                  # mantle over the shoulder burl
        c = V((s * rng.uniform(0.12, 0.25), rng.uniform(-0.09, 0.09), SHOULDER_Z + 0.08))   # inside the joint: stays put when the arm drops
        leaf_jobs.append((L + "Shoulder", c, V((s * 0.5, rng.uniform(-0.4, 0.4), 1)).normalized(), rng.uniform(0.2, 0.24), 1.15,
                          None))
    for k in range(4):                                  # upper arm: top and back
        x = s * (0.33 + k * 0.06)
        a = rng.uniform(0.3, 2.6)
        n = V((0, math.cos(a), math.sin(a)))
        leaf_jobs.append((L + "UpperArm", V((x, 0, SHOULDER_Z)) + n * 0.05, n, rng.uniform(0.16, 0.2), 1.0, None))
    for k in range(3):                                  # forearm: over the top and the back
        x = s * (0.57 + k * 0.06)
        n = V((0, 0.5 + k * 0.3, 1)).normalized()
        leaf_jobs.append((L + "LowerArm", V((x, 0.01, SHOULDER_Z + 0.04)), n, 0.15, 1.0, None))
for i in range(9):                                      # leafy crown among the antlers, swept off the face
    a = math.radians(rng.uniform(-170, 10)) if i % 2 else math.radians(rng.uniform(-10, 190))
    a = a if math.sin(a) > -0.35 else a + math.pi
    p, n = sec_point(HEAD, rng.uniform(1.8, 1.875), a)
    leaf_jobs.append(("Head", p, (n + V((0, 0, 1.0))).normalized(), rng.uniform(0.18, 0.22), 1.0, None))
for i in range(5):                                      # hood: leaves down the back of the skull
    a = math.radians(40 + i * 25)
    p, n = sec_point(HEAD, rng.uniform(1.64, 1.78), a)
    leaf_jobs.append(("Head", p, (n + V((0, 0, 0.35))).normalized(), 0.21, 1.25, None))
for s in (-1, 1):                                       # a mossy beard either side of the nose (dark)
    leaf_jobs.append(("Head", V((s * 0.07, FACE_Y + 0.03, 1.585)), V((s * 0.4, -0.6, -0.6)).normalized(), 0.12, 1.2, True))
for i in range(4):                                      # collar
    a = -math.pi / 2 + (i - 1.5) * 0.9 if i < 2 else math.pi / 2 + (i - 2.5) * 0.9
    p, n = sec_point(TORSO["Neck"], 1.52, a)
    leaf_jobs.append(("Neck", p, (n + V((0, 0, 0.6))).normalized(), 0.17, 1.0, None))

druid_objs = [p.finish(smooth_angle=40) for p in parts]
select_only(druid_objs)
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "DruidForm"
body.data.name = "DruidForm_Mesh"

arm_data = bpy.data.armatures.new("DruidForm_Armature")
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
print("DruidForm tris", tris([body]), "bones", len(BONES))
export("DruidForm.fbx", [arm_obj, body], {'ARMATURE', 'MESH'})

leaf_objs = []
for idx, (bone, p, nrm, size, hang, shade) in enumerate(leaf_jobs):
    lp = Part(f"DLeaf_{bone}_{idx:03d}", None, p)
    clump_cards(lp, p, nrm, size, hang, shade)
    leaf_objs.append(lp.finish(weld=False))
print("DruidFormLeaves tris", tris(leaf_objs), "clumps", len(leaf_objs))
export("DruidFormLeaves.fbx", leaf_objs)

# =================================================================================================
# STAFF: grip at the origin, shaft along +Z. A gnarled pale-wood staff whose head splits into three branches that
# twist up and curl back in around a glowing seed.
# =================================================================================================
staff = Part("DruidStaff")
SHAFT = [V((0, 0, -0.62)), V((0.008, 0.004, -0.3)), V((-0.006, 0.0, 0.05)), V((0.01, -0.004, 0.45)), V((0.0, 0.006, 0.86)),
         V((0.004, 0.0, 1.0))]
tube(staff, SHAFT, [0.02, 0.022, 0.024, 0.023, 0.026, 0.03], 6, "M_DruidBark", ref=V((1, 0, 0)), jag=0.14, seed=201)
for k, z in enumerate((-0.48, -0.1, 0.32, 0.7)):         # knots and a broken twig or two
    a = k * 2.1
    d = V((math.cos(a), math.sin(a), 0.35)).normalized()
    b = V((0, 0, z))
    tube(staff, [b + d * 0.01, b + d * 0.05, b + d * 0.08 + V((0, 0, 0.03))], [0.012, 0.007, 0.002], 4, "M_DruidBark", seed=210 + k)
# a root foot at the butt: small spurs splaying out
for k in range(3):
    a = k * 2 * math.pi / 3 + 0.4
    d = V((math.cos(a), math.sin(a), -0.6)).normalized()
    b = V((0, 0, -0.58))
    tube(staff, [b, b + d * 0.06, b + d * 0.1 + V((0, 0, -0.02))], [0.012, 0.007, 0.002], 4, "M_DruidRoot", seed=220 + k)
# grip wrap: twisted root cord around the hold
pts = [V((math.cos(j * 1.1) * 0.027, math.sin(j * 1.1) * 0.027, -0.08 + j * 0.012)) for j in range(16)]
tube(staff, pts, [0.006] * 16, 3, "M_DruidRoot")
SEED = V((0.0, 0.0, 1.2))
for k in range(3):                                       # the crook: three branches spiralling around the seed
    a0 = k * 2 * math.pi / 3
    pts = []
    for j in range(7):
        t = j / 6
        a = a0 + t * 2.2
        r = 0.028 + math.sin(t * math.pi) * 0.085 - t * 0.02
        z = 1.0 + t * 0.32
        pts.append(V((math.cos(a) * r, math.sin(a) * r, z)))
    pts.append(SEED + (pts[-1] - SEED) * 0.55 + V((0, 0, 0.05)))
    tube(staff, pts, [0.02, 0.017, 0.015, 0.013, 0.011, 0.008, 0.005, 0.002], 5, "M_DruidBark", jag=0.1, seed=230 + k)
staff.ico(SEED, 0.05, "M_DruidGlow", jag=0.18, seed=7)
for k in range(5):                                       # leaves sprouting from the crook, moss hanging below
    a = k * 2 * math.pi / 5 + 0.3
    n = V((math.cos(a), math.sin(a), 0.2 if k % 2 else -0.4)).normalized()
    c = V((0, 0, 1.06 + 0.05 * (k % 3))) + n * 0.07
    clump_cards(staff, c, n, 0.12, 1.0 if k % 2 else 1.4, shade=(k == 2))
staff_ob = staff.finish(smooth_angle=40, weld=False)
focus = bpy.data.objects.new("Focus", None)
focus.location = SEED
scene.collection.objects.link(focus)
focus.parent = staff_ob
export("DruidStaff.fbx", [staff_ob, focus])
place([staff_ob], 0.9)

# =================================================================================================
# ORB: faceted glowing core + two leafy vine rings (Unity spins the rings and adds the glow)
# =================================================================================================
core = Part("Core")
core.ico(V((0, 0, 0)), 0.5, "M_DruidGlow", jag=0.08, seed=3)
orb_objs = [core.finish(smooth_angle=10)]
for k in range(2):
    ring = Part(f"Ring{k}")
    pts = []
    for j in range(13):
        a = 2 * math.pi * j / 12
        w = 0.04 * math.sin(a * 3 + k)
        pts.append(V((math.cos(a) * (0.66 + w), math.sin(a) * (0.66 + w), 0.05 * math.sin(a * 2 + k))))
    tube(ring, pts, [0.035] * 13, 4, "M_DruidRoot", cap0=False, cap1=False, phase=k)
    for j in range(4):
        a = 2 * math.pi * (j + 0.5) / 4 + k
        p = V((math.cos(a) * 0.68, math.sin(a) * 0.68, 0))
        clump_cards(ring, p, V((math.cos(a), math.sin(a), 0.3)).normalized(), 0.3, 1.0, shade=(j % 2 == 1))
    ob = ring.finish(weld=False)
    if k == 1:
        ob.rotation_euler = (math.radians(70), 0, math.radians(35))
    orb_objs.append(ob)
export("DruidOrb.fbx", orb_objs)
place(orb_objs, 1.6)

# =================================================================================================
# MASK (reward item): a bark face with the drooping nose, green eyes and a rim of leaves
# =================================================================================================
mask = Part("DruidMask")
MF = [(-0.13, 0.05, 0.075, 0.02), (-0.06, 0.1, 0.11, 0.0), (0.02, 0.115, 0.12, -0.005), (0.09, 0.1, 0.11, 0.0),
      (0.15, 0.06, 0.07, 0.02)]
rings = []
for (z, rx, ry, y) in MF:      # a shallow shell: only the front half, curved
    rings.append([V((math.cos(a) * rx, y - math.sin(a) * ry * 0.45, z)) for a in (math.radians(d) for d in range(-10, 191, 25))])
mask.loft(rings, "M_DruidBark", closed=False)
mask.loft([list(reversed(r)) for r in rings], "M_DruidBark", closed=False)
for s in (-1, 1):
    mask.disc(V((s * 0.045, -0.05, 0.035)), V((s * 0.3, -1, 0)), 0.026, "M_DruidHollow", sides=6, depth=0.006)
    mask.disc(V((s * 0.045, -0.052, 0.035)), V((s * 0.3, -1, 0)), 0.011, "M_DruidEye", sides=5, depth=0.003)
tube(mask, [V((-0.1, -0.05, 0.065)), V((0, -0.065, 0.055)), V((0.1, -0.05, 0.065))], [0.015, 0.02, 0.015], 4, "M_DruidBark")
tube(mask, [V((0, -0.055, 0.04)), V((0, -0.1, 0.02)), V((0, -0.125, -0.03)), V((0, -0.118, -0.08)), V((0, -0.1, -0.1))],
     [0.02, 0.024, 0.03, 0.032, 0.016], 5, "M_DruidBark", ref=V((1, 0, 0)), jag=0.08, seed=301)
for k in range(9):                                       # rim of leaves around the top and sides
    a = math.radians(-20 + k * 25)
    p = V((math.cos(a) * 0.1, 0.0, 0.03 + math.sin(a) * 0.12))
    n = V((math.cos(a), 0.3, math.sin(a))).normalized()
    clump_cards(mask, p, n, 0.1, 1.0, shade=(k % 3 == 1))
for k, lat in enumerate((-0.05, 0.05)):                  # antler stubs
    b = V((lat, 0.0, 0.14))
    tube(mask, [b, b + V((lat * 0.8, 0.0, 0.06)), b + V((lat * 1.6, 0.01, 0.1))], [0.01, 0.006, 0.002], 4, "M_DruidBark", seed=310 + k)
mask_ob = mask.finish(weld=False)
export("DruidMask.fbx", [mask_ob])
place([mask_ob], 2.4)
print("OUT", OUT if EXPORT else "(no export)")
