"""T_PaperGrain.png (tileable, linear: R = fine grain, G = mid fibre) and T_GrassTuft.png (RGBA card)."""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

def spec(size, falloff, low_cut, seed, aniso=1.0, angle=0.0):
    r = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size)[:, None] * size
    fx = np.fft.fftfreq(size)[None, :] * size
    u = fx * np.cos(angle) + fy * np.sin(angle); v = -fx * np.sin(angle) + fy * np.cos(angle)
    f = np.sqrt((u * aniso) ** 2 + v ** 2)
    amp = np.where(f < low_cut, 0.0, 1.0 / np.maximum(f, 1.0) ** falloff)
    n = np.real(np.fft.ifft2(amp * np.exp(2j * np.pi * r.random((size, size)))))
    return (n - n.min()) / (n.max() - n.min())

def center(a):
    return np.clip(a - a.mean() + 0.5, 0, 1)

# ---- paper grain: fine near-white noise + longer fibres, low contrast, mean 0.5
S = 512
fine = spec(S, 0.35, 8, 1)                                   # nearly white noise
fibre = spec(S, 0.9, 6, 2, aniso=0.15, angle=0.4) * 0.5 + spec(S, 0.9, 6, 3, aniso=0.15, angle=-0.9) * 0.5
R = center(fine * 0.75 + fibre * 0.25)
G = center(spec(S, 1.1, 3, 4))                               # mid-frequency for the ragged vignette
Image.fromarray(np.dstack([(R * 255).astype(np.uint8), (G * 255).astype(np.uint8), np.full((S, S), 128, np.uint8)])).save("T_PaperGrain.png")

# ---- grass tuft: a fan of tapered blades with ink outline, pivot bottom-centre
W, H = 512, 512
rng = np.random.default_rng(9)
def blade_layer(count, col_range, width, seed, alpha_scale=1.0):
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = np.random.default_rng(seed)
    for i in range(count):
        x0 = W / 2 + (r.random() - 0.5) * W * 0.55
        y0 = H - 6
        lean = (x0 - W / 2) / (W / 2) * 0.9 + (r.random() - 0.5) * 0.8
        length = H * (0.45 + 0.5 * r.random())
        pts = []
        for k, t in enumerate(np.linspace(0, 1, 14)):
            bend = lean * t * t * length * 0.6
            px = x0 + bend + np.sin(t * 3 + i) * 4
            py = y0 - t * length
            pts.append((px, py))
        c = col_range[0] + (col_range[1] - col_range[0]) * r.random()
        col = (int(c[0] * 255), int(c[1] * 255), int(c[2] * 255), int(255 * alpha_scale))
        for k in range(len(pts) - 1):
            w = max(1, int(width * (1 - k / len(pts)) ** 0.8 * (0.7 + 0.6 * r.random())))
            d.line([pts[k], pts[k + 1]], fill=col, width=w)
    return im

dark = np.array([[0.16, 0.24, 0.10], [0.24, 0.32, 0.12]])
mid  = np.array([[0.30, 0.42, 0.16], [0.44, 0.52, 0.20]])
lit  = np.array([[0.50, 0.58, 0.24], [0.66, 0.68, 0.30]])
back = blade_layer(28, dark, 16, 1)
midl = blade_layer(24, mid, 13, 2)
front = blade_layer(16, lit, 10, 3)
tuft = Image.alpha_composite(Image.alpha_composite(back, midl), front)
# ink outline: dilate alpha, dark fill, under the tuft
a = np.array(tuft)[..., 3]
outline = Image.fromarray(a).filter(ImageFilter.MaxFilter(7))
ink = Image.new("RGBA", (W, H), (28, 24, 22, 0)); ink.putalpha(outline)
tuft = Image.alpha_composite(ink, tuft)
# darken toward the root a touch, and soften edges slightly
arr = np.array(tuft).astype(np.float32)
yy = np.linspace(1, 0, H)[:, None]
arr[..., :3] *= (0.75 + 0.25 * yy)[..., None]
tuft = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))
tuft.save("T_GrassTuft.png")
bg = Image.new("RGBA", (W, H), (110, 110, 110, 255)); bg.alpha_composite(tuft)
bg.convert("RGB").resize((256, 256)).save("/tmp/claude-0/-home-claude/b42a96a5-9e2e-5318-85b9-05a593e1ad9f/scratchpad/tuft.png")
print("ok")
