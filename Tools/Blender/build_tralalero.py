"""
Tralalero Tralala boss, PSX dark fantasy (Docs/ART_DIRECTION.md).

    blender -b --factory-startup --python Tools/Blender/build_tralalero.py -- <abs out_dir>

Live (MCP): exec with TS_EXPORT=False to build into a dedicated "Tralalero" scene without exporting.

Tralalero.fbx - a scarred grey shark standing on three legs in worn, mud-caked blue sneakers (~2.2k tris).
                Rigid PS1-style segments so Unity can animate them procedurally:
                  Shark_Body        torso + head (upper jaw, eyes, gills, dorsal + pectoral fins)
                  Shark_Jaw         lower jaw, origin on the hinge
                  Shark_Tail0       tail stock, origin at the joint with the body
                  Shark_Tail1       caudal fin, origin at the joint with Tail0
                  Shark_Thigh{FL,FR,B}  origin on the hip
                  Shark_Shin{FL,FR,B}   origin on the knee
                  Shark_Foot{FL,FR,B}   sneaker, origin on the ankle
                Nose along Blender -Y (Unity +Z). Origin = ground under the body centre, soles on z=0.
                No brand marks on the shoes: a plain side stripe and a pale toe cap.

Materials (remapped in Unity by name): M_SharkSkin, M_SharkBelly, M_SharkEye, M_CrocMouth, M_CrocTeeth,
M_SneakerCloth, M_SneakerSole. UVs are box-projected in world metres (uniform texel density).
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = globals().get("TS_EXPORT", True)
OUT = os.path.abspath(argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "export"))
if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
else:
    scene = bpy.data.scenes.get("Tralalero") or bpy.data.scenes.new("Tralalero")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.window.scene = scene

MAT_RGB = {
    "M_SharkSkin": (0.17, 0.20, 0.23), "M_SharkBelly": (0.48, 0.46, 0.41), "M_SharkEye": (0.6, 0.25, 0.1),
    "M_CrocMouth": (0.25, 0.08, 0.08), "M_CrocTeeth": (0.62, 0.57, 0.46),
    "M_SneakerCloth": (0.14, 0.27, 0.36), "M_SneakerSole": (0.52, 0.5, 0.45),
}
# metres per texture tile (box projection)
TILE = {"M_SharkSkin": 1.6, "M_SharkBelly": 1.6, "M_CrocMouth": 1.0, "M_CrocTeeth": 0.6,
        "M_SneakerCloth": 0.5, "M_SneakerSole": 0.5}
FLAT_UV = ("M_SharkEye",)


def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*MAT_RGB[name], 1)
    return m


def P(f, lat, z):
    """forward distance, lateral (right +), height -> Blender coords (nose along -Y)."""
    return Vector((lat, -f, z))


class Part:
    """One mesh object being assembled with bmesh; faces carry material names."""

    def __init__(self, name, origin=Vector((0, 0, 0))):
        self.name = name
        self.origin = Vector(origin)
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def v(self, co):
        return self.bm.verts.new(Vector(co) - self.origin)

    def face(self, verts, mat):
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = self.mi(mat)
        return f

    def loft(self, rings, mat, cap0=True, cap1=True, closed=True, mats=None):
        """rings: list of lists of world points (same count). mats(i_ring, j_seg) -> material override."""
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

    def spike(self, base, tip, side, width, mat, sides=3):
        """pyramid (tooth / spike) from base centre to tip."""
        axis = (tip - base).normalized()
        s1 = side.normalized()
        s2 = axis.cross(s1).normalized()
        ring = [self.v(base + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides)) * width)
                for k in range(sides)]
        t = self.v(tip)
        for k in range(sides):
            self.face([ring[k], ring[(k + 1) % sides], t], mat)
        self.face(list(reversed(ring)), mat)

    def tube(self, a, b, r0, r1, mat, sides=6, mid=None):
        """Tube from a to b (radius r0 -> r1), optional bulge radius at the middle."""
        d = (b - a).normalized()
        up = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
        s1 = d.cross(up).normalized()
        s2 = d.cross(s1).normalized()
        pts = [(a, r0)] + ([((a + b) * 0.5, mid)] if mid else []) + [(b, r1)]
        rings = [[c + (s1 * math.cos(k * 2 * math.pi / sides + math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides + math.pi / sides)) * r
                  for k in range(sides)] for c, r in pts]
        self.loft(rings, mat)

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
                if ax == 0:
                    uv = (co.y, co.z)
                elif ax == 1:
                    uv = (co.x, co.z)
                else:
                    uv = (co.x, co.y)
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


def lerp_sections(secs, f):
    """linear interpolation of (f, a, b, c...) tuples at f (secs sorted by descending or ascending f)."""
    s = sorted(secs, key=lambda x: x[0])
    if f <= s[0][0]:
        return s[0][1:]
    for s0, s1 in zip(s, s[1:]):
        if s0[0] <= f <= s1[0]:
            t = (f - s0[0]) / (s1[0] - s0[0])
            return tuple(a + (b - a) * t for a, b in zip(s0[1:], s1[1:]))
    return s[-1][1:]


# ---- shark cross-section: 10 points, top and belly vertices, flatter belly than back
NS = 10
ANG = [math.pi / 2 + 2 * math.pi * j / NS for j in range(NS)]


def shark_ring(f, w, ht, hb, zc, seed=0, jag=0.0):
    ring = []
    for j, a in enumerate(ANG):
        s = math.sin(a)
        k = 1 + jag * noise.noise(Vector((f * 2.1 + seed, j * 1.7, seed * 0.3)))
        ring.append(P(f, math.cos(a) * w * k, zc + s * (ht if s > 0 else hb) * k))
    return ring


def seg_mid_sin(j):
    return math.sin((ANG[j] + ANG[(j + 1) % NS] + (2 * math.pi if j == NS - 1 else 0)) / 2)


def countershade(mouth_rings=()):
    """skin on the back and flanks, pale belly underneath; mouth roof inside the head."""
    def m(i, j):
        if j < 0:
            return "M_SharkSkin"
        s = seg_mid_sin(j)
        if i in mouth_rings and s < -0.8:
            return "M_CrocMouth"
        return "M_SharkBelly" if s < -0.3 else "M_SharkSkin"
    return m


def fin_ring(z, f_le, f_te, t, lat=0.0):
    """6-point airfoil section at height z, thickness along the lateral axis."""
    c = f_le - f_te
    return [P(f_le, lat, z), P(f_le - c * 0.25, lat + t * 0.6, z), P(f_le - c * 0.62, lat + t * 0.4, z),
            P(f_te, lat, z), P(f_le - c * 0.62, lat - t * 0.4, z), P(f_le - c * 0.25, lat - t * 0.6, z)]


def wing_ring(lat, f_le, f_te, z, t):
    """6-point airfoil section at a lateral station, thickness along z (pectoral fins)."""
    c = f_le - f_te
    return [P(f_le, lat, z), P(f_le - c * 0.22, lat, z + t * 0.6), P(f_le - c * 0.6, lat, z + t * 0.4),
            P(f_te, lat, z + 0.01), P(f_le - c * 0.62, lat, z - t * 0.25), P(f_le - c * 0.18, lat, z - t * 0.35)]


objects = []

# =====================================================================================  BODY + HEAD
BODY = [  # f, half width, height above centre, depth below centre, centre z
    (3.75, 0.05, 0.04, 0.03, 2.00), (3.45, 0.30, 0.27, 0.12, 2.03), (3.0, 0.52, 0.48, 0.18, 2.07),
    (2.4, 0.70, 0.66, 0.24, 2.11), (1.7, 0.83, 0.80, 0.34, 2.13), (1.1, 0.89, 0.90, 0.84, 2.12),
    (0.3, 0.90, 0.92, 0.88, 2.10), (-0.5, 0.80, 0.84, 0.78, 2.12), (-1.3, 0.60, 0.66, 0.56, 2.16)]
body = Part("Shark_Body")
body.loft([shark_ring(*s, seed=1, jag=0.03 if 0 < i < len(BODY) - 1 else 0) for i, s in enumerate(BODY)],
          "M_SharkSkin", cap1=False, mats=countershade(mouth_rings=(1, 2, 3)))


def body_at(f):
    return lerp_sections(BODY, f)   # (w, ht, hb, zc)


# ---- eyes: small dead discs with a dim ember glint, under an angry ridge
for s in (-1, 1):
    f = 2.62
    w, ht, hb, zc = body_at(f)
    a = math.radians(32)
    c = P(f, s * math.cos(a) * w * 1.0, zc + math.sin(a) * ht * 0.98)
    out = Vector((s, 0, 0.35)).normalized()
    s1 = Vector((0, 1, 0))
    s2 = out.cross(s1).normalized()
    rings = []
    for q, (r, h) in enumerate(((0.12, 0.0), (0.105, 0.04), (0.06, 0.07))):
        rings.append([c + out * h + (s1 * math.cos(k * 2 * math.pi / 6) + s2 * math.sin(k * 2 * math.pi / 6)) * r for k in range(6)])
    body.loft(rings, "M_SharkEye", cap0=False)
    # brow ridge slanting down toward the snout
    body.loft([[P(2.9, s * 0.42, 2.5), P(2.86, s * 0.5, 2.6), P(2.9, s * 0.58, 2.5)],
               [P(2.4, s * 0.58, 2.62), P(2.38, s * 0.66, 2.72), P(2.4, s * 0.72, 2.58)]], "M_SharkSkin", closed=False)

# ---- gill slits: thin dark boxes standing proud of the flank
for s in (-1, 1):
    for g in range(5):
        f = 1.32 - g * 0.13
        w, ht, hb, zc = body_at(f)
        rings = []
        for zf, lw in ((0.62, 0.0), (0.2, 0.0), (-0.25, 0.0)):
            z = zc + zf * (ht if zf > 0 else hb) * 0.75
            lat = s * (math.sqrt(max(0.0, 1 - (zf * 0.75) ** 2)) * w + 0.012)
            rings.append([P(f + 0.025, lat, z), P(f - 0.025, lat, z), P(f - 0.025, lat + s * 0.03, z), P(f + 0.025, lat + s * 0.03, z)])
        body.loft(rings, "M_CrocMouth")

# ---- dorsal fin (tall, swept, notched trailing edge from old fights)
body.loft([fin_ring(2.92, 0.62, -0.58, 0.15), fin_ring(3.4, 0.1, -0.78, 0.1), fin_ring(3.78, -0.42, -0.86, 0.06),
           fin_ring(3.98, -0.78, -0.9, 0.025)], "M_SharkSkin")
# ---- pectoral fins (drooping wings above the front legs)
for s in (-1, 1):
    body.loft([wing_ring(s * 0.72, 1.3, 0.55, 1.78, 0.12), wing_ring(s * 1.25, 0.82, 0.25, 1.5, 0.07),
               wing_ring(s * 1.72, 0.25, 0.02, 1.2, 0.025)], "M_SharkSkin",
              mats=lambda i, j: "M_SharkBelly" if j in (3, 4, 5) else "M_SharkSkin")

# ---- upper teeth: a row of triangular blades hanging from the mouth edge, bigger at the front
for s in (-1, 1):
    n = 9
    for k in range(n):
        f = 1.8 + k * (3.25 - 1.8) / (n - 1)
        w, ht, hb, zc = body_at(f)
        a = math.radians(234)
        base = P(f, s * abs(math.cos(a)) * w * 0.98, zc + math.sin(a) * hb * 0.9)
        L = 0.13 + 0.07 * (k / (n - 1)) + 0.03 * (k % 2)
        body.spike(base, base + Vector((s * 0.02, 0, -L)), Vector((0, 1, 0)), 0.05, "M_CrocTeeth")
for lat in (-0.12, 0.0, 0.12):
    base = P(3.33, lat, 1.93)
    body.spike(base, base + Vector((0, -0.03, -0.17)), Vector((1, 0, 0)), 0.045, "M_CrocTeeth")
objects.append(body.finish())

# =====================================================================================  JAW
HINGE = P(1.45, 0, 1.8)
JAW = [(1.35, 0.80, 0.62, 1.80), (1.9, 0.72, 0.56, 1.82), (2.5, 0.58, 0.44, 1.86), (3.0, 0.42, 0.32, 1.88),
       (3.3, 0.22, 0.17, 1.89)]
jaw = Part("Shark_Jaw", HINGE)
jr = []
for f, w, d, zm in JAW:
    jr.append([P(f, -w, zm), P(f, -w * 0.95, zm - d * 0.55), P(f, -w * 0.55, zm - d), P(f, w * 0.55, zm - d),
               P(f, w * 0.95, zm - d * 0.55), P(f, w, zm), P(f, w * 0.55, zm - 0.06), P(f, -w * 0.55, zm - 0.06)])
jaw.loft(jr, "M_SharkBelly",
         mats=lambda i, j: "M_CrocMouth" if j >= 5 else ("M_SharkSkin" if j in (0, 4) else "M_SharkBelly"))
for s in (-1, 1):
    n = 8
    for k in range(n):
        f = 1.85 + k * (3.15 - 1.85) / (n - 1)
        w, d, zm = lerp_sections(JAW, f)
        base = P(f, s * w * 0.8, zm - 0.05)
        L = 0.11 + 0.05 * (k / (n - 1)) + 0.03 * ((k + 1) % 2)
        jaw.spike(base, base + Vector((-s * 0.02, 0, L)), Vector((0, 1, 0)), 0.045, "M_CrocTeeth")
objects.append(jaw.finish())

# =====================================================================================  TAIL
TAIL0 = [(-1.15, 0.64, 0.70, 0.60, 2.15), (-1.9, 0.44, 0.50, 0.42, 2.21), (-2.6, 0.27, 0.33, 0.25, 2.28),
         (-3.25, 0.13, 0.18, 0.13, 2.35)]
tail0 = Part("Shark_Tail0", P(-1.25, 0, 2.16))
tail0.loft([shark_ring(*s, seed=7, jag=0.03 if i == 1 else 0) for i, s in enumerate(TAIL0)], "M_SharkSkin",
           cap0=False, mats=countershade())
# second dorsal + anal fin
tail0.loft([fin_ring(2.62, -2.2, -2.6, 0.07), fin_ring(2.98, -2.55, -2.7, 0.02)], "M_SharkSkin")
tail0.loft([fin_ring(1.95, -2.3, -2.62, 0.06), fin_ring(1.64, -2.62, -2.74, 0.02)], "M_SharkSkin")
objects.append(tail0.finish())

tail1 = Part("Shark_Tail1", P(-3.15, 0, 2.35))
tail1.loft([shark_ring(-3.1, 0.15, 0.2, 0.14, 2.35), shark_ring(-3.55, 0.09, 0.14, 0.1, 2.38)], "M_SharkSkin",
           cap0=False, mats=countershade())
# caudal fin: tall swept upper lobe, short lower lobe, ragged trailing edge
tail1.loft([fin_ring(2.42, -3.32, -3.75, 0.09), fin_ring(3.0, -3.78, -4.12, 0.06), fin_ring(3.52, -4.22, -4.46, 0.035),
            fin_ring(3.85, -4.56, -4.64, 0.015)], "M_SharkSkin")
tail1.loft([fin_ring(2.32, -3.38, -3.75, 0.08), fin_ring(1.86, -3.64, -3.94, 0.05), fin_ring(1.46, -3.9, -4.06, 0.02)],
           "M_SharkSkin")
objects.append(tail1.finish())

# =====================================================================================  LEGS + SNEAKERS
LEGS = {  # hip, knee, ankle, thigh radius
    "FL": (P(0.9, -0.52, 1.5), P(1.15, -0.6, 0.88), P(0.95, -0.6, 0.3), 0.3),
    "FR": (P(0.9, 0.52, 1.5), P(1.15, 0.6, 0.88), P(0.95, 0.6, 0.3), 0.3),
    "B": (P(-0.85, 0.0, 1.5), P(-0.6, 0.0, 0.88), P(-0.8, 0.0, 0.3), 0.32),
}


def sneaker(part, ankle, k=1.0):
    """A chunky, worn running shoe around the ankle: rubber sole, cloth upper, toe cap, stripe, laces."""
    fa, lat = -ankle.y, ankle.x

    def S(df, dl, z):
        return P(fa + df * k, lat + dl * k, z * k)
    # sole: tread slab, wider at the ball of the foot, round toe
    sole = [(-0.18, 0.12), (-0.1, 0.145), (0.1, 0.155), (0.3, 0.15), (0.42, 0.12), (0.49, 0.06)]
    part.loft([[S(f, -w, 0.0), S(f, w, 0.0), S(f, w, 0.075), S(f, -w, 0.075)] for f, w in sole], "M_SneakerSole")
    # upper: collar at the heel, low toe box
    up = [(-0.17, 0.115, 0.3), (-0.06, 0.13, 0.31), (0.1, 0.14, 0.25), (0.26, 0.135, 0.19), (0.38, 0.11, 0.14), (0.46, 0.06, 0.1)]
    part.loft([[S(f, -w, 0.07), S(f, -w * 0.97, h * 0.62), S(f, -w * 0.6, h), S(f, w * 0.6, h), S(f, w * 0.97, h * 0.62), S(f, w, 0.07)]
               for f, w, h in up], "M_SneakerCloth", mats=lambda i, j: "M_SneakerSole" if i >= 4 else "M_SneakerCloth")
    # side stripe: one plain slanted band each side (no brand marks)
    for s in (-1, 1):
        a0, a1 = (-0.08, 0.1), (0.22, 0.2)
        ring0 = [S(a0[0], s * 0.137, a0[1]), S(a0[0] + 0.05, s * 0.137, a0[1] + 0.035), S(a0[0] + 0.05, s * 0.147, a0[1] + 0.035), S(a0[0], s * 0.147, a0[1])]
        ring1 = [S(a1[0], s * 0.14, a1[1]), S(a1[0] + 0.05, s * 0.14, a1[1] + 0.03), S(a1[0] + 0.05, s * 0.15, a1[1] + 0.03), S(a1[0], s * 0.15, a1[1])]
        part.loft([ring0, ring1], "M_SneakerSole")
    # laces across the instep, tongue sticking up at the collar
    for f in (0.04, 0.13, 0.22):
        h = lerp_sections([(u[0], u[2]) for u in up], f)[0]
        part.loft([[S(f - 0.012, -0.08, h + 0.005), S(f + 0.012, -0.08, h + 0.005), S(f + 0.012, -0.08, h + 0.03), S(f - 0.012, -0.08, h + 0.03)],
                   [S(f - 0.012, 0.08, h + 0.005), S(f + 0.012, 0.08, h + 0.005), S(f + 0.012, 0.08, h + 0.03), S(f - 0.012, 0.08, h + 0.03)]],
                  "M_SneakerSole")
    part.loft([[S(-0.02, -0.07, 0.3), S(-0.02, 0.07, 0.3)], [S(-0.07, -0.06, 0.37), S(-0.07, 0.06, 0.37)]], "M_SneakerCloth", closed=False)
    # heel tab
    part.loft([[S(-0.19, -0.05, 0.22), S(-0.19, 0.05, 0.22), S(-0.17, 0.05, 0.22), S(-0.17, -0.05, 0.22)],
               [S(-0.2, -0.04, 0.34), S(-0.2, 0.04, 0.34), S(-0.18, 0.04, 0.34), S(-0.18, -0.04, 0.34)]], "M_SneakerSole")


leg_objs = []
for key, (hip, knee, ankle, r) in LEGS.items():
    thigh = Part(f"Shark_Thigh{key}", hip)
    thigh.tube(hip + Vector((0, 0, 0.12)), knee, r, r * 0.72, "M_SharkSkin", sides=6, mid=r * 1.05)
    leg_objs.append(thigh.finish())
    shin = Part(f"Shark_Shin{key}", knee)
    shin.tube(knee + (knee - ankle).normalized() * 0.08, ankle + Vector((0, 0, 0.08)), r * 0.74, r * 0.5, "M_SharkSkin", sides=6, mid=r * 0.66)
    leg_objs.append(shin.finish())
    foot = Part(f"Shark_Foot{key}", ankle)
    sneaker(foot, ankle - Vector((0, 0, ankle.z)), 1.38 if key != "B" else 1.45)
    leg_objs.append(foot.finish(smooth_angle=30))
objects += leg_objs


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


export("Tralalero.fbx", objects)
print("Tralalero tris", tris(objects), {o.name: tris([o]) for o in objects})
print("OUT", OUT if EXPORT else "(no export)")
