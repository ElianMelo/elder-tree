"""
Tung Tung Tung Sahur - procedural character builder for Blender (4.2+ / 5.x).

Builds the character mesh (log body, carved face, bulging eyes, thin limbs,
bare feet, fists), a Unity-Humanoid-compatible armature in T-pose with
computed skin weights, and a separate wooden club. Exports both as FBX.

Run headless:
    blender -b --factory-startup --python build_tung_sahur.py -- <out_dir> [preview.png]

Or paste into Blender's Text Editor / send through Blender MCP `execute_code`
(set OUT_DIR below when doing so).

Conventions: Z up, character faces -Y, character's LEFT side is +X.
"""
import bpy
import bmesh
import math
import sys
import os
from mathutils import Vector, Matrix

# ----------------------------------------------------------------------------
# Args
# ----------------------------------------------------------------------------
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = argv[0] if len(argv) > 0 else os.path.join(os.path.dirname(bpy.data.filepath or "."), "export")
PREVIEW = argv[1] if len(argv) > 1 else None
os.makedirs(OUT_DIR, exist_ok=True)

# ----------------------------------------------------------------------------
# Proportions (metres)
# ----------------------------------------------------------------------------
LOG_BOTTOM = 0.70
LOG_TOP = 1.94
LOG_R_BOTTOM = 0.226
LOG_R_TOP = 0.212
LOG_BEVEL = 0.075          # rounding radius at top
LOG_BEVEL_BOTTOM = 0.09

SHOULDER_X = 0.265
SHOULDER_Z = 1.30
ELBOW_X = 0.53
WRIST_X = 0.78
HAND_END_X = 0.885

HIP_X = 0.105
HIP_Z = 0.80
KNEE_Z = 0.44
ANKLE_Z = 0.085

BONES = {
    # name: (head, tail, parent)
    "Hips":          ((0, 0.0, 0.84), (0, 0.0, 1.00), None),
    "Spine":         ((0, 0.0, 1.00), (0, 0.0, 1.18), "Hips"),
    "Chest":         ((0, 0.0, 1.18), (0, 0.0, 1.34), "Spine"),
    "UpperChest":    ((0, 0.0, 1.34), (0, 0.0, 1.46), "Chest"),
    "Neck":          ((0, 0.0, 1.46), (0, 0.0, 1.56), "UpperChest"),
    "Head":          ((0, 0.0, 1.56), (0, 0.0, 1.94), "Neck"),
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
    "LeftFoot":      ((HIP_X, 0.0, ANKLE_Z), (HIP_X, -0.115, 0.025), "LeftLowerLeg"),
    "LeftToes":      ((HIP_X, -0.115, 0.025), (HIP_X, -0.185, 0.025), "LeftFoot"),
    "RightUpperLeg": ((-HIP_X, 0.0, HIP_Z), (-HIP_X, 0.0, KNEE_Z), "Hips"),
    "RightLowerLeg": ((-HIP_X, 0.0, KNEE_Z), (-HIP_X, 0.0, ANKLE_Z), "RightUpperLeg"),
    "RightFoot":     ((-HIP_X, 0.0, ANKLE_Z), (-HIP_X, -0.115, 0.025), "RightLowerLeg"),
    "RightToes":     ((-HIP_X, -0.115, 0.025), (-HIP_X, -0.185, 0.025), "RightFoot"),
}

MAT_WOOD, MAT_EYE, MAT_DARK, MAT_MOUTH = 0, 1, 2, 3

# ----------------------------------------------------------------------------
# Scene reset
# ----------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def log_radius(z):
    t = (z - LOG_BOTTOM) / (LOG_TOP - LOG_BOTTOM)
    t = max(0.0, min(1.0, t))
    # subtle barrel shape + taper
    return LOG_R_BOTTOM + (LOG_R_TOP - LOG_R_BOTTOM) * t + 0.006 * math.sin(t * math.pi)


def surface_y(x, z, offset=0.0):
    """Front surface (-Y) of the log at (x, z)."""
    r = log_radius(z) + offset
    return -math.sqrt(max(r * r - x * x, 1e-6))


# ----------------------------------------------------------------------------
# Mesh builder: accumulates verts / faces / uvs / material / weights
# ----------------------------------------------------------------------------
class Builder:
    def __init__(self):
        self.verts = []
        self.faces = []        # (indices, mat, uvs)
        self.weights = []      # per vertex dict bone->w

    def add_vert(self, co, w):
        self.verts.append(Vector(co))
        self.weights.append(w)
        return len(self.verts) - 1

    def add_face(self, idx, mat, uvs):
        self.faces.append((idx, mat, uvs))


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


def tube(points, radii, segs, mat, weight_fn, ref=Vector((0, 0, 1)), cap_start=True, cap_end=True,
         v_scale=1.0, seam_angle=0.0):
    """Generate a smooth tube along `points`. radii: float or (rx, ry) per point.
    Caps are hemispherical. weight_fn(co)->dict."""
    pts = [Vector(p) for p in points]
    rad = [(r, r) if isinstance(r, (int, float)) else r for r in radii]

    def tangent_at(i):
        if i == 0:
            return pts[1] - pts[0]
        if i == len(pts) - 1:
            return pts[-1] - pts[-2]
        return (pts[i + 1] - pts[i - 1])

    # Expand with cap rings
    ring_centers, ring_rad, ring_tan = [], [], []
    cap_steps = 4
    t0 = tangent_at(0).normalized()
    if cap_start:
        for k in range(cap_steps, 0, -1):
            a = (k / cap_steps) * (math.pi / 2) * 0.92
            ring_centers.append(pts[0] - t0 * math.sin(a) * min(rad[0]))
            ring_rad.append((rad[0][0] * math.cos(a), rad[0][1] * math.cos(a)))
            ring_tan.append(t0)
    for i, p in enumerate(pts):
        ring_centers.append(p)
        ring_rad.append(rad[i])
        ring_tan.append(tangent_at(i))
    t1 = tangent_at(len(pts) - 1).normalized()
    if cap_end:
        for k in range(1, cap_steps + 1):
            a = (k / cap_steps) * (math.pi / 2) * 0.92
            ring_centers.append(pts[-1] + t1 * math.sin(a) * min(rad[-1]))
            ring_rad.append((rad[-1][0] * math.cos(a), rad[-1][1] * math.cos(a)))
            ring_tan.append(t1)

    rings = []
    length_acc = [0.0]
    for i in range(1, len(ring_centers)):
        length_acc.append(length_acc[-1] + (ring_centers[i] - ring_centers[i - 1]).length)
    total = max(length_acc[-1], 1e-6)

    prev_n = None
    for i, c in enumerate(ring_centers):
        t, n, b = ring_frame(ring_tan[i], ref if prev_n is None else prev_n)
        prev_n = n
        rx, ry = ring_rad[i]
        ring = []
        for j in range(segs):
            ang = seam_angle + 2 * math.pi * j / segs
            co = c + n * math.cos(ang) * rx + b * math.sin(ang) * ry
            ring.append(B.add_vert(co, weight_fn(co)))
        rings.append(ring)

    for i in range(len(rings) - 1):
        v0 = length_acc[i] / total * v_scale
        v1 = length_acc[i + 1] / total * v_scale
        for j in range(segs):
            j2 = (j + 1) % segs
            u0 = j / segs
            u1 = (j + 1) / segs
            B.add_face([rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]], mat,
                       [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
    # pole caps
    if cap_start:
        c = ring_centers[0] - t0 * min(rad[0]) * 0.08
        pole = B.add_vert(c, weight_fn(c))
        for j in range(segs):
            j2 = (j + 1) % segs
            B.add_face([pole, rings[0][j2], rings[0][j]], mat, [(0.5, 0), ((j + 1) / segs, 0), (j / segs, 0)])
    if cap_end:
        c = ring_centers[-1] + t1 * min(rad[-1]) * 0.08
        pole = B.add_vert(c, weight_fn(c))
        for j in range(segs):
            j2 = (j + 1) % segs
            B.add_face([rings[-1][j], rings[-1][j2], pole], mat,
                       [(j / segs, v_scale), ((j + 1) / segs, v_scale), (0.5, v_scale)])


# ----------------------------------------------------------------------------
# Weight functions
# ----------------------------------------------------------------------------
TORSO_BONES = [("Hips", 0.84), ("Spine", 1.06), ("Chest", 1.26), ("UpperChest", 1.40), ("Neck", 1.52), ("Head", 1.66)]


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
    """chain: list of (bone, start_point, end_point). Weight by projection along chain."""
    segs = [(b, Vector(a), Vector(c)) for b, a, c in chain]

    def fn(co):
        # find param along each segment
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
        # blend with neighbours near joints
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


def fixed(bone):
    return lambda co: {bone: 1.0}


def mix(a, b, t):
    out = {}
    for k, v in a.items():
        out[k] = out.get(k, 0) + v * (1 - t)
    for k, v in b.items():
        out[k] = out.get(k, 0) + v * t
    return out


# ----------------------------------------------------------------------------
# 1. LOG BODY (lathe with rounded ends)
# ----------------------------------------------------------------------------
def build_log():
    segs = 48
    profile = []  # (z, r)
    # bottom rounded cap
    steps = 6
    for k in range(steps, 0, -1):
        a = k / steps * (math.pi / 2)
        z = LOG_BOTTOM + LOG_BEVEL_BOTTOM * (1 - math.cos(a))
        r = log_radius(z) - LOG_BEVEL_BOTTOM * (1 - math.cos(math.pi / 2 - a)) * 0 - LOG_BEVEL_BOTTOM * (1 - math.sin(a))
        profile.append((z - LOG_BEVEL_BOTTOM * 0.0, max(r, 0.02)))
    # straight body
    body_rings = 26
    z0 = LOG_BOTTOM + LOG_BEVEL_BOTTOM
    z1 = LOG_TOP - LOG_BEVEL
    for i in range(body_rings + 1):
        z = z0 + (z1 - z0) * i / body_rings
        profile.append((z, log_radius(z)))
    for k in range(1, steps + 1):
        a = k / steps * (math.pi / 2)
        z = z1 + LOG_BEVEL * math.sin(a)
        r = log_radius(z) - LOG_BEVEL * (1 - math.cos(a))
        profile.append((z, max(r, 0.03)))
    # inner top disc rings for end-grain
    rtop = profile[-1][1]
    for k in range(1, 4):
        profile.append((LOG_TOP + 0.002 * k, rtop * (1 - k / 4)))

    # bottom inner disc
    rbot = profile[0][1]
    bottom_inner = [(profile[0][0] - 0.002 * k, rbot * (1 - k / 3)) for k in range(3, 0, -1)]
    profile = bottom_inner + profile

    # arc-length v
    vs = [0.0]
    for i in range(1, len(profile)):
        dz = profile[i][0] - profile[i - 1][0]
        dr = profile[i][1] - profile[i - 1][1]
        vs.append(vs[-1] + math.hypot(dz, dr))
    total = vs[-1]

    rings = []
    for (z, r) in profile:
        ring = []
        for j in range(segs):
            # seam at back (+Y). angle measured so j=0 is +Y
            ang = math.pi / 2 + 2 * math.pi * j / segs
            x = math.cos(ang) * r
            y = math.sin(ang) * r
            # gentle organic irregularity (deterministic)
            n = 0.0025 * math.sin(3 * ang + z * 9.0) + 0.0018 * math.sin(7 * ang - z * 13.0)
            if r > 0.1:
                x *= 1 + n / r
                y *= 1 + n / r
            # slightly flatter front for the face
            if y < 0:
                y *= 0.965
            co = Vector((x, y, z))
            ring.append(B.add_vert(co, torso_weights(co)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        va = vs[i] / total * 3.0
        vb = vs[i + 1] / total * 3.0
        for j in range(segs):
            j2 = (j + 1) % segs
            ua, ub = j / segs, (j + 1) / segs
            B.add_face([rings[i][j], rings[i + 1][j], rings[i + 1][j2], rings[i][j2]], MAT_WOOD,
                       [(ua, va), (ua, vb), (ub, vb), (ub, va)])
    # caps
    for ring, z, top in ((rings[0], profile[0][0], False), (rings[-1], profile[-1][0], True)):
        c = Vector((0, 0, z + (0.001 if top else -0.001)))
        pole = B.add_vert(c, torso_weights(c))
        v = 3.0 if top else 0.0
        for j in range(segs):
            j2 = (j + 1) % segs
            if top:
                B.add_face([ring[j], ring[j2], pole], MAT_WOOD, [(j / segs, v), ((j + 1) / segs, v), (0.5, v)])
            else:
                B.add_face([ring[j2], ring[j], pole], MAT_WOOD, [((j + 1) / segs, v), (j / segs, v), (0.5, v)])


# ----------------------------------------------------------------------------
# 2. FACE
# ----------------------------------------------------------------------------
EYE_Z = 1.60
EYE_X = 0.088
EYE_R = 0.074


def build_eye(side):
    x = EYE_X * side
    cy = surface_y(x, EYE_Z) + 0.022
    center = Vector((x, cy, EYE_Z))
    segs, rings_n = 32, 16
    # gaze: slightly outward & down like the reference
    gaze = Vector((0.10 * side, -1.0, -0.05)).normalized()
    up = Vector((0, 0, 1))
    right = gaze.cross(up).normalized()
    up2 = right.cross(gaze).normalized()
    wfn = lambda co: torso_weights(Vector((co.x, co.y, EYE_Z)))
    verts_rings = []
    for i in range(1, rings_n):
        th = math.pi * i / rings_n  # 0 = front pole
        ring = []
        for j in range(segs):
            ph = 2 * math.pi * j / segs
            d = gaze * math.cos(th) + (right * math.cos(ph) + up2 * math.sin(ph)) * math.sin(th)
            co = center + d * EYE_R
            ring.append(B.add_vert(co, wfn(co)))
        verts_rings.append((th, ring))

    def uv_of(th, ph):
        r = math.sin(min(th, math.pi / 2)) * 0.5
        if th > math.pi / 2:
            r = 0.5
        return (0.5 + r * math.cos(ph), 0.5 + r * math.sin(ph))

    for k in range(len(verts_rings) - 1):
        th0, r0 = verts_rings[k]
        th1, r1 = verts_rings[k + 1]
        for j in range(segs):
            j2 = (j + 1) % segs
            p0 = 2 * math.pi * j / segs
            p1 = 2 * math.pi * (j + 1) / segs
            B.add_face([r0[j], r1[j], r1[j2], r0[j2]], MAT_EYE,
                       [uv_of(th0, p0), uv_of(th1, p0), uv_of(th1, p1), uv_of(th0, p1)])
    front = B.add_vert(center + gaze * EYE_R, wfn(center))
    back = B.add_vert(center - gaze * EYE_R, wfn(center))
    th0, r0 = verts_rings[0]
    thn, rn = verts_rings[-1]
    for j in range(segs):
        j2 = (j + 1) % segs
        p0 = 2 * math.pi * j / segs
        p1 = 2 * math.pi * (j + 1) / segs
        B.add_face([front, r0[j], r0[j2]], MAT_EYE, [(0.5, 0.5), uv_of(th0, p0), uv_of(th0, p1)])
        B.add_face([rn[j2], rn[j], back], MAT_EYE, [uv_of(thn, p1), uv_of(thn, p0), (0.5, 0.5)])

    # heavy upper eyelid: a shell sitting on the top of the eyeball (gives the sly, half-lidded look)
    for layer, (pol_deg, rs, th0, th1) in enumerate([(40, 1.025, 0.010, 0.012), (52, 1.02, 0.008, 0.010)]):
        lid_pts, lid_r = [], []
        n = 22
        pol = math.radians(pol_deg)
        for k in range(n + 1):
            f = k / n
            a = math.radians(12) + math.radians(156) * f
            d = gaze * math.cos(pol) + (right * math.cos(a) + up2 * math.sin(a)) * math.sin(pol)
            lid_pts.append(center + d * EYE_R * rs)
            lid_r.append((th0 + th1 * math.sin(math.pi * f), (th0 + th1 * math.sin(math.pi * f)) * 0.7))
        tube(lid_pts, lid_r, 10, MAT_WOOD, lambda co: torso_weights(Vector((co.x, co.y, EYE_Z))), ref=gaze)


def build_brow(side):
    pts, rad = [], []
    n = 14
    for k in range(n + 1):
        t = k / n
        px = side * (0.020 + 0.150 * t)
        pz = EYE_Z + 0.088 + 0.026 * math.sin(math.pi * (0.2 + 0.8 * t)) - 0.030 * t * t
        py = surface_y(px, pz) + 0.004
        pts.append((px, py, pz))
        rad.append((0.020 * (1.0 - 0.45 * t) + 0.005, 0.013 * (1.0 - 0.35 * t) + 0.004))
    tube(pts, rad, 10, MAT_DARK, torso_weights, ref=Vector((0, -1, 0)))


def build_nose():
    pts, rad = [], []
    for k in range(6):
        t = k / 5
        pz = 1.535 - 0.07 * t
        py = surface_y(0, pz) - 0.004 - 0.026 * math.sin(math.pi * min(1, t * 1.05))
        pts.append((0, py, pz))
        rad.append((0.016 + 0.007 * t, 0.013 + 0.004 * t))
    tube(pts, rad, 12, MAT_WOOD, torso_weights, ref=Vector((1, 0, 0)))


def build_mouth():
    # wide closed smile: dark seam + upper & lower lip
    def curve(zoff, depth, rx, ry, mat, width=0.135, n=22):
        pts, rad = [], []
        for k in range(n + 1):
            t = -1 + 2 * k / n
            px = t * width
            pz = 1.405 + zoff + 0.045 * t * t * (0.6 + 0.4 * abs(t))
            py = surface_y(px, pz) + depth
            pts.append((px, py, pz))
            taper = 1 - 0.55 * abs(t) ** 3
            rad.append((rx * taper, ry * taper))
        tube(pts, rad, 10, mat, torso_weights, ref=Vector((0, -1, 0)))
    curve(0.000, 0.004, 0.010, 0.0085, MAT_MOUTH, width=0.128)
    curve(0.017, 0.002, 0.012, 0.010, MAT_WOOD, width=0.122)
    curve(-0.019, 0.002, 0.014, 0.011, MAT_WOOD, width=0.105)


# ----------------------------------------------------------------------------
# 3. ARMS + HANDS
# ----------------------------------------------------------------------------
def build_arm(side):
    s = side
    chain = [
        (("Left" if s > 0 else "Right") + "Shoulder", (0.07 * s, 0, SHOULDER_Z), (SHOULDER_X * s, 0, SHOULDER_Z)),
        (("Left" if s > 0 else "Right") + "UpperArm", (SHOULDER_X * s, 0, SHOULDER_Z), (ELBOW_X * s, 0, SHOULDER_Z)),
        (("Left" if s > 0 else "Right") + "LowerArm", (ELBOW_X * s, 0, SHOULDER_Z), (WRIST_X * s, 0, SHOULDER_Z)),
        (("Left" if s > 0 else "Right") + "Hand", (WRIST_X * s, 0, SHOULDER_Z), (HAND_END_X * s, 0, SHOULDER_Z)),
    ]
    wfn = chain_weights(chain, blend=0.045)
    hb = ("Left" if s > 0 else "Right") + "Hand"
    hand_w = lambda co: {hb: 1.0}
    xs = [0.19, 0.24, SHOULDER_X, 0.32, 0.40, 0.47, ELBOW_X, 0.58, 0.66, 0.73, WRIST_X - 0.01,
          WRIST_X + 0.02, WRIST_X + 0.06, WRIST_X + 0.095]
    radii = [(0.050, 0.050), (0.050, 0.050), (0.048, 0.048), (0.045, 0.045), (0.043, 0.043), (0.041, 0.041),
             (0.040, 0.040), (0.040, 0.040), (0.038, 0.038), (0.034, 0.034), (0.031, 0.031),
             (0.031, 0.037), (0.035, 0.046), (0.031, 0.040)]
    pts = []
    for x in xs:
        dy = 0.0 if x < WRIST_X else -0.008 * (x - WRIST_X) / 0.095
        dz = (0.004 if 0.3 < x < 0.5 else 0.0) + (0.0 if x < WRIST_X else -0.010 * (x - WRIST_X) / 0.095)
        pts.append((x * s, dy, SHOULDER_Z + dz))
    tube(pts, radii, 14, MAT_WOOD, wfn, ref=Vector((0, 0, 1)), cap_start=True, cap_end=True, v_scale=2.0)
    # knuckle bumps
    for k in range(4):
        z = SHOULDER_Z + 0.018 - k * 0.013
        px = (WRIST_X + 0.088) * s
        c = Vector((px, -0.030, z - 0.004))
        tube([c + Vector((0.008 * s, 0, 0)), c + Vector((-0.004 * s, -0.004, 0))], [0.011, 0.011], 8, MAT_WOOD, hand_w)
    # thumb wrapping in front
    th = [((WRIST_X + 0.015) * s, -0.030, SHOULDER_Z - 0.006), ((WRIST_X + 0.045) * s, -0.046, SHOULDER_Z - 0.010),
          ((WRIST_X + 0.075) * s, -0.044, SHOULDER_Z - 0.014)]
    tube(th, [0.014, 0.012, 0.0105], 8, MAT_WOOD, hand_w)


# ----------------------------------------------------------------------------
# 4. LEGS + FEET
# ----------------------------------------------------------------------------
def build_leg(side):
    s = side
    L = "Left" if s > 0 else "Right"
    chain = [
        ("Hips", (HIP_X * s, 0, 0.95), (HIP_X * s, 0, HIP_Z)),
        (L + "UpperLeg", (HIP_X * s, 0, HIP_Z), (HIP_X * s, 0, KNEE_Z)),
        (L + "LowerLeg", (HIP_X * s, 0, KNEE_Z), (HIP_X * s, 0, ANKLE_Z)),
    ]
    wfn = chain_weights(chain, blend=0.05)
    zs = [0.90, 0.84, HIP_Z, 0.72, 0.64, 0.56, 0.49, KNEE_Z, 0.39, 0.31, 0.22, 0.15, 0.10]
    rad = [0.060, 0.062, 0.062, 0.058, 0.054, 0.050, 0.047, 0.046, 0.046, 0.044, 0.038, 0.034, 0.033]
    pts = [(HIP_X * s, 0.004 if 0.35 < z < 0.5 else 0.0, z) for z in zs]
    # calves slightly behind
    pts = [(p[0], p[1] + (0.006 if 0.18 < p[2] < 0.38 else 0.0), p[2]) for p in pts]
    tube(pts, rad, 14, MAT_WOOD, wfn, ref=Vector((0, -1, 0)), cap_start=True, cap_end=True, v_scale=2.0)

    foot_b, toe_b = L + "Foot", L + "Toes"

    def foot_w(co):
        lower = {L + "LowerLeg": 1.0}
        foot = {foot_b: 1.0}
        toes = {toe_b: 1.0}
        w = mix(lower, foot, smoothstep(0.13, 0.07, co.z))
        t = smoothstep(-0.095, -0.13, co.y)
        return mix(w, toes, t)

    # ankle -> foot body (heel to ball)
    fp = [(HIP_X * s, 0.004, 0.11), (HIP_X * s, 0.012, 0.065), (HIP_X * s, 0.006, 0.040),
          (HIP_X * s * 1.03, -0.040, 0.034), (HIP_X * s * 1.05, -0.095, 0.030), (HIP_X * s * 1.06, -0.135, 0.027)]
    fr = [(0.035, 0.035), (0.040, 0.042), (0.046, 0.036), (0.050, 0.032), (0.056, 0.028), (0.054, 0.023)]
    # heel bulge
    tube([(HIP_X * s, 0.035, 0.035), (HIP_X * s, 0.010, 0.032)], [0.032, 0.033], 12, MAT_WOOD, foot_w)
    tube(fp, fr, 14, MAT_WOOD, foot_w, ref=Vector((0, 0, 1)), cap_start=True, cap_end=True)
    # toes
    toe_x = [-0.032, -0.015, 0.002, 0.018, 0.033]
    toe_r = [0.0135, 0.0115, 0.011, 0.010, 0.009]
    toe_len = [0.040, 0.036, 0.032, 0.028, 0.022]
    for k in range(5):
        # big toe on the inner side (towards body center)
        ox = -toe_x[k] * s  # inner side = towards x=0
        x0 = HIP_X * s * 1.06 + ox
        y0 = -0.140 + abs(k - 0) * 0.004
        p0 = (x0, y0, 0.020)
        p1 = (x0 + 0.002 * s * (k - 2), y0 - toe_len[k], 0.016)
        tube([p0, p1], [toe_r[k], toe_r[k] * 0.9], 8, MAT_WOOD, lambda co: {toe_b: 1.0})


# ----------------------------------------------------------------------------
# Build all
# ----------------------------------------------------------------------------
build_log()
for sd in (-1, 1):
    build_eye(sd)
    build_brow(sd)
    build_arm(sd)
    build_leg(sd)
build_nose()
build_mouth()

# Create mesh
mesh = bpy.data.meshes.new("TungSahur_Mesh")
bm = bmesh.new()
uv_layer = bm.loops.layers.uv.new("UVMap")
bverts = [bm.verts.new(v) for v in B.verts]
bm.verts.ensure_lookup_table()
for (idx, mat, uvs) in B.faces:
    try:
        f = bm.faces.new([bverts[i] for i in idx])
    except ValueError:
        continue
    f.material_index = mat
    f.smooth = True
    for loop, uv in zip(f.loops, uvs):
        loop[uv_layer].uv = uv
bm.normal_update()
bm.to_mesh(mesh)
bm.free()

body = bpy.data.objects.new("TungSahur", mesh)
scene.collection.objects.link(body)

# Vertex groups
groups = {name: body.vertex_groups.new(name=name) for name in BONES.keys()}
for vi, w in enumerate(B.weights):
    tot = sum(w.values())
    for bname, val in w.items():
        if val <= 1e-4:
            continue
        groups[bname].add([vi], val / tot, 'REPLACE')


# Materials (preview colours; Unity replaces them with generated wood/eye materials)
def make_mat(name, rgb, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1)
        bsdf.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*rgb, 1)
    return m


mats = [make_mat("M_TungWood", (0.62, 0.38, 0.19)),
        make_mat("M_TungEye", (0.95, 0.94, 0.90), 0.2),
        make_mat("M_TungDarkWood", (0.16, 0.08, 0.04)),
        make_mat("M_TungMouth", (0.09, 0.03, 0.02))]
for m in mats:
    body.data.materials.append(m)

# Smooth: subdivision applied (keeps weights)
bpy.context.view_layer.objects.active = body
body.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0001)
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode='OBJECT')
sub = body.modifiers.new("Subsurf", 'SUBSURF')
sub.levels = 1
sub.render_levels = 1
bpy.ops.object.modifier_apply(modifier=sub.name)
for p in body.data.polygons:
    p.use_smooth = True

# ----------------------------------------------------------------------------
# Armature
# ----------------------------------------------------------------------------
arm_data = bpy.data.armatures.new("TungSahur_Armature")
arm_obj = bpy.data.objects.new("Armature", arm_data)
scene.collection.objects.link(arm_obj)
bpy.ops.object.select_all(action='DESELECT')
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
# CLUB (separate static mesh). Origin = grip point, extends along +Z.
# ----------------------------------------------------------------------------
B = Builder()
club_profile = [  # (z, r, mat)
    (-0.120, 0.000, 2), (-0.118, 0.026, 2), (-0.110, 0.034, 2), (-0.098, 0.035, 2), (-0.088, 0.028, 2),
    (-0.080, 0.019, 2), (-0.06, 0.0185, 2), (0.0, 0.019, 2), (0.06, 0.0195, 2), (0.14, 0.020, 2),
    (0.16, 0.0205, 0), (0.24, 0.022, 0), (0.33, 0.026, 0), (0.42, 0.033, 0), (0.50, 0.041, 0),
    (0.58, 0.048, 0), (0.66, 0.053, 0), (0.74, 0.056, 0), (0.80, 0.057, 0), (0.84, 0.056, 0),
    (0.865, 0.050, 0), (0.880, 0.038, 0), (0.888, 0.020, 0), (0.890, 0.000, 0),
]
segs = 24
rings = []
for (z, r, m) in club_profile:
    ring = []
    for j in range(segs):
        a = 2 * math.pi * j / segs
        # slight dents & wear
        rr = r * (1 + 0.012 * math.sin(5 * a + z * 31) * (1 if z > 0.3 else 0))
        ring.append(B.add_vert((math.cos(a) * rr, math.sin(a) * rr, z), {}))
    rings.append((ring, m, z))
for i in range(len(rings) - 1):
    r0, m0, z0 = rings[i]
    r1, m1, z1 = rings[i + 1]
    mat = m1 if z1 <= 0.16 else 0
    mat = 2 if z1 <= 0.15 else 0
    for j in range(segs):
        j2 = (j + 1) % segs
        B.add_face([r0[j], r0[j2], r1[j2], r1[j]], mat,
                   [(j / segs, z0 * 2), ((j + 1) / segs, z0 * 2), ((j + 1) / segs, z1 * 2), (j / segs, z1 * 2)])
cmesh = bpy.data.meshes.new("Club_Mesh")
bm = bmesh.new()
uv_layer = bm.loops.layers.uv.new("UVMap")
bverts = [bm.verts.new(v) for v in B.verts]
for (idx, mat, uvs) in B.faces:
    try:
        f = bm.faces.new([bverts[i] for i in idx])
    except ValueError:
        continue
    f.material_index = mat
    f.smooth = True
    for loop, uv in zip(f.loops, uvs):
        loop[uv_layer].uv = uv
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
bm.normal_update()
bm.to_mesh(cmesh)
bm.free()
club = bpy.data.objects.new("TungSahur_Club", cmesh)
scene.collection.objects.link(club)
club_wood = make_mat("M_ClubWood", (0.42, 0.22, 0.10))
club_grip = make_mat("M_ClubGrip", (0.08, 0.06, 0.05), 0.8)
club.data.materials.append(club_wood)
club.data.materials.append(club_wood)
club.data.materials.append(club_grip)
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = club
club.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode='OBJECT')
sub = club.modifiers.new("Subsurf", 'SUBSURF')
sub.levels = 1
bpy.ops.object.modifier_apply(modifier=sub.name)

# ----------------------------------------------------------------------------
# Export
# ----------------------------------------------------------------------------
def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=True,
        use_mesh_modifiers=False,
        mesh_smooth_type='FACE',
        use_tspace=True,
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=False,
        armature_nodetype='NULL',
        bake_anim=False,
    )
    print("EXPORTED", path)


export([arm_obj, body], os.path.join(OUT_DIR, "TungSahur.fbx"))
export([club], os.path.join(OUT_DIR, "TungSahur_Club.fbx"))

# Also save a .blend for hand-editing later
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "TungSahur_Source.blend"))
print("SAVED blend")

# ----------------------------------------------------------------------------
# Optional preview render (Workbench)
# ----------------------------------------------------------------------------
if PREVIEW:
    # pose club in right hand for preview
    club.location = Vector((-(WRIST_X + 0.05), -0.02, SHOULDER_Z))
    club.rotation_euler = (math.radians(90), 0, 0)
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = Vector((1.3, -3.4, 1.45))
    direction = Vector((0, 0, 1.05)) - cam.location
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = 45
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1100
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    # face close-up
    cam.location = Vector((0.35, -1.25, 1.55))
    direction = Vector((0, 0, 1.52)) - cam.location
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = PREVIEW.replace(".png", "_face.png")
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", PREVIEW)
