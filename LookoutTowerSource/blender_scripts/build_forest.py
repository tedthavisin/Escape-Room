"""Forest assets: conifers with 3 LODs, a dead snag, rocks, stump, fallen log. All origins at (0,0,0)."""
import math, sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
import numpy as np
import bpy, bmesh
from mathutils import Vector, Matrix
from bgeo import MB, vec, register_material, reset_scene, export_fbx

TREES = {
    # name: (height, seed, crown_base_frac, spread)
    "Pine_A": (21.0, 11, 0.26, 0.19),
    "Pine_B": (16.0, 23, 0.22, 0.2),
    "Pine_C": (26.0, 37, 0.36, 0.16),
    "Pine_D": (10.0, 41, 0.14, 0.23),
    "Pine_E": (5.5, 53, 0.06, 0.26),
}


def trunk(m, h, r0, rng, segs, top_frac=0.97, mat="M_Bark"):
    pts = [vec(0, 0, -0.3)]
    n = 4
    for i in range(1, n + 1):
        z = h * top_frac * i / n
        pts.append(vec(rng.normal(0, 0.04 * i), rng.normal(0, 0.04 * i), z))
    def rad(z):
        return max(0.012, r0 * (1 - z / h) ** 0.85)
    for a, b in zip(pts[:-1], pts[1:]):
        m.cyl(a, b, rad(max(a.z, 0)), rad(b.z), segs=segs, mat=mat, caps=(a.z < 0, False))
    # root flare
    if segs >= 6:
        m.cyl(vec(0, 0, -0.25), vec(0, 0, 0.5), r0 * 1.55, r0 * 1.02, segs=segs, mat=mat, caps=(False, False))
        for k in range(4):
            a = k * math.pi / 2 + rng.uniform(-0.4, 0.4)
            d = vec(math.cos(a), math.sin(a), 0)
            m.beam(d * r0 * 0.6 + vec(0, 0, 0.35), d * (r0 * 2.2) + vec(0, 0, -0.08), r0 * 0.7, r0 * 0.5, mat,
                   up=(0, 0, 1))
    return pts, rad


def branch(m, base, ang, R, n, widths, droop, rng, mat="M_Needles", wfac=0.55, tilt=0.15):
    d = vec(math.cos(ang), math.sin(ang), 0)
    side = vec(-d.y, d.x, 0)
    W = R * wfac
    st = []
    for i in range(n + 1):
        u = i / n
        p = base + d * (R * u) + vec(0, 0, R * (tilt * u - droop * u * u))
        p += side * (rng.normal(0, 0.06) * R * u)
        w = W * widths[i]
        L = p + side * (w / 2) + vec(0, 0, -w * 0.42)
        C = p + vec(0, 0, w * 0.10)
        Rr = p - side * (w / 2) + vec(0, 0, -w * 0.42)
        st.append((L, C, Rr, R * u, w))
    for i in range(n):
        L0, C0, R0, u0, w0 = st[i]
        L1, C1, R1, u1, w1 = st[i + 1]
        if i == n - 1 and w1 < 1e-4:
            m.poly([L0, C1, C0], [(u0, w0 / 2), (u1, 0), (u0, 0)], mat)
            m.poly([C0, C1, R0], [(u0, 0), (u1, 0), (u0, -w0 / 2)], mat)
        else:
            m.poly([L0, L1, C1, C0], [(u0, w0 / 2), (u1, w1 / 2), (u1, 0), (u0, 0)], mat)
            m.poly([C0, C1, R1, R0], [(u0, 0), (u1, 0), (u1, -w1 / 2), (u0, -w0 / 2)], mat)


def branch_specs(h, seed, cb, Rmax):
    """Deterministic list of branches shared by all LODs: (z, angle, length, droop, tilt, rank)."""
    rng = np.random.default_rng(seed + 100)
    specs = []
    z = cb
    phase = rng.uniform(0, 6.28)
    base_sp = 0.55 + 0.01 * h
    while z < h - 0.9:
        f = max(0.0, (h - z) / (h - cb))
        R = max(0.5, Rmax * f ** 0.95)
        k = int(round(5 + 4 * f)) if f > 0.15 else 4
        phase += 2.39996
        for b in range(k):
            ang = phase + 2 * math.pi * b / k + rng.uniform(-0.3, 0.3)
            L = R * rng.uniform(0.72, 1.12)
            droop = 0.35 + 0.45 * f + rng.uniform(-0.08, 0.08)
            tilt = 0.25 - 0.2 * f
            specs.append((z + rng.uniform(-0.08, 0.08), ang, L, droop, tilt, b % 2))
        # filler whorl of short branches between main tiers
        if f > 0.2:
            zf = z + base_sp * 0.5
            for b in range(3):
                ang = phase + 1.1 + 2 * math.pi * b / 3 + rng.uniform(-0.4, 0.4)
                specs.append((zf, ang, R * rng.uniform(0.45, 0.65), 0.35, 0.1, 2))
        z += base_sp * rng.uniform(0.75, 1.25)
    return specs


def pine(name, h, seed, cb_frac, spread, lod):
    rng = np.random.default_rng(seed)
    m = MB(f"{name}_LOD{lod}")
    r0 = 0.05 + 0.013 * h
    cb = h * cb_frac
    Rmax = h * spread
    if lod == 2:
        m.cyl((0, 0, -0.2), (0, 0, cb + 0.5), r0, r0 * 0.7, segs=4, mat="M_Bark", caps=(False, False))
        m.cyl((0, 0, cb - Rmax * 0.35), (0, 0, h), Rmax * 0.92, 0.02, segs=7, mat="M_Needles", caps=(True, False),
              uv_scale=0.5)
        return m
    pts, rad = trunk(m, h, r0, rng, 8 if lod == 0 else 5)
    specs = branch_specs(h, seed, cb, Rmax)
    rngb = np.random.default_rng(seed + 7)
    for (z, ang, L, droop, tilt, rank) in specs:
        base = vec(0, 0, z) + vec(math.cos(ang), math.sin(ang), 0) * rad(z) * 0.5
        if lod == 0:
            branch(m, base, ang, L, 3, [0.3, 0.95, 0.7, 0.0], droop, rngb, wfac=0.5, tilt=tilt)
        elif rank == 0:
            branch(m, base, ang, L * 1.04, 2, [0.4, 1.0, 0.0], droop, rngb, wfac=0.85, tilt=tilt)
    # Top: two crossed, alpha-cut needle cards tapering to a point, so the crown ends in a soft spire of
    # foliage. (A solid needle-textured cone here read as a dark "dunce cap" on every tree.)
    # Stacked, shrinking triangles give the same serrated outline as the branch tiers below.
    z0, W0, n_t = h - 3.4, 1.15, 4
    step = (h + 0.1 - 0.9 - z0) / (n_t - 1)
    a0 = rng.uniform(0, math.pi)
    for k in range(2 if lod == 0 else 1):
        a = a0 + k * math.pi / 2
        d = vec(math.cos(a), math.sin(a), 0)
        for j in range(n_t):
            zb = z0 + j * step
            w = W0 * (1 - 0.7 * j / (n_t - 1))
            ht = 0.9 + 0.1 * j
            bl, br, tp = vec(0, 0, zb) + d * (w / 2) + vec(0, 0, -0.15), vec(0, 0, zb) - d * (w / 2) + vec(0, 0, -0.15), vec(0, 0, zb + ht)
            m.poly([bl, tp, br], [(0, w / 2), (ht, 0), (0, -w / 2)], "M_Needles")
    if lod == 0:
        for k in range(int(rng.integers(5, 10))):
            z = rng.uniform(1.6, max(1.8, cb - 0.2))
            a = rng.uniform(0, 6.28)
            d = vec(math.cos(a), math.sin(a), 0)
            L = rng.uniform(0.35, 1.3)
            p0 = vec(0, 0, z) + d * rad(z) * 0.8
            m.cyl(p0, p0 + d * L + vec(0, 0, -L * rng.uniform(0.0, 0.5)), 0.022, 0.006, segs=4, mat="M_Timber",
                  caps=(False, False))
    return m


def snag(name="Snag_A", h=15.0, seed=71):
    rng = np.random.default_rng(seed)
    m = MB(name)
    r0 = 0.25
    pts, rad = trunk(m, h, r0, rng, 7, top_frac=1.0, mat="M_Timber")
    for k in range(26):
        z = rng.uniform(2.0, h * 0.95)
        a = rng.uniform(0, 6.28)
        d = vec(math.cos(a), math.sin(a), 0)
        L = rng.uniform(0.4, 2.4) * (1 - z / h) + 0.3
        p0 = vec(0, 0, z) + d * rad(z) * 0.8
        p1 = p0 + d * L + vec(0, 0, -L * rng.uniform(0.1, 0.6))
        m.cyl(p0, p1, max(0.012, rad(z) * 0.18), 0.006, segs=4, mat="M_Timber", caps=(False, False))
        if L > 1.0:
            q = p0 + (p1 - p0) * 0.6
            b = vec(math.cos(a + 0.8), math.sin(a + 0.8), 0)
            m.cyl(q, q + b * L * 0.35 + vec(0, 0, -0.2), 0.01, 0.004, segs=3, mat="M_Timber", caps=(False, False))
    # broken top
    m.cyl(vec(0, 0, h), vec(0.1, 0.05, h + 0.7), rad(h) * 1.2, 0.01, segs=5, mat="M_Timber", caps=(False, False))
    return m


def rock(name, seed, size, flat=0.6, sub=2):
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    dirs = rng.normal(size=(6, 3))
    dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
    freqs = rng.uniform(1.2, 3.5, 6)
    amps = rng.uniform(0.05, 0.16, 6)
    ph = rng.uniform(0, 6.28, 6)
    sx, sy, sz = size * rng.uniform(0.85, 1.2), size * rng.uniform(0.75, 1.0), size * flat
    for v in bm.verts:
        p = np.array(v.co)
        disp = sum(a * math.sin(f * p.dot(d) + q) for a, f, d, q in zip(amps, freqs, dirs, ph))
        # facet it a bit (rocks are angular)
        p = p * (1 + disp)
        z = p[2]
        if z < -0.35:
            z = -0.35 - (z + 0.35) * 0.2
        v.co = Vector((p[0] * sx, p[1] * sy, z * sz + 0.3 * sz))
    bm.normal_update()
    uvl = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for l in f.loops:
            co = l.vert.co
            l[uvl].uv = (co[a], co[b])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.materials.append(bpy.data.materials.get("M_Rock") or bpy.data.materials.new("M_Rock"))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def jagged_cap(m, c, r, n, rng, axis, mat_top, amp):
    """Fan cap with jagged rim (broken wood). axis: unit vector pointing out of the cap."""
    axis = vec(axis).normalized()
    t = vec(1, 0, 0) if abs(axis.x) < 0.9 else vec(0, 1, 0)
    ex = t.cross(axis).normalized()
    ey = axis.cross(ex)
    centre = c + axis * rng.uniform(0.0, amp)
    rim = []
    for i in range(n):
        a = 2 * math.pi * i / n
        rim.append(c + (ex * math.cos(a) + ey * math.sin(a)) * r + axis * rng.uniform(-amp * 0.3, amp))
    for i in range(n):
        p, q = rim[i], rim[(i + 1) % n]
        m.poly([centre, p, q], [(0, 0), ((p - c).dot(ex), (p - c).dot(ey)), ((q - c).dot(ex), (q - c).dot(ey))],
               mat_top)
    return rim


def stump(name="Stump_A", seed=81):
    rng = np.random.default_rng(seed)
    m = MB(name)
    r, h = 0.32, 0.55
    n = 10
    m.cyl((0, 0, -0.2), (0, 0, h - 0.06), r * 1.08, r, segs=n, mat="M_Bark", caps=(False, False))
    jagged_cap(m, vec(0, 0, h - 0.06), r, n, rng, (0, 0, 1), "M_Timber", 0.12)
    for k in range(5):
        a = k * 2 * math.pi / 5 + rng.uniform(-0.3, 0.3)
        d = vec(math.cos(a), math.sin(a), 0)
        m.beam(d * r * 0.5 + vec(0, 0, 0.25), d * (r * 2.0) + vec(0, 0, -0.1), r * 0.55, r * 0.45, "M_Bark", up=(0, 0, 1))
    return m


def fallen_log(name="Log_A", seed=91):
    rng = np.random.default_rng(seed)
    m = MB(name)
    L, r = 4.6, 0.24
    n = 10
    a, b = vec(-L / 2, 0, r * 0.75), vec(L / 2, 0.15, r * 0.7)
    m.cyl(a, b, r, r * 0.85, segs=n, mat="M_Bark", caps=(False, False))
    jagged_cap(m, a, r, n, rng, a - b, "M_Timber", 0.18)
    jagged_cap(m, b, r * 0.85, n, rng, b - a, "M_Timber", 0.25)
    for k in range(5):
        t = rng.uniform(0.1, 0.9)
        p = a + (b - a) * t
        ang = rng.uniform(0, 6.28)
        d = vec(0, math.cos(ang), math.sin(ang))
        m.cyl(p + d * r * 0.8, p + d * (r + rng.uniform(0.15, 0.6)) + vec(rng.uniform(-0.2, 0.2), 0, 0), 0.03, 0.01,
              segs=4, mat="M_Timber", caps=(False, False))
    return m


def main(outdir, preview_layout=False):
    reset_scene()
    for k, v, t in (("M_Bark", "Bark", 1.0), ("M_Needles", "Needles", 1.0), ("M_Timber", "Timber", 1.0),
                    ("M_Rock", "Rock", 2.0)):
        register_material(k, v, t)
        from bgeo import get_mat
        get_mat(k)
    objs, stats = [], {}
    builders = []
    for name, (h, seed, cb, spread) in TREES.items():
        for lod in range(3):
            builders.append(pine(name, h, seed, cb, spread, lod))
    builders += [snag(), stump(), fallen_log()]
    for b in builders:
        objs.append(b.build())
        stats[b.name] = b.tri_count()
    for i, (s, size, flat) in enumerate(((101, 1.1, 0.55), (102, 0.6, 0.7), (103, 1.9, 0.45), (104, 0.35, 0.8))):
        ob = rock(f"Rock_{'ABCD'[i]}", s, size, flat, sub=2 if size > 0.5 else 1)
        objs.append(ob)
        stats[ob.name] = len(ob.data.polygons)
    print(json.dumps(stats, indent=1))
    export_fbx(os.path.join(outdir, "Forest.fbx"), objs)
    json.dump(dict(trees={k: dict(height=v[0]) for k, v in TREES.items()}, stats=stats),
              open(os.path.join(outdir, "forest_meta.json"), "w"), indent=1)
    if preview_layout:
        x = -24
        for ob in objs:
            nm = ob.name
            if "_LOD" in nm:
                lod = int(nm[-1])
                tree = list(TREES).index(nm.split("_LOD")[0])
                ob.location = (x + tree * 9, lod * 12 - 6, 0)
            elif nm.startswith("Snag"):
                ob.location = (22, -6, 0)
            elif nm.startswith("Rock"):
                ob.location = (-12 + 3.2 * "ABCD".index(nm[-1]), -14, 0)
            elif nm.startswith("Stump"):
                ob.location = (2, -14, 0)
            elif nm.startswith("Log"):
                ob.location = (8, -14, 0)
    return objs


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    outdir = argv[0] if argv else "/home/claude/lookout/out"
    main(outdir, preview_layout=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, "Forest_preview.blend"))
