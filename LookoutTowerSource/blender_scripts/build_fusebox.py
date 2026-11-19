"""Fuse box puzzle set (Blender, Z up, metres; Blender x,y,z -> Unity x,z,y), in the same style as build_props.py.

Everything is placed at its spot in the cab and exported together as FuseBox.fbx. Objects that move in Unity have
their origin on the pivot: the panel door on its hinge, the main switch handle on its spindle, and the lids on their
hinges. Empties mark where things go in Unity (fuse sockets, lights, spark point, text cards).

Layout (see the README for the puzzle):
  * Fuse panel + main switch on a plywood backboard on the solid strip of wall just east of the cab door
    (the only full-height wall in the cab), at eye height.
  * Conduit 1 (lights)   : up the wall and across the ceiling to a pendant lamp over the fire finder.
  * Conduit 2 (radio)    : along the south header, along the east header, down the mullion behind the desk to a
                           junction box, then a cloth cord to the radio.
  * Conduit 3 (catwalk)  : through the wall and up the outside to a caged lamp over the door.
  * Spare-fuse tin on the desk beside the lantern; parts crate on a wall shelf on the north wall, under window N1
    (clear of the fog-window puzzle on N2).

usage: python build_fusebox.py <outdir> [--with-cab]   (--with-cab also rebuilds the tower + props for previews)
"""
import math, sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
import bpy
from mathutils import Vector, Matrix
from bgeo import MB, vec, register_material, reset_scene, export_fbx
import build_tower as T

F = T.FLOOR_Z
WALL_S = -T.CIN                   # inside face of the south wall
DOOR_E = T.DOOR_W / 2             # east jamb of the door
BAY_E = -T.CAB + T.MULL + T.DOOR_BAY * (T.BAY_W + T.MULL) + T.BAY_W   # east edge of the door bay
EAST_IN = T.CIN                   # inside face of the east wall
MULL_Y = 0.72                     # centre of the mullion behind the north end of the desk

PANEL_X = 0.55                    # centre line of the panel strip
BOARD = dict(x0=0.445, x1=0.655, z0=F + 0.93, z1=F + 1.72, t=0.02)
CAB_BOX = dict(x0=0.455, x1=0.645, z0=F + 1.24, z1=F + 1.64, d=0.085)    # fuse cabinet
SW_BOX = dict(x0=0.48, x1=0.62, z0=F + 1.00, z1=F + 1.18, d=0.07)        # main switch
SOCKET_Z = [F + 1.555, F + 1.44, F + 1.325]                             # slots 1..3, top to bottom
SOCKET_X = 0.533
COND_R = 0.0105
EMPTIES = {}
STATS = {}


# ---------------------------------------------------------------------- helpers
def frame_for(axis):
    ez = vec(axis).normalized()
    t = vec(1, 0, 0) if abs(ez.x) < 0.9 else vec(0, 1, 0)
    ex = t.cross(ez).normalized()
    ey = ez.cross(ex)
    return ex, ey, ez


def lathe(m, base, axis, profile, segs, mat, smooth=True, inward=False, cap0=False, cap1=False, cap_mat=None):
    """Surface of revolution. profile = [(r, h), ...] along `axis` from `base`. Shared rings (smooth)."""
    base = vec(base)
    ex, ey, ez = frame_for(axis)
    rings = []
    for r, h in profile:
        ring = []
        for i in range(segs + 1):
            a = 2 * math.pi * i / segs
            ring.append(base + ez * h + (ex * math.cos(a) + ey * math.sin(a)) * r)
        rings.append(ring)
    circ = 2 * math.pi * max(r for r, _ in profile)
    vacc = [0.0]
    for k in range(1, len(profile)):
        (r0, h0), (r1, h1) = profile[k - 1], profile[k]
        vacc.append(vacc[-1] + math.hypot(r1 - r0, h1 - h0))
    n = segs + 1
    start = len(m.verts)
    for ring in rings:
        m.verts.extend(ring)
    for k in range(len(rings) - 1):
        for i in range(segs):
            a = start + k * n + i
            b = a + 1
            c = start + (k + 1) * n + i + 1
            d = start + (k + 1) * n + i
            f = [a, b, c, d]
            if inward:
                f = f[::-1]
            m.faces.append(f)
            u0, u1 = circ * i / segs, circ * (i + 1) / segs
            uv = [(u0, vacc[k]), (u1, vacc[k]), (u1, vacc[k + 1]), (u0, vacc[k + 1])]
            m.uvs.append(uv[::-1] if inward else uv)
            m.mats.append(mat)
            m.smooth.append(smooth)
    cm = cap_mat or mat
    if cap0 and profile[0][0] > 1e-5:
        pts = list(reversed(rings[0][:-1]))
        m.poly(pts, [(0.5 + 0.5 * (p - base).dot(ex) / profile[0][0], 0.5 + 0.5 * (p - base).dot(ey) / profile[0][0])
                     for p in pts], cm)
    if cap1 and profile[-1][0] > 1e-5:
        top = base + ez * profile[-1][1]
        pts = rings[-1][:-1]
        m.poly(pts, [(0.5 + 0.5 * (p - top).dot(ex) / profile[-1][0], 0.5 + 0.5 * (p - top).dot(ey) / profile[-1][0])
                     for p in pts], cm)


def annulus(m, c, axis, r_in, r_out, segs, mat):
    """Flat ring facing +axis."""
    c = vec(c)
    ex, ey, ez = frame_for(axis)
    for i in range(segs):
        a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
        d0 = ex * math.cos(a0) + ey * math.sin(a0)
        d1 = ex * math.cos(a1) + ey * math.sin(a1)
        m.quad(c + d0 * r_in, c + d0 * r_out, c + d1 * r_out, c + d1 * r_in, mat, ux=ex, uy=ey)


def conduit(m, pts, r=COND_R, mat="M_Iron", straps=True, strap_axis=None):
    """EMT conduit along a polyline with rounded elbows (a short swept bend at every corner) and pipe straps."""
    pts = [vec(p) for p in pts]
    bend = 0.045
    path = [pts[0]]
    for i in range(1, len(pts) - 1):
        a, b, c = pts[i - 1], pts[i], pts[i + 1]
        d0, d1 = (b - a).normalized(), (c - b).normalized()
        k0 = b - d0 * min(bend, (b - a).length * 0.45)
        k1 = b + d1 * min(bend, (c - b).length * 0.45)
        for s in range(0, 7):
            t = s / 6
            q = (1 - t) ** 2 * k0 + 2 * (1 - t) * t * b + t * t * k1     # quadratic bezier elbow
            path.append(q)
    path.append(pts[-1])
    for a, b in zip(path[:-1], path[1:]):
        if (b - a).length > 1e-4:
            m.cyl(a, b, r, segs=8, mat=mat, caps=(False, False))
    # couplings every ~1.2 m and straps every ~0.6 m on straight runs
    for a, b in zip(pts[:-1], pts[1:]):
        L = (b - a).length
        d = (b - a).normalized()
        if L > 0.5:
            nc = int(L // 1.2)
            for k in range(1, nc + 1):
                p = a + d * (L * k / (nc + 1))
                m.cyl(p - d * 0.015, p + d * 0.015, r * 1.35, segs=8, mat=mat)
        if straps and L > 0.25:
            ns = max(1, int(L // 0.6))
            for k in range(ns):
                p = a + d * (L * (k + 0.5) / ns)
                _strap(m, p, d, r, strap_axis)


def _strap(m, p, d, r, toward_wall):
    """One-hole pipe strap: a saddle over the pipe and a tab screwed to the surface behind it."""
    if toward_wall is None:
        return
    w = vec(toward_wall).normalized()          # from the pipe centre to the mounting surface
    side = d.cross(w).normalized()
    m.cyl(p - d * 0.007, p + d * 0.007, r * 1.25, segs=8, mat="M_Iron", caps=(True, True))
    o = p + w * r * 0.9 + side * r * 0.6
    m.beam(o, o + side * 0.026, 0.014, 0.003, "M_Iron", up=w)
    m.cyl(o + side * 0.016 - w * 0.0015, o + side * 0.016 - w * 0.004, 0.004, segs=6, mat="M_Iron")


def empty(name, loc):
    EMPTIES[name] = [round(c, 5) for c in loc]


# ---------------------------------------------------------------------- the panel
def panel():
    """Backboard, fuse cabinet (body), main switch box. The door and switch handle are separate (they move)."""
    m = MB("FuseBox_Panel")
    b = BOARD
    y0 = WALL_S
    W = "M_WoodFurniture"
    m.box((b["x0"], y0, b["z0"]), (b["x1"], y0 + b["t"], b["z1"]), W, grain="z")
    # chamfer strips round the board front edge
    yb = y0 + b["t"]
    for z in (b["z0"] + 0.03, b["z1"] - 0.03):
        m.cyl((b["x0"] + 0.025, yb, z), (b["x0"] + 0.025, yb + 0.004, z), 0.006, segs=6, mat="M_Iron")
        m.cyl((b["x1"] - 0.025, yb, z), (b["x1"] - 0.025, yb + 0.004, z), 0.006, segs=6, mat="M_Iron")

    # --- fuse cabinet: open-front steel box (door is a separate object)
    c = CAB_BOX
    P = "M_PaintedMetal"
    t = 0.006
    yb0, yb1 = yb, yb + c["d"]
    m.box((c["x0"], yb0, c["z0"]), (c["x1"], yb0 + t, c["z1"]), P)                          # back
    m.box((c["x0"], yb0, c["z0"]), (c["x0"] + t, yb1, c["z1"]), P, grain="y")               # left
    m.box((c["x1"] - t, yb0, c["z0"]), (c["x1"], yb1, c["z1"]), P, grain="y")               # right
    m.box((c["x0"], yb0, c["z0"]), (c["x1"], yb1, c["z0"] + t), P)                          # bottom
    m.box((c["x0"], yb0, c["z1"] - t), (c["x1"], yb1, c["z1"]), P)                          # top
    # front flange (where the door seals)
    f = 0.012
    m.box((c["x0"], yb1 - 0.004, c["z0"]), (c["x1"], yb1, c["z0"] + f), P)
    m.box((c["x0"], yb1 - 0.004, c["z1"] - f), (c["x1"], yb1, c["z1"]), P)
    m.box((c["x0"], yb1 - 0.004, c["z0"]), (c["x0"] + f, yb1, c["z1"]), P)
    m.box((c["x1"] - f, yb1 - 0.004, c["z0"]), (c["x1"], yb1, c["z1"]), P)
    # knockouts with conduit locknuts on top and bottom
    for x in (0.515, 0.585):
        m.cyl((x, yb0 + 0.03, c["z1"]), (x, yb0 + 0.03, c["z1"] + 0.006), 0.017, segs=6, mat="M_Iron")
    m.cyl((PANEL_X, yb0 + 0.03, c["z0"] - 0.006), (PANEL_X, yb0 + 0.03, c["z0"]), 0.017, segs=6, mat="M_Iron")

    # porcelain fuse block with three Edison-base receptacles
    by0, by1 = yb0 + t, yb0 + t + 0.03
    m.box((0.474, by0, c["z0"] + 0.03), (0.626, by1, c["z1"] - 0.03), "M_Porcelain")
    for z in (c["z0"] + 0.045, c["z1"] - 0.045):
        for x in (0.485, 0.615):
            m.cyl((x, by1, z), (x, by1 + 0.003, z), 0.006, segs=8, mat="M_Iron")       # mounting screws
    for i, z in enumerate(SOCKET_Z):
        cxz = vec(SOCKET_X, 0, z)
        mouth = by1 + 0.008
        # raised porcelain collar
        lathe(m, (SOCKET_X, by1, z), (0, 1, 0), [(0.024, 0.0), (0.024, 0.006), (0.0215, 0.008)], 20, "M_Porcelain")
        annulus(m, (SOCKET_X, mouth, z), (0, 1, 0), 0.0138, 0.0215, 20, "M_Porcelain")
        # threaded brass shell (inside surface) and the centre contact at the bottom
        prof = []
        for k in range(9):
            prof.append((0.0138 if k % 2 == 0 else 0.0128, -0.0025 * k))
        lathe(m, (SOCKET_X, mouth, z), (0, 1, 0), prof, 16, "M_Brass", inward=True)
        m.cyl((SOCKET_X, mouth - 0.021, z), (SOCKET_X, mouth - 0.0205, z), 0.0138, segs=16, mat="M_PlasticBlack")
        m.cyl((SOCKET_X, mouth - 0.0205, z), (SOCKET_X, mouth - 0.0185, z), 0.004, segs=8, mat="M_Brass")
        # number plate (Unity writes the digit)
        m.cyl((0.603, by1, z), (0.603, by1 + 0.002, z), 0.0095, segs=14, mat="M_Brass")
        empty(f"Socket_{i + 1}", (SOCKET_X, mouth, z))
        empty(f"SlotNumber_{i + 1}", (0.603, by1 + 0.0021, z))
    # bus bar and wires between the receptacles (visible behind the fuses)
    m.box((0.493, by1, SOCKET_Z[2] - 0.01), (0.499, by1 + 0.004, SOCKET_Z[0] + 0.01), "M_Brass")
    for z in SOCKET_Z:
        m.box((0.496, by1, z - 0.002), (SOCKET_X - 0.022, by1 + 0.003, z + 0.002), "M_Brass")
    empty("SparkPoint", (SOCKET_X, by1 + 0.03, SOCKET_Z[1]))

    # front brass label plate on the cabinet top (above the door)
    # --- main switch box (safety switch) under the cabinet
    s = SW_BOX
    sy0, sy1 = yb, yb + s["d"]
    m.box((s["x0"], sy0, s["z0"]), (s["x1"], sy1, s["z1"]), P)
    # raised cover with rolled edge
    m.box((s["x0"] + 0.008, sy1, s["z0"] + 0.008), (s["x1"] - 0.008, sy1 + 0.004, s["z1"] - 0.008), P)
    # handle escutcheon
    m.box((PANEL_X - 0.022, sy1 + 0.004, s["z0"] + 0.03), (PANEL_X + 0.022, sy1 + 0.009, s["z1"] - 0.03), "M_Iron")
    # ON / OFF plates (Unity writes the words)
    m.box((PANEL_X - 0.026, sy1 + 0.004, s["z1"] - 0.026), (PANEL_X + 0.026, sy1 + 0.0055, s["z1"] - 0.012), "M_Brass")
    m.box((PANEL_X - 0.026, sy1 + 0.004, s["z0"] + 0.012), (PANEL_X + 0.026, sy1 + 0.0055, s["z0"] + 0.026), "M_Brass")
    empty("Label_On", (PANEL_X, sy1 + 0.0056, s["z1"] - 0.019))
    empty("Label_Off", (PANEL_X, sy1 + 0.0056, s["z0"] + 0.019))
    # nipple + locknuts from the switch box up into the cabinet
    m.cyl((PANEL_X, yb0 + 0.03, s["z1"]), (PANEL_X, yb0 + 0.03, c["z0"]), COND_R, segs=8, mat="M_Iron")
    m.cyl((PANEL_X, yb0 + 0.03, s["z1"]), (PANEL_X, yb0 + 0.03, s["z1"] + 0.006), 0.017, segs=6, mat="M_Iron")
    # warning card tacked on the board under the switch
    m.box((0.465, yb, F + 0.94), (0.635, yb + 0.0015, F + 0.985), "M_Paper")
    for x in (0.47, 0.63):
        m.cyl((x, yb + 0.0015, F + 0.98), (x, yb + 0.004, F + 0.98), 0.002, segs=5, mat="M_Iron")
    empty("Label_Warning", (PANEL_X, yb + 0.0016, F + 0.9625))
    m.box((PANEL_X - 0.04, yb1 + 0.0, c["z1"] + 0.012), (PANEL_X + 0.04, yb1 + 0.0015, c["z1"] + 0.034), "M_Brass")
    empty("Label_Fuses", (PANEL_X, yb1 + 0.0016, c["z1"] + 0.023))
    m.cyl((PANEL_X, yb0, c["z1"] + 0.023), (PANEL_X, yb1, c["z1"] + 0.023), 0.0015, segs=4, mat="M_Iron")  # (hidden) plate stud
    return m


def panel_door():
    """Hinged on the east edge; swings into the room. Diagram card on the inside."""
    c = CAB_BOX
    yb1 = WALL_S + BOARD["t"] + c["d"]
    hinge = vec(c["x1"] + 0.004, yb1 + 0.004, (c["z0"] + c["z1"]) / 2)
    m = MB("FuseBox_Door", origin=hinge)
    P = "M_PaintedMetal"
    x0, x1, z0, z1 = c["x0"] - 0.003, c["x1"] + 0.002, c["z0"] - 0.003, c["z1"] + 0.003
    yf0, yf1 = yb1, yb1 + 0.006
    m.box((x0, yf0, z0), (x1, yf1, z1), P)
    # folded return round the edge (towards the wall)
    for (a, b_, cz, dz) in ((x0, x1, z0, z0 + 0.008), (x0, x1, z1 - 0.008, z1), (x0, x0 + 0.008, z0, z1), (x1 - 0.008, x1, z0, z1)):
        m.box((a, yf0 - 0.01, cz), (b_, yf0, dz), P)
    # pressed panel in the face
    m.box((x0 + 0.025, yf1, z0 + 0.03), (x1 - 0.025, yf1 + 0.002, z1 - 0.03), P)
    # piano-ish hinge knuckles on the east edge
    for zc in (z0 + 0.06, (z0 + z1) / 2, z1 - 0.06):
        m.cyl((hinge.x, hinge.y, zc - 0.025), (hinge.x, hinge.y, zc + 0.025), 0.0045, segs=8, mat="M_Iron")
    # latch: turn knob on the west edge
    kz = (z0 + z1) / 2
    m.cyl((x0 + 0.022, yf1, kz), (x0 + 0.022, yf1 + 0.014, kz), 0.006, segs=8, mat="M_Iron")
    m.cyl((x0 + 0.022, yf1 + 0.014, kz), (x0 + 0.022, yf1 + 0.024, kz), 0.011, 0.009, segs=12, mat="M_PlasticBlack")
    m.box((x0 + 0.017, yf1 + 0.021, kz - 0.015), (x0 + 0.027, yf1 + 0.027, kz + 0.015), "M_PlasticBlack")
    # wiring diagram card on the inside face
    yi = yf0 - 0.0105
    m.box((x0 + 0.014, yi - 0.001, z0 + 0.018), (x1 - 0.014, yi, z1 - 0.018), "M_Paper")
    for (x, z) in ((x0 + 0.02, z0 + 0.024), (x1 - 0.02, z0 + 0.024), (x0 + 0.02, z1 - 0.024), (x1 - 0.02, z1 - 0.024)):
        m.cyl((x, yi - 0.001, z), (x, yi - 0.003, z), 0.002, segs=5, mat="M_Iron")
    empty("DiagramCard", ((x0 + x1) / 2, yi - 0.0011, (z0 + z1) / 2))
    return m


def switch_handle():
    """Main switch handle. Origin on the spindle (axis = X). Modelled straight out from the box; Unity sets ON/OFF."""
    s = SW_BOX
    sy1 = WALL_S + BOARD["t"] + s["d"]
    piv = vec(PANEL_X, sy1 + 0.012, (s["z0"] + s["z1"]) / 2)
    m = MB("FuseBox_SwitchHandle", origin=piv)
    m.cyl(piv - vec(0.016, 0, 0), piv + vec(0.016, 0, 0), 0.011, segs=12, mat="M_Iron")
    m.beam(piv, piv + vec(0, 0.07, 0), 0.014, 0.008, "M_Iron", up=(0, 0, 1))
    m.cyl(piv + vec(-0.028, 0.07, 0), piv + vec(0.028, 0.07, 0), 0.0085, segs=10, mat="M_RedPaint")
    for sgn in (-1, 1):
        m.cyl(piv + vec(sgn * 0.028, 0.07, 0), piv + vec(sgn * 0.031, 0.07, 0), 0.0085, 0.006, segs=10, mat="M_RedPaint")
    return m


# ---------------------------------------------------------------------- the fuse (one model, instanced in Unity)
def fuse():
    """Edison-base plug fuse. Origin on the base contact, axis +Z. ~32 mm across, 34 mm tall."""
    m = MB("Fuse", origin=(0, 0, 0))
    z = 0.0
    m.cyl((0, 0, 0), (0, 0, 0.0018), 0.0042, segs=10, mat="M_Brass")                   # centre contact
    m.cyl((0, 0, 0.0016), (0, 0, 0.003), 0.0095, segs=14, mat="M_PlasticBlack")       # insulator
    # threaded shell: rolled threads as alternating radii
    prof = [(0.0118, 0.003)]
    for k in range(6):
        prof.append((0.0128 if k % 2 == 0 else 0.0118, 0.0045 + 0.002 * k))
    prof.append((0.0122, 0.0165))
    lathe(m, (0, 0, 0), (0, 0, 1), [(r, h) for r, h in prof], 14, "M_Brass")
    # porcelain body with a rounded shoulder; coloured band (Unity swaps M_FuseBand per rating)
    lathe(m, (0, 0, 0), (0, 0, 1), [(0.0122, 0.0165), (0.0155, 0.017), (0.016, 0.0185), (0.016, 0.0205)], 16, "M_Porcelain")
    lathe(m, (0, 0, 0), (0, 0, 1), [(0.0161, 0.0205), (0.0161, 0.0255)], 16, "M_FuseBand")
    lathe(m, (0, 0, 0), (0, 0, 1), [(0.016, 0.0255), (0.016, 0.029), (0.0148, 0.0318), (0.0122, 0.0328)], 16, "M_Porcelain")
    # mica window with the fusible link seen through it
    lathe(m, (0, 0, 0), (0, 0, 1), [(0.0122, 0.0328), (0.0112, 0.0328)], 16, "M_Brass")    # crimped rim
    m.cyl((0, 0, 0.0324), (0, 0, 0.0329), 0.0112, segs=16, mat="M_FuseWindow", caps=(False, True))
    m.box((-0.0085, -0.0011, 0.0329), (0.0085, 0.0011, 0.03305), "M_FuseLink")
    m.box((-0.0012, -0.0035, 0.0329), (0.0012, 0.0035, 0.03302), "M_FuseLink")
    return m


# ---------------------------------------------------------------------- conduits, lamps, junction box, cord
def conduits():
    m = MB("FuseBox_Conduits")
    c = CAB_BOX
    yb0 = WALL_S + BOARD["t"]
    yc = yb0 + 0.03                          # conduit centre off the board
    top = c["z1"] + 0.006
    wall_y = WALL_S + 0.035                  # conduit on the paneling above the board (no board up there)
    ceil = T.CEIL_Z - 0.016
    # 1: lights -> up the wall, across the ceiling (through the beam), to the lamp over the fire finder
    lamp = (0.05, 0.0)
    conduit(m, [(0.515, yc, top), (0.515, yc, BOARD["z1"] - 0.01), (0.515, wall_y, BOARD["z1"] + 0.05),
                (0.515, wall_y, ceil - 0.02), (0.515, wall_y + 0.06, ceil), (0.515, lamp[1] - 0.25, ceil),
                (lamp[0] + 0.25, lamp[1], ceil), (lamp[0] + 0.06, lamp[1], ceil)],
            strap_axis=None)
    # straps: wall run (towards -y), ceiling run (towards +z)
    for z in (F + 1.95, F + 2.35):
        _strap(m, vec(0.515, wall_y, z), vec(0, 0, 1), COND_R, (0, -1, 0))
    for y in (-3.0, -2.4, -1.4, -0.8):
        _strap(m, vec(0.515, y, ceil), vec(0, 1, 0), COND_R, (0, 0, 1))
    # 2: radio -> up, east along the south header, north along the east header, down the mullion
    hz = F + 2.52
    xe = EAST_IN - 0.065
    conduit(m, [(0.585, yc, top), (0.585, yc, BOARD["z1"] - 0.01), (0.585, wall_y, BOARD["z1"] + 0.05),
                (0.585, wall_y, hz), (xe, wall_y, hz), (xe, MULL_Y, hz), (xe, MULL_Y, F + 0.86)], strap_axis=None)
    for z in (F + 1.95, F + 2.3):
        _strap(m, vec(0.585, wall_y, z), vec(0, 0, 1), COND_R, (0, -1, 0))
    for x in (1.2, 1.9, 2.6, 3.2):
        _strap(m, vec(x, wall_y, hz), vec(1, 0, 0), COND_R, (0, -1, 0))
    for y in (-2.9, -2.1, -1.3, -0.5, 0.2):
        _strap(m, vec(xe, y, hz), vec(0, 1, 0), COND_R, (1, 0, 0))
    for z in (F + 2.0, F + 1.4):
        _strap(m, vec(xe, MULL_Y, z), vec(0, 0, 1), COND_R, (1, 0, 0))
    # junction box behind the desk + cloth cord to the radio
    jx0, jx1 = xe - 0.022, EAST_IN
    m.box((jx0, MULL_Y - 0.045, F + 0.74), (jx1, MULL_Y + 0.045, F + 0.86), "M_PaintedMetal")
    m.box((jx0 - 0.003, MULL_Y - 0.038, F + 0.748), (jx0, MULL_Y + 0.038, F + 0.852), "M_PaintedMetal")
    for z in (F + 0.756, F + 0.844):
        m.cyl((jx0 - 0.003, MULL_Y, z), (jx0 - 0.006, MULL_Y, z), 0.003, segs=5, mat="M_Iron")
    m.cyl((jx0 - 0.003, MULL_Y + 0.02, F + 0.77), (jx0 - 0.012, MULL_Y + 0.02, F + 0.77), 0.006, segs=8, mat="M_PlasticBlack")
    rad = (3.255 + 0.075 - 0.075, -0.20 + 0.33)          # radio placement from build_props
    cord = [(jx0 - 0.012, MULL_Y + 0.02, F + 0.77), (jx0 - 0.03, 0.62, F + 0.70), (3.41, 0.48, F + 0.665),
            (3.405, 0.39, F + 0.70), (3.40, 0.355, F + 0.765), (3.39, 0.32, F + 0.775), (3.37, 0.27, F + 0.80)]
    m.tube_path(cord, 0.0042, segs=5, mat="M_PlasticBlack")
    # 3: catwalk lamp -> exterior: out through the wall behind the panel, up the siding, over the door
    yo = -T.CAB - 0.022
    lz = F + 2.30
    conduit(m, [(0.585, yo, F + 1.62), (0.585, yo, lz), (0.1, yo, lz)], strap_axis=None)
    m.box((0.565, yo - 0.02, F + 1.56), (0.605, -T.CAB, F + 1.64), "M_PaintedMetal")       # LB fitting on the siding
    for z in (F + 1.9, F + 2.15):
        _strap(m, vec(0.585, yo, z), vec(0, 0, 1), COND_R, (0, 1, 0))
    _strap(m, vec(0.33, yo, lz), vec(1, 0, 0), COND_R, (0, 1, 0))
    return m


def ceiling_lamp():
    """Ceiling box, cloth cord, brass socket, green enamel shade. Bulb is its own object (it glows)."""
    m = MB("CeilingLamp")
    cx, cy = 0.05, 0.0
    cz = T.CEIL_Z
    lathe(m, (cx, cy, cz - 0.045), (0, 0, 1), [(0.0, 0.0), (0.06, 0.0), (0.062, 0.006), (0.062, 0.045)], 20,
          "M_PaintedMetal", cap0=False)
    m.disc((cx, cy, cz - 0.045), 0.06, 20, "M_PaintedMetal", normal=(0, 0, -1))
    m.cyl((cx, cy, cz - 0.045), (cx, cy, cz - 0.06), 0.012, segs=8, mat="M_Iron")
    m.cyl((cx, cy, cz - 0.06), (cx, cy, F + 2.33), 0.0035, segs=6, mat="M_PlasticBlack")
    lathe(m, (cx, cy, F + 2.27), (0, 0, 1), [(0.016, 0.0), (0.018, 0.012), (0.018, 0.05), (0.012, 0.062)], 14, "M_Brass",
          cap1=True)
    # enamel shade: green outside, white inside, rolled rim
    prof = [(0.03, 0.065), (0.06, 0.05), (0.12, 0.02), (0.17, -0.01), (0.178, -0.014)]
    lathe(m, (cx, cy, F + 2.255), (0, 0, 1), prof, 28, "M_Enamel")
    lathe(m, (cx, cy, F + 2.255 - 0.003), (0, 0, 1), [(r - 0.003, h) for r, h in prof], 28, "M_EnamelWhite", inward=True)
    lathe(m, (cx, cy, F + 2.24), (0, 0, 1), [(0.172, 0.0), (0.182, 0.001), (0.182, 0.004), (0.172, 0.005)], 28, "M_Enamel")
    b = MB("CeilingLamp_Bulb")
    prof = [(0.0, -0.075), (0.016, -0.072), (0.03, -0.058), (0.034, -0.04), (0.03, -0.022), (0.016, -0.008), (0.013, 0.0)]
    lathe(b, (cx, cy, F + 2.27), (0, 0, 1), prof, 16, "M_Bulb")
    empty("Light_Ceiling", (cx, cy, F + 2.215))
    return [m, b]


def catwalk_lamp():
    """Vapour-tight 'jelly jar' with a wire guard over the door, outside."""
    m = MB("CatwalkLamp")
    x, z = 0.0, F + 2.30
    y0 = -T.CAB
    # cast box on the wall, fed by the conduit from the east
    m.box((x - 0.055, y0 - 0.045, z - 0.055), (x + 0.055, y0, z + 0.055), "M_PaintedMetal")
    m.cyl((0.1, -T.CAB - 0.022, z), (x + 0.055, -T.CAB - 0.022, z), COND_R * 1.3, segs=8, mat="M_Iron")
    # angled neck so the jar points down and out over the catwalk
    tilt = Matrix.Rotation(math.radians(-35), 4, "X")
    base = vec(x, y0 - 0.045, z)

    def T_(p):
        return base + tilt @ vec(p)
    neck = MB("tmp")
    lathe(neck, (0, 0, 0), (0, -1, 0), [(0.05, 0.0), (0.05, 0.02), (0.046, 0.035), (0.046, 0.05)], 18, "M_PaintedMetal")
    guard = MB("tmp2")
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        r = 0.05
        p0 = vec(math.cos(a) * r, -0.05, math.sin(a) * r)
        p1 = vec(math.cos(a) * r, -0.14, math.sin(a) * r)
        guard.cyl(p0, p1, 0.003, segs=4, mat="M_Iron")
        guard.cyl(p1, vec(0, -0.16, 0), 0.003, segs=4, mat="M_Iron")
    for yy in (-0.08, -0.12):
        pts = [vec(math.cos(2 * math.pi * k / 12) * 0.05, yy, math.sin(2 * math.pi * k / 12) * 0.05) for k in range(13)]
        guard.tube_path(pts, 0.0025, segs=4, mat="M_Iron")
    for mb in (neck, guard):
        mb.transform(Matrix.Translation(base) @ tilt)
        m.merge(mb)
    g = MB("CatwalkLamp_Globe")
    jar = MB("tmp3")
    lathe(jar, (0, 0, 0), (0, -1, 0), [(0.04, 0.05), (0.043, 0.07), (0.043, 0.12), (0.035, 0.14), (0.0, 0.145)], 16,
          "M_Bulb")
    jar.transform(Matrix.Translation(base) @ tilt)
    g.merge(jar)
    empty("Light_Catwalk", tuple(base + tilt @ vec(0, -0.1, 0)))
    return [m, g]


# ---------------------------------------------------------------------- where the fuses are kept
TIN_POS = (2.95, -0.63, F + 0.76)        # front corner of the desk, beside the lantern (lit, in plain view)
SHELF = dict(x0=0.80, x1=1.50, y0=3.12, z=F + 0.80)   # north wall under window N1, between the fog window (N2) and the firewood
CRATE_POS = (1.15, 3.31, SHELF["z"])      # on the shelf, at waist height


def fuse_tin():
    """Red tobacco-style tin, hinged lid at the back."""
    x, y, z = TIN_POS
    m = MB("FuseTin")
    R = "M_RedPaint"
    lathe(m, (x, y, z), (0, 0, 1), [(0.044, 0.0), (0.047, 0.002), (0.047, 0.046), (0.045, 0.048)], 24, R, cap0=True)
    lathe(m, (x, y, z + 0.044), (0, 0, 1), [(0.043, 0.0), (0.043, 0.004)], 24, R)
    m.disc((x, y, z + 0.006), 0.043, 24, "M_Iron")                      # bare tinplate floor inside
    lathe(m, (x, y, z + 0.006), (0, 0, 1), [(0.0435, 0.0), (0.0435, 0.04)], 24, "M_Iron", inward=True)
    hinge = vec(x, y - 0.049, z + 0.048)
    lid = MB("FuseTin_Lid", origin=hinge)
    lathe(lid, (x, y, z + 0.046), (0, 0, 1), [(0.0475, 0.0), (0.049, 0.002), (0.049, 0.012), (0.047, 0.014), (0.0, 0.0145)],
          24, R)
    lid.disc((x, y, z + 0.046), 0.0475, 24, "M_Iron", normal=(0, 0, -1))
    lid.cyl((x, y, z + 0.0605), (x, y, z + 0.061), 0.03, segs=20, mat="M_Paper", caps=(False, True))
    lid.cyl((hinge.x - 0.02, hinge.y, hinge.z), (hinge.x + 0.02, hinge.y, hinge.z), 0.003, segs=6, mat="M_Iron")
    empty("Label_Tin", (x, y, z + 0.0612))
    empty("TinSpot_1", (x, y, z + 0.008))
    return [m, lid]


def parts_crate():
    """Slatted crate on the wall shelf; lid hinged on the north edge (opens up against the wall)."""
    x, y, z = CRATE_POS
    W = "M_WoodFurniture"
    sx, sy, h = 0.21, 0.15, 0.12          # shallow, so the fuses on the floor show over the front from standing height
    m = MB("PartsCrate")
    t = 0.014
    # floor
    m.box((x - sx, y - sy, z), (x + sx, y + sy, z + 0.012), W)
    # slatted sides: 3 slats each with gaps, corner posts inside
    for zz in (0.012, 0.068):
        m.box((x - sx, y - sy, z + zz), (x + sx, y - sy + t, z + zz + 0.046), W)
        m.box((x - sx, y + sy - t, z + zz), (x + sx, y + sy, z + zz + 0.046), W)
        m.box((x - sx, y - sy + t, z + zz), (x - sx + t, y + sy - t, z + zz + 0.046), W, grain="y")
        m.box((x + sx - t, y - sy + t, z + zz), (x + sx, y + sy - t, z + zz + 0.046), W, grain="y")
    m.box((x - sx + t, y - sy + t, z + 0.012), (x + sx - t, y + sy - t, z + 0.013), "M_PlasticBlack")
    for px in (-1, 1):
        for py in (-1, 1):
            cx, cy = x + px * (sx - t - 0.012), y + py * (sy - t - 0.012)
            m.box((cx - 0.012, cy - 0.012, z + 0.012), (cx + 0.012, cy + 0.012, z + h), W, grain="z")
    # rope handles on the short ends
    for px in (-1, 1):
        pts = [vec(x + px * (sx + 0.004), y - 0.05, z + 0.09), vec(x + px * (sx + 0.018), y - 0.035, z + 0.07),
               vec(x + px * (sx + 0.022), y, z + 0.065), vec(x + px * (sx + 0.018), y + 0.035, z + 0.07),
               vec(x + px * (sx + 0.004), y + 0.05, z + 0.09)]
        m.tube_path(pts, 0.005, segs=5, mat="M_Wool")
    # stencil plate on the front (Unity writes on it)
    m.box((x - 0.09, y - sy - 0.001, z + 0.05), (x + 0.09, y - sy, z + 0.08), "M_Paper")
    empty("Label_Crate", (x, y - sy - 0.0011, z + 0.065))
    # junk inside: coil of wire, roll of friction tape, a spark plug-ish insulator
    coil = [vec(x + 0.1 + 0.055 * math.cos(a), y + 0.04 + 0.055 * math.sin(a), z + 0.02 + 0.004 * a)
            for a in [k * 0.4 for k in range(0, 48)]]
    m.tube_path(coil, 0.0045, segs=5, mat="M_Brass", caps=False)
    m.cyl((x - 0.12, y + 0.07, z + 0.012), (x - 0.12, y + 0.07, z + 0.04), 0.032, segs=14, mat="M_PlasticBlack")
    m.cyl((x - 0.12, y + 0.07, z + 0.0405), (x - 0.12, y + 0.07, z + 0.041), 0.014, segs=12, mat="M_Iron")
    m.cyl((x - 0.02, y + 0.08, z + 0.03), (x + 0.03, y + 0.06, z + 0.03), 0.009, segs=10, mat="M_GlassInsulator")
    empty("CrateSpot_1", (x - 0.1, y - 0.01, z + 0.0125))      # middle of the floor, in view over the front
    empty("CrateSpot_2", (x + 0.0, y - 0.045, z + 0.0125))
    hinge = vec(x, y + sy, z + h)
    lid = MB("PartsCrate_Lid", origin=hinge)
    for k in range(4):
        y0 = y - sy + k * (2 * sy / 4) + 0.003
        lid.box((x - sx - 0.005, y0, z + h), (x + sx + 0.005, y0 + 2 * sy / 4 - 0.006, z + h + 0.016), W)
    for px in (-0.15, 0.15):
        lid.box((x + px - 0.02, y - sy + 0.01, z + h - 0.012), (x + px + 0.02, y + sy - 0.01, z + h), W, grain="y")
    for px in (-0.12, 0.12):
        lid.box((x + px - 0.02, y + sy - 0.006, z + h + 0.002), (x + px + 0.02, y + sy + 0.004, z + h + 0.014), "M_Iron")
    return [m, lid]


def wall_shelf():
    """Plank shelf on two iron brackets, screwed to the north wall paneling under window N1's sill."""
    sh = SHELF
    m = MB("Wall_Shelf")
    W = "M_WoodFurniture"
    wy = T.CIN
    m.box((sh["x0"], sh["y0"], sh["z"] - 0.03), (sh["x1"], wy, sh["z"]), W)
    # front lip so nothing rolls off
    m.box((sh["x0"], sh["y0"] - 0.012, sh["z"] - 0.03), (sh["x1"], sh["y0"], sh["z"] + 0.012), W)
    for x in (sh["x0"] + 0.09, sh["x1"] - 0.09):
        m.box((x - 0.012, wy - 0.008, sh["z"] - 0.27), (x + 0.012, wy, sh["z"] - 0.03), "M_Iron")      # on the wall
        m.box((x - 0.012, sh["y0"] + 0.03, sh["z"] - 0.038), (x + 0.012, wy, sh["z"] - 0.03), "M_Iron")  # under the plank
        m.beam(vec(x, wy - 0.008, sh["z"] - 0.25), vec(x, sh["y0"] + 0.06, sh["z"] - 0.04), 0.016, 0.006, "M_Iron", up=(1, 0, 0))
        for z in (sh["z"] - 0.08, sh["z"] - 0.22):
            m.cyl((x, wy - 0.008, z), (x, wy - 0.011, z), 0.005, segs=6, mat="M_Iron")
    return m


# ---------------------------------------------------------------------- build
PLAIN = {
    # name: (rgba, roughness, metallic, emission rgba*strength or None)
    "M_Porcelain": ((0.86, 0.84, 0.78, 1), 0.15, 0.0, None),
    "M_FuseBand": ((0.15, 0.3, 0.75, 1), 0.4, 0.0, None),
    "M_FuseWindow": ((0.62, 0.66, 0.68, 1), 0.05, 0.0, None),
    "M_FuseLink": ((0.72, 0.72, 0.74, 1), 0.3, 1.0, None),
    "M_Enamel": ((0.12, 0.2, 0.14, 1), 0.2, 0.0, None),
    "M_EnamelWhite": ((0.85, 0.85, 0.8, 1), 0.25, 0.0, None),
    "M_Bulb": ((0.95, 0.88, 0.7, 1), 0.2, 0.0, None),
    "M_PlasticBlack": ((0.03, 0.03, 0.03, 1), 0.45, 0.0, None),
    "M_RedPaint": ((0.45, 0.06, 0.04, 1), 0.5, 0.0, None),
    "M_Paper": ((0.85, 0.82, 0.72, 1), 0.9, 0.0, None),
    "M_GlassInsulator": ((0.35, 0.6, 0.45, 1), 0.1, 0.0, None),
}
TEX = {"M_WoodFurniture": "WoodFurniture", "M_Iron": "Iron", "M_Brass": "Brass", "M_PaintedMetal": "PaintedMetal",
       "M_Wool": "Wool"}


def builders():
    out = [panel(), panel_door(), switch_handle()]
    out += [conduits()]
    out += ceiling_lamp()
    out += catwalk_lamp()
    out += fuse_tin()
    out += parts_crate()
    out.append(wall_shelf())
    f = fuse()
    f.origin = vec(0, 0, 0)
    out.append(f)
    return out


def main(outdir, reset=True):
    if reset:
        reset_scene()
    for k, v in TEX.items():
        register_material(k, v, 0.5 if k in ("M_Brass", "M_PaintedMetal", "M_Wool") else 1.0)
    for k, (col, rough, metal, em) in PLAIN.items():
        register_material(k, None, 1.0, color=col, rough=rough, metallic=metal, emission=em)
    objs = []
    for b in builders():
        objs.append(b.build())
        STATS[b.name] = b.tri_count()
    # the fuse model sits at the world origin in the file; park it out of the way of the cab in the .blend preview
    coll = bpy.context.scene.collection
    for name, loc in EMPTIES.items():
        e = bpy.data.objects.new("MK_" + name, None)
        e.empty_display_size = 0.02
        e.location = loc
        coll.objects.link(e)
        objs.append(e)
    print(json.dumps(STATS, indent=1), "TOTAL", sum(STATS.values()))
    os.makedirs(outdir, exist_ok=True)
    export_fbx(os.path.join(outdir, "FuseBox.fbx"), objs)
    json.dump(dict(empties=EMPTIES, tris=STATS), open(os.path.join(outdir, "fusebox_meta.json"), "w"), indent=1)
    return objs


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    outdir = argv[0] if argv else "/home/claude/lt/out"
    if "--with-cab" in argv:
        import build_props as P
        T.main(outdir)
        P.main(outdir, reset=False)
        main(outdir, reset=False)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, "FuseBox_preview.blend"))
    else:
        main(outdir)
