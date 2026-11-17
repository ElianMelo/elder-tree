"""
Elder Tree - low-poly PSX dark-fantasy player character (replaces the Tung Sahur log).

Art direction: Docs/ART_DIRECTION.md. ~2k triangles, 3-12 sided tubes, silhouette first
(crown of antler branches, heavy brow, long drooping nose, moss beard, root feet,
twig fingers). All surface detail lives in one small point-filtered atlas:

    albedo = procedural bark/moss/lichen (painted per texel, in metres, grain along
             each part) x baked AO x soft top light, quantised to 15-bit with a 4x4
             Bayer dither.

Alpha-cut cards (moss beard, leaf clumps, hanging roots) use a second small atlas.
The rig is the exact 22-bone Unity Humanoid skeleton of the old character, so every
retargeted clip, the club grip and the quiver/bow sockets keep working.

Run headless:
    blender -b --factory-startup --python build_elder_tree.py -- <out_dir> [--bake <tex_dir>] [--size 256]
Run live (Blender MCP):
    g = {"__name__": "__main__", "__file__": path, "TS_EXPORT": False, "TS_BAKE_DIR": tex_dir}
    exec(compile(open(path).read(), path, "exec"), g)

Conventions: Z up, character faces -Y, character's LEFT side is +X.
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector

_argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(globals().get("__file__", bpy.data.filepath or ".")))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = globals().get("TS_OUT_DIR") or (_argv[0] if _argv and not _argv[0].startswith("--") else os.path.join(HERE, "export"))
DO_EXPORT = globals().get("TS_EXPORT", True)
BAKE_DIR = globals().get("TS_BAKE_DIR")
if "--bake" in _argv:
    BAKE_DIR = _argv[_argv.index("--bake") + 1]
OUT_DIR = os.path.abspath(OUT_DIR)
BAKE_DIR = os.path.abspath(BAKE_DIR) if BAKE_DIR else None
TEX = int(globals().get("TS_TEX_SIZE", 256))
if "--size" in _argv:
    TEX = int(_argv[_argv.index("--size") + 1])
CARD_TEX = 256
SCENE_NAME = "ElderTree"

# Skeleton retargeted onto the Human Mega Animations Pack rig (Kevin Iglesias HumanM_Model):
# the pack is the source of truth for all motion, so joint heights, segment ratios, the
# Hips-Spine-Chest-Neck-Head chain (no UpperChest) and the 5-finger hands follow HumanM
# scaled by 0.95. Only the shoulders sit wider (0.30 vs 0.19) so the arms clear the trunk.
# Character-space sockets in Unity (club grip, bow, quiver) depend on these numbers.
HIPS_Z, SPINE_Z, CHEST_Z, NECK_Z, HEAD_Z, HEAD_END_Z = 0.93, 1.01, 1.20, 1.455, 1.525, 1.95
SHOULDER_IN, SHOULDER_X, SHOULDER_Z = 0.07, 0.30, 1.38
ELBOW_X, WRIST_X = 0.545, 0.755
KNUCKLE_X = WRIST_X + 0.075                     # hand bone tail = finger roots (HumanM: 0.14 * 0.95 incl. palm)
HAND_END_X = KNUCKLE_X
HIP_X, HIP_Z, KNEE_Z, ANKLE_Z = 0.12, 0.88, 0.49, 0.12
TOE_Y, TOE_Z, TOE_END_Y = -0.14, 0.03, -0.24
# fingers (index..little): sideways offset (-Y = forward/thumb side) and the three phalanx lengths
FINGERS = [("Index", -0.040, (0.046, 0.036, 0.030)), ("Middle", -0.013, (0.050, 0.039, 0.032)),
           ("Ring", 0.014, (0.047, 0.037, 0.030)), ("Little", 0.040, (0.040, 0.033, 0.027))]
# thumb: root offset from the wrist and per-phalanx deltas (HumanM thumb: forward, out, down)
THUMB_ROOT = (0.035, -0.036, -0.014)
THUMB_SEGS = [(0.040, -0.028, -0.018), (0.032, -0.019, -0.014), (0.027, -0.014, -0.011)]
PHALANX = ("Proximal", "Intermediate", "Distal")
BONES = {
    "Hips":          ((0, 0.0, HIPS_Z), (0, 0.0, SPINE_Z), None),
    "Spine":         ((0, 0.0, SPINE_Z), (0, 0.0, CHEST_Z), "Hips"),
    "Chest":         ((0, 0.0, CHEST_Z), (0, 0.0, NECK_Z), "Spine"),
    "Neck":          ((0, 0.0, NECK_Z), (0, 0.0, HEAD_Z), "Chest"),
    "Head":          ((0, 0.0, HEAD_Z), (0, 0.0, HEAD_END_Z), "Neck"),
}


def finger_chains(s):
    """{finger: [(bone, head, tail), x3]} in Blender space for side s (+1 left, -1 right)."""
    L = "Left" if s > 0 else "Right"
    out = {}
    for name, y, lens in FINGERS:
        x, z, chain = KNUCKLE_X, SHOULDER_Z, []
        for k, ln in enumerate(lens):
            h = (x * s, y, z)
            x += ln
            z -= 0.002 * (k + 1)          # HumanM fingers droop slightly at rest
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

MAT_BARK, MAT_MOSS, MAT_EYE = 0, 1, 2
MAT_NAMES = ["M_ElderBark", "M_ElderMoss", "M_ElderEye"]

# Face layout on the trunk (12-sided lathe, vertex 6 = front centre, 30 deg steps)
EYE_Z = 1.74
EYE_GLOW = (0.78, 0.47, 0.12)          # dim ember amber (Palette.Ember family, darker)


# ----------------------------------------------------------------------------
# Scene reset (live: dedicated scene, never touches the user's scenes)
# ----------------------------------------------------------------------------
if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
else:
    scene = bpy.data.scenes.get(SCENE_NAME) or bpy.data.scenes.new(SCENE_NAME)
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.cameras, bpy.data.lights, bpy.data.images):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)
    try:
        bpy.context.window.scene = scene
    except Exception:
        pass
scene.unit_settings.system = 'METRIC'


# ----------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------
def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def hash01(*k):
    h = 0.0
    for i, v in enumerate(k):
        h += v * (127.1 + 91.7 * i)
    s = math.sin(h * 0.731 + 11.3) * 43758.5453
    return s - math.floor(s)


def mix(a, b, t):
    out = {}
    for k, v in a.items():
        out[k] = out.get(k, 0) + v * (1 - t)
    for k, v in b.items():
        out[k] = out.get(k, 0) + v * t
    return out


class Builder:
    def __init__(self):
        self.verts, self.weights = [], []
        self.faces = []        # [idx, mat, uvs, part]
        self.parts = []        # {"C": around (m), "L": along (m), "tint": (r,g,b), "moss": bias}

    def v(self, co, w):
        self.verts.append(Vector(co))
        self.weights.append(w)
        return len(self.verts) - 1

    def f(self, idx, mat, uvs, part=None):
        self.faces.append([idx, mat, uvs, part])

    def part(self, C, L, tint=(1, 1, 1), moss=0.0):
        self.parts.append({"C": C, "L": L, "tint": tint, "moss": moss})
        return len(self.parts) - 1


B = Builder()      # bark: baked into the atlas
X = Builder()      # extras: cards + eyes (not baked)


def ring_frame(tangent, ref):
    t = tangent.normalized()
    n = ref - t * ref.dot(t)
    if n.length < 1e-5:
        n = Vector((0, 0, 1)) - t * t.z
        if n.length < 1e-5:
            n = Vector((1, 0, 0))
    n.normalize()
    return t, n, t.cross(n).normalized()


def tube(points, radii, sides, wfn, ref=Vector((0, 0, 1)), jag=0.12, seed=0, cap_start=True, cap_end=True,
         tint=(1, 1, 1), moss=0.0, twist=0.0):
    """Low-poly gnarled tube. radii: float or (rn, rb). Pointed caps (one pole).
    Becomes one atlas part: u = around, v = along (in metres for uniform texel density)."""
    pts = [Vector(p) for p in points]
    rad = [(r, r) if isinstance(r, (int, float)) else r for r in radii]
    n = len(pts)

    def tan(i):
        if i == 0:
            return pts[1] - pts[0]
        if i == n - 1:
            return pts[-1] - pts[-2]
        return pts[i + 1] - pts[i - 1]

    acc = [0.0]
    for i in range(1, n):
        acc.append(acc[-1] + (pts[i] - pts[i - 1]).length)
    cs = min(rad[0]) * 0.8 if cap_start else 0.0
    ce = min(rad[-1]) * 0.8 if cap_end else 0.0
    total = acc[-1] + cs + ce
    circ = sum(math.pi * (a + b) for a, b in rad) / n
    part = B.part(circ, total, tint, moss)

    rings, prev = [], None
    for i, c in enumerate(pts):
        t, nn, bb = ring_frame(tan(i), ref if prev is None else prev)
        prev = nn
        rx, ry = rad[i]
        ring = []
        for j in range(sides + 1):
            jj = j % sides
            a = 2 * math.pi * jj / sides + twist * i
            k = 1.0 + jag * (hash01(seed, i, jj) - 0.5) * 2.0
            co = c + nn * math.cos(a) * rx * k + bb * math.sin(a) * ry * k
            ring.append(B.v(co, wfn(co)))
        rings.append(ring)
    for i in range(n - 1):
        v0, v1 = (acc[i] + cs) / total, (acc[i + 1] + cs) / total
        for j in range(sides):
            u0, u1 = j / sides, (j + 1) / sides
            B.f([rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]], MAT_BARK,
                [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], part)
    if cap_start:
        c = pts[0] - tan(0).normalized() * cs
        p = B.v(c, wfn(c))
        for j in range(sides):
            B.f([p, rings[0][j + 1], rings[0][j]], MAT_BARK,
                [((j + 0.5) / sides, 0.0), ((j + 1) / sides, cs / total), (j / sides, cs / total)], part)
    if cap_end:
        c = pts[-1] + tan(n - 1).normalized() * ce
        p = B.v(c, wfn(c))
        vv = (acc[-1] + cs) / total
        for j in range(sides):
            B.f([rings[-1][j], rings[-1][j + 1], p], MAT_BARK,
                [(j / sides, vv), ((j + 1) / sides, vv), ((j + 0.5) / sides, 1.0)], part)


# ----------------------------------------------------------------------------
# Weights
# ----------------------------------------------------------------------------
TORSO_BONES = [("Hips", 0.95), ("Spine", 1.06), ("Chest", 1.24), ("Neck", 1.50), ("Head", 1.60)]


def torso_weights(co):
    z = co.z
    if z <= TORSO_BONES[0][1]:
        return {"Hips": 1.0}
    if z >= TORSO_BONES[-1][1]:
        return {"Head": 1.0}
    for (b0, z0), (b1, z1) in zip(TORSO_BONES, TORSO_BONES[1:]):
        if z0 <= z <= z1:
            t = smoothstep(z0, z1, z)
            return {b0: 1 - t, b1: t}
    return {"Hips": 1.0}


def chain_weights(chain, blend=0.05):
    segs = [(b, Vector(a), Vector(c)) for b, a, c in chain]

    def fn(co):
        best = None
        for idx, (b, a, c) in enumerate(segs):
            d = c - a
            L = d.length
            t = (co - a).dot(d) / (L * L)
            dist = (a + d * max(0.0, min(1.0, t)) - co).length
            if best is None or dist < best[0] - 1e-9:
                best = (dist, idx, t, L)
        _, idx, t, L = best
        w = {segs[idx][0]: 1.0}
        along = t * L
        if idx > 0 and along < blend:
            k = max(0.0, min(0.5, 0.5 * (1 - along / blend)))
            w = {segs[idx][0]: 1 - k, segs[idx - 1][0]: k}
        elif idx < len(segs) - 1 and (L - along) < blend:
            k = max(0.0, min(0.5, 0.5 * (1 - (L - along) / blend)))
            w = {segs[idx][0]: 1 - k, segs[idx + 1][0]: k}
        return w
    return fn


const = lambda bone: (lambda co: {bone: 1.0})

# ----------------------------------------------------------------------------
# 1. TRUNK (torso + carved face): 12-sided lathe, fluted bark, hunched forward
# ----------------------------------------------------------------------------
SIDES = 12
#        z     r      lean(y)
RINGS = [(0.70, 0.190, 0.000),
         (0.80, 0.215, 0.000),
         (0.93, 0.228, 0.000),
         (1.06, 0.250, -0.005),
         (1.20, 0.300, -0.015),
         (1.32, 0.335, -0.030),
         (1.44, 0.318, -0.050),
         (1.53, 0.255, -0.070),
         (1.63, 0.245, -0.082),
         (EYE_Z, 0.240, -0.088),
         (1.83, 0.250, -0.088),
         (1.92, 0.225, -0.082),
         (2.01, 0.170, -0.070)]
# radial push per ring for the face: {ring index: {k (0 = front, +-1 = 30 deg, +-2 = 60 deg): metres}}
FACE = {
    7:  {0: -0.035, 1: -0.012, 2: 0.0},     # mouth recess (under the beard)
    8:  {0: 0.060, 1: 0.022, 2: 0.006},     # nose base + cheekbones
    9:  {0: 0.036, 1: -0.068, 2: -0.004},   # bridge + deep eye sockets
    10: {0: 0.030, 1: 0.072, 2: 0.034},     # heavy brow ridge
    11: {0: 0.018, 1: 0.026, 2: 0.010},     # forehead knots
}


def trunk_point(i, j):
    z, r, lean = RINGS[i]
    jj = j % SIDES
    a = -math.pi + 2 * math.pi * jj / SIDES       # a = 0 at the front (j = 6)
    k = jj - SIDES // 2
    face = i in FACE and abs(k) <= 2
    rr = r
    if face:
        rr += FACE[i].get(abs(k), 0.0)
    else:
        rr *= 1.0 + 0.11 * (1 if jj % 2 == 0 else -1)            # fluted bark ridges
        rr *= 1.0 + 0.09 * (hash01(3, i, jj) - 0.5) * 2          # gnarl
    if not (7 <= i <= 11 and abs(k) <= 2):
        a += 0.10 * math.sin(i * 1.3)                              # slight twist of the grain
    return Vector((math.sin(a) * rr, -math.cos(a) * rr + lean, z + 0.012 * (hash01(5, i, jj) - 0.5)))


def build_trunk():
    zb, zt = RINGS[0][0] - 0.06, RINGS[-1][0] + 0.07
    L = (zt - zb) + 0.2
    part = B.part(2 * math.pi * 0.26, L, tint=(1, 1, 1), moss=0.0)
    rings = []
    for i in range(len(RINGS)):
        ring = []
        for j in range(SIDES + 1):
            co = trunk_point(i, j)
            ring.append(B.v(co, torso_weights(co)))
        rings.append(ring)
    vz = lambda z: (z - zb + 0.1) / L
    for i in range(len(RINGS) - 1):
        for j in range(SIDES):
            u0, u1 = j / SIDES, (j + 1) / SIDES
            B.f([rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]], MAT_BARK,
                [(u0, vz(RINGS[i][0])), (u1, vz(RINGS[i][0])), (u1, vz(RINGS[i + 1][0])), (u0, vz(RINGS[i + 1][0]))],
                part)
    bot = B.v((0, 0, zb), {"Hips": 1.0})
    top = B.v((0, RINGS[-1][2] - 0.01, zt), {"Head": 1.0})
    for j in range(SIDES):
        u0, u1 = j / SIDES, (j + 1) / SIDES
        B.f([bot, rings[0][j + 1], rings[0][j]], MAT_BARK, [((u0 + u1) / 2, 0.0), (u1, vz(RINGS[0][0])), (u0, vz(RINGS[0][0]))], part)
        B.f([rings[-1][j], rings[-1][j + 1], top], MAT_BARK, [(u0, vz(RINGS[-1][0])), (u1, vz(RINGS[-1][0])), ((u0 + u1) / 2, 1.0)], part)


def face_vertex(i, k):
    return trunk_point(i, SIDES // 2 + k)


def build_face():
    head = const("Head")
    # long drooping nose (4-sided, knotted)
    top = face_vertex(9, 0)
    nose = [top + Vector((0, 0.03, 0.03)), top + Vector((0, -0.035, -0.035)), top + Vector((0, -0.075, -0.10)),
            top + Vector((0.004, -0.085, -0.16)), top + Vector((0.006, -0.07, -0.19))]
    tube(nose, [(0.038, 0.030), (0.040, 0.034), (0.034, 0.030), (0.026, 0.022), (0.014, 0.012)], 4, head,
         ref=Vector((0, -1, 0)), jag=0.15, seed=11, cap_start=False, tint=(1.05, 1.0, 0.95))
    # brow overhang: a fat 5-sided ridge across the sockets
    bl, bc, br = face_vertex(10, 1), face_vertex(10, 0), face_vertex(10, -1)
    out = Vector((0, -0.028, 0.0))
    brow = [bl + Vector((0.075, 0.035, 0.045)), bl + out + Vector((0, 0, 0.012)), bc + out * 0.7 + Vector((0, 0, -0.03)),
            br + out + Vector((0, 0, 0.012)), br + Vector((-0.075, 0.035, 0.045))]     # angry V
    tube(brow, [0.022, 0.034, 0.028, 0.034, 0.022], 5, head, ref=Vector((0, 0, 1)), jag=0.2, seed=12,
         tint=(1.05, 1.0, 0.95))
    # eyes: small emissive diamonds deep in the sockets
    for s in (1, -1):
        c = face_vertex(9, s)
        nrm = Vector((c.x, c.y - RINGS[9][2], 0)).normalized()
        c = c + nrm * 0.012
        up, side = Vector((0, 0, 1)), Vector((-nrm.y, nrm.x, 0))
        a, b = 0.026, 0.014
        pts = [c + side * a, c + up * b, c - side * a, c - up * b]
        tip = c + nrm * 0.008
        back = c - nrm * 0.02
        ids = [X.v(p, {"Head": 1.0}) for p in pts]
        it, ib = X.v(tip, {"Head": 1.0}), X.v(back, {"Head": 1.0})
        for q in range(4):
            X.f([ids[q], ids[(q + 1) % 4], it], MAT_EYE, [(0.5, 0.5)] * 3)
            X.f([ids[(q + 1) % 4], ids[q], ib], MAT_EYE, [(0.5, 0.5)] * 3)


# ----------------------------------------------------------------------------
# 2. CROWN: antler branches + sparse dead-leaf clumps
# ----------------------------------------------------------------------------
LEAF_TIPS = []


def branch(base, direction, length, r0, seed, depth=0):
    d = Vector(direction).normalized()
    up = Vector((0, 0, 1))
    pts, p = [Vector(base)], Vector(base)
    segs = 4 if depth == 0 else 3
    for i in range(segs):
        bend = up * (0.30 - 0.20 * i) + Vector((hash01(seed, i, 1) - 0.5, hash01(seed, i, 2) - 0.5, 0)) * 0.5
        d = (d + bend * 0.45).normalized()
        p = p + d * (length / segs)
        pts.append(p.copy())
    radii = [r0 * (1 - 0.82 * i / segs) for i in range(segs + 1)]
    tube(pts, radii, 4 if depth == 0 else 3, const("Head"), ref=Vector((0, 0, 1)), jag=0.18, seed=seed,
         cap_start=False, tint=(0.95, 0.95, 0.95), moss=0.15)
    if depth == 0:
        fork = pts[2]
        side = d.cross(up)
        if side.length < 1e-3:
            side = Vector((1, 0, 0))
        fd = (d * 0.6 + side.normalized() * (0.7 if hash01(seed, 7) > 0.5 else -0.7) + up * 0.3)
        branch(fork, fd, length * 0.5, radii[2] * 0.85, seed + 100, depth=1)
    LEAF_TIPS.append((pts[-1], d))


def build_crown():
    top_z = RINGS[-1][0]
    lean = RINGS[-1][2]
    # (angle around from front (deg, + = left), elevation, length, radius)
    spec = [(40, 0.38, 0.70, 0.060), (-45, 0.40, 0.74, 0.062), (95, 0.22, 0.62, 0.052), (-100, 0.25, 0.60, 0.052),
            (150, 0.42, 0.55, 0.050), (-155, 0.38, 0.52, 0.048), (8, 0.75, 0.45, 0.050), (180, 0.65, 0.40, 0.045)]
    for n, (ang, elev, L, r) in enumerate(spec):
        a = math.radians(ang)
        out = Vector((math.sin(a), -math.cos(a), 0))
        base = Vector((0, lean, top_z - 0.02)) + out * 0.10
        d = out * (1 - elev) + Vector((0, 0, elev))
        branch(base, d, L, r, seed=20 + n * 7)
    # two broken spurs on the shoulders (silhouette breakers)
    for s in (1, -1):
        base = Vector((0.24 * s, 0.05, SHOULDER_Z + 0.10))
        tube([base, base + Vector((0.06 * s, 0.06, 0.14)), base + Vector((0.07 * s, 0.13, 0.26))],
             [0.035, 0.022, 0.006], 3, const("Chest"), jag=0.15, seed=60 + s, cap_start=False, moss=0.2)


# ----------------------------------------------------------------------------
# 3. SHOULDER BURLS + SHELF FUNGUS
# ----------------------------------------------------------------------------
def build_burls():
    for s in (1, -1):
        c = Vector((0.27 * s, -0.005, SHOULDER_Z + 0.04))
        shoulder = "LeftShoulder" if s > 0 else "RightShoulder"
        tube([c - Vector((0.07 * s, 0, 0.02)), c, c + Vector((0.06 * s, 0, 0.05))],
             [(0.12, 0.13), (0.13, 0.14), (0.06, 0.07)], 6, (lambda co, sh=shoulder: {"Chest": 0.45, sh: 0.55}), ref=Vector((0, -1, 0)), jag=0.18,
             seed=70 + s, moss=0.35)
    # shelf fungus stacked on the left flank (pale, the only light value on the body)
    for n, (z, r) in enumerate([(1.10, 0.075), (1.17, 0.06), (1.02, 0.05)]):
        a = math.radians(70 + n * 12)
        c = Vector((math.sin(a) * 0.255, -math.cos(a) * 0.255, z))
        out = Vector((math.sin(a), -math.cos(a), 0))
        tube([c - out * 0.02, c + out * r * 0.9], [(0.012, r), (0.006, r * 0.55)], 6, torso_weights,
             ref=Vector((0, 0, 1)), jag=0.1, seed=80 + n, cap_start=False, tint=(2.0, 1.95, 1.8), moss=-1.0)


def build_root_skirt():
    """Roots draping from the trunk base over the thighs: hides the trunk/leg join."""
    for n, ang in enumerate([-150, -105, -60, -20, 20, 60, 105, 150, 180]):
        a = math.radians(ang + 8 * (hash01(n, 9) - 0.5))
        out = Vector((math.sin(a), -math.cos(a), 0))
        side = "Left" if out.x > 0.05 else ("Right" if out.x < -0.05 else None)
        r0 = RINGS[1][1] * 0.95
        p0 = out * r0 + Vector((0, 0, 0.88))
        p1 = out * (r0 + 0.04) + Vector((0, 0, 0.74))
        p2 = out * (r0 + 0.03) + Vector((0, 0, 0.62 - 0.05 * hash01(n, 4)))

        def w(co, side=side):
            if side is None:
                return {"Hips": 1.0}
            t = smoothstep(0.86, 0.66, co.z) * 0.6
            return {"Hips": 1 - t, side + "UpperLeg": t}
        tube([p0, p1, p2], [0.07, 0.055, 0.02], 4, w, ref=Vector((0, 0, 1)), jag=0.2, seed=150 + n,
             cap_start=False, moss=0.2)


# ----------------------------------------------------------------------------
# 4. ARMS + TWIG HANDS (right fist wraps the club axis exactly like the old rig)
# ----------------------------------------------------------------------------
def build_arm(s):
    L = "Left" if s > 0 else "Right"
    chain = [(L + "Shoulder", (SHOULDER_IN * s, 0, SHOULDER_Z), (SHOULDER_X * s, 0, SHOULDER_Z)),
             (L + "UpperArm", (SHOULDER_X * s, 0, SHOULDER_Z), (ELBOW_X * s, 0, SHOULDER_Z)),
             (L + "LowerArm", (ELBOW_X * s, 0, SHOULDER_Z), (WRIST_X * s, 0, SHOULDER_Z)),
             (L + "Hand", (WRIST_X * s, 0, SHOULDER_Z), (KNUCKLE_X * s, 0, SHOULDER_Z))]
    wfn = chain_weights(chain, blend=0.05)
    Z = SHOULDER_Z
    mid_up, mid_lo = (SHOULDER_X + ELBOW_X) * 0.5, (ELBOW_X + WRIST_X) * 0.5
    xs = [0.17, SHOULDER_X, mid_up, ELBOW_X, mid_lo, WRIST_X - 0.01]
    rr = [(0.12, 0.125), (0.105, 0.11), (0.085, 0.09), (0.095, 0.10), (0.078, 0.082), (0.064, 0.07)]
    pts = [(x * s, 0.0 if x != ELBOW_X else 0.012, Z + (0.01 if x == mid_up else 0.0)) for x in xs]
    tube(pts, rr, 6, wfn, jag=0.16, seed=90 + s, cap_start=False, cap_end=False, moss=0.25, twist=0.35 * s)
    # knot spur on the forearm
    b = Vector(((mid_lo + 0.01) * s, 0.02, Z + 0.04))
    tube([b, b + Vector((0.03 * s, 0.05, 0.08)), b + Vector((0.07 * s, 0.07, 0.13))], [0.022, 0.014, 0.004], 3,
         const(L + "LowerArm"), jag=0.1, seed=95 + s, cap_start=False)
    hand = const(L + "Hand")
    # palm knot (wrist -> knuckles)
    tube([((WRIST_X - 0.03) * s, 0, Z), ((WRIST_X + 0.03) * s, 0, Z + 0.006), ((KNUCKLE_X + 0.004) * s, 0, Z + 0.002)],
         [(0.06, 0.068), (0.045, 0.07), (0.026, 0.062)], 5, hand, jag=0.12, seed=97 + s)
    # twig fingers: straight at rest along their bones (the pack's clips curl them into grips),
    # each skinned to its three phalanges, with a short claw tip past the distal joint
    for n, (finger, bones) in enumerate(finger_chains(s).items()):
        hand_seg = (L + "Hand", (WRIST_X * s, 0, Z), bones[0][1])
        fw = chain_weights([hand_seg] + bones, blend=0.012)
        h0, t2 = Vector(bones[0][1]), Vector(bones[-1][2])
        tip = t2 + (t2 - Vector(bones[-1][1])).normalized() * (0.026 if finger != "Thumb" else 0.018)             + Vector((0, 0, -0.008))
        path = [h0 - (Vector(bones[0][2]) - h0).normalized() * 0.012] + [Vector(b[1]) for b in bones] + [t2, tip]
        r0 = 0.021 if finger != "Thumb" else 0.019
        radii = [r0, r0, r0 * 0.85, r0 * 0.7, r0 * 0.55, r0 * 0.22]
        tube(path, radii, 3, fw, ref=Vector((0, 0, 1)), jag=0.15, seed=100 + n + 10 * s, cap_start=False)


# ----------------------------------------------------------------------------
# 5. LEGS + ROOT FEET
# ----------------------------------------------------------------------------
def build_leg(s):
    L = "Left" if s > 0 else "Right"
    chain = [("Hips", (HIP_X * s, 0, HIPS_Z + 0.07), (HIP_X * s, 0, HIP_Z)),
             (L + "UpperLeg", (HIP_X * s, 0, HIP_Z), (HIP_X * s, 0, KNEE_Z)),
             (L + "LowerLeg", (HIP_X * s, 0, KNEE_Z), (HIP_X * s, 0, ANKLE_Z))]
    wfn = chain_weights(chain, blend=0.06)
    zs = [0.92, 0.80, 0.66, KNEE_Z + 0.02, 0.34, 0.20, 0.11]
    rr = [0.15, 0.14, 0.12, 0.12, 0.10, 0.095, 0.11]
    pts = [(HIP_X * s * (1.0 + 0.15 * (z < 0.5)), -0.018 if abs(z - KNEE_Z - 0.02) < 1e-3 else 0.0, z) for z in zs]
    tube(pts, rr, 6, wfn, ref=Vector((0, -1, 0)), jag=0.15, seed=120 + s, cap_start=False, cap_end=False, moss=0.1,
         twist=0.25)
    foot, toes, low = L + "Foot", L + "Toes", L + "LowerLeg"

    def root_w(co):
        w = mix({low: 1.0}, {foot: 1.0}, smoothstep(ANKLE_Z + 0.04, ANKLE_Z - 0.04, co.z))
        return mix(w, {toes: 1.0}, smoothstep(TOE_Y + 0.03, TOE_Y - 0.05, co.y))
    fx = HIP_X * s * 1.15
    # root toes: (direction angle from forward (deg, + = outward), reach, radius)
    for n, (ang, reach, r) in enumerate([(-12, 0.30, 0.058), (22, 0.29, 0.054), (70, 0.20, 0.045), (165, 0.18, 0.05),
                                         (-50, 0.17, 0.04)]):
        a = math.radians(ang)
        d = Vector((math.sin(a) * s, -math.cos(a), 0))
        p0 = Vector((fx, 0, 0.17))
        p1 = p0 + d * reach * 0.35 + Vector((0, 0, -0.09))
        p2 = p0 + d * reach * 0.75 + Vector((0, 0, -0.14))
        p3 = p0 + d * reach + Vector((0, 0, -0.17))
        tube([p0, p1, p2, p3], [r, r * 0.8, r * 0.5, r * 0.18], 4, root_w, jag=0.15, seed=130 + n + 10 * s,
             cap_start=False, moss=0.3, tint=(0.9, 0.88, 0.85))


# ----------------------------------------------------------------------------
# 6. CARDS (alpha cut): moss beard, leaf clumps, hanging roots
# ----------------------------------------------------------------------------
TILE = {"moss": (0.0, 0.5), "leaf": (0.5, 0.5), "roots": (0.0, 0.0), "lichen": (0.5, 0.0)}


def card_strip(path, widths, normal, tile, wfn):
    """Vertical strip along `path` (top -> bottom), facing `normal`. UV: full tile, top at v=1."""
    ox, oy = TILE[tile]
    n = len(path)
    rows = []
    for i, p in enumerate(path):
        p = Vector(p)
        t = (Vector(path[min(i + 1, n - 1)]) - Vector(path[max(i - 1, 0)])).normalized()
        side = t.cross(Vector(normal)).normalized()
        w = widths[i]
        rows.append((X.v(p - side * w, wfn(p)), X.v(p + side * w, wfn(p))))
    for i in range(n - 1):
        v0, v1 = 1 - i / (n - 1), 1 - (i + 1) / (n - 1)
        a, b = rows[i]
        c, d = rows[i + 1]
        X.f([a, b, d, c], MAT_MOSS, [(ox + 0.01, oy + v0 * 0.49), (ox + 0.49, oy + v0 * 0.49),
                                     (ox + 0.49, oy + v1 * 0.49 + 0.005), (ox + 0.01, oy + v1 * 0.49 + 0.005)])


def front_y(z, k=0.0):
    # y of the trunk surface at height z and angle k*30deg (approx, for hanging cards)
    zs = [r[0] for r in RINGS]
    i = max(0, min(len(RINGS) - 2, sum(1 for q in zs if q <= z) - 1))
    t = (z - zs[i]) / (zs[i + 1] - zs[i])
    p0, p1 = face_vertex(i, int(round(k))), face_vertex(i + 1, int(round(k)))
    return p0.lerp(p1, max(0, min(1, t)))


def build_cards():
    # moss beard: 5 strips from under the nose/mouth down over the chest
    for n, (k, len_, w) in enumerate([(0, 0.52, 0.075), (0.7, 0.44, 0.07), (-0.7, 0.46, 0.07), (1.4, 0.30, 0.06),
                                      (-1.4, 0.32, 0.06)]):
        path, widths = [], []
        z0 = 1.60
        for i in range(4):
            z = z0 - len_ * i / 3
            p = front_y(z, k)
            ang = math.radians(k * 30)
            out = Vector((math.sin(ang), -math.cos(ang), 0))
            p = p + out * (0.035 + 0.02 * i) + Vector((0, 0, 0))
            path.append(p)
            widths.append(w * (1.0 - 0.15 * i))
        nrm = Vector((math.sin(math.radians(k * 30)), -math.cos(math.radians(k * 30)), 0.2))
        card_strip(path, widths, nrm, "moss", torso_weights)
    # hanging roots/moss from the arms (sleeves) and the back of the crown
    for s in (1, -1):
        L = "Left" if s > 0 else "Right"
        for x0, ln in ((0.40, 0.30), (0.62, 0.22)):
            p = Vector((x0 * s, 0.0, SHOULDER_Z - 0.05))
            card_strip([p, p + Vector((0.01 * s, 0.01, -ln * 0.5)), p + Vector((0.02 * s, 0.02, -ln))],
                       [0.045, 0.04, 0.03], (0, -1, 0), "roots" if x0 < 0.5 else "moss",
                       const(L + ("UpperArm" if x0 < ELBOW_X else "LowerArm")))
    # leaf clumps: a few small crossed quads hanging off branch tips (dying canopy)
    for n, (tip, d) in enumerate(LEAF_TIPS):
        if n % 3 != 0:
            continue
        size = 0.11 + 0.05 * hash01(n, 3)
        for rot in (0, 90):
            a = math.radians(rot + 37 * n)
            side = Vector((math.cos(a), math.sin(a), 0))
            nrm = side.cross(Vector((0, 0, 1))) + Vector((0, 0, 0.4))
            c = tip - d * 0.04
            card_strip([c + Vector((0, 0, size * 0.6)), c, c - Vector((0, 0, size * 1.1))],
                       [size, size * 1.05, size * 0.8], nrm, "leaf", const("Head"))


# ----------------------------------------------------------------------------
# Build
# ----------------------------------------------------------------------------
build_trunk()
build_face()
build_crown()
build_burls()
build_root_skirt()
for sd in (1, -1):
    build_arm(sd)
    build_leg(sd)
build_cards()


def skyline_pack(sizes, gap):
    """Bottom-left skyline packing into the unit square. Returns rects or None if it does not fit."""
    sky = [(0.0, 1.0, 0.0)]            # (x, width, height)
    rects = {}
    for i, (w, h) in sizes:
        w2, h2 = w + gap, h + gap
        best = None
        for si in range(len(sky)):
            x = sky[si][0]
            if x + w2 > 1.0 + 1e-9:
                break
            # height needed over [x, x + w2]
            top, need, j = 0.0, w2, si
            while need > 1e-9 and j < len(sky):
                top = max(top, sky[j][2])
                need -= sky[j][1]
                j += 1
            if top + h2 > 1.0 + 1e-9:
                continue
            if best is None or top < best[1] - 1e-9 or (abs(top - best[1]) < 1e-9 and x < best[0]):
                best = (x, top)
        if best is None:
            return None
        x, y = best
        rects[i] = (x + gap, y + gap, w, h)
        # update skyline
        new, x1 = [], x + w2
        for (sx, sw, sh) in sky:
            ex = sx + sw
            if ex <= x or sx >= x1:
                new.append((sx, sw, sh))
                continue
            if sx < x:
                new.append((sx, x - sx, sh))
            if ex > x1:
                new.append((x1, ex - x1, sh))
        new.append((x, w2, y + h2))
        new.sort()
        merged = []
        for seg in new:
            if merged and abs(merged[-1][2] - seg[2]) < 1e-12:
                merged[-1] = (merged[-1][0], merged[-1][1] + seg[1], seg[2])
            else:
                merged.append(seg)
        sky = merged
    return rects


def pack_parts(builder, gap_px=3):
    gap = gap_px / TEX
    order = sorted(range(len(builder.parts)), key=lambda i: -(builder.parts[i]["C"] * max(builder.parts[i]["L"], 0.3)))
    k = 1.4
    while True:
        rects = skyline_pack([(i, (builder.parts[i]["L"] * k, builder.parts[i]["C"] * k)) for i in order], gap)
        if rects is not None:
            break
        k *= 0.99
    for f in builder.faces:
        if f[3] is None:
            continue
        ox, oy, w, h = rects[f[3]]
        f[2] = [(ox + v * w, oy + u * h) for (u, v) in f[2]]
    for i, r in rects.items():
        builder.parts[i]["rect"] = r
    print("UV density px/m", round(k * TEX, 1))
    return k


UV_K = pack_parts(B)


def to_mesh(builder, name):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    bverts = [bm.verts.new(v) for v in builder.verts]
    bm.verts.ensure_lookup_table()
    for (idx, mat, uvs, _p) in builder.faces:
        try:
            f = bm.faces.new([bverts[i] for i in idx])
        except ValueError:
            continue
        f.material_index = mat
        f.smooth = True
        for loop, uv in zip(f.loops, uvs):
            loop[uv_layer].uv = uv
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00002)
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def add_weights(obj, builder):
    from mathutils.kdtree import KDTree
    groups = {n: (obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n)) for n in BONES}
    kd = KDTree(len(builder.verts))
    for i, v in enumerate(builder.verts):
        kd.insert(v, i)
    kd.balance()
    for v in obj.data.vertices:
        _, src, _ = kd.find(v.co)
        w = builder.weights[src]
        tot = sum(w.values())
        for b, val in w.items():
            if val > 1e-4:
                groups[b].add([v.index], val / tot, 'REPLACE')


# ----------------------------------------------------------------------------
# Materials (Blender preview; Unity remaps by name)
# ----------------------------------------------------------------------------
def image(name, size, float_buffer=False, alpha=False):
    img = bpy.data.images.get(name)
    if img is not None and (img.size[0] != size or img.size[1] != size):
        bpy.data.images.remove(img)
        img = None
    if img is None:
        img = bpy.data.images.new(name, size, size, alpha=alpha, float_buffer=float_buffer)
    return img


def tex_material(name, img, cutout=False):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (500, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location = (200, 0)
    tx = nt.nodes.new("ShaderNodeTexImage"); tx.location = (-200, 0)
    tx.image = img
    tx.interpolation = 'Closest'
    bsdf.inputs["Roughness"].default_value = 1.0
    for nm in ("Specular IOR Level", "Specular"):
        if nm in bsdf.inputs:
            bsdf.inputs[nm].default_value = 0.0
    nt.links.new(tx.outputs["Color"], bsdf.inputs["Base Color"])
    if cutout:
        nt.links.new(tx.outputs["Alpha"], bsdf.inputs["Alpha"])
        try:
            m.blend_method = 'CLIP'
        except Exception:
            pass
        m.use_backface_culling = False
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    return m, tx


def eye_material():
    m = bpy.data.materials.get(MAT_NAMES[2]) or bpy.data.materials.new(MAT_NAMES[2])
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*EYE_GLOW, 1)
    b.inputs["Emission Color"].default_value = (*EYE_GLOW, 1)
    b.inputs["Emission Strength"].default_value = 1.6
    return m


bark_img = image("T_ElderBark", TEX)
moss_img = image("T_ElderMoss", CARD_TEX, alpha=True)
mat_bark, bark_node = tex_material(MAT_NAMES[0], bark_img)
mat_moss, _ = tex_material(MAT_NAMES[1], moss_img, cutout=True)
mat_eye = eye_material()

body = bpy.data.objects.new("ElderTree", to_mesh(B, "ElderTree_Mesh"))
scene.collection.objects.link(body)
body.data.materials.append(mat_bark)
add_weights(body, B)
extras = bpy.data.objects.new("ElderTree_Extras", to_mesh(X, "ElderTree_Extras"))
scene.collection.objects.link(extras)
for m in (mat_bark, mat_moss, mat_eye):
    extras.data.materials.append(m)
add_weights(extras, X)


# ----------------------------------------------------------------------------
# Texture synthesis (numpy): bark atlas from baked AO / normal / position
# ----------------------------------------------------------------------------
def np_noise(x, y, seed):
    import numpy as np
    xi, yi = np.floor(x), np.floor(y)
    xf, yf = x - xi, y - yi
    u, v = xf * xf * (3 - 2 * xf), yf * yf * (3 - 2 * yf)

    def h(ix, iy):
        s = np.sin(ix * 127.1 + iy * 311.7 + seed * 74.7) * 43758.5453
        return s - np.floor(s)
    a, b = h(xi, yi), h(xi + 1, yi)
    c, d = h(xi, yi + 1), h(xi + 1, yi + 1)
    return (a + (b - a) * u) * (1 - v) + (c + (d - c) * u) * v


def fbm(x, y, seed, octaves=4):
    tot, amp, norm = 0.0, 0.5, 0.0
    for o in range(octaves):
        tot = tot + amp * np_noise(x * (2 ** o), y * (2 ** o), seed + o * 13)
        norm += amp
        amp *= 0.5
    return tot / norm


def sstep(e0, e1, x):
    import numpy as np
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def hexc(h):
    import numpy as np
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


BAYER4 = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def psx_quantize(rgb, bits=5, strength=1.0):
    """15-bit colour with 4x4 ordered dither (PS1 style)."""
    import numpy as np
    h, w, _ = rgb.shape
    bay = (np.array(BAYER4, dtype=np.float64) + 0.5) / 16.0 - 0.5
    d = np.tile(bay, (h // 4 + 1, w // 4 + 1))[:h, :w][..., None]
    levels = (1 << bits) - 1
    return np.clip(np.floor(np.clip(rgb, 0, 1) * levels + 0.5 + d * strength), 0, levels) / levels


def bake_pass(obj, kind, size, float_buffer, space=None):
    import numpy as np
    img = image(f"_bake_{kind}", size, float_buffer=float_buffer)
    bark_node.image = img
    mat_bark.node_tree.nodes.active = bark_node
    for o in scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    kw = dict(type=kind, margin=4, use_clear=True)
    if space:
        kw["normal_space"] = space
    bpy.ops.object.bake(**kw)
    px = np.empty(size * size * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(size, size, 4)


def downsample(a, f):
    h, w = a.shape[0] // f, a.shape[1] // f
    return a[:h * f, :w * f].reshape(h, f, w, f, -1).mean(axis=(1, 3))


def synth_bark(ao, nrm, pos):
    """All inputs (TEX, TEX, c). Returns RGB in [0,1] (rows bottom-up, Blender order)."""
    import numpy as np
    N = TEX
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    U, V = (xx + 0.5) / N, (yy + 0.5) / N
    along, around = U / UV_K, V / UV_K           # metres; grain runs "along" each part
    tint = np.ones((N, N, 3))
    mossb = np.zeros((N, N))
    for p in B.parts:
        x0, y0, w, h = p["rect"]
        sel = (U >= x0 - 2 / N) & (U <= x0 + w + 2 / N) & (V >= y0 - 2 / N) & (V <= y0 + h + 2 / N)
        tint[sel] = p["tint"]
        mossb[sel] = p["moss"]
    nz = nrm[..., 2] * 2 - 1
    pz = pos[..., 2]
    # --- bark: fibres, deep fissures, plates, cross cracks, knots
    warp = fbm(around * 3.0, along * 1.1, 1) - 0.5
    ridge = np.abs(np.sin(math.pi * (around * 9.0 + warp * 2.6)))           # 0 in the fissures
    fiss = 1 - sstep(0.0, 0.32, ridge)
    crest = sstep(0.72, 1.0, ridge)
    fib = fbm(around * 60, along * 5, 2, 3)
    plate = fbm(around * 6, along * 2.4, 3)
    cross = sstep(0.83, 0.9, fbm(around * 2.2, along * 10, 4)) * (1 - fiss)
    knots = sstep(0.80, 0.9, fbm(around * 2.4 + 7, along * 2.4 + 3, 5, 2))
    dark, mid, light = hexc("#2E251E"), hexc("#5C4A3B"), hexc("#847059")
    grit = np_noise(xx * 1.0, yy * 1.0, 31) - 0.5                         # per-texel grit
    t = np.clip(0.22 + 0.5 * plate + 0.55 * (fib - 0.5) + 0.22 * grit, 0, 1)[..., None]
    col = dark + (mid - dark) * np.clip(t * 1.6, 0, 1) + (light - mid) * np.clip(t * 1.6 - 1, 0, 1)
    col = col + hexc("#5A5650")[None, None] * 0.18 * crest[..., None] * (fib[..., None] > 0.45)
    col = col * (1 - 0.62 * fiss[..., None]) * (1 - 0.5 * cross[..., None])
    ring = np.abs(np.sin(knots * 9.0)) * (knots > 0.05)
    col = col * (1 - 0.35 * ring[..., None]) * (1 - 0.4 * knots[..., None])
    col = col * tint
    # --- lichen (pale grey-green crusts), sparse, on crests
    lich = sstep(0.66, 0.74, fbm(around * 9 + 40, along * 9, 6, 3)) * (1 - fiss) * (mossb > -0.5)
    col = col + (hexc("#8A9180") - col) * (0.55 * lich)[..., None]
    # --- moss: on upward faces, in fissures, near the ground
    mn = fbm(around * 5 + 9, along * 5, 7)
    moss_amt = sstep(0.05, 0.55, nz + mossb + 0.55 * (mn - 0.5)) * sstep(0.35, 0.6, fbm(around * 3, along * 3 + 5, 8))
    moss_amt = np.clip(moss_amt + 0.45 * fiss * sstep(0.45, 0.7, mn), 0, 1) * (mossb > -0.5)
    mcol = hexc("#2C3A1E") + (hexc("#62723D") - hexc("#2C3A1E")) * fbm(around * 70, along * 70, 9, 2)[..., None]
    col = col + (mcol - col) * moss_amt[..., None]
    # --- ground grime on root feet / shins
    grime = sstep(0.42, 0.05, pz)
    col = col * (1 - 0.35 * grime[..., None]) + hexc("#21190F") * (0.25 * grime)[..., None]
    # --- painted light: AO x soft moon-top light (PSX baked shading)
    shade = (0.45 + 0.55 * ao[..., 0]) * (0.84 + 0.16 * np.clip(nz, -1, 1))
    col = col * shade[..., None]
    # --- ember glow seeping from the eye sockets
    for s in (1, -1):
        c = face_vertex(9, s)
        d = np.sqrt((pos[..., 0] - c.x) ** 2 + (pos[..., 1] - c.y) ** 2 + (pos[..., 2] - c.z) ** 2)
        g = np.exp(-(d / 0.05) ** 2) * (0.55 + 0.45 * fib)
        col = col + np.array(EYE_GLOW)[None, None] * (0.55 * g)[..., None]
    return psx_quantize(col)


def synth_cards():
    """Alpha-cut card atlas, 2x2 tiles: moss strands | leaves / hanging roots | lichen."""
    import numpy as np
    N, T = CARD_TEX, CARD_TEX // 2
    rgba = np.zeros((N, N, 4))
    rng = np.random.default_rng(7)

    def stamp(x, y, r, col):
        x0, x1 = int(max(0, x - r - 1)), int(min(N, x + r + 2))
        y0, y1 = int(max(0, y - r - 1)), int(min(N, y + r + 2))
        if x0 >= x1 or y0 >= y1:
            return
        gy, gx = np.mgrid[y0:y1, x0:x1]
        m = (gx - x + 0.5) ** 2 + (gy - y + 0.5) ** 2 <= r * r
        rgba[y0:y1, x0:x1][m] = (*col, 1.0)

    def strands(ox, oy, count, w0, cols, wander, clump):
        for _ in range(count):
            x = ox + rng.uniform(4, T - 4)
            top = oy + T - rng.uniform(0, 10)
            ln = rng.uniform(0.45, 0.98) * (T - 6)
            ph, fr = rng.uniform(0, 6.3), rng.uniform(0.05, 0.15)
            base = cols[rng.integers(len(cols))]
            for i in range(int(ln)):
                y = top - i
                xx = x + math.sin(ph + i * fr) * wander
                r = max(0.6, w0 * (1 - i / ln * 0.7))
                shade = base * rng.uniform(0.7, 1.15)
                stamp(xx, y, r, shade)
                if clump and rng.random() < 0.05:
                    stamp(xx + rng.uniform(-2, 2), y, r + 1.5, base * rng.uniform(0.6, 1.0))
    moss_cols = [hexc("#33401F"), hexc("#46532B"), hexc("#566239"), hexc("#4A3F2F"), hexc("#636757")]
    strands(0, T, 46, 1.6, moss_cols, 2.5, True)
    root_cols = [hexc("#3D3025"), hexc("#524031"), hexc("#665240")]
    strands(0, 0, 14, 2.2, root_cols, 1.5, False)
    # leaves: dark, dying, clustered in a ragged disc
    leaf_cols = [hexc("#2C3B20"), hexc("#3F4F2A"), hexc("#55512F"), hexc("#64502C"), hexc("#3A3024")]
    cx, cy = T + T / 2, T + T / 2
    for _ in range(140):
        a, rr = rng.uniform(0, 6.3), (rng.uniform(0, 1) ** 0.6) * (T / 2 - 6)
        x, y = cx + math.cos(a) * rr, cy + math.sin(a) * rr * 0.9
        col = leaf_cols[rng.integers(len(leaf_cols))] * rng.uniform(0.75, 1.2)
        ang = rng.uniform(0, 3.14)
        for k in range(-3, 4):
            stamp(x + math.cos(ang) * k, y + math.sin(ang) * k, 2.2 - abs(k) * 0.45, col)
    # lichen fringe tile
    for _ in range(300):
        x, y = rng.uniform(T, N), rng.uniform(0, T * 0.7)
        stamp(x, y, rng.uniform(0.8, 2.2), hexc("#5E6656") * rng.uniform(0.6, 1.1))
    rgb = psx_quantize(rgba[..., :3])
    rgba[..., :3] = rgb
    return rgba


def bake_textures(tex_dir):
    import numpy as np
    os.makedirs(tex_dir, exist_ok=True)
    prev_engine = scene.render.engine
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 48
    try:
        scene.cycles.device = 'GPU'
    except Exception:
        pass
    if scene.world is None:
        scene.world = bpy.data.worlds.new("BakeWorld")
    scene.world.light_settings.distance = 0.22
    extras.hide_render = True
    S = TEX * 2
    ao = downsample(bake_pass(body, 'AO', S, False), 2)
    nrm = downsample(bake_pass(body, 'NORMAL', S, False, 'OBJECT'), 2)
    pos = downsample(bake_pass(body, 'POSITION', S, True), 2)
    extras.hide_render = False
    rgb = synth_bark(ao, nrm, pos)
    out = np.ones((TEX, TEX, 4), dtype=np.float32)
    out[..., :3] = rgb
    bark_img.pixels.foreach_set(out.ravel())
    bark_img.filepath_raw = os.path.join(tex_dir, "T_ElderBark.png")
    bark_img.file_format = 'PNG'
    bark_img.save()
    cards = synth_cards().astype(np.float32)
    moss_img.pixels.foreach_set(cards.ravel())
    moss_img.filepath_raw = os.path.join(tex_dir, "T_ElderMoss.png")
    moss_img.file_format = 'PNG'
    moss_img.save()
    bark_node.image = bark_img
    try:
        scene.render.engine = prev_engine
    except TypeError:
        pass
    print("BAKED", tex_dir)


if BAKE_DIR:
    bake_textures(BAKE_DIR)

# join cards + eyes into the body (after the bake so they cast no AO)
for o in scene.objects:
    o.select_set(False)
body.select_set(True)
extras.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
try:
    body.data.set_sharp_from_angle(angle=math.radians(52))
except Exception as e:
    print("sharp edges skipped", e)

# ----------------------------------------------------------------------------
# Armature
# ----------------------------------------------------------------------------
arm_data = bpy.data.armatures.new("ElderTree_Armature")
arm_obj = bpy.data.objects.new("Armature", arm_data)
scene.collection.objects.link(arm_obj)
for o in scene.objects:
    o.select_set(False)
bpy.context.view_layer.objects.active = arm_obj
arm_obj.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = {}
for name, (h, t, parent) in BONES.items():
    b = arm_data.edit_bones.new(name)
    b.head, b.tail, b.roll = Vector(h), Vector(t), 0.0
    eb[name] = b
for name, (h, t, parent) in BONES.items():
    if parent:
        eb[name].parent = eb[parent]
        eb[name].use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
body.parent = arm_obj
mod = body.modifiers.new("Armature", 'ARMATURE')
mod.object = arm_obj


def export(objs, path):
    for o in scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=False, armature_nodetype='NULL', bake_anim=False,
        colors_type='NONE',
    )
    print("EXPORTED", path)


if DO_EXPORT:
    os.makedirs(OUT_DIR, exist_ok=True)
    export([arm_obj, body], os.path.join(OUT_DIR, "ElderTree.fbx"))
    if bpy.app.background:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "ElderTree_Source.blend"))

tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
print("BUILD OK verts", len(body.data.vertices), "tris", tris, "parts", len(B.parts))
