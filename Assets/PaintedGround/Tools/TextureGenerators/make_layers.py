"""Placeholder painted layer textures: dirt, grass, forest, stone, cliff.
Tileable, brushy, no landmark features. Meant to be replaced by real paintings."""
import numpy as np
from PIL import Image, ImageFilter

S = 1024

def spec(size, falloff, low_cut, seed, aniso=1.0, angle=0.0):
    r = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size)[:, None] * size
    fx = np.fft.fftfreq(size)[None, :] * size
    u = fx * np.cos(angle) + fy * np.sin(angle)
    v = -fx * np.sin(angle) + fy * np.cos(angle)
    f = np.sqrt((u * aniso) ** 2 + v ** 2)
    amp = np.where(f < low_cut, 0.0, 1.0 / np.maximum(f, 1.0) ** falloff)
    n = np.real(np.fft.ifft2(amp * np.exp(2j * np.pi * r.random((size, size)))))
    return (n - n.min()) / (n.max() - n.min())

def brushy(seed, coarse=1.6, fine=1.1, streak_angle=0.3, streak_aniso=0.2):
    a = spec(S, coarse, 2, seed)
    b = spec(S, fine, 6, seed + 1)
    s = spec(S, 1.3, 3, seed + 2, aniso=streak_aniso, angle=streak_angle)
    return 0.45 * a + 0.30 * b + 0.25 * s

def strokes(seed, count, length, width, angle_jitter, base_angle):
    """Cheap dry-brush marks: random short segments rasterised, tileable via modulo."""
    r = np.random.default_rng(seed)
    img = np.zeros((S, S), np.float32)
    yy, xx = np.mgrid[0:S, 0:S]
    for _ in range(count):
        cx, cy = r.random(2) * S
        ang = base_angle + (r.random() - 0.5) * angle_jitter
        L = length * (0.5 + r.random())
        W = width * (0.6 + 0.8 * r.random())
        dx, dy = np.cos(ang), np.sin(ang)
        # distance to segment, on a torus
        px = (xx - cx + S / 2) % S - S / 2
        py = (yy - cy + S / 2) % S - S / 2
        t = np.clip(px * dx + py * dy, -L / 2, L / 2)
        d = np.hypot(px - t * dx, py - t * dy)
        img += np.clip(1 - d / W, 0, 1) * (0.5 + r.random())
    if img.max() > 0: img /= img.max()
    return img

def paint(base_rgb, dark_rgb, light_rgb, value, hue_noise, hue_rgb, grain=0.06, seed=0):
    base = np.array(base_rgb)[None, None, :]
    dark = np.array(dark_rgb)[None, None, :]
    light = np.array(light_rgb)[None, None, :]
    v = value[..., None]
    col = np.where(v < 0.5, base + (dark - base) * (0.5 - v) * 2, base + (light - base) * (v - 0.5) * 2)
    col = col + (np.array(hue_rgb)[None, None, :] - base) * hue_noise[..., None] * 0.35
    g = np.random.default_rng(seed).random((S, S, 1)) - 0.5
    col = col + g * grain
    return (np.clip(col, 0, 1) * 255 + 0.5).astype(np.uint8)

def save(name, arr):
    Image.fromarray(arr).save(name)
    print(name)

# Dirt: warm brown, soft mottling, faint horizontal-ish strokes
v = brushy(10, streak_angle=0.15)
v = 0.5 + (v - 0.5) * 0.9 + (strokes(11, 500, 90, 3, 0.6, 0.1) - 0.3) * 0.18
save("../../Textures/Layers/T_Dirt.png", paint((0.36, 0.28, 0.20), (0.22, 0.16, 0.12), (0.52, 0.42, 0.30), v, spec(S, 1.8, 1, 12), (0.42, 0.30, 0.16), seed=1))

# Grass: mossy green, lots of short upward strokes (hatching), cooler in the shadows
v = brushy(20, streak_angle=1.4, streak_aniso=0.15)
v = 0.5 + (v - 0.5) * 0.8 + (strokes(21, 2200, 26, 1.6, 0.5, 1.45) - 0.35) * 0.30
save("../../Textures/Layers/T_Grass.png", paint((0.40, 0.46, 0.22), (0.20, 0.27, 0.14), (0.62, 0.66, 0.34), v, spec(S, 1.8, 1, 22), (0.34, 0.40, 0.10), seed=2))

# Forest floor: dark cool green-brown, leaf litter blobs
v = brushy(30, coarse=1.4)
v = 0.5 + (v - 0.5) * 0.9 + (strokes(31, 900, 14, 5, 3.0, 0) - 0.3) * 0.22
save("../../Textures/Layers/T_Forest.png", paint((0.22, 0.24, 0.16), (0.10, 0.11, 0.09), (0.36, 0.38, 0.24), v, spec(S, 1.8, 1, 32), (0.30, 0.22, 0.12), seed=3))

# Stone: grey-blue, flat patches with ink cracks
v = brushy(40, coarse=1.9, fine=0.9)
patches = spec(S, 2.4, 1, 41)
v = 0.5 + (np.round(patches * 5) / 5 - 0.5) * 0.35 + (v - 0.5) * 0.35
cracks = strokes(42, 260, 160, 1.2, 1.0, 0.7)
v = v - cracks * 0.35
save("../../Textures/Layers/T_Stone.png", paint((0.44, 0.45, 0.46), (0.24, 0.25, 0.28), (0.62, 0.62, 0.60), v, spec(S, 1.8, 1, 43), (0.40, 0.42, 0.50), seed=4))

# Cliff: sampled on vertical faces, so strong vertical streaks + horizontal strata
v = brushy(50, streak_angle=np.pi / 2, streak_aniso=0.08)
v = 0.5 + (v - 0.5) * 0.7 + (strokes(51, 700, 220, 2.5, 0.25, np.pi / 2) - 0.3) * 0.3
strata = spec(S, 1.2, 4, 52, aniso=0.05, angle=0.0)
v = v + (strata - 0.5) * 0.25
save("../../Textures/Layers/T_Cliff.png", paint((0.33, 0.29, 0.27), (0.14, 0.12, 0.13), (0.50, 0.46, 0.42), v, spec(S, 1.8, 1, 53), (0.36, 0.26, 0.20), seed=5))

