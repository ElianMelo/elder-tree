"""
Fairy boss, PSX dark fantasy (Docs/ART_DIRECTION.md), from the user's reference painting (2026-11-24): a tall pale fae
in a muted blue dress, long white hair and four white veined wings. She hovers rather than flies.

    blender -b --factory-startup --python Tools/Blender/build_fairy.py -- <abs out_dir>

Live (MCP): exec with TS_EXPORT=False to build into a dedicated "Fairy" scene without exporting.

Fairy.fbx - about 2.6 m from the pointed toes to the crown. Rigid PS1-style segments so Unity animates them
            procedurally; every object's origin sits on its joint:
              Fairy_Pelvis                 hips (origin: hip centre)
              Fairy_Skirt                  the dress below the waist, a ragged hem (origin: waist)
              Fairy_Torso                  bodice, bare shoulders, neck, sash, chest gem (origin: waist)
              Fairy_Head                   face, glowing eyes, pointed ears (origin: neck)
              Fairy_Hair                   crown, long fall down the back, two front locks (origin: neck)
              Fairy_UpperArm_L/R, Fairy_Forearm_L/R, Fairy_Hand_L/R
              Fairy_Thigh_L/R, Fairy_Shin_L/R, Fairy_Foot_L/R (pointed, bare)
              Fairy_Wing_U_L/R, Fairy_Wing_D_L/R   flat veined wing cards (origin: the wing root on the back)
            Forward is Blender -Y (Unity +Z). Origin = ground under the hips (toes at z 0). _R is Blender +X.

Materials (remapped in Unity by name): M_FairyDress, M_FairySkin, M_FairyHair, M_FairyWing, M_FairyEye,
M_FairyHollow, M_FairyTrim, M_FairyGem. UVs are box-projected in world metres; wings use polar UVs
(u = across the wing from its leading edge, v = root -> rim) so one vein texture fits every wing shape.
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
    scene = bpy.data.scenes.get("Fairy") or bpy.data.scenes.new("Fairy")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.window.scene = scene

MAT_RGB = {
    "M_FairyDress": (0.18, 0.27, 0.45), "M_FairySkin": (0.78, 0.74, 0.7), "M_FairyHair": (0.86, 0.85, 0.82),
    "M_FairyWing": (0.9, 0.92, 0.95), "M_FairyEye": (0.6, 0.85, 1.0), "M_FairyHollow": (0.05, 0.04, 0.05),
    "M_FairyTrim": (0.6, 0.62, 0.66), "M_FairyGem": (0.55, 0.8, 1.0),
}
TILE = {"M_FairyDress": 0.7, "M_FairySkin": 0.5, "M_FairyHair": 0.55, "M_FairyTrim": 0.3, "M_FairyHollow": 1.0}
FLAT_UV = ("M_FairyEye", "M_FairyGem")
rng = random.Random(4242)


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

    def branch(self, pts, radii, mat, sides=6, jag=0.0, seed=0, cap0=True, cap1=True, mats=None, flat=1.0):
        """Tube through pts (radius per point). Rings follow a parallel-transported frame; flat < 1 squashes it."""
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
                ring.append(c + (s1 * math.cos(a) * flat + s2 * math.sin(a)) * r * kk)
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

    def disc(self, c, normal, r, mat, sides=6, depth=0.0, squash=1.0):
        """Flat (or shallow cone) disc facing `normal`; squash < 1 flattens it vertically (almond eyes)."""
        n = normal.normalized()
        s1 = n.cross(Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((1, 0, 0))).normalized()
        s2 = n.cross(s1).normalized()
        ring = [self.v(c + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides) * squash) * r)
                for k in range(sides)]
        mid = self.v(c + n * depth)
        for k in range(sides):
            self.face([ring[(k + 1) % sides], ring[k], mid], mat)

    def octa(self, c, r, mat, stretch=1.4):
        p = [self.v(c + d) for d in (Vector((r, 0, 0)), Vector((0, r, 0)), Vector((-r, 0, 0)), Vector((0, -r, 0)))]
        top, bot = self.v(c + Vector((0, 0, r * stretch))), self.v(c - Vector((0, 0, r * stretch)))
        for k in range(4):
            a, b = p[k], p[(k + 1) % 4]
            self.face([a, b, top], mat)
            self.face([b, a, bot], mat)

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


def ellipse_ring(z, w, d, f, sides, a0=0.0, a1=2 * math.pi, closed=True, jag=0.0, seed=0, zjag=0.0):
    """Ring (or open arc a0..a1) around the vertical axis. a = 0 is right, pi/2 is the front."""
    ring = []
    n = sides if closed else sides + 1
    for k in range(n):
        a = (a0 + (a1 - a0) * k / sides) + (math.pi / sides if closed else 0.0)
        kk = 1 + jag * noise.noise(Vector((z * 3.1 + seed, k * 1.3, seed * 0.4)))
        dz = zjag * (1 if k % 2 == 0 else -1) * (0.6 + 0.4 * abs(noise.noise(Vector((k * 0.7, seed, 1.3)))))
        ring.append(V(math.cos(a) * w * kk, f + math.sin(a) * d * kk, z + dz))
    return ring


objects = []
SIDES = (-1, 1)
SIDE_NAME = {-1: "L", 1: "R"}

# =====================================================================================  JOINTS
HIP_Z = 1.4
HIP = {s: V(s * 0.1, 0.0, HIP_Z) for s in SIDES}
KNEE = {s: V(s * 0.11, 0.03, 0.82) for s in SIDES}
ANKLE = {s: V(s * 0.115, 0.0, 0.2) for s in SIDES}
TOE = {s: V(s * 0.115, 0.07, 0.0) for s in SIDES}
WAIST = V(0, 0, 1.66)
SHOULDER = {s: V(s * 0.2, -0.005, 2.14) for s in SIDES}
ELBOW = {s: V(s * 0.26, -0.01, 1.82) for s in SIDES}
WRIST = {s: V(s * 0.29, 0.02, 1.53) for s in SIDES}
NECK = V(0, 0.005, 2.27)
WING_U = {s: V(s * 0.05, -0.1, 2.02) for s in SIDES}
WING_D = {s: V(s * 0.05, -0.095, 1.9) for s in SIDES}

# =====================================================================================  PELVIS (under the skirt)
pelvis = Part("Fairy_Pelvis", V(0, 0, HIP_Z))
pelvis.loft([ellipse_ring(1.3, 0.15, 0.11, 0, 8), ellipse_ring(1.45, 0.17, 0.12, 0, 8), ellipse_ring(1.62, 0.13, 0.1, 0, 8)],
            "M_FairySkin")
objects.append(pelvis.finish())

# =====================================================================================  SKIRT
# a long dress flaring from the waist to a ragged hem just below the knee; open underneath, double sided
SKIRT = [(1.68, 0.125, 0.095, 0.0, 0.0), (1.55, 0.19, 0.15, 0.0, 0.0), (1.38, 0.25, 0.2, 0.0, 0.0),
         (1.05, 0.32, 0.27, 0.01, 0.0), (0.72, 0.41, 0.36, 0.02, 0.07)]
skirt = Part("Fairy_Skirt", WAIST)
skirt.loft([ellipse_ring(z, w, d, f, 14, jag=0.06 if zj else 0.03, seed=7 + i, zjag=zj)
            for i, (z, w, d, f, zj) in enumerate(SKIRT)], "M_FairyDress", cap0=False, cap1=False)
# a few torn tatters hanging past the hem
for k in range(7):
    a = 2 * math.pi * k / 7 + rng.uniform(-0.2, 0.2)
    top = V(math.cos(a) * 0.39, 0.02 + math.sin(a) * 0.34, 0.74)
    out = V(math.cos(a), math.sin(a), 0)
    skirt.spike(top, top + out * 0.05 + Vector((0, 0, -rng.uniform(0.12, 0.22))), V(-math.sin(a), math.cos(a), 0), 0.045,
                "M_FairyDress", sides=3)
objects.append(skirt.finish())

# =====================================================================================  TORSO
TORSO = [(1.64, 0.12, 0.095, 0.0), (1.78, 0.13, 0.095, 0.0), (1.92, 0.16, 0.115, 0.012), (2.03, 0.17, 0.11, 0.0),
         (2.12, 0.2, 0.1, -0.01), (2.19, 0.17, 0.09, -0.01), (2.25, 0.055, 0.055, 0.0), (2.33, 0.045, 0.045, 0.01)]
torso = Part("Fairy_Torso", WAIST)
torso.loft([ellipse_ring(z, w, d, f, 8) for z, w, d, f in TORSO], "M_FairyDress",
           mats=lambda i, j: "M_FairyDress" if i < 3 else "M_FairySkin")
# straps over the bare shoulders
for s in SIDES:
    torso.branch([V(s * 0.1, 0.105, 2.02), V(s * 0.13, 0.04, 2.14), V(s * 0.12, -0.05, 2.15), V(s * 0.1, -0.1, 2.03)],
                 [0.016, 0.018, 0.018, 0.016], "M_FairyDress", sides=4)
# a silver sash at the waist and a pale gem on the breast (the reference's brooch)
torso.loft([ellipse_ring(1.645, 0.128, 0.102, 0, 8), ellipse_ring(1.7, 0.132, 0.1, 0, 8)], "M_FairyTrim", cap0=False, cap1=False)
torso.octa(V(0, 0.128, 1.97), 0.026, "M_FairyGem")
objects.append(torso.finish())

# =====================================================================================  HEAD
HEAD = [(2.28, 0.035, 0.035, 0.03), (2.33, 0.07, 0.075, 0.015), (2.4, 0.085, 0.095, 0.0), (2.47, 0.09, 0.1, -0.005),
        (2.54, 0.08, 0.088, -0.01), (2.6, 0.045, 0.05, -0.01)]
head = Part("Fairy_Head", NECK)
head.loft([ellipse_ring(z, w, d, f, 8) for z, w, d, f in HEAD], "M_FairySkin")
for s in SIDES:
    # almond eyes that glow a cold pale blue, set in a dark lid
    head.disc(V(s * 0.037, 0.093, 2.442), V(s * 0.35, 1, 0.05), 0.024, "M_FairyHollow", sides=6, depth=0.002, squash=0.55)
    head.disc(V(s * 0.037, 0.096, 2.442), V(s * 0.35, 1, 0.05), 0.016, "M_FairyEye", sides=5, depth=0.003, squash=0.6)
    # long pointed fae ears sweeping up and back through the hair
    head.spike(V(s * 0.082, -0.005, 2.425), V(s * 0.175, -0.045, 2.53), V(0, 1, 0), 0.026, "M_FairySkin", sides=3)
head.spike(V(0, 0.096, 2.432), V(0, 0.118, 2.398), V(1, 0, 0), 0.012, "M_FairySkin", sides=3)
m = [head.v(V(-0.018, 0.087, 2.358)), head.v(V(0.018, 0.087, 2.358)), head.v(V(0.012, 0.089, 2.351)), head.v(V(-0.012, 0.089, 2.351))]
head.face([m[0], m[1], m[2], m[3]], "M_FairyHollow")
objects.append(head.finish())

# =====================================================================================  HAIR
hair = Part("Fairy_Hair", NECK)
# crown: a shell over the skull with a fringe over the brow
hair.loft([ellipse_ring(2.462, 0.1, 0.113, -0.004, 10, jag=0.05, seed=31, zjag=0.018),
           ellipse_ring(2.53, 0.101, 0.11, -0.008, 10, seed=32), ellipse_ring(2.595, 0.084, 0.09, -0.012, 10, seed=33),
           ellipse_ring(2.64, 0.048, 0.052, -0.014, 10, seed=34), ellipse_ring(2.655, 0.014, 0.014, -0.014, 10, seed=35)],
          "M_FairyHair", cap0=False)
# the long fall: an open curtain round the back, from the crown to the small of the back, ragged at the tips
FALL = [(2.56, 0.1, 0.108, 0.0, 0.0), (2.44, 0.108, 0.118, -0.01, 0.0), (2.3, 0.115, 0.105, -0.045, 0.0),
        (2.12, 0.155, 0.095, -0.09, 0.0), (1.9, 0.175, 0.075, -0.108, 0.0), (1.7, 0.165, 0.065, -0.115, 0.0),
        (1.56, 0.13, 0.055, -0.115, 0.06)]
a0, a1 = math.pi / 2 + 1.15, math.pi / 2 + 2 * math.pi - 1.15
hair.loft([ellipse_ring(z, w, d, f, 10, a0, a1, closed=False, jag=0.07, seed=40 + i, zjag=zj)
           for i, (z, w, d, f, zj) in enumerate(FALL)], "M_FairyHair", closed=False)
# two locks framing the face, falling in front of the shoulders onto the breast
for s in SIDES:
    hair.branch([V(s * 0.085, 0.06, 2.5), V(s * 0.11, 0.055, 2.34), V(s * 0.15, 0.075, 2.12), V(s * 0.13, 0.085, 1.94),
                 V(s * 0.115, 0.09, 1.84)], [0.03, 0.036, 0.038, 0.03, 0.008], "M_FairyHair", sides=4, jag=0.1,
                seed=50 + s, flat=0.6)
objects.append(hair.finish())

# =====================================================================================  ARMS
for s in SIDES:
    n = SIDE_NAME[s]
    up = Part(f"Fairy_UpperArm_{n}", SHOULDER[s])
    up.branch([SHOULDER[s] + V(-s * 0.02, 0, 0.02), (SHOULDER[s] + ELBOW[s]) * 0.5, ELBOW[s]], [0.046, 0.038, 0.03],
              "M_FairySkin", sides=6)
    objects.append(up.finish())
    fo = Part(f"Fairy_Forearm_{n}", ELBOW[s])
    fo.branch([ELBOW[s], (ELBOW[s] + WRIST[s]) * 0.5, WRIST[s]], [0.031, 0.027, 0.02], "M_FairySkin", sides=6)
    # a silver band round the wrist
    fo.branch([WRIST[s] + V(0, 0, 0.05), WRIST[s] + V(0, 0, 0.02)], [0.025, 0.024], "M_FairyTrim", sides=6)
    objects.append(fo.finish())
    # hand: a slim palm whose flat faces the thigh (inward), long fingers together, the thumb toward the front
    hd = Part(f"Fairy_Hand_{n}", WRIST[s])
    w = WRIST[s]
    rings = []
    for dz, wl, df, ff in ((0.0, 0.013, 0.026, 0.0), (-0.06, 0.014, 0.038, 0.004), (-0.12, 0.01, 0.034, 0.008),
                           (-0.165, 0.004, 0.014, 0.012)):
        rings.append([w + V(math.cos(a) * wl, ff + math.sin(a) * df, dz) for a in (2 * math.pi * k / 6 for k in range(6))])
    hd.loft(rings, "M_FairySkin")
    hd.spike(w + V(-s * 0.004, 0.03, -0.03), w + V(-s * 0.02, 0.075, -0.09), V(1, 0, 0), 0.01, "M_FairySkin", sides=3)
    objects.append(hd.finish())

# =====================================================================================  LEGS (bare, toes pointed)
for s in SIDES:
    n = SIDE_NAME[s]
    th = Part(f"Fairy_Thigh_{n}", HIP[s])
    th.branch([HIP[s] + V(0, 0, 0.06), (HIP[s] + KNEE[s]) * 0.5, KNEE[s]], [0.075, 0.064, 0.05], "M_FairySkin", sides=6)
    objects.append(th.finish())
    sh = Part(f"Fairy_Shin_{n}", KNEE[s])
    sh.branch([KNEE[s], (KNEE[s] + ANKLE[s]) * 0.5 + V(0, -0.012, 0.05), ANKLE[s]], [0.049, 0.043, 0.027], "M_FairySkin", sides=6)
    objects.append(sh.finish())
    ft = Part(f"Fairy_Foot_{n}", ANKLE[s])
    ft.branch([ANKLE[s] + V(0, -0.012, 0.02), ANKLE[s] + V(0, 0.04, -0.09), TOE[s]], [0.03, 0.03, 0.01], "M_FairySkin",
              sides=5, flat=0.75)
    objects.append(ft.finish())

# =====================================================================================  WINGS
WING_ROWS = (0.14, 0.42, 0.72, 1.0)


def wing(name, root, s, phi0, phi1, length, peak, tip_k, sweep, cup=0.08, cols=10):
    """
    A flat veined wing card fanned out from its root. phi = angle in the wing plane (0 = straight out to the side,
    90 = up); r(phi) bulges toward `peak` and spikes into a point there (`tip_k`). The plane is swept back by
    `sweep` degrees and cupped backward a little toward the rim. UV: u = across the fan, v = root -> rim.
    """
    p = Part(name, root)
    sw = math.radians(sweep)
    out = V(s * math.cos(sw), -math.sin(sw), 0)
    up = Vector((0, 0, 1))
    rv = p.v(root)
    grid = []
    for k in range(cols + 1):
        t = k / cols
        phi = math.radians(phi0 + (phi1 - phi0) * t)
        r = length * (0.32 + 0.68 * math.sin(math.pi * t) ** 0.75) * (1 + tip_k * math.exp(-((t - peak) / 0.1) ** 2))
        r *= 1 + 0.04 * noise.noise(Vector((t * 7.0, s * 1.3, length)))
        col = []
        for v in WING_ROWS:
            q = root + out * (math.cos(phi) * r * v) + up * (math.sin(phi) * r * v) + V(0, -cup * v * v * math.sin(math.pi * t), 0)
            col.append(p.v(q))
        grid.append(col)
    for k in range(cols):
        t0, t1 = k / cols, (k + 1) / cols
        p.face([rv, grid[k][0], grid[k + 1][0]], "M_FairyWing", uv=[((t0 + t1) * 0.5, 0.0), (t0, WING_ROWS[0]), (t1, WING_ROWS[0])])
        for j in range(len(WING_ROWS) - 1):
            p.face([grid[k][j], grid[k][j + 1], grid[k + 1][j + 1], grid[k + 1][j]], "M_FairyWing",
                   uv=[(t0, WING_ROWS[j]), (t0, WING_ROWS[j + 1]), (t1, WING_ROWS[j + 1]), (t1, WING_ROWS[j])])
    return p.finish(smooth_angle=180)


for s in SIDES:
    n = SIDE_NAME[s]
    # big upper wings rising out and up to a point; smaller lower wings drooping out and down
    objects.append(wing(f"Fairy_Wing_U_{n}", WING_U[s], s, -8, 82, 1.3, 0.6, 0.3, 24))
    objects.append(wing(f"Fairy_Wing_D_{n}", WING_D[s], s, -80, -6, 0.9, 0.38, 0.45, 32, cup=0.06))


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


export("Fairy.fbx", objects)
print("Fairy tris", tris(objects))
print({o.name: tris([o]) for o in objects})
print("OUT", OUT if EXPORT else "(no export)")
