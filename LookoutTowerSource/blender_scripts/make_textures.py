"""Generate all PBR texture sets for the lookout tower project.
Convention: UVs are authored in metres; each material's tiling = 1 / tile_metres (see TILE dict)."""
import os, sys, json, math
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(__file__))
from texlib import *

OUT = sys.argv[1] if len(sys.argv) > 1 else "/home/claude/lookout/textures"
ONLY = set(sys.argv[2].split(",")) if len(sys.argv) > 2 else None
os.makedirs(OUT, exist_ok=True)
TILE = {}  # material -> metres covered by one texture repeat


def out(name, alb, nrm, mask, tile_m):
    if alb.shape[-1] == 4:
        save(f"{OUT}/T_{name}_Albedo.png", alb)
    else:
        save(f"{OUT}/T_{name}_Albedo.png", alb[..., :3])
    if nrm is not None:
        save(f"{OUT}/T_{name}_Normal.png", nrm)
    if mask is not None:
        save(f"{OUT}/T_{name}_Mask.png", mask)
    TILE[name] = tile_m
    print("wrote", name, flush=True)


def want(name):
    return ONLY is None or name in ONLY


# ---------------------------------------------------------------- wood helpers
def wood_grain(h, w, seed, rings=34, warp=1.4, stretch=10.0, knots=0, knot_r=(10, 22)):
    """Grain along x. Returns latewood line intensity (0..1) and fine fibre noise (unit std)."""
    yy = (np.arange(h, dtype=F)[:, None] + 0.5) / h
    n1 = fft_noise(h, w, 3.4, seed, sx=stretch, sy=1.0)
    n2 = fft_noise(h, w, 2.2, seed + 1, sx=stretch * 1.6, sy=1.0)
    phase = rings * yy + warp * n1 + 0.18 * warp * n2
    dark = np.zeros((h, w), F)
    if knots:
        r = rng(seed + 7)
        Y, X = np.mgrid[0:h, 0:w].astype(F)
        for _ in range(knots):
            kx, ky = r.uniform(0, w), r.uniform(0, h)
            rx = r.uniform(*knot_r)
            ry = rx * r.uniform(0.45, 0.7)
            dx = np.abs(X - kx); dx = np.minimum(dx, w - dx)
            dy = np.abs(Y - ky); dy = np.minimum(dy, h - dy)
            d = np.sqrt((dx / (rx * 3.2)) ** 2 + (dy / (ry * 1.6)) ** 2)
            phase += 1.6 * np.exp(-d * d) * np.sign(np.sin(r.uniform(0, 6.28)) + 0.01)
            core = np.sqrt((dx / rx) ** 2 + (dy / ry) ** 2)
            dark = np.maximum(dark, smoothstep(1.0, 0.55, core))
    ring = 0.5 + 0.5 * np.sin(2 * np.pi * phase)
    late = ring ** 5
    fib = fft_noise(h, w, 1.1, seed + 2, sx=stretch * 3.5, sy=1.0)
    return late.astype(F), fib, dark


def plank_layout(h, w, seed, board_px, joists=5, joints=(1, 2)):
    """Boards along x, `board_px` rows each. Butt joints snap to `joists` evenly spaced columns."""
    r = rng(seed)
    nb = h // board_px
    xs = np.arange(w, dtype=F) + 0.5
    jcols = (np.arange(joists) + 0.5) * w / joists
    seg = np.zeros((h, w), np.int32)
    jdist = np.full((h, w), 1e6, F)
    vloc = np.zeros((h, w), F)
    joint_list = []
    count = 0
    for b in range(nb):
        y0 = b * board_px
        k = int(r.integers(joints[0], joints[1] + 1)) if joints[1] > 0 else 0
        if k > 0:
            jx = np.sort(r.choice(jcols, size=min(k, joists), replace=False))
            idx = np.searchsorted(jx, xs)
            idx[idx == len(jx)] = 0
            d = np.abs(xs[:, None] - jx[None, :]); d = np.minimum(d, w - d)
            jdist[y0:y0 + board_px] = d.min(1)[None, :]
            seg[y0:y0 + board_px] = (count + idx)[None, :]
            count += len(jx)
            joint_list.append((b, jx))
        else:
            seg[y0:y0 + board_px] = count
            count += 1
        vloc[y0:y0 + board_px] = ((np.arange(board_px) + 0.5) / board_px)[:, None]
    return dict(seg=seg, n=count, jdist=jdist, vloc=vloc, nb=nb, board_px=board_px,
                jcols=jcols, joints=joint_list)


def sample_by_segment(field, seg, n, seed):
    """Sample a tileable field with a random offset per segment (variety between boards)."""
    h, w = field.shape
    r = rng(seed)
    ox = r.integers(0, w, n)
    oy = r.integers(0, h, n)
    Y, X = np.mgrid[0:h, 0:w]
    return field[(Y + oy[seg]) % h, (X + ox[seg]) % w]


def nails(h, w, L, seed, r_px=2.6):
    """Nail heads at every joist crossing (2 per board); offset either side of butt joints."""
    rr = rng(seed)
    pts = []
    bp = L["board_px"]
    joint_at = {}
    for b, jx in L["joints"]:
        for x in jx:
            joint_at[(b, round(float(x), 2))] = True
    for b in range(L["nb"]):
        y0 = b * bp
        for jc in L["jcols"]:
            is_joint = (b, round(float(jc), 2)) in joint_at
            for fy in (0.28, 0.72):
                yv = y0 + bp * fy + rr.uniform(-1.5, 1.5)
                if is_joint:
                    pts.append((jc - 7 + rr.uniform(-1, 1), yv))
                    pts.append((jc + 7 + rr.uniform(-1, 1), yv))
                else:
                    pts.append((jc + rr.uniform(-2, 2), yv))
    ss = 4
    img = Image.new("L", (w * ss, h * ss), 0)
    d = ImageDraw.Draw(img)
    for x, y in pts:
        for ox in (-w, 0, w):
            for oy in (-h, 0, h):
                cx, cy = (x + ox) * ss, (y + oy) * ss
                rad = r_px * ss
                d.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], fill=255)
    m = np.asarray(img.resize((w, h), Image.LANCZOS)).astype(F) / 255
    return m


def edge_profile(L, bevel_px, gap_px, joint=True):
    bp = L["board_px"]
    e = np.minimum(L["vloc"], 1 - L["vloc"]) * bp
    if joint:
        e = np.minimum(e, L["jdist"])
    gap = smoothstep(gap_px + 0.8, gap_px - 0.4, e) if gap_px > 0 else np.zeros_like(e)
    bev = smoothstep(gap_px, gap_px + bevel_px, e)
    return gap, bev, e


def palette_pick(n, seed, pal, jitter=0.06):
    r = rng(seed)
    pal = np.array(pal, dtype=F) / 255.0
    cols = pal[r.integers(0, len(pal), n)]
    cols *= r.uniform(1 - jitter, 1 + jitter, (n, 1))
    return cols.astype(F)


def planks_material(name, size, tile_m, seed, board_m, pal, gap_px, bevel_px, smooth, weather,
                    joints=(1, 2), moss=0.0, transpose=False, grain_contrast=0.32, rings=30):
    h = w = size
    board_px = int(round(board_m / tile_m * size))
    while size % board_px:
        board_px += 1
    L = plank_layout(h, w, seed, board_px, joists=5, joints=joints)
    late, fib, _ = wood_grain(h, w, seed + 10, rings=rings, warp=1.6, stretch=9.0, knots=10, knot_r=(6, 14))
    late = sample_by_segment(late, L["seg"], L["n"], seed + 11)
    fib = sample_by_segment(fib, L["seg"], L["n"], seed + 11)
    cols = palette_pick(L["n"], seed + 12, pal)
    base = cols[L["seg"]]
    gap, bev, e = edge_profile(L, bevel_px, gap_px, joint=joints[1] > 0)
    wn = fft_noise(h, w, 2.4, seed + 13)
    alb = base * (1 - grain_contrast * late[..., None]) * (1 + 0.06 * fib[..., None])
    # weathering: greyer, lighter blotches
    wmask = np.clip(0.5 + 0.35 * wn, 0, 1) * weather
    grey = alb.mean(-1, keepdims=True) * np.array([1.0, 0.98, 0.94], F) * 1.08
    alb = lerp(alb, grey, wmask)
    # dirt close to gaps
    dirt = np.clip(1 - e / (board_px * 0.35), 0, 1) ** 2
    alb *= (1 - 0.25 * dirt[..., None])
    # nails
    nm = nails(h, w, L, seed + 14)
    rust = blur(nm, 2.2) * 0.9
    alb = lerp(alb, col(96, 60, 36), np.clip(rust - nm, 0, 1) * 0.6)
    alb = lerp(alb, col(52, 50, 48), nm)
    if moss > 0:
        mn = fft_noise(h, w, 2.0, seed + 15)
        mm = np.clip(smoothstep(0.2, 1.2, mn) * moss * (gap * 0.6 + dirt * 0.8), 0, 1)
        alb = lerp(alb, col(62, 78, 40), mm)
    alb = lerp(alb, col(24, 20, 17), gap)
    hgt = (bev * 1.0 - late * 0.12 + fib * 0.04 - gap * 0.6 + nm * 0.15)
    # cupping across each board
    hgt += (0.5 - np.abs(L["vloc"] - 0.5)) * 0.12
    nrm = normal_map(hgt, 3.0)
    ao = ao_from_height(hgt, 5, 1.6) * (1 - 0.4 * gap)
    sm = np.clip(smooth + 0.05 * fft_noise(h, w, 2.0, seed + 16) - 0.1 * wmask - 0.1 * gap, 0.02, 1)
    mask = mask_map(np.zeros_like(hgt), ao, norm01(hgt), sm)
    if transpose:
        alb = np.transpose(alb, (1, 0, 2))[::-1]
        mask = np.transpose(mask, (1, 0, 2))[::-1]
        hgt2 = np.transpose(hgt)[::-1]
        nrm = normal_map(hgt2, 3.0)
    out(name, alb, nrm, mask, tile_m)


# ---------------------------------------------------------------- paint helper
def paint_over(h, w, seed, wood_alb, wood_h, paint, peel=0.3, board_v=None, streaks=0.5, chalky=0.15):
    n1 = fft_noise(h, w, 2.3, seed)
    n2 = fft_noise(h, w, 1.5, seed + 1)
    F1, F2, cell = worley(h, w, 900, seed + 2)
    crack = smoothstep(2.2, 0.4, F2 - F1)
    field = 0.65 * n1 + 0.35 * n2
    if board_v is not None:
        field += (board_v - 0.55) * 1.6  # paint fails along the lower lap
    thr = np.quantile(field, 1 - peel)
    peel_m = smoothstep(thr - 0.04, thr + 0.04, field)
    # alligator crackle in remaining paint
    alb = np.broadcast_to(paint, (h, w, 3)).copy()
    alb *= (1 + 0.05 * fft_noise(h, w, 1.8, seed + 3))[..., None]
    alb = lerp(alb, alb * 0.72, crack * 0.55 * (1 - peel_m))
    # chalky / dirty streaks running down (along -v, i.e. rows)
    st = fft_noise(h, w, 1.6, seed + 4, sx=1.0, sy=14.0)
    st = smoothstep(0.2, 2.0, st) * streaks
    dirt_col = col(70, 64, 54)
    alb = lerp(alb, dirt_col, np.clip(st * 0.45, 0, 1))
    alb = lerp(alb, alb * 1.08 + 0.03, chalky * smoothstep(0.0, 1.5, n2))
    edge = np.clip(blur(peel_m, 1.2) - peel_m, 0, 1) * 4
    res = lerp(alb, wood_alb, peel_m)
    res = lerp(res, res * 1.12 + 0.04, np.clip(edge, 0, 1) * 0.5)
    hgt = wood_h + (1 - peel_m) * 0.25 + edge * 0.08 - crack * 0.05 * (1 - peel_m)
    return res, hgt, peel_m


def wood_plain(h, w, seed, pal_light, pal_dark, rings=30, stretch=9.0, knots=4):
    late, fib, kd = wood_grain(h, w, seed, rings=rings, warp=1.5, stretch=stretch, knots=knots)
    a = lerp(col(*pal_light), col(*pal_dark), np.clip(late * 0.9 + kd * 0.8, 0, 1))
    a = a * (1 + 0.05 * fib[..., None])
    hgt = -late * 0.15 + fib * 0.05 - kd * 0.1
    return a, hgt, late, fib


# ================================================================ materials
def timber_weathered():
    h = w = 1024
    late, fib, kd = wood_grain(h, w, 101, rings=26, warp=1.8, stretch=11.0, knots=7, knot_r=(9, 18))
    wn = fft_noise(h, w, 2.6, 102)
    wn2 = fft_noise(h, w, 1.8, 103, sx=6, sy=1)
    base = lerp(col(104, 90, 74), col(132, 126, 116), np.clip(0.5 + 0.3 * wn, 0, 1))
    base = lerp(base, col(70, 58, 48), np.clip(0.35 * smoothstep(0.4, 1.8, wn2), 0, 1))
    alb = base * (1 - 0.38 * late[..., None]) * (1 + 0.07 * fib[..., None])
    alb = lerp(alb, col(52, 40, 32), kd * 0.85)
    # checks / splits along the grain
    _, cov = strokes(h, w, 34, 104, length=(60, 260), width=(1, 2.2), angle=0.0, angle_jit=0.05,
                     colors=((0, 0, 0),), alpha=(0.7, 1.0), curve=0.06)
    alb = lerp(alb, col(28, 22, 18), cov * 0.9)
    # water staining
    stain = smoothstep(0.6, 2.2, fft_noise(h, w, 2.0, 105, sx=4, sy=1))
    alb = lerp(alb, alb * 0.7, stain * 0.6)
    lich = smoothstep(1.7, 2.4, fft_noise(h, w, 1.4, 106)) * 0.6
    alb = lerp(alb, col(126, 138, 108), lich)
    hgt = -late * 0.18 + fib * 0.07 - cov * 0.9 - kd * 0.12 + 0.1 * wn
    nrm = normal_map(hgt, 3.2)
    ao = ao_from_height(hgt, 4, 1.5)
    sm = np.clip(0.2 + 0.05 * fft_noise(h, w, 2.0, 107) - 0.1 * cov, 0.02, 1)
    out("Timber", alb, nrm, mask_map(np.zeros_like(hgt), ao, norm01(hgt), sm), 1.0)


def iron_rusty():
    h = w = 512
    n = 0.6 * fft_noise(h, w, 2.4, 201) + 0.4 * fft_noise(h, w, 1.3, 202)
    F1, F2, _ = worley(h, w, 700, 203)
    pits = smoothstep(3.0, 0.5, F1) * 0.5
    rust = np.clip(smoothstep(0.35, 1.1, n) + pits * smoothstep(-0.2, 0.6, n), 0, 1)
    base = col(44, 43, 42) * (1 + 0.12 * fft_noise(h, w, 1.5, 204))[..., None]
    rc = lerp(col(84, 44, 24), col(146, 80, 40), np.clip(0.5 + 0.4 * fft_noise(h, w, 1.6, 205), 0, 1))
    alb = lerp(base, rc, rust)
    hgt = rust * 0.5 + 0.15 * fft_noise(h, w, 1.2, 206) * rust - pits * 0.3
    metal = 0.7 * (1 - rust)
    sm = 0.42 * (1 - rust) + 0.12 * rust
    out("Iron", alb, normal_map(hgt, 2.5), mask_map(metal, ao_from_height(hgt, 3, 1.2), norm01(hgt), sm), 1.0)


def brass():
    h = w = 256
    n = fft_noise(h, w, 2.2, 301)
    tar = smoothstep(0.0, 1.5, n)
    alb = lerp(col(176, 134, 64), col(96, 84, 52), tar * 0.8)
    out("Brass", alb, None, mask_map(np.full((h, w), 1.0, F) - tar * 0.3, np.ones((h, w), F),
                                    np.zeros((h, w), F), 0.62 - 0.3 * tar), 0.5)


def concrete():
    h = w = 512
    n = fft_noise(h, w, 2.5, 401)
    F1, F2, _ = worley(h, w, 2200, 402)
    pores = smoothstep(2.2, 0.6, F1) * (fft_noise(h, w, 0.5, 403) > 0.6)
    spk = smoothstep(1.8, 2.6, fft_noise(h, w, 0.2, 404))
    alb = col(146, 144, 138) * (1 + 0.08 * n)[..., None]
    alb = lerp(alb, col(96, 94, 90), pores * 0.8)
    alb = lerp(alb, col(176, 172, 164), spk * 0.4)
    st = smoothstep(0.8, 2.2, fft_noise(h, w, 2.0, 405, sx=1, sy=6))
    alb = lerp(alb, col(88, 92, 70), st * 0.45)
    hgt = 0.2 * n - pores * 0.6
    out("Concrete", alb, normal_map(hgt, 2.0), mask_map(np.zeros_like(hgt), ao_from_height(hgt, 3, 1.5), norm01(hgt),
                                                  np.full_like(hgt, 0.18)), 1.0)


def glass_grime():
    h = w = 512
    Y, X = np.mgrid[0:h, 0:w].astype(F)
    u, v = (X + 0.5) / w, 1 - (Y + 0.5) / h
    e = np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v))
    edge = smoothstep(0.18, 0.0, e)
    corner = smoothstep(0.35, 0.0, np.sqrt(np.minimum(u, 1 - u) ** 2 + np.minimum(v, 1 - v) ** 2))
    st = smoothstep(0.3, 2.0, fft_noise(h, w, 1.5, 501, sx=1, sy=12)) * smoothstep(0.8, 0.0, v)
    blot = smoothstep(0.4, 1.8, fft_noise(h, w, 2.2, 502))
    sp, _ = blobs(h, w, 260, 503, radius=(0.8, 3.0))
    g = np.clip(edge * 0.55 + corner * 0.4 + st * 0.5 + blot * 0.3 + sp * 0.5, 0, 1)
    alb = lerp(col(150, 160, 166), col(92, 84, 70), g)
    a = 0.08 + 0.62 * g
    rgba = np.concatenate([alb, a[..., None]], -1)
    sm = 0.94 - 0.6 * g
    out("GlassGrime", rgba, None, mask_map(np.zeros_like(a), np.ones_like(a), np.zeros_like(a), sm), 1.0)


def fabric_wool():
    h = w = 512
    Y, X = np.mgrid[0:h, 0:w].astype(F)
    p = 8.0
    twill = 0.5 + 0.5 * np.sin(2 * np.pi * (X + Y) / p)
    wx = 0.5 + 0.5 * np.sin(2 * np.pi * X / (p / 2))
    wy = 0.5 + 0.5 * np.sin(2 * np.pi * Y / (p / 2))
    weave = 0.5 * twill + 0.25 * wx + 0.25 * wy
    fuzz = fft_noise(h, w, 0.8, 601)
    big = fft_noise(h, w, 2.4, 602)
    alb = col(84, 86, 60) * (0.85 + 0.15 * weave + 0.05 * fuzz + 0.06 * big)[..., None]
    # faded stripe bands near the blanket ends (texture repeats every 1 m)
    band = smoothstep(0.02, 0.0, np.abs(((Y / h) % 1.0) - 0.12)) + smoothstep(0.02, 0.0, np.abs(((Y / h) % 1.0) - 0.17))
    alb = lerp(alb, col(40, 40, 36), np.clip(band, 0, 1) * 0.0)
    hgt = weave * 0.5 + fuzz * 0.15
    out("Wool", alb, normal_map(hgt, 1.6), mask_map(np.zeros_like(hgt), ao_from_height(hgt, 2, 1.0), norm01(hgt),
                                               np.full_like(hgt, 0.05)), 0.5)


def fabric_ticking():
    h = w = 512
    Y, X = np.mgrid[0:h, 0:w].astype(F)
    t = (Y % 32) / 32.0
    stripe = smoothstep(0.06, 0.03, np.abs(t - 0.2)) + smoothstep(0.03, 0.015, np.abs(t - 0.32))
    alb = lerp(col(206, 200, 184), col(66, 78, 98), np.clip(stripe, 0, 1))
    weave = 0.5 + 0.5 * np.sin(2 * np.pi * X / 3.0) * np.sin(2 * np.pi * Y / 3.0)
    alb *= (0.94 + 0.06 * weave)[..., None]
    stain = smoothstep(0.6, 1.6, fft_noise(h, w, 2.6, 701))
    ring = smoothstep(0.12, 0.0, np.abs(stain - 0.5)) * 0.3
    alb = lerp(alb, col(150, 126, 82), np.clip(stain * 0.45 + ring, 0, 1))
    dirt = np.clip(0.5 + 0.3 * fft_noise(h, w, 2.0, 702), 0, 1)
    alb *= (0.9 + 0.1 * dirt)[..., None]
    hgt = weave * 0.2 + fft_noise(h, w, 2.4, 703) * 0.3
    out("Ticking", alb, normal_map(hgt, 1.2), mask_map(np.zeros_like(hgt), np.ones_like(hgt), norm01(hgt),
                                                 np.full_like(hgt, 0.08)), 1.0)


def paper_map():
    h = w = 1024
    r = rng(801)
    paper = col(220, 208, 172) * (1 + 0.04 * fft_noise(h, w, 1.0, 802) + 0.05 * fft_noise(h, w, 2.4, 803))[..., None]
    terr = fft_noise(h, w, 3.6, 804)
    terr = norm01(terr)
    N = 34.0
    s = terr * N
    gy, gx = np.gradient(s)
    gm = np.sqrt(gx * gx + gy * gy) + 1e-4
    fr = s - np.floor(s)
    d = np.minimum(fr, 1 - fr) / gm
    idx = (np.floor(s + 0.5).astype(int) % 5 == 0)
    line = smoothstep(0.9, 0.2, d) * np.where(idx, 1.0, 0.55)
    line = np.maximum(line, smoothstep(1.6, 0.7, d) * idx * 0.8)
    forest = smoothstep(-0.2, 0.6, fft_noise(h, w, 2.8, 805)) * smoothstep(0.75, 0.6, terr)
    a = lerp(paper, paper * col(196, 226, 176) * 1.15, forest * 0.6)
    water = smoothstep(0.14, 0.11, terr)
    a = lerp(a, col(150, 186, 206), water)
    a = lerp(a, col(150, 96, 52), line * 0.85)
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    dr = ImageDraw.Draw(img)
    # rivers flowing downhill from random springs
    for k in range(5):
        y, x = r.uniform(0, h), r.uniform(0, w)
        pts = [(x, y)]
        for _ in range(500):
            iy, ix = int(y) % h, int(x) % w
            vx, vy = -gx[iy, ix], -gy[iy, ix]
            l = math.hypot(vx, vy) + 1e-6
            x += vx / l * 3 + r.normal(0, 0.8)
            y += vy / l * 3 + r.normal(0, 0.8)
            if not (0 < x < w and 0 < y < h):
                break
            pts.append((x, y))
            if terr[int(y) % h, int(x) % w] < 0.12:
                break
        if len(pts) > 20:
            dr.line(pts, fill=(64, 112, 160), width=2)
    # section grid (township/range style)
    for i in range(1, 6):
        p = int(i * w / 6)
        dr.line([(p, 0), (p, h)], fill=(176, 64, 54), width=2)
        dr.line([(0, p), (w, p)], fill=(176, 64, 54), width=2)
    # roads (dashed)
    for k in range(2):
        y = r.uniform(100, h - 100)
        pts = [(0, y)]
        x = 0
        while x < w:
            x += 12
            y += r.normal(0, 3)
            pts.append((x, y))
        for i in range(0, len(pts) - 1, 2):
            dr.line([pts[i], pts[i + 1]], fill=(60, 50, 40), width=2)
    # lookout marker (centre) and small tick labels
    cx, cy = w / 2, h / 2
    dr.polygon([(cx, cy - 12), (cx - 10, cy + 8), (cx + 10, cy + 8)], outline=(120, 30, 30), width=3)
    for k in range(40):
        x, y = r.uniform(30, w - 60), r.uniform(30, h - 30)
        for j in range(r.integers(3, 7)):
            dr.rectangle([x + j * 6, y, x + j * 6 + 3, y + 5], fill=(70, 60, 50))
    a = np.asarray(img).astype(F) / 255
    # age: stains, creases, edge darkening
    stain = smoothstep(0.8, 2.0, fft_noise(h, w, 2.4, 806))
    a = lerp(a, a * col(200, 170, 120), stain * 0.6)
    Y, X = np.mgrid[0:h, 0:w].astype(F)
    crease = smoothstep(3, 0, np.abs(X - w / 3)) + smoothstep(3, 0, np.abs(Y - h / 2))
    a = lerp(a, a * 0.82, np.clip(crease, 0, 1) * 0.5)
    e = np.minimum(np.minimum(X, w - X), np.minimum(Y, h - Y)) / w
    a *= (0.78 + 0.22 * smoothstep(0.0, 0.08, e))[..., None]
    hgt = crease * 0.2 + fft_noise(h, w, 1.0, 807) * 0.05
    out("Map", a, normal_map(hgt, 1.5), mask_map(np.zeros_like(hgt), np.ones_like(hgt), norm01(hgt),
                                          np.full_like(hgt, 0.1)), 1.0)


def painted_metal():
    h = w = 512
    alb = col(76, 82, 54) * (1 + 0.06 * fft_noise(h, w, 2.0, 901))[..., None]
    sc, scov = strokes(h, w, 160, 902, length=(5, 40), width=(0.6, 1.4), colors=((160, 158, 150),),
                       alpha=(0.4, 0.9), curve=0.2)
    chip = smoothstep(1.6, 2.0, fft_noise(h, w, 1.6, 903))
    alb = lerp(alb, col(56, 54, 50), chip)
    alb = lerp(alb, col(150, 150, 144), scov)
    metal = np.clip(scov * 0.9 + chip * 0.3, 0, 1)
    sm = 0.35 + 0.2 * scov
    hgt = -scov * 0.3 - chip * 0.4
    out("PaintedMetal", alb, normal_map(hgt, 2.0), mask_map(metal, np.ones_like(hgt), norm01(hgt), sm), 0.5)


def wood_furniture():
    h = w = 512
    a, hgt, late, fib = wood_plain(h, w, 1001, (152, 106, 64), (118, 80, 48), rings=26, knots=2)
    sc, scov = strokes(h, w, 90, 1002, length=(8, 50), width=(0.5, 1.2), colors=((186, 150, 110),),
                       alpha=(0.2, 0.6), curve=0.1)
    a = lerp(a, col(186, 150, 110), scov * 0.6)
    sm = np.clip(0.45 - 0.2 * scov + 0.05 * fft_noise(h, w, 2.0, 1003), 0, 1)
    out("WoodFurniture", a, normal_map(hgt - scov * 0.2, 2.5), mask_map(np.zeros_like(hgt), np.ones_like(hgt),
                                                                    norm01(hgt), sm), 1.0)


def siding_cream():
    h = w = 1024
    tile = 2.0
    board_px = 64  # 0.125 m exposure
    L = plank_layout(h, w, 1101, board_px, joists=4, joints=(0, 1))
    late, fib, _ = wood_grain(h, w, 1102, rings=30, warp=1.6, stretch=9)
    late = sample_by_segment(late, L["seg"], L["n"], 1103)
    wood = lerp(col(138, 132, 120), col(98, 90, 80), late * 0.8)
    # lap profile: thicker toward the bottom edge of each board (rows increase downwards)
    lap = L["vloc"] ** 0.85
    hgt = lap * 1.0 - late * 0.08
    gap, bev, e = edge_profile(L, 2, 0.0, joint=True)
    jgap = smoothstep(2.0, 0.5, L["jdist"])
    hgt -= jgap * 0.4
    alb, hgt2, peel = paint_over(h, w, 1104, wood, hgt, col(214, 208, 188), peel=0.22, board_v=L["vloc"],
                                 streaks=0.7)
    shadow = smoothstep(0.08, 0.0, L["vloc"]) * 0.45  # shadow line under each lap
    alb *= (1 - shadow)[..., None]
    alb = lerp(alb, col(40, 36, 32), jgap * 0.8)
    nrm = normal_map(hgt2, 2.2)
    ao = np.clip(ao_from_height(hgt2, 5, 1.2) * (1 - shadow), 0, 1)
    sm = np.clip(0.32 * (1 - peel) + 0.12 * peel, 0, 1)
    out("Siding", alb, nrm, mask_map(np.zeros_like(hgt), ao, norm01(hgt2), sm), tile)


def paint_trim(name, rgb, seed, peel=0.2):
    h = w = 512
    wood, wh, late, fib = wood_plain(h, w, seed, (136, 130, 118), (96, 88, 78), rings=18, knots=0)
    alb, hgt, peel_m = paint_over(h, w, seed + 1, wood, wh, col(*rgb), peel=peel, streaks=0.45)
    out(name, alb, normal_map(hgt, 2.0), mask_map(np.zeros_like(hgt), ao_from_height(hgt, 3, 1.0), norm01(hgt),
                                            0.36 * (1 - peel_m) + 0.12 * peel_m), 1.0)


def roof_shakes():
    h = w = 1024
    tile = 2.0
    course_px = 128
    r = rng(1201)
    nc = h // course_px
    shake = np.zeros((h, w), np.int32)
    gapd = np.full((h, w), 1e6, F)
    count = 0
    xs = np.arange(w, dtype=F) + 0.5
    for c in range(nc):
        cuts = []
        x = r.uniform(0, w)
        start = x
        while True:
            cuts.append(x % w)
            x += r.uniform(60, 170)
            if x - start > w - 50:
                break
        cuts = np.sort(np.array(cuts))
        idx = np.searchsorted(cuts, xs)
        idx[idx == len(cuts)] = 0
        y0 = c * course_px
        shake[y0:y0 + course_px] = (count + idx)[None, :]
        d = np.abs(xs[:, None] - cuts[None, :]); d = np.minimum(d, w - d)
        gapd[y0:y0 + course_px] = d.min(1)[None, :]
        count += len(cuts)
    vloc = ((np.arange(h) % course_px + 0.5) / course_px)[:, None].repeat(w, 1).astype(F)
    # grain runs down the shake (along rows): build grain on transposed grid
    lateT, fibT, _ = wood_grain(w, h, 1202, rings=40, warp=1.2, stretch=8)
    late = sample_by_segment(lateT.T.copy(), shake, count, 1203)
    fib = sample_by_segment(fibT.T.copy(), shake, count, 1203)
    cols = palette_pick(count, 1204, [(100, 92, 82), (84, 76, 66), (114, 106, 94), (74, 64, 54), (92, 80, 66)], 0.08)
    alb = cols[shake] * (1 - 0.3 * late[..., None]) * (1 + 0.06 * fib[..., None])
    keyway = smoothstep(3.0, 1.0, gapd)
    hgt = vloc ** 0.9 * 1.4 - late * 0.12 + fib * 0.05 - keyway * 0.8
    shadow = smoothstep(0.12, 0.0, vloc) * 0.55
    mn = fft_noise(h, w, 2.1, 1205)
    moss = np.clip(smoothstep(0.35, 1.3, mn) * (0.5 + 0.8 * vloc + keyway), 0, 1)
    moss_col = lerp(col(50, 62, 36), col(82, 92, 54), np.clip(0.5 + 0.4 * fft_noise(h, w, 1.2, 1206), 0, 1))
    alb = lerp(alb, moss_col, moss * 0.85)
    lc, _ = blobs(h, w, 160, 1207, radius=(2, 7))
    alb = lerp(alb, col(150, 156, 126), lc * 0.6)
    alb = lerp(alb, col(22, 20, 18), keyway * 0.9)
    alb *= (1 - shadow)[..., None]
    hgt += moss * 0.25 + 0.15 * fft_noise(h, w, 0.6, 1208) * moss
    nrm = normal_map(hgt, 2.4)
    ao = np.clip(ao_from_height(hgt, 6, 1.0) * (1 - shadow * 0.6), 0, 1)
    sm = np.clip(0.16 - 0.08 * moss, 0.02, 1)
    out("RoofShakes", alb, nrm, mask_map(np.zeros_like(hgt), ao, norm01(hgt), sm), tile)


def bark_pine():
    h = w = 1024
    F1, F2, cell = worley(h, w, 70, 1301, aniso=(1.0, 0.28))
    fur = smoothstep(1.5, 16.0, F2 - F1)
    f1, f2, c2 = worley(h, w, 900, 1306, aniso=(1.0, 0.55))
    flake = smoothstep(0.4, 3.0, f2 - f1)
    r = rng(1302)
    pal = np.array([(116, 76, 52), (100, 72, 56), (126, 88, 62), (90, 72, 60), (108, 82, 64)], F) / 255
    pc = pal[r.integers(0, len(pal), 70)][cell] * r.uniform(0.88, 1.12, 70)[cell][..., None]
    fl = r.uniform(0.82, 1.12, 900)[c2]
    alb = pc * fl[..., None] * (0.8 + 0.2 * flake)[..., None]
    alb *= (1 + 0.08 * fft_noise(h, w, 1.2, 1303))[..., None]
    grey = smoothstep(0.3, 1.6, fft_noise(h, w, 2.4, 1307))
    alb = lerp(alb, alb.mean(-1, keepdims=True) * col(150, 146, 140) * 1.4, grey * 0.5)
    alb = lerp(col(24, 19, 16), alb, fur)
    lich = smoothstep(1.5, 2.2, fft_noise(h, w, 1.8, 1304)) * fur
    alb = lerp(alb, col(128, 138, 110), lich * 0.55)
    hgt = fur * 1.2 + 0.15 * flake + 0.25 * (fl - 1) + 0.1 * fft_noise(h, w, 2.4, 1305)
    out("Bark", alb, normal_map(hgt, 1.4), mask_map(np.zeros_like(hgt), ao_from_height(hgt, 6, 1.4), norm01(hgt),
                                             np.full_like(hgt, 0.1)), 1.0)


def pine_needles():
    h = w = 512
    bg = col(14, 22, 14) * (1 + 0.3 * fft_noise(h, w, 2.0, 1401))[..., None]
    rgb = bg.copy()
    hacc = np.zeros((h, w), F)
    pal = [(34, 58, 30), (44, 72, 36), (26, 46, 24), (58, 84, 42), (38, 64, 40)]
    for layer, (n, al) in enumerate([(2600, (0.6, 0.9)), (2600, (0.7, 1.0)), (1600, (0.8, 1.0))]):
        cols = [tuple(np.array(c) / 255 * (0.7 + 0.15 * layer)) for c in pal]
        c, cov = strokes(h, w, n, 1402 + layer, length=(10, 22), width=(1.0, 1.8), angle=0.0,
                         angle_jit=0.55, colors=cols, alpha=al, curve=0.15)
        rgb = lerp(rgb, c, np.clip(cov, 0, 1))
        hacc = hacc * (1 - cov) + cov * (0.4 + 0.3 * layer)
    dead = smoothstep(1.5, 2.3, fft_noise(h, w, 2.0, 1405))
    rgb = lerp(rgb, col(88, 72, 44), dead * 0.6)
    out("Needles", rgb, normal_map(hacc, 2.5), mask_map(np.zeros_like(hacc), ao_from_height(hacc, 3, 1.5),
                                                 norm01(hacc), 0.22 + 0.1 * hacc), 1.0)


def ground_forest():
    h = w = 1024
    base = lerp(col(70, 54, 40), col(92, 74, 54), np.clip(0.5 + 0.3 * fft_noise(h, w, 2.4, 1501), 0, 1))
    base *= (1 + 0.1 * fft_noise(h, w, 1.0, 1502))[..., None]
    rgb = base
    hgt = 0.2 * fft_noise(h, w, 2.6, 1503)
    pc, pcov = blobs(h, w, 260, 1504, radius=(1.5, 5))
    rgb = lerp(rgb, col(112, 108, 100), pc * 0.8)
    hgt += pc * 0.4
    tw, twc = strokes(h, w, 70, 1505, length=(30, 110), width=(1.5, 3.5), colors=((60, 44, 30), (84, 64, 44)),
                      alpha=(0.8, 1.0), curve=0.15)
    rgb = lerp(rgb, tw, twc)
    hgt = hgt * (1 - twc) + twc * 0.8
    pal = [(150, 104, 62), (120, 84, 50), (168, 128, 82), (98, 72, 46), (132, 96, 58)]
    for layer in range(3):
        cols = [tuple(np.array(c) / 255 * (0.8 + 0.12 * layer)) for c in pal]
        nd, ndc = strokes(h, w, 4200, 1506 + layer, length=(10, 24), width=(1.0, 1.8), colors=cols,
                          alpha=(0.7, 1.0), curve=0.12)
        rgb = lerp(rgb, nd, ndc)
        hgt = hgt * (1 - ndc) + ndc * (0.5 + 0.2 * layer)
    moss = smoothstep(0.9, 1.8, fft_noise(h, w, 2.6, 1509))
    rgb = lerp(rgb, col(58, 70, 38), moss * 0.6)
    out("ForestFloor", rgb, normal_map(hgt, 3.0), mask_map(np.zeros_like(hgt), ao_from_height(hgt, 3, 1.5),
                                                    norm01(hgt), np.full_like(hgt, 0.08)), 4.0)


def ground_moss():
    h = w = 1024
    n = fft_noise(h, w, 2.6, 1601)
    rgb = lerp(col(40, 52, 28), col(78, 92, 44), np.clip(0.5 + 0.35 * n, 0, 1))
    hgt = 0.4 * n
    clump = fft_noise(h, w, 0.9, 1602)
    rgb *= (1 + 0.15 * clump)[..., None]
    hgt += 0.3 * clump
    pal = [(96, 112, 54), (72, 90, 40), (120, 124, 64), (54, 70, 34)]
    for layer in range(2):
        g, gc = strokes(h, w, 3500, 1603 + layer, length=(6, 16), width=(0.8, 1.5), colors=[tuple(np.array(c) / 255) for c in pal],
                        alpha=(0.6, 1.0), curve=0.2)
        rgb = lerp(rgb, g, gc)
        hgt = hgt * (1 - gc) + gc * (0.6 + 0.2 * layer)
    nd, ndc = strokes(h, w, 900, 1605, length=(10, 20), width=(0.8, 1.2), colors=((140, 100, 60), (110, 80, 50)),
                      alpha=(0.6, 0.9))
    rgb = lerp(rgb, nd, ndc * 0.8)
    out("Moss", rgb, normal_map(hgt, 2.6), mask_map(np.zeros_like(hgt), ao_from_height(hgt, 3, 1.5), norm01(hgt),
                                             np.full_like(hgt, 0.1)), 4.0)


def ground_gravel():
    h = w = 1024
    F1, F2, cell = worley(h, w, 5200, 1701)
    r = rng(1702)
    pal = np.array([(118, 114, 106), (96, 92, 86), (138, 132, 120), (104, 94, 80), (80, 76, 72)], F) / 255
    pc = pal[r.integers(0, len(pal), 5200)][cell]
    stone = smoothstep(0.5, 4.0, F2 - F1)
    dirt = lerp(col(70, 58, 44), col(90, 76, 58), np.clip(0.5 + 0.3 * fft_noise(h, w, 2.0, 1703), 0, 1))
    rgb = lerp(dirt, pc * (1 + 0.08 * fft_noise(h, w, 0.8, 1704))[..., None], stone)
    hgt = stone * (1 - (F1 / (F2 + 1e-3)) ** 2) + 0.1 * fft_noise(h, w, 2.4, 1705)
    nd, ndc = strokes(h, w, 700, 1706, length=(10, 20), width=(0.8, 1.2), colors=((140, 100, 60), (110, 80, 50)),
                      alpha=(0.6, 0.9))
    rgb = lerp(rgb, nd, ndc * 0.7)
    out("Gravel", rgb, normal_map(hgt, 3.0), mask_map(np.zeros_like(hgt), ao_from_height(hgt, 3, 1.6), norm01(hgt),
                                               0.15 + 0.1 * stone), 3.0)


def rock_granite():
    h = w = 1024
    n = fft_noise(h, w, 2.6, 1801)
    rgb = col(124, 122, 116) * (1 + 0.12 * n)[..., None]
    sp = fft_noise(h, w, 0.0, 1802, fmax=300)
    rgb = lerp(rgb, col(52, 50, 48), smoothstep(1.6, 2.4, sp) * 0.8)
    rgb = lerp(rgb, col(196, 192, 184), smoothstep(-1.7, -2.4, sp) * 0.6)
    F1, F2, _ = worley(h, w, 40, 1803)
    crack = smoothstep(4.0, 0.5, F2 - F1) * smoothstep(-0.5, 0.8, fft_noise(h, w, 2.0, 1804))
    rgb = lerp(rgb, col(46, 44, 42), crack * 0.85)
    lich = smoothstep(0.7, 1.4, fft_noise(h, w, 2.3, 1805))
    lc = lerp(col(148, 156, 120), col(112, 126, 92), np.clip(0.5 + 0.4 * fft_noise(h, w, 1.0, 1806), 0, 1))
    rgb = lerp(rgb, lc, lich * 0.75)
    moss = smoothstep(1.4, 2.0, fft_noise(h, w, 2.0, 1807))
    rgb = lerp(rgb, col(54, 70, 38), moss * 0.8)
    hgt = 0.5 * n + 0.15 * fft_noise(h, w, 1.2, 1808) - crack * 0.8 + lich * 0.1 + moss * 0.2
    out("Rock", rgb, normal_map(hgt, 2.4), mask_map(np.zeros_like(hgt), ao_from_height(hgt, 5, 1.2), norm01(hgt),
                                             0.22 - 0.1 * lich), 2.0)


JOBS = {
    "Timber": timber_weathered,
    "PlanksWeathered": lambda: planks_material(
        "PlanksWeathered", 1024, 2.0, 2001, 0.14, [(138, 128, 112), (120, 110, 96), (150, 142, 128), (110, 98, 84), (128, 116, 100)],
        gap_px=2.2, bevel_px=3, smooth=0.2, weather=0.55, moss=0.8),
    "PlanksInterior": lambda: planks_material(
        "PlanksInterior", 1024, 2.0, 2101, 0.14, [(126, 88, 56), (110, 76, 46), (138, 98, 62), (100, 70, 42), (118, 84, 52)],
        gap_px=1.4, bevel_px=2, smooth=0.38, weather=0.12),
    "Paneling": lambda: planks_material(
        "Paneling", 1024, 2.0, 2201, 0.125, [(168, 118, 70), (150, 104, 60), (180, 128, 78), (140, 96, 54), (160, 112, 66)],
        gap_px=0.9, bevel_px=5, smooth=0.42, weather=0.0, joints=(0, 0), transpose=True, grain_contrast=0.28),
    "Siding": siding_cream,
    "TrimGreen": lambda: paint_trim("TrimGreen", (52, 76, 58), 2301, peel=0.18),
    "TrimCream": lambda: paint_trim("TrimCream", (214, 208, 188), 2401, peel=0.15),
    "RoofShakes": roof_shakes,
    "Iron": iron_rusty,
    "Brass": brass,
    "Concrete": concrete,
    "GlassGrime": glass_grime,
    "Wool": fabric_wool,
    "Ticking": fabric_ticking,
    "Map": paper_map,
    "PaintedMetal": painted_metal,
    "WoodFurniture": wood_furniture,
    "Bark": bark_pine,
    "Needles": pine_needles,
    "ForestFloor": ground_forest,
    "Moss": ground_moss,
    "Gravel": ground_gravel,
    "Rock": rock_granite,
}

if __name__ == "__main__":
    tiles_path = os.path.join(OUT, "tiles.json")
    if os.path.exists(tiles_path):
        TILE.update(json.load(open(tiles_path)))
    for k, fn in JOBS.items():
        if want(k):
            fn()
    TILE.update({"PlanksWeathered": 2.0, "PlanksInterior": 2.0, "Paneling": 2.0})
    json.dump(TILE, open(tiles_path, "w"), indent=1)
