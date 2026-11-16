"""
Tung Tung Tung Sahur - procedural character builder for Blender (4.2+ / 5.x).

v2: sculpted look matching the reference image. The log is a dense lathe whose
front is displaced by an analytic height field (carved brow ridge, eye sockets
with lids, bulbous nose, apple cheeks, smirking lips, nasolabial folds, chin).
Big glossy eyeballs sit in the sockets. Thin limbs, gripping hands, big bare
feet. A Unity-Humanoid armature with computed skin weights. Plain wooden bat.

Textures are baked (albedo with AO + tangent-space normal) into a hand-laid
atlas: the log is unwrapped cylindrically (seam at the back, face seam-free),
limb tubes are shelf-packed underneath.

Run headless:
    blender -b --factory-startup --python build_tung_sahur.py -- <out_dir> [--bake <tex_dir>]

Run live (Blender MCP execute_code):
    g = {"__name__": "__main__", "__file__": path, "TS_OUT_DIR": out, "TS_EXPORT": False}
    exec(compile(open(path).read(), path, "exec"), g)

Conventions: Z up, character faces -Y, character's LEFT side is +X.
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector

# ----------------------------------------------------------------------------
# Args / config
# ----------------------------------------------------------------------------
_argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(globals().get("__file__", bpy.data.filepath or ".")))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = globals().get("TS_OUT_DIR") or (_argv[0] if _argv and not _argv[0].startswith("--") else os.path.join(HERE, "export"))
DO_EXPORT = globals().get("TS_EXPORT", True)
BAKE_DIR = globals().get("TS_BAKE_DIR")
if "--bake" in _argv:
    BAKE_DIR = _argv[_argv.index("--bake") + 1]
BAKE_SIZE = globals().get("TS_BAKE_SIZE", 4096)
UNITY_TEX = os.path.join(PROJECT, "Assets", "TungSahur", "Art", "Textures")
os.makedirs(OUT_DIR, exist_ok=True)

# ----------------------------------------------------------------------------
# Proportions (metres)
# ----------------------------------------------------------------------------
LOG_BOTTOM = 0.80
LOG_TOP = 1.95
R_BOTTOM = 0.272
R_TOP = 0.262
R0 = 0.268               # reference radius for face coordinates (arc length s = R0 * theta)
BEVEL_T = 0.065
BEVEL_B = 0.070

SHOULDER_X = 0.295
SHOULDER_Z = 1.25
ELBOW_X = 0.535
WRIST_X = 0.775
HAND_END_X = 0.875

HIP_X = 0.12
HIP_Z = 0.88
KNEE_Z = 0.47
ANKLE_Z = 0.09

BONES = {
    # name: (head, tail, parent)
    "Hips":          ((0, 0.0, 0.90), (0, 0.0, 1.02), None),
    "Spine":         ((0, 0.0, 1.02), (0, 0.0, 1.12), "Hips"),
    "Chest":         ((0, 0.0, 1.12), (0, 0.0, 1.22), "Spine"),
    "UpperChest":    ((0, 0.0, 1.22), (0, 0.0, 1.40), "Chest"),
    "Neck":          ((0, 0.0, 1.40), (0, 0.0, 1.52), "UpperChest"),
    "Head":          ((0, 0.0, 1.52), (0, 0.0, 1.95), "Neck"),
    "LeftShoulder":  ((0.07, 0.0, SHOULDER_Z), (SHOULDER_X, 0.0, SHOULDER_Z), "UpperChest"),
    "LeftUpperArm":  ((SHOULDER_X, 0.0, SHOULDER_Z), (ELBOW_X, 0.0, SHOULDER_Z), "LeftShoulder"),
    "LeftLowerArm":  ((ELBOW_X, 0.0, SHOULDER_Z), (WRIST_X, 0.0, SHOULDER_Z), "LeftUpperArm"),
    "LeftHand":      ((WRIST_X, 0.0, SHOULDER_Z), (HAND_END_X, 0.0, SHOULDER_Z), "LeftLowerArm"),
    "RightShoulder": ((-0.07, 0.0, SHOULDER_Z), (-SHOULDER_X, 0.0, SHOULDER_Z), "UpperChest"),
    "RightUpperArm": ((-SHOULDER_X, 0.0, SHOULDER_Z), (-ELBOW_X, 0.0, SHOULDER_Z), "RightShoulder"),
    "RightLowerArm": ((-ELBOW_X, 0.0, SHOULDER_Z), (-WRIST_X, 0.0, SHOULDER_Z), "RightUpperArm"),
    "RightHand":     ((-WRIST_X, 0.0, SHOULDER_Z), (-HAND_END_X, 0.0, SHOULDER_Z), "RightLowerArm"),
    "LeftUpperLeg":  ((HIP_X, 0.0, HIP_Z), (HIP_X, 0.0, KNEE_Z), "Hips"),
    "LeftLowerLeg":  ((HIP_X, 0.0, KNEE_Z), (HIP_X, 0.0, ANKLE_Z), "LeftUpperLeg"),
    "LeftFoot":      ((HIP_X, 0.0, ANKLE_Z), (HIP_X, -0.18, 0.028), "LeftLowerLeg"),
    "LeftToes":      ((HIP_X, -0.18, 0.028), (HIP_X, -0.28, 0.028), "LeftFoot"),
    "RightUpperLeg": ((-HIP_X, 0.0, HIP_Z), (-HIP_X, 0.0, KNEE_Z), "Hips"),
    "RightLowerLeg": ((-HIP_X, 0.0, KNEE_Z), (-HIP_X, 0.0, ANKLE_Z), "RightUpperLeg"),
    "RightFoot":     ((-HIP_X, 0.0, ANKLE_Z), (-HIP_X, -0.18, 0.028), "RightLowerLeg"),
    "RightToes":     ((-HIP_X, -0.18, 0.028), (-HIP_X, -0.28, 0.028), "RightFoot"),
}

MAT_WOOD, MAT_EYE = 0, 1
LIMB_UV_TOP = 0.355      # limbs packed in v < this; the log fills v in [LOG_UV_V0, 1]
LOG_UV_V0 = 0.365

# ----------------------------------------------------------------------------
# Face design (s = arc length from the front centre, + = character's left)
# ----------------------------------------------------------------------------
EYE_S = 0.116
EYE_Z = 1.655
EYE_A = 0.071            # opening half-width (arc)
EYE_B = 0.062            # opening half-height
EYE_R = 0.083            # eyeball radius
EYE_FRONT = 0.022        # eyeball front pole height above the log surface
EYE_TOE_IN = 0.62        # 0 = gaze along surface normal, 1 = straight ahead (-Y)


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def g2(ds, dz, ss, sz):
    return math.exp(-(ds / ss) ** 2 - (dz / sz) ** 2)


def smax(a, b, k=0.006):
    m = max(a, b)
    return m + k * math.log(math.exp((a - m) / k) + math.exp((b - m) / k))


def mouth_z(s):
    return 1.430 + 2.15 * s * s + 0.11 * s


def face_field(s, z):
    """Returns (height, darkness, redness) of the carved face at arc s, height z."""
    h = 0.0
    dark = 0.0
    red = 0.0
    a = abs(s)
    side = 1 if s >= 0 else -1

    # --- brow ridge (heavy, arched, carved into the wood; darker crest)
    amp = 0.028 * smoothstep(0.035, 0.085, a) * (1 - smoothstep(0.18, 0.235, a))
    zb = EYE_Z + 0.098 + 0.020 * math.exp(-((a - 0.115) / 0.055) ** 2) - 0.45 * max(0.0, a - 0.15) ** 1.5
    zb += 0.006 * side  # the smirk side brow sits a touch higher
    below = z < zb
    h += amp * math.exp(-((z - zb) / (0.015 if below else 0.028)) ** 2)
    dark += 0.30 * smoothstep(0.03, 0.06, a) * (1 - smoothstep(0.16, 0.2, a)) * math.exp(-((z - zb - 0.004) / 0.010) ** 2)
    # glabella / nose root
    h += 0.006 * g2(s, z - (EYE_Z + 0.030), 0.030, 0.040)

    # --- cheeks (smile-raised apples, smirk lifts the left one)
    cz = 1.525 + 0.010 * side
    h += 0.040 * g2(a - 0.130, z - cz, 0.056, 0.046)
    red += 0.30 * g2(a - 0.130, z - cz, 0.05, 0.04)

    # --- chin
    h += 0.016 * g2(s, z - 1.335, 0.050, 0.028)

    # --- nose: bridge + broad bulbous tip + wings (smooth union)
    bridge = (0.006 + 0.034 * smoothstep(1.63, 1.53, z) ** 1.5) * math.exp(-(s / (0.016 + 0.010 * smoothstep(1.62, 1.52, z))) ** 2)         * smoothstep(1.69, 1.63, z) * smoothstep(1.47, 1.52, z)
    tz = 1.506
    tip = 0.056 * math.exp(-(s / 0.037) ** 2 - ((z - tz) / (0.030 if z > tz else 0.020)) ** 2)
    wings = 0.030 * g2(a - 0.036, z - 1.494, 0.018, 0.016)
    nose = smax(smax(bridge, tip), wings)
    h += nose
    dark += 0.75 * g2(a - 0.020, z - 1.478, 0.011, 0.0055)  # nostrils
    dark += 0.30 * g2(a - 0.050, z - 1.490, 0.007, 0.014)   # wing crease

    # --- mouth: deep crease + thin lips, smirk to the left
    zm = mouth_z(s)
    reach = 0.150 if s > 0 else 0.134
    wm = smoothstep(reach + 0.010, reach - 0.030, a)
    h += -0.016 * wm * math.exp(-((z - zm) / 0.0055) ** 2)
    dark += 1.0 * wm * math.exp(-((z - zm) / 0.0058) ** 2)
    h += 0.004 * wm * math.exp(-((z - (zm + 0.012)) / 0.008) ** 2)
    h += 0.007 * wm * math.exp(-((z - (zm - 0.017)) / 0.010) ** 2)
    red += 0.45 * wm * math.exp(-((z - (zm - 0.016)) / 0.011) ** 2)
    red += 0.25 * wm * math.exp(-((z - (zm + 0.010)) / 0.007) ** 2)
    # mouth corners (dimples)
    zc = mouth_z(side * reach)
    h -= 0.010 * g2(a - reach, z - zc, 0.012, 0.012)
    dark += 0.55 * g2(a - reach, z - zc, 0.010, 0.010)
    # groove under the lower lip
    h -= 0.005 * wm * math.exp(-((z - (zm - 0.040)) / 0.008) ** 2)
    dark += 0.15 * wm * math.exp(-((z - (zm - 0.040)) / 0.007) ** 2)

    # --- nasolabial folds (from the nose wings, around the smile)
    if 0.040 < a < 0.185:
        t = a - 0.045
        zl = 1.488 - t * 0.50 - 0.9 * t * t + 0.010 * side * smoothstep(0.0, 0.1, t)
        f = smoothstep(0.040, 0.06, a) * (1 - smoothstep(0.16, 0.185, a))
        h -= 0.009 * f * math.exp(-((z - zl) / 0.0075) ** 2)
        dark += 0.35 * f * math.exp(-((z - zl) / 0.0065) ** 2)

    # --- eyes: socket shadow, lids, opening
    for es in (EYE_S, -EYE_S):
        ds = s - es
        dz = z - EYE_Z
        re = math.sqrt((ds / EYE_A) ** 2 + (dz / EYE_B) ** 2)
        if re > 2.6:
            continue
        up = smoothstep(-0.35, 0.35, dz / EYE_B)
        inner = smoothstep(0.0, -0.06, ds * (1 if es > 0 else -1))   # 1 towards the nose
        vert = min(1.0, abs(dz) / (EYE_B * re + 1e-6))                # 0 at the corners
        # socket hollow + shadow ring (lighter towards the nose bridge)
        h += -0.018 * math.exp(-((re - 1.50) / 0.42) ** 2) * (0.75 + 0.25 * up)
        dark += 0.70 * math.exp(-((re - 1.40) / 0.42) ** 2) * (0.75 + 0.25 * (1 - up)) * (1 - 0.55 * inner)
        # lids: heavy upper lid, thinner lower lid + bag, softer at the corners (almond)
        h += up * 0.020 * math.exp(-((re - 1.12) / 0.13) ** 2) * (0.55 + 0.45 * vert)
        h += (1 - up) * (0.009 * math.exp(-((re - 1.12) / 0.12) ** 2) * (0.5 + 0.5 * vert)
                         + 0.010 * math.exp(-((re - 1.60) / 0.22) ** 2))
        dark += up * 0.35 * math.exp(-((re - 1.36) / 0.08) ** 2)          # upper lid fold
        dark += 0.70 * math.exp(-((re - 1.01) / 0.05) ** 2)               # lid line
        # keep the lid above the eyeball, then open the hole behind it
        r = math.hypot(ds, dz)
        if r < EYE_R:
            sph = (EYE_FRONT - EYE_R) + math.sqrt(EYE_R * EYE_R - r * r)
            if re >= 0.97:
                h = smax(h, sph + 0.005, 0.003)
        if re < 1.0:
            t = smoothstep(0.88, 1.0, re)
            h = h * t + (-0.09) * (1 - t)
    return h, min(dark, 1.0), min(red, 1.0)


# ----------------------------------------------------------------------------
# Scene reset (safe in a live session: works in a dedicated scene)
# ----------------------------------------------------------------------------
if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
else:
    scene = bpy.data.scenes.get("TungSahur") or bpy.data.scenes.new("TungSahur")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.cameras, bpy.data.lights):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)
    try:
        bpy.context.window.scene = scene
    except Exception:
        pass
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0


def log_radius(z):
    t = max(0.0, min(1.0, (z - LOG_BOTTOM) / (LOG_TOP - LOG_BOTTOM)))
    return R_BOTTOM + (R_TOP - R_BOTTOM) * t + 0.007 * math.sin(t * math.pi)


# ----------------------------------------------------------------------------
# Mesh builder
# ----------------------------------------------------------------------------
class Builder:
    def __init__(self):
        self.verts, self.weights, self.colors = [], [], []
        self.faces = []        # [idx, mat, uvs, part]
        self.parts = []        # packed UV parts: dict(C=, L=)

    def add_vert(self, co, w, col=(0.0, 0.0, 0.0)):
        self.verts.append(Vector(co))
        self.weights.append(w)
        self.colors.append(col)
        return len(self.verts) - 1

    def add_face(self, idx, mat, uvs, part=None):
        self.faces.append([idx, mat, uvs, part])


B = Builder()


def ring_frame(tangent, ref):
    t = tangent.normalized()
    n = ref - t * ref.dot(t)
    if n.length < 1e-5:
        n = Vector((0, 0, 1)) - t * t.z
        if n.length < 1e-5:
            n = Vector((1, 0, 0))
    n.normalize()
    b = t.cross(n).normalized()
    return t, n, b


def tube(points, radii, segs, mat, weight_fn, ref=Vector((0, 0, 1)), cap_start=True, cap_end=True, col=(0, 0, 1)):
    """Smooth tube along `points`. radii: float or (rn, rb) per point. Hemispherical caps.
    UVs: u = around, v = along (normalised); the tube becomes one packed atlas part."""
    pts = [Vector(p) for p in points]
    rad = [(r, r) if isinstance(r, (int, float)) else r for r in radii]

    def tangent_at(i):
        if i == 0:
            return pts[1] - pts[0]
        if i == len(pts) - 1:
            return pts[-1] - pts[-2]
        return pts[i + 1] - pts[i - 1]

    centers, rr, tans = [], [], []
    cap_steps = 4
    t0 = tangent_at(0).normalized()
    if cap_start:
        for k in range(cap_steps, 0, -1):
            a = (k / cap_steps) * (math.pi / 2) * 0.92
            centers.append(pts[0] - t0 * math.sin(a) * min(rad[0]))
            rr.append((rad[0][0] * math.cos(a), rad[0][1] * math.cos(a)))
            tans.append(t0)
    for i, p in enumerate(pts):
        centers.append(p)
        rr.append(rad[i])
        tans.append(tangent_at(i))
    t1 = tangent_at(len(pts) - 1).normalized()
    if cap_end:
        for k in range(1, cap_steps + 1):
            a = (k / cap_steps) * (math.pi / 2) * 0.92
            centers.append(pts[-1] + t1 * math.sin(a) * min(rad[-1]))
            rr.append((rad[-1][0] * math.cos(a), rad[-1][1] * math.cos(a)))
            tans.append(t1)

    acc = [0.0]
    for i in range(1, len(centers)):
        acc.append(acc[-1] + (centers[i] - centers[i - 1]).length)
    total = max(acc[-1], 1e-6)
    circ = sum(math.pi * (a + b) for a, b in rad) / len(rad)
    part = len(B.parts)
    B.parts.append({"C": circ, "L": total + 2 * 0.3 * min(rad[0])})

    rings = []
    prev_n = None
    for i, c in enumerate(centers):
        t, n, b = ring_frame(tans[i], ref if prev_n is None else prev_n)
        prev_n = n
        rx, ry = rr[i]
        ring = []
        for j in range(segs + 1):  # duplicated seam column for clean UVs
            ang = 2 * math.pi * j / segs
            co = c + n * math.cos(ang) * rx + b * math.sin(ang) * ry
            ring.append(B.add_vert(co, weight_fn(co), col))
        rings.append(ring)
    for i in range(len(rings) - 1):
        v0, v1 = acc[i] / total, acc[i + 1] / total
        for j in range(segs):
            u0, u1 = j / segs, (j + 1) / segs
            B.add_face([rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]], mat,
                       [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], part)
    if cap_start:
        c = centers[0] - t0 * min(rad[0]) * 0.08
        pole = B.add_vert(c, weight_fn(c), col)
        for j in range(segs):
            B.add_face([pole, rings[0][j + 1], rings[0][j]], mat,
                       [((j + 0.5) / segs, 0.0), ((j + 1) / segs, 0.0), (j / segs, 0.0)], part)
    if cap_end:
        c = centers[-1] + t1 * min(rad[-1]) * 0.08
        pole = B.add_vert(c, weight_fn(c), col)
        for j in range(segs):
            B.add_face([rings[-1][j], rings[-1][j + 1], pole], mat,
                       [(j / segs, 1.0), ((j + 1) / segs, 1.0), ((j + 0.5) / segs, 1.0)], part)


# ----------------------------------------------------------------------------
# Weights
# ----------------------------------------------------------------------------
TORSO_BONES = [("Hips", 0.92), ("Spine", 1.07), ("Chest", 1.17), ("UpperChest", 1.30), ("Neck", 1.46), ("Head", 1.56)]


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
            tc = max(0.0, min(1.0, t))
            dist = (a + d * tc - co).length
            if best is None or dist < best[0] - 1e-9:
                best = (dist, idx, t, L)
        _, idx, t, L = best
        w = {segs[idx][0]: 1.0}
        along = t * L
        if idx > 0 and along < blend:
            k = 0.5 * (1 - along / blend) if along > -blend else 0.5
            k = max(0.0, min(0.5, k))
            w = {segs[idx][0]: 1 - k, segs[idx - 1][0]: k}
        elif idx < len(segs) - 1 and (L - along) < blend:
            k = 0.5 * (1 - (L - along) / blend)
            k = max(0.0, min(0.5, k))
            w = {segs[idx][0]: 1 - k, segs[idx + 1][0]: k}
        return w
    return fn


def mix(a, b, t):
    out = {}
    for k, v in a.items():
        out[k] = out.get(k, 0) + v * (1 - t)
    for k, v in b.items():
        out[k] = out.get(k, 0) + v * t
    return out


# ----------------------------------------------------------------------------
# 1. LOG BODY: dense lathe + carved face height field
# ----------------------------------------------------------------------------
def build_log():
    dense = 1.35
    n_dense = int(round(2 * dense * R0 / 0.0048))
    n_half_back = 12
    back_half = math.pi - dense
    thetas = [math.pi + back_half * k / n_half_back for k in range(n_half_back)]          # back -> left side
    thetas = [t - 2 * math.pi for t in thetas]                                            # -pi .. -dense
    thetas += [-dense + 2 * dense * k / n_dense for k in range(n_dense)]                   # front
    thetas += [dense + back_half * k / n_half_back for k in range(n_half_back + 1)]       # .. +pi (seam dup)
    # profile: (z, base radius, carve?)
    prof = []
    rflat_b = R_BOTTOM - BEVEL_B
    for f in (0.0, 0.34, 0.67):
        prof.append((LOG_BOTTOM, rflat_b * f, False))
    for k in range(0, 9):
        a = k / 8 * (math.pi / 2)
        prof.append((LOG_BOTTOM + BEVEL_B * (1 - math.cos(a)), R_BOTTOM - BEVEL_B * (1 - math.sin(a)), False))
    z = LOG_BOTTOM + BEVEL_B
    z_top_side = LOG_TOP - BEVEL_T
    while True:
        step = 0.0042 if 1.29 <= z <= 1.80 else 0.028
        z = min(z + step, z_top_side)
        prof.append((z, log_radius(z), True))
        if z >= z_top_side:
            break
    for k in range(1, 9):
        a = k / 8 * (math.pi / 2)
        zz = z_top_side + BEVEL_T * math.sin(a)
        prof.append((zz, log_radius(zz) - BEVEL_T * (1 - math.cos(a)), False))
    rflat_t = prof[-1][1]
    for f in (0.67, 0.34, 0.0):
        prof.append((LOG_TOP, rflat_t * f, False))

    # arc-length v
    vs = [0.0]
    for i in range(1, len(prof)):
        vs.append(vs[-1] + math.hypot(prof[i][0] - prof[i - 1][0], prof[i][1] - prof[i - 1][1]))
    vtot = vs[-1]

    rings = []
    for (z, r, carve) in prof:
        ring = []
        for th in thetas:
            h, dark, red = 0.0, 0.0, 0.0
            if carve and abs(th) < dense:
                h, dark, red = face_field(R0 * th, z)
                h *= smoothstep(dense, dense - 0.25, abs(th))
            if r > 0.05:
                h += 0.0012 * math.sin(3 * th + z * 9.0) + 0.0009 * math.sin(7 * th - z * 13.0)
            rr = max(r + h, 0.0)
            co = Vector((math.sin(th) * rr, -math.cos(th) * rr, z))
            ring.append(B.add_vert(co, torso_weights(co), (dark, red, 0.0)))
        rings.append(ring)
    n = len(thetas)
    for i in range(len(rings) - 1):
        va = LOG_UV_V0 + (1 - LOG_UV_V0) * vs[i] / vtot
        vb = LOG_UV_V0 + (1 - LOG_UV_V0) * vs[i + 1] / vtot
        for j in range(n - 1):
            ua = (thetas[j] + math.pi) / (2 * math.pi)
            ub = (thetas[j + 1] + math.pi) / (2 * math.pi)
            B.add_face([rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]], MAT_WOOD,
                       [(ua, va), (ub, va), (ub, vb), (ua, vb)])


# ----------------------------------------------------------------------------
# 2. EYES (glossy spheres in the sockets, UV pole = pupil)
# ----------------------------------------------------------------------------
def eye_frame(side):
    th = side * EYE_S / R0
    nrm = Vector((math.sin(th), -math.cos(th), 0.0))
    surf_r = log_radius(EYE_Z)
    center = nrm * (surf_r + EYE_FRONT - EYE_R) + Vector((0, 0, EYE_Z))
    gaze = nrm.lerp(Vector((0, -1, 0)), EYE_TOE_IN).normalized()
    gaze = (gaze + Vector((0, 0, 0.03))).normalized()
    return center, gaze


def build_eye(side):
    center, gaze = eye_frame(side)
    segs, rings_n = 40, 20
    up = Vector((0, 0, 1))
    right = gaze.cross(up).normalized()
    up2 = right.cross(gaze).normalized()
    wfn = lambda co: torso_weights(Vector((co.x, co.y, EYE_Z)))
    vr = []
    for i in range(1, rings_n):
        th = math.pi * i / rings_n
        ring = []
        for j in range(segs):
            ph = 2 * math.pi * j / segs
            d = gaze * math.cos(th) + (right * math.cos(ph) + up2 * math.sin(ph)) * math.sin(th)
            ring.append(B.add_vert(center + d * EYE_R, wfn(center)))
        vr.append((th, ring))

    def uv_of(th, ph):
        r = 0.5 * (math.sin(th) if th <= math.pi / 2 else 1.0)
        return (0.5 + r * math.cos(ph), 0.5 + r * math.sin(ph))

    for k in range(len(vr) - 1):
        th0, r0 = vr[k]
        th1, r1 = vr[k + 1]
        for j in range(segs):
            j2 = (j + 1) % segs
            p0, p1 = 2 * math.pi * j / segs, 2 * math.pi * (j + 1) / segs
            B.add_face([r0[j], r1[j], r1[j2], r0[j2]], MAT_EYE,
                       [uv_of(th0, p0), uv_of(th1, p0), uv_of(th1, p1), uv_of(th0, p1)])
    front = B.add_vert(center + gaze * EYE_R, wfn(center))
    back = B.add_vert(center - gaze * EYE_R, wfn(center))
    th0, r0 = vr[0]
    thn, rn = vr[-1]
    for j in range(segs):
        j2 = (j + 1) % segs
        p0, p1 = 2 * math.pi * j / segs, 2 * math.pi * (j + 1) / segs
        B.add_face([front, r0[j], r0[j2]], MAT_EYE, [(0.5, 0.5), uv_of(th0, p0), uv_of(th0, p1)])
        B.add_face([rn[j2], rn[j], back], MAT_EYE, [uv_of(thn, p1), uv_of(thn, p0), (0.5, 0.5)])


# ----------------------------------------------------------------------------
# 3. ARMS + HANDS (right hand grips the bat, left hand relaxed)
# ----------------------------------------------------------------------------
def build_arm(side):
    s = side
    L = "Left" if s > 0 else "Right"
    chain = [
        (L + "Shoulder", (0.07 * s, 0, SHOULDER_Z), (SHOULDER_X * s, 0, SHOULDER_Z)),
        (L + "UpperArm", (SHOULDER_X * s, 0, SHOULDER_Z), (ELBOW_X * s, 0, SHOULDER_Z)),
        (L + "LowerArm", (ELBOW_X * s, 0, SHOULDER_Z), (WRIST_X * s, 0, SHOULDER_Z)),
        (L + "Hand", (WRIST_X * s, 0, SHOULDER_Z), (HAND_END_X * s, 0, SHOULDER_Z)),
    ]
    wfn = chain_weights(chain, blend=0.045)
    hand_w = lambda co: {L + "Hand": 1.0}
    SX = SHOULDER_X
    xs = [SX - 0.08, SX - 0.035, SX, SX + 0.04, SX + 0.10, SX + 0.16, ELBOW_X - 0.02, ELBOW_X, ELBOW_X + 0.03,
          ELBOW_X + 0.07, ELBOW_X + 0.13, ELBOW_X + 0.19, WRIST_X - 0.01, WRIST_X + 0.012]
    rr = [0.043, 0.043, 0.041, 0.038, 0.036, 0.034, 0.032, 0.033, 0.033, 0.034, 0.033,
          0.030, 0.026, 0.026]
    pts = [(x * s, 0.0, SHOULDER_Z + (0.003 if SX < x < ELBOW_X - 0.04 else 0.0)) for x in xs]
    radii = [(r * 1.08, r * 1.08 * (1.12 if x > ELBOW_X + 0.02 else 1.0)) for x, r in zip(xs, rr)]
    tube(pts, radii, 16, MAT_WOOD, wfn, ref=Vector((0, 0, 1)), cap_start=True, cap_end=False)

    z0 = SHOULDER_Z
    grip = s < 0
    # palm: flattened block sitting on top of the grip axis
    pz = 0.024 if grip else 0.012
    palm_pts = [((WRIST_X - 0.004) * s, 0, z0), ((WRIST_X + 0.025) * s, 0, z0 + pz * 0.6),
                ((WRIST_X + 0.055) * s, 0, z0 + pz), ((WRIST_X + 0.074) * s, 0, z0 + pz)]
    palm_r = [(0.024, 0.030), (0.022, 0.040), (0.021, 0.044), (0.019, 0.043)]
    tube(palm_pts, palm_r, 16, MAT_WOOD, hand_w, ref=Vector((0, 0, 1)))
    # fingers curl around the grip axis (along Y) under the palm
    gx = WRIST_X + 0.066
    fy = [-0.031, -0.0105, 0.0105, 0.030]
    fr = [0.0108, 0.0114, 0.0108, 0.0094]
    if grip:
        rc = 0.031
        ends = [-158, -164, -150, -128]
    else:
        rc = 0.034
        ends = [-62, -70, -66, -52]
    for k in range(4):
        a0 = 72.0
        n = 9
        path = []
        for i in range(n + 1):
            a = math.radians(a0 + (ends[k] - a0) * i / n)
            path.append(((gx + rc * math.cos(a)) * s if False else (gx * s + rc * math.cos(a) * s),
                         fy[k] * (1 - 0.15 * i / n), z0 + rc * math.sin(a) + (0.004 if not grip else 0.0)))
        tube(path, [fr[k] * (1 - 0.18 * i / n) for i in range(n + 1)], 10, MAT_WOOD, hand_w,
             ref=Vector((0, 1, 0)))
    # thumb
    if grip:
        th = [((WRIST_X + 0.010) * s, -0.030, z0 + 0.010), ((WRIST_X + 0.036) * s, -0.052, z0 - 0.002),
              ((WRIST_X + 0.058) * s, -0.056, z0 - 0.024), ((WRIST_X + 0.074) * s, -0.046, z0 - 0.036)]
    else:
        th = [((WRIST_X + 0.010) * s, -0.030, z0 + 0.008), ((WRIST_X + 0.040) * s, -0.054, z0 + 0.000),
              ((WRIST_X + 0.066) * s, -0.060, z0 - 0.010), ((WRIST_X + 0.084) * s, -0.054, z0 - 0.018)]
    tube(th, [0.0145, 0.0132, 0.0118, 0.0105], 10, MAT_WOOD, hand_w, ref=Vector((0, 0, 1)))


# ----------------------------------------------------------------------------
# 4. LEGS + FEET
# ----------------------------------------------------------------------------
def build_leg(side):
    s = side
    L = "Left" if s > 0 else "Right"
    chain = [
        ("Hips", (HIP_X * s, 0, 1.0), (HIP_X * s, 0, HIP_Z)),
        (L + "UpperLeg", (HIP_X * s, 0, HIP_Z), (HIP_X * s, 0, KNEE_Z)),
        (L + "LowerLeg", (HIP_X * s, 0, KNEE_Z), (HIP_X * s, 0, ANKLE_Z)),
    ]
    wfn = chain_weights(chain, blend=0.05)
    zs = [0.97, 0.90, HIP_Z, 0.80, 0.72, 0.64, 0.56, 0.50, KNEE_Z, 0.43, 0.38, 0.32, 0.25, 0.18, 0.13, 0.10]
    rad = [0.052, 0.052, 0.052, 0.050, 0.047, 0.044, 0.041, 0.040, 0.041, 0.039, 0.041, 0.041, 0.036, 0.031, 0.029,
           0.029]
    pts = []
    for z, r in zip(zs, rad):
        y = -0.006 * math.exp(-((z - KNEE_Z) / 0.03) ** 2)          # kneecap forward
        y += 0.007 * math.exp(-((z - 0.34) / 0.06) ** 2)            # calf behind
        pts.append((HIP_X * s, y, z))
    tube(pts, [r * 1.24 for r in rad], 16, MAT_WOOD, wfn, ref=Vector((0, -1, 0)), cap_start=True, cap_end=True)

    foot_b, toe_b = L + "Foot", L + "Toes"

    def foot_w(co):
        w = mix({L + "LowerLeg": 1.0}, {foot_b: 1.0}, smoothstep(0.14, 0.075, co.z))
        return mix(w, {toe_b: 1.0}, smoothstep(-0.16, -0.205, co.y))

    fx = HIP_X * s
    FS = 1.34  # foot scale (big bare feet)

    def F(p):
        return (fx + (p[0] - fx) * FS, p[1] * FS, p[2] * (1 + (FS - 1) * 0.5))
    tube([F((fx, 0.042, 0.036)), F((fx, 0.012, 0.033))], [0.034 * FS, 0.035 * FS], 12, MAT_WOOD, foot_w)  # heel
    fp = [(fx, 0.004, 0.115), F((fx, 0.014, 0.068)), F((fx, 0.006, 0.042)), F((fx * 1.03, -0.045, 0.035)),
          F((fx * 1.06, -0.105, 0.030)), F((fx * 1.08, -0.150, 0.027))]
    fr = [(0.033, 0.033), (0.040 * FS, 0.042 * FS), (0.047 * FS, 0.037 * FS), (0.053 * FS, 0.033 * FS),
          (0.060 * FS, 0.029 * FS), (0.059 * FS, 0.024 * FS)]
    tube(fp, fr, 16, MAT_WOOD, foot_w, ref=Vector((0, 0, 1)))
    toe_x = [-0.036, -0.017, 0.001, 0.018, 0.034]
    toe_r = [0.0150, 0.0122, 0.0115, 0.0105, 0.0095]
    toe_len = [0.046, 0.040, 0.036, 0.031, 0.025]
    for k in range(5):
        x0 = fx * 1.08 - toe_x[k] * s
        y0 = -0.155 + k * 0.006
        p0 = F((x0, y0, 0.022))
        p1 = F((x0 + 0.004 * s * (k - 2), y0 - toe_len[k] * 0.6, 0.020))
        p2 = F((x0 + 0.006 * s * (k - 2), y0 - toe_len[k], 0.014))
        tr = toe_r[k] * FS
        tube([p0, p1, p2], [tr, tr * 0.95, tr * 0.85], 10, MAT_WOOD, lambda co: {toe_b: 1.0})


# ----------------------------------------------------------------------------
# UV packing for limb parts
# ----------------------------------------------------------------------------
def pack_parts(builder, top, gap=0.006):
    order = sorted(range(len(builder.parts)), key=lambda i: -builder.parts[i]["C"])
    k = 0.62
    while True:
        rects, x, y, shelf_h, ok = {}, gap, gap, 0.0, True
        for i in order:
            w, h = builder.parts[i]["L"] * k, builder.parts[i]["C"] * k
            if x + w > 1.0 - gap:
                x, y = gap, y + shelf_h + gap
                shelf_h = 0.0
            if y + h > top:
                ok = False
                break
            rects[i] = (x, y, w, h)
            x += w + gap
            shelf_h = max(shelf_h, h)
        if ok:
            break
        k *= 0.96
    for f in builder.faces:
        if f[3] is None:
            continue
        ox, oy, w, h = rects[f[3]]
        f[2] = [(ox + v * w, oy + u * h) for (u, v) in f[2]]
    print("UV pack density", round(k, 3))


# ----------------------------------------------------------------------------
# Build body
# ----------------------------------------------------------------------------
def to_mesh(builder, name, weights=True):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    col_layer = bm.verts.layers.float_color.new("Tint")
    reg_layer = bm.faces.layers.int.new("ts_region")  # 0 log, 1 eye, 2 limb (used to split the AO bake)
    bverts = [bm.verts.new(v) for v in builder.verts]
    for bv, c in zip(bverts, builder.colors):
        bv[col_layer] = (c[0], c[1], c[2], 1.0)
    bm.verts.ensure_lookup_table()
    for (idx, mat, uvs, _p) in builder.faces:
        try:
            f = bm.faces.new([bverts[i] for i in idx])
        except ValueError:
            continue
        f.material_index = mat
        f.smooth = True
        f[reg_layer] = 2 if _p is not None else (1 if mat == MAT_EYE else 0)
        for loop, uv in zip(f.loops, uvs):
            loop[uv_layer].uv = uv
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00005)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    return mesh


build_log()
for sd in (-1, 1):
    build_eye(sd)
    build_arm(sd)
    build_leg(sd)
pack_parts(B, LIMB_UV_TOP)

body_mesh = to_mesh(B, "TungSahur_Mesh")
body = bpy.data.objects.new("TungSahur", body_mesh)
scene.collection.objects.link(body)
# vertex groups (weights survive remove_doubles via a position lookup)
groups = {name: body.vertex_groups.new(name=name) for name in BONES.keys()}
from mathutils.kdtree import KDTree
kd = KDTree(len(B.verts))
for i, v in enumerate(B.verts):
    kd.insert(v, i)
kd.balance()
for v in body_mesh.vertices:
    _, src, _ = kd.find(v.co)
    w = B.weights[src]
    tot = sum(w.values())
    for bname, val in w.items():
        if val > 1e-4:
            groups[bname].add([v.index], val / tot, 'REPLACE')


# ----------------------------------------------------------------------------
# Materials (Blender preview + bake sources; Unity remaps by name)
# ----------------------------------------------------------------------------
def node(nt, type_name, loc, **inputs):
    n = nt.nodes.new(type_name)
    n.location = loc
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n


def sk(n, name, out=False):
    """Socket by name, preferring the colour variant (Mix nodes have A/B/Result per data type)."""
    socks = n.outputs if out else n.inputs
    cands = [x for x in socks if x.name == name]
    for x in cands:
        if x.type == 'RGBA':
            return x
    return cands[0]


def wood_material(name, tint=True):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (900, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location = (600, 0)
    tc = nt.nodes.new("ShaderNodeTexCoord"); tc.location = (-1200, 0)
    mp = nt.nodes.new("ShaderNodeMapping"); mp.location = (-1000, 0)
    mp.inputs["Scale"].default_value = (5.0, 5.0, 0.55)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    # long soft streaks (grain along the log)
    n1 = nt.nodes.new("ShaderNodeTexNoise"); n1.location = (-800, 150)
    n1.inputs["Scale"].default_value = 2.2
    n1.inputs["Detail"].default_value = 6.0
    n1.inputs["Roughness"].default_value = 0.55
    nt.links.new(mp.outputs["Vector"], n1.inputs["Vector"])
    # fine fibres
    mp2 = nt.nodes.new("ShaderNodeMapping"); mp2.location = (-1000, -250)
    mp2.inputs["Scale"].default_value = (60.0, 60.0, 2.0)
    nt.links.new(tc.outputs["Object"], mp2.inputs["Vector"])
    n2 = nt.nodes.new("ShaderNodeTexNoise"); n2.location = (-800, -250)
    n2.inputs["Scale"].default_value = 1.0
    n2.inputs["Detail"].default_value = 3.0
    nt.links.new(mp2.outputs["Vector"], n2.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB"); ramp.location = (-560, 150)
    ramp.color_ramp.elements[0].position = 0.22
    ramp.color_ramp.elements[0].color = (0.46, 0.15, 0.04, 1)
    ramp.color_ramp.elements[1].position = 0.80
    ramp.color_ramp.elements[1].color = (0.74, 0.34, 0.11, 1)
    nt.links.new(n1.outputs["Fac"], ramp.inputs["Fac"])
    fib = nt.nodes.new("ShaderNodeMix"); fib.location = (-280, 100)
    fib.data_type = 'RGBA'; fib.blend_type = 'MULTIPLY'
    fib.inputs["Factor"].default_value = 0.22
    nt.links.new(ramp.outputs["Color"], sk(fib, "A"))
    nt.links.new(n2.outputs["Fac"], sk(fib, "B"))
    col = sk(fib, "Result", True)
    if tint:
        attr = nt.nodes.new("ShaderNodeAttribute"); attr.location = (-560, -450)
        attr.attribute_name = "Tint"
        sep = nt.nodes.new("ShaderNodeSeparateColor"); sep.location = (-380, -450)
        nt.links.new(attr.outputs["Color"], sep.inputs["Color"])
        redm = nt.nodes.new("ShaderNodeMix"); redm.location = (-100, 0)
        redm.data_type = 'RGBA'
        sk(redm, "B").default_value = (0.55, 0.13, 0.06, 1)
        mr = nt.nodes.new("ShaderNodeMath"); mr.location = (-250, -300); mr.operation = 'MULTIPLY'
        mr.inputs[1].default_value = 0.45
        nt.links.new(sep.outputs["Green"], mr.inputs[0])
        nt.links.new(mr.outputs[0], redm.inputs["Factor"])
        nt.links.new(col, sk(redm, "A"))
        darkm = nt.nodes.new("ShaderNodeMix"); darkm.location = (100, 0)
        darkm.data_type = 'RGBA'
        sk(darkm, "B").default_value = (0.09, 0.03, 0.012, 1)
        md = nt.nodes.new("ShaderNodeMath"); md.location = (-250, -450); md.operation = 'MULTIPLY'
        md.inputs[1].default_value = 0.85
        nt.links.new(sep.outputs["Red"], md.inputs[0])
        nt.links.new(md.outputs[0], darkm.inputs["Factor"])
        nt.links.new(sk(redm, "Result", True), sk(darkm, "A"))
        col = sk(darkm, "Result", True)
    nt.links.new(col, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.42
    try:
        bsdf.inputs["Subsurface Weight"].default_value = 0.08
        bsdf.inputs["Subsurface Radius"].default_value = (0.02, 0.008, 0.004)
        bsdf.inputs["Subsurface Scale"].default_value = 0.05
    except KeyError:
        pass
    # subtle grain bump
    bump = nt.nodes.new("ShaderNodeBump"); bump.location = (300, -300)
    bump.inputs["Strength"].default_value = 0.07
    bump.inputs["Distance"].default_value = 0.002
    nt.links.new(n2.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    m.diffuse_color = (0.8, 0.45, 0.2, 1)
    return m


EYE_TEX = os.path.join(UNITY_TEX, "T_TungEye_Sculpt.png")


def make_eye_texture(path, size=1024):
    """Sclera / dark-brown iris / black pupil. UV centre = front pole, radius 0.5 = equator,
    rho = 2*|uv-0.5| = sin(angle from gaze)."""
    import numpy as np
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    u = (x + 0.5) / size - 0.5
    v = (y + 0.5) / size - 0.5
    rho = np.sqrt(u * u + v * v) * 2.0
    ang = np.arctan2(v, u)

    def lin(hexs):
        c = np.array([int(hexs[i:i + 2], 16) / 255.0 for i in (1, 3, 5)], dtype=np.float32)
        return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

    def ss(e0, e1, t):
        t = np.clip((t - e0) / (e1 - e0), 0, 1)
        return t * t * (3 - 2 * t)

    sclera, sclera_edge = lin("#F1EBE1"), lin("#B99A88")
    iris_in, iris_out, limbal, pupil = lin("#6B3B1C"), lin("#2A1409"), lin("#0E0704"), lin("#030202")
    col = sclera[None, None, :] * (1 - ss(0.55, 0.98, rho))[..., None] + sclera_edge[None, None, :] * ss(0.55, 0.98, rho)[..., None]
    vein = np.abs(np.sin(ang * 11.0 + rho * 7.0)) ** 30 * ss(0.62, 0.92, rho)
    col = col * (1 - 0.25 * vein[..., None]) + lin("#B5524A")[None, None, :] * 0.25 * vein[..., None]
    RI, RP = 0.47, 0.205
    fib = 0.5 + 0.5 * np.sin(ang * 70.0 + np.sin(ang * 13.0) * 2.0 + rho * 9.0)
    ic = iris_in[None, None, :] * (1 - ss(0.25, RI, rho))[..., None] + iris_out[None, None, :] * ss(0.25, RI, rho)[..., None]
    ic = ic * (0.8 + 0.45 * fib[..., None])
    ic = ic * (1 - ss(RI - 0.07, RI, rho))[..., None] + limbal[None, None, :] * ss(RI - 0.07, RI, rho)[..., None]
    m = ss(RI + 0.012, RI - 0.008, rho)[..., None]
    col = col * (1 - m) + ic * m
    pm = ss(RP + 0.012, RP - 0.006, rho)[..., None]
    col = col * (1 - pm) + pupil[None, None, :] * pm
    # painted catch-light (upper left) so the eyes read alive in any light
    hx, hy = u + 0.085, v - 0.095
    hl = ss(0.055, 0.035, np.sqrt(hx * hx + hy * hy))[..., None]
    col = col * (1 - 0.9 * hl) + 0.9 * hl
    img = np.ones((size, size, 4), dtype=np.float32)
    img[..., :3] = col
    im = bpy.data.images.get("T_TungEye_Sculpt") or bpy.data.images.new("T_TungEye_Sculpt", size, size)
    im.scale(size, size)
    im.pixels.foreach_set(img.ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    return im


def eye_material():
    m = bpy.data.materials.get("M_TungEye") or bpy.data.materials.new("M_TungEye")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (600, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location = (300, 0)
    bsdf.inputs["Roughness"].default_value = 0.12
    try:
        bsdf.inputs["Coat Weight"].default_value = 0.6
    except KeyError:
        pass
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.location = (0, 0)
    tex.image = make_eye_texture(EYE_TEX)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    m.diffuse_color = (0.95, 0.94, 0.9, 1)
    return m


body.data.materials.append(wood_material("M_TungWood"))
body.data.materials.append(eye_material())

# ----------------------------------------------------------------------------
# Armature
# ----------------------------------------------------------------------------
arm_data = bpy.data.armatures.new("TungSahur_Armature")
arm_obj = bpy.data.objects.new("Armature", arm_data)
scene.collection.objects.link(arm_obj)
for o in scene.objects:
    o.select_set(False)
bpy.context.view_layer.objects.active = arm_obj
arm_obj.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
ebones = {}
for name, (h, t, parent) in BONES.items():
    eb = arm_data.edit_bones.new(name)
    eb.head = Vector(h)
    eb.tail = Vector(t)
    eb.roll = 0.0
    ebones[name] = eb
for name, (h, t, parent) in BONES.items():
    if parent:
        ebones[name].parent = ebones[parent]
        ebones[name].use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
body.parent = arm_obj
mod = body.modifiers.new("Armature", 'ARMATURE')
mod.object = arm_obj

# ----------------------------------------------------------------------------
# BAT (separate static mesh). Origin = grip point, extends along +Z.
# ----------------------------------------------------------------------------
CB = Builder()
club_profile = [  # (z, r)
    (-0.112, 0.000), (-0.110, 0.022), (-0.104, 0.032), (-0.094, 0.034), (-0.084, 0.029), (-0.074, 0.020),
    (-0.06, 0.0178), (0.0, 0.0182), (0.08, 0.0188), (0.16, 0.0196), (0.24, 0.0215), (0.32, 0.026),
    (0.40, 0.032), (0.48, 0.039), (0.56, 0.046), (0.64, 0.051), (0.72, 0.055), (0.78, 0.0565),
    (0.83, 0.0565), (0.865, 0.053), (0.888, 0.044), (0.900, 0.030), (0.905, 0.012), (0.906, 0.0),
]
csegs = 28
vs = [0.0]
for i in range(1, len(club_profile)):
    vs.append(vs[-1] + math.hypot(club_profile[i][0] - club_profile[i - 1][0], club_profile[i][1] - club_profile[i - 1][1]))
crings = []
for (z, r) in club_profile:
    ring = []
    for j in range(csegs + 1):
        a = 2 * math.pi * j / csegs
        rr = r * (1 + 0.010 * math.sin(5 * a + z * 31) * (1 if z > 0.3 else 0))
        ring.append(CB.add_vert((math.cos(a) * rr, math.sin(a) * rr, z), {}))
    crings.append(ring)
for i in range(len(crings) - 1):
    for j in range(csegs):
        u0, u1 = j / csegs, (j + 1) / csegs
        v0, v1 = vs[i] / vs[-1], vs[i + 1] / vs[-1]
        CB.add_face([crings[i][j], crings[i][j + 1], crings[i + 1][j + 1], crings[i + 1][j]], 0,
                    [(v0, u0), (v0, u1), (v1, u1), (v1, u0)])
cmesh = to_mesh(CB, "Club_Mesh")
club = bpy.data.objects.new("TungSahur_Club", cmesh)
scene.collection.objects.link(club)
club.data.materials.append(wood_material("M_ClubWood", tint=False))
sub = club.modifiers.new("Subsurf", 'SUBSURF')
sub.levels = 1
sub.render_levels = 1


def apply_subsurf(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    m = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = obj.data
    obj.modifiers.clear()
    obj.data = m
    bpy.data.meshes.remove(old)


apply_subsurf(club)

# preview pose for the bat: in the right fist, hanging down past the hip
club.parent = arm_obj
club.location = Vector((-(WRIST_X + 0.066), 0.0, SHOULDER_Z))
club.rotation_euler = (math.radians(90), 0, 0)


# ----------------------------------------------------------------------------
# Texture bake (albedo x AO, tangent normal) - optional
# ----------------------------------------------------------------------------
def split_regions(obj, groups):
    """Temporary copies of `obj` keeping only the faces whose ts_region is in each group."""
    out = []
    for keep in groups:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        lay = bm.faces.layers.int.get("ts_region")
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[lay] not in keep], context='FACES')
        me = bpy.data.meshes.new(obj.name + "_aopart")
        bm.to_mesh(me)
        bm.free()
        for m in obj.data.materials:
            me.materials.append(m)
        ob = bpy.data.objects.new(me.name, me)
        scene.collection.objects.link(ob)
        ob.matrix_world = obj.matrix_world.copy()
        out.append(ob)
    return out


def bake_textures(tex_dir, size):
    import numpy as np
    os.makedirs(tex_dir, exist_ok=True)
    scene.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for dt in ("OPTIX", "CUDA", "HIP", "ONEAPI", "METAL"):
            try:
                prefs.compute_device_type = dt
                prefs.get_devices()
                if any(d.type == dt for d in prefs.devices):
                    for d in prefs.devices:
                        d.use = True
                    scene.cycles.device = 'GPU'
                    break
            except TypeError:
                continue
    except Exception as e:
        print("GPU setup failed", e)
    scene.cycles.samples = 64
    if scene.world is None:
        scene.world = bpy.data.worlds.new("BakeWorld")
    scene.world.light_settings.distance = 0.14   # local AO: creases & sockets, not whole limbs
    bk = scene.render.bake
    bk.margin = 12
    bk.use_clear = True

    def target(obj, img_name, sz, non_color=False):
        img = bpy.data.images.get(img_name)
        if img:
            bpy.data.images.remove(img)
        img = bpy.data.images.new(img_name, sz, sz, alpha=False, float_buffer=non_color)
        if non_color:
            img.colorspace_settings.name = 'Non-Color'
        dummy = bpy.data.images.get("_bake_dummy") or bpy.data.images.new("_bake_dummy", 16, 16)
        for slot in obj.material_slots:
            nt = slot.material.node_tree
            n = nt.nodes.get("_bake") or nt.nodes.new("ShaderNodeTexImage")
            n.name = "_bake"
            n.image = img if slot.material.name != "M_TungEye" else dummy
            nt.nodes.active = n
        return img

    def run(obj, kind, **kw):
        for o in scene.objects:
            o.select_set(False)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.bake(type=kind, **kw)

    def px(img):
        a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
        img.pixels.foreach_get(a)
        return a.reshape(img.size[1], img.size[0], 4)

    def save(arr, name, non_color=False):
        h, w = arr.shape[:2]
        img = bpy.data.images.new(name, w, h, alpha=False, float_buffer=False)
        if non_color:
            img.colorspace_settings.name = 'Non-Color'
        img.pixels.foreach_set(arr.astype(np.float32).ravel())
        img.filepath_raw = os.path.join(tex_dir, name + ".png")
        img.file_format = 'PNG'
        img.save()
        print("SAVED", img.filepath_raw)

    for obj, prefix, sz, ao_mix in ((body, "T_TungBody", size, 0.75), (club, "T_TungClub", size // 4, 0.35)):
        obj.hide_render = False
        a_img = target(obj, prefix + "_bakeA", sz)
        run(obj, 'DIFFUSE', pass_filter={'COLOR'})
        albedo = px(a_img).copy()
        scene.cycles.samples = 128
        o_img = target(obj, prefix + "_bakeAO", sz, non_color=True)
        # AO: each region only occludes itself (log+eyes vs limbs), so limbs posed away from
        # the T-pose don't leave dark prints on the log.
        parts = split_regions(obj, [(0, 1), (2,)]) if obj is body else [obj]
        hidden = {o: o.hide_render for o in scene.objects}
        for i, ro in enumerate(parts):
            for o in scene.objects:
                o.hide_render = o is not ro
            bk.use_clear = i == 0
            run(ro, 'AO')
        bk.use_clear = True
        for o, hflag in hidden.items():
            o.hide_render = hflag
        for ro in parts:
            if ro is not obj:
                me = ro.data
                bpy.data.objects.remove(ro, do_unlink=True)
                bpy.data.meshes.remove(me)
        ao = px(o_img)[..., :1].copy()
        scene.cycles.samples = 16
        n_img = target(obj, prefix + "_bakeN", sz, non_color=True)
        run(obj, 'NORMAL', normal_space='TANGENT')
        nrm = px(n_img).copy()
        ao_c = np.clip(ao, 0, 1) ** 1.25
        out = albedo.copy()
        out[..., :3] = albedo[..., :3] * ((1 - ao_mix) + ao_mix * ao_c)
        out[..., 3] = 1.0
        save(out, prefix + "_Albedo")
        nrm[..., 3] = 1.0
        save(nrm, prefix + "_Normal", non_color=True)
        for slot in obj.material_slots:
            nt = slot.material.node_tree
            n = nt.nodes.get("_bake")
            if n:
                nt.nodes.remove(n)


if BAKE_DIR:
    club_parent = club.parent
    bake_textures(BAKE_DIR, BAKE_SIZE)

# ----------------------------------------------------------------------------
# Export
# ----------------------------------------------------------------------------
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
        use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=True,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=False, armature_nodetype='NULL', bake_anim=False,
        colors_type='NONE',
    )
    print("EXPORTED", path)


if DO_EXPORT:
    # the bat is exported in its own space (origin = grip)
    mw = club.matrix_world.copy()
    club.parent = None
    club.matrix_world = mw
    club.location = (0, 0, 0)
    club.rotation_euler = (0, 0, 0)
    export([arm_obj, body], os.path.join(OUT_DIR, "TungSahur.fbx"))
    export([club], os.path.join(OUT_DIR, "TungSahur_Club.fbx"))
    club.parent = arm_obj
    club.location = Vector((-(WRIST_X + 0.066), 0.0, SHOULDER_Z))
    club.rotation_euler = (math.radians(90), 0, 0)
    if bpy.app.background:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "TungSahur_Source.blend"))

print("BUILD OK verts", len(body.data.vertices), "tris", sum(len(p.vertices) - 2 for p in body.data.polygons))
