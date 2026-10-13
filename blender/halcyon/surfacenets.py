"""Naive surface nets: polygonize a sampled signed distance field into quads.

One vertex per grid cell the surface passes through (the mean of the zero
crossings on its edges), one quad per grid edge with a sign change. Pure
numpy, so it runs both in Blender's bundled Python and outside it.
"""
import numpy as np

# Cell corner offsets and the 12 cell edges as corner index pairs.
_CORNERS = np.array([[i, j, k] for k in (0, 1) for j in (0, 1) for i in (0, 1)])
_EDGES = [(a, b) for a in range(8) for b in range(a + 1, 8) if np.abs(_CORNERS[a] - _CORNERS[b]).sum() == 1]


def polygonize(f, origin, h):
    """f: (nx, ny, nz) samples at origin + h * (i, j, k). Negative inside.

    Returns (verts (V, 3) float64, quads (Q, 4) int64), quads wound so their
    normals point outwards (towards positive f).
    """
    f = np.asarray(f, dtype=np.float32)
    nx, ny, nz = f.shape
    inside = f < 0
    # Cells crossed by the surface.
    cx, cy, cz = nx - 1, ny - 1, nz - 1
    corner_in = [inside[c[0]:c[0] + cx, c[1]:c[1] + cy, c[2]:c[2] + cz] for c in _CORNERS]
    n_in = sum(c.astype(np.uint8) for c in corner_in)
    active = (n_in > 0) & (n_in < 8)
    idx = np.full((cx, cy, cz), -1, dtype=np.int64)
    ai = np.nonzero(active)
    nv = len(ai[0])
    idx[ai] = np.arange(nv)
    # Vertex = mean of edge zero crossings (in cell-local coordinates).
    acc = np.zeros((nv, 3))
    cnt = np.zeros(nv)
    corner_f = [f[c[0]:c[0] + cx, c[1]:c[1] + cy, c[2]:c[2] + cz][ai] for c in _CORNERS]
    for a, b in _EDGES:
        fa = corner_f[a]
        fb = corner_f[b]
        cross = (fa < 0) != (fb < 0)
        if not cross.any():
            continue
        t = np.where(cross, fa / np.where(cross, fa - fb, 1), 0)
        p = _CORNERS[a][None, :] + t[:, None] * (_CORNERS[b] - _CORNERS[a])[None, :]
        acc[cross] += p[cross]
        cnt[cross] += 1
    local = acc / np.maximum(cnt, 1)[:, None]
    verts = (np.stack(ai, axis=1) + local) * h + np.asarray(origin)[None, :]
    # Quads: one per grid edge with a sign change, joining the 4 cells around it.
    quads = []
    # Edge along x between (i,j,k) and (i+1,j,k): cells (i, j-1..j, k-1..k).
    e = inside[:-1, 1:-1, 1:-1] != inside[1:, 1:-1, 1:-1]
    i, j, k = np.nonzero(e)
    j += 1
    k += 1
    q = np.stack([idx[i, j - 1, k - 1], idx[i, j, k - 1], idx[i, j, k], idx[i, j - 1, k]], axis=1)
    flip = inside[i, j, k]  # inside at the low end: the surface faces +x
    quads.append(np.where(flip[:, None], q, q[:, ::-1]))
    # Edge along y: cells (i-1..i, j, k-1..k).
    e = inside[1:-1, :-1, 1:-1] != inside[1:-1, 1:, 1:-1]
    i, j, k = np.nonzero(e)
    i += 1
    k += 1
    q = np.stack([idx[i - 1, j, k - 1], idx[i - 1, j, k], idx[i, j, k], idx[i, j, k - 1]], axis=1)
    flip = inside[i, j, k]
    quads.append(np.where(flip[:, None], q, q[:, ::-1]))
    # Edge along z: cells (i-1..i, j-1..j, k).
    e = inside[1:-1, 1:-1, :-1] != inside[1:-1, 1:-1, 1:]
    i, j, k = np.nonzero(e)
    i += 1
    j += 1
    q = np.stack([idx[i - 1, j - 1, k], idx[i, j - 1, k], idx[i, j, k], idx[i - 1, j, k]], axis=1)
    flip = inside[i, j, k]
    quads.append(np.where(flip[:, None], q, q[:, ::-1]))
    quads = np.concatenate(quads)
    quads = quads[(quads >= 0).all(axis=1)]
    return verts, quads


def sample_grid(sdf, lo, hi, h):
    """Evaluate sdf(x, y, z) (vectorized) on a grid covering [lo, hi] with spacing h, slab by slab."""
    lo = np.asarray(lo, dtype=np.float64)
    hi = np.asarray(hi, dtype=np.float64)
    n = np.ceil((hi - lo) / h).astype(int) + 1
    xs = lo[0] + np.arange(n[0]) * h
    ys = lo[1] + np.arange(n[1]) * h
    zs = lo[2] + np.arange(n[2]) * h
    f = np.empty((n[0], n[1], n[2]), dtype=np.float32)
    Y, Z = np.meshgrid(ys, zs, indexing='ij')
    for a in range(n[0]):
        X = np.full_like(Y, xs[a])
        f[a] = sdf(X, Y, Z)
    return f, lo


def normals(sdf, verts, eps=1e-3):
    """Unit gradient of the field at each vertex (smooth shading normals)."""
    x, y, z = verts[:, 0], verts[:, 1], verts[:, 2]
    gx = sdf(x + eps, y, z) - sdf(x - eps, y, z)
    gy = sdf(x, y + eps, z) - sdf(x, y - eps, z)
    gz = sdf(x, y, z + eps) - sdf(x, y, z - eps)
    g = np.stack([gx, gy, gz], axis=1)
    return g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-12)
