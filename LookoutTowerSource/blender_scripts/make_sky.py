"""Equirectangular night sky for Unity's Skybox/Panoramic (latitude-longitude layout).
Horizon colour == scene fog colour; moon placed opposite the moonlight's forward vector."""
import sys, os, math
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
from texlib import fft_noise, smoothstep

W, H = 4096, 2048
FOG = np.array([16, 20, 28], np.float32) / 255.0          # sRGB, must match RenderSettings.fogColor
ZEN = np.array([3, 4, 8], np.float32) / 255.0
HAZE = np.array([26, 31, 42], np.float32) / 255.0
# moonlight rotation Euler(35, -30, 0) -> forward; moon is at -forward
pitch, yaw = math.radians(35), math.radians(-30)
fwd = np.array([math.sin(yaw) * math.cos(pitch), -math.sin(pitch), math.cos(yaw) * math.cos(pitch)])
moon = -fwd / np.linalg.norm(fwd)

r = (np.arange(H, dtype=np.float32) + 0.5) / H
u = (np.arange(W, dtype=np.float32) + 0.5) / W
theta = r * math.pi                      # polar angle from +Y (zenith)
phi = (0.5 - u) * 2 * math.pi            # atan2(z, x) per Unity's panoramic shader
st, ct = np.sin(theta)[:, None], np.cos(theta)[:, None]
x = st * np.cos(phi)[None, :]
z = st * np.sin(phi)[None, :]
y = np.broadcast_to(ct, (H, W))
elev = np.arcsin(np.clip(y, -1, 1))

t = np.clip(elev / (math.pi / 2), 0, 1) ** 0.45
sky = FOG[None, None, :] * (1 - t[..., None]) + ZEN[None, None, :] * t[..., None]
haze = np.exp(-np.maximum(elev, 0) / 0.08)[..., None]
sky = sky * (1 - haze * 0.6) + HAZE * haze * 0.6
below = elev < 0
sky[below] = FOG * 0.9

# clouds (periodic noise wraps horizontally), stretched along longitude
n1 = fft_noise(H, W, 3.0, 5, sx=0.35, sy=1.0)
n2 = fft_noise(H, W, 2.0, 6, sx=0.5, sy=1.0)
cl = smoothstep(0.2, 1.6, 0.7 * n1 + 0.3 * n2) * smoothstep(0.0, 0.25, elev) * (1 - smoothstep(0.9, 1.4, elev))

cosang = x * moon[0] + y * moon[1] + z * moon[2]
ang = np.arccos(np.clip(cosang, -1, 1))
glow = (np.exp(-ang / 0.045) * 0.22 + np.exp(-ang / 0.22) * 0.07)[..., None]
moon_col = np.array([0.72, 0.78, 0.95], np.float32)
sky = sky + glow * moon_col

# stars: uniform on the sphere, fainter near the horizon, hidden by clouds
rng = np.random.default_rng(9)
N = 9000
v = rng.normal(size=(N, 3)); v /= np.linalg.norm(v, axis=1, keepdims=True)
v = v[v[:, 1] > 0.02]
mag = rng.pareto(2.2, len(v)) * 0.25 + 0.15
img_star = np.zeros((H, W), np.float32)
th = np.arccos(v[:, 1]); ph = np.arctan2(v[:, 2], v[:, 0])
rows = th / math.pi * H - 0.5
cols = (0.5 - ph / (2 * math.pi)) % 1.0 * W - 0.5
for rr, cc, m_, yy in zip(rows, cols, mag, v[:, 1]):
    b = min(1.0, m_) * smoothstep(0.02, 0.35, yy)
    r0, c0 = int(rr), int(cc)
    fr, fc = rr - r0, cc - c0
    for dr, wr in ((0, 1 - fr), (1, fr)):
        for dc, wc in ((0, 1 - fc), (1, fc)):
            img_star[min(H - 1, r0 + dr), (c0 + dc) % W] += b * wr * wc
star_col = np.array([0.85, 0.9, 1.0], np.float32)
sky = sky + (img_star * (1 - cl) * (1 - np.clip(glow[..., 0] * 3, 0, 1)))[..., None] * star_col

cloud_col = np.array([10, 12, 17], np.float32) / 255.0
lit = np.clip(glow[..., 0] * 5.0, 0, 1)[..., None]
cc_ = cloud_col * (1 - lit) + np.array([0.32, 0.35, 0.42], np.float32) * lit
sky = sky * (1 - cl[..., None] * 0.85) + cc_ * cl[..., None] * 0.85

# moon disc (drawn after clouds; thin clouds dim it slightly)
R = math.radians(0.75)
disc = smoothstep(R, R * 0.92, ang)
maria = 0.82 + 0.18 * fft_noise(H, W, 1.5, 7, fmax=900)[..., None] * 0.5
sky = sky * (1 - disc[..., None]) + (np.array([1.0, 0.97, 0.9]) * maria) * disc[..., None] * (1 - cl[..., None] * 0.5)

out = np.clip(sky, 0, 1)
Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(sys.argv[1] if len(sys.argv) > 1 else "/home/claude/lookout/sky/T_SkyNight.png", optimize=True)
row = (math.acos(moon[1]) / math.pi) * H
col = ((0.5 - math.atan2(moon[2], moon[0]) / (2 * math.pi)) % 1.0) * W
print("moon dir", moon, "pixel", round(col), round(row))
