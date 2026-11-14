"""Tileable procedural texture toolkit (numpy). All fields wrap seamlessly."""
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.spatial import cKDTree

F = np.float32


def rng(seed):
    return np.random.default_rng(seed)


def fft_noise(h, w, beta=2.0, seed=0, sx=1.0, sy=1.0, fmin=0.0, fmax=None):
    """Tileable noise with power spectrum ~ 1/f^beta. Returns zero-mean, unit-std field.
    sx/sy > 1 penalise frequencies along that axis -> features elongated along that axis."""
    r = rng(seed)
    fy = np.fft.fftfreq(h)[:, None] * h
    fx = np.fft.fftfreq(w)[None, :] * w
    f = np.sqrt((fx * sx) ** 2 + (fy * sy) ** 2)
    f0 = f.copy()
    f[0, 0] = 1.0
    amp = 1.0 / f ** (beta / 2.0)
    amp[0, 0] = 0.0
    if fmin:
        amp[f0 < fmin] = 0.0
    if fmax:
        amp *= np.exp(-(f0 / fmax) ** 2)
    phase = r.uniform(0, 2 * np.pi, (h, w))
    n = np.fft.ifft2(amp * np.exp(1j * phase)).real
    n -= n.mean()
    s = n.std()
    return (n / (s if s > 0 else 1)).astype(F)


def worley(h, w, n, seed=0, aniso=(1.0, 1.0), k=2):
    """Periodic Worley noise. Returns F1, F2 (pixel units) and the id of the nearest cell."""
    r = rng(seed)
    pts = r.uniform(0, 1, (n, 2)) * np.array([w, h])
    offs = np.array([(dx * w, dy * h) for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
    allp = (pts[None, :, :] + offs[:, None, :]).reshape(-1, 2)
    ids = np.tile(np.arange(n), 9)
    a = np.array(aniso, dtype=np.float64)
    tree = cKDTree(allp * a)
    yy, xx = np.mgrid[0:h, 0:w]
    q = np.stack([xx.ravel() + 0.5, yy.ravel() + 0.5], 1) * a
    d, i = tree.query(q, k=k)
    F1 = d[:, 0].reshape(h, w).astype(F)
    F2 = d[:, 1].reshape(h, w).astype(F) if k > 1 else None
    cell = ids[i[:, 0]].reshape(h, w)
    return F1, F2, cell


def blur(a, sigma):
    if a.ndim == 3:
        return np.stack([ndimage.gaussian_filter(a[..., c], sigma, mode="wrap") for c in range(a.shape[2])], -1)
    return ndimage.gaussian_filter(a, sigma, mode="wrap")


def warp(a, dx, dy):
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(F)
    coords = [yy + dy, xx + dx]
    if a.ndim == 3:
        return np.stack([ndimage.map_coordinates(a[..., c], coords, order=1, mode="grid-wrap") for c in range(a.shape[2])], -1)
    return ndimage.map_coordinates(a, coords, order=1, mode="grid-wrap")


def remap(n, lo, hi):
    return lo + (hi - lo) * n


def norm01(a):
    a = a - a.min()
    m = a.max()
    return a / (m if m > 0 else 1)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    if np.ndim(t) == 2 and (np.ndim(a) in (1, 3) or np.ndim(b) in (1, 3)):
        t = np.asarray(t)[..., None]
    return a + (b - a) * t


def col(*rgb):
    return np.array(rgb, dtype=F) / 255.0


def normal_map(hgt, strength=1.0):
    """OpenGL-style (Unity) tangent-space normal map from a height field (wraps)."""
    dx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * 0.5
    dyr = (np.roll(hgt, -1, 0) - np.roll(hgt, 1, 0)) * 0.5
    nx = -dx * strength
    ny = dyr * strength
    nz = np.ones_like(hgt)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return n * 0.5 + 0.5


def save(path, arr, mode=None):
    a = np.clip(arr, 0, 1)
    a = (a * 255 + 0.5).astype(np.uint8)
    if mode is None:
        mode = {2: "L", 3: "RGB", 4: "RGBA"}[a.ndim if a.ndim == 2 else a.shape[2] if a.shape[2] in (3, 4) else 0] if a.ndim == 3 else "L"
    Image.fromarray(a, mode=mode if a.ndim == 3 else "L").save(path, optimize=True)


def mask_map(metallic, ao, height, smooth):
    """URP packing: R metallic, G occlusion, B height/detail mask, A smoothness."""
    return np.stack([metallic, ao, height, smooth], -1)


def ao_from_height(hgt, radius=4, strength=1.0):
    """Cheap cavity AO: how far below its blurred neighbourhood a pixel sits."""
    b = blur(hgt, radius)
    cav = np.clip((b - hgt) * strength, 0, 1)
    return np.clip(1.0 - cav, 0, 1)


def strokes(h, w, n, seed, length=(6, 20), width=(1, 2), angle=None, angle_jit=np.pi,
            colors=((1, 1, 1),), alpha=(0.6, 1.0), ss=2, curve=0.0, value_mode=False):
    """Draw many anti-aliased line strokes with wraparound. Returns (rgb, coverage)."""
    r = rng(seed)
    H, W = h * ss, w * ss
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cols = np.array(colors, dtype=np.float64)
    if cols.max() > 1.0:
        cols = cols / 255.0
    for _ in range(n):
        x, y = r.uniform(0, W), r.uniform(0, H)
        L = r.uniform(*length) * ss
        a = (angle if angle is not None else 0.0) + r.uniform(-angle_jit, angle_jit)
        wd = max(1, int(round(r.uniform(*width) * ss)))
        c = cols[r.integers(len(cols))] * r.uniform(0.85, 1.15)
        c = np.clip(c, 0, 1)
        al = r.uniform(*alpha)
        rgba = (int(c[0] * 255), int(c[1] * 255), int(c[2] * 255), int(al * 255))
        bend = r.uniform(-curve, curve)
        pts = []
        segs = 4 if curve else 1
        for s in range(segs + 1):
            t = s / segs
            px = x + np.cos(a) * L * t - np.sin(a) * bend * L * t * (1 - t)
            py = y + np.sin(a) * L * t + np.cos(a) * bend * L * t * (1 - t)
            pts.append((px, py))
        for ox in (-W, 0, W):
            for oy in (-H, 0, H):
                pp = [(p[0] + ox, p[1] + oy) for p in pts]
                xs = [p[0] for p in pp]
                ys = [p[1] for p in pp]
                if max(xs) < 0 or min(xs) > W or max(ys) < 0 or min(ys) > H:
                    continue
                d.line(pp, fill=rgba, width=wd)
    img = img.resize((w, h), Image.LANCZOS)
    a = np.asarray(img).astype(F) / 255.0
    return a[..., :3], a[..., 3]


def blobs(h, w, n, seed, radius=(2, 6), ss=2):
    """Random filled ellipses (wraparound) -> coverage mask and per-blob random value map."""
    r = rng(seed)
    H, W = h * ss, w * ss
    cov = Image.new("L", (W, H), 0)
    val = Image.new("L", (W, H), 0)
    dc, dv = ImageDraw.Draw(cov), ImageDraw.Draw(val)
    for _ in range(n):
        x, y = r.uniform(0, W), r.uniform(0, H)
        rx = r.uniform(*radius) * ss
        ry = rx * r.uniform(0.6, 1.0)
        v = int(r.uniform(40, 255))
        for ox in (-W, 0, W):
            for oy in (-H, 0, H):
                bb = [x + ox - rx, y + oy - ry, x + ox + rx, y + oy + ry]
                if bb[2] < 0 or bb[0] > W or bb[3] < 0 or bb[1] > H:
                    continue
                dc.ellipse(bb, fill=255)
                dv.ellipse(bb, fill=v)
    cov = np.asarray(cov.resize((w, h), Image.LANCZOS)).astype(F) / 255
    val = np.asarray(val.resize((w, h), Image.BILINEAR)).astype(F) / 255
    return cov, val
