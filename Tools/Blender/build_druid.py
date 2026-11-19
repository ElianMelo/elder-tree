"""
Druid boss, PSX dark fantasy (Docs/ART_DIRECTION.md).

    blender -b --factory-startup --python Tools/Blender/build_druid.py -- <abs out_dir>

Live (MCP): exec with TS_EXPORT=False to build into a dedicated "Druid" scene without exporting.

Druid.fbx - a giant dryad (~11 m to the crown tips) in the spirit of the "Brr Brr Patapim" meme: a long drooping
            proboscis nose in a bark face, long gnarled arms with twig fingers, two trunk legs, and feet made of
            splayed roots instead of big human feet. Rigid PS1-style segments so Unity animates them procedurally;
            every object's origin sits on its joint:
              Druid_Pelvis                 hips (origin: hip centre)
              Druid_Torso                  barrel trunk chest, shoulders (origin: waist)
              Druid_Head                   skull, brow, sockets, ember-green eyes, the nose, mouth, antler crown
              Druid_UpperArm_L/R, Druid_Forearm_L/R, Druid_Hand_L/R (palm + curled twig fingers)
              Druid_Thigh_L/R, Druid_Shin_L/R
              Druid_Foot_L/R               root flare + root toes (origin: ankle)
              Druid_Leaf_<Part>_<n>        leaf clumps (3 crossed cutout cards) whose origin is where they grow:
                                           Unity parents each to <Part> by name and sways it.
            Forward is Blender -Y (Unity +Z). Origin = ground under the hips. _R is Blender +X.

Materials (remapped in Unity by name): M_DruidBark, M_DruidRoot, M_DruidLeaf, M_DruidEye, M_DruidHollow.
UVs are box-projected in world metres (uniform texel density); leaf cards use a 2x2 atlas.
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
OUT = os.path.abspath(argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "export"))
if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
else:
    scene = bpy.data.scenes.get("Druid") or bpy.data.scenes.new("Druid")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.window.scene = scene

MAT_RGB = {
    "M_DruidBark": (0.20, 0.16, 0.13), "M_DruidRoot": (0.27, 0.21, 0.16), "M_DruidLeaf": (0.17, 0.24, 0.11),
    "M_DruidEye": (0.55, 0.75, 0.25), "M_DruidHollow": (0.05, 0.04, 0.035),
}
TILE = {"M_DruidBark": 2.2, "M_DruidRoot": 1.6, "M_DruidHollow": 1.0}
FLAT_UV = ("M_DruidEye",)
rng = random.Random(1337)


def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*MAT_RGB[name], 1)
    return m


def V(lat, f, z):
    """lateral (right +), forward, height -> Blender coords (forward along -Y)."""
    return Vector((lat, -f, z))


class Part:
    """One mesh object being assembled with bmesh; faces carry material names."""

    def __init__(self, name, origin=Vector((0, 0, 0))):
        self.name = name
        self.origin = Vector(origin)
        self.bm = bmesh.new()
        self.mats = []
        self.uvs = {}

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def v(self, co):
        return self.bm.verts.new(Vector(co) - self.origin)

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
                c = self.bm.verts.new(sum((v.co for v in ring), Vector()) / len(ring))
                for j in range(n):
                    a, b = ring[j], ring[(j + 1) % n]
                    self.face([b, a, c] if rev else [a, b, c], mats(-1, j) if mats else mat)
        return vr

    def branch(self, pts, radii, mat, sides=6, jag=0.0, seed=0, cap0=True, cap1=True, mats=None):
        """Gnarled tube through pts (radius per point). Rings follow a parallel-transported frame."""
        rings = []
        prev_s1 = None
        for i, (c, r) in enumerate(zip(pts, radii)):
            if i == 0:
                d = (pts[1] - pts[0]).normalized()
            elif i == len(pts) - 1:
                d = (pts[-1] - pts[-2]).normalized()
            else:
                d = ((pts[i + 1] - pts[i]).normalized() + (pts[i] - pts[i - 1]).normalized()).normalized()
            if prev_s1 is None:
                up = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
                s1 = d.cross(up).normalized()
            else:
                s1 = (prev_s1 - d * prev_s1.dot(d)).normalized()
            s2 = d.cross(s1).normalized()
            prev_s1 = s1
            ring = []
            for k in range(sides):
                a = k * 2 * math.pi / sides + math.pi / sides
                kk = 1 + jag * noise.noise(Vector((i * 1.37 + seed, k * 0.91, seed * 0.17)))
                ring.append(c + (s1 * math.cos(a) + s2 * math.sin(a)) * r * kk)
            rings.append(ring)
        return self.loft(rings, mat, cap0=cap0, cap1=cap1, mats=mats)

    def spike(self, base, tip, side, width, mat, sides=3):
        axis = (tip - base).normalized()
        s1 = (side - axis * side.dot(axis)).normalized()
        s2 = axis.cross(s1).normalized()
        ring = [self.v(base + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides)) * width)
                for k in range(sides)]
        t = self.v(tip)
        for k in range(sides):
            self.face([ring[k], ring[(k + 1) % sides], t], mat)
        self.face(list(reversed(ring)), mat)

    def disc(self, c, normal, r, mat, sides=6, depth=0.0):
        """Flat (or shallow cone) disc facing `normal`."""
        n = normal.normalized()
        s1 = n.cross(Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((1, 0, 0))).normalized()
        s2 = n.cross(s1).normalized()
        ring = [self.v(c + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides)) * r)
                for k in range(sides)]
        mid = self.v(c + n * depth)
        for k in range(sides):
            self.face([ring[(k + 1) % sides], ring[k], mid], mat)

    def finish(self, smooth_angle=40):
        bm = self.bm
        if not self.uvs:
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
                if mat in FLAT_UV:
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
        return ob


def ellipse_rings(secs, sides, seed, jag):
    """secs: (z, half width, half depth, forward shift) -> rings around the vertical axis."""
    rings = []
    for i, (z, w, d, f) in enumerate(secs):
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides + math.pi / sides
            kk = 1 + jag * noise.noise(Vector((z * 1.9 + seed, k * 1.3, seed * 0.4)))
            ring.append(V(math.cos(a) * w * kk, f + math.sin(a) * d * kk, z))
        rings.append(ring)
    return rings


def ring_surface(secs, z, a):
    """Point + outward normal on an ellipse_rings body at height z, angle a (0 = right, pi/2 = front)."""
    s = sorted(secs)
    z = min(max(z, s[0][0]), s[-1][0])
    for s0, s1 in zip(s, s[1:]):
        if s0[0] <= z <= s1[0]:
            t = (z - s0[0]) / max(1e-4, s1[0] - s0[0])
            w, d, f = (s0[1] + (s1[1] - s0[1]) * t, s0[2] + (s1[2] - s0[2]) * t, s0[3] + (s1[3] - s0[3]) * t)
            break
    p = V(math.cos(a) * w, f + math.sin(a) * d, z)
    n = V(math.cos(a) / max(w, 0.1), math.sin(a) / max(d, 0.1), 0).normalized()
    return p, n


objects = []
leaf_jobs = []      # (part name, point, normal, size)


def leaves_on_tube(part, a, b, r, count, size, up_bias=0.25, arc=(0, 2 * math.pi)):
    for i in range(count):
        t = (i + 0.5) / count + rng.uniform(-0.12, 0.12)
        c = a + (b - a) * t
        d = (b - a).normalized()
        up = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
        s1 = d.cross(up).normalized()
        s2 = d.cross(s1).normalized()
        ang = rng.uniform(*arc)
        n = (s1 * math.cos(ang) + s2 * math.sin(ang)).normalized()
        leaf_jobs.append((part, c + n * r * 0.9, (n + Vector((0, 0, up_bias))).normalized(), size * rng.uniform(0.8, 1.15)))


# =====================================================================================  JOINTS
HIP_Z = 5.2
SIDES = (-1, 1)
HIP = {s: V(s * 0.8, 0.0, HIP_Z) for s in SIDES}
KNEE = {s: V(s * 0.92, 0.3, 3.0) for s in SIDES}
ANKLE = {s: V(s * 1.05, 0.0, 0.95) for s in SIDES}
WAIST = V(0, 0, 5.85)
SHOULDER = {s: V(s * 1.55, 0.25, 7.85) for s in SIDES}
ELBOW = {s: V(s * 1.9, 0.45, 5.75) for s in SIDES}
WRIST = {s: V(s * 2.0, 0.6, 3.85) for s in SIDES}
NECK = V(0, 0.4, 8.35)
SIDE_NAME = {-1: "L", 1: "R"}

# =====================================================================================  PELVIS
PELVIS = [(4.7, 1.0, 0.78, 0.0), (5.2, 1.22, 0.92, 0.0), (5.75, 1.18, 0.9, 0.0), (6.1, 1.08, 0.84, 0.02)]
pelvis = Part("Druid_Pelvis", V(0, 0, HIP_Z))
pelvis.loft(ellipse_rings(PELVIS, 9, 3, 0.07), "M_DruidBark")
objects.append(pelvis.finish())
for i in range(9):
    a = 2 * math.pi * i / 9 + rng.uniform(-0.2, 0.2)
    p, n = ring_surface(PELVIS, rng.uniform(5.0, 5.95), a)
    leaf_jobs.append(("Pelvis", p, (n + Vector((0, 0, 0.2))).normalized(), rng.uniform(0.85, 1.1)))

# =====================================================================================  TORSO
TORSO = [(5.85, 1.1, 0.84, 0.0), (6.5, 1.2, 0.92, 0.05), (7.1, 1.36, 0.98, 0.12), (7.7, 1.6, 1.0, 0.22),
         (8.1, 1.48, 0.9, 0.3), (8.38, 0.82, 0.62, 0.38), (8.6, 0.55, 0.5, 0.4)]
torso = Part("Druid_Torso", WAIST)
torso.loft(ellipse_rings(TORSO, 10, 5, 0.08), "M_DruidBark")
# shoulder burls: knotted caps over the joints
for s in SIDES:
    c = SHOULDER[s] + V(s * 0.05, 0, 0.12)
    torso.branch([c + V(-s * 0.35, 0, -0.1), c, c + V(s * 0.32, 0, -0.08)], [0.45, 0.55, 0.38], "M_DruidBark", sides=6,
                 jag=0.12, seed=11 + s)
# bark knots and broken branch stubs on the chest and back
for (lat, z, f_dir) in ((0.6, 7.3, 1), (-0.75, 6.6, 1), (0.25, 6.2, -1), (-0.4, 7.6, -1), (0.95, 6.9, -1)):
    a = math.atan2(f_dir * 0.8, lat)
    p, n = ring_surface(TORSO, z, a)
    torso.branch([p - n * 0.15, p + n * 0.35 + Vector((0, 0, 0.12)), p + n * 0.62 + Vector((0, 0, 0.3))],
                 [0.2, 0.13, 0.04], "M_DruidBark", sides=5, jag=0.15, seed=int(z * 10))
objects.append(torso.finish())
for i in range(28):
    a = 2 * math.pi * i / 28 + rng.uniform(-0.15, 0.15)
    z = 5.95 + (i * 0.618 % 1.0) * 2.4
    p, n = ring_surface(TORSO, z, a)
    leaf_jobs.append(("Torso", p, (n + Vector((0, 0, 0.3))).normalized(), rng.uniform(0.85, 1.15)))
# mantle of leaves over the shoulders
for s in SIDES:
    for k in range(3):
        c = SHOULDER[s] + V(s * rng.uniform(-0.3, 0.2), rng.uniform(-0.45, 0.45), 0.45)
        leaf_jobs.append(("Torso", c, V(s * 0.4, rng.uniform(-0.3, 0.3), 1).normalized(), rng.uniform(0.9, 1.15)))

# =====================================================================================  HEAD
HEADS = [(8.35, 0.52, 0.48, 0.45), (8.7, 0.84, 0.74, 0.5), (9.2, 1.0, 0.9, 0.55), (9.7, 0.95, 0.86, 0.5),
         (10.1, 0.74, 0.7, 0.45), (10.38, 0.38, 0.38, 0.4)]
head = Part("Druid_Head", NECK)
head.loft(ellipse_rings(HEADS, 10, 9, 0.06), "M_DruidBark")
# heavy brow: a ridge that slants down to the middle (a scowl over the sockets)
brow = []
for lat in (-0.85, -0.45, 0.0, 0.45, 0.85):
    zc = 9.62 - (0.85 - abs(lat)) * 0.12
    f = 1.42 + (0.85 - abs(lat)) * 0.2
    brow.append([V(lat, f - 0.25, zc + 0.16), V(lat, f + 0.08, zc + 0.06), V(lat, f + 0.02, zc - 0.12), V(lat, f - 0.28, zc - 0.08)])
head.loft(brow, "M_DruidBark")
# sunken sockets with dim sickly-green eyes deep inside
for s in SIDES:
    c = V(s * 0.4, 1.47, 9.3)
    nrm = V(s * 0.35, 1, 0.05)
    head.disc(c, nrm, 0.24, "M_DruidHollow", sides=7, depth=-0.06)
    head.disc(c + nrm.normalized() * 0.02, nrm, 0.1, "M_DruidEye", sides=6, depth=0.02)
# the nose: long, drooping, bulbous at the end (Patapim's proboscis), knotted bark
NOSE = [V(0, 1.38, 9.3), V(0, 1.86, 9.12), V(0, 2.2, 8.74), V(0, 2.26, 8.26), V(0, 2.12, 7.86)]
head.branch(NOSE, [0.26, 0.31, 0.4, 0.42, 0.22], "M_DruidBark", sides=7, jag=0.1, seed=21)
for s in SIDES:   # nostrils under the bulb
    head.disc(V(s * 0.15, 2.16, 7.98), V(s * 0.3, 0.6, -1), 0.07, "M_DruidHollow", sides=5, depth=0.04)
# cheeks bulging either side of the nose
for s in SIDES:
    c = V(s * 0.55, 1.28, 8.95)
    head.branch([c + V(-s * 0.15, -0.2, 0.1), c, c + V(s * 0.12, -0.2, -0.12)], [0.2, 0.28, 0.18], "M_DruidBark", sides=6,
                jag=0.1, seed=31 + s)
# mouth: a dark slit half hidden under the nose, with a thick lower lip
head.loft([[V(-0.4, 1.36, 8.72), V(-0.4, 1.36, 8.62), V(-0.4, 1.1, 8.62), V(-0.4, 1.1, 8.72)],
           [V(0.4, 1.36, 8.72), V(0.4, 1.36, 8.62), V(0.4, 1.1, 8.62), V(0.4, 1.1, 8.72)]], "M_DruidHollow")
head.branch([V(-0.42, 1.3, 8.58), V(0, 1.42, 8.52), V(0.42, 1.3, 8.58)], [0.07, 0.1, 0.07], "M_DruidBark", sides=5)
# antler crown: twisted branches forking up out of the skull
for k, (lat, f, lean) in enumerate(((-0.5, 0.3, -0.5), (0.55, 0.35, 0.5), (-0.15, 0.75, -0.15), (0.2, 0.05, 0.2),
                                    (-0.2, -0.15, -0.3))):
    b = V(lat, f, 10.0)
    m = b + V(lean * 0.9, rng.uniform(-0.2, 0.2), 0.75)
    t = m + V(lean * 1.1, rng.uniform(-0.3, 0.3), 0.6)
    head.branch([b, m, t], [0.16, 0.09, 0.025], "M_DruidBark", sides=5, jag=0.12, seed=40 + k)
    fork = m + (t - m) * 0.4
    head.branch([fork, fork + V(-lean * 0.4, rng.uniform(-0.3, 0.3), 0.55)], [0.06, 0.02], "M_DruidBark", sides=4, seed=50 + k)
objects.append(head.finish())
for i in range(14):    # leafy crown, swept back off the face
    a = math.radians(rng.uniform(-200, 20))      # right side, back, left side: never the front
    z = rng.uniform(9.75, 10.35)
    p, n = ring_surface(HEADS, z, a)
    leaf_jobs.append(("Head", p, (n + Vector((0, 0, 1.1))).normalized(), rng.uniform(0.85, 1.15)))
for s in SIDES:        # moss beard hanging off the jaw either side of the nose, tufts over the temples
    leaf_jobs.append(("Head", V(s * 0.55, 1.0, 8.5), V(s * 0.5, 0.4, -1).normalized(), 0.8))
    leaf_jobs.append(("Head", V(s * 0.95, 0.6, 9.6), V(s, -0.2, 0.5).normalized(), 0.9))

# =====================================================================================  ARMS
for s in SIDES:
    n = SIDE_NAME[s]
    up = Part(f"Druid_UpperArm_{n}", SHOULDER[s])
    mid = (SHOULDER[s] + ELBOW[s]) * 0.5 + V(s * 0.06, -0.05, 0)
    up.branch([SHOULDER[s], mid, ELBOW[s] + V(0, 0, -0.1)], [0.36, 0.3, 0.28], "M_DruidBark", sides=6, jag=0.12, seed=60 + s)
    objects.append(up.finish())
    leaves_on_tube(f"UpperArm_{n}", SHOULDER[s], ELBOW[s], 0.3, 5, 0.9, arc=(-0.6, 3.6) if s > 0 else (-0.6, 3.6))

    fo = Part(f"Druid_Forearm_{n}", ELBOW[s])
    mid = (ELBOW[s] + WRIST[s]) * 0.5 + V(s * 0.08, 0.04, 0)
    fo.branch([ELBOW[s] + V(0, 0, 0.12), mid, WRIST[s]], [0.3, 0.25, 0.2], "M_DruidBark", sides=6, jag=0.14, seed=70 + s)
    objects.append(fo.finish())
    leaves_on_tube(f"Forearm_{n}", ELBOW[s], WRIST[s], 0.25, 3, 0.75)

    # hand: palm faces the thigh (inward), width along the forward axis; long curled twig fingers
    hd = Part(f"Druid_Hand_{n}", WRIST[s])
    w = WRIST[s]
    palm = [[w + V(0, -0.15, 0.05), w + V(s * 0.12, 0, 0.05), w + V(0, 0.15, 0.05), w + V(-s * 0.12, 0, 0.05)],
            [w + V(0, -0.36, -0.4), w + V(s * 0.13, 0, -0.42), w + V(0, 0.36, -0.4), w + V(-s * 0.1, 0, -0.42)],
            [w + V(0, -0.42, -0.72), w + V(s * 0.12, 0, -0.74), w + V(0, 0.42, -0.72), w + V(-s * 0.09, 0, -0.74)]]
    hd.loft(palm, "M_DruidBark")
    for k, fo_ in enumerate((-0.32, -0.11, 0.11, 0.32)):
        L = (0.95, 1.15, 1.1, 0.9)[k]
        b = w + V(0, fo_, -0.7)
        pts = [b, b + V(-s * 0.05, fo_ * 0.15, -L * 0.4), b + V(-s * 0.22, fo_ * 0.2, -L * 0.75),
               b + V(-s * 0.45, fo_ * 0.25, -L * 0.98)]
        hd.branch(pts, [0.085, 0.07, 0.05, 0.015], "M_DruidBark", sides=5, jag=0.15, seed=80 + k + s * 10)
    b = w + V(-s * 0.02, 0.38, -0.3)   # thumb, toward the front
    hd.branch([b, b + V(-s * 0.08, 0.38, -0.32), b + V(-s * 0.3, 0.55, -0.6)], [0.08, 0.055, 0.015], "M_DruidBark",
              sides=5, jag=0.15, seed=95 + s)
    objects.append(hd.finish())

# =====================================================================================  LEGS + ROOT FEET
for s in SIDES:
    n = SIDE_NAME[s]
    th = Part(f"Druid_Thigh_{n}", HIP[s])
    mid = (HIP[s] + KNEE[s]) * 0.5 + V(s * 0.05, 0.05, 0)
    th.branch([HIP[s] + V(0, 0, 0.3), mid, KNEE[s] + V(0, 0, -0.05)], [0.78, 0.66, 0.58], "M_DruidBark", sides=8,
              jag=0.1, seed=100 + s)
    objects.append(th.finish())
    leaves_on_tube(f"Thigh_{n}", HIP[s], KNEE[s], 0.62, 8, 0.85, up_bias=0.15)

    sh = Part(f"Druid_Shin_{n}", KNEE[s])
    mid = (KNEE[s] + ANKLE[s]) * 0.5 + V(0, 0.08, 0)
    sh.branch([KNEE[s] + V(0, 0.02, 0.22), mid, ANKLE[s] + V(0, 0, 0.1)], [0.6, 0.5, 0.56], "M_DruidBark", sides=8,
              jag=0.12, seed=110 + s)
    # knee burl
    sh.branch([KNEE[s] + V(0, 0.25, 0.25), KNEE[s] + V(0, 0.48, 0.0), KNEE[s] + V(0, 0.3, -0.35)], [0.22, 0.3, 0.18],
              "M_DruidBark", sides=6, jag=0.15, seed=120 + s)
    objects.append(sh.finish())
    leaves_on_tube(f"Shin_{n}", KNEE[s], ANKLE[s], 0.52, 3, 0.7, arc=(0.2, 2.9))

    ft = Part(f"Druid_Foot_{n}", ANKLE[s])
    a = ANKLE[s]
    flare = [(0.95, 0.56), (0.62, 0.74), (0.32, 0.98)]
    ft.loft([[a + V(math.cos(t) * r * (1 + 0.12 * noise.noise(Vector((k, z * 3, s)))),
                   math.sin(t) * r * (1 + 0.12 * noise.noise(Vector((k, z * 3, s)))) + 0.08, z - 0.95)
              for k, t in enumerate(2 * math.pi * j / 8 + math.pi / 8 for j in range(8))] for z, r in flare],
            "M_DruidRoot", cap0=False)
    # root toes: a big toe forward, a fan either side, and heel roots; they curl into the dirt at the tips
    ROOTS = [(0, 2.25, 0.36), (-30 * s, 1.9, 0.28), (32 * s, 1.85, 0.27), (-62 * s, 1.5, 0.22), (66 * s, 1.4, 0.21),
             (165, 1.1, 0.24), (-150 * s, 0.95, 0.2)]
    for k, (deg, L, r0) in enumerate(ROOTS):
        t = math.radians(deg)
        d = V(math.sin(t), math.cos(t), 0)
        side = V(math.cos(t), -math.sin(t), 0)
        p0 = a + d * 0.55 + Vector((0, 0, -0.55))
        wig = rng.uniform(-0.18, 0.18)
        pts = [p0, p0 + d * L * 0.35 + side * wig + Vector((0, 0, -0.18)),
               p0 + d * L * 0.7 - side * wig * 0.6 + Vector((0, 0, -0.32)), p0 + d * L + side * wig * 0.4 + Vector((0, 0, -0.5))]
        ft.branch(pts, [r0, r0 * 0.75, r0 * 0.45, 0.04], "M_DruidRoot", sides=5, jag=0.15, seed=130 + k + s * 20)
        # a gnarled knuckle on the bigger toes
        if r0 > 0.25:
            kn = pts[1] + Vector((0, 0, r0 * 0.55))
            ft.spike(kn - Vector((0, 0, 0.1)), kn + Vector((0, 0, 0.16)) + d * 0.08, side, r0 * 0.5, "M_DruidRoot", sides=4)
    objects.append(ft.finish())
    leaf_jobs.append((f"Foot_{n}", a + V(s * 0.3, -0.25, -0.35), V(s, -0.3, 1).normalized(), 0.7))

# =====================================================================================  LEAF CLUMPS
CELLS = [(0, 0), (0.5, 0), (0, 0.5), (0.5, 0.5)]


def card(clump, c, right, down, w, h):
    cu, cv = CELLS[rng.randrange(4)]
    vs = [clump.v(c - right * w + down * h), clump.v(c + right * w + down * h), clump.v(c + right * w - down * h * 0.2),
          clump.v(c - right * w - down * h * 0.2)]
    clump.face(vs, "M_DruidLeaf", uv=[(cu, cv), (cu + 0.5, cv), (cu + 0.5, cv + 0.5), (cu, cv + 0.5)])


for idx, (pname, p, nrm, size) in enumerate(leaf_jobs):
    # a clump hangs from where it grows: one shingle card lying along the bark, two cards fanned out from it
    clump = Part(f"Druid_Leaf_{pname}_{idx:02d}", p)
    n = nrm.normalized()
    down = Vector((0, 0, -1)) - n * (-n.z)
    if down.length < 0.3:
        down = n.cross(Vector((1, 0, 0)))
    down.normalize()
    right = down.cross(n).normalized()
    root = p + n * size * 0.1
    card(clump, root, right, (down * 0.85 + n * 0.3).normalized(), size * 0.5, size * 0.85)
    for sgn in (-1, 1):
        r2 = (right * math.cos(0.9) + n * math.sin(0.9) * 0.6 * sgn).normalized()
        d2 = (down * 0.7 + n * 0.55 + right * 0.25 * sgn).normalized()
        card(clump, root + n * size * 0.05, r2, d2, size * 0.42, size * 0.75)
    objects.append(clump.finish())


def export(path, objs):
    if not EXPORT:
        return
    os.makedirs(OUT, exist_ok=True)
    for o in scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, path), use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, bake_anim=False, colors_type='NONE')


def tris(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


export("Druid.fbx", objects)
body_objs = [o for o in objects if "_Leaf_" not in o.name]
leaf_objs = [o for o in objects if "_Leaf_" in o.name]
print("Druid tris", tris(objects), "body", tris(body_objs), "leaves", tris(leaf_objs), "clumps", len(leaf_objs))
print({o.name: tris([o]) for o in body_objs})
print("OUT", OUT if EXPORT else "(no export)")
