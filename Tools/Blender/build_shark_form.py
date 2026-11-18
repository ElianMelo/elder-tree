"""
Shark Mask set, PSX dark fantasy (Docs/ART_DIRECTION.md). Everything the Tralalero reward needs:

    blender -b --factory-startup --python Tools/Blender/build_shark_form.py -- <abs out_dir>

SharkForm.fbx     - the humanoid shark the player turns into. Same skeleton as the Elder Tree and the
                    alligator (Human Mega Animations Pack HumanM x 0.95), PS1 rigid skinning. Countershaded
                    slate hide with a pale belly, a tall swept dorsal fin, gill slits, a tapered great-white head with
                    an underslung grin of serrated teeth, pectoral fins sweeping off the forearms, a crescent
                    caudal fin, a shark-jaw pauldron, and Tralalero's worn blue running shoes (no brand marks).
SharkSword.fbx    - two-handed long sword (nodachi). Origin = grip, blade along +Z (Unity +Y), cutting edge
                    toward -X (faces away from the wielder in Unity). Rayskin grip, jaw-bone guard ringed with teeth,
                    tooth pommel, a curved blade with a saw of shark teeth on the spine.
SharkCrossbow.fbx - crossbow: origin = grip, bolts fly along -Y (Unity +Z). Dark wood stock wrapped in hide,
                    a crescent tail-fin prod, a shark jaw ringing the bolt's exit, side crank, fin sight.
                    Children: "LoadedBolt" (shown while loaded) and the "Muzzle" empty.
SharkBolt.fbx     - the projectile: origin at the nock, along -Y; serrated tooth head, fin fletching.
SharkMask.fbx     - the reward item: slate shark face with a grin of teeth and a dorsal crest.
Vines.fbx         - VineSegment0-2 (0.5 m, origin at the top, hanging -Z), VineBeam (lintel on two posts)
                    and ThicketMound (root mound): the cuttable vines.

Materials are remapped by name in Unity. UVs are box-projected in metres, like the other sets.
Conventions: Z up, the character faces -Y, its LEFT side is +X.
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = globals().get("TS_EXPORT", True)
OUT = os.path.abspath(argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(globals().get("__file__", "."))), "export"))
if bpy.app.background:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
else:
    scene = bpy.data.scenes.get("SharkForm") or bpy.data.scenes.new("SharkForm")
    for ob in list(scene.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    try:
        bpy.context.window.scene = scene
    except Exception:
        pass

MAT_RGB = {
    "M_SharkSkin": (0.17, 0.21, 0.24), "M_SharkBelly": (0.5, 0.49, 0.43), "M_SharkEye": (0.08, 0.03, 0.02),
    "M_CrocMouth": (0.25, 0.08, 0.08), "M_CrocTeeth": (0.62, 0.57, 0.46), "M_SneakerCloth": (0.15, 0.27, 0.35),
    "M_SneakerSole": (0.55, 0.53, 0.47), "M_Leather": (0.22, 0.14, 0.09), "M_Iron": (0.12, 0.11, 0.11),
    "M_Rope": (0.4, 0.33, 0.22), "M_DarkWood": (0.16, 0.11, 0.08), "M_BowString": (0.6, 0.56, 0.48),
    "M_SharkSteel": (0.2, 0.23, 0.27), "M_SharkEdge": (0.55, 0.6, 0.62), "M_Vine": (0.17, 0.2, 0.1),
    "M_VineLeaf": (0.13, 0.2, 0.09), "M_Bark": (0.18, 0.14, 0.1),
}
# metres per texture tile (box projection)
TILE = {"M_SharkSkin": 0.9, "M_SharkBelly": 0.7, "M_CrocMouth": 0.6, "M_CrocTeeth": 0.4, "M_SneakerCloth": 0.4,
        "M_SneakerSole": 0.4, "M_Leather": 0.8, "M_Iron": 1.0, "M_Rope": 0.5, "M_DarkWood": 1.0, "M_BowString": 0.5,
        "M_SharkSteel": 0.8, "M_SharkEdge": 0.8, "M_Vine": 0.5, "M_VineLeaf": 0.4, "M_Bark": 1.5}
FLAT = ("M_SharkEye",)

# ----------------------------------------------------------------------------------------------- skeleton
# Identical to build_elder_tree.py (HumanM x 0.95). Keep these in sync: sockets in Unity rely on them.
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
    """One mesh piece assembled with bmesh. bone: rigid skin binding (100% to that bone)."""

    def __init__(self, name, bone=None, origin=V((0, 0, 0))):
        self.name, self.bone, self.origin = name, bone, V(origin)
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def v(self, co):
        return self.bm.verts.new(V(co) - self.origin)

    def face(self, verts, mat):
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = self.mi(mat)
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

    def box(self, c, size, mat, rot=None):
        c, (sx, sy, sz) = V(c), size
        corners = [V((x * sx / 2, y * sy / 2, z * sz / 2)) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
        if rot is not None:
            corners = [rot @ p for p in corners]
        vs = [self.v(c + p) for p in corners]
        for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.face([vs[i] for i in q], mat)

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
            vg.add([v.index for v in me.vertices], 1.0, 'REPLACE')
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
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


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
    print("EXPORTED", path, "tris", tris([o for o in objs if o.type == 'MESH']))




def counter_shade(sides, phase=0.0, front=-0.45, skin="M_SharkSkin", belly="M_SharkBelly"):
    """Countershading: faces looking forward (-Y) get the pale belly, the rest the slate hide."""
    def fn(i, j):
        if i < 0:
            return skin
        a = 2 * math.pi * (j + 0.5) / sides + phase
        return belly if math.sin(a) < front else skin
    return fn


def fin(part, base0, base1, tip, thick, mat, back_curve=0.0):
    """Thin swept fin: triangle base0-base1-tip, given thickness along the base x tip normal."""
    base0, base1, tip = V(base0), V(base1), V(tip)
    n = (base1 - base0).cross(tip - base0).normalized() * thick
    mid = (base0 + base1) * 0.5
    # a soft trailing curve: the tip edge bows back
    bow = (tip + base1) * 0.5 + (base1 - base0).normalized() * back_curve
    ring_a = [base0 + n, bow + n * 0.4, base1 + n]
    ring_b = [base0 - n, bow - n * 0.4, base1 - n]
    vs_a = [part.v(p) for p in ring_a]
    vs_b = [part.v(p) for p in ring_b]
    t = part.v(tip)
    part.face([vs_a[0], vs_a[1], t], mat)
    part.face([vs_a[1], vs_a[2], t], mat)
    part.face([vs_b[1], vs_b[0], t], mat)
    part.face([vs_b[2], vs_b[1], t], mat)
    part.face([vs_a[0], vs_b[0], vs_b[1], vs_a[1]], mat)
    part.face([vs_a[1], vs_b[1], vs_b[2], vs_a[2]], mat)
    part.face([vs_a[2], vs_b[2], vs_b[0], vs_a[0]], mat)


def tooth_row(part, pts, down, size, mat="M_CrocTeeth", side=V((0, 1, 0))):
    """Triangular serrated teeth standing on pts, pointing along down."""
    for k, p in enumerate(pts):
        p = V(p)
        h = size * (0.8 + 0.2 * ((k * 5) % 3) / 2)
        part.spike(p, p + V(down).normalized() * h, side, size * 0.38, mat)


# =================================================================================================
# SHARK FORM
# =================================================================================================
parts = []


def P(name, bone):
    p = Part(name, bone)
    parts.append(p)
    return p


# ---- torso: V-shaped swimmer's trunk, countershaded (pale belly in front, slate hide behind)
TS = 10
torso = [
    ("Hips", [(0.78, 0.16, 0.14, 0.02), (0.86, 0.2, 0.17, 0.01), (0.96, 0.215, 0.18, 0.0), (1.05, 0.22, 0.18, 0.0)]),
    ("Spine", [(1.0, 0.22, 0.18, 0.0), (1.11, 0.24, 0.195, -0.005), (1.23, 0.27, 0.21, -0.01)]),
    ("Chest", [(1.19, 0.27, 0.21, -0.01), (1.30, 0.31, 0.235, -0.015), (1.40, 0.315, 0.23, -0.015),
               (1.48, 0.26, 0.2, -0.015), (1.53, 0.16, 0.15, -0.02)]),
    ("Neck", [(1.45, 0.17, 0.155, -0.025), (1.55, 0.15, 0.145, -0.035), (1.64, 0.14, 0.14, -0.045)]),
]
for bone, rings in torso:
    p = P("Shark_" + bone, bone)
    tube(p, [(0, y, z) for z, _, _, y in rings], [(rx, ry) for _, rx, ry, _ in rings], TS, "M_SharkSkin",
         ref=V((1, 0, 0)), phase=math.pi / TS, mats=counter_shade(TS, math.pi / TS), jag=0.03, seed=len(parts))


def torso_surface(z, side_angle):
    for bone, rings in torso:
        for (z0, rx0, ry0, y0), (z1, rx1, ry1, y1) in zip(rings, rings[1:]):
            if z0 <= z <= z1:
                t = (z - z0) / (z1 - z0)
                rx, ry, y = rx0 + (rx1 - rx0) * t, ry0 + (ry1 - ry0) * t, y0 + (y1 - y0) * t
                return V((math.cos(side_angle) * rx, y + math.sin(side_angle) * ry, z))
    return V((0, 0, z))


# the dorsal fin: tall, swept back, the silhouette everyone knows (rigid on the chest)
dorsal = P("Shark_Dorsal", "Chest")
b0 = torso_surface(1.2, math.pi / 2) + V((0, -0.02, 0))
b1 = torso_surface(1.47, math.pi / 2) + V((0, -0.03, 0))
fin(dorsal, b0, b1, V((0, 0.62, 1.72)), 0.022, "M_SharkSkin", back_curve=-0.06)
# a torn notch and old bite scars read on the trailing edge (two little nicks)
dorsal.spike(V((0, 0.43, 1.5)), V((0, 0.5, 1.47)), V((1, 0, 0)), 0.012, "M_SharkBelly")
# gill slits: three dark gashes on each side of the neck
for s in (-1, 1):
    for k in range(3):
        z = 1.47 + k * 0.045
        q = torso_surface(min(z, 1.62), -0.2 if s > 0 else math.pi + 0.2)
        q = V((s * (abs(q.x) + 0.004), q.y - 0.02, z))
        dorsal.loft([[q + V((0, 0.035, 0.012)), q + V((0, -0.035, -0.008))],
                     [q + V((s * 0.006, 0.035, 0.014)), q + V((s * 0.006, -0.035, -0.006))]], "M_CrocMouth", closed=False)

# belt: rope-tied leather strap with an iron ring (the scabbard hangs from it when the crossbow is up)
belt = P("Shark_Belt", "Hips")
tube(belt, [(0, 0.005, 0.90), (0, 0.005, 0.955)], [(0.226, 0.187)] * 2, TS, "M_Leather", ref=V((1, 0, 0)),
     phase=math.pi / TS, cap0=False, cap1=False)
tube(belt, [(0.12, -0.2, 0.93), (0.13, -0.215, 0.93)], [0.035, 0.035], 6, "M_Iron", ref=V((0, 1, 0)), cap0=False, cap1=False)
for k in range(2):
    belt.spike((0.12 + k * 0.03, -0.2, 0.9), (0.14 + k * 0.035, -0.22, 0.74 - k * 0.04), V((0, 1, 0)), 0.014, "M_Rope")

# ---- head: one tapered great-white head flowing out of the neck (no neck step). Slate on top, pale underneath,
# a closed conical snout, an underslung jaw hanging a little open, rows of big triangular teeth, small flush eyes.
HS = 10
HP = [  # y (forward is -), z centre, half height, half width
    (0.10, 1.60, 0.15, 0.135), (0.02, 1.64, 0.15, 0.155), (-0.08, 1.665, 0.135, 0.15), (-0.17, 1.68, 0.11, 0.13),
    (-0.25, 1.69, 0.085, 0.105), (-0.32, 1.695, 0.062, 0.075), (-0.38, 1.697, 0.036, 0.042), (-0.42, 1.695, 0.012, 0.012)]


def underside(sides, phase, under=-0.45, top=None, top_mat="M_CrocMouth"):
    """Material callback for tubes along Y with ref +Z: faces looking down get the pale belly (countershading)."""
    def fn(i, j):
        if i < 0:
            return "M_SharkSkin"
        c = math.cos(2 * math.pi * (j + 0.5) / sides + phase)
        if top is not None and c > top:
            return top_mat
        return "M_SharkBelly" if c < under else "M_SharkSkin"
    return fn


def head_at(y, prof=HP):
    """Interpolated (z centre, half height, half width) of a profile at y."""
    for (y0, z0, h0, w0), (y1, z1, h1, w1) in zip(prof, prof[1:]):
        if y1 <= y <= y0:
            t = (y0 - y) / (y0 - y1)
            return z0 + (z1 - z0) * t, h0 + (h1 - h0) * t, w0 + (w1 - w0) * t
    return prof[-1][1], prof[-1][2], prof[-1][3]


head = P("Shark_Head", "Head")
tube(head, [(0, y, z) for y, z, _, _ in HP], [(h, w) for _, _, h, w in HP], HS, "M_SharkSkin", ref=V((0, 0, 1)),
     phase=math.pi / HS, jag=0.02, seed=31, mats=underside(HS, math.pi / HS))
# lower jaw: hinged at the back of the mouth, hanging ~10 degrees open; its top is the dark mouth
JAW_HINGE = (0.06, 1.585)
JP = [(0.06, 1.55, 0.05, 0.12), (-0.04, 1.54, 0.045, 0.125), (-0.14, 1.548, 0.04, 0.105), (-0.22, 1.562, 0.03, 0.075),
      (-0.27, 1.575, 0.018, 0.035)]


def jaw_z(y, z):
    return z + (y - JAW_HINGE[0]) * 0.17     # front drops: the mouth hangs open


jaw = P("Shark_Jaw", "Head")
tube(jaw, [(0, y, jaw_z(y, z)) for y, z, _, _ in JP], [(h, w) for _, _, h, w in JP], 8, "M_SharkBelly", ref=V((0, 0, 1)),
     phase=math.pi / 8, jag=0.02, seed=37, mats=underside(8, math.pi / 8, under=-0.2, top=0.55))
# roof of the mouth: a dark U under the skull between the upper tooth rows (both faces)
roof = []
for y in (0.04, -0.06, -0.16, -0.24, -0.29):
    z, hh, hw = head_at(y)
    roof.append([V((-hw * 0.66, y, z - hh * 0.62)), V((hw * 0.66, y, z - hh * 0.62))])
head.loft(roof, "M_CrocMouth", closed=False)
head.loft([list(reversed(r)) for r in roof], "M_CrocMouth", closed=False)
# teeth: the grin of a great white. Upper row hangs from the skull's lower rim, lower row stands on the jaw rim,
# bigger toward the front, leaning slightly forward so they read from the side
for s in (-1, 1):
    for k in range(7):
        y = -0.285 + k * 0.045
        z, hh, hw = head_at(y)
        size = 0.046 - k * 0.003
        base = V((s * hw * 0.7, y, z - hh * 0.6))
        head.spike(base, base + V((s * 0.004, -0.008, -size)), V((0, 1, 0)), size * 0.36, "M_CrocTeeth")
    for k in range(6):
        y = -0.25 + k * 0.05
        jz, jh, jw = head_at(y, JP)
        size = 0.036 - k * 0.002
        base = V((s * jw * 0.72, y, jaw_z(y, jz) + jh * 0.55))
        jaw.spike(base, base + V((s * 0.003, -0.006, size)), V((0, 1, 0)), size * 0.36, "M_CrocTeeth")
    # small flush black eye on the side, a little behind the mouth corner line
    z, hh, hw = head_at(-0.19)
    ez = z + hh * 0.22
    tube(head, [(s * hw * 0.86, -0.19, ez), (s * hw * 0.99, -0.19, ez)], [0.021, 0.017], 6, "M_SharkEye", ref=V((0, 0, 1)))
    # nostril slit under the snout
    z, hh, hw = head_at(-0.36)
    head.loft([[V((s * 0.022, -0.35, z - hh * 0.75)), V((s * 0.034, -0.32, z - hh * 0.8))],
               [V((s * 0.024, -0.35, z - hh * 0.75 - 0.004)), V((s * 0.036, -0.32, z - hh * 0.8 - 0.004))]], "M_CrocMouth", closed=False)
# front teeth at the tip of the U
for x in (-0.018, 0.018):
    z, hh, hw = head_at(-0.3)
    base = V((x, -0.3, z - hh * 0.6))
    head.spike(base, base + V((0, -0.01, -0.044)), V((0, 1, 0)), 0.016, "M_CrocTeeth")

# ---- tail: thick muscular stalk off the hips, then the crescent caudal fin (big upper lobe)
tail = P("Shark_Tail", "Hips")
TP = [(0.12, 0.92, 0.13, 0.12), (0.34, 0.8, 0.11, 0.09), (0.55, 0.64, 0.08, 0.06), (0.72, 0.5, 0.05, 0.035),
      (0.8, 0.44, 0.03, 0.022)]
tube(tail, [(0, y, z) for y, z, _, _ in TP], [(v, h) for _, _, v, h in TP], 6, "M_SharkSkin", ref=V((0, 0, 1)),
     phase=math.pi / 6, jag=0.04, seed=51, cap0=False)
fin(tail, V((0, 0.74, 0.5)), V((0, 0.82, 0.43)), V((0, 1.12, 0.86)), 0.016, "M_SharkSkin", back_curve=-0.08)   # upper lobe
fin(tail, V((0, 0.75, 0.47)), V((0, 0.82, 0.43)), V((0, 0.98, 0.2)), 0.014, "M_SharkSkin", back_curve=0.04)    # lower lobe
fin(tail, V((0, 0.42, 0.78)), V((0, 0.55, 0.69)), V((0, 0.6, 0.86)), 0.012, "M_SharkSkin")                     # second dorsal

# ---- arms: slate hide, pectoral fins sweeping back off the forearms like blades
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    sh = P(f"Shark_{L}Shoulder", L + "Shoulder")
    if s > 0:
        # pauldron made from a shark jaw: a bone arc ringed with teeth
        rings = []
        for x, r in ((0.17, 0.115), (0.25, 0.145), (0.33, 0.14), (0.39, 0.1)):
            rings.append([V((x, math.cos(a) * r * 0.95, SHOULDER_Z + 0.015 + math.sin(a) * r))
                          for a in (math.radians(d) for d in range(-35, 216, 28))])
        sh.loft(rings, "M_SharkBelly", closed=False)
        for k in range(7):
            a = math.radians(-20 + k * 30)
            base = V((0.39, math.cos(a) * 0.1 * 0.95, SHOULDER_Z + 0.015 + math.sin(a) * 0.1))
            sh.spike(base, base + V((0.05, 0, 0)) + (base - V((0.39, 0, SHOULDER_Z))).normalized() * 0.02, V((0, 0, 1)),
                     0.012, "M_CrocTeeth")
        tube(sh, [(0.2, -0.15, SHOULDER_Z - 0.02), (0.05, -0.2, SHOULDER_Z - 0.15)], [0.016, 0.016], 4, "M_Leather",
             ref=V((0, 0, 1)))   # strap across the chest
    else:
        tube(sh, [(-0.16, 0, SHOULDER_Z), (-0.3, 0, SHOULDER_Z)], [(0.11, 0.1), (0.1, 0.095)], 6, "M_SharkSkin",
             ref=V((0, 0, 1)), phase=math.pi / 6)
    ua = P(f"Shark_{L}UpperArm", L + "UpperArm")
    tube(ua, [(s * 0.27, 0, SHOULDER_Z), (s * 0.40, 0, SHOULDER_Z + 0.004), (s * 0.565, 0, SHOULDER_Z)],
         [(0.088, 0.084), (0.094, 0.088), (0.07, 0.066)], 6, "M_SharkSkin", ref=V((0, 0, 1)), phase=math.pi / 6,
         jag=0.04, seed=60 + s)
    la = P(f"Shark_{L}LowerArm", L + "LowerArm")
    tube(la, [(s * 0.525, 0, SHOULDER_Z), (s * 0.64, 0, SHOULDER_Z), (s * 0.77, 0, SHOULDER_Z)],
         [(0.074, 0.07), (0.07, 0.064), (0.052, 0.048)], 6, "M_SharkSkin", ref=V((0, 0, 1)), phase=math.pi / 6,
         jag=0.04, seed=70 + s)
    # pectoral fin: rooted along the back of the forearm, sweeping back past the elbow
    fin(la, V((s * 0.7, 0.05, SHOULDER_Z)), V((s * 0.58, 0.06, SHOULDER_Z)), V((s * 0.47, 0.25, SHOULDER_Z - 0.05)),
        0.012, "M_SharkSkin", back_curve=0.03)
    tube(la, [(s * 0.68, 0, SHOULDER_Z), (s * 0.75, 0, SHOULDER_Z)], [(0.064, 0.06), (0.058, 0.054)], 6,
         "M_Leather", ref=V((0, 0, 1)), phase=math.pi / 6, cap0=False, cap1=False)   # wrist wrap
    hand = P(f"Shark_{L}Hand", L + "Hand")
    tube(hand, [(s * 0.745, 0, SHOULDER_Z), (s * 0.79, 0, SHOULDER_Z - 0.003), (s * 0.84, 0.0, SHOULDER_Z - 0.005)],
         [(0.028, 0.048), (0.03, 0.055), (0.025, 0.052)], 6, "M_SharkSkin", ref=V((0, 0, 1)), phase=math.pi / 6)
    for fname, chain in finger_chains(s).items():
        if fname == "Little":
            continue
        for k, (bn, h, t) in enumerate(chain):
            fp = P(f"Shark_{bn}", bn)
            h, t = V(h), V(t)
            d = (t - h)
            r = 0.017 if fname != "Thumb" else 0.019
            tube(fp, [h - d.normalized() * 0.006, t + d.normalized() * 0.004], [r * (1 - 0.15 * k), r * (0.85 - 0.15 * k)],
                 4, "M_SharkSkin", ref=V((0, 0, 1)), phase=math.pi / 4)
            if k == 2:
                fp.spike(t, t + d.normalized() * 0.03 + V((0, 0, -0.01)), V((0, 0, 1)), 0.009, "M_CrocTeeth")

# ---- legs: lean hide over muscle, worn blue running shoes (the meme's sneakers, no brand: a plain stripe)
for s in (1, -1):
    L = "Left" if s > 0 else "Right"
    ul = P(f"Shark_{L}UpperLeg", L + "UpperLeg")
    tube(ul, [(s * HIP_X, 0.0, 0.96), (s * (HIP_X + 0.012), -0.005, 0.72), (s * HIP_X, 0, KNEE_Z - 0.03)],
         [(0.12, 0.125), (0.112, 0.108), (0.084, 0.082)], 7, "M_SharkSkin", ref=V((1, 0, 0)), jag=0.04, seed=80 + s,
         mats=counter_shade(7, 0.0, -0.7))
    ll = P(f"Shark_{L}LowerLeg", L + "LowerLeg")
    tube(ll, [(s * HIP_X, 0, KNEE_Z + 0.03), (s * HIP_X, 0.01, 0.31), (s * HIP_X, 0, ANKLE_Z + 0.04)],
         [(0.084, 0.084), (0.078, 0.074), (0.056, 0.054)], 6, "M_SharkSkin", ref=V((1, 0, 0)), jag=0.04, seed=90 + s)
    # sock cuff above the shoe
    tube(ll, [(s * HIP_X, 0, ANKLE_Z + 0.07), (s * HIP_X, 0, ANKLE_Z + 0.02)], [(0.06, 0.06), (0.062, 0.062)], 6,
         "M_SneakerSole", ref=V((1, 0, 0)), cap0=False, cap1=False)
    ft = P(f"Shark_{L}Foot", L + "Foot")
    X = s * HIP_X
    # sole slab (pale, tread texture) then the mesh upper, heel counter at the back
    ft.loft([[V((X + dx, 0.075, dz)) for dx, dz in ((-0.058, 0.0), (0.058, 0.0), (0.058, 0.035), (-0.058, 0.035))],
             [V((X + dx, -0.06, dz)) for dx, dz in ((-0.064, 0.0), (0.064, 0.0), (0.064, 0.035), (-0.064, 0.035))]],
            "M_SneakerSole")
    ft.loft([[V((X + dx, 0.07, dz)) for dx, dz in ((-0.054, 0.035), (0.054, 0.035), (0.05, 0.13), (-0.05, 0.13))],
             [V((X + dx, -0.06, dz)) for dx, dz in ((-0.06, 0.035), (0.06, 0.035), (0.05, 0.1), (-0.05, 0.1))]],
            "M_SneakerCloth")
    # side stripe: a plain pale swoosh-free band
    for side in (-1, 1):
        ft.loft([[V((X + side * 0.059, 0.05, 0.05)), V((X + side * 0.059, 0.05, 0.07))],
                 [V((X + side * 0.063, -0.05, 0.045)), V((X + side * 0.063, -0.05, 0.08))]], "M_SneakerSole", closed=False)
    toes = P(f"Shark_{L}Toes", L + "Toes")
    toes.loft([[V((X + dx, TOE_Y + 0.08, dz)) for dx, dz in ((-0.064, 0.0), (0.064, 0.0), (0.064, 0.035), (-0.064, 0.035))],
               [V((X + dx, TOE_END_Y + 0.01, dz)) for dx, dz in ((-0.05, 0.0), (0.05, 0.0), (0.045, 0.03), (-0.045, 0.03))]],
              "M_SneakerSole")
    toes.loft([[V((X + dx, TOE_Y + 0.08, dz)) for dx, dz in ((-0.06, 0.035), (0.06, 0.035), (0.05, 0.1), (-0.05, 0.1))],
               [V((X + dx, TOE_END_Y + 0.015, dz)) for dx, dz in ((-0.045, 0.03), (0.045, 0.03), (0.03, 0.055), (-0.03, 0.055))]],
              "M_SneakerCloth")
    # laces
    for k in range(3):
        toes.box((X, TOE_Y + 0.06 - k * 0.025, 0.105 - k * 0.012), (0.07, 0.008, 0.008), "M_SneakerSole")

shark_objs = [p.finish(smooth_angle=40) for p in parts]
select_only(shark_objs)
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "SharkForm"
body.data.name = "SharkForm_Mesh"

arm_data = bpy.data.armatures.new("SharkForm_Armature")
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
print("SharkForm tris", tris([body]), "bones", len(BONES))
export("SharkForm.fbx", [arm_obj, body], {'ARMATURE', 'MESH'})


def place(objs, dx):
    if EXPORT:
        return
    for o in objs:
        o.location.x += dx


# =================================================================================================
# LONG SWORD: grip at the origin, blade along +Z, cutting edge toward -X (faces away from the wielder once gripped).
# A nodachi: long two-hand handle wrapped in rayskin, a jaw-bone guard, a curved single-edged blade with a
# row of shark teeth along the spine.
# =================================================================================================
sw = Part("SharkSword")
# handle: rayskin (pale, the real samegawa is shark/ray skin) under a crossed leather wrap
tube(sw, [(0, 0, -0.34), (0, 0, 0.12)], [(0.026, 0.021), (0.025, 0.02)], 6, "M_SharkBelly", ref=V((1, 0, 0)),
     phase=math.pi / 6)
for k in range(6):
    z = -0.3 + k * 0.075
    tube(sw, [(0, 0, z), (0, 0, z + 0.03)], [(0.029, 0.024)] * 2, 6, "M_Leather", ref=V((1, 0, 0)), phase=math.pi / 6,
         cap0=False, cap1=False)
# pommel: one big serrated shark tooth
sw.spike((0, 0, -0.34), (0, 0, -0.46), V((1, 0, 0)), 0.032, "M_CrocTeeth", sides=3)
tube(sw, [(0, 0, -0.36), (0, 0, -0.33)], [0.03, 0.03], 6, "M_Iron", ref=V((1, 0, 0)))
# guard (tsuba): a ring of jaw bone with teeth pointing up the blade
tube(sw, [(0, 0, 0.115), (0, 0, 0.15)], [(0.075, 0.055), (0.075, 0.055)], 8, "M_SharkBelly", ref=V((1, 0, 0)),
     phase=math.pi / 8)
for k in range(8):
    a = k * math.pi / 4 + math.pi / 8
    base = V((math.cos(a) * 0.068, math.sin(a) * 0.05, 0.15))
    sw.spike(base, base + V((0, 0, 0.035)) + V((math.cos(a), math.sin(a), 0)) * 0.012, V((0, 0, 1)), 0.01, "M_CrocTeeth")
tube(sw, [(0, 0, 0.15), (0, 0, 0.19)], [(0.032, 0.016), (0.03, 0.014)], 4, "M_Iron", ref=V((1, 0, 0)))   # habaki collar

# blade: 5-point section (edge, bevel, spine, bevel), curving gently back toward the spine
BL0, BL1 = 0.19, 1.56
NB = 12
EDGE = -1                                       # the cutting edge faces -X: out, away from the wielder (in Unity)
rings = []
for i in range(NB + 1):
    t = i / NB
    z = BL0 + (BL1 - BL0) * t
    curve = -0.07 * t * t                      # sori: the blade bows back toward the spine (-X)
    w = 0.066 * (1 - 0.25 * t)                 # edge-to-spine width
    th = 0.011 * (1 - 0.35 * t)                # spine thickness
    if t > 0.9:                                 # kissaki: the tip sweeps up into a point on the edge side
        k = (t - 0.9) / 0.1
        w *= 1 - 0.85 * k
        th *= 1 - 0.8 * k
    xs = EDGE * (curve - w * 0.5)               # spine x
    xe = EDGE * (curve + w * 0.5)               # edge x
    xm = EDGE * (curve - w * 0.5 + w * 0.62)    # where the edge bevel meets the flat
    rings.append([V((xe, 0, z)), V((xm, th, z)), V((xs, th * 0.8, z)), V((xs, -th * 0.8, z)), V((xm, -th, z))])
tip = V((EDGE * -0.07, 0, BL1 + 0.05))


def blade_mats(i, j):
    if i < 0:
        return "M_SharkSteel"
    return "M_SharkEdge" if j in (0, 4) else "M_SharkSteel"


vr = sw.loft(rings, "M_SharkSteel", cap0=True, cap1=False, mats=blade_mats)
tv = sw.v(tip)
last = vr[-1]
for j in range(5):
    sw.face([last[j], last[(j + 1) % 5], tv], "M_SharkEdge" if j in (0, 4) else "M_SharkSteel")
# shark teeth serrating the spine (pointing back toward the guard, like a saw)
for k in range(9):
    t = 0.1 + k * 0.075
    z = BL0 + (BL1 - BL0) * t
    curve = -0.07 * t * t
    w = 0.066 * (1 - 0.25 * t)
    base = V((EDGE * (curve - w * 0.5 + 0.004), 0, z))
    sw.spike(base, base + V((EDGE * -0.031, 0, -0.024)), V((0, 1, 0)), 0.0121, "M_CrocTeeth", sides=3)
sword = sw.finish(smooth_angle=25)
place([sword], 1.4)
export("SharkSword.fbx", [sword], {'MESH'})

# =================================================================================================
# CROSSBOW: grip at the origin, bolt flies along -Y (Unity +Z). Stock of dark wood wrapped in shark hide;
# the prod is a crescent tail fin; a shark jaw rings the bolt's exit; a crank on the side. "LoadedBolt" is
# its own object (shown only while a bolt sits in the groove); "Muzzle" marks where bolts leave.
# =================================================================================================
cb = Part("SharkCrossbow")
BZ = 0.085   # rail height above the grip
# stock: butt behind the shoulder (+Y), tapering to the fore-end
ST = [(0.42, 0.055, 0.075), (0.3, 0.045, 0.07), (0.12, 0.035, 0.05), (-0.1, 0.03, 0.035), (-0.42, 0.028, 0.032)]
tube(cb, [(0, y, BZ - 0.035 - (0.05 if y > 0.2 else 0.0)) for y, _, _ in ST], [(h, w) for _, w, h in ST], 6, "M_DarkWood",
     ref=V((0, 0, 1)), phase=math.pi / 6)
# hide wrap over the fore-end
tube(cb, [(0, -0.06, BZ - 0.033), (0, -0.3, BZ - 0.033)], [(0.04, 0.036), (0.038, 0.034)], 6, "M_SharkSkin",
     ref=V((0, 0, 1)), phase=math.pi / 6, cap0=False, cap1=False)
# pistol grip + trigger guard
cb.box((0, 0.02, 0.0), (0.034, 0.05, 0.13), "M_DarkWood", rot=None)
tube(cb, [(0, 0.075, BZ - 0.06), (0, 0.075, BZ - 0.12), (0, 0.005, BZ - 0.125)], [0.01] * 3, 4, "M_Iron", ref=V((1, 0, 0)))
# rail with the bolt groove (iron)
cb.box((0, -0.24, BZ), (0.026, 0.5, 0.012), "M_Iron")
# prod: a crescent caudal fin across the front, thick at the middle, tapering to the tips
PRY, PRZ = -0.44, BZ + 0.005
for s in (-1, 1):
    pts = [(0, PRY, PRZ), (s * 0.12, PRY + 0.005, PRZ + 0.004), (s * 0.24, PRY + 0.04, PRZ + 0.01), (s * 0.33, PRY + 0.11, PRZ + 0.02),
           (s * 0.37, PRY + 0.17, PRZ + 0.03)]
    tube(cb, pts, [(0.032, 0.012), (0.03, 0.011), (0.022, 0.009), (0.013, 0.007), (0.004, 0.004)], 5, "M_SharkSkin",
         ref=V((0, 0, 1)), phase=math.pi / 5, cap0=False)
    # trailing fin web behind each arm (the fin's soft edge)
    fin(cb, V((s * 0.06, PRY + 0.015, PRZ)), V((s * 0.3, PRY + 0.1, PRZ + 0.018)), V((s * 0.2, PRY - 0.05, PRZ + 0.008)),
        0.004, "M_SharkBelly")
# string: drawn back to the nut (cocked)
NUT_Y = -0.02
for s in (-1, 1):
    a, b = V((s * 0.37, PRY + 0.17, PRZ + 0.03)), V((s * 0.012, NUT_Y, BZ + 0.012))
    tube(cb, [a, b], [0.0045, 0.0045], 3, "M_BowString", ref=V((0, 0, 1)), cap0=False, cap1=False)
cb.box((0, NUT_Y + 0.01, BZ + 0.012), (0.03, 0.025, 0.02), "M_Iron")   # nut / latch
# shark jaw around the bolt's exit: upper and lower tooth rows
for s in (-1, 1):
    for k in range(4):
        x = s * (0.012 + k * 0.012)
        cb.spike((x, PRY - 0.06, BZ + 0.04 - k * 0.006), (x, PRY - 0.075, BZ + 0.012), V((0, 1, 0)), 0.006, "M_CrocTeeth")
        cb.spike((x, PRY - 0.06, BZ - 0.028 + k * 0.004), (x, PRY - 0.075, BZ - 0.002), V((0, 1, 0)), 0.006, "M_CrocTeeth")
tube(cb, [(0, PRY - 0.02, BZ + 0.006), (0, PRY - 0.07, BZ + 0.006)], [(0.05, 0.045), (0.042, 0.04)], 8, "M_SharkBelly",
     ref=V((0, 0, 1)), cap0=False, cap1=False)
# crank wheel on the right side
tube(cb, [(-0.045, 0.1, BZ - 0.02), (-0.06, 0.1, BZ - 0.02)], [0.04, 0.04], 8, "M_Iron", ref=V((0, 1, 0)))
cb.box((-0.07, 0.1, BZ + 0.015), (0.012, 0.012, 0.07), "M_Iron")
# a dorsal fin standing on the stock (sight)
fin(cb, V((0, 0.1, BZ + 0.0)), V((0, 0.2, BZ - 0.01)), V((0, 0.21, BZ + 0.08)), 0.005, "M_SharkSkin")
crossbow = cb.finish(smooth_angle=30)
# the bolt that sits in the groove
lb = Part("LoadedBolt")
tube(lb, [(0, NUT_Y - 0.005, BZ + 0.018), (0, PRY - 0.05, BZ + 0.018)], [0.009, 0.009], 5, "M_DarkWood", ref=V((0, 0, 1)))
lb.loft([[V((-0.018, PRY - 0.05, BZ + 0.018)), V((0.018, PRY - 0.05, BZ + 0.018))],
         [V((0, PRY - 0.12, BZ + 0.018)), V((0, PRY - 0.12, BZ + 0.019))]], "M_CrocTeeth", closed=False)
lb.loft([[V((0.018, PRY - 0.05, BZ + 0.017)), V((-0.018, PRY - 0.05, BZ + 0.017))],
         [V((0, PRY - 0.12, BZ + 0.0175)), V((0, PRY - 0.12, BZ + 0.0165))]], "M_CrocTeeth", closed=False)
loaded = lb.finish(smooth_angle=30)
loaded.parent = crossbow
cmuz = bpy.data.objects.new("Muzzle", None)
scene.collection.objects.link(cmuz)
cmuz.location = (0, PRY - 0.13, BZ + 0.018)
cmuz.parent = crossbow
place([crossbow], 2.4)
export("SharkCrossbow.fbx", [crossbow, loaded, cmuz])

# =================================================================================================
# BOLT (projectile): origin at the nock, flies along -Y. Serrated tooth head, fin fletching.
# =================================================================================================
bt = Part("SharkBolt")
BLEN = 0.6
tube(bt, [(0, 0, 0), (0, -BLEN + 0.07, 0)], [0.0095, 0.0095], 5, "M_DarkWood", ref=V((0, 0, 1)))
for flip in (1, -1):
    pts = [V((-0.022, -BLEN + 0.075, 0)), V((-0.012, -BLEN + 0.03, 0)), V((0, -BLEN - 0.005, 0)),
           V((0.012, -BLEN + 0.03, 0)), V((0.022, -BLEN + 0.075, 0))]
    off = V((0, 0, 0.003 * flip))
    vs = [bt.v(p + off) for p in pts]
    c = bt.v(V((0, -BLEN + 0.07, 0.0)) + off * 2)
    for k in range(4):
        bt.face([vs[k], vs[k + 1], c] if flip > 0 else [vs[k + 1], vs[k], c], "M_CrocTeeth")
# serration nicks on the head edges
for s in (-1, 1):
    for k in range(2):
        bt.spike((s * (0.016 - k * 0.006), -BLEN + 0.05 - k * 0.02, 0), (s * (0.024 - k * 0.006), -BLEN + 0.06 - k * 0.02, 0),
                 V((0, 0, 1)), 0.003, "M_CrocTeeth")
for k in range(3):
    a = k * 2 * math.pi / 3
    d = V((math.cos(a), 0, math.sin(a)))
    fin(bt, V((0, 0.0, 0)) + d * 0.008, V((0, -0.11, 0)) + d * 0.008, V((0, 0.01, 0)) + d * 0.04, 0.0025, "M_SharkSkin")
bolt = bt.finish(smooth_angle=30)
place([bolt], 3.2)
export("SharkBolt.fbx", [bolt], {'MESH'})

# =================================================================================================
# MASK: front along -Y, origin at the face centre. Slate hide, blunt snout, a lower row of teeth, little
# dead eyes, gill slits on the cheeks and a dorsal fin crest on top.
# =================================================================================================
mk = Part("SharkMask")
face_rings = []
for z, r, y in ((-0.13, 0.11, 0.0), (-0.06, 0.15, -0.02), (0.03, 0.16, -0.035), (0.11, 0.14, -0.025), (0.17, 0.09, 0.0)):
    face_rings.append([V((math.cos(a) * r, y + math.sin(a) * r * 0.75, z)) for a in (math.radians(d) for d in range(185, 356, 17))])
mk.loft(face_rings, "M_SharkSkin", closed=False)
MS = [(-0.1, 0.03, 0.08, 0.1), (-0.19, 0.03, 0.06, 0.075), (-0.25, 0.025, 0.035, 0.045), (-0.28, 0.02, 0.012, 0.015)]
tube(mk, [(0, y, z) for y, z, _, _ in MS], [(v, h) for _, _, v, h in MS], 6, "M_SharkSkin", ref=V((0, 0, 1)),
     phase=math.pi / 6, cap0=False)
# mouth: a dark crescent under the snout with the lower teeth standing in it
mk.loft([[V((-0.1, -0.13, -0.05)), V((0.1, -0.13, -0.05))], [V((-0.07, -0.18, -0.03)), V((0.07, -0.18, -0.03))]],
        "M_CrocMouth", closed=False)
for s in (-1, 1):
    tooth_row(mk, [(s * (0.085 - k * 0.022), -0.14 - k * 0.012, -0.06) for k in range(4)], V((0, -0.2, 1)), 0.035)
    tube(mk, [(s * 0.09, -0.14, 0.05), (s * 0.095, -0.15, 0.05)], [(0.018, 0.024), (0.014, 0.02)], 6, "M_SharkEye",
         ref=V((0, 0, 1)))
    for k in range(3):
        x = s * (0.135 - k * 0.006)
        mk.loft([[V((x, -0.08 + k * 0.03, 0.03)), V((x, -0.08 + k * 0.03, -0.06))],
                 [V((x + s * 0.004, -0.085 + k * 0.03, 0.03)), V((x + s * 0.004, -0.085 + k * 0.03, -0.06))]], "M_CrocMouth", closed=False)
    mk.loft([[V((s * 0.155, 0.0, 0.0)), V((s * 0.155, 0.0, 0.05))], [V((s * 0.16, 0.08, 0.0)), V((s * 0.16, 0.08, 0.05))]],
            "M_Leather", closed=False)
fin(mk, V((0, -0.06, 0.16)), V((0, 0.06, 0.15)), V((0, 0.13, 0.32)), 0.01, "M_SharkSkin", back_curve=-0.03)
mask = mk.finish(smooth_angle=35)
place([mask], 3.8)
export("SharkMask.fbx", [mask], {'MESH'})

# =================================================================================================
# VINES: three hanging segments (0.5 m, origin at the top, hanging down -Z), a mossy lintel on two posts
# for the curtain, and a root mound for the thickets. Segments chain in Unity into strands.
# =================================================================================================
vine_objs = []
SEG = 0.5
for i in range(3):
    vp = Part(f"VineSegment{i}")
    pts = []
    for k in range(5):
        z = -SEG * k / 4
        pts.append((0.022 * math.sin(k * 1.7 + i * 2.1), 0.022 * math.cos(k * 1.3 + i), z))
    pts[0] = (0, 0, 0.02)
    pts[-1] = (0, 0, -SEG - 0.02)
    tube(vp, pts, [0.03, 0.032, 0.028, 0.031, 0.03], 5, "M_Vine", ref=V((1, 0, 0)), jag=0.15, seed=200 + i)
    # leaves: drooping diamonds, both faces
    for k in range(3 + i % 2):
        z = -0.08 - k * 0.13
        a = k * 2.3 + i * 1.1
        d = V((math.cos(a), math.sin(a), 0))
        base = V((0, 0, z)) + d * 0.025
        l1 = base + d * 0.09 + V((0, 0, -0.03))
        l2 = base + d * 0.17 + V((0, 0, -0.1))
        side = d.cross(V((0, 0, 1))).normalized() * 0.045
        quad = [base, l1 + side, l2, l1 - side]
        vp.face([vp.v(q) for q in quad], "M_VineLeaf")
        vp.face([vp.v(q) for q in reversed(quad)], "M_VineLeaf")
    # thorns
    for k in range(4):
        a = k * 1.9 + i
        d = V((math.cos(a), math.sin(a), 0))
        z = -0.05 - k * 0.11
        vp.spike(V((0, 0, z)) + d * 0.025, V((0, 0, z - 0.02)) + d * 0.065, V((0, 0, 1)), 0.008, "M_CrocTeeth")
    o = vp.finish(smooth_angle=40)
    o.location.x += (i * 0.4 if not EXPORT else 0)
    vine_objs.append(o)

bm_ = Part("VineBeam")
for s in (-1, 1):
    tube(bm_, [(s * 1.95, 0, 0), (s * 1.93, 0.02, 1.4), (s * 1.95, 0, 2.95)], [0.13, 0.115, 0.11], 6, "M_DarkWood",
         ref=V((1, 0, 0)), jag=0.12, seed=220 + s)
tube(bm_, [(-2.25, 0, 2.8), (0, 0.03, 2.84), (2.25, 0, 2.8)], [(0.12, 0.14), (0.13, 0.15), (0.12, 0.14)], 6, "M_DarkWood",
     ref=V((0, 0, 1)), jag=0.12, seed=230)
# moss clumps draped on the beam
for k in range(7):
    x = -1.9 + k * 0.63
    tube(bm_, [(x - 0.15, 0, 2.95), (x, 0.02, 2.99), (x + 0.15, 0, 2.95)], [0.06, 0.08, 0.06], 5, "M_VineLeaf",
         ref=V((0, 0, 1)), jag=0.3, seed=240 + k)
beam = bm_.finish(smooth_angle=30)
vine_objs.append(beam)

mp = Part("ThicketMound")
rr = []
for i in range(4):
    t = i / 3
    r = 0.9 * (1 - t * t * 0.75)
    z = 0.28 * math.sin(t * math.pi * 0.5)
    rr.append([V((math.cos(a) * r * (1 + 0.18 * noise.noise(V((i, j * 1.3, 7.0)))), math.sin(a) * r * 0.85, z))
               for j, a in enumerate(2 * math.pi * k / 9 for k in range(9))])
mp.loft(rr, "M_Bark", cap0=False)
# gnarled roots spreading out
for k in range(5):
    a = k * 2 * math.pi / 5 + 0.4
    d = V((math.cos(a), math.sin(a), 0))
    tube(mp, [d * 0.4 + V((0, 0, 0.18)), d * 0.85 + V((0, 0, 0.08)), d * 1.2 + V((0, 0, -0.02))], [0.07, 0.05, 0.02], 5,
         "M_Bark", ref=V((0, 0, 1)), jag=0.2, seed=260 + k)
mound = mp.finish(smooth_angle=30)
vine_objs.append(mound)
place(vine_objs, -3.0)
export("Vines.fbx", vine_objs, {'MESH'})
print("SHARK SET OK", "out" if EXPORT else "(live, no export)", OUT)
