"""Fire lookout tower (Blender, Z up, metres). Blender (x, y, z) -> Unity (x, z, y)."""
import math, sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
import bpy
from mathutils import Vector, Matrix
from bgeo import MB, vec, register_material, reset_scene, export_fbx, setup_preview_materials

# ------------------------------------------------------------------ dimensions
PIER, PIER_TOP, PIER_BOT = 0.7, 0.35, -0.6
LEG = 0.36
LEG_BASE, LEG_TOP, LEG_TOP_Z = 4.8, 3.2, 30.02     # 100 ft (30.5 m) tower
BEAM_H, JOIST_H, JOIST_W, DECK_T = 0.25, 0.20, 0.06, 0.03
FLOOR_Z = LEG_TOP_Z + BEAM_H + JOIST_H + DECK_T          # 30.50 m = 100 ft
CAB = 3.65            # outer half-width of cab walls (7.3 m = 24 ft square)
WALL_T = 0.12
CIN = CAB - WALL_T    # 2.03 interior half-width
SILL_Z = FLOOR_Z + 0.95
WIN_TOP = SILL_Z + 1.45
CEIL_Z = FLOOR_Z + 2.75
EAVE_Z = FLOOR_Z + 2.85
ROOF_OVER = 1.0
PITCH = math.radians(27)
WALK = 4.65           # catwalk outer half-width (1 m wide)
STOVE = (3.03, 3.03)  # stove / stovepipe position (NE corner)
RAIL_H = 1.05
_N_PANELS = 6
GIRTS = [round(0.6 + i * (LEG_TOP_Z - 0.2 - 0.6) / _N_PANELS, 3) for i in range(_N_PANELS + 1)]

# stairs
POST_X = [-1.72, -0.76, 0.20]
POST_Y = 1.72
POST = 0.14
LANE_A = (POST_X[0] + POST / 2, POST_X[1] - POST / 2)     # west lane  (-1.65, -0.83)
LANE_B = (POST_X[1] + POST / 2, POST_X[2] - POST / 2)     # east lane  (-0.69, 0.13)
LAND_Y = 0.86          # landings occupy |y| in [0.86, 1.72]
N_LOW = 20             # must be even: the last low flight ends on the south landing
RISERS = 8
LOW_TOP = FLOOR_Z - 2.40   # top of the switchbacks; the steep ship's ladder climbs the last 2.4 m
RISE = LOW_TOP / (N_LOW * RISERS)          # 0.16875
GOING = (2 * LAND_Y) / (RISERS - 1)         # 0.2457
STEEP_RISERS = 12
STEEP_RISE = (FLOOR_Z - LOW_TOP) / STEEP_RISERS   # 0.2
STEEP_GOING = 0.10
FLOOR_COL_T = 0.15     # thickness of the cab-floor collider slab (the head bumps its underside)


def _hatch_south_edge(player_h=1.80, player_r=0.30, margin=0.10):
    """How far south the hatch must start so a standing player's head clears the floor while climbing.

    On the 63-degree ship's ladder a CharacterController capsule rides r/cos(a) - r above the slope, so its
    head reaches the underside of the floor slab almost at the foot of the flight. The opening has to begin
    a capsule radius (plus skin and margin) south of that point."""
    slope = STEEP_RISE / STEEP_GOING
    lift = player_r / math.cos(math.atan(slope)) - player_r
    y_foot = -LAND_Y - STEEP_GOING                  # where the ramp surface leaves the landing
    slab_bottom = FLOOR_Z - FLOOR_COL_T
    y_head = y_foot + (slab_bottom - player_h - lift - LOW_TOP) / slope
    return round(y_head - player_r - margin, 2)    # -1.32


HATCH = dict(x0=LANE_A[0], x1=LANE_A[1], y0=_hatch_south_edge(),
             y1=-LAND_Y + (STEEP_RISERS - 1) * STEEP_GOING + 0.04)

# door / windows
DOOR_W, DOOR_H = 0.86, 2.0
N_BAYS, MULL = 5, 0.10
BAY_W = (2 * CAB - (N_BAYS + 1) * MULL) / N_BAYS
DOOR_BAY = N_BAYS // 2

MATS = {
    "M_Timber": ("Timber", 1.0), "M_PlanksWeathered": ("PlanksWeathered", 2.0),
    "M_PlanksInterior": ("PlanksInterior", 2.0), "M_Siding": ("Siding", 2.0), "M_TrimGreen": ("TrimGreen", 1.0),
    "M_TrimCream": ("TrimCream", 1.0), "M_Paneling": ("Paneling", 2.0), "M_Roof": ("RoofShakes", 2.0),
    "M_Iron": ("Iron", 1.0), "M_Concrete": ("Concrete", 1.0), "M_Glass": ("GlassGrime", 1.0),
}


def legpos(sx, sy, z):
    t = (z - PIER_TOP) / (LEG_TOP_Z - PIER_TOP)
    h = LEG_BASE + (LEG_TOP - LEG_BASE) * t
    return vec(sx * h, sy * h, z)


def leg_half(z):
    return legpos(1, 1, z).x


# =================================================================== frame
def build_frame():
    m = MB("Tower_Frame")
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sy in corners:
        c = legpos(sx, sy, PIER_TOP)
        m.box((c.x - PIER / 2, c.y - PIER / 2, PIER_BOT), (c.x + PIER / 2, c.y + PIER / 2, PIER_TOP), "M_Concrete")
        # leg
        m.beam(legpos(sx, sy, PIER_TOP), legpos(sx, sy, LEG_TOP_Z), LEG, LEG, "M_Timber", up=(1, 0, 0))
        # iron anchor straps at the foot
        for ax in ("x", "y"):
            off = vec(sx * (LEG / 2 + 0.006), 0, 0) if ax == "x" else vec(0, sy * (LEG / 2 + 0.006), 0)
            p = c + off
            if ax == "x":
                m.box((p.x - 0.006, p.y - 0.05, PIER_TOP - 0.1), (p.x + 0.006, p.y + 0.05, PIER_TOP + 0.45), "M_Iron")
            else:
                m.box((p.x - 0.05, p.y - 0.006, PIER_TOP - 0.1), (p.x + 0.05, p.y + 0.006, PIER_TOP + 0.45), "M_Iron")
    # faces: (corner a, corner b, outward normal)
    faces = [((-1, -1), (1, -1), vec(0, -1, 0)), ((1, -1), (1, 1), vec(1, 0, 0)),
             ((1, 1), (-1, 1), vec(0, 1, 0)), ((-1, 1), (-1, -1), vec(-1, 0, 0))]
    GT, GH = 0.08, 0.20
    BT, BH = 0.06, 0.15
    for a, b, n in faces:
        south = n.y < -0.5
        for z in GIRTS:
            if south and z == GIRTS[0]:
                continue                      # walk-in opening on the south face
            off = n * (LEG / 2 + GT / 2)
            m.beam(legpos(*a, z) + off, legpos(*b, z) + off, GT, GH, "M_Timber", up=(0, 0, 1), extend=0.16)
        if south:
            # portal knee braces instead of the bottom X-brace
            off = n * (LEG / 2 + GT + BT / 2)
            for c, dirx in ((a, 1), (b, -1)):
                p0 = legpos(*c, 1.75) + off
                p1 = legpos(*c, GIRTS[1] - 0.05) + off + vec(dirx * 1.35, 0, 0)
                m.beam(p0, p1, BT, BH, "M_Timber", up=(0, 0, 1), extend=0.04)
        for i in range(len(GIRTS) - 1):
            if south and i == 0:
                continue
            z0, z1 = GIRTS[i] + 0.1, GIRTS[i + 1] - 0.1
            for k, (p, q) in enumerate([(legpos(*a, z0), legpos(*b, z1)), (legpos(*b, z0), legpos(*a, z1))]):
                off = n * (LEG / 2 + GT + BT / 2 + k * BT)
                m.beam(p + off, q + off, BT, BH, "M_Timber", up=(0, 0, 1), extend=0.05)
                # bolts where the brace crosses the legs
                for pt in (p, q):
                    bp = pt + n * (LEG / 2 + GT + BT * (k + 1) + 0.004)
                    m.cyl(bp - n * 0.004, bp + n * 0.02, 0.022, segs=6, mat="M_Iron")
            # bolt at the X crossing
            mid = (legpos(*a, (z0 + z1) / 2) + legpos(*b, (z0 + z1) / 2)) / 2
            bp = mid + n * (LEG / 2 + GT + 2 * BT)
            m.cyl(bp, bp + n * 0.025, 0.025, segs=6, mat="M_Iron")
    # main beams (along Y) on leg tops, cantilevered to carry the catwalk
    for sx in (-1, 1):
        x = sx * LEG_TOP
        m.box((x - 0.125, -WALK + 0.03, LEG_TOP_Z), (x + 0.125, WALK - 0.03, LEG_TOP_Z + BEAM_H), "M_Timber", grain="y")
    # joists (along X), trimmed around the hatch
    jz0, jz1 = LEG_TOP_Z + BEAM_H, LEG_TOP_Z + BEAM_H + JOIST_H
    _n = round(2 * (WALK - 0.15) / 0.4) + 1
    ys = [-(WALK - 0.15) + i * 2 * (WALK - 0.15) / (_n - 1) for i in range(_n)]
    hx0, hx1 = HATCH["x0"] - JOIST_W, HATCH["x1"] + JOIST_W
    oy0, oy1 = HATCH["y0"] - JOIST_W, HATCH["y1"] + JOIST_W      # opening incl. its headers
    for y in ys:
        if y + JOIST_W / 2 > oy0 and y - JOIST_W / 2 < oy1:          # stops at the trimmers
            m.box((-WALK, y - JOIST_W / 2, jz0), (hx0, y + JOIST_W / 2, jz1), "M_Timber")
            m.box((hx1, y - JOIST_W / 2, jz0), (WALK, y + JOIST_W / 2, jz1), "M_Timber")
        else:
            m.box((-WALK, y - JOIST_W / 2, jz0), (WALK, y + JOIST_W / 2, jz1), "M_Timber")
    # hatch trimmers + headers (north and south ends of the opening, fitted between the trimmers)
    for xa, xb in ((hx0, HATCH["x0"]), (HATCH["x1"], hx1)):
        m.box((xa, oy0, jz0), (xb, oy1, jz1), "M_Timber", grain="y")
    m.box((HATCH["x0"], HATCH["y1"], jz0), (HATCH["x1"], oy1, jz1), "M_Timber")
    m.box((HATCH["x0"], oy0, jz0), (HATCH["x1"], HATCH["y0"], jz1), "M_Timber")
    # rim joists / fascia around the catwalk edge
    RT = 0.035
    for sx in (-1, 1):
        m.box((sx * WALK, -WALK - RT, jz0 - 0.05), (sx * (WALK + RT), WALK + RT, FLOOR_Z + 0.008), "M_Timber", grain="y")
    for sy in (-1, 1):
        m.box((-WALK, sy * WALK, jz0 - 0.05), (WALK, sy * (WALK + RT), FLOOR_Z + 0.008), "M_Timber")
    # knee braces under the catwalk
    for sx, sy in corners:
        lp = legpos(sx, sy, LEG_TOP_Z - 1.5)
        m.beam(lp + vec(sx * 0.13, 0, 0), vec(sx * (WALK - 0.25), sy * LEG_TOP, jz0 - 0.02), 0.10, 0.14, "M_Timber",
               up=(0, 0, 1), extend=0.03)
        m.beam(lp + vec(0, sy * 0.13, 0), vec(sx * LEG_TOP, sy * (WALK - 0.25), LEG_TOP_Z - 0.02), 0.10, 0.14,
               "M_Timber", up=(0, 0, 1), extend=0.03)
    return m


# =================================================================== stairs
def flight(m, col, lane, y_start, z_start, direction, risers, rise, going, steep=False):
    """Open-riser flight. direction +1 = north (+y). Returns top (y, z)."""
    x0, x1 = lane
    treads = risers - 1
    tread_d = going + (0.03 if not steep else 0.05)
    tt = 0.04
    for i in range(treads):
        z = z_start + rise * (i + 1)
        y_front = y_start + direction * going * (i + 1) if not steep else y_start + direction * going * (i + 1)
        # tread spans from the riser line back by tread_d
        ya = y_start + direction * going * i
        yb = ya + direction * tread_d
        lo, hi = min(ya, yb), max(ya, yb)
        m.box((x0 + 0.05, lo, z - tt), (x1 - 0.05, hi, z), "M_PlanksWeathered", grain="x")
    run = going * treads
    y_end = y_start + direction * run
    z_end = z_start + rise * risers
    pa = vec((x0 + x1) / 2, y_start - direction * going, z_start)
    pb = vec((x0 + x1) / 2, y_end, z_end)
    dirv = (pb - pa).normalized()
    nrm = vec(0, 0, 1) - dirv * dirv.z
    nrm.normalize()
    drop = 0.16 if steep else 0.10
    for xs in (x0 + 0.025, x1 - 0.025):
        p0 = vec(xs, pa.y, pa.z)
        p1 = vec(xs, pb.y, pb.z)
        m.beam(p0 - nrm * drop, p1 - nrm * drop, 0.05, 0.26, "M_Timber", up=(0, 0, 1))
    col.beam(pa - nrm * 0.06, pb - nrm * 0.06, x1 - x0, 0.12, "COL", up=(0, 0, 1), extend=0.02)
    return y_end, z_end


def handrail(m, p0, p1, h=0.9, posts=True, mid=True):
    p0, p1 = vec(p0), vec(p1)
    top0, top1 = p0 + vec(0, 0, h), p1 + vec(0, 0, h)
    m.beam(top0, top1, 0.05, 0.07, "M_Timber", up=(0, 0, 1), extend=0.03)
    if mid:
        m.beam(p0 + vec(0, 0, h * 0.5), p1 + vec(0, 0, h * 0.5), 0.035, 0.08, "M_Timber", up=(0, 0, 1))
    if posts:
        for p in (p0, p1):
            m.box((p.x - 0.04, p.y - 0.04, p.z), (p.x + 0.04, p.y + 0.04, p.z + h + 0.04), "M_Timber", grain="z")


def build_stairs(col):
    m = MB("Tower_Stairs")
    # posts on small footings
    for x in POST_X:
        for y in (-POST_Y, POST_Y):
            m.box((x - 0.2, y - 0.2, -0.4), (x + 0.2, y + 0.2, 0.08), "M_Concrete")
            m.box((x - POST / 2, y - POST / 2, 0.08), (x + POST / 2, y + POST / 2, LEG_TOP_Z + BEAM_H), "M_Timber", grain="z")
    # stair column side girts tying posts every landing level
    levels = [RISE * RISERS * k for k in range(1, N_LOW + 1)]
    ycur, zcur = -LAND_Y, 0.0
    lanes = [LANE_A, LANE_B]
    for k in range(N_LOW):
        lane = lanes[k % 2]
        d = 1 if k % 2 == 0 else -1
        ystart = -LAND_Y if d == 1 else LAND_Y
        flight(m, col, lane, ystart, zcur, d, RISERS, RISE, GOING)
        zcur += RISE * RISERS
        # landing at the far end
        ly0, ly1 = (LAND_Y, POST_Y) if d == 1 else (-POST_Y, -LAND_Y)
        x0, x1 = LANE_A[0], LANE_B[1]
        m.box((x0, ly0, zcur - 0.04), (x1, ly1, zcur), "M_PlanksWeathered", grain="x")
        # landing framing
        for xx in (x0 + 0.04, (x0 + x1) / 2, x1 - 0.04):
            m.box((xx - 0.03, ly0, zcur - 0.22), (xx + 0.03, ly1, zcur - 0.04), "M_Timber", grain="y")
        for yy in (ly0 + 0.04, ly1 - 0.04):
            m.box((POST_X[0], yy - 0.03, zcur - 0.22), (POST_X[2], yy + 0.03, zcur - 0.04), "M_Timber")
        col.box((x0, ly0, zcur - 0.12), (x1, ly1, zcur), "COL")
        # rails around the landing (outer edge and lane sides)
        ye = ly1 if d == 1 else ly0
        handrail(m, (POST_X[0], ye, zcur), (POST_X[2], ye, zcur))
    # rails along flights: outer sides of the column and the lane divider
    zb = 0.0
    for k in range(N_LOW):
        d = 1 if k % 2 == 0 else -1
        y0 = -LAND_Y if d == 1 else LAND_Y
        y1 = -y0
        z0, z1 = zb, zb + RISE * RISERS
        lane = lanes[k % 2]
        outer_x = POST_X[0] if k % 2 == 0 else POST_X[2]
        handrail(m, (outer_x, y0, z0), (outer_x, y1, z1), h=0.95)
        handrail(m, (POST_X[1], y0, z0), (POST_X[1], y1, z1), h=0.95)
        # rails along the landing sides (outer columns)
        zb = z1
    for k in range(N_LOW):
        z = RISE * RISERS * (k + 1)
        ly = (LAND_Y, POST_Y) if k % 2 == 0 else (-POST_Y, -LAND_Y)
        for x in (POST_X[0], POST_X[2]):
            handrail(m, (x, ly[0], z), (x, ly[1], z), h=0.95, posts=False)
    # steep final stair up through the hatch (lane A, northbound from the top landing)
    flight(m, col, LANE_A, -LAND_Y, LOW_TOP, 1, STEEP_RISERS, STEEP_RISE, STEEP_GOING, steep=True)
    slope = (STEEP_RISE * STEEP_RISERS) / (STEEP_GOING * STEEP_RISERS)
    y_cut = -LAND_Y - STEEP_GOING + (FLOOR_Z - 0.06 - 0.9 - LOW_TOP) / slope
    for xs in (LANE_A[0] + 0.02, LANE_A[1] - 0.02):
        handrail(m, (xs, -LAND_Y - STEEP_GOING, LOW_TOP), (xs, y_cut, FLOOR_Z - 0.06 - 0.9), h=0.9, posts=False, mid=False)
    # safety colliders: column perimeter and lane divider (invisible walls)
    W = 0.06
    x0, x1 = POST_X[0] - POST / 2, POST_X[2] + POST / 2
    col.box((x0 - W, -POST_Y, 0), (x0, POST_Y, LEG_TOP_Z), "COL")
    col.box((x1, -POST_Y, 0), (x1 + W, POST_Y, LEG_TOP_Z), "COL")
    col.box((x0, POST_Y, 0), (x1, POST_Y + W, LEG_TOP_Z), "COL")
    # south side: leave the ground-level entry to lane A open
    col.box((x0, -POST_Y - W, 2.1), (x1, -POST_Y, LEG_TOP_Z), "COL")
    col.box((LANE_B[0] - 0.07, -POST_Y - W, 0), (x1, -POST_Y, 2.1), "COL")
    col.box((POST_X[1] - 0.03, -LAND_Y, 0), (POST_X[1] + 0.03, LAND_Y, LEG_TOP_Z), "COL")
    # ground-entry handrail flare
    handrail(m, (POST_X[1], -POST_Y, 0.0), (POST_X[1], -LAND_Y, 0.0), h=0.95)
    m.box((LANE_A[0], -POST_Y - 0.3, -0.05), (LANE_A[1], -POST_Y + 0.1, 0.02), "M_Concrete")
    return m


# =================================================================== cab
def side_frame(side):
    """Return (origin on outer face at s=0,z=0, along-s unit, inward unit) for a cab side."""
    if side == "S":
        return vec(0, -CAB, 0), vec(1, 0, 0), vec(0, 1, 0)
    if side == "N":
        return vec(0, CAB, 0), vec(-1, 0, 0), vec(0, -1, 0)
    if side == "E":
        return vec(CAB, 0, 0), vec(0, 1, 0), vec(-1, 0, 0)
    return vec(-CAB, 0, 0), vec(0, -1, 0), vec(1, 0, 0)


SIDE_UOFF = dict(S=0.0, E=7.3, N=13.1, W=19.7)


def wall_block(m, side, s0, s1, z0, z1, d0, d1, mat_out, mat_in, mat_edge, faces="all"):
    """A block in side coordinates: s along the wall, d depth from outer face (0) inward.
    UVs: U = -s (+side offset), V = world z, so siding laps line up across blocks."""
    o, es, ein = side_frame(side)
    ez = vec(0, 0, 1)
    eout = -ein
    sy = d1 - d0
    ex, ey = es, eout
    origin = o + es * s0 + ein * d1 + ez * z0
    uoff = s0
    if ex.cross(ey).dot(ez) < 0:
        origin = o + es * s1 + ein * d1 + ez * z0
        ex = -es
        uoff = -s1
    uvoff = (uoff + SIDE_UOFF[side], z0)
    keys = {"+y": mat_out, "-y": mat_in}
    lo_end = "+x" if uoff == -s1 else "-x"     # local face at s0
    hi_end = "-x" if uoff == -s1 else "+x"     # local face at s1
    skip = set()
    if s0 <= -CAB + 1e-6:
        skip.add(lo_end)
    if s1 >= CAB - 1e-6:
        skip.add(hi_end)
    for k in ("+y", "-y", "+x", "-x", "+z", "-z"):
        if faces != "all" and k not in faces:
            continue
        if k in skip:
            continue
        mat = keys.get(k, mat_edge)
        if mat is None:
            continue
        m.obox(origin, ex, ey, ez, s1 - s0, sy, z1 - z0, mat, "len", uvoff, {k})


def bays(side):
    """Window bays along a side: list of (s0, s1, kind)."""
    out = []
    for i in range(N_BAYS):
        s0 = -CAB + MULL + i * (BAY_W + MULL)
        out.append((s0, s0 + BAY_W, "door" if (side == "S" and i == DOOR_BAY) else "window"))
    return out


def build_cab(col):
    m = MB("Cab_Shell")
    # interior floor (boards run along Y), hatch hole
    z0, z1 = FLOOR_Z - DECK_T, FLOOR_Z
    hx0, hx1, hy0, hy1 = HATCH["x0"], HATCH["x1"], HATCH["y0"], HATCH["y1"]
    rects = [(-CAB, hx0, -CAB, CAB), (hx1, CAB, -CAB, CAB), (hx0, hx1, -CAB, hy0), (hx0, hx1, hy1, CAB)]
    for (a, b, c, d) in rects:
        m.box((a, c, z0), (b, d, z1), "M_PlanksInterior", grain="y")
        col.box((a, c, z1 - FLOOR_COL_T), (b, d, z1), "COL")
    # ledger strips inside the hatch opening
    m.box((hx0 - 0.03, hy0, z0 - 0.06), (hx0, hy1, z1 - 0.005), "M_Timber", grain="y")
    m.box((hx1, hy0, z0 - 0.06), (hx1 + 0.03, hy1, z1 - 0.005), "M_Timber", grain="y")
    # catwalk deck
    for (a, b, c, d) in [(-WALK, WALK, -WALK, -CAB), (-WALK, WALK, CAB, WALK), (-WALK, -CAB, -CAB, CAB), (CAB, WALK, -CAB, CAB)]:
        m.box((a, c, z0), (b, d, z1), "M_PlanksWeathered", grain="y")
        col.box((a, c, z1 - 0.15), (b, d, z1), "COL")
    # catwalk railing
    ps = [-WALK + 0.06 + i * (2 * (WALK - 0.06) / 10) for i in range(11)]
    rail_pts = []
    for p in ps:
        for (x, y) in [(p, -WALK + 0.06), (p, WALK - 0.06), (-WALK + 0.06, p), (WALK - 0.06, p)]:
            rail_pts.append((round(x, 3), round(y, 3)))
    for (x, y) in set(rail_pts):
        m.box((x - 0.045, y - 0.045, FLOOR_Z), (x + 0.045, y + 0.045, FLOOR_Z + RAIL_H), "M_Timber", grain="z")
    e = WALK - 0.06
    for (a, b) in [((-e, -e), (e, -e)), ((e, -e), (e, e)), ((e, e), (-e, e)), ((-e, e), (-e, -e))]:
        pa, pb = vec(a[0], a[1], 0), vec(b[0], b[1], 0)
        m.beam(pa + vec(0, 0, FLOOR_Z + RAIL_H + 0.025), pb + vec(0, 0, FLOOR_Z + RAIL_H + 0.025), 0.13, 0.05,
               "M_Timber", up=(0, 0, 1), extend=0.07)
        for hz in (0.36, 0.70):
            m.beam(pa + vec(0, 0, FLOOR_Z + hz), pb + vec(0, 0, FLOOR_Z + hz), 0.035, 0.09, "M_Timber", up=(0, 0, 1),
                   extend=0.05)
        # toe board
        m.beam(pa + vec(0, 0, FLOOR_Z + 0.06), pb + vec(0, 0, FLOOR_Z + 0.06), 0.025, 0.12, "M_Timber", up=(0, 0, 1),
               extend=0.05)
    for (a, b, c, d) in [(-WALK, WALK, -WALK, -WALK + 0.1), (-WALK, WALK, WALK - 0.1, WALK),
                         (-WALK, -WALK + 0.1, -WALK, WALK), (WALK - 0.1, WALK, -WALK, WALK)]:
        col.box((a, c, FLOOR_Z), (b, d, FLOOR_Z + RAIL_H + 0.1), "COL")

    # walls
    for side in "SNEW":
        bl = bays(side)
        # lower wall (wainscot) - full length, except the door gap on S
        segs = [(-CAB, CAB)]
        if side == "S":
            segs = [(-CAB, -DOOR_W / 2), (DOOR_W / 2, CAB)]
        for s0, s1 in segs:
            wall_block(m, side, s0, s1, FLOOR_Z, SILL_Z - 0.04, 0.0, WALL_T, "M_Siding", "M_Paneling", "M_TrimCream")
            _wall_col(col, side, s0, s1, FLOOR_Z, SILL_Z)
        # sills: exterior (projecting, green) and interior stool (cream)
        for s0, s1, kind in bl:
            if kind == "door":
                continue
            wall_block(m, side, s0 - 0.04, s1 + 0.04, SILL_Z - 0.04, SILL_Z, -0.07, 0.05, "M_TrimGreen", "M_TrimGreen",
                       "M_TrimGreen")
            wall_block(m, side, s0 - 0.02, s1 + 0.02, SILL_Z - 0.04, SILL_Z, 0.07, WALL_T + 0.05, "M_TrimCream",
                       "M_TrimCream", "M_TrimCream")
        # corners + mullions (full depth posts in the window band)
        posts = [(-CAB, -CAB + MULL)] + [(bl[i][1], bl[i + 1][0]) for i in range(N_BAYS - 1)] + [(CAB - MULL, CAB)]
        for s0, s1 in posts:
            wall_block(m, side, s0, s1, SILL_Z, WIN_TOP, 0.0, WALL_T, "M_TrimGreen", "M_TrimCream", "M_TrimCream")
            _wall_col(col, side, s0, s1, SILL_Z, WIN_TOP)
        # header band above the windows (exterior frieze green, interior paneling)
        wall_block(m, side, -CAB, CAB, WIN_TOP, EAVE_Z, 0.0, WALL_T, "M_TrimGreen", "M_Paneling", "M_TrimCream")
        _wall_col(col, side, -CAB, CAB, WIN_TOP, EAVE_Z)
        # window head stops and jamb liners (thin trim inside each opening)
        for s0, s1, kind in bl:
            if kind == "door":
                continue
            wall_block(m, side, s0, s1, WIN_TOP - 0.03, WIN_TOP, 0.07, WALL_T, None, "M_TrimCream", "M_TrimCream")
        # door bay
        if side == "S":
            b0, b1, _ = bl[DOOR_BAY]
            for s0, s1 in ((b0, -DOOR_W / 2), (DOOR_W / 2, b1)):
                wall_block(m, side, s0, s1, SILL_Z - 0.04, WIN_TOP, 0.0, WALL_T, "M_Siding", "M_Paneling", "M_TrimCream")
                _wall_col(col, side, s0, s1, FLOOR_Z, WIN_TOP)
            top = FLOOR_Z + DOOR_H
            wall_block(m, side, -DOOR_W / 2, DOOR_W / 2, top, WIN_TOP, 0.0, WALL_T, "M_Siding", "M_Paneling", "M_TrimCream")
            # door casing (exterior)
            for s0, s1 in ((-DOOR_W / 2 - 0.09, -DOOR_W / 2), (DOOR_W / 2, DOOR_W / 2 + 0.09)):
                wall_block(m, side, s0, s1, FLOOR_Z, top + 0.09, -0.025, 0.0, "M_TrimGreen", "M_TrimGreen", "M_TrimGreen")
            wall_block(m, side, -DOOR_W / 2 - 0.09, DOOR_W / 2 + 0.09, top, top + 0.11, -0.03, 0.0, "M_TrimGreen",
                       "M_TrimGreen", "M_TrimGreen")
            # threshold + jambs
            wall_block(m, side, -DOOR_W / 2, DOOR_W / 2, FLOOR_Z, FLOOR_Z + 0.02, -0.04, WALL_T, "M_Timber", "M_Timber",
                       "M_Timber")
            for s0, s1 in ((-DOOR_W / 2, -DOOR_W / 2 + 0.02), (DOOR_W / 2 - 0.02, DOOR_W / 2)):
                wall_block(m, side, s0, s1, FLOOR_Z, top, 0.05, WALL_T, "M_TrimCream", "M_TrimCream", "M_TrimCream")
        # corner boards outside
        o, es, ein = side_frame(side)
        for s0, s1 in ((-CAB - 0.025, -CAB + 0.11), (CAB - 0.11, CAB + 0.025)):
            wall_block(m, side, s0, s1, FLOOR_Z, EAVE_Z, -0.025, 0.0, "M_TrimGreen", "M_TrimGreen", "M_TrimGreen")
        # skirt board at the floor line
        wall_block(m, side, -CAB - 0.025, CAB + 0.025, FLOOR_Z - 0.02, FLOOR_Z + 0.14, -0.022, 0.0, "M_TrimGreen",
                   "M_TrimGreen", "M_TrimGreen")
        # interior baseboard
        wall_block(m, side, -CIN, CIN, FLOOR_Z, FLOOR_Z + 0.1, WALL_T, WALL_T + 0.015, "M_TrimCream", "M_TrimCream",
                   "M_TrimCream") if side != "S" else None
    for s0, s1 in ((-CIN, -DOOR_W / 2), (DOOR_W / 2, CIN)):
        wall_block(m, "S", s0, s1, FLOOR_Z, FLOOR_Z + 0.1, WALL_T, WALL_T + 0.015, "M_TrimCream", "M_TrimCream",
                   "M_TrimCream")
    # ceiling (paneling underside) + two exposed beams
    m.box((-CIN, -CIN, CEIL_Z), (CIN, CIN, CEIL_Z + 0.03), "M_Paneling", grain="y")
    for y in (-1.9, 1.9):
        m.box((-CIN, y - 0.05, CEIL_Z - 0.12), (CIN, y + 0.05, CEIL_Z), "M_Timber")
    # stovepipe through the ceiling: thimble ring
    m.ring((STOVE[0], STOVE[1], 0), 0.078, 0.13, CEIL_Z - 0.01, CEIL_Z + 0.0, 12, "M_Iron")
    return m


def _wall_col(col, side, s0, s1, z0, z1):
    o, es, ein = side_frame(side)
    a = o + es * s0
    b = o + es * s1 + ein * WALL_T
    col.box((min(a.x, b.x), min(a.y, b.y), z0), (max(a.x, b.x), max(a.y, b.y), z1), "COL")


# =================================================================== roof
def roof_z(r):
    """Top surface height at 'radius' r = max(|x|,|y|)."""
    return EAVE_Z + (CAB - r) * math.tan(PITCH)


def build_roof():
    m = MB("Cab_Roof")
    E = CAB + ROOF_OVER
    T = 0.12
    zt_edge = roof_z(E)
    apex = vec(0, 0, roof_z(0))
    corners = [vec(-E, -E, zt_edge), vec(E, -E, zt_edge), vec(E, E, zt_edge), vec(-E, E, zt_edge)]
    slope_len = math.hypot(E, apex.z - zt_edge)
    for i in range(4):
        a, b = corners[i], corners[(i + 1) % 4]
        ux = (b - a).normalized()
        mid = (a + b) / 2
        upv = (apex - mid).normalized()
        # top (shakes), UV: U along eave, V up-slope
        m.tri(a, b, apex, "M_Roof", ux=ux, uy=upv, origin=a)
        # soffit underneath, offset down by T
        dz = vec(0, 0, -T)
        m.tri(b + dz, a + dz, apex + dz, "M_Timber", ux=-ux, uy=upv, origin=b + dz)
        # fascia
        m.quad(a + dz, b + dz, b, a, "M_TrimGreen", ux=ux, uy=(0, 0, 1))
        # hip cap
        m.beam(a + vec(0, 0, 0.02), apex + vec(0, 0, 0.02), 0.16, 0.05, "M_Roof",
               up=((a - vec(0, 0, a.z)).normalized() * -0.4 + vec(0, 0, 1)), extend=0.02)
    # rafter tails / outlookers visible under the eaves
    for side in range(4):
        for k in range(-6, 7):
            s = k * 0.52
            ang = side * math.pi / 2
            ca, sa = math.cos(ang), math.sin(ang)
            p_in = vec(0, -CAB, 0)
            p_out = vec(0, -(E - 0.05), 0)
            rot = Matrix.Rotation(ang, 3, "Z")
            pa = rot @ (p_in + vec(s, 0, 0))
            pb = rot @ (p_out + vec(s, 0, 0))
            pa.z = roof_z(CAB) - T - 0.075
            pb.z = roof_z(E - 0.05) - T - 0.075
            m.beam(pa, pb, 0.05, 0.14, "M_Timber", up=(0, 0, 1))
    # lightning rod
    m.cyl(apex + vec(0, 0, -0.05), apex + vec(0, 0, 0.12), 0.05, 0.03, segs=8, mat="M_Iron")
    m.cyl(apex + vec(0, 0, 0.1), apex + vec(0, 0, 1.05), 0.012, segs=6, mat="M_Iron")
    m.cyl(apex + vec(0, 0, 1.05), apex + vec(0, 0, 1.2), 0.012, 0.001, segs=6, mat="M_Iron")
    m.cyl(apex + vec(0, 0, 0.45), apex + vec(0, 0, 0.55), 0.045, segs=10, mat="M_Glass")
    # ground cable down one hip and the SE corner
    m.tube_path([apex + vec(0.03, -0.03, 0.06), corners[1] + vec(-0.1, 0.1, 0.03), corners[1] + vec(-0.35, 0.35, -0.2),
                 vec(CAB + 0.03, -CAB - 0.03, EAVE_Z - 0.3), vec(CAB + 0.03, -CAB - 0.03, FLOOR_Z + 0.2)], 0.008, segs=5)
    # stovepipe stub + cap
    sx, sy = STOVE
    zr = roof_z(max(sx, sy))
    m.cyl((sx, sy, CEIL_Z), (sx, sy, zr + 0.75), 0.075, segs=12, mat="M_Iron", caps=(False, False))
    m.cyl((sx, sy, zr - 0.02), (sx, sy, zr + 0.08), 0.16, 0.08, segs=12, mat="M_Iron", caps=(True, False))
    m.cyl((sx, sy, zr + 0.86), (sx, sy, zr + 0.96), 0.17, 0.03, segs=12, mat="M_Iron")
    for k in range(3):
        a = k * 2 * math.pi / 3
        p = vec(sx + 0.07 * math.cos(a), sy + 0.07 * math.sin(a), 0)
        m.cyl((p.x, p.y, zr + 0.72), (p.x, p.y, zr + 0.87), 0.006, segs=4, mat="M_Iron")
    # radio antenna mast on the west wall
    ax, ay = -CAB - 0.08, -2.6
    m.cyl((ax, ay, SILL_Z - 0.2), (ax, ay, apex.z + 1.4), 0.025, segs=8, mat="M_Iron")
    m.cyl((ax, ay, apex.z + 1.4), (ax, ay, apex.z + 2.6), 0.008, segs=5, mat="M_Iron")
    for z in (SILL_Z, EAVE_Z - 0.2):
        m.box((ax - 0.01, ay - 0.04, z), (-CAB, ay + 0.04, z + 0.05), "M_Iron")
    for k in range(4):
        a = k * math.pi / 2 + 0.4
        m.cyl((ax, ay, apex.z + 1.2), (ax + 0.45 * math.cos(a), ay + 0.45 * math.sin(a), apex.z + 0.95), 0.006, segs=4,
              mat="M_Iron")
    return m


# =================================================================== openables
def build_door():
    o = vec(-DOOR_W / 2, -CAB, FLOOR_Z)            # hinge line on the outer face, west jamb
    m = MB("Door", origin=o)
    W, H, T = DOOR_W - 0.01, DOOR_H - 0.01, 0.045
    x0 = o.x + 0.005
    y0, y1 = -CAB + 0.003, -CAB + 0.003 + T
    z0 = FLOOR_Z + 0.005

    def slab(a, b, c, d, mat="M_TrimGreen", dy0=0.0, dy1=0.0):
        m.box((x0 + a, y0 + dy0, z0 + c), (x0 + b, y1 - dy1, z0 + d), mat, grain="z" if (b - a) < (d - c) else "x")

    st, tr, br, mr = 0.11, 0.12, 0.24, 0.10
    glass_bot = 0.98
    slab(0, st, 0, H)
    slab(W - st, W, 0, H)
    slab(st, W - st, H - tr, H)
    slab(st, W - st, 0, br)
    slab(st, W - st, glass_bot - mr, glass_bot)
    slab(st, W - st, br, glass_bot - mr, dy0=0.012, dy1=0.012)  # recessed panel
    # glass 2x2 with muntins
    gx0, gx1, gz0, gz1 = st, W - st, glass_bot, H - tr
    mx = (gx0 + gx1) / 2
    mz = (gz0 + gz1) / 2
    slab(mx - 0.015, mx + 0.015, gz0, gz1, dy0=0.008, dy1=0.008)
    slab(gx0, gx1, mz - 0.015, mz + 0.015, dy0=0.008, dy1=0.008)
    m.obox((x0 + gx0, (y0 + y1) / 2 - 0.003, z0 + gz0), (1, 0, 0), (0, 1, 0), (0, 0, 1), gx1 - gx0, 0.006, gz1 - gz0,
           "M_Glass", uv="unit")
    # knob + rose on both faces, lock plate
    kx = x0 + W - 0.07
    kz = z0 + 0.98
    for sgn, yb in ((-1, y0), (1, y1)):
        m.cyl((kx, yb, kz), (kx, yb + sgn * 0.012, kz), 0.03, segs=12, mat="M_Iron")
        m.cyl((kx, yb + sgn * 0.012, kz), (kx, yb + sgn * 0.06, kz), 0.009, segs=8, mat="M_Iron")
        m.cyl((kx, yb + sgn * 0.06, kz), (kx, yb + sgn * 0.08, kz), 0.028, segs=12, mat="M_Iron")
    m.box((kx - 0.03, y0 - 0.006, kz + 0.06), (kx + 0.03, y0, kz + 0.16), "M_Iron")
    # strap hinges on the outside face
    for hz in (0.25, H - 0.3):
        m.box((x0 - 0.01, y0 - 0.006, z0 + hz), (x0 + 0.32, y0, z0 + hz + 0.045), "M_Iron")
        m.cyl((o.x, o.y - 0.012, z0 + hz - 0.02), (o.x, o.y - 0.012, z0 + hz + 0.065), 0.012, segs=8, mat="M_Iron")
    return m


def build_trapdoor():
    hx0, hx1, hy0, hy1 = HATCH["x0"], HATCH["x1"], HATCH["y0"], HATCH["y1"]
    o = vec(hx0, (hy0 + hy1) / 2, FLOOR_Z)        # hinge along the west edge, at floor level
    m = MB("Trapdoor", origin=o)
    g = 0.006
    m.box((hx0 + g, hy0 + g, FLOOR_Z - DECK_T), (hx1 - g, hy1 - g, FLOOR_Z - 0.002), "M_PlanksInterior", grain="y")
    # battens underneath
    for y in (hy0 + 0.15, (hy0 + hy1) / 2, hy1 - 0.15):
        m.box((hx0 + 0.05, y - 0.04, FLOOR_Z - DECK_T - 0.03), (hx1 - 0.05, y + 0.04, FLOOR_Z - DECK_T), "M_Timber")
    m.beam((hx0 + 0.08, hy0 + 0.2, FLOOR_Z - DECK_T - 0.015), (hx1 - 0.08, hy1 - 0.2, FLOOR_Z - DECK_T - 0.015), 0.07,
           0.025, "M_Timber", up=(0, 0, -1))
    # flush ring pull (recessed plate + ring lying flat)
    rx, ry = hx1 - 0.12, (hy0 + hy1) / 2
    m.box((rx - 0.05, ry - 0.06, FLOOR_Z - 0.003), (rx + 0.05, ry + 0.06, FLOOR_Z - 0.001), "M_Iron")
    m.ring((rx, ry, 0), 0.035, 0.045, FLOOR_Z - 0.0015, FLOOR_Z + 0.008, 12, "M_Iron")
    # hinges
    for y in (hy0 + 0.2, hy1 - 0.2):
        m.box((hx0 - 0.02, y - 0.025, FLOOR_Z - 0.001), (hx0 + 0.25, y + 0.025, FLOOR_Z + 0.004), "M_Iron")
        m.cyl((hx0, y - 0.04, FLOOR_Z + 0.006), (hx0, y + 0.04, FLOOR_Z + 0.006), 0.009, segs=8, mat="M_Iron")
    # underside pull handle so it can be opened from the stairs
    m.tube_path([(rx - 0.05, ry - 0.08, FLOOR_Z - DECK_T), (rx - 0.05, ry - 0.08, FLOOR_Z - DECK_T - 0.07),
                 (rx - 0.05, ry + 0.08, FLOOR_Z - DECK_T - 0.07), (rx - 0.05, ry + 0.08, FLOOR_Z - DECK_T)], 0.01, segs=6)
    return m


def build_window(side, i, s0, s1):
    o, es, ein = side_frame(side)
    d0 = 0.015                     # sash outer face depth from wall outer face
    T = 0.045
    hinge = o + es * ((s0 + s1) / 2) + ein * d0 + vec(0, 0, WIN_TOP - 0.004)
    m = MB(f"Window_{side}{i}", origin=hinge)
    W = s1 - s0 - 0.008
    H = WIN_TOP - SILL_Z - 0.008
    st, tr, br, mu = 0.06, 0.06, 0.08, 0.028
    s_a = s0 + 0.004
    z_a = SILL_Z + 0.004

    def piece(a, b, c, d, mat="M_TrimGreen", inset=0.0):
        wall_block(m, side, s_a + a, s_a + b, z_a + c, z_a + d, d0 + inset, d0 + T - inset, mat, "M_TrimCream", mat)

    piece(0, st, 0, H)
    piece(W - st, W, 0, H)
    piece(st, W - st, H - tr, H)
    piece(st, W - st, 0, br)
    mxs = (st + (W - st)) / 2
    mzs = (br + (H - tr)) / 2
    piece(mxs - mu / 2, mxs + mu / 2, br, H - tr, inset=0.008)
    piece(st, W - st, mzs - mu / 2, mzs + mu / 2, inset=0.008)
    # glass panes (unit UVs per pane for the grime map)
    for (a, b) in ((st, mxs - mu / 2), (mxs + mu / 2, W - st)):
        for (c, d) in ((br, mzs - mu / 2), (mzs + mu / 2, H - tr)):
            oo, es_, ein_ = side_frame(side)
            p0 = oo + es_ * (s_a + a) + ein_ * (d0 + T / 2 + 0.003) + vec(0, 0, z_a + c)
            ex = es_
            ey = -ein_
            ez = vec(0, 0, 1)
            if ex.cross(ey).dot(ez) < 0:
                p0 = oo + es_ * (s_a + b) + ein_ * (d0 + T / 2 + 0.003) + vec(0, 0, z_a + c)
                ex = -es_
            m.obox(p0, ex, ey, ez, b - a, 0.006, d - c, "M_Glass", uv="unit")
    # small iron latch on the inside bottom rail + prop hook
    lp = o + es * ((s0 + s1) / 2) + ein * (d0 + T) + vec(0, 0, z_a + 0.03)
    m.box(lp + vec(0, 0, 0) - es * 0.04, lp + es * 0.04 + ein * 0.02 + vec(0, 0, 0.03), "M_Iron")
    return m


# =================================================================== main
def main(outdir):
    reset_scene()
    for name, (tex, tile) in MATS.items():
        register_material(name, tex, tile, alpha=(name == "M_Glass"))
    col = MB("COL_Static")
    frame = build_frame()
    stairs = build_stairs(col)
    cab = build_cab(col)
    roof = build_roof()
    # leg colliders near the ground
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = legpos(sx, sy, 1.0)
            col.box((c.x - 0.25, c.y - 0.25, -0.5), (c.x + 0.25, c.y + 0.25, 2.5), "COL")
    # bottom panel of the N/E/W faces is braced: block walking through it (south face is the way in)
    hh = leg_half(1.5) + 0.2
    col.box((-hh, hh - 0.08, -0.5), (hh, hh + 0.08, 2.9), "COL")
    col.box((hh - 0.08, -hh, -0.5), (hh + 0.08, hh, 2.9), "COL")
    col.box((-hh - 0.08, -hh, -0.5), (-hh + 0.08, hh, 2.9), "COL")
    openables = [build_door(), build_trapdoor()]
    for side in "SNEW":
        for i, (s0, s1, kind) in enumerate(bays(side)):
            if kind == "window":
                openables.append(build_window(side, i, s0, s1))
    objs = []
    stats = {}
    for mb in [frame, stairs, cab, roof] + openables:
        ob = mb.build()
        objs.append(ob)
        stats[mb.name] = mb.tri_count()
    colob = col.build()
    stats["COL_Static"] = col.tri_count()
    print(json.dumps(stats, indent=1))
    print("TOTAL visible tris:", sum(v for k, v in stats.items() if k != "COL_Static"))
    os.makedirs(outdir, exist_ok=True)
    export_fbx(os.path.join(outdir, "LookoutTower.fbx"), objs + [colob])
    meta = dict(
        floor_z=FLOOR_Z, cab_half=CAB, cab_inner=CIN, hatch=HATCH, door=dict(w=DOOR_W, h=DOOR_H),
        sill_z=SILL_Z, win_top=WIN_TOP, ceil_z=CEIL_Z, eave_z=EAVE_Z, walk=WALK, stove=STOVE,
        low_top=LOW_TOP, n_low=N_LOW, rise=RISE, lanes=dict(A=LANE_A, B=LANE_B), land_y=LAND_Y, stats=stats)
    json.dump(meta, open(os.path.join(outdir, "tower_meta.json"), "w"), indent=1)
    return objs, colob


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    outdir = argv[0] if argv else "/home/claude/lookout/out"
    objs, colob = main(outdir)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, "LookoutTower.blend"))
