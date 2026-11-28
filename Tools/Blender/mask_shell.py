"""
Shared shape for the four transformation masks (Crocodile, Shark, Druid, Fairy), after Majora's Mask: a big,
thick face plate that covers the whole face and reaches past the cheeks, painted on the outside, bare carved wood
on the inside and around the rim, held on by two straps.

Used by build_croc_form.py, build_shark_form.py, build_druid_form.py and build_fairy_form.py. Each script builds
its own Part (bmesh) class; the shell only needs part.v(co) and part.face(verts, mat), so it fits all of them.

Conventions (same as the form scripts): Blender Z up, the mask's front looks along -Y, origin = face centre,
its LEFT side is +X. The wood material is M_MaskWood (remapped by name in Unity, built by
ElderTreeBuilder.MaskModels.cs); its UVs come from the scripts' box projection in metres (TILE), so the grain runs
up the inside of the mask.
"""
import math
from mathutils import Vector

V = Vector

WOOD = "M_MaskWood"
WOOD_RGB = (0.36, 0.24, 0.14)
WOOD_TILE = 0.32

# half width of the plate (metres) at a height fraction v (0 = chin, 1 = crown): a broad heart / shield
HEART = [(0.0, 0.045), (0.08, 0.10), (0.22, 0.150), (0.42, 0.180), (0.62, 0.192), (0.80, 0.186), (0.92, 0.160), (1.0, 0.105)]


def register(mat_rgb, tile):
    """Adds the wood to a form script's material tables."""
    mat_rgb.setdefault(WOOD, WOOD_RGB)
    tile.setdefault(WOOD, WOOD_TILE)


def _lerp_profile(prof, v):
    v = min(max(v, prof[0][0]), prof[-1][0])
    for (a, wa), (b, wb) in zip(prof, prof[1:]):
        if a <= v <= b:
            t = (v - a) / max(1e-6, b - a)
            t = t * t * (3 - 2 * t)
            return wa + (wb - wa) * t
    return prof[-1][1]


class Shell:
    """The face plate. profile: (v, half width) pairs; z0..z1: chin to crown; y0: how far the centre stands in front
    of the face; wrap / crown: how strongly it curves back toward the cheeks and over the brow and chin."""

    def __init__(self, profile=None, z0=-0.23, z1=0.21, width=1.0, y0=-0.065, wrap=2.3, crown=0.85, zc=0.0,
                 thick=0.017, nu=12, nv=11):
        self.prof = profile or HEART
        self.z0, self.z1, self.width = z0, z1, width
        self.y0, self.wrap, self.crown, self.zc = y0, wrap, crown, zc
        self.thick, self.nu, self.nv = thick, nu, nv

    # ---- geometry queries (for placing features on the plate)
    def hw(self, z):
        return _lerp_profile(self.prof, (z - self.z0) / (self.z1 - self.z0)) * self.width

    def y(self, x, z):
        return self.y0 + self.wrap * x * x + self.crown * (z - self.zc) ** 2

    def normal(self, x, z):
        """Outward (front-facing) normal of the plate."""
        return V((2 * self.wrap * x, -1.0, 2 * self.crown * (z - self.zc))).normalized()

    def at(self, x, z, off=0.0):
        """Point on the front surface at (x, z), lifted off it by off along the normal."""
        return V((x, self.y(x, z), z)) + self.normal(x, z) * off

    def rim(self, v, side):
        """Point on the outer edge at height fraction v on one side (+1 = left / +X)."""
        z = self.z0 + (self.z1 - self.z0) * v
        return self.at(side * self.hw(z), z)

    # ---- mesh
    def build(self, part, outer, inner=WOOD, strap=None, strap_v=0.58):
        """outer: material name, or fn(x, z) -> material (e.g. a pale lower jaw). strap: material of the side straps."""
        nu, nv = self.nu, self.nv
        mat_at = outer if callable(outer) else (lambda x, z: outer)
        F, B, P = [], [], []
        for j in range(nv + 1):
            z = self.z0 + (self.z1 - self.z0) * j / nv
            h = self.hw(z)
            rf, rb, rp = [], [], []
            for i in range(nu + 1):
                u = -1.0 + 2.0 * i / nu
                # columns bunch toward the edge a little so the wrap-around stays smooth at PSX density
                u = math.copysign(abs(u) ** 0.85, u)
                x = u * h
                p = V((x, self.y(x, z), z))
                n = self.normal(x, z)
                rp.append(p)
                rf.append(part.v(p))
                rb.append(part.v(p - n * self.thick))
            F.append(rf)
            B.append(rb)
            P.append(rp)
        for j in range(nv):
            for i in range(nu):
                c = (P[j][i] + P[j][i + 1] + P[j + 1][i + 1] + P[j + 1][i]) / 4
                # front: x then z gives a -Y normal; the back runs the other way round
                part.face([F[j][i], F[j][i + 1], F[j + 1][i + 1], F[j + 1][i]], mat_at(c.x, c.z))
                part.face([B[j][i], B[j + 1][i], B[j + 1][i + 1], B[j][i + 1]], inner)
        # rim: walk the border and close front to back in wood
        loop = [(0, i) for i in range(nu + 1)] + [(j, nu) for j in range(1, nv + 1)] + \
               [(nv, i) for i in range(nu - 1, -1, -1)] + [(j, 0) for j in range(nv - 1, 0, -1)]
        centre = V((0.0, 0.0, (self.z0 + self.z1) * 0.5))
        for k in range(len(loop)):
            (ja, ia), (jb, ib) = loop[k], loop[(k + 1) % len(loop)]
            fa, fb, bb, ba = F[ja][ia], F[jb][ib], B[jb][ib], B[ja][ia]
            mid = (P[ja][ia] + P[jb][ib]) * 0.5
            out = V((mid.x - centre.x, 0.0, mid.z - centre.z))
            nrm = (fb.co - fa.co).cross(ba.co - fa.co)
            quad = [fa, ba, bb, fb] if nrm.dot(out) >= 0 else [fa, fb, bb, ba]
            part.face(quad, inner)
        if strap:
            for s in (-1, 1):
                self.strap(part, s, strap, strap_v)

    def strap(self, part, side, mat, v=0.58, length=0.11, width=0.045, thick=0.008):
        """A short strap running back from the inside of the rim (the rest is hidden by the head)."""
        z = self.z0 + (self.z1 - self.z0) * v
        x = side * (self.hw(z) - 0.012)
        p = V((x, self.y(x, z) + self.thick, z))
        back = V((side * 0.25, 1.0, 0.0)).normalized()
        a0, a1 = p + V((0, 0, -width / 2)), p + V((0, 0, width / 2))
        b0, b1 = a0 + back * length, a1 + back * length
        t = V((side * thick, 0, 0))
        vs = [part.v(q) for q in (a0, a1, b1, b0, a0 + t, a1 + t, b1 + t, b0 + t)]
        for q in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 3, 7, 4), (1, 5, 6, 2), (3, 2, 6, 7), (0, 4, 5, 1)):
            part.face([vs[i] for i in q], mat)


# ---------------------------------------------------------------------------------------------- features
def disc(part, c, n, r, mat, sides=7, depth=0.0, squash=1.0, roll=0.0):
    """Flat or shallow-coned disc facing n (an eye, a socket, a gem setting)."""
    n = V(n).normalized()
    s1 = n.cross(V((0, 0, 1)) if abs(n.z) < 0.9 else V((1, 0, 0))).normalized()
    s2 = n.cross(s1).normalized()
    ring = [part.v(V(c) + (s1 * math.cos(k * 2 * math.pi / sides + roll) + s2 * math.sin(k * 2 * math.pi / sides + roll) * squash) * r)
            for k in range(sides)]
    mid = part.v(V(c) + n * depth)
    for k in range(sides):
        face_toward(part, [ring[(k + 1) % sides], ring[k], mid], mat, n)


def face_toward(part, verts, mat, want):
    """Adds a face wound so its normal points along want (the druid and fairy scripts don't recalc normals)."""
    nrm = (verts[1].co - verts[0].co).cross(verts[-1].co - verts[0].co)
    if nrm.dot(want) < 0:
        verts = list(reversed(verts))
    return part.face(verts, mat)


def socket(part, shell, x, z, r, hollow, eye, eye_r=None, squash=1.0, tilt=0.0, glow_out=0.004):
    """A carved eye: a dark sunken socket in the plate with a glowing eye floating in it."""
    n = shell.normal(x, z)
    disc(part, shell.at(x, z, 0.0025), n, r, hollow, sides=8, depth=-0.006, squash=squash, roll=tilt)
    disc(part, shell.at(x, z, glow_out), n, eye_r or r * 0.45, eye, sides=6, depth=0.002, squash=squash, roll=tilt)


def band(part, shell, pts, width, mat, off=0.003):
    """A painted strip following the plate (a mouth, a gill slit): pts are (x, z) along its middle."""
    left, right = [], []
    for k, (x, z) in enumerate(pts):
        a = pts[max(0, k - 1)]
        b = pts[min(len(pts) - 1, k + 1)]
        d = V((b[0] - a[0], 0, b[1] - a[1]))
        d = d.normalized() if d.length > 1e-6 else V((1, 0, 0))
        perp = V((-d.z, 0, d.x))
        w = width[k] if isinstance(width, (list, tuple)) else width
        left.append(part.v(shell.at(x + perp.x * w / 2, z + perp.z * w / 2, off)))
        right.append(part.v(shell.at(x - perp.x * w / 2, z - perp.z * w / 2, off)))
    for k in range(len(pts) - 1):
        x, z = pts[k]
        face_toward(part, [right[k], right[k + 1], left[k + 1], left[k]], mat, shell.normal(x, z))


def rim_spikes(part, shell, vs, length, width, mat, lean=0.25, sides=4, centre=True):
    """Majora-style spikes standing out of the edge of the plate at height fractions vs (both sides)."""
    out = []
    for v in vs:
        for s in (-1, 1):
            base = shell.rim(v, s) + shell.normal(shell.rim(v, s).x, shell.rim(v, s).z) * 0.002
            cz = (shell.z0 + shell.z1) * 0.5
            d = V((base.x, 0.0, base.z - cz)).normalized()
            tip = base + (d + V((0, lean, 0))).normalized() * length
            out.append((base, tip))
    if centre:
        base = shell.at(0.0, shell.z1 - 0.004)
        out.append((base, base + V((0, lean, 1)).normalized() * length * 1.25))
    for base, tip in out:
        _spike(part, base, tip, width, mat, sides)


def _spike(part, base, tip, width, mat, sides=4):
    base, tip = V(base), V(tip)
    axis = (tip - base).normalized()
    s1 = V((0, 1, 0)) - axis * axis.y
    if s1.length < 1e-5:
        s1 = axis.orthogonal()
    s1.normalize()
    s2 = axis.cross(s1).normalized()
    ring = [part.v(base + (s1 * math.cos(k * 2 * math.pi / sides) + s2 * math.sin(k * 2 * math.pi / sides)) * width)
            for k in range(sides)]
    t = part.v(tip)
    for k in range(sides):
        part.face([ring[k], ring[(k + 1) % sides], t], mat)
    part.face(list(reversed(ring)), mat)
