"""Makes a generated picture a tileable detail texture for the ground shader.

  tile.py in.png out.png [--keep-hue 0..1] [--size 1024] [--contrast 1.0]

1. Flattens large light and dark patches (they are what makes a repeat visible).
2. Makes it wrap: blends with a half-shifted copy along x, then along y, keeping the contrast.
3. Sets the mean to linear 0.25 per channel (the shader multiplies by 4 and by the cell colour),
   keeping `keep-hue` of the picture's own tint.
"""
import sys
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

args = sys.argv[1:]
src, dst = args[0], args[1]
keep_hue = float(args[args.index("--keep-hue") + 1]) if "--keep-hue" in args else 0.0
size = int(args[args.index("--size") + 1]) if "--size" in args else 1024
contrast = float(args[args.index("--contrast") + 1]) if "--contrast" in args else 1.0
MEAN = 0.25


def to_linear(s):
    return np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4)


def to_srgb(l):
    l = np.clip(l, 0.0, 1.0)
    return np.where(l <= 0.0031308, l * 12.92, 1.055 * l ** (1 / 2.4) - 0.055)


img = Image.open(src).convert("RGB")
if img.size != (size, size):
    img = img.resize((size, size), Image.LANCZOS)
lin = to_linear(np.asarray(img, dtype=np.float64) / 255.0)

# 1. Flatten: divide by the picture's own very blurred brightness.
lum = lin.mean(axis=2)
low = gaussian_filter(lum, sigma=size / 9, mode="wrap")
lin = lin * (low.mean() / np.maximum(low, 1e-4))[..., None]


# 2. Wrap. The mask is 1 in the middle and 0 at the two borders of the axis; its edge is
# wobbled by smooth noise so the join is not a straight line.
def wrap_axis(a, axis, seed):
    n = a.shape[axis]
    rng = np.random.default_rng(seed)
    pos = np.arange(n) / n
    border = np.minimum(pos, 1 - pos)                       # 0 at the borders, 0.5 in the middle
    wobble = gaussian_filter(rng.standard_normal(a.shape[:2]), sigma=size / 40, mode="wrap")
    wobble = wobble / (np.abs(wobble).max() + 1e-9) * 0.05
    b = border[:, None] if axis == 0 else border[None, :]
    t = np.clip((b + wobble - 0.05) / 0.12, 0.0, 1.0)
    w = (t * t * (3 - 2 * t))[..., None]
    # The outermost texels must be the shifted copy only, or the wrap is not exact.
    edge = (border < 1.5 / n)
    w = w * (~edge[:, None] if axis == 0 else ~edge[None, :])[..., None]
    shifted = np.roll(a, n // 2, axis=axis)
    mean = a.mean(axis=(0, 1), keepdims=True)
    mixed = (a - mean) * w + (shifted - mean) * (1 - w)
    return mean + mixed / np.sqrt(w * w + (1 - w) * (1 - w))


lin = wrap_axis(lin, 1, 1)
lin = wrap_axis(lin, 0, 2)

# 3. Mean and hue. Contrast is changed around the mean, in ratios: dark cracks stay cracks.
if contrast != 1.0:
    m = lin.mean()
    lin = m * (np.maximum(lin, 1e-5) / m) ** contrast
mean = lin.mean(axis=(0, 1))
grey = mean.mean()
target = MEAN * ((mean / grey) ** keep_hue)
lin = lin * (target / mean)

out = (to_srgb(lin) * 255.0 + 0.5).astype(np.uint8)
Image.fromarray(out).save(dst, optimize=True)
clipped = float((lin > 1.0).mean())
print(f"{dst}: mean linear {lin.mean(axis=(0, 1)).round(3)}, clipped {clipped:.4f}")
