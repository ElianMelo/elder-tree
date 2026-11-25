"""
Crocodile boss + its cannonballs, PSX dark fantasy (Docs/ART_DIRECTION.md).

    blender -b --factory-startup --python Tools/Blender/build_crocodile.py -- <out_dir>

Live (MCP): exec with TS_EXPORT=False to build into a dedicated "Crocodile" scene without exporting.

Crocodile.fbx   - a rotting Santos-Dumont 14-bis: box-kite canard up front, box-cell wings with dihedral at the
                  back, open wooden truss fuselage, Antoinette V8 driving a two-paddle pusher propeller, bicycle
                  wheels, a wicker pilot basket with a lantern. A crocodile head bursts out through the canard.
                  Separate parts so Unity can animate them procedurally:
                    Bomber_Body   airframe, canvas, wings, canard, engine, basket, gear, scaly neck
                    Bomber_Head   upper skull, brow, eyes, nostrils, upper teeth
                    Bomber_Jaw    lower jaw, origin on the hinge (rotate around local X to open)
                    Bomber_Prop0  pusher propeller, origin on the hub (spins around the nose axis)
                  Nose points along Blender -Y (Unity +Z). Origin = ground under the wing root, wheels on z=0.
AerialBomb.fbx  - cast-iron cannonball with a fuse plug (~150 tris), origin at its centre, fuse trailing (+Y).
LivingBomb.fbx  - the same cannonball grown croc legs, a grin and red eyes. Parts: LBomb_Body,
                  LBomb_Leg0-3 (origin on the hip), origin on the ground.

Materials (remapped in Unity by name): M_BomberMetal, M_BomberCanvas, M_BomberWood, M_BomberGlass, M_Iron,
M_CrocHide, M_CrocMouth, M_CrocTeeth, M_CrocEye, M_BombIron, M_BombBand, M_BombEye.
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
    "M_BomberMetal": (0.30, 0.31, 0.29), "M_BomberGlass": (0.55, 0.32, 0.10), "M_Iron": (0.12, 0.11, 0.11),
    "M_BomberCanvas": (0.40, 0.36, 0.27), "M_BomberWood": (0.15, 0.10, 0.06),
    "M_CrocHide": (0.20, 0.25, 0.14), "M_CrocMouth": (0.25, 0.08, 0.08), "M_CrocTeeth": (0.62, 0.57, 0.46),
    "M_CrocEye": (0.75, 0.70, 0.20), "M_BombIron": (0.17, 0.18, 0.13), "M_BombBand": (0.35, 0.09, 0.07),
    "M_BombEye": (0.9, 0.15, 0.08),
}
# metres per texture tile (box projection)
TILE = {"M_BomberMetal": 2.4, "M_BomberCanvas": 2.0, "M_BomberWood": 1.2, "M_CrocHide": 1.4, "M_CrocMouth": 1.0,
        "M_CrocTeeth": 0.6, "M_Iron": 1.5, "M_BombIron": 0.8, "M_BombBand": 0.8}


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


def tube(part, a, b, r, mat, sides=4, caps=True, phase=math.pi / 4):
    """n-sided tube from a to b (struts, spars, cylinders)."""
    d = (b - a).normalized()
    s1 = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
    s2 = d.cross(s1).normalized()
    ring = [s1 * math.cos(phase + k * 2 * math.pi / sides) * r + s2 * math.sin(phase + k * 2 * math.pi / sides) * r
            for k in range(sides)]
    part.loft([[a + o for o in ring], [b + o for o in ring]], mat, cap0=caps, cap1=caps)


def strut(part, a, b, t, mat="M_Iron", caps=True):
    tube(part, a, b, t * math.sqrt(2), mat, 4, caps)


def slab(part, quad, thick, mat):
    """a thin box from a planar quad (canvas panels must have two sides: URP culls back faces)."""
    q = [Vector(p) for p in quad]
    n = (q[1] - q[0]).cross(q[3] - q[0]).normalized() * thick * 0.5
    top = [part.v(p + n) for p in q]
    bot = [part.v(p - n) for p in q]
    part.face(top, mat)
    part.face(list(reversed(bot)), mat)
    for j in range(4):
        part.face([top[j], bot[j], bot[(j + 1) % 4], top[(j + 1) % 4]], mat)


objects = []

# =====================================================================================  14-BIS AIRFRAME
body = Part("Bomber_Body")
ZF = 1.8                                                      # fuselage centreline height
FUS = [(-4.75, 0.2), (-3.4, 0.3), (-1.8, 0.36), (1.5, 0.3)]   # (f, half size): square box girder


def fs(f):
    return lerp_sections(FUS, f)[0]


def corner(f, sl, sz, inset=1.0):
    s = fs(f) * inset
    return P(f, sl * s, ZF + sz * s)


def sweep(part, pts, radii, mat, sides=6, cap0=True, cap1=True, jag=0.0, seed=0):
    """tube along a polyline with per-point radii (legs, tail)."""
    pts = [Vector(p) for p in pts]
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        ref = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 0, 1))
        s1 = (ref - t * ref.dot(t)).normalized()
        s2 = t.cross(s1)
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            r = radii[i] * (1 + jag * noise.noise(Vector((i * 1.7 + seed, k * 2.3, seed * 0.37))))
            ring.append(p + (s1 * math.cos(a) + s2 * math.sin(a)) * r)
        rings.append(ring)
    part.loft(rings, mat, cap0=cap0, cap1=cap1)


def patch(part, centre, u, v, w, h, seed):
    """an irregular stitched-on hide patch (thin slab) on the plane spanned by u, v."""
    j = [1 + 0.28 * noise.noise(Vector((seed * 1.3, k * 1.7, 0.3))) for k in range(8)]
    slab(part, [centre - u * w * j[0] - v * h * j[1], centre + u * w * j[2] - v * h * j[3],
                centre + u * w * j[4] + v * h * j[5], centre - u * w * j[6] + v * h * j[7]], 0.03, "M_CrocHide")


def membrane(part, quad, sag, thick, mat):
    """a two-sided hide sheet over a quad whose middle billows by `sag` (closed, so URP shows both faces)."""
    q = [Vector(x) for x in quad]
    n = (q[1] - q[0]).cross(q[3] - q[0]).normalized() * thick * 0.5
    c = sum(q, Vector()) / 4 + sag
    top = [part.v(x + n) for x in q]
    bot = [part.v(x - n) for x in q]
    ct, cb = part.v(c + n), part.v(c - n)
    for j in range(4):
        part.face([top[j], top[(j + 1) % 4], ct], mat)
        part.face([bot[(j + 1) % 4], bot[j], cb], mat)
        part.face([top[j], bot[j], bot[(j + 1) % 4], top[(j + 1) % 4]], mat)


CORNERS = ((-1, -1), (1, -1), (1, 1), (-1, 1))
WOOD = 1.6           # framing is chunky and dark (the 1906 ash spars read as twigs at PSX resolution)

# ---- the croc IS the fuselage: its hide is stretched over the box girder, pinned along the four longerons
# and bulging between them (saggy belly). Octagon rings: diagonals sit on the longerons.


def body_ring(f, out=0.0):
    s = fs(f)
    ring = []
    for j in range(8):
        a = j * math.pi / 4
        if j % 2:
            r = s * math.sqrt(2) * 0.97
        else:
            r = s * (1.65 if j == 6 else 1.5) * (1 + 0.07 * noise.noise(Vector((f * 1.3, j * 2.1, 0.5))))
        r += out
        ring.append(P(f, math.cos(a) * r, ZF + math.sin(a) * r))
    return ring


BODY_F = [1.5, 0.9, 0.2, -0.5, -1.2, -1.9, -2.6, -3.3, -4.0, -4.75]
body.loft([body_ring(f) for f in BODY_F], "M_CrocHide")
# longerons ride the creases of the hide
for sl, sz in CORNERS:
    for (f0, _), (f1, _) in zip(FUS, FUS[1:]):
        strut(body, corner(f0, sl, sz), corner(f1, sl, sz), 0.035 * WOOD, "M_BomberWood", caps=False)
# wooden hoops binding the body like a harness
for f in (0.75, -0.6, -3.0, -4.3):
    ring = body_ring(f, 0.04)
    for k in range(8):
        strut(body, ring[k], ring[(k + 1) % 8], 0.035 * WOOD, "M_BomberWood", caps=False)
# dorsal scutes in two rows along the back (clear of the basket and the engine)
for f in (1.3, 0.95, 0.6, 0.25, -0.1, -0.95, -1.3, -2.75, -3.05, -4.5):
    s = fs(f)
    for sl in (-1, 1):
        base = P(f, sl * s * 0.55, ZF + s * 1.38)
        body.spike(base, base + Vector((sl * 0.04, 0.12, 0.17 * (s / 0.36))), Vector((1, 0, 0)), 0.07, "M_CrocHide")
# tail: drops off the end of the girder and curls forward under it, clear of the pusher prop
TAIL = [P(-4.5, 0, 1.78), P(-4.62, 0, 1.45), P(-4.52, 0, 1.15), P(-4.3, 0, 0.95), P(-4.02, 0, 0.86), P(-3.75, 0, 0.88)]
sweep(body, TAIL, [0.25, 0.2, 0.15, 0.11, 0.08, 0.05], "M_CrocHide", sides=6, jag=0.08, seed=9)
body.spike(TAIL[-1], P(-3.5, 0, 0.93), Vector((1, 0, 0)), 0.05, "M_CrocHide")
for k, p in enumerate(TAIL[1:4]):
    body.spike(p + Vector((0, 0.1, 0.06)), p + Vector((0, 0.28 - 0.04 * k, 0.12)), Vector((1, 0, 0)), 0.06, "M_CrocHide")

# ---- box-kite canard around the neck: four canvas panels + thick ash edge frames, braced back to the girder
CF0, CF1, CW, CH, CZ = 0.8, 2.3, 1.45, 1.05, 1.8
for s in (-1, 1):
    slab(body, [P(CF0, -CW, CZ + s * CH), P(CF0, CW, CZ + s * CH), P(CF1, CW, CZ + s * CH), P(CF1, -CW, CZ + s * CH)],
         0.05, "M_BomberCanvas")
    slab(body, [P(CF0, s * CW, CZ - CH), P(CF1, s * CW, CZ - CH), P(CF1, s * CW, CZ + CH), P(CF0, s * CW, CZ + CH)],
         0.05, "M_BomberCanvas")
    patch(body, P(1.5, s * (CW + 0.04), CZ + 0.15), Vector((0, 1, 0)), Vector((0, 0, 1)), 0.42, 0.34, 3 + s)
patch(body, P(1.45, 0.5, CZ + CH + 0.04), Vector((0, 1, 0)), Vector((1, 0, 0)), 0.4, 0.45, 7)
for f in (CF0, CF1):
    rect = [P(f, -CW, CZ - CH), P(f, CW, CZ - CH), P(f, CW, CZ + CH), P(f, -CW, CZ + CH)]
    for k in range(4):
        strut(body, rect[k], rect[(k + 1) % 4], 0.04 * WOOD, "M_BomberWood")
    for sl, sz in CORNERS:
        if f == CF0:
            strut(body, P(f, sl * CW, CZ + sz * CH), corner(0.0, sl, sz), 0.03 * WOOD, "M_BomberWood")
# side-edge rails along the box
for sl, sz in CORNERS:
    strut(body, P(CF0, sl * CW, CZ + sz * CH), P(CF1, sl * CW, CZ + sz * CH), 0.035 * WOOD, "M_BomberWood")

# hide webbing stretched from the neck to the canard's front frame (closes the box around the head).
# A closed shell (front skin + back skin) so it renders from both sides.
NW = 14
web_f, web_b = [], []
for f_off, dst in ((0.0, web_f), (-0.05, web_b)):
    for ring_t, ff in ((0.0, 2.2), (0.5, 2.12), (1.0, 2.26)):
        ring = []
        for k in range(NW):
            a = 2 * math.pi * k / NW
            c, sn = math.cos(a), math.sin(a)
            neck_p = Vector((c * 0.8, sn * 0.84))
            rs = min((CW - 0.05) / max(abs(c), 1e-4), (CH - 0.05) / max(abs(sn), 1e-4))
            rect_p = Vector((c * rs, sn * rs))
            q = neck_p.lerp(rect_p, ring_t)
            if ring_t == 0.5:
                q *= 1 + 0.06 * noise.noise(Vector((k * 1.9, 0.4, 2.0)))
            ring.append(P(ff + f_off, q.x, 1.76 + q.y))
        dst.append(ring)
body.loft(web_f + list(reversed(web_b)) + [web_f[0]], "M_CrocHide", cap0=False, cap1=False)

# ---- wings: two box-cell wings with strong dihedral, three cells each, set at the back.
# The planes run into the girder; the cell walls start just outboard of the engine.
LE, TE, R0, R1, ZL, GAP = -2.5, -4.6, 0.5, 5.6, 1.66, 1.3
RIN = 0.24
DIH = math.tan(math.radians(10))


def zl(lat):
    return ZL + (max(abs(lat), R0) - R0) * DIH


for s in (-1, 1):
    span = (P(0, s * R1, zl(R1)) - P(0, s * R0, zl(R0))).normalized()
    nrm = span.cross(Vector((0, 1, 0))).normalized()
    if nrm.z < 0:
        nrm = -nrm
    for dz in (0.0, GAP):
        slab(body, [P(LE, s * RIN, zl(R0) + dz), P(LE, s * R1, zl(R1) + dz), P(TE, s * R1, zl(R1) + dz),
                    P(TE, s * RIN, zl(R0) + dz)], 0.06, "M_BomberCanvas")
        # ash spars along both edges
        strut(body, P(LE + 0.04, s * RIN, zl(R0) + dz), P(LE + 0.04, s * (R1 + 0.04), zl(R1) + dz), 0.04 * WOOD, "M_BomberWood")
        strut(body, P(TE - 0.03, s * RIN, zl(R0) + dz), P(TE - 0.03, s * (R1 + 0.04), zl(R1) + dz), 0.03 * WOOD, "M_BomberWood")
    for k in range(4):
        lat = R0 + (R1 - R0) * k / 3
        # inner cell walls are leathery croc-hide membranes, the tip walls stay canvas
        slab(body, [P(LE, s * lat, zl(lat)), P(TE, s * lat, zl(lat)), P(TE, s * lat, zl(lat) + GAP),
                    P(LE, s * lat, zl(lat) + GAP)], 0.05, "M_CrocHide" if k < 3 else "M_BomberCanvas")
        for fp in (LE + 0.04, TE - 0.03):
            strut(body, P(fp, s * lat, zl(lat)), P(fp, s * lat, zl(lat) + GAP), 0.035 * WOOD, "M_BomberWood")
    # the cells' trailing openings are closed by billowing croc-hide sheets (the outer one torn half way)
    for k in range(3):
        l0, l1 = R0 + (R1 - R0) * k / 3 + 0.04, R0 + (R1 - R0) * (k + 1) / 3 - 0.04
        top = GAP - 0.04 if k < 2 else GAP * 0.55
        membrane(body, [P(TE + 0.05, s * l0, zl(l0) + 0.04), P(TE + 0.05, s * l1, zl(l1) + 0.04),
                        P(TE + 0.05, s * l1, zl(l1) + top), P(TE + 0.05, s * l0, zl(l0) + top)],
                 Vector((0, -0.3, 0)), 0.04, "M_CrocHide")
    # hide patches stitched over the torn canvas: on top of the upper plane, under the lower one
    for i, (l, fc, w, h) in enumerate(((1.4, -3.2, 0.5, 0.42), (3.1, -3.95, 0.42, 0.35), (4.6, -3.05, 0.36, 0.48))):
        patch(body, P(fc, s * l, zl(l) + GAP) + nrm * 0.045, span, Vector((0, 1, 0)), w, h, 11 + i + 5 * s)
    for i, (l, fc, w, h) in enumerate(((2.3, -3.5, 0.55, 0.42), (4.2, -3.1, 0.4, 0.32))):
        patch(body, P(fc, s * l, zl(l)) - nrm * 0.045, span, Vector((0, 1, 0)), w, h, 21 + i + 5 * s)
    # scutes riding the upper leading edge
    for l in (1.2, 2.2, 3.2, 4.2):
        base = P(LE + 0.04, s * l, zl(l) + GAP + 0.05)
        body.spike(base, base + Vector((0, 0.16, 0.2)), Vector((1, 0, 0)), 0.07, "M_CrocHide")

# ---- Antoinette V8 saddled on the croc's back between the wing roots
E0, E1, EZ = -4.35, -3.25, 0.12
body.loft([[P(f, -0.2, 2.0 + EZ), P(f, 0.2, 2.0 + EZ), P(f, 0.2, 2.3 + EZ), P(f, -0.2, 2.3 + EZ)] for f in (E0, E1)],
          "M_BomberMetal")
for s in (-1, 1):
    up = Vector((s * 0.5, 0, 0.866))
    for k in range(4):
        base = P(-4.2 + k * 0.29, s * 0.08, 2.24 + EZ)
        tube(body, base, base + up * 0.32, 0.08, "M_Iron", sides=6, phase=0)
        tube(body, base + up * 0.32, base + up * 0.38, 0.06, "M_BomberMetal", sides=6, phase=0)
    # exhaust manifold along each bank, a stub pipe out the back
    m0, m1 = P(-4.25, s * 0.3, 2.6 + EZ), P(-3.3, s * 0.3, 2.6 + EZ)
    strut(body, m0, m1, 0.035, "M_Iron")
    strut(body, m0, P(-4.55, s * 0.36, 2.48 + EZ), 0.03, "M_Iron")
strut(body, P(E0, 0, 2.17 + EZ), P(-4.88, 0, 2.17 + EZ), 0.03, "M_Iron")  # prop shaft

# ---- wicker pilot basket hung under the girder just ahead of the wings, a lantern on its rim
B0, B1, BW, BZ0, BZ1 = -2.4, -1.75, 0.5, 1.2, 2.3
slab(body, [P(B0, -BW, BZ0), P(B0, BW, BZ0), P(B1, BW, BZ0), P(B1, -BW, BZ0)], 0.06, "M_BomberWood")
for f in (B0, B1):
    slab(body, [P(f, -BW, BZ0), P(f, BW, BZ0), P(f, BW, BZ1), P(f, -BW, BZ1)], 0.06, "M_BomberWood")
for s in (-1, 1):
    slab(body, [P(B0, s * BW, BZ0), P(B1, s * BW, BZ0), P(B1, s * BW, BZ1), P(B0, s * BW, BZ1)], 0.06, "M_BomberWood")
lc = P(B1 + 0.02, 0.32, BZ1)
tube(body, lc, lc + Vector((0, 0, 0.22)), 0.08, "M_BomberGlass", sides=6, phase=0)
tube(body, lc + Vector((0, 0, 0.22)), lc + Vector((0, 0, 0.29)), 0.1, "M_Iron", sides=6, phase=0)

# ---- bicycle-wheel landing gear: two mains under the wing roots + a small nose wheel


def bike_wheel(part, f, lat, zc, r, seg=10):
    rings = []
    for k in range(seg):
        a = k * 2 * math.pi / seg
        c = P(f + math.cos(a) * r, lat, zc + math.sin(a) * r)
        d = Vector((0, -math.cos(a), math.sin(a)))
        x = Vector((1, 0, 0))
        w = 0.05
        rings.append([c + d * w + x * w, c + d * w - x * w, c - d * w - x * w, c - d * w + x * w])
    part.loft(rings + [rings[0]], "M_Iron", cap0=False, cap1=False)
    tube(part, P(f, lat - 0.07, zc), P(f, lat + 0.07, zc), 0.07, "M_Iron", sides=6, phase=0)
    for k in range(3):
        a = k * math.pi / 3 + 0.3
        d = Vector((0, -math.cos(a), math.sin(a))) * r
        strut(part, P(f, lat, zc) + d, P(f, lat, zc) - d, 0.016, "M_Iron", caps=False)


GF, GL, GR = -2.85, 0.78, 0.44
for s in (-1, 1):
    bike_wheel(body, GF, s * GL, GR, GR)
    axle = P(GF, s * (GL - 0.08), GR)
    strut(body, P(GF + 0.45, s * 0.3, 1.48), axle, 0.05)
    strut(body, P(GF - 0.45, s * 0.28, 1.5), axle, 0.05)
strut(body, P(GF, -GL + 0.08, GR), P(GF, GL - 0.08, GR), 0.035)
bike_wheel(body, 0.6, 0, 0.26, 0.26, seg=8)
for s in (-1, 1):
    strut(body, P(0.4, s * 0.22, 1.5), P(0.6, s * 0.09, 0.26), 0.035)

# ---- croc limbs clutching the airframe: forelegs hook the canard's rear frame, hind legs grip the gear struts


def claws(part, at, tips):
    for t in tips:
        part.spike(at, t, Vector((0, 1, 0)), 0.035, "M_CrocTeeth")


for s in (-1, 1):
    fore = [P(0.42, s * 0.36, 1.62), P(0.3, s * 0.95, 1.28), P(0.58, s * 1.3, 0.98), P(0.74, s * 1.43, 0.86)]
    sweep(body, fore, [0.17, 0.12, 0.09, 0.075], "M_CrocHide", jag=0.1, seed=30 + s)
    hand = fore[-1]
    claws(body, hand, [P(0.84 + 0.05 * c, s * (1.52 + 0.02 * c), 0.66 + 0.03 * c) for c in (-1, 0, 1)])
    hind = [P(-2.7, s * 0.4, 1.58), P(-2.5, s * 0.9, 1.3), P(-2.6, s * 0.74, 0.98), P(-2.66, s * 0.6, 0.9)]
    sweep(body, hind, [0.19, 0.14, 0.1, 0.08], "M_CrocHide", jag=0.1, seed=40 + s)
    claws(body, hind[-1], [P(-2.58, s * 0.47, 0.76), P(-2.74, s * 0.5, 0.77), P(-2.72, s * 0.64, 0.72)])

# ---- scaly neck: the croc's head bursts forward through the canard
neck = [(1.4, 0.46, 1.8), (1.8, 0.6, 1.78), (2.1, 0.78, 1.76), (2.45, 0.9, 1.74)]
body.loft(lathe_rings(neck, 10, wz=1.04, jag=0.08, seed=4), "M_CrocHide", cap0=False, cap1=False)
objects.append(body.finish())

# ---- the croc head is oversized (the reference reads croc first, plane second): scale about the neck
HS = 1.12


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
    # the eye, open: a glowing eyeball out in front of the turret, looking forward and out, hooded by a heavy hide lid
    # over its top third, with a dark vertical slit pupil (it used to sit buried in the turret and read as shut)
    gaze = (P(0.85, s * 0.42, 0.2) - P(0, 0, 0)).normalized()
    eb = P(3.02, s * 0.56, 2.47)
    er = 0.165
    side_t = gaze.cross(Vector((0, 0, 1))).normalized()       # horizontal across the eye
    up_t = side_t.cross(gaze).normalized()                     # vertical across the eye
    eye_ball = []
    for k in range(7):
        a = -math.pi / 2 + math.pi * k / 6
        eye_ball.append([eb + gaze * (math.sin(a) * er * 0.9) + (side_t * math.cos(b) + up_t * math.sin(b)) * (math.cos(a) * er)
                         for b in (q * 2 * math.pi / 10 for q in range(10))])
    head.loft(eye_ball, "M_CrocEye")
    # slit pupil: a thin near-black lens on the front of the eyeball, tall and narrow
    pc = eb + gaze * (er * 0.86)
    pupil = []
    for k in range(5):
        a = -math.pi / 2 + math.pi * k / 4
        pupil.append([pc + gaze * (math.sin(a) * 0.03) + (side_t * math.cos(b) * 0.032 + up_t * math.sin(b) * 0.13) * math.cos(a)
                      for b in (q * 2 * math.pi / 8 for q in range(8))])
    head.loft(pupil, "M_BombIron")
    # the upper lid: a hide shell over the top of the eye, tipped forward so its edge cuts across the eye (a hooded glare)
    lid_axis = (up_t + gaze * 0.45 - side_t * s * 0.15).normalized()
    lid_u = gaze.cross(lid_axis).normalized()
    lid_v = lid_axis.cross(lid_u).normalized()
    lid = []
    for k in range(5):
        t = math.radians(4 + 58 * k / 4)
        lid.append([eb + (lid_axis * math.cos(t) + (lid_u * math.cos(b) + lid_v * math.sin(b)) * math.sin(t)) * (er + 0.03)
                    for b in (q * 2 * math.pi / 10 for q in range(10))])
    head.loft(lid, "M_CrocHide", cap1=False)
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

# ---- pusher propeller behind the engine: two steel arms carrying flat paddle blades, origin on the hub
hub = P(-4.92, 0, 2.29)
prop = Part("Bomber_Prop0", hub)
prop.loft(lathe_rings([(-4.84, 0.1, hub.z), (-4.94, 0.11, hub.z), (-5.1, 0.04, hub.z)], 6), "M_Iron")
for b in range(2):
    a = b * math.pi + 0.4
    radial = Vector((math.cos(a), 0, math.sin(a)))
    tang = Vector((-math.sin(a), 0, math.cos(a)))
    strut(prop, hub + radial * 0.08, hub + radial * 0.62, 0.028)
    pitch = math.radians(20)
    rings = []
    for r, w in ((0.45, 0.1), (0.6, 0.21), (1.05, 0.23), (1.2, 0.13)):
        c = hub + radial * r + Vector((0, 0.02, 0))
        chord = (tang * math.cos(pitch) + Vector((0, 1, 0)) * math.sin(pitch)) * w
        thick = (Vector((0, 1, 0)) * math.cos(pitch) - tang * math.sin(pitch)) * 0.025
        rings.append([c - chord, c + thick, c + chord, c - thick])
    prop.loft(rings, "M_BomberMetal")
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

# =====================================================================================  CANNONBALLS


def ball(part, r, zc, sides=10, jag=0.035, seed=7):
    """cast-iron sphere around the forward axis (poles front/back), slightly lumpy like a sand casting."""
    prof = []
    for t in (14, 40, 65, 90, 115, 140, 166):
        a = math.radians(t)
        prof.append((r * math.cos(a), r * math.sin(a), zc))
    part.loft(lathe_rings(prof, sides, jag=jag, seed=seed, phase=math.pi / sides), "M_BombIron")


def on_ball(r, zc, elev, az, out=0.0):
    """point on the ball surface: elev up from the equator, az right of the nose (degrees)."""
    e, a = math.radians(elev), math.radians(az)
    return P((r + out) * math.cos(e) * math.cos(a), (r + out) * math.cos(e) * math.sin(a), zc + (r + out) * math.sin(e))


def fuse(part, base, axis, r):
    """iron fuse plug + a frayed cord, base on the ball surface, axis pointing out."""
    tube(part, base - axis * 0.02, base + axis * 0.05 * r / 0.3, 0.065 * r / 0.3, "M_BombBand", sides=6, phase=0)
    tip = base + axis * 0.05 * r / 0.3
    strut(part, tip, tip + (axis * 0.09 + Vector((0.02, 0, 0.03))) * r / 0.3, 0.012, "M_Iron")


BALL_R = 0.3
cball = Part("AerialBomb")
ball(cball, BALL_R, 0.0)
# casting seam around the middle
cball.loft(lathe_rings([(0.0, BALL_R * 1.02, 0.0), (0.03, BALL_R * 1.02, 0.0)], 10, phase=math.pi / 10), "M_Iron",
           cap0=False, cap1=False)
fuse(cball, P(-BALL_R * 0.97, 0, 0), Vector((0, 1, 0)), BALL_R)
bomb_obj = cball.finish(smooth_angle=50)
bomb_obj.location.x += 3.0 if not EXPORT else 0
export("AerialBomb.fbx", [bomb_obj])
print("AerialBomb tris", tris([bomb_obj]))

# =====================================================================================  LIVING CANNONBALL
LB_Z = 0.52
LR = 0.3
lbody = Part("LBomb_Body")
ball(lbody, LR, LB_Z, sides=12, seed=11)
# eyes: angry slanted red glints on the front, an iron brow plate over each
for s in (-1, 1):
    c = on_ball(LR, LB_Z, 14, s * 24, -0.01)
    n = (c - P(0, 0, LB_Z)).normalized()
    side = Vector((1, 0, 0))
    up = n.cross(side).normalized()
    ring = [[c + n * dn + side * math.cos(a) * 0.065 + up * (math.sin(a) * 0.045 + s * math.cos(a) * 0.018)
             for a in (k * 2 * math.pi / 6 for k in range(6))] for dn in (0.0, 0.025)]
    lbody.loft(ring, "M_BombEye")
    b0, b1 = on_ball(LR, LB_Z, 24, s * 6, 0.012), on_ball(LR, LB_Z, 34, s * 42, 0.012)
    slab(lbody, [b0, b1, b1 + Vector((0, 0, 0.045)), b0 + Vector((0, 0, 0.045))], 0.025, "M_Iron")
# grin: dark mouth band across the lower front + a zigzag of teeth
AZ = list(range(-55, 56, 22))
lbody.loft([[on_ball(LR, LB_Z, el, az, 0.008) for az in AZ] for el in (-8, -26)], "M_CrocMouth", closed=False)
for k, az in enumerate(range(-50, 51, 20)):
    top = on_ball(LR, LB_Z, -8, az, 0.012)
    lbody.spike(top, on_ball(LR, LB_Z, -16, az, 0.03), Vector((1, 0, 0)), 0.022, "M_CrocTeeth")
    bot = on_ball(LR, LB_Z, -26, az + 10, 0.012)
    lbody.spike(bot, on_ball(LR, LB_Z, -18, az + 10, 0.03), Vector((1, 0, 0)), 0.02, "M_CrocTeeth")
# fuse on top, leaning back
fuse(lbody, on_ball(LR, LB_Z, 75, 180, -0.01), (on_ball(LR, LB_Z, 75, 180) - P(0, 0, LB_Z)).normalized(), LR)
lb_objs = [lbody.finish(smooth_angle=50)]
# stubby croc legs: origin at the hip, foot forward with claws
for i, (fs_, ls) in enumerate(((1, -1), (1, 1), (-1, -1), (-1, 1))):
    hip = P(fs_ * 0.15, ls * 0.15, LB_Z - 0.16)
    leg = Part(f"LBomb_Leg{i}", hip)
    knee = hip + Vector((ls * 0.1, 0, -0.17))
    foot = P(fs_ * 0.2 + 0.05, ls * 0.29, 0.04)
    strut(leg, hip, knee, 0.055, "M_CrocHide")
    strut(leg, knee, foot, 0.045, "M_CrocHide")
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
