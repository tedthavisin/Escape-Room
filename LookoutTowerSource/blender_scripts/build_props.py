"""Cab furniture props. Each prop is built in local space (origin = floor contact centre, +Y = north)
and then placed at its spot in the cab. Exported together as CabProps.fbx (children keep their placement)."""
import math, sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
import bpy
from mathutils import Matrix, Vector
from bgeo import MB, vec, register_material, reset_scene, export_fbx
import build_tower as T

F = T.FLOOR_Z
DESK_Z = 0.76
DESK_HL = 0.55          # desk half-length (along the east wall)
DESK_POS = (1.70, -0.20)
# Layout note: the cab is only 4.06 m across, so the furniture is placed to keep a 0.75 m+ walkway
# (a 0.6 m capsule plus skin) all the way round the fire finder and from the hatch to every piece.
# Check with analysis/search.py-style footprints before moving things.
PLACEMENTS = {}


def place(m, pos, rot_deg=0.0):
    M = Matrix.Translation(vec(pos)) @ Matrix.Rotation(math.radians(rot_deg), 4, "Z")
    m.transform(M)
    m.origin = vec(pos)
    PLACEMENTS[m.name] = dict(pos=list(pos), rot=rot_deg)
    return m


def fire_finder():
    base = MB("FireFinder")
    W = "M_WoodFurniture"
    base.box((-0.36, -0.045, 0), (0.36, 0.045, 0.07), W)
    base.box((-0.045, -0.36, 0), (0.045, 0.36, 0.07), W, grain="y")
    base.box((-0.07, -0.07, 0.07), (0.07, 0.07, 0.92), W, grain="z")
    for a in range(4):
        ang = a * math.pi / 2
        d = vec(math.cos(ang), math.sin(ang), 0)
        base.beam(d * 0.07 + vec(0, 0, 0.62), d * 0.21 + vec(0, 0, 0.92), 0.04, 0.05, W, up=(0, 0, 1))
    base.box((-0.24, -0.24, 0.92), (0.24, 0.24, 0.95), W)
    base.cyl((0, 0, 0.95), (0, 0, 0.975), 0.375, segs=32, mat="M_Iron")
    base.disc((0, 0, 0.9765), 0.345, 32, "M_Map")
    # rotating alidade (ring + sights)
    s = MB("FireFinder_Sight")
    zr = 0.975
    s.ring((0, 0, 0), 0.347, 0.372, zr, zr + 0.012, 32, "M_Brass")
    # degree ticks on the ring
    for k in range(36):
        a = 2 * math.pi * k / 36
        L = 0.012 if k % 3 else 0.022
        p0 = vec(math.cos(a) * (0.371 - L), math.sin(a) * (0.371 - L), zr + 0.0125)
        p1 = vec(math.cos(a) * 0.371, math.sin(a) * 0.371, zr + 0.0125)
        s.beam(p0, p1, 0.002, 0.0015, "M_PlasticBlack", up=(0, 0, 1))
    # front sight: frame with vertical hair (north side)
    for dx in (-0.035, 0.035):
        s.box((dx - 0.006, 0.352, zr + 0.012), (dx + 0.006, 0.366, zr + 0.17), "M_Brass")
    s.box((-0.041, 0.352, zr + 0.16), (0.041, 0.366, zr + 0.172), "M_Brass")
    s.cyl((0, 0.359, zr + 0.012), (0, 0.359, zr + 0.16), 0.0012, segs=4, mat="M_Iron")
    # rear peep sight (south side)
    s.box((-0.03, -0.366, zr + 0.012), (0.03, -0.352, zr + 0.13), "M_Brass")
    s.box((-0.004, -0.367, zr + 0.06), (0.004, -0.351, zr + 0.115), "M_PlasticBlack")
    # handles
    for sgn in (-1, 1):
        s.cyl((sgn * 0.372, 0, zr + 0.006), (sgn * 0.40, 0, zr + 0.006), 0.006, segs=6, mat="M_Brass")
        s.cyl((sgn * 0.40, 0, zr - 0.01), (sgn * 0.40, 0, zr + 0.04), 0.012, segs=8, mat="M_PlasticBlack")
    pos = (0.05, 0.0, F)      # centre of the cab, as in real lookouts
    place(base, pos)
    place(s, (pos[0], pos[1], F))
    s.origin = vec(pos[0], pos[1], F + zr)
    PLACEMENTS[s.name]["pos"][2] = F + zr
    return [base, s]


def cot():
    m = MB("Cot")
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * 0.9, sy * 0.33
            m.cyl((x, y, 0), (x, y, 0.065), 0.045, 0.035, segs=10, mat="M_GlassInsulator")
            m.cyl((x, y, 0.065), (x, y, 0.4), 0.013, segs=6, mat="M_Iron")
    for sy in (-1, 1):
        m.cyl((-0.93, sy * 0.35, 0.4), (0.93, sy * 0.35, 0.4), 0.016, segs=6, mat="M_Iron")
    for sx in (-1, 1):
        m.cyl((sx * 0.92, -0.36, 0.4), (sx * 0.92, 0.36, 0.4), 0.016, segs=6, mat="M_Iron")
    for i in range(9):
        x = -0.8 + i * 0.2
        m.box((x - 0.012, -0.35, 0.398), (x + 0.012, 0.35, 0.405), "M_Iron", grain="y")
    m.box((-0.91, -0.34, 0.405), (0.91, 0.34, 0.53), "M_Ticking")
    m.box((-0.88, -0.27, 0.53), (-0.56, 0.27, 0.615), "M_Ticking", grain="y")
    m.box((0.36, -0.31, 0.53), (0.88, 0.31, 0.6), "M_Wool")
    m.box((-0.3, -0.355, 0.53), (0.33, 0.335, 0.545), "M_Wool")
    m.box((-0.3, -0.37, 0.24), (0.33, -0.355, 0.545), "M_Wool")
    return [place(m, (-1.0, 1.58, F))]


def stove():
    m = MB("WoodStove")
    I = "M_Iron"
    m.box((-0.45, -0.85, 0), (0.45, 0.45, 0.008), I)
    for x, y in ((-0.2, -0.37), (0.2, -0.37), (-0.2, 0.17), (0.2, 0.17)):
        m.cyl((x, y, 0.008), (x, y, 0.17), 0.03, 0.022, segs=8, mat=I)
    m.box((-0.24, -0.42, 0.16), (0.24, 0.22, 0.58), I)
    m.box((-0.26, -0.44, 0.58), (0.26, 0.24, 0.6), I)
    m.box((-0.25, -0.47, 0.16), (0.25, -0.42, 0.2), I)              # ash lip
    m.cyl((0, -0.24, 0.6), (0, -0.24, 0.607), 0.09, segs=16, mat=I)
    m.box((-0.008, -0.33, 0.607), (0.008, -0.3, 0.612), I)
    m.cyl((0, 0, 0.6), (0, 0, 0.66), 0.082, segs=14, mat=I)
    m.cyl((0, 0, 0.66), (0, 0, T.CEIL_Z - F), 0.075, segs=14, mat=I, caps=(False, False))
    for z in (1.05, 1.6):
        m.cyl((0, 0, z), (0, 0, z + 0.02), 0.079, segs=14, mat=I)
    m.cyl((-0.11, 0, 1.0), (0.11, 0, 1.0), 0.006, segs=5, mat=I)    # damper rod
    m.cyl((0.11, 0, 0.985), (0.11, 0, 1.015), 0.02, segs=8, mat="M_PlasticBlack")
    m.box((-0.17, -0.445, 0.25), (0.17, -0.42, 0.5), I)             # door
    m.cyl((0.12, -0.445, 0.38), (0.12, -0.5, 0.38), 0.008, segs=6, mat=I)
    m.cyl((0.12, -0.5, 0.38), (0.06, -0.5, 0.38), 0.008, segs=6, mat=I)
    m.cyl((-0.06, -0.445, 0.3), (-0.06, -0.455, 0.3), 0.035, segs=12, mat=I)   # draft vent
    for z in (0.29, 0.46):
        m.cyl((-0.175, -0.43, z - 0.02), (-0.175, -0.43, z + 0.02), 0.01, segs=6, mat=I)
    return [place(m, (1.55, 1.55, F))]


def desk_and_chair():
    d = MB("Desk")
    W = "M_WoodFurniture"
    L = DESK_HL
    d.box((-0.315, -L, DESK_Z - 0.03), (0.315, L, DESK_Z), W, grain="y")
    for sx in (-1, 1):
        for sy in (-1, 1):
            d.box((sx * 0.28 - 0.025, sy * (L - 0.04) - 0.025, 0), (sx * 0.28 + 0.025, sy * (L - 0.04) + 0.025, DESK_Z - 0.03),
                  W, grain="z")
    d.box((-0.255, -(L - 0.06), DESK_Z - 0.13), (-0.235, L - 0.06, DESK_Z - 0.03), W, grain="y")
    d.box((0.235, -(L - 0.06), DESK_Z - 0.13), (0.255, L - 0.06, DESK_Z - 0.03), W, grain="y")
    d.box((-0.26, -(L - 0.05), DESK_Z - 0.13), (0.26, -(L - 0.07), DESK_Z - 0.03), W)
    d.box((-0.26, L - 0.07, DESK_Z - 0.13), (0.26, L - 0.05, DESK_Z - 0.03), W)
    d.box((-0.275, -0.3, DESK_Z - 0.12), (-0.255, 0.3, DESK_Z - 0.04), W, grain="y")
    d.cyl((-0.275, 0, DESK_Z - 0.08), (-0.3, 0, DESK_Z - 0.08), 0.012, segs=8, mat="M_Iron")
    d.box((-0.02, -(L - 0.06), 0.12), (0.02, L - 0.06, 0.16), W, grain="y")
    c = MB("Chair")
    for sx in (-1, 1):
        for sy in (-1, 1):
            top = 0.92 if sx < 0 else 0.43
            c.box((sx * 0.19 - 0.018, sy * 0.19 - 0.018, 0), (sx * 0.19 + 0.018, sy * 0.19 + 0.018, top), W, grain="z")
    c.box((-0.215, -0.215, 0.43), (0.215, 0.215, 0.46), W, grain="y")
    for z in (0.66, 0.82):
        c.box((-0.205, -0.19, z), (-0.18, 0.19, z + 0.07), W, grain="y")
    for z in (0.14,):
        c.box((-0.19, -0.175, z), (0.19, -0.16, z + 0.03), W)
        c.box((-0.19, 0.16, z), (0.19, 0.175, z + 0.03), W)
    # chair pushed in under the desk so it doesn't block the walkway round the fire finder
    return [place(d, (DESK_POS[0], DESK_POS[1], F)), place(c, (DESK_POS[0] - 0.25, DESK_POS[1], F))]


def radio():
    m = MB("Radio")
    m.box((-0.12, -0.18, 0), (0.12, 0.18, 0.2), "M_PaintedMetal", grain="y")
    m.box((-0.126, -0.165, 0.015), (-0.12, 0.165, 0.185), "M_Iron", grain="y")
    for y in (-0.11, 0.0, 0.11):
        m.cyl((-0.126, y, 0.055), (-0.15, y, 0.055), 0.017, segs=10, mat="M_PlasticBlack")
    m.box((-0.129, -0.09, 0.105), (-0.126, 0.09, 0.15), "M_DialGlow", grain="y")
    m.box((-0.13, -0.002, 0.103), (-0.129, 0.002, 0.152), "M_PlasticBlack")
    m.cyl((-0.126, 0.13, 0.13), (-0.135, 0.13, 0.13), 0.012, segs=6, mat="M_Iron")
    m.box((-0.126, -0.16, 0.1), (-0.12, -0.12, 0.17), "M_PlasticBlack")
    # handset on cradle
    for y in (-0.08, 0.08):
        m.box((-0.03, y - 0.01, 0.2), (0.03, y + 0.01, 0.235), "M_PlasticBlack")
    m.box((-0.022, -0.11, 0.235), (0.022, 0.11, 0.258), "M_PlasticBlack", grain="y")
    for y in (-0.12, 0.12):
        m.cyl((0, y, 0.228), (0, y, 0.262), 0.032, segs=10, mat="M_PlasticBlack")
    cord = []
    for i in range(14):
        t = i / 13
        cord.append((0.13 + 0.02 * math.sin(i * 2.2), -0.12 + 0.24 * t * 0.3 - 0.1 * t, 0.02 + 0.2 * (1 - t) ** 2))
    m.tube_path([(0.0, -0.13, 0.24)] + cord, 0.004, segs=4, mat="M_PlasticBlack")
    m.cyl((0.1, 0.15, 0.2), (0.1, 0.15, 0.24), 0.01, segs=6, mat="M_Iron")   # antenna post
    return [place(m, (DESK_POS[0] + 0.075, DESK_POS[1] + 0.33, F + DESK_Z), rot_deg=-4)]


def desk_items():
    b = MB("Logbook")
    b.box((-0.12, -0.16, 0), (0.12, 0.16, 0.003), "M_BookCover", grain="y")
    b.box((-0.115, -0.155, 0.003), (0.115, 0.155, 0.026), "M_Paper", grain="y")
    b.box((-0.12, -0.16, 0.026), (0.12, 0.16, 0.029), "M_BookCover", grain="y")
    b.box((-0.125, -0.16, 0), (-0.115, 0.16, 0.029), "M_BookCover", grain="y")
    n = MB("Binoculars")
    for y in (-0.038, 0.038):
        n.cyl((-0.08, y, 0.032), (0.05, y, 0.032), 0.032, segs=12, mat="M_PlasticBlack")
        n.cyl((0.05, y, 0.032), (0.09, y, 0.032), 0.022, segs=10, mat="M_PlasticBlack")
        n.cyl((-0.085, y, 0.032), (-0.08, y, 0.032), 0.028, segs=12, mat="M_Glass")
    n.box((-0.02, -0.03, 0.045), (0.06, 0.03, 0.06), "M_PlasticBlack")
    n.cyl((0.0, 0, 0.06), (0.0, 0, 0.075), 0.012, segs=8, mat="M_Iron")
    l = MB("Lantern")
    R = "M_RedPaint"
    l.cyl((0, 0, 0), (0, 0, 0.055), 0.075, segs=16, mat=R)
    l.cyl((0, 0, 0.055), (0, 0, 0.072), 0.075, 0.05, segs=16, mat=R)
    l.cyl((0, 0, 0.072), (0, 0, 0.09), 0.03, segs=10, mat="M_Brass")
    l.cyl((0, 0, 0.09), (0, 0, 0.13), 0.05, 0.064, segs=14, mat="M_LanternGlass")
    l.cyl((0, 0, 0.13), (0, 0, 0.2), 0.064, 0.052, segs=14, mat="M_LanternGlass")
    l.cyl((0, 0, 0.2), (0, 0, 0.225), 0.06, segs=14, mat=R)
    l.cyl((0, 0, 0.225), (0, 0, 0.27), 0.075, 0.03, segs=14, mat=R)
    l.cyl((0, 0, 0.27), (0, 0, 0.29), 0.035, segs=10, mat=R)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        p = vec(math.cos(a) * 0.072, math.sin(a) * 0.072, 0)
        l.cyl(p + vec(0, 0, 0.07), p + vec(0, 0, 0.215), 0.0035, segs=4, mat="M_Iron")
    for sx in (-1, 1):
        l.cyl((sx * 0.082, 0, 0.04), (sx * 0.082, 0, 0.24), 0.009, segs=6, mat=R)
        l.cyl((sx * 0.082, 0, 0.24), (sx * 0.04, 0, 0.26), 0.009, segs=6, mat=R)
    arc = [(0.09 * math.cos(math.pi * t / 8), 0, 0.24 + 0.09 * math.sin(math.pi * t / 8)) for t in range(9)]
    l.tube_path(arc, 0.003, segs=4, mat="M_Iron")
    dx, dy = DESK_POS
    return [place(b, (dx - 0.08, dy - 0.05, F + DESK_Z), rot_deg=12), place(n, (dx + 0.17, dy - 0.32, F + DESK_Z), rot_deg=-25),
            place(l, (dx - 0.08, dy - 0.40, F + DESK_Z))]


def map_board():
    m = MB("MapBoard")
    W = "M_WoodFurniture"
    w, z0, z1, t = 0.46, 0.36, 0.92, 0.025
    m.box((-w, 0, z0), (w, t, z1), W)                 # backing
    for (a, b, c, d) in ((-w - 0.03, w + 0.03, z0 - 0.03, z0), (-w - 0.03, w + 0.03, z1, z1 + 0.03),
                         (-w - 0.03, -w, z0, z1), (w, w + 0.03, z0, z1)):
        m.box((a, -0.005, c), (b, t + 0.02, d), W, grain="x" if (b - a) > (d - c) else "z")
    # map sheet facing +Y (into the room), readable (U to the viewer's right = -X)
    y = t + 0.002
    m.poly([vec(w - 0.03, y, z0 + 0.03), vec(-w + 0.03, y, z0 + 0.03), vec(-w + 0.03, y, z1 - 0.03),
            vec(w - 0.03, y, z1 - 0.03)], [(0.1, 0.2), (0.9, 0.2), (0.9, 0.75), (0.1, 0.75)], "M_Map")
    for (px, pz) in ((-0.2, 0.75), (0.12, 0.6), (0.3, 0.82), (-0.05, 0.48)):
        m.cyl((px, y, pz), (px, y + 0.012, pz), 0.006, segs=6, mat="M_RedPaint")
        m.cyl((px, y + 0.012, pz), (px, y + 0.02, pz), 0.011, segs=8, mat="M_RedPaint")
    return [place(m, (-1.25, -T.CIN, F))]


def cupboard():
    m = MB("Cupboard")
    W = "M_WoodFurniture"
    m.box((-0.39, -0.225, 0.06), (0.39, 0.225, 0.82), W)
    m.box((-0.38, -0.215, 0.0), (0.38, 0.2, 0.06), W)
    m.box((-0.41, -0.235, 0.82), (0.41, 0.25, 0.85), W)
    for sx in (-1, 1):
        m.box((sx * 0.006 if sx > 0 else -0.375, 0.225, 0.08), (0.375 if sx > 0 else -0.006, 0.24, 0.8), W, grain="z")
        m.cyl((sx * 0.05, 0.24, 0.5), (sx * 0.05, 0.26, 0.5), 0.013, segs=8, mat="M_Iron")
    it = MB("CupboardItems")
    for i, (x, y) in enumerate(((-0.3, -0.05), (-0.21, -0.1), (-0.25, 0.05))):
        it.cyl((x, y, 0.85), (x, y, 0.96), 0.038, segs=12, mat="M_PaintedMetal" if i % 2 else "M_Iron")
    it.cyl((0.0, 0.0, 0.85), (0.0, 0.0, 1.07), 0.085, 0.07, segs=14, mat="M_PaintedMetal")
    it.cyl((0.0, 0.0, 1.07), (0.0, 0.0, 1.11), 0.035, segs=10, mat="M_PaintedMetal")
    it.tube_path([(0.07, 0, 1.02), (0.13, 0, 1.0), (0.13, 0, 0.92), (0.08, 0, 0.9)], 0.008, segs=5, mat="M_PaintedMetal")
    it.box((0.18, -0.15, 0.85), (0.36, 0.1, 0.885), "M_BookCover", grain="y")
    it.box((0.19, -0.13, 0.885), (0.35, 0.08, 0.915), "M_Paper", grain="y")
    pos = (1.59, -1.755, F)
    return [place(m, pos), place(it, pos)]


def firewood():
    m = MB("Firewood")
    logs = [(-0.13, 0.06, 0.06), (0.0, 0.06, 0.065), (0.13, 0.06, 0.058), (-0.065, 0.17, 0.06), (0.065, 0.17, 0.062),
            (0.0, 0.275, 0.055)]
    for i, (y, z, r) in enumerate(logs):
        L = 0.42 + 0.03 * math.sin(i * 1.7)
        m.cyl((-L / 2, y, z), (L / 2, y, z), r, segs=9, mat="M_Bark", cap_mat="M_Timber", ang0=i * 0.7)
    return [place(m, (0.92, 1.75, F), rot_deg=4)]


def main(outdir, reset=True):
    if reset:
        reset_scene()
    tex = {"M_WoodFurniture": "WoodFurniture", "M_Iron": "Iron", "M_Brass": "Brass", "M_Map": "Map",
           "M_Ticking": "Ticking", "M_Wool": "Wool", "M_PaintedMetal": "PaintedMetal", "M_Glass": "GlassGrime",
           "M_Bark": "Bark", "M_Timber": "Timber"}
    for k, v in tex.items():
        register_material(k, v, 1.0)
    builders = []
    for fn in (fire_finder, cot, stove, desk_and_chair, radio, desk_items, map_board, cupboard, firewood):
        builders += fn()
    objs, stats = [], {}
    for b in builders:
        objs.append(b.build())
        stats[b.name] = b.tri_count()
    print(json.dumps(stats, indent=1), "TOTAL", sum(stats.values()))
    export_fbx(os.path.join(outdir, "CabProps.fbx"), objs)
    json.dump(PLACEMENTS, open(os.path.join(outdir, "props_meta.json"), "w"), indent=1)
    return objs


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    outdir = argv[0] if argv else "/home/claude/lookout/out"
    T.main(outdir)                      # tower (resets scene, exports LookoutTower.fbx)
    main(outdir, reset=False)           # props on top, exported separately
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, "Combined.blend"))
