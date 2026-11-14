"""Contact sheet: per material -> albedo tiled 2x2 (seam check) and a lit preview."""
import sys, os, glob
import numpy as np
from PIL import Image, ImageDraw

TEX = sys.argv[1]
OUTP = sys.argv[2]
names = sys.argv[3].split(",") if len(sys.argv) > 3 else sorted(
    {os.path.basename(p)[2:].rsplit("_", 1)[0] for p in glob.glob(f"{TEX}/T_*_Albedo.png")})
S = 320
rows = []
for n in names:
    a = Image.open(f"{TEX}/T_{n}_Albedo.png").convert("RGBA")
    an = np.asarray(a).astype(np.float32) / 255
    tile = np.tile(an[..., :3], (2, 2, 1))
    tile_img = Image.fromarray((tile * 255).astype(np.uint8)).resize((S, S), Image.LANCZOS)
    p = f"{TEX}/T_{n}_Normal.png"
    if os.path.exists(p):
        nm = np.asarray(Image.open(p).convert("RGB")).astype(np.float32) / 255 * 2 - 1
        L = np.array([-0.5, 0.6, 0.62]); L /= np.linalg.norm(L)
        lam = np.clip((nm * L).sum(-1), 0, 1)
        lit = an[..., :3] * (0.25 + 0.95 * lam[..., None])
    else:
        lit = an[..., :3]
    lit_img = Image.fromarray((np.clip(lit, 0, 1) * 255).astype(np.uint8)).resize((S, S), Image.LANCZOS)
    crop = Image.fromarray((np.clip(lit, 0, 1) * 255).astype(np.uint8))
    h, w = crop.size[1], crop.size[0]
    crop = crop.crop((w // 2 - S // 2, h // 2 - S // 2, w // 2 + S // 2, h // 2 + S // 2)) if min(w, h) >= S else crop.resize((S, S))
    row = Image.new("RGB", (S * 3 + 20, S + 22), (30, 30, 30))
    row.paste(tile_img, (0, 22)); row.paste(lit_img, (S + 10, 22)); row.paste(crop, (2 * S + 20, 22))
    ImageDraw.Draw(row).text((4, 4), f"{n}  (2x2 tile | lit | 1:1 crop)", fill=(230, 230, 230))
    rows.append(row)
sheet = Image.new("RGB", (rows[0].width, sum(r.height for r in rows)), (0, 0, 0))
y = 0
for r in rows:
    sheet.paste(r, (0, y)); y += r.height
sheet.save(OUTP)
print(OUTP, sheet.size)
