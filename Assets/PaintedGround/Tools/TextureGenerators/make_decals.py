"""Painted decal atlas for Painted/Decal. 4 x 2 cells of 512 px, RGBA.
Tiles (row-major from top-left):
  0 crack     1 crack2    2 stain     3 scorch      (multiply)
  4 puddle    5 leaves    6 pebbles   7 moss        (normal)
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

C = 512
rng = np.random.default_rng(3)
yy, xx = np.mgrid[0:C, 0:C]
cx = (xx - C / 2) / (C / 2)
cy = (yy - C / 2) / (C / 2)
rr = np.hypot(cx, cy)

def noise(size, falloff, low_cut, seed):
    r = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size)[:, None] * size
    fx = np.fft.fftfreq(size)[None, :] * size
    f = np.hypot(fx, fy)
    amp = np.where(f < low_cut, 0.0, 1.0 / np.maximum(f, 1.0) ** falloff)
    n = np.real(np.fft.ifft2(amp * np.exp(2j * np.pi * r.random((size, size)))))
    return (n - n.min()) / (n.max() - n.min())

def blob(seed, radius=0.7, rag=0.25, soft=0.08):
    """ragged filled disc, 0..1"""
    n = noise(C, 1.4, 2, seed)
    edge = radius + (n - 0.5) * 2 * rag
    return np.clip((edge - rr) / soft + 0.5, 0, 1)

def ink(mask, width=3.0, strength=0.9):
    """dark rim along a mask's edge"""
    im = Image.fromarray((mask * 255).astype(np.uint8))
    inner = np.array(im.filter(ImageFilter.MinFilter(int(width) * 2 + 1))) / 255.0
    return np.clip(mask - inner, 0, 1) * strength

def wobbly_line(draw, pts, width, fill, jitter=4.0, seed=0):
    r = np.random.default_rng(seed)
    out = []
    for i in range(len(pts) - 1):
        a, b = np.array(pts[i]), np.array(pts[i + 1])
        for t in np.linspace(0, 1, 12, endpoint=False):
            p = a + (b - a) * t + (r.random(2) - 0.5) * jitter
            out.append(tuple(p))
    out.append(tuple(pts[-1]))
    w = width
    for i in range(len(out) - 1):
        ww = max(1, int(w * (0.6 + 0.8 * r.random())))
        draw.line([out[i], out[i + 1]], fill=fill, width=ww)

def branch(draw, start, ang, length, width, depth, seed):
    r = np.random.default_rng(seed)
    if depth == 0 or length < 8:
        return
    pts = [start]
    p = np.array(start, float)
    a = ang
    for _ in range(6):
        a += (r.random() - 0.5) * 0.9
        p = p + np.array([np.cos(a), np.sin(a)]) * length / 6
        pts.append(tuple(p))
    wobbly_line(draw, pts, width, 255, jitter=2, seed=seed)
    for k in range(2):
        if r.random() < 0.8:
            i = r.integers(1, len(pts))
            branch(draw, pts[i], a + (r.random() - 0.5) * 2.2, length * 0.55, max(1, width * 0.6), depth - 1, seed + 17 + k)

def crack(seed):
    im = Image.new("L", (C, C), 0)
    d = ImageDraw.Draw(im)
    r = np.random.default_rng(seed)
    for k in range(3):
        a0 = r.random() * 2 * np.pi
        branch(d, (C / 2 + (r.random() - 0.5) * 60, C / 2 + (r.random() - 0.5) * 60), a0, 150 + r.random() * 80, 6, 4, seed * 10 + k)
    m = np.array(im.filter(ImageFilter.GaussianBlur(0.8))) / 255.0
    m *= np.clip(1.4 - rr, 0, 1)                      # fade toward the cell edge
    m *= 0.7 + 0.3 * noise(C, 1.0, 8, seed + 99)      # dry-brush breakup
    rgb = np.dstack([np.full_like(m, 0.18), np.full_like(m, 0.14), np.full_like(m, 0.14)])
    return rgb, m * 0.9

def stain(seed):
    m = blob(seed, 0.55, 0.3, 0.2) * (0.5 + 0.5 * noise(C, 1.6, 2, seed + 1))
    rim = ink(blob(seed, 0.55, 0.3, 0.05), 6, 0.5)
    a = np.clip(m * 0.6 + rim, 0, 1)
    rgb = np.dstack([np.full_like(m, 0.30), np.full_like(m, 0.22), np.full_like(m, 0.16)])
    return rgb, a

def scorch(seed):
    core = blob(seed, 0.45, 0.2, 0.25)
    halo = blob(seed + 1, 0.7, 0.35, 0.3) * 0.5
    a = np.clip(core + halo, 0, 1) * (0.7 + 0.3 * noise(C, 1.2, 6, seed + 2))
    v = 0.05 + 0.15 * (1 - core)
    rgb = np.dstack([v, v * 0.9, v * 0.85])
    return rgb, a

def puddle(seed):
    m = blob(seed, 0.6, 0.25, 0.03)
    rim = ink(m, 3, 0.6)
    highlight = blob(seed + 5, 0.25, 0.3, 0.1)
    highlight = np.roll(np.roll(highlight, -60, 0), -40, 1) * m
    base = np.dstack([np.full_like(m, 0.28), np.full_like(m, 0.36), np.full_like(m, 0.40)])
    base = base * (1 - highlight[..., None] * 0.6) + highlight[..., None] * 0.6 * np.array([0.75, 0.82, 0.85])
    base = base * (1 - rim[..., None]) + rim[..., None] * np.array([0.90, 0.94, 0.94])   # pale wet rim
    a = np.clip(m * 0.85 + rim * 0.15, 0, 1)
    return base, a

def scatter(seed, count, rx, ry, colors, outline=True, shape="ellipse"):
    im = Image.new("RGBA", (C, C), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = np.random.default_rng(seed)
    for _ in range(count):
        # cluster toward the centre
        while True:
            p = r.normal(0, 0.28, 2)
            if np.hypot(*p) < 0.85: break
        x, y = (p + 1) * C / 2
        w, h = rx * (0.6 + 0.8 * r.random()), ry * (0.6 + 0.8 * r.random())
        ang = r.random() * 180
        col = colors[r.integers(len(colors))]
        col = tuple(int(np.clip(c * 255 * (0.85 + 0.3 * r.random()), 0, 255)) for c in col) + (255,)
        leaf = Image.new("RGBA", (int(w * 2 + 8), int(h * 2 + 8)), (0, 0, 0, 0))
        ld = ImageDraw.Draw(leaf)
        box = [4, 4, 4 + w * 2, 4 + h * 2]
        if outline:
            ld.ellipse(box, fill=(30, 22, 20, 255))
            ld.ellipse([box[0] + 2, box[1] + 2, box[2] - 2, box[3] - 2], fill=col)
        else:
            ld.ellipse(box, fill=col)
        leaf = leaf.rotate(ang, expand=True, resample=Image.BILINEAR)
        im.alpha_composite(leaf, (int(x - leaf.width / 2), int(y - leaf.height / 2)))
    arr = np.array(im) / 255.0
    return arr[..., :3], arr[..., 3]

def leaves(seed):
    return scatter(seed, 42, 16, 8, [(0.55, 0.36, 0.16), (0.62, 0.45, 0.18), (0.42, 0.30, 0.14), (0.58, 0.28, 0.14)])

def pebbles(seed):
    rgb, a = scatter(seed, 22, 18, 13, [(0.42, 0.41, 0.42), (0.50, 0.47, 0.44), (0.33, 0.34, 0.37)])
    return rgb, a

def moss(seed):
    m = blob(seed, 0.5, 0.35, 0.12)
    m = m * (0.55 + 0.45 * noise(C, 1.1, 6, seed + 3))
    a = np.clip(m, 0, 1) ** 0.8
    n = noise(C, 1.5, 3, seed + 4)
    rgb = np.dstack([0.24 + 0.14 * n, 0.36 + 0.16 * n, 0.14 + 0.08 * n])
    return rgb, a

tiles = [crack(1), crack(2), stain(3), scorch(4), puddle(5), leaves(6), pebbles(7), moss(8)]
atlas = np.zeros((C * 2, C * 4, 4))
for i, (rgb, a) in enumerate(tiles):
    r, c = divmod(i, 4)
    # tiny margin so bilinear filtering never bleeds a neighbour in
    a = a * np.clip((0.98 - np.maximum(abs(cx), abs(cy))) / 0.03, 0, 1)
    atlas[r * C:(r + 1) * C, c * C:(c + 1) * C, :3] = rgb
    atlas[r * C:(r + 1) * C, c * C:(c + 1) * C, 3] = a
Image.fromarray((np.clip(atlas, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save("T_DecalAtlas.png")
# preview over mid-grey
bg = np.full_like(atlas[..., :3], 0.45)
prev = bg * (1 - atlas[..., 3:]) + atlas[..., :3] * atlas[..., 3:]
Image.fromarray((prev * 255).astype(np.uint8)).resize((1024, 512)).save("/tmp/claude-0/-home-claude/b42a96a5-9e2e-5318-85b9-05a593e1ad9f/scratchpad/atlas_preview.png")
print("ok")
