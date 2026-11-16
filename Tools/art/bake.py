"""Turns a generated GLB (hundreds of thousands of triangles, 2K texture) into a game prop.

  bake.py in.glb out_dir name [--faces 2000] [--tex 512] [--yaw deg] [--cut height]

Writes out_dir/name.obj (footprint centred on the origin, bottom at y = 0, the larger of
width and depth = 1) and out_dir/name_tex.png (colour re-baked onto a fresh unwrap of the
simplified mesh), plus a preview picture next to this script.
"""
import sys
import numpy as np
import trimesh
import pymeshlab
import xatlas
from PIL import Image
from scipy.spatial import cKDTree
from scipy.ndimage import distance_transform_edt

args = sys.argv[1:]
src, out_dir, name = args[0], args[1], args[2]


def opt(flag, default):
    return type(default)(args[args.index(flag) + 1]) if flag in args else default


FACES, TEX, YAW, ALPHA = opt("--faces", 2000), opt("--tex", 512), opt("--yaw", 0.0), opt("--alpha", 1.0)
CUT = opt("--cut", 0.0)
SAMPLES = 1_500_000

scene = trimesh.load(src)
high = scene.to_geometry() if hasattr(scene, "to_geometry") else scene.dump(concatenate=True)
material = high.visual.material
texture = np.asarray(material.baseColorTexture.convert("RGB"), dtype=np.float64) / 255.0

# Unit footprint, standing on y = 0. glTF is y-up already.
v = high.vertices.copy()
if YAW:
    a = np.radians(YAW)
    rot = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
    v = v @ rot.T
lo, hi = v.min(axis=0), v.max(axis=0)
scale = 1.0 / max(hi[0] - lo[0], hi[2] - lo[2])
v = (v - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * scale
high.vertices = v

# Some models stand on a square slab, which next to one another reads as paving. Everything
# below the cut is dropped and what is left becomes the unit footprint.
if CUT > 0:
    v[:, 1] -= CUT
    high.vertices = v
    high.update_faces((v[high.faces][:, :, 1] > 0).any(axis=1))
    high.remove_unreferenced_vertices()
    v = high.vertices.copy()
    v[:, 1] = np.maximum(v[:, 1], 0)
    lo, hi = v.min(axis=0), v.max(axis=0)
    v = (v - [(lo[0] + hi[0]) / 2, 0, (lo[2] + hi[2]) / 2]) / max(hi[0] - lo[0], hi[2] - lo[2])
    high.vertices = v


# Colour of the surface: many random points of the detailed mesh with the texture's colour.
def bilinear(tex, uv):
    h, w, _ = tex.shape
    x = (uv[:, 0] % 1.0) * w - 0.5
    y = (1.0 - (uv[:, 1] % 1.0)) * h - 0.5
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    x0, x1, y0, y1 = x0 % w, (x0 + 1) % w, y0 % h, (y0 + 1) % h
    return (tex[y0, x0] * (1 - fx) + tex[y0, x1] * fx) * (1 - fy) + (tex[y1, x0] * (1 - fx) + tex[y1, x1] * fx) * fy


rng = np.random.default_rng(7)
tri = high.vertices[high.faces]
areas = high.area_faces
face = rng.choice(len(areas), size=SAMPLES, p=areas / areas.sum())
r1, r2 = np.sqrt(rng.random(SAMPLES)), rng.random(SAMPLES)
bary = np.stack([1 - r1, r1 * (1 - r2), r1 * r2], axis=1)
points = np.einsum("ij,ijk->ik", bary, tri[face])
uvs = np.einsum("ij,ijk->ik", bary, high.visual.uv[high.faces[face]])
colors = bilinear(texture, uvs)
normals_high = high.face_normals[face]

# The generated mesh is many loose sheets with junk inside. Wrap its outside into one closed
# shell, then simplify the shell; the texture is baked again afterwards.
ms = pymeshlab.MeshSet()
ms.add_mesh(pymeshlab.Mesh(high.vertices, high.faces))
ms.meshing_merge_close_vertices()
ms.generate_alpha_wrap(alpha=pymeshlab.PercentageValue(ALPHA), offset=pymeshlab.PercentageValue(0.15))
shell = trimesh.Trimesh(ms.current_mesh().vertex_matrix(), ms.current_mesh().face_matrix(), process=False)
ms.meshing_decimation_quadric_edge_collapse(targetfacenum=FACES, qualitythr=0.5, preservenormal=True,
                                            preservetopology=True, optimalplacement=True, planarquadric=True)
ms.meshing_remove_unreferenced_vertices()
low_v, low_f = ms.current_mesh().vertex_matrix(), ms.current_mesh().face_matrix()

# Only what lies on the outside gives colour: points near the shell.
near, _ = cKDTree(shell.sample(400_000)).query(points)
keep = near < 0.02
points, colors, normals_high = points[keep], colors[keep], normals_high[keep]
tree = cKDTree(points)

# Fresh unwrap.
atlas = xatlas.Atlas()
atlas.add_mesh(low_v, low_f)
pack = xatlas.PackOptions()
pack.resolution = TEX
pack.padding = 3
pack.bilinear = True
atlas.generate(pack_options=pack)
vmap, faces, uv = atlas[0]
verts = low_v[vmap]

# Bake: every texel of the new atlas takes the colour of the nearest points of the detailed surface.
image = np.zeros((TEX, TEX, 3))
filled = np.zeros((TEX, TEX), dtype=bool)
px = uv * [TEX, TEX]
for f in faces:
    p, q = px[f], verts[f]
    x0, x1 = int(np.floor(p[:, 0].min())) - 1, int(np.ceil(p[:, 0].max())) + 1
    y0, y1 = int(np.floor(p[:, 1].min())) - 1, int(np.ceil(p[:, 1].max())) + 1
    x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, TEX - 1), min(y1, TEX - 1)
    if x1 < x0 or y1 < y0:
        continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    d = (p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1])
    if abs(d) < 1e-12:
        continue
    a = ((p[1, 1] - p[2, 1]) * (gx - p[2, 0]) + (p[2, 0] - p[1, 0]) * (gy - p[2, 1])) / d
    b = ((p[2, 1] - p[0, 1]) * (gx - p[2, 0]) + (p[0, 0] - p[2, 0]) * (gy - p[2, 1])) / d
    c = 1 - a - b
    eps = -0.6 / max(np.sqrt(abs(d)), 1.0)          # a little over the edge, so no texel is missed
    inside = (a >= eps) & (b >= eps) & (c >= eps)
    if not inside.any():
        continue
    a, b, c = np.clip(a[inside], 0, 1), np.clip(b[inside], 0, 1), np.clip(c[inside], 0, 1)
    s = a + b + c
    world = (a[:, None] * q[0] + b[:, None] * q[1] + c[:, None] * q[2]) / s[:, None]
    _, idx = tree.query(world, k=4)
    ys = (TEX - 1 - (gy[inside] - 0.5)).astype(int)   # image rows run down, v runs up
    xs = (gx[inside] - 0.5).astype(int)
    image[ys, xs] = colors[idx].mean(axis=1)
    filled[ys, xs] = True

# Spread the colour past the chart borders, so filtering never pulls in black.
nearest = distance_transform_edt(~filled, return_distances=False, return_indices=True)
image = image[nearest[0], nearest[1]]

import os
os.makedirs(out_dir, exist_ok=True)
Image.fromarray((np.clip(image, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(out_dir, name + "_tex.png"), optimize=True)
with open(os.path.join(out_dir, name + ".obj"), "w") as f:
    f.write(f"# {name}: {len(faces)} triangles\no {name}\n")
    for p in verts:
        f.write(f"v {p[0]:.5f} {p[1]:.5f} {p[2]:.5f}\n")
    for t in uv:
        f.write(f"vt {t[0]:.5f} {t[1]:.5f}\n")
    for t in faces + 1:
        f.write(f"f {t[0]}/{t[0]} {t[1]}/{t[1]} {t[2]}/{t[2]}\n")


# Preview: the detailed mesh as coloured points and the baked one as flat triangles, side by side.
def view(pts):
    yaw, pitch = np.radians(35), np.radians(40)
    ry = np.array([[np.cos(yaw), 0, np.sin(yaw)], [0, 1, 0], [-np.sin(yaw), 0, np.cos(yaw)]])
    rx = np.array([[1, 0, 0], [0, np.cos(pitch), -np.sin(pitch)], [0, np.sin(pitch), np.cos(pitch)]])
    return (pts - [0, 0.4, 0]) @ ry.T @ rx.T


def shade(col, nrm):
    light = np.array([0.4, 0.8, 0.45])
    light = light / np.linalg.norm(light)
    return col * (0.45 + 0.65 * np.clip(nrm @ light, 0, 1))[:, None]


def splat(pts, col, size=420):
    p = view(pts)
    xs = np.clip(((p[:, 0] * 0.62 + 0.5) * size).astype(int), 0, size - 1)
    ys = np.clip(((0.5 - p[:, 1] * 0.62) * size).astype(int), 0, size - 1)
    order = np.argsort(p[:, 2])
    img = np.full((size, size, 3), 0.62)
    img[ys[order], xs[order]] = col[order]
    return img


left = splat(points, shade(colors, normals_high))
low = trimesh.Trimesh(verts, faces, process=False)
lface = rng.choice(len(faces), size=SAMPLES // 2, p=low.area_faces / low.area_faces.sum())
r1, r2 = np.sqrt(rng.random(len(lface))), rng.random(len(lface))
lb = np.stack([1 - r1, r1 * (1 - r2), r1 * r2], axis=1)
lp = np.einsum("ij,ijk->ik", lb, verts[faces[lface]])
luv = np.einsum("ij,ijk->ik", lb, uv[faces[lface]])
right = splat(lp, shade(bilinear(image, luv), low.face_normals[lface]))
top = np.full((420, 420, 3), 0.62)
tx = np.clip(((lp[:, 0] * 0.8 + 0.5) * 420).astype(int), 0, 419)
tz = np.clip(((0.5 - lp[:, 2] * 0.8) * 420).astype(int), 0, 419)
order = np.argsort(lp[:, 1])
top[tz[order], tx[order]] = shade(bilinear(image, luv), low.face_normals[lface])[order]
sheet = np.concatenate([left, right, top], axis=1)
Image.fromarray((np.clip(sheet, 0, 1) * 255).astype(np.uint8)).save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "preview", name + ".jpg"), quality=88)
size = verts.max(axis=0) - verts.min(axis=0)
print(f"{name}: {len(high.faces)} -> {len(faces)} triangles, {len(verts)} vertices, size {size.round(2)}")
