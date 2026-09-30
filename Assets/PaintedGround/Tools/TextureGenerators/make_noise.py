"""Generates the two tileable helper textures for Painted/Ground.
Tileable by construction: noise is built in the frequency domain, so wrapping is exact."""
import numpy as np
from PIL import Image

rng = np.random.default_rng(7)

def spectral_noise(size, falloff, low_cut=1.0, seed=None):
    r = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size)[:, None] * size
    fx = np.fft.fftfreq(size)[None, :] * size
    f = np.sqrt(fx * fx + fy * fy)
    amp = np.where(f < low_cut, 0.0, 1.0 / np.maximum(f, 1.0) ** falloff)
    phase = np.exp(2j * np.pi * r.random((size, size)))
    n = np.real(np.fft.ifft2(amp * phase))
    n = (n - n.min()) / (n.max() - n.min())
    return n

def to_u8(a):
    return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)

# --- Macro: R = very soft large-scale value variation, G = patch mask for second sample,
#            B = mid-scale spare channel (use for dirt darkening etc.)
S = 512
macroR = spectral_noise(S, 2.2, low_cut=1.0, seed=1)
macroG = spectral_noise(S, 1.6, low_cut=1.5, seed=2)
macroB = spectral_noise(S, 1.8, low_cut=3.0, seed=3)
# keep mean at 0.5 so the neutral tint really is neutral
def center(a):
    a = a - a.mean() + 0.5
    return np.clip(a, 0, 1)
Image.fromarray(np.dstack([to_u8(center(macroR)), to_u8(center(macroG)), to_u8(center(macroB))])).save("PaintedGround_Macro.png")

# --- Brush: three channels of ragged, brushy, mid/high-frequency noise for blend edges.
# Mix a 1/f^1.1 base with a directional streak layer so it reads like dry-brush strokes.
def brush_channel(seed):
    base = spectral_noise(S, 1.15, low_cut=2.0, seed=seed)
    # streaks: anisotropic spectrum stretched along one axis
    r = np.random.default_rng(seed + 100)
    fy = np.fft.fftfreq(S)[:, None] * S
    fx = np.fft.fftfreq(S)[None, :] * S
    ang = r.random() * np.pi
    u = fx * np.cos(ang) + fy * np.sin(ang)
    v = -fx * np.sin(ang) + fy * np.cos(ang)
    f = np.sqrt((u * 0.25) ** 2 + v ** 2)
    amp = np.where(f < 2.0, 0.0, 1.0 / np.maximum(f, 1.0) ** 1.3)
    streak = np.real(np.fft.ifft2(amp * np.exp(2j * np.pi * r.random((S, S)))))
    streak = (streak - streak.min()) / (streak.max() - streak.min())
    m = 0.65 * base + 0.35 * streak
    return center(m)
Image.fromarray(np.dstack([to_u8(brush_channel(s)) for s in (11, 12, 13)])).save("PaintedGround_Brush.png")

# --- tiling proof: 2x2 tile of each, for eyeballing seams
for name in ("PaintedGround_Macro", "PaintedGround_Brush"):
    im = np.array(Image.open(name + ".png"))
    Image.fromarray(np.tile(im, (2, 2, 1))).save(f"_check_{name}_2x2.png")
print("done")
