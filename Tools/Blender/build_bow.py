"""
Tung Sahur - bow, arrow and quiver (Blender 4.2+/5.x). Live (MCP) or headless:

    blender -b --factory-startup --python Tools/Blender/build_bow.py -- <out_dir>

Live via MCP:  exec(compile(open(p).read(), p, 'exec'), {"__name__": "__main__", "__file__": p, "TS_EXPORT": True})

Builds into a dedicated "TungBow" scene so the character scene is never touched.

Conventions (match the club / character exports):
  * Bow: limbs along +Z, origin at the grip centre, arrows fly toward -Y (Unity +Z), the string
    sits on the +Y side. A "Draw" shape key bends the limbs to full draw. Empties mark the string
    anchors at rest and at full draw plus the arrow rest; the string itself is drawn at runtime so
    the nocking point can follow the drawing hand.
  * Arrow: origin at the nock, shaft along -Y (Unity +Z), 0.80 m long.
  * Quiver: origin at its centre, opening toward +Z.
Material slot names match Unity materials so the importer can remap them.
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector, Matrix

_argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
_here = os.path.dirname(os.path.abspath(globals().get("__file__", bpy.data.filepath or ".")))
OUT_DIR = globals().get("TS_OUT_DIR") or (_argv[0] if _argv else os.path.join(_here, "export"))
DO_EXPORT = globals().get("TS_EXPORT", True)
os.makedirs(OUT_DIR, exist_ok=True)

if bpy.app.background and "--" in sys.argv:
    bpy.ops.wm.read_factory_settings(use_empty=True)

scene = bpy.data.scenes.get("TungBow") or bpy.data.scenes.new("TungBow")
for ob in list(scene.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
if bpy.context.window is not None:
    bpy.context.window.scene = scene

# ---------------------------------------------------------------------------------- dimensions
BOW_HALF = 0.68          # tip-to-tip 1.36 m (Tung is ~1.9 m tall)
RISER_HALF = 0.15        # stiff handle section (fraction of half length below is "s")
GRIP_LO, GRIP_HI = -0.075, 0.062
SHELF_Z = 0.075          # arrow rest just above the grip
ARROW_LEN = 0.80


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------------------------- materials
def node(nt, type_name, loc, **inputs):
    n = nt.nodes.new(type_name)
    n.location = loc
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n


def material(name, rgb, rough=0.55, metal=0.0, wood=False, weave=False):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial", (500, 0))
    bsdf = node(nt, "ShaderNodeBsdfPrincipled", (200, 0))
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    col = (*rgb, 1)
    if wood or weave:
        tc = node(nt, "ShaderNodeTexCoord", (-900, 0))
        mp = node(nt, "ShaderNodeMapping", (-700, 0))
        mp.inputs["Scale"].default_value = (60, 60, 4) if wood else (90, 90, 90)
        nt.links.new(tc.outputs["Object"], mp.inputs[0])
        if wood:
            tex = node(nt, "ShaderNodeTexWave", (-500, 0))
            tex.wave_type = 'RINGS'
            tex.inputs["Distortion"].default_value = 6
            tex.inputs["Detail"].default_value = 3
        else:
            tex = node(nt, "ShaderNodeTexVoronoi", (-500, 0))
        nt.links.new(mp.outputs[0], tex.inputs["Vector"])
        ramp = node(nt, "ShaderNodeValToRGB", (-250, 0))
        ramp.color_ramp.elements[0].color = (rgb[0] * 0.55, rgb[1] * 0.5, rgb[2] * 0.45, 1)
        ramp.color_ramp.elements[1].color = col
        nt.links.new(tex.outputs[1 if wood else 0], ramp.inputs[0])
        nt.links.new(ramp.outputs[0], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = col
    m.diffuse_color = col
    return m


MAT = {
    "M_BowWood": material("M_BowWood", (0.46, 0.27, 0.13), 0.42, wood=True),
    "M_BowHorn": material("M_BowHorn", (0.09, 0.06, 0.045), 0.3),
    "M_Rope": material("M_Rope", (0.55, 0.43, 0.26), 0.85, weave=True),
    "M_Iron": material("M_Iron", (0.17, 0.17, 0.18), 0.38, metal=0.9),
    "M_ArrowShaft": material("M_ArrowShaft", (0.78, 0.66, 0.42), 0.5, wood=True),
    "M_FletchRed": material("M_FletchRed", (0.62, 0.09, 0.06), 0.8),
    "M_FletchCream": material("M_FletchCream", (0.86, 0.8, 0.66), 0.8),
    "M_Quiver": material("M_Quiver", (0.6, 0.47, 0.28), 0.8, weave=True),
}


class MeshBuilder:
    """Collects verts (with an optional shape-key position), faces, materials and UVs."""

    def __init__(self):
        self.verts, self.drawn, self.faces, self.mats, self.uvs = [], [], [], [], []
        self.mat_names = []

    def v(self, co, drawn=None):
        self.verts.append(Vector(co))
        self.drawn.append(Vector(drawn if drawn is not None else co))
        return len(self.verts) - 1

    def f(self, idx, mat, uvs):
        if mat not in self.mat_names:
            self.mat_names.append(mat)
        self.faces.append(idx)
        self.mats.append(self.mat_names.index(mat))
        self.uvs.append(uvs)

    def build(self, name, shape_key=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in self.verts], [], self.faces)
        for n in self.mat_names:
            me.materials.append(MAT[n])
        for i, p in enumerate(me.polygons):
            p.material_index = self.mats[i]
        uv = me.uv_layers.new(name="UVMap")
        li = 0
        for i, p in enumerate(me.polygons):
            for k in range(p.loop_total):
                uv.data[p.loop_start + k].uv = self.uvs[i][k]
        me.validate()
        me.shade_smooth()
        ob = bpy.data.objects.new(name, me)
        scene.collection.objects.link(ob)
        # auto-smooth equivalent baked into edge flags (a modifier would block shape-key export)
        bm = bmesh.new()
        bm.from_mesh(me)
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(50):
                e.smooth = False
        bm.to_mesh(me)
        bm.free()
        if shape_key:
            ob.shape_key_add(name="Basis")
            k = ob.shape_key_add(name=shape_key)
            for i, co in enumerate(self.drawn):
                k.data[i].co = co
            k.value = 0.0  # new keys default to 1.0 in recent Blender; rest pose must be the basis
        return ob


def round_rect(n, w, t, r=0.8):
    """Superellipse cross-section: n points, half extents w (x) / t (y)."""
    pts = []
    e = 2.0 / (2.0 + 6.0 * r)  # r=1 -> boxy, r=0 -> ellipse
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        x = math.copysign(abs(c) ** e, c) * w
        y = math.copysign(abs(s) ** e, s) * t
        pts.append((x, y))
    return pts


def sweep(mb, frames, sections, mat_fn, closed_start=True, closed_end=True, v_scale=1.0):
    """frames: list of (C, X, N) for rest and drawn: [(rest, drawn), ...]; sections: per ring list of (u, v) offsets."""
    rings = []
    n = len(sections[0])
    for (rest, drawn), sec in zip(frames, sections):
        ring = []
        for (u, w) in sec:
            p = rest[0] + rest[1] * u + rest[2] * w
            q = drawn[0] + drawn[1] * u + drawn[2] * w
            ring.append(mb.v(p, q))
        rings.append(ring)
    acc = 0.0
    vs = [0.0]
    for i in range(1, len(frames)):
        acc += (frames[i][0][0] - frames[i - 1][0][0]).length
        vs.append(acc * v_scale)
    for i in range(len(rings) - 1):
        m = mat_fn(i)
        for j in range(n):
            j2 = (j + 1) % n
            a, b, c, d = rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]
            u0, u1 = j / n, (j + 1) / n
            mb.f([a, b, c, d], m, [(u0, vs[i]), (u1, vs[i]), (u1, vs[i + 1]), (u0, vs[i + 1])])
    for closed, ring, rev in ((closed_start, rings[0], True), (closed_end, rings[-1], False)):
        if not closed:
            continue
        idx = list(reversed(ring)) if rev else list(ring)
        mb.f(idx, mat_fn(0 if rev else len(rings) - 2), [(0.5 + 0.5 * math.cos(2 * math.pi * k / n), 0.5 + 0.5 * math.sin(2 * math.pi * k / n)) for k in range(n)])
    return rings


# ---------------------------------------------------------------------------------- bow
def bow_centerline(s_signed, drawn):
    """Point on the bow's centreline for s in [-1, 1] (bottom..top)."""
    s = abs(s_signed)
    sign = 1 if s_signed >= 0 else -1
    # reflexed riser sits toward the target; limbs sweep back toward the archer; recurved tips flick forward
    riser = -0.055 * (1 - smoothstep(0.0, 0.42, s))
    limb = 0.17 * s * s
    # quadratic past the knee so the tip keeps a forward slope (smoothstep would flatten it)
    rk = max(0.0, (s - 0.74) / 0.26)
    recurve = 0.105 * rk * rk
    y = riser + limb - recurve
    z = s * BOW_HALF
    if drawn:
        bend = smoothstep(RISER_HALF / BOW_HALF * 0.9, 1.0, s)
        y += 0.13 * bend ** 1.35
        y += recurve * 0.4           # recurve partly unfolds
        z -= 0.06 * bend ** 1.5      # tips come in as the limbs bend
    # top limb a hair longer than the bottom (real bows are tillered like this)
    if sign < 0:
        z *= 0.985
    return Vector((0.0, y, z * sign))


def bow_frame(s, drawn):
    eps = 1e-3
    c = bow_centerline(s, drawn)
    t = (bow_centerline(min(1, s + eps), drawn) - bow_centerline(max(-1, s - eps), drawn)).normalized()
    x = Vector((1, 0, 0))
    nrm = t.cross(x).normalized()  # in the YZ plane, pointing toward +Y at the grip
    if nrm.y < 0:
        nrm = -nrm
    return (c, x, nrm)


def bow_section(s):
    a = abs(s)
    riser = 1 - smoothstep(0.17, 0.30, a)
    width = 0.046 * (1 - a * 0.55) * (1 - riser) + 0.036 * riser
    thick = 0.019 * (1 - a * 0.45) * (1 - riser) + 0.056 * riser
    # grip swell
    z = a * BOW_HALF
    grip = math.exp(-((s * BOW_HALF - (GRIP_LO + GRIP_HI) / 2) / 0.07) ** 2)
    width += 0.004 * grip
    thick -= 0.01 * grip
    if a > 0.965:  # tapered nock tips
        k = smoothstep(0.965, 1.0, a)
        width *= 1 - 0.45 * k
        thick *= 1 - 0.2 * k
    return round_rect(14, width / 2, thick / 2, r=0.55 + 0.35 * riser)


def build_bow():
    mb = MeshBuilder()
    N = 120
    ss = []
    for i in range(N + 1):
        # denser sampling near the tips and riser transitions
        t = -1 + 2 * i / N
        ss.append(math.copysign(abs(t) ** 0.92, t))
    frames = [(bow_frame(s, False), bow_frame(s, True)) for s in ss]
    sections = [bow_section(s) for s in ss]

    def mat_fn(i):
        s = abs((ss[i] + ss[i + 1]) / 2)
        if s > 0.945:
            return "M_BowHorn"   # horn tip overlays
        if 0.235 < s < 0.255:
            return "M_BowHorn"   # inlay band where limb meets riser
        return "M_BowWood"

    sweep(mb, frames, sections, mat_fn, v_scale=1.5)

    # rattan grip wrap: tilted rings hugging the grip
    turns = 15
    for k in range(turns):
        z0 = GRIP_LO + (GRIP_HI - GRIP_LO) * (k + 0.5) / turns
        s0 = z0 / BOW_HALF
        c, x, nrm = bow_frame(s0, False)
        sec = bow_section(s0)
        seg = len(sec)
        tube_r = 0.0042
        ring_pts = []
        for j in range(seg * 2):
            a = 2 * math.pi * j / (seg * 2)
            # interpolate the section perimeter
            fi = (a / (2 * math.pi)) * seg
            j0 = int(fi) % seg
            j1 = (j0 + 1) % seg
            fr = fi - int(fi)
            u = sec[j0][0] * (1 - fr) + sec[j1][0] * fr
            w = sec[j0][1] * (1 - fr) + sec[j1][1] * fr
            outward = Vector((u, w)).normalized() * (tube_r * 0.85)
            tilt = 0.006 * math.sin(a)  # spiral look
            ring_pts.append(c + x * (u + outward.x) + nrm * (w + outward.y) + Vector((0, 0, tilt)))
        tube_loop(mb, ring_pts, tube_r, 6, "M_Rope")

    # leather-ish arrow shelf pad on the archer's left (-X) side
    z0 = SHELF_Z
    c, x, nrm = bow_frame(z0 / BOW_HALF, False)
    sec = bow_section(z0 / BOW_HALF)
    half_w = max(p[0] for p in sec)
    shelf = [(-half_w - 0.008, -0.012), (-half_w - 0.008, 0.012)]
    base = c + x * (-half_w + 0.002)
    quad = []
    for dz in (-0.012, 0.014):
        for dy in (-0.016, 0.018):
            quad.append(mb.v(base + nrm * dy + Vector((0, 0, dz)) + x * -0.007))
    for dz in (-0.012, 0.014):
        for dy in (-0.016, 0.018):
            quad.append(mb.v(base + nrm * dy + Vector((0, 0, dz))))
    box_faces(mb, quad, "M_BowHorn")

    ob = mb.build("TungSahur_Bow", shape_key="Draw")
    return ob


def tube_loop(mb, pts, r, seg, mat):
    n = len(pts)
    rings = []
    for i in range(n):
        p = pts[i]
        t = (pts[(i + 1) % n] - pts[i - 1]).normalized()
        ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        a = t.cross(ref).normalized()
        b = t.cross(a).normalized()
        ring = []
        for j in range(seg):
            th = 2 * math.pi * j / seg
            ring.append(mb.v(p + (a * math.cos(th) + b * math.sin(th)) * r))
        rings.append(ring)
    for i in range(n):
        for j in range(seg):
            i2, j2 = (i + 1) % n, (j + 1) % seg
            mb.f([rings[i][j], rings[i][j2], rings[i2][j2], rings[i2][j]], mat,
                 [(j / seg, i / n), ((j + 1) / seg, i / n), ((j + 1) / seg, (i + 1) / n), (j / seg, (i + 1) / n)])


def box_faces(mb, q, mat):
    # q: 8 verts ordered [inner(z0y0,z0y1,z1y0,z1y1), outer(...)]
    i = q
    faces = [(i[0], i[1], i[3], i[2]), (i[4], i[6], i[7], i[5]), (i[0], i[4], i[5], i[1]),
             (i[2], i[3], i[7], i[6]), (i[0], i[2], i[6], i[4]), (i[1], i[5], i[7], i[3])]
    for f in faces:
        mb.f(list(f), mat, [(0, 0), (1, 0), (1, 1), (0, 1)])


def add_empty(name, loc, parent):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'
    e.empty_display_size = 0.03
    e.location = loc
    scene.collection.objects.link(e)
    e.parent = parent
    return e


# ---------------------------------------------------------------------------------- arrow
def lathe(mb, profile, seg, mat_fn, axis_dir=Vector((0, -1, 0)), origin=Vector((0, 0, 0)), cap_start=True, cap_end=True):
    """profile: list of (distance along axis, radius)."""
    ref = Vector((1, 0, 0))
    a = axis_dir.cross(ref).normalized() if abs(axis_dir.dot(ref)) < 0.9 else axis_dir.cross(Vector((0, 0, 1))).normalized()
    b = axis_dir.cross(a).normalized()
    frames, secs = [], []
    for d, r in profile:
        c = origin + axis_dir * d
        frames.append(((c, a, b), (c, a, b)))
        secs.append([(math.cos(2 * math.pi * j / seg) * r, math.sin(2 * math.pi * j / seg) * r) for j in range(seg)])
    return sweep(mb, frames, secs, mat_fn, cap_start, cap_end)


def build_arrow(name="TungSahur_Arrow", mb=None, origin=Vector((0, 0, 0)), axis=Vector((0, -1, 0)), roll=0.0, head=True, length=ARROW_LEN):
    own = mb is None
    mb = mb or MeshBuilder()
    L = length
    sr = 0.0048
    # nock (dark horn) -> bamboo shaft with nodes -> iron socket
    nock = [(0.0, 0.0035), (0.002, 0.0056), (0.024, 0.0058), (0.030, sr)]
    shaft = []
    nodes = (0.30, 0.55)
    d = 0.030
    while d < L - 0.075:
        r = sr
        for nd in nodes:
            r += 0.0009 * math.exp(-((d - nd) / 0.006) ** 2)
        shaft.append((d, r))
        d += 0.012
    shaft.append((L - 0.075, sr))
    prof = nock + shaft[1:]
    lim = len(nock) - 1

    def m_fn(i):
        return "M_BowHorn" if i < lim else "M_ArrowShaft"

    lathe(mb, prof, 8, m_fn, axis, origin, True, not head)

    # thread wraps holding the fletching
    for d0 in (0.036, 0.17):
        lathe(mb, [(d0 - 0.006, sr * 1.02), (d0 - 0.005, sr * 1.25), (d0 + 0.005, sr * 1.25), (d0 + 0.006, sr * 1.02)], 8,
              lambda i: "M_BowHorn", axis, origin, False, False)

    if head:
        # socket + leaf-shaped broadhead blade (two crossed thin blades read better in flight)
        lathe(mb, [(L - 0.08, sr * 1.05), (L - 0.074, 0.0062), (L - 0.05, 0.0058), (L - 0.044, 0.0042)], 8,
              lambda i: "M_Iron", axis, origin, False, False)
        ref = Vector((1, 0, 0)) if abs(axis.dot(Vector((1, 0, 0)))) < 0.9 else Vector((0, 0, 1))
        for k in range(2):
            ang = roll + k * math.pi / 2
            side = (axis.cross(ref).normalized() * math.cos(ang) + axis.cross(axis.cross(ref)).normalized() * math.sin(ang))
            thick_dir = axis.cross(side).normalized()
            blade = [(L - 0.062, 0.0), (L - 0.05, 0.011), (L - 0.03, 0.0135), (L - 0.012, 0.009), (L + 0.012, 0.0)]
            top, bot = [], []
            for (dd, w) in blade:
                c = origin + axis * dd
                top.append((mb.v(c + side * w + thick_dir * 0.0012), mb.v(c - side * w + thick_dir * 0.0012)))
                bot.append((mb.v(c + side * w - thick_dir * 0.0012), mb.v(c - side * w - thick_dir * 0.0012)))
            for i in range(len(blade) - 1):
                for (s0, s1) in ((0, 0), (1, 1)):
                    a, b = top[i][s0], top[i + 1][s0]
                    c2, d2 = bot[i + 1][s0], bot[i][s0]
                    mb.f([a, b, c2, d2] if s0 == 0 else [a, d2, c2, b], "M_Iron", [(0, 0), (1, 0), (1, 1), (0, 1)])
                mb.f([top[i][0], top[i][1], top[i + 1][1], top[i + 1][0]], "M_Iron", [(0, 0), (1, 0), (1, 1), (0, 1)])
                mb.f([bot[i][0], bot[i + 1][0], bot[i + 1][1], bot[i][1]], "M_Iron", [(0, 0), (1, 0), (1, 1), (0, 1)])

    # three fletchings: one red cock feather, two cream hens
    ref = Vector((1, 0, 0)) if abs(axis.dot(Vector((1, 0, 0)))) < 0.9 else Vector((0, 0, 1))
    e1 = axis.cross(ref).normalized()
    e2 = axis.cross(e1).normalized()
    for k in range(3):
        ang = roll + k * 2 * math.pi / 3 + math.pi / 2
        out = e1 * math.cos(ang) + e2 * math.sin(ang)
        thick = axis.cross(out).normalized()
        mat = "M_FletchRed" if k == 0 else "M_FletchCream"
        n = 9
        top, bot = [], []
        for i in range(n + 1):
            t = i / n
            dd = 0.04 + t * 0.125
            # shield-cut feather: low at the front, tall and squared at the back
            h = 0.0045 + 0.017 * (math.sin((1 - t) * math.pi * 0.62) ** 0.8) * (0.55 + 0.45 * (1 - t))
            twist = (t - 0.5) * 0.0015  # slight helical offset
            base = origin + axis * dd + out * sr * 0.9
            tip = base + out * h
            top.append((mb.v(base + thick * (0.0006 + twist)), mb.v(tip + thick * (0.0006 + twist))))
            bot.append((mb.v(base - thick * (0.0006 - twist)), mb.v(tip - thick * (0.0006 - twist))))
        for i in range(n):
            u0, u1 = i / n, (i + 1) / n
            mb.f([top[i][0], top[i + 1][0], top[i + 1][1], top[i][1]], mat, [(u0, 0), (u1, 0), (u1, 1), (u0, 1)])
            mb.f([bot[i][0], bot[i][1], bot[i + 1][1], bot[i + 1][0]], mat, [(u0, 0), (u0, 1), (u1, 1), (u1, 0)])
            mb.f([top[i][1], top[i + 1][1], bot[i + 1][1], bot[i][1]], mat, [(u0, 1), (u1, 1), (u1, 1), (u0, 1)])
        mb.f([top[0][0], top[0][1], bot[0][1], bot[0][0]], mat, [(0, 0), (0, 1), (0, 1), (0, 0)])
        mb.f([top[n][0], bot[n][0], bot[n][1], top[n][1]], mat, [(1, 0), (1, 0), (1, 1), (1, 1)])
    if own:
        return mb.build(name)
    return None


# ---------------------------------------------------------------------------------- quiver
def build_quiver():
    mb = MeshBuilder()
    H = 0.56
    body = []
    for i in range(29):
        t = i / 28
        r = 0.066 + 0.012 * t + 0.003 * math.sin(t * math.pi)
        body.append((t * H - H / 2, r))
    # body (open top: inner wall)
    lathe(mb, body, 20, lambda i: "M_Quiver", Vector((0, 0, 1)), Vector((0, 0, 0)), True, False)
    inner = [(H / 2, 0.074), (H / 2 - 0.12, 0.07)]
    lathe(mb, inner, 20, lambda i: "M_Quiver", Vector((0, 0, 1)), Vector((0, 0, 0)), False, True)
    # dark wooden bands: base, middle, rim
    for (z0, r0, hgt) in ((-H / 2 + 0.01, 0.069, 0.03), (0.02, 0.075, 0.024), (H / 2 - 0.014, 0.081, 0.03)):
        lathe(mb, [(z0 - hgt / 2, r0), (z0 - hgt / 2 + 0.003, r0 + 0.005), (z0 + hgt / 2 - 0.003, r0 + 0.005), (z0 + hgt / 2, r0)],
              20, lambda i: "M_BowHorn", Vector((0, 0, 1)), Vector((0, 0, 0)), False, False)
    # strap loops
    for z0 in (-0.12, 0.17):
        pts = []
        for j in range(24):
            a = 2 * math.pi * j / 24
            pts.append(Vector((math.cos(a) * 0.084, math.sin(a) * 0.084, z0)))
        tube_loop(mb, pts, 0.0055, 6, "M_Rope")
    # arrow tails poking out (fanned)
    import random
    rng = random.Random(4)
    for k in range(6):
        a = k * 2 * math.pi / 6 + rng.uniform(-0.3, 0.3)
        rr = 0.03 + rng.uniform(0, 0.02)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, H / 2 + 0.06 + rng.uniform(0, 0.05)))
        axis = Vector((math.cos(a) * -0.08, math.sin(a) * -0.08, -1)).normalized()
        # only the part above the rim is visible: stop the shafts inside the quiver
        build_arrow(mb=mb, origin=base, axis=axis, roll=rng.uniform(0, 3), head=False, length=0.38)
    return mb.build("TungSahur_Quiver")


# ---------------------------------------------------------------------------------- build
bow = build_bow()
top_rest = bow_centerline(1.0, False)
bot_rest = bow_centerline(-1.0, False)
top_drawn = bow_centerline(1.0, True)
bot_drawn = bow_centerline(-1.0, True)
# string anchors sit on the belly side of the tips
add_empty("StringTop", top_rest + Vector((0, 0.004, -0.012)), bow)
add_empty("StringBottom", bot_rest + Vector((0, 0.004, 0.012)), bow)
add_empty("StringTopDrawn", top_drawn + Vector((0, 0.004, -0.012)), bow)
add_empty("StringBottomDrawn", bot_drawn + Vector((0, 0.004, 0.012)), bow)
shelf_c = bow_centerline(SHELF_Z / BOW_HALF, False)
add_empty("ArrowRest", Vector((-0.024, shelf_c.y, SHELF_Z + 0.008)), bow)

arrow = build_arrow()
arrow.location = (0.35, 0.0, 0.0)
quiver = build_quiver()
quiver.location = (-0.35, 0.0, 0.0)

# look-dev string (not exported: Unity draws the string at runtime)
crv = bpy.data.curves.new("BowStringPreview", 'CURVE')
crv.dimensions = '3D'
crv.bevel_depth = 0.0016
sp = crv.splines.new('POLY')
sp.points.add(1)
sp.points[0].co = (*bow.children[0].location, 1) if bow.children else (*top_rest, 1)
sl = {c.name: c.location for c in bow.children}
sp.points[0].co = (*sl["StringTop"], 1)
sp.points[1].co = (*sl["StringBottom"], 1)
string_ob = bpy.data.objects.new("BowStringPreview", crv)
string_ob.data.materials.append(material("M_BowString", (0.85, 0.8, 0.68), 0.6))
scene.collection.objects.link(string_ob)
string_ob.parent = bow

# look-dev lighting (warm key / cool fill / rim, matching the game's lantern night); never exported
if scene.world is None:
    scene.world = bpy.data.worlds.new("TungBowWorld")
scene.world.use_nodes = True
_bg = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND")
_bg.inputs[0].default_value = (0.05, 0.06, 0.08, 1)
_bg.inputs[1].default_value = 1.0
for _n, _loc, _e, _c in (("BowKey", (2.0, -1.5, 1.8), 220, (1.0, 0.82, 0.62)), ("BowFill", (2.2, 2.0, 0.6), 90, (0.6, 0.7, 1.0)),
                         ("BowRim", (-1.8, 0.8, 1.6), 160, (1.0, 0.9, 0.8))):
    _l = bpy.data.lights.new(_n, 'AREA')
    _l.energy, _l.color, _l.size = _e, _c, 1.2
    _lo = bpy.data.objects.new(_n, _l)
    _lo.location = _loc
    _lo.rotation_euler = (Vector((0, 0, 0)) - Vector(_loc)).to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(_lo)

print("bow verts", len(bow.data.vertices), "arrow verts", len(arrow.data.vertices), "quiver verts", len(quiver.data.vertices))
print("string anchors rest", tuple(round(v, 3) for v in sl["StringTop"]), tuple(round(v, 3) for v in sl["StringBottom"]))
print("brace height", round(sl["StringTop"].y - bow_centerline(0.0, False).y, 3))


def export(objs, path, types=frozenset({'MESH', 'EMPTY'})):
    for o in scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    saved = {o: o.location.copy() for o in objs if o.parent is None}
    for o in saved:
        o.location = (0, 0, 0)
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types=set(types),
            apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
            axis_forward='-Z', axis_up='Y', bake_space_transform=True,
            use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=True,
            add_leaf_bones=False, bake_anim=False)
    for o, loc in saved.items():
        o.location = loc
    print("exported", path)


if DO_EXPORT:
    empties = [c for c in bow.children if c.type == 'EMPTY']
    export([bow] + empties, os.path.join(OUT_DIR, "TungSahur_Bow.fbx"))
    export([arrow], os.path.join(OUT_DIR, "TungSahur_Arrow.fbx"))
    export([quiver], os.path.join(OUT_DIR, "TungSahur_Quiver.fbx"))
