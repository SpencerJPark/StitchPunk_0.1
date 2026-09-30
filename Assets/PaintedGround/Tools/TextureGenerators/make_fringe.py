"""T_EdgeFringe.png: painted turf-border strips for Painted/Ground.
1024 x 1024, four rows of 256 px. Within a row, top = inside the turf, bottom = outside.
R = coverage silhouette, G = highlight rim (just inside the silhouette), B = ink line (just outside).
Rows: 0 grass blades, 1 forest leafy scallops, 2 stone chips, 3 cliff rock chunks."""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1024, 256

def draw_row(fn, seed):
    im = Image.new("L", (W * 3, H), 0)          # draw 3 wide and take the middle so it tiles
    d = ImageDraw.Draw(im)
    fn(d, np.random.default_rng(seed))
    a = np.array(im, dtype=np.float32) / 255.0
    # make tileable by wrapping the overflow
    a = a[:, W:2 * W] + np.roll(a[:, :W], 0, 1) * 0 + a[:, 2 * W:] * 0
    a = np.array(im, dtype=np.float32) / 255.0
    core = a[:, W:2 * W].copy()
    core = np.maximum(core, a[:, :W])            # left overflow wraps onto the right... (drawn at x<W lands on x+W)
    core = np.maximum(core, a[:, 2 * W:])
    # close enclosed holes: flood the exterior from the bottom edge, anything else that is
    # still empty is a hole inside the turf
    im2 = Image.fromarray(np.where(core > 0.5, 255, 0).astype(np.uint8)).copy()
    for x in range(0, W, 2):
        if im2.getpixel((x, H - 1)) == 0:
            ImageDraw.floodfill(im2, (x, H - 1), 128)
    arr = np.array(im2)
    holes = (arr == 0).astype(np.float32)          # empty but not connected to the outside
    return np.clip(np.maximum(core, holes), 0, 1)

def blades(d, r):
    d.rectangle([0, 0, W * 3, int(H * 0.30)], fill=255)          # solid inside
    x = 0.0
    while x < W * 3:
        base_y = H * 0.30
        tip = H * (0.45 + 0.5 * r.random())
        lean = (r.random() - 0.5) * 60
        wdt = 10 + 22 * r.random()
        # a blade: triangle from the base up to a tip, slightly curved by an extra midpoint
        pts = [(x - wdt / 2, base_y), (x + wdt / 2, base_y), (x + lean * 0.6 + wdt * 0.15, (base_y + tip) / 2), (x + lean, tip), (x + lean * 0.6 - wdt * 0.15, (base_y + tip) / 2)]
        d.polygon(pts, fill=255)
        # small filler tufts between blades
        if r.random() < 0.6:
            d.polygon([(x + wdt, base_y), (x + wdt * 1.6, base_y), (x + wdt * 1.3 + lean * 0.2, H * (0.36 + 0.12 * r.random()))], fill=255)
        x += wdt * (0.9 + 0.8 * r.random())

def scallops(d, r):
    d.rectangle([0, 0, W * 3, int(H * 0.28)], fill=255)
    x = 0.0
    while x < W * 3:
        rad = 22 + 30 * r.random()
        cy = H * 0.28 + rad * 0.9 * (0.55 + 0.45 * r.random())
        d.ellipse([x - rad, cy - rad * 0.9, x + rad, cy + rad * 0.9], fill=255)
        # a second, smaller lobe overlapping
        if r.random() < 0.7:
            rad2 = rad * 0.6
            cy2 = cy + rad * 0.6
            d.ellipse([x + rad * 0.4 - rad2, cy2 - rad2, x + rad * 0.4 + rad2, cy2 + rad2], fill=255)
        x += rad * (1.2 + 0.6 * r.random())

def chips(d, r):
    d.rectangle([0, 0, W * 3, int(H * 0.30)], fill=255)
    x = 0.0
    while x < W * 3:
        n = 4 + r.integers(3)
        rad = 18 + 26 * r.random()
        cx = x; cy = H * 0.30 + rad * 0.5 * (0.6 + 0.5 * r.random())
        pts = []
        for k in range(n):
            ang = 2 * np.pi * k / n + (r.random() - 0.5) * 0.6
            rr = rad * (0.7 + 0.5 * r.random())
            pts.append((cx + np.cos(ang) * rr, cy + np.sin(ang) * rr * 0.8))
        d.polygon(pts, fill=255)
        # detached chip a bit further out
        if r.random() < 0.5:
            sx = cx + (r.random() - 0.5) * rad * 2; sy = cy + rad * (1.1 + 0.5 * r.random())
            sr = 4 + 7 * r.random()
            d.polygon([(sx - sr, sy), (sx, sy - sr * 0.8), (sx + sr, sy), (sx, sy + sr * 0.8)], fill=255)
        x += rad * (1.3 + 0.8 * r.random())

def chunks(d, r):
    d.rectangle([0, 0, W * 3, int(H * 0.34)], fill=255)
    x = 0.0
    while x < W * 3:
        n = 5 + r.integers(3)
        rad = 40 + 55 * r.random()
        cx = x; cy = H * 0.34 + rad * 0.5 * (0.5 + 0.6 * r.random())
        pts = []
        for k in range(n):
            ang = 2 * np.pi * k / n + (r.random() - 0.5) * 0.5
            rr = rad * (0.7 + 0.5 * r.random())
            pts.append((cx + np.cos(ang) * rr, cy + np.sin(ang) * rr * 0.7))
        d.polygon(pts, fill=255)
        x += rad * (1.1 + 0.6 * r.random())

def cobbles(d, r):
    """rounded stones jutting out of the paved edge, with a few loose ones"""
    d.rectangle([0, 0, W * 3, int(H * 0.26)], fill=255)
    x = 0.0
    while x < W * 3:
        rw = 20 + 22 * r.random(); rh = rw * (0.7 + 0.4 * r.random())
        cy = H * 0.26 + rh * (0.3 + 0.5 * r.random())
        d.rounded_rectangle([x - rw, cy - rh, x + rw, cy + rh], radius=rw * 0.55, fill=255)
        if r.random() < 0.45:
            sx = x + (r.random() - 0.5) * rw; sy = cy + rh * (1.6 + 0.9 * r.random()); sr = 6 + 9 * r.random()
            d.ellipse([sx - sr, sy - sr * 0.8, sx + sr, sy + sr * 0.8], fill=255)
        x += rw * (1.7 + 0.6 * r.random())

def bands(cov, rim_w=7, ink_w=5, blur=1.2):
    im = Image.fromarray((cov * 255).astype(np.uint8))
    eroded = np.array(im.filter(ImageFilter.MinFilter(rim_w * 2 + 1))) / 255.0
    dilated = np.array(im.filter(ImageFilter.MaxFilter(ink_w * 2 + 1))) / 255.0
    rim = np.clip(cov - eroded, 0, 1)
    ink = np.clip(dilated - cov, 0, 1)
    # break the highlight up so it reads as brushed, not ruled
    yy, xx = np.mgrid[0:H, 0:W]
    breakup = 0.55 + 0.45 * np.sin(xx * 0.11) * np.sin(xx * 0.037 + yy * 0.05)
    rim = rim * np.clip(breakup, 0, 1)
    def soft(a): return np.array(Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(blur))) / 255.0
    return soft(cov), soft(rim), soft(ink)

rows = [draw_row(blades, 1), draw_row(scallops, 2), draw_row(chips, 3), draw_row(chunks, 4)]
out = np.zeros((H * len(rows), W, 3), np.float32)
for i, cov in enumerate(rows):
    # guarantee: v=0 side fully inside, v=1 side fully outside, so the interior never leaks
    cov[:8, :] = 1.0; cov[-8:, :] = 0.0
    c, g, b = bands(cov)
    out[i * H:(i + 1) * H, :, 0] = c
    out[i * H:(i + 1) * H, :, 1] = g
    out[i * H:(i + 1) * H, :, 2] = b
import os
outdir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Textures", "Edges")
os.makedirs(outdir, exist_ok=True)
Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(outdir, "T_EdgeFringe.png"))

# preview: grass green inside over brown dirt, with rim and ink applied like the shader does
prev = np.zeros((H * len(rows), W, 3), np.float32)
inside = np.array([0.40, 0.46, 0.22]); outside = np.array([0.36, 0.28, 0.20])
for i in range(len(rows)):
    c = out[i * H:(i + 1) * H, :, 0:1]; g = out[i * H:(i + 1) * H, :, 1:2]; b = out[i * H:(i + 1) * H, :, 2:3]
    col = outside * (1 - c) + inside * c
    col = col * (1 - g * 0.55) + (col * 1.6 * np.array([0.95, 0.92, 0.80]) + 0.25 * np.array([0.95, 0.92, 0.80])) * g * 0.55
    col = col * (1 - b * 0.75) + col * np.array([0.12, 0.10, 0.12]) * b * 0.75
    prev[i * H:(i + 1) * H] = col
Image.fromarray((np.clip(prev, 0, 1) * 255).astype(np.uint8)).resize((1024, 1024)).save("/tmp/claude-0/-home-claude/b42a96a5-9e2e-5318-85b9-05a593e1ad9f/scratchpad/fringe_preview.png")
print("ok")
