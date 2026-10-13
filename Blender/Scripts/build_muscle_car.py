"""
Stylized Muscle Car generator for the Vehicle Damage Lab.

Run headless:
    blender -b --factory-startup --python build_muscle_car.py -- <UnityProjectRoot> [--render <previewDir>]

Produces:
    <root>/Blender/MuscleCar.blend
    <root>/Assets/DamageLab/Art/Models/MuscleCar.fbx
    <root>/Assets/DamageLab/Art/Textures/CarPaint_Stripes.png

Design notes
------------
* All geometry is authored in "car space": x = right, y = forward, z = up (metres), and the
  car is left facing +Y. Unity's FBX importer maps Blender +Y to Unity +Z (forward) and
  Blender +X to Unity +X (right), so the vehicle imports correctly oriented with an identity root.
* The body shell is a loft of ~190 cross-sections x 158 points, giving an even quad grid
  (~3-4 cm spacing) so procedural dents and folds have enough vertices to read well.
  Wheel wells are part of the same continuous grid (the section contour folds up into a
  well where an arch is present), so there are no boolean seams or n-gons on the shell.
* The cabin is lofted from horizontal slices so window edges follow grid lines.
* Details (grille, lights, bumpers, mirrors...) are generated from dense rounded boxes,
  sweeps and lathes so they bend with the body instead of moving as rigid slabs.
* The racing stripes come from a 1D texture sampled by a top-down planar UV (u = lateral
  position), so the stripes stay attached to the metal when the mesh is deformed.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PROJECT = os.path.abspath(argv[0]) if argv else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RENDER_DIR = argv[argv.index("--render") + 1] if "--render" in argv else None

BLEND_PATH = os.path.join(PROJECT, "Blender", "MuscleCar.blend")
FBX_PATH = os.path.join(PROJECT, "Assets", "DamageLab", "Art", "Models", "MuscleCar.fbx")
TEX_PATH = os.path.join(PROJECT, "Assets", "DamageLab", "Art", "Textures", "CarPaint_Stripes.png")
for p in (BLEND_PATH, FBX_PATH, TEX_PATH):
    os.makedirs(os.path.dirname(p), exist_ok=True)

# ----------------------------------------------------------------------------- math
def clamp(x, a, b): return a if x < a else b if x > b else x
def lerp(a, b, t): return a + (b - a) * t
def smoothstep(e0, e1, x):
    t = clamp((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)

def curve(keys, x):
    """Monotone-ish cubic Hermite through (x, y) keys, clamped at the ends."""
    if x <= keys[0][0]: return keys[0][1]
    if x >= keys[-1][0]: return keys[-1][1]
    n = len(keys)
    def slope(j):
        if j == 0: return (keys[1][1] - keys[0][1]) / (keys[1][0] - keys[0][0])
        if j == n - 1: return (keys[j][1] - keys[j - 1][1]) / (keys[j][0] - keys[j - 1][0])
        return (keys[j + 1][1] - keys[j - 1][1]) / (keys[j + 1][0] - keys[j - 1][0])
    for i in range(n - 1):
        x0, y0 = keys[i]; x1, y1 = keys[i + 1]
        if x0 <= x <= x1:
            h = x1 - x0; t = (x - x0) / h
            m0 = slope(i) * h; m1 = slope(i + 1) * h
            t2 = t * t; t3 = t2 * t
            return (2*t3 - 3*t2 + 1)*y0 + (t3 - 2*t2 + t)*m0 + (-2*t3 + 3*t2)*y1 + (t3 - t2)*m1
    return keys[-1][1]

# ----------------------------------------------------------------------------- mesh builder
class MB:
    """Tiny mesh accumulator: verts + (indices, material, smooth) faces."""
    def __init__(self):
        self.v = []; self.f = []
    def vert(self, p):
        self.v.append((float(p[0]), float(p[1]), float(p[2]))); return len(self.v) - 1
    def face(self, idx, mat, smooth=True):
        self.f.append((tuple(idx), mat, smooth))
    def grid(self, rings, mat, smooth=True, close=False):
        ids = [[self.vert(p) for p in ring] for ring in rings]
        n = len(rings[0])
        for i in range(len(ids) - 1):
            a, b = ids[i], ids[i + 1]
            for j in range(n if close else n - 1):
                j2 = (j + 1) % n
                self.face((a[j], a[j2], b[j2], b[j]), mat, smooth)
        return ids

# ----------------------------------------------------------------------------- materials
MATS = {}
def make_mat(name, color, metallic=0.0, rough=0.5, emission=None, estrength=0.0, coat=0.0, image=None):
    m = bpy.data.materials.new(name)
    try: m.use_nodes = True
    except Exception: pass
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    if coat and "Coat Weight" in bsdf.inputs:
        bsdf.inputs["Coat Weight"].default_value = coat
        bsdf.inputs["Coat Roughness"].default_value = 0.05
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = estrength
    if image is not None:
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = image
        tex.extension = 'EXTEND'
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    m.diffuse_color = (*color, 1.0)
    MATS[name] = m
    return m

def srgb_to_lin(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

PAINT_BLUE = (0.11, 0.38, 0.88)   # sRGB
STRIPE_WHITE = (0.93, 0.94, 0.95)
STRIPE_INNER, STRIPE_OUTER = 0.045, 0.255   # |x| range of each racing stripe (m)
UV_HALF_WIDTH, UV_LENGTH = 1.0, 4.6

def make_stripe_texture():
    w, h = 1024, 4
    img = bpy.data.images.new("CarPaint_Stripes", w, h, alpha=False)
    px = []
    ss = 8
    row = []
    for i in range(w):
        cov = 0.0
        for s in range(ss):
            u = (i + (s + 0.5) / ss) / w
            x = abs((u - 0.5) * 2 * UV_HALF_WIDTH)
            cov += 1.0 if STRIPE_INNER <= x <= STRIPE_OUTER else 0.0
        cov /= ss
        c = [lerp(PAINT_BLUE[k], STRIPE_WHITE[k], cov) for k in range(3)]
        row += [c[0], c[1], c[2], 1.0]
    for _ in range(h): px += row
    img.pixels = px
    img.filepath_raw = TEX_PATH
    img.file_format = 'PNG'
    img.save()
    return img

def build_materials():
    img = make_stripe_texture()
    make_mat("Paint", srgb_to_lin(PAINT_BLUE), 0.25, 0.22, coat=0.8, image=img)
    make_mat("Chrome", (0.85, 0.86, 0.88), 1.0, 0.1)
    make_mat("BlackTrim", (0.015, 0.015, 0.017), 0.0, 0.45)
    make_mat("Glass", (0.012, 0.016, 0.024), 0.0, 0.04, coat=1.0)
    make_mat("Tire", (0.022, 0.022, 0.024), 0.0, 0.85)
    make_mat("Rim", (0.08, 0.085, 0.09), 0.9, 0.32)
    make_mat("Headlight", (1.0, 0.95, 0.82), 0.0, 0.1, emission=(1.0, 0.92, 0.75), estrength=3.0)
    make_mat("Taillight", (0.75, 0.02, 0.02), 0.0, 0.15, emission=(1.0, 0.05, 0.03), estrength=1.5)
    make_mat("Indicator", (1.0, 0.38, 0.02), 0.0, 0.2, emission=(1.0, 0.35, 0.02), estrength=1.0)
    make_mat("Underbody", (0.035, 0.036, 0.04), 0.2, 0.75)
    make_mat("Caliper", (0.72, 0.04, 0.03), 0.1, 0.35)
    make_mat("DarkMetal", (0.05, 0.05, 0.055), 0.8, 0.45)

# ----------------------------------------------------------------------------- scene / objects
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)

COLL = None
def build_object(name, mb, parent, weld=False, recalc=True, mat_fn=None, location=(0, 0, 0), uv_u=None):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bv = [bm.verts.new(Vector(p)) for p in mb.v]
    bm.verts.ensure_lookup_table()
    names = []
    def mat_index(m):
        if m not in names: names.append(m)
        return names.index(m)
    for idx, mat, smooth in mb.f:
        if len(set(idx)) < 3: continue
        try:
            fc = bm.faces.new([bv[i] for i in idx])
        except ValueError:
            continue
        fc.material_index = mat_index(mat); fc.smooth = smooth
    if weld:
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    if mat_fn:
        for fc in bm.faces:
            m = mat_fn(fc.calc_center_median(), fc.normal, names[fc.material_index])
            if m: fc.material_index = mat_index(m)
    uvl = bm.loops.layers.uv.new("UVMap")
    off = Vector(location)
    for fc in bm.faces:
        for l in fc.loops:
            co = l.vert.co + off
            l[uvl].uv = (0.5 + co.x / (2 * UV_HALF_WIDTH) if uv_u is None else uv_u, 0.5 + co.y / UV_LENGTH)
    bm.to_mesh(me); bm.free()
    for m in names: me.materials.append(MATS[m])
    ob = bpy.data.objects.new(name, me)
    COLL.objects.link(ob)
    ob.parent = parent
    ob.location = location
    return ob

# ----------------------------------------------------------------------------- primitives
def rounded_box(mb, half, radius, mat, xf, seg=0.03, smooth=True):
    """Dense rounded box. half = (hx,hy,hz); xf maps local Vector -> car space Vector."""
    hx, hy, hz = half
    radius = min(radius, hx, hy, hz)
    seg = clamp(radius * 0.8, 0.014, seg)
    nx = max(2, int(math.ceil(2 * hx / seg))); ny = max(2, int(math.ceil(2 * hy / seg))); nz = max(2, int(math.ceil(2 * hz / seg)))
    ix, iy, iz = hx - radius, hy - radius, hz - radius
    vid = {}
    def V(i, j, k):
        key = (i, j, k)
        if key in vid: return vid[key]
        p = Vector((-hx + 2 * hx * i / nx, -hy + 2 * hy * j / ny, -hz + 2 * hz * k / nz))
        q = Vector((clamp(p.x, -ix, ix), clamp(p.y, -iy, iy), clamp(p.z, -iz, iz)))
        d = p - q
        if d.length > 1e-9: p = q + d.normalized() * radius
        vid[key] = mb.vert(xf(p)); return vid[key]
    for j in range(ny):
        for k in range(nz):
            mb.face((V(nx, j, k), V(nx, j + 1, k), V(nx, j + 1, k + 1), V(nx, j, k + 1)), mat, smooth)
            mb.face((V(0, j, k + 1), V(0, j + 1, k + 1), V(0, j + 1, k), V(0, j, k)), mat, smooth)
    for k in range(nz):
        for i in range(nx):
            mb.face((V(i, ny, k), V(i, ny, k + 1), V(i + 1, ny, k + 1), V(i + 1, ny, k)), mat, smooth)
            mb.face((V(i + 1, 0, k), V(i + 1, 0, k + 1), V(i, 0, k + 1), V(i, 0, k)), mat, smooth)
    for i in range(nx):
        for j in range(ny):
            mb.face((V(i, j, nz), V(i + 1, j, nz), V(i + 1, j + 1, nz), V(i, j + 1, nz)), mat, smooth)
            mb.face((V(i, j + 1, 0), V(i + 1, j + 1, 0), V(i + 1, j, 0), V(i, j, 0)), mat, smooth)

def xform(loc, rot=(0, 0, 0), scale=(1, 1, 1)):
    M = Matrix.Translation(Vector(loc)) @ Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X')
    S = Matrix.Diagonal((*scale, 1.0))
    T = M @ S
    return lambda p: T @ p

def lathe(mb, profile, center, axis, n, mat, smooth=True, cap_start=False, cap_end=False):
    """Revolve (rho, a) profile around axis through center."""
    axis = Vector(axis).normalized()
    e1 = axis.orthogonal().normalized(); e2 = axis.cross(e1)
    c = Vector(center)
    rings = []
    for rho, a in profile:
        rings.append([c + axis * a + (e1 * math.cos(2 * math.pi * k / n) + e2 * math.sin(2 * math.pi * k / n)) * rho for k in range(n)])
    ids = mb.grid(rings, mat, smooth, close=True)
    if cap_start: mb.face(ids[0][::-1], mat, smooth)
    if cap_end: mb.face(ids[-1], mat, smooth)
    return ids

def sweep(mb, path, section, mat, up=(0, 0, 1), cap=True, smooth=True):
    up = Vector(up); path = [Vector(p) for p in path]
    section = section[::-1]   # clockwise in (side, up) -> outward-facing quads
    n = len(path); rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        side = t.cross(up).normalized(); u2 = side.cross(t)
        rings.append([p + side * s[0] + u2 * s[1] for s in section])
    ids = mb.grid(rings, mat, smooth, close=True)
    if cap:
        mb.face(ids[0][::-1], mat, smooth); mb.face(ids[-1], mat, smooth)
    return ids

def rounded_rect(hw, hh, r, per_corner=4):
    pts = []
    for cx, cy, a0 in ((hw - r, hh - r, 0), (-hw + r, hh - r, 90), (-hw + r, -hh + r, 180), (hw - r, -hh + r, 270)):
        for k in range(per_corner + 1):
            a = math.radians(a0 + 90 * k / per_corner)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts

def circle(r, n=16):
    return [(r * math.cos(2 * math.pi * k / n), r * math.sin(2 * math.pi * k / n)) for k in range(n)]

# ----------------------------------------------------------------------------- body shell
F_FRONT, F_REAR = 2.30, -2.30
AXLE_F, AXLE_R = 1.36, -1.36
WHEEL_R, WHEEL_Z, TRACK, TIRE_HW = 0.40, 0.40, 0.80, 0.17
ARCH_R = 0.465

TOP_Z = [(-2.30, 0.975), (-2.0, 1.02), (-1.6, 1.035), (-1.2, 1.03), (0.0, 1.03), (0.75, 1.02), (1.3, 1.0), (1.9, 0.955), (2.30, 0.905)]
CROWN = [(-2.3, 0.012), (-1.7, 0.025), (0.0, 0.03), (1.0, 0.036), (2.3, 0.022)]
BOT_Z = [(-2.30, 0.45), (-2.0, 0.37), (-1.65, 0.33), (1.65, 0.33), (2.0, 0.36), (2.30, 0.42)]
MAX_W = [(-2.30, 0.885), (-2.05, 0.93), (-1.4, 0.965), (-0.7, 0.935), (0.4, 0.93), (1.3, 0.955), (2.0, 0.93), (2.30, 0.895)]
TUMBLE = 0.07
CREASE_Z = 0.74
R_SH = 0.06
N_TOP, N_SH, N_SIDE, N_WR, N_WW, N_BOT = 22, 7, 30, 6, 6, 8
X_IN, XMID_Q, XMID_R = 0.56, 0.72, 0.62

def arch_z(f):
    best = -1.0
    for ax in (AXLE_F, AXLE_R):
        d = abs(f - ax)
        if d < ARCH_R: best = max(best, WHEEL_Z + math.sqrt(ARCH_R * ARCH_R - d * d))
    return best

def flare(f):
    return 0.03 * max(smoothstep(ARCH_R + 0.32, ARCH_R - 0.02, abs(f - AXLE_F)),
                      smoothstep(ARCH_R + 0.32, ARCH_R - 0.02, abs(f - AXLE_R)))

def body_params(f):
    zT = curve(TOP_Z, f); cr = curve(CROWN, f); zS = zT - cr
    zB = curve(BOT_Z, f); fl = flare(f)
    Wm = curve(MAX_W, f) + fl; Wt = Wm - TUMBLE + 0.4 * fl
    return zT, cr, zS, zB, Wm, Wt

def side_x(z, zS, zB, Wm, Wt):
    zts = zS - R_SH
    if z >= CREASE_Z:
        t = clamp((zts - z) / max(zts - CREASE_Z, 1e-4), 0, 1)
        x = lerp(Wt, Wm, 1 - (1 - t) ** 2)
    else:
        t = clamp((CREASE_Z - z) / max(CREASE_Z - zB, 1e-4), 0, 1)
        x = Wm - 0.08 * t ** 2.2
    x += 0.009 * math.exp(-((z - CREASE_Z) / 0.022) ** 2)   # character line
    return x

def half_section(f):
    zT, cr, zS, zB, Wm, Wt = body_params(f)
    pts = []
    rt = Wt - R_SH
    for i in range(N_TOP):
        r = rt * i / N_TOP
        pts.append((r, zS + cr * (1 - (r / rt) ** 2)))
    for i in range(N_SH):
        a = math.pi / 2 * (1 - i / N_SH)
        pts.append((rt + R_SH * math.cos(a), zS - R_SH + R_SH * math.sin(a)))
    zL = max(zB, arch_z(f))
    zts = zS - R_SH
    for i in range(N_SIDE + 1):
        z = lerp(zts, zL, i / N_SIDE)
        pts.append((side_x(z, zS, zB, Wm, Wt), z))
    w = smoothstep(0.0, 0.12, zL - zB)
    x0 = pts[-1][0]
    qx = lerp(XMID_Q, X_IN, w); rx = lerp(XMID_R, X_IN, w)
    for i in range(1, N_WR + 1):
        pts.append((lerp(x0, qx, i / N_WR), zL))
    for i in range(1, N_WW + 1):
        t = i / N_WW; pts.append((lerp(qx, rx, t), lerp(zL, zB, t)))
    for i in range(1, N_BOT + 1):
        pts.append((lerp(rx, 0.0, i / N_BOT), zB))
    return pts

def full_section(f):
    h = half_section(f)
    return h + [(-r, z) for (r, z) in reversed(h[1:-1])]

def stations():
    s = set()
    n = int(round((F_FRONT - F_REAR) / 0.035))
    for i in range(n + 1): s.add(round(lerp(F_REAR, F_FRONT, i / n), 5))
    for ax in (AXLE_F, AXLE_R):
        for k in range(29):
            s.add(round(ax + ARCH_R * 0.999 * math.cos(math.pi * k / 28), 5))
        s.add(round(ax + ARCH_R + 0.006, 5)); s.add(round(ax - ARCH_R - 0.006, 5))
    out = []
    for f in sorted(s):
        if not out or f - out[-1] > 0.006 or f in (F_FRONT, F_REAR): out.append(f)
    return out

def cap_rings(f_end, sign, bulge):
    ring = full_section(f_end)
    zs = [p[1] for p in ring]; zc = (max(zs) + min(zs)) / 2; Hh = (max(zs) - min(zs)) / 2
    W = max(abs(p[0]) for p in ring)
    rc = 0.09
    steps = []
    for k in range(1, 6):
        th = math.pi / 2 * k / 5
        steps.append((rc * (1 - math.cos(th)), rc * math.sin(th)))
    dmax = 0.86 * Hh
    for k in range(1, 9):
        t = k / 8
        steps.append((lerp(rc, dmax, t), rc + bulge * (1 - (1 - t) ** 2)))
    rings = []
    for d, df in steps:
        sr = 1 - d / W; sz = 1 - d / Hh
        rings.append([(p[0] * sr, f_end + sign * df, zc + (p[1] - zc) * sz) for p in ring])
    return rings

def build_body():
    mb = MB()
    rings = [[(r, f, z) for (r, z) in full_section(f)] for f in stations()]
    rear = cap_rings(F_REAR, -1, 0.02)
    front = cap_rings(F_FRONT, +1, 0.035)
    all_rings = list(reversed(rear)) + rings + front
    ids = mb.grid(all_rings, "Paint", True, close=True)
    mb.face(ids[0][::-1], "Paint"); mb.face(ids[-1], "Paint")
    return mb

def body_mat(c, n, cur):
    if n.z < -0.55: return "Underbody"
    if abs(c.x) < 0.66 and c.z < 0.9 and abs(c.y) < 2.2: return "Underbody"
    return None

# ----------------------------------------------------------------------------- cabin
CAB_LEVELS = [0.93, 0.97, 1.0, 1.02, 1.04, 1.08, 1.12, 1.16, 1.20, 1.24, 1.28, 1.32, 1.345, 1.365, 1.385, 1.40, 1.41]
CAB_W = [(0.93, 0.84), (1.0, 0.825), (1.345, 0.73), (1.385, 0.71), (1.41, 0.67)]
CAB_FF = [(0.93, 0.92), (1.0, 0.82), (1.345, 0.28), (1.385, 0.235), (1.41, 0.19)]
CAB_FR = [(0.93, -1.40), (1.0, -1.31), (1.345, -0.70), (1.385, -0.64), (1.41, -0.59)]
GLASS_Z0, GLASS_Z1 = 1.04, 1.32
SIDE_GLASS_FRACTION = 0.66   # side glass covers the front 66% of the side columns (rear edge follows a grid line)
RC_F, RC_R = 0.10, 0.17
NC_FE, NC_FC, NC_S, NC_RC, NC_RE = 10, 6, 20, 7, 10

def cabin_half_ring(z):
    w = curve(CAB_W, z); fF = curve(CAB_FF, z); fR = curve(CAB_FR, z)
    pts, kinds = [], []
    wf = w - RC_F
    for i in range(NC_FE):
        r = wf * i / NC_FE
        pts.append((r, fF - 0.07 * (r / wf) ** 2)); kinds.append("FE")
    cfy = fF - 0.07 - RC_F
    for i in range(NC_FC):
        a = math.pi / 2 * (1 - i / NC_FC)
        pts.append((wf + RC_F * math.cos(a), cfy + RC_F * math.sin(a))); kinds.append("FC")
    s0 = cfy; s1 = fR + 0.05 + RC_R
    for i in range(NC_S):
        pts.append((w, lerp(s0, s1, i / NC_S))); kinds.append("S")
    wr = w - RC_R
    for i in range(NC_RC):
        a = -math.pi / 2 * i / NC_RC
        pts.append((wr + RC_R * math.cos(a), s1 + RC_R * math.sin(a))); kinds.append("RC")
    for i in range(NC_RE + 1):
        r = wr * (1 - i / NC_RE)
        pts.append((r, fR + 0.05 * (r / wr) ** 2)); kinds.append("RE")
    return pts, kinds

def build_cabin():
    mb = MB()
    rings = []; kinds = None
    for z in CAB_LEVELS:
        h, k = cabin_half_ring(z)
        ring = h + [(-r, f) for (r, f) in reversed(h[1:-1])]
        kk = k + list(reversed(k[1:-1]))
        rings.append([(r, f, z) for (r, f) in ring]); kinds = kk
    n = len(rings[0])
    ids = [[mb.vert(p) for p in ring] for ring in rings]
    for i in range(len(rings) - 1):
        z0, z1 = CAB_LEVELS[i], CAB_LEVELS[i + 1]; zm = (z0 + z1) / 2
        for j in range(n):
            j2 = (j + 1) % n
            q = (ids[i][j], ids[i][j2], ids[i + 1][j2], ids[i + 1][j])
            kind = kinds[j] if kinds[j] == kinds[j2] else ("FE" if "FE" in (kinds[j], kinds[j2]) and "FC" not in (kinds[j], kinds[j2]) else kinds[j])
            side_col = (j - NC_FE - NC_FC) if j < n // 2 else (n - 1 - j - NC_FE - NC_FC)
            glass_col = kind in ("FE", "RE") or (kind == "S" and 0 <= side_col < int(NC_S * SIDE_GLASS_FRACTION))
            mat = "Paint"
            if glass_col:
                if GLASS_Z0 <= zm <= GLASS_Z1: mat = "Glass"
                elif (1.02 <= zm < GLASS_Z0) or (GLASS_Z1 < zm <= 1.345): mat = "Chrome"
            mb.face(q, mat)
    # roof cap
    top = rings[-1]
    fs = [p[1] for p in top]; fc = (max(fs) + min(fs)) / 2; Lh = (max(fs) - min(fs)) / 2
    W = max(abs(p[0]) for p in top); zt = CAB_LEVELS[-1]
    rr = 0.035; steps = []
    for k in range(1, 5):
        th = math.pi / 2 * k / 4
        steps.append((rr * (1 - math.cos(th)), rr * math.sin(th)))
    dmax = 0.88 * min(Lh, W)
    for k in range(1, 10):
        t = k / 9
        steps.append((lerp(rr, dmax, t), rr + 0.025 * (1 - (1 - t) ** 2)))
    prev = ids[-1]
    for d, dz in steps:
        sr = 1 - d / W; sf = 1 - d / Lh
        cur = [mb.vert((p[0] * sr, fc + (p[1] - fc) * sf, zt + dz)) for p in top]
        for j in range(n):
            j2 = (j + 1) % n
            mb.face((prev[j], prev[j2], cur[j2], cur[j]), "Paint")
        prev = cur
    mb.face(prev, "Paint")
    return mb

# ----------------------------------------------------------------------------- wheels
TIRE_PROFILE = [(0.262, 0.13), (0.30, 0.163), (0.34, 0.171), (0.37, 0.167), (0.387, 0.155), (0.396, 0.138),
                (0.40, 0.10), (0.40, 0.05), (0.40, 0.0), (0.40, -0.05), (0.40, -0.10), (0.396, -0.138),
                (0.387, -0.155), (0.37, -0.167), (0.34, -0.171), (0.30, -0.163), (0.262, -0.13)]

def build_wheel_mb(side):
    """Wheel centred at origin, axle along X. side=+1 outer face toward +X."""
    mb = MB()
    n = 56
    ax = Vector((1, 0, 0))
    # Lathe winding rule: walking the (rho, a) profile, the face normal is (da, -drho),
    # so every profile below is ordered to make normals face outward.
    tire = lathe(mb, TIRE_PROFILE[::-1], (0, 0, 0), ax, n, "Tire")
    lug_faces = set()
    for seg in range(5, 11):   # tread + shoulder segments get staggered lugs
        g = (seg - 5) // 2
        for k in range(n):
            if (k // 2 + g) % 2 == 0:
                lug_faces.add((seg, k))
    # rim: chrome lip + dark barrel
    lathe(mb, [(0.292, 0.135), (0.29, 0.145), (0.284, 0.153), (0.272, 0.157), (0.259, 0.152), (0.253, 0.135)], (0, 0, 0), ax, n, "Chrome")
    lathe(mb, [(0.253, 0.135), (0.252, -0.13)], (0, 0, 0), ax, n, "Rim")
    # back plate so you cannot see through the wheel
    lathe(mb, [(0.252, -0.06), (0.0, -0.06)], (0, 0, 0), ax, 28, "DarkMetal")
    # brake disc + caliper
    lathe(mb, [(0.06, -0.025), (0.205, -0.025), (0.205, 0.0), (0.06, 0.0)], (0, 0, 0), ax, 36, "DarkMetal")
    rounded_box(mb, (0.03, 0.06, 0.035), 0.015, "Caliper",
                xform((0.0, -0.13, 0.12), (math.radians(-35), 0, 0)), seg=0.02)
    # hub + spokes
    lathe(mb, [(0.09, 0.05), (0.088, 0.10), (0.07, 0.116), (0.04, 0.124), (0.0, 0.125)], (0, 0, 0), ax, 28, "Rim")
    lathe(mb, [(0.042, 0.122), (0.035, 0.13), (0.0, 0.133)], (0, 0, 0), ax, 20, "Chrome")
    for s in range(5):
        a = 2 * math.pi * s / 5
        def spoke_xf(p, a=a):
            # local: x = axial, y = radial, z = tangential ; taper + dish
            t = clamp((p.y + 0.09) / 0.18, 0, 1)
            radial = 0.165 + p.y
            tang = p.z * lerp(1.15, 0.7, t)
            axial = 0.098 + p.x - 0.022 * t
            return Vector((axial, radial * math.cos(a) - tang * math.sin(a), radial * math.sin(a) + tang * math.cos(a)))
        rounded_box(mb, (0.02, 0.09, 0.035), 0.012, "Rim", spoke_xf, seg=0.02)
        # lug nut
        la = a + math.pi / 5
        c = Vector((0.12, 0.055 * math.cos(la), 0.055 * math.sin(la)))
        lathe(mb, [(0.012, 0.0), (0.011, 0.014), (0.0, 0.016)], c, ax, 8, "Chrome")
    if side < 0:   # mirror for the left wheels (and flip winding to keep normals outward)
        mb.v = [(-x, y, z) for (x, y, z) in mb.v]
        mb.f = [(idx[::-1], m, s) for (idx, m, s) in mb.f]
    return mb, tire, lug_faces

def build_wheel(name, parent, loc, side):
    mb, tire_ids, lugs = build_wheel_mb(side)
    ob = build_object(name, mb, parent, weld=True, recalc=False, location=loc)
    # Extrude chunky tread lugs.
    bm = bmesh.new(); bm.from_mesh(ob.data)
    n = 56
    tire_mat = list(ob.data.materials).index(MATS["Tire"])
    targets = []
    for fc in bm.faces:
        if fc.material_index != tire_mat: continue
        c = fc.calc_center_median()
        rho = math.hypot(c.y, c.z)
        if rho < 0.385: continue
        ang = math.atan2(c.z, c.y) % (2 * math.pi)
        k = int(ang / (2 * math.pi) * n) % n
        axial = c.x * side
        seg = None
        for s in range(5, 11):
            a0 = TIRE_PROFILE[s][1]; a1 = TIRE_PROFILE[s + 1][1]
            if min(a0, a1) <= axial <= max(a0, a1): seg = s; break
        if seg is not None and (seg, k) in lugs: targets.append(fc)
    res = bmesh.ops.extrude_discrete_faces(bm, faces=targets)
    for fc in res["faces"]:
        nrm = fc.normal.copy()
        for v in fc.verts: v.co += nrm * 0.016
        fc.smooth = False
    bm.normal_update()
    bm.to_mesh(ob.data); bm.free()
    return ob

# ----------------------------------------------------------------------------- details
def build_details(root, body_mb):
    bvh = BVHTree.FromPolygons([Vector(v) for v in body_mb.v], [f[0] for f in body_mb.f])
    def hit(o, d):
        loc, nor, idx, dist = bvh.ray_cast(Vector(o), Vector(d).normalized(), 20.0)
        return loc
    front_y = lambda r, z: hit((r, 6, z), (0, -1, 0)).y
    rear_y = lambda r, z: hit((r, -6, z), (0, 1, 0)).y
    side_x = lambda f, z: hit((6, f, z), (-1, 0, 0)).x
    top_z = lambda r, f: hit((r, f, 6), (0, 0, -1)).z
    objs = []

    # --- bumpers (chrome sweeps wrapping the corners)
    def bumper(z, front):
        sgn = 1 if front else -1
        face = front_y(0, z) if front else rear_y(0, z)
        f_side = sgn * 1.92
        wr = side_x(f_side, z)
        off = 0.042
        Rc = 0.24
        cx = wr + off - Rc; cy = face + sgn * off - sgn * Rc
        path = []
        for i in range(4):
            path.append((wr + off, lerp(f_side - sgn * 0.02, cy, i / 3), z))
        for i in range(1, 9):
            a = math.pi / 2 * i / 8
            path.append((cx + Rc * math.cos(a), cy + sgn * Rc * math.sin(a), z))
        m = 30
        for i in range(1, m):
            path.append((lerp(cx, -cx, i / m), face + sgn * off, z))
        for i in range(0, 8):
            a = math.pi / 2 * (1 - i / 8)
            path.append((-cx - Rc * math.cos(a), cy + sgn * Rc * math.sin(a), z))
        for i in range(4):
            path.append((-(wr + off), lerp(cy, f_side - sgn * 0.02, i / 3), z))
        mb = MB()
        sweep(mb, path, rounded_rect(0.045, 0.068, 0.03), "Chrome")
        if front:
            for r in (-0.33, 0.33):
                rounded_box(mb, (0.035, 0.05, 0.105), 0.025, "BlackTrim", xform((r, face + sgn * (off + 0.035), z + 0.01)))
        else:
            for r in (-0.55, 0.55):
                lathe(mb, [(0.0, 0.0), (0.03, 0.0), (0.045, 0.0), (0.045, 0.30)], (r, -2.08, 0.37), (0, -1, 0), 18, "Chrome", cap_end=False)
                lathe(mb, [(0.036, 0.299), (0.0, 0.299)], (r, -2.08, 0.37), (0, -1, 0), 18, "BlackTrim")
        return mb
    objs.append(build_object("Bumper_Front", bumper(0.49, True), root, weld=True, recalc=False))
    objs.append(build_object("Bumper_Rear", bumper(0.52, False), root, weld=True, recalc=False))

    # --- grille + headlights
    mb = MB()
    zg = 0.70
    fg = max(front_y(r, zg) for r in (-0.6, -0.3, 0.0, 0.3, 0.6))
    rounded_box(mb, (0.825, 0.03, 0.155), 0.03, "Chrome", xform((0, fg - 0.015, zg)))
    rounded_box(mb, (0.805, 0.03, 0.14), 0.025, "BlackTrim", xform((0, fg + 0.0, zg)))
    for i in range(6):
        z = zg - 0.085 + i * 0.034
        rounded_box(mb, (0.36, 0.008, 0.0065), 0.005, "DarkMetal", xform((0, fg + 0.032, z)), seg=0.04)
    rounded_box(mb, (0.012, 0.012, 0.115), 0.008, "Chrome", xform((0, fg + 0.04, zg)), seg=0.04)
    rounded_box(mb, (0.375, 0.006, 0.112), 0.01, "Chrome", xform((0, fg + 0.028, zg)), seg=0.04)
    rounded_box(mb, (0.365, 0.004, 0.103), 0.008, "BlackTrim", xform((0, fg + 0.033, zg)), seg=0.04)
    objs.append(build_object("Grille", mb, root, weld=True, recalc=False))
    mb = MB()
    for r in (-0.68, -0.50, 0.50, 0.68):
        c = (r, fg + 0.03, zg)
        lathe(mb, [(0.052, -0.005), (0.072, 0.0), (0.08, 0.015), (0.075, 0.03), (0.064, 0.028)], c, (0, 1, 0), 28, "Chrome")
        lathe(mb, [(0.064, 0.026), (0.052, 0.035), (0.03, 0.043), (0.0, 0.045)], c, (0, 1, 0), 28, "Headlight")
    for r in (-0.55, 0.55):
        rounded_box(mb, (0.07, 0.02, 0.022), 0.012, "Indicator", xform((r, front_y(r, 0.385) + 0.005, 0.385)), seg=0.02)
    objs.append(build_object("Headlights", mb, root, weld=True, recalc=False))

    # --- taillights + rear emblem
    mb = MB()
    zt = 0.80
    frr = min(rear_y(r, zt) for r in (-0.7, -0.4, 0.0, 0.4, 0.7))
    for r in (-0.47, 0.47):
        rounded_box(mb, (0.30, 0.025, 0.072), 0.02, "Chrome", xform((r, frr + 0.01, zt)))
        rounded_box(mb, (0.278, 0.02, 0.054), 0.015, "Taillight", xform((r, frr - 0.008, zt)))
        rounded_box(mb, (0.008, 0.01, 0.056), 0.005, "Chrome", xform((r, frr - 0.024, zt)), seg=0.03)
    lathe(mb, [(0.058, 0.0), (0.055, 0.018), (0.04, 0.03), (0.0, 0.032)], (0, frr + 0.005, zt), (0, -1, 0), 28, "Chrome")
    objs.append(build_object("Taillights", mb, root, weld=True, recalc=False))

    # --- hood scoop
    mb = MB()
    fs = 1.22
    zh = top_z(0, fs)
    def scoop_xf(p):
        t = (p.y + 0.30) / 0.60          # 0 rear .. 1 front
        h = p.z * lerp(0.55, 1.0, t)
        return Vector((p.x * lerp(0.9, 1.0, t), fs + p.y, zh - 0.01 + h + 0.035))
    rounded_box(mb, (0.21, 0.30, 0.06), 0.035, "Paint", scoop_xf, seg=0.03)
    rounded_box(mb, (0.165, 0.012, 0.03), 0.012, "BlackTrim", xform((0, fs + 0.296, zh + 0.055)), seg=0.02)
    objs.append(build_object("HoodScoop", mb, root, weld=True, recalc=False, uv_u=0.5))  # solid blue (centre of the stripe texture)

    # --- rear spoiler (ducktail wing)
    mb = MB()
    fsp = -2.13
    zd = top_z(0, fsp)
    # ducktail lip: a wedge whose leading edge blends into the deck
    def lip_xf(p):
        t = (p.y + 0.11) / 0.22            # 0 front .. 1 rear edge (rear = -y)
        t = 1 - t
        return Vector((p.x * lerp(1.0, 0.97, t), fsp + p.y, zd - 0.025 + (p.z + 0.03) * lerp(0.35, 1.25, t)))
    rounded_box(mb, (0.82, 0.11, 0.03), 0.02, "Paint", lip_xf, seg=0.025)
    objs.append(build_object("Spoiler", mb, root, weld=True, recalc=False))

    # --- mirrors
    mb = MB()
    for s in (-1, 1):
        fm = 0.70
        wx = side_x(fm, 0.97)
        rounded_box(mb, (0.055, 0.018, 0.016), 0.008, "Chrome", xform((s * (wx + 0.02), fm, 1.02), (0, s * math.radians(-25), 0)), seg=0.02)
        rounded_box(mb, (0.07, 0.048, 0.045), 0.03, "Chrome", xform((s * (wx + 0.085), fm - 0.01, 1.065)), seg=0.02)
        rounded_box(mb, (0.055, 0.004, 0.033), 0.004, "Glass", xform((s * (wx + 0.085), fm - 0.058, 1.065)), seg=0.02)
    objs.append(build_object("Mirrors", mb, root, weld=True, recalc=False))

    # --- small trim: door handles, side markers, fuel cap
    mb = MB()
    for s in (-1, 1):
        x = side_x(0.02, 0.88)
        rounded_box(mb, (0.012, 0.075, 0.013), 0.008, "Chrome", xform((s * (x + 0.006), 0.02, 0.88)), seg=0.02)
        x = side_x(1.98, 0.60)
        rounded_box(mb, (0.012, 0.055, 0.02), 0.008, "Indicator", xform((s * (x - 0.002), 1.98, 0.60)), seg=0.02)
        x = side_x(-1.98, 0.66)
        rounded_box(mb, (0.012, 0.055, 0.02), 0.008, "Taillight", xform((s * (x - 0.002), -1.98, 0.66)), seg=0.02)
    x = side_x(-1.80, 0.88)
    lathe(mb, [(0.052, -0.004), (0.05, 0.006), (0.035, 0.014), (0.0, 0.016)], (-x, -1.80, 0.88), (-1, 0, 0), 24, "Chrome")
    objs.append(build_object("Trim", mb, root, weld=True, recalc=False))

    # --- side exhaust pipes
    mb = MB()
    for s in (-1, 1):
        path = [(s * 0.895, lerp(-0.80, 0.80, i / 24), 0.345) for i in range(25)]
        sweep(mb, path, circle(0.045, 16), "Chrome", up=(0, 0, 1))
        for y in (-0.80, 0.80):
            lathe(mb, [(0.03, 0.0), (0.0, 0.0)], (s * 0.895, y + (0.001 if y > 0 else -0.001), 0.345), (0, 1 if y > 0 else -1, 0), 16, "BlackTrim")
    objs.append(build_object("SidePipes", mb, root, weld=True, recalc=False))
    return objs

# ----------------------------------------------------------------------------- vertex groups (documentation of panel regions)
def add_body_regions(ob):
    groups = {}
    def g(name):
        if name not in groups: groups[name] = ob.vertex_groups.new(name=name)
        return groups[name]
    for v in ob.data.vertices:
        x, y, z = v.co
        names = []
        if y > 1.95: names.append("FrontSection")
        if y < -1.95: names.append("RearSection")
        if y > 0.85 and z > 0.85 and abs(x) < 0.85: names.append("Hood")
        if y < -1.45 and z > 0.85 and abs(x) < 0.85: names.append("Trunk")
        side = "L" if x < 0 else "R"
        if abs(x) > 0.7 and z <= 0.98:
            if -0.75 < y < 0.80: names.append("Door_" + side)
            elif y >= 0.80: names.append("FrontFender_" + side)
            else: names.append("RearQuarter_" + side)
        for n in names: g(n).add([v.index], 1.0, 'REPLACE')

# ----------------------------------------------------------------------------- render preview
def setup_preview():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    world = bpy.data.worlds.new("World"); sc.world = world
    try: world.use_nodes = True
    except Exception: pass
    bg = world.node_tree.nodes.get("Background")
    if bg: bg.inputs[0].default_value = (0.32, 0.33, 0.35, 1); bg.inputs[1].default_value = 0.8
    # floor
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    floor = bpy.context.active_object
    fm = bpy.data.materials.new("Floor")
    try: fm.use_nodes = True
    except Exception: pass
    bs = next(n for n in fm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs["Base Color"].default_value = (0.25, 0.25, 0.26, 1); bs.inputs["Roughness"].default_value = 0.45
    floor.data.materials.append(fm)
    sun = bpy.data.lights.new("Key", 'SUN'); sun.energy = 3.0; sun.angle = math.radians(8)
    so = bpy.data.objects.new("Key", sun); sc.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), math.radians(10), math.radians(140))
    area = bpy.data.lights.new("Fill", 'AREA'); area.energy = 600; area.size = 6
    ao = bpy.data.objects.new("Fill", area); sc.collection.objects.link(ao)
    ao.location = (-4, 3, 5); ao.rotation_euler = (math.radians(40), 0, math.radians(-130))
    cam = bpy.data.cameras.new("Cam"); cam.lens = 50
    co = bpy.data.objects.new("Cam", cam); sc.collection.objects.link(co); sc.camera = co
    return co

def render_views(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    co = setup_preview()
    target = Vector((0, 0, 0.7))
    views = {"front34": (-5.2, 6.2, 2.3), "side": (-8.5, 0, 1.2), "rear34": (5.0, -6.0, 2.6), "front": (0, 8.5, 1.5), "top": (0.01, 0, 11)}
    for name, pos in views.items():
        co.location = Vector(pos)
        d = target - co.location
        co.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        bpy.context.scene.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)

# ----------------------------------------------------------------------------- main
def main():
    global COLL
    reset_scene()
    COLL = bpy.context.scene.collection
    build_materials()
    root = bpy.data.objects.new("MuscleCar", None)
    COLL.objects.link(root)
    body_mb = build_body()
    body = build_object("Body", body_mb, root, mat_fn=body_mat)
    add_body_regions(body)
    cabin = build_object("Cabin", build_cabin(), root)
    build_details(root, body_mb)
    for name, x, y, s in (("Wheel_FL", -TRACK, AXLE_F, -1), ("Wheel_FR", TRACK, AXLE_F, 1),
                          ("Wheel_RL", -TRACK, AXLE_R, -1), ("Wheel_RR", TRACK, AXLE_R, 1)):
        build_wheel(name, root, (x, y, WHEEL_Z), s)

    # The car is left facing +Y. Unity's FBX importer converts the file's axis system back to its own
    # (Blender +Y -> Unity +Z, Blender +X -> Unity +X), so +Y here lands as "forward" in Unity.

    stats = {ob.name: (len(ob.data.vertices), len(ob.data.polygons)) for ob in COLL.objects if ob.type == 'MESH'}
    for k, v in stats.items(): print("MESH", k, v)
    print("TOTAL_VERTS", sum(v[0] for v in stats.values()))

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    # make the texture path relative inside the .blend
    for img in bpy.data.images:
        if img.filepath: img.filepath = bpy.path.relpath(img.filepath)
    bpy.ops.wm.save_mainfile(filepath=BLEND_PATH)

    bpy.ops.export_scene.fbx(filepath=FBX_PATH, use_selection=False, object_types={'EMPTY', 'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True,
                             add_leaf_bones=False, path_mode='AUTO', embed_textures=False, use_custom_props=False)
    print("EXPORTED", FBX_PATH)
    if RENDER_DIR:
        render_views(RENDER_DIR)

main()
