"""
Crocodile boss + its bombs, PSX dark fantasy (Docs/ART_DIRECTION.md).

    blender -b --factory-startup --python Tools/Blender/build_crocodile.py -- <out_dir>

Live (MCP): exec with TS_EXPORT=False to build into a dedicated "Crocodile" scene without exporting.

Crocodile.fbx  - a rusted four-engine high-wing bomber whose nose is a crocodile head (~2.4k tris).
                  Separate parts so Unity can animate them procedurally:
                    Bomber_Body   fuselage, scaly neck, cockpit, tail, wings, nacelles, landing gear
                    Bomber_Head   upper skull, brow, eyes, nostrils, upper teeth
                    Bomber_Jaw    lower jaw, origin on the hinge (rotate around local X to open)
                    Bomber_Prop0-3 three-blade propellers, origin on the hub (spin around the nose axis)
                  Nose points along Blender -Y (Unity +Z). Origin = ground under the wing root, wheels on z=0.
AerialBomb.fbx  - finned drop bomb (~130 tris), origin at its centre, nose along -Y.
LivingBomb.fbx  - the same bomb grown legs, a grin and red eyes (~420 tris). Parts: LBomb_Body,
                  LBomb_Leg0-3 (origin on the hip), origin on the ground.

Materials (remapped in Unity by name): M_BomberMetal, M_BomberGlass, M_Iron, M_CrocHide, M_CrocMouth,
M_CrocTeeth, M_CrocEye, M_BombIron, M_BombBand, M_BombEye.
UVs are box-projected in world metres (uniform texel density across the model).
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
    scene = bpy.data.scenes.get("Crocodile") or bpy.data.scenes.new("Crocodile")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.window.scene = scene

MAT_RGB = {
    "M_BomberMetal": (0.30, 0.31, 0.29), "M_BomberGlass": (0.10, 0.09, 0.16), "M_Iron": (0.12, 0.11, 0.11),
    "M_CrocHide": (0.20, 0.25, 0.14), "M_CrocMouth": (0.25, 0.08, 0.08), "M_CrocTeeth": (0.62, 0.57, 0.46),
    "M_CrocEye": (0.75, 0.70, 0.20), "M_BombIron": (0.17, 0.18, 0.13), "M_BombBand": (0.35, 0.09, 0.07),
    "M_BombEye": (0.9, 0.15, 0.08),
}
# metres per texture tile (box projection)
TILE = {"M_BomberMetal": 2.4, "M_CrocHide": 1.4, "M_CrocMouth": 1.0, "M_CrocTeeth": 0.6, "M_Iron": 1.5,
        "M_BombIron": 0.8, "M_BombBand": 0.8}


def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*MAT_RGB[name], 1)
    return m


def P(f, lat, z):
    """forward distance, lateral (right +), height -> Blender coords (nose along -Y)."""
    return Vector((lat, -f, z))


class Part:
    """One mesh object being assembled with bmesh; faces carry material names."""

    def __init__(self, name, origin=Vector((0, 0, 0)), xf=None):
        self.name = name
        self.xf = xf or (lambda co: Vector(co))
        self.origin = self.xf(origin)
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def v(self, co):
        return self.bm.verts.new(self.xf(co) - self.origin)

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
        """3-sided pyramid (tooth / spike) from base centre to tip."""
        axis = (tip - base).normalized()
        s1 = side.normalized()
        s2 = axis.cross(s1).normalized()
        ring = [self.v(base + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides)) * width)
                for k in range(sides)]
        t = self.v(tip)
        for k in range(sides):
            self.face([ring[k], ring[(k + 1) % sides], t], mat)
        self.face(list(reversed(ring)), mat)

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
                if mat in ("M_CrocEye", "M_BomberGlass", "M_BombEye"):
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


def lathe_rings(profile, sides, wz=1.0, jag=0.0, seed=0, phase=0.0, lat0=0.0):
    """profile: [(f, r, zc)] along the forward axis -> rings of points."""
    rings = []
    for i, (f, r, zc) in enumerate(profile):
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides + phase
            k = 1 + jag * noise.noise(Vector((i * 1.7 + seed, j * 2.3, seed * 0.37)))
            ring.append(P(f, lat0 + math.cos(a) * r * k, zc + math.sin(a) * r * k * wz))
        rings.append(ring)
    return rings


def lerp_sections(secs, f):
    """linear interpolation of (f, a, b, c...) tuples at f."""
    if f <= secs[0][0]:
        return secs[0][1:]
    for s0, s1 in zip(secs, secs[1:]):
        if s0[0] <= f <= s1[0]:
            t = (f - s0[0]) / (s1[0] - s0[0])
            return tuple(a + (b - a) * t for a, b in zip(s0[1:], s1[1:]))
    return secs[-1][1:]


objects = []

# =====================================================================================  BOMBER
body = Part("Bomber_Body")
# ---- fuselage (8-sided, a little taller than wide; the tail rises like a real bomber)
fus = [(-4.75, 0.14, 2.32), (-4.3, 0.30, 2.22), (-3.3, 0.54, 2.06), (-2.0, 0.74, 1.9), (-0.8, 0.86, 1.8),
       (0.6, 0.90, 1.76), (1.6, 0.88, 1.75), (2.35, 0.84, 1.74)]
body.loft(lathe_rings(fus, 8, wz=1.06, phase=math.pi / 8), "M_BomberMetal", cap1=False)
# ---- scaly neck collar over the joint (croc hide, jagged)
neck = [(1.85, 0.90, 1.75), (2.15, 1.02, 1.74), (2.45, 1.08, 1.72), (2.7, 1.0, 1.72)]
body.loft(lathe_rings(neck, 10, wz=1.04, jag=0.07, seed=4), "M_CrocHide", cap0=False, cap1=False)
# dorsal scutes running down the neck onto the back
for k in range(6):
    f = 2.5 - k * 0.42
    zc = lerp_sections([(s[0], s[2], s[1]) for s in fus + [(2.6, 1.8, 0.86)]], f)
    top = zc[0] + zc[1] * 1.06
    body.spike(P(f, 0, top - 0.04), P(f - 0.18, 0, top + 0.2 - k * 0.02), Vector((1, 0, 0)), 0.11, "M_CrocHide")
# ---- cockpit canopy (dim amber lit inside)
can = []
for f, w, z0, z1 in ((0.55, 0.28, 2.48, 2.6), (0.95, 0.44, 2.5, 2.92), (1.7, 0.5, 2.46, 2.95), (2.15, 0.38, 2.4, 2.72)):
    can.append([P(f, -w, z0), P(f, -w * 0.8, z1 - 0.06), P(f, 0, z1), P(f, w * 0.8, z1 - 0.06), P(f, w, z0)])
body.loft(can, "M_BomberGlass", closed=False)
body.face([body.v(p) for p in can[0]] , "M_BomberGlass")
# window frame bars
for f in (0.95, 1.35, 1.7):
    w, z0, z1 = lerp_sections([(0.55, 0.28, 2.48, 2.6), (0.95, 0.44, 2.5, 2.92), (1.7, 0.5, 2.46, 2.95), (2.15, 0.38, 2.4, 2.72)], f)
    body.loft([[P(f - 0.03, -w - 0.02, z0), P(f - 0.03, 0, z1 + 0.03), P(f - 0.03, w + 0.02, z0)],
               [P(f + 0.03, -w - 0.02, z0), P(f + 0.03, 0, z1 + 0.03), P(f + 0.03, w + 0.02, z0)]], "M_Iron", closed=False)

# ---- wings: high-mounted, slight sweep and dihedral, 6-point airfoil
def airfoil(lat, f_le, f_te, z, t):
    c = f_le - f_te
    return [P(f_le, lat, z), P(f_le - c * 0.22, lat, z + t * 0.62), P(f_le - c * 0.6, lat, z + t * 0.42),
            P(f_te, lat, z + 0.01), P(f_le - c * 0.62, lat, z - t * 0.22), P(f_le - c * 0.18, lat, z - t * 0.36)]


WING = [(-5.2, 0.38, -0.5, 2.78, 0.09), (-3.9, 0.6, -0.68, 2.69, 0.14), (-2.1, 0.86, -0.86, 2.6, 0.2),
        (-0.5, 1.02, -0.96, 2.55, 0.24), (0.5, 1.02, -0.96, 2.55, 0.24), (2.1, 0.86, -0.86, 2.6, 0.2),
        (3.9, 0.6, -0.68, 2.69, 0.14), (5.2, 0.38, -0.5, 2.78, 0.09)]
body.loft([airfoil(*w) for w in WING], "M_BomberMetal")
# ---- tailplane + fin
TAIL = [(-2.0, -3.95, -4.55, 2.36, 0.07), (-0.3, -3.75, -4.75, 2.3, 0.1), (0.3, -3.75, -4.75, 2.3, 0.1),
        (2.0, -3.95, -4.55, 2.36, 0.07)]
body.loft([airfoil(*w) for w in TAIL], "M_BomberMetal")
fin = []
for z, f_le, f_te, t in ((2.3, -3.45, -4.8, 0.12), (3.2, -4.05, -4.95, 0.09), (3.95, -4.5, -5.05, 0.06)):
    c = f_le - f_te
    fin.append([P(f_le, 0, z), P(f_le - c * 0.25, t * 0.6, z), P(f_le - c * 0.62, t * 0.4, z), P(f_te, 0, z),
                P(f_le - c * 0.62, -t * 0.4, z), P(f_le - c * 0.25, -t * 0.6, z)])
body.loft(fin, "M_BomberMetal")

# ---- engine nacelles (iron cowling ring at the front, exhaust stubs)
ENGINES = [-3.9, -2.1, 2.1, 3.9]
PROP_F = 1.98
for e, lat in enumerate(ENGINES):
    zw = lerp_sections([(w[0], w[3]) for w in WING], lat)[0]
    zc = zw - 0.14
    prof = [(-1.05, 0.10, zc + 0.05), (-0.45, 0.28, zc), (0.5, 0.34, zc), (1.45, 0.36, zc), (1.6, 0.40, zc),
            (1.9, 0.38, zc), (1.95, 0.24, zc)]
    body.loft(lathe_rings(prof, 8, phase=math.pi / 8, lat0=lat), "M_BomberMetal",
              mats=lambda i, j: "M_Iron" if i >= 4 or i == -1 else "M_BomberMetal")
    for s in (-1, 1):
        a = P(0.9, lat + s * 0.3, zc - 0.12)
        body.loft([[a + Vector((dx, 0, dz)) for dx, dz in ((-0.05, -0.05), (0.05, -0.05), (0.05, 0.05), (-0.05, 0.05))],
                   [a + Vector((s * 0.12 + dx, 0.35, dz - 0.04)) for dx, dz in ((-0.04, -0.04), (0.04, -0.04), (0.04, 0.04), (-0.04, 0.04))]],
                  "M_Iron")

# ---- landing gear: main legs under the inner engines + a nose wheel under the neck
def wheel(part, f, lat, z, r, w):
    rings = []
    for x in (lat - w / 2, lat + w / 2):
        rings.append([P(f + math.cos(a) * r, x, z + math.sin(a) * r) for a in (k * 2 * math.pi / 8 for k in range(8))])
    part.loft(rings, "M_Iron")


def strut(part, a, b, t):
    d = (b - a).normalized()
    s1 = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized() * t
    s2 = d.cross(s1).normalized() * t
    part.loft([[a + s1, a + s2, a - s1, a - s2], [b + s1, b + s2, b - s1, b - s2]], "M_Iron")


for lat in (-2.1, 2.1):
    wheel(body, 0.35, lat, 0.46, 0.46, 0.3)
    strut(body, P(0.35, lat, 2.2), P(0.35, lat, 0.46), 0.07)
    strut(body, P(-0.45, lat, 2.25), P(0.3, lat, 0.75), 0.045)
wheel(body, 1.55, 0, 0.3, 0.3, 0.2)
strut(body, P(1.5, 0, 1.0), P(1.55, 0, 0.3), 0.05)
objects.append(body.finish())

# ---- the croc head is oversized (the reference reads croc first, plane second): scale about the neck
HS = 1.32


def HX(co):
    co = Vector(co)
    a = P(2.3, 0, 1.75)
    return a + (co - a) * HS


# ---- croc head (upper): lofted sections, mouth line zm
HEAD = [  # f, half width, height above mouth, mouth z
    (2.25, 0.80, 1.02, 1.42), (2.9, 0.80, 0.98, 1.44), (3.45, 0.62, 0.72, 1.47), (4.1, 0.46, 0.5, 1.5),
    (4.8, 0.37, 0.42, 1.52), (5.25, 0.41, 0.45, 1.52), (5.6, 0.26, 0.3, 1.55)]
head = Part("Bomber_Head", xf=HX)


def head_ring(f, w, h, zm):
    return [P(f, -w, zm), P(f, -w * 0.97, zm + h * 0.45), P(f, -w * 0.72, zm + h * 0.88), P(f, -w * 0.25, zm + h),
            P(f, w * 0.25, zm + h), P(f, w * 0.72, zm + h * 0.88), P(f, w * 0.97, zm + h * 0.45), P(f, w, zm),
            P(f, w * 0.5, zm + 0.07), P(f, -w * 0.5, zm + 0.07)]


rings = []
for i, (f, w, h, zm) in enumerate(HEAD):
    r = head_ring(f, w, h, zm)
    # lumpy hide: jitter the upper points
    for j in range(1, 7):
        r[j] += Vector((0, 0, 0.035 * noise.noise(Vector((i * 1.9, j * 1.3, 0.7)))))
    rings.append(r)
head.loft(rings, "M_CrocHide", mats=lambda i, j: "M_CrocMouth" if j >= 7 else "M_CrocHide")
# eye turrets, brows, glowing eyes
for s in (-1, 1):
    ec = P(2.85, s * 0.5, 2.44)
    head.loft(lathe_rings([(2.6, 0.2, 2.38), (2.75, 0.24, 2.44), (2.95, 0.22, 2.46), (3.1, 0.12, 2.42)], 6, lat0=s * 0.5),
              "M_CrocHide", cap0=False)
    eye = []
    for k in range(5):
        a = -math.pi / 2 + math.pi * k / 4
        eye.append([ec + Vector((math.cos(b) * math.cos(a) * 0.15, math.sin(a) * 0.15, 0.06 + math.sin(b) * math.cos(a) * 0.13))
                    for b in (q * 2 * math.pi / 8 for q in range(8))])
    head.loft(eye, "M_CrocEye")
    # heavy brow ridge (angry slant toward the snout)
    head.loft([[P(2.6, s * 0.3, 2.5), P(2.62, s * 0.55, 2.66), P(2.6, s * 0.78, 2.5)],
               [P(3.05, s * 0.32, 2.48), P(3.0, s * 0.5, 2.6), P(3.0, s * 0.72, 2.46)]], "M_CrocHide", closed=False)
    # nostril bump
    head.loft(lathe_rings([(5.25, 0.08, 1.98), (5.38, 0.1, 2.0), (5.5, 0.05, 1.97)], 5, lat0=s * 0.14), "M_CrocHide")
# upper teeth: two rows hanging along the mouth line, bigger canines
for s in (-1, 1):
    n = 11
    for k in range(n):
        f = 2.75 + k * (5.45 - 2.75) / (n - 1)
        w, h, zm = lerp_sections(HEAD, f)
        big = k in (3, 9)
        L = 0.3 if big else 0.16 + 0.05 * (k % 2)
        base = P(f, s * (w - 0.05), zm + 0.05)
        head.spike(base, base + Vector((s * 0.02, 0, -L)), Vector((0, 1, 0)), 0.06 if big else 0.045, "M_CrocTeeth")
objects.append(head.finish())

# ---- lower jaw, origin on the hinge
HINGE = P(2.45, 0, 1.44)
JAW = [(2.3, 0.74, 0.46), (3.0, 0.68, 0.42), (3.8, 0.52, 0.32), (4.6, 0.4, 0.27), (5.3, 0.38, 0.24), (5.6, 0.24, 0.16)]
jaw = Part("Bomber_Jaw", HINGE, xf=HX)
jr = []
for f, w, d in JAW:
    zm = 1.44 + (f - 2.3) * 0.03
    jr.append([P(f, -w, zm), P(f, -w * 0.92, zm - d * 0.6), P(f, -w * 0.5, zm - d), P(f, w * 0.5, zm - d),
               P(f, w * 0.92, zm - d * 0.6), P(f, w, zm), P(f, w * 0.55, zm - 0.07), P(f, -w * 0.55, zm - 0.07)])
jaw.loft(jr, "M_CrocHide", mats=lambda i, j: "M_CrocMouth" if j >= 5 else "M_CrocHide")
for s in (-1, 1):
    n = 10
    for k in range(n):
        f = 2.9 + k * (5.45 - 2.9) / (n - 1)
        w, d = lerp_sections([(j[0], j[1], j[2]) for j in JAW], f)
        zm = 1.44 + (f - 2.3) * 0.03
        big = k in (2, 8)
        L = 0.26 if big else 0.13 + 0.04 * (k % 2)
        base = P(f + 0.08, s * (w - 0.08), zm - 0.04)
        jaw.spike(base, base + Vector((s * -0.02, 0, L)), Vector((0, 1, 0)), 0.055 if big else 0.04, "M_CrocTeeth")
objects.append(jaw.finish())

# ---- propellers (3 paddle blades + spinner), origin on the hub
for e, lat in enumerate(ENGINES):
    zw = lerp_sections([(w[0], w[3]) for w in WING], lat)[0]
    hub = P(PROP_F, lat, zw - 0.14)
    prop = Part(f"Bomber_Prop{e}", hub)
    prop.loft(lathe_rings([(PROP_F - 0.05, 0.17, hub.z), (PROP_F + 0.12, 0.15, hub.z), (PROP_F + 0.3, 0.06, hub.z)], 6,
                          lat0=lat), "M_Iron", cap1=True)
    for b in range(3):
        a = b * 2 * math.pi / 3 + (0.3 if e % 2 else 0)
        radial = Vector((math.cos(a), 0, math.sin(a)))
        tang = Vector((-math.sin(a), 0, math.cos(a)))
        pitch = math.radians(24)
        rings = []
        for r, w in ((0.12, 0.08), (0.45, 0.13), (0.78, 0.1), (0.86, 0.05)):
            c = hub + radial * r + Vector((0, -0.02, 0))
            chord = (tang * math.cos(pitch) + Vector((0, 1, 0)) * math.sin(pitch)) * w
            thick = (Vector((0, 1, 0)) * math.cos(pitch) - tang * math.sin(pitch)) * 0.02
            rings.append([c - chord, c + thick, c + chord, c - thick])
        prop.loft(rings, "M_Iron")
    objects.append(prop.finish(smooth_angle=20))


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


export("Crocodile.fbx", objects)
print("Crocodile tris", tris(objects))

# =====================================================================================  AERIAL BOMB
BOMB_PROF = [(-0.46, 0.07), (-0.32, 0.16), (-0.1, 0.2), (0.25, 0.2), (0.42, 0.16), (0.54, 0.08), (0.58, 0.02)]


def bomb_body(part, scale, z, lat=0.0, f0=0.0, band=True):
    prof = [(f0 + f * scale, r * scale, z) for f, r in BOMB_PROF]
    part.loft(lathe_rings(prof, 8, phase=math.pi / 8, lat0=lat), "M_BombIron")
    if band:
        part.loft(lathe_rings([(f0 + 0.12 * scale, 0.205 * scale, z), (f0 + 0.2 * scale, 0.205 * scale, z)], 8,
                              phase=math.pi / 8, lat0=lat), "M_BombBand", cap0=False, cap1=False)
    # box tail: four fins
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        d = Vector((math.cos(a), 0, math.sin(a)))
        base = Vector((lat, 0, z))
        pts = [(-0.38, 0.06), (-0.66, 0.06), (-0.66, 0.26), (-0.5, 0.26)]
        quad = [base + P(f0 + f * scale, 0, 0) + d * r * scale for f, r in pts]
        th = Vector((-d.z, 0, d.x)) * 0.012 * scale
        part.loft([[q + th for q in quad], [q - th for q in quad]], "M_BombIron")


bomb = Part("AerialBomb")
bomb_body(bomb, 1.0, 0.0)
bomb_obj = bomb.finish()
bomb_obj.location.x += 3.0 if not EXPORT else 0
export("AerialBomb.fbx", [bomb_obj])
print("AerialBomb tris", tris([bomb_obj]))

# =====================================================================================  LIVING BOMB
LB_Z = 0.5
LS = 0.95
lbody = Part("LBomb_Body")
bomb_body(lbody, LS, LB_Z)
# eyes: angry slanted red glints on the nose cone
for s in (-1, 1):
    c = P(0.42 * LS, s * 0.11, LB_Z + 0.15)
    ring = [[c + Vector((math.cos(a) * 0.065, -0.02 + 0.01 * i, math.sin(a) * 0.05 - s * math.cos(a) * 0.02))
             for a in (k * 2 * math.pi / 6 for k in range(6))] for i in range(2)]
    lbody.loft(ring, "M_BombEye")
    # iron brow plate
    lbody.loft([[P(0.36 * LS, s * 0.03, LB_Z + 0.22), P(0.36 * LS, s * 0.2, LB_Z + 0.2)],
                [P(0.46 * LS, s * 0.03, LB_Z + 0.17), P(0.46 * LS, s * 0.18, LB_Z + 0.24)]], "M_Iron", closed=False)
# grin: dark mouth band + a zigzag of teeth
mouth_f = 0.5 * LS
lbody.loft([[P(mouth_f - 0.02, math.cos(a) * 0.15, LB_Z - 0.02 + math.sin(a) * 0.09) for a in
             (math.radians(d) for d in range(200, 345, 24))],
            [P(mouth_f + 0.06, math.cos(a) * 0.12, LB_Z - 0.02 + math.sin(a) * 0.07) for a in
             (math.radians(d) for d in range(200, 345, 24))]], "M_CrocMouth", closed=False)
for k in range(7):
    a = math.radians(205 + k * 21)
    base = P(mouth_f + 0.02, math.cos(a) * 0.145, LB_Z + 0.0 + math.sin(a) * 0.05)
    tip = base + Vector((0, 0, -0.05 if k % 2 == 0 else 0.0)) + Vector((0, -0.02, 0))
    if k % 2 == 1:
        tip = P(mouth_f + 0.04, math.cos(a) * 0.14, LB_Z - 0.09)
        base = tip + Vector((0, 0, 0.05))
    lbody.spike(base, tip, Vector((0, 1, 0)), 0.022, "M_CrocTeeth")
# fuse on the back with a frayed end
fz = LB_Z + 0.2 * LS
strut(lbody, P(-0.05, 0, fz - 0.05), P(-0.12, 0.02, fz + 0.16), 0.02)
lb_objs = [lbody.finish()]
# stubby croc legs: origin at the hip, foot forward with claws
for i, (fs, ls) in enumerate(((1, -1), (1, 1), (-1, -1), (-1, 1))):
    hip = P(fs * 0.28, ls * 0.17, LB_Z - 0.12)
    leg = Part(f"LBomb_Leg{i}", hip)
    knee = hip + Vector((ls * 0.08, 0, -0.18))
    foot = P(fs * 0.28 + 0.05, ls * 0.27, 0.04)
    strut(leg, hip, knee, 0.055)
    strut(leg, knee, foot, 0.045)
    leg.loft([[foot + Vector((dx, 0.03, dz)) for dx, dz in ((-0.07, -0.04), (0.07, -0.04), (0.06, 0.03), (-0.06, 0.03))],
              [foot + Vector((dx, -0.12, dz)) for dx, dz in ((-0.08, -0.04), (0.08, -0.04), (0.05, 0.0), (-0.05, 0.0))]],
             "M_CrocHide")
    for c in (-1, 0, 1):
        b = foot + Vector((c * 0.05, -0.12, -0.02))
        leg.spike(b, b + Vector((0, -0.06, -0.02)), Vector((1, 0, 0)), 0.015, "M_CrocTeeth")
    lb_objs.append(leg.finish())
if not EXPORT:
    for o in lb_objs:
        o.location.x += 5.5
export("LivingBomb.fbx", lb_objs)
print("LivingBomb tris", tris(lb_objs))
print("OUT", OUT if EXPORT else "(no export)")
