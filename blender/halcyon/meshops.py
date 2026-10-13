"""Triangle-mesh operations in numpy for the car generator.

The body is polygonized from a signed distance field, so its edges follow
the voxel grid. Features (windows, lights, grilles, panel gaps) are cut into
it along the exact zero contour of a region function instead: every edge
that crosses the contour gets a new vertex at the crossing (refined on the
real function and projected back onto the body), and each crossed triangle
splits into three. The cut is as smooth as the region function, whatever the
voxel size.

Meshes are (V (n, 3) float64, F (m, 3) int64), counter-clockwise seen from
outside. Pure numpy, so it runs both in Blender's Python and outside it.
"""
import numpy as np


def triangulate(V, quads):
    """Split quads along their shorter diagonal."""
    q = np.asarray(quads)
    a, b, c, d = q[:, 0], q[:, 1], q[:, 2], q[:, 3]
    ac = np.linalg.norm(V[a] - V[c], axis=1)
    bd = np.linalg.norm(V[b] - V[d], axis=1)
    use_ac = ac <= bd
    t1 = np.where(use_ac[:, None], np.stack([a, b, c], 1), np.stack([a, b, d], 1))
    t2 = np.where(use_ac[:, None], np.stack([a, c, d], 1), np.stack([b, c, d], 1))
    return np.concatenate([t1, t2])


def gradient(fn, P, eps=1e-3):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    g = np.stack([
        fn(x + eps, y, z) - fn(x - eps, y, z),
        fn(x, y + eps, z) - fn(x, y - eps, z),
        fn(x, y, z + eps) - fn(x, y, z - eps),
    ], axis=1) / (2 * eps)
    return g


def unit(g):
    return g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-12)


def project(fn, P, iters=3, max_step=0.004, eps=5e-4):
    """Move points onto fn = 0 with damped Newton steps along the gradient."""
    P = np.array(P, dtype=np.float64)
    for _ in range(iters):
        f = fn(P[:, 0], P[:, 1], P[:, 2])
        g = gradient(fn, P, eps)
        g2 = np.maximum((g * g).sum(1), 1e-12)
        step = (f / g2)[:, None] * g
        n = np.linalg.norm(step, axis=1, keepdims=True)
        step *= np.minimum(1.0, max_step / np.maximum(n, 1e-12))
        P -= step
    return P


def _edge_key(i, j, n):
    lo = np.minimum(i, j).astype(np.int64)
    hi = np.maximum(i, j).astype(np.int64)
    return lo * n + hi


def cut(V, F, region, N=None, surface=None, refine=5, values=None):
    """Split the mesh along region = 0.

    region(P, N) is vectorized over points P (k, 3) with normals N (k, 3);
    negative inside. N are per-vertex normals (interpolated along edges for
    the crossing points). surface, if given, is the body's field: new
    vertices are projected onto it. Returns (V, F, inside, parent, N):
    inside[k] is True for faces in the region, parent[k] is the index of the
    original face each new face came from.
    """
    V = np.asarray(V, dtype=np.float64)
    F = np.asarray(F, dtype=np.int64)
    if N is None:
        N = np.zeros_like(V)
    n = len(V)
    s = (region(V, N) if values is None else values).astype(np.float64)
    s = np.where(np.abs(s) < 1e-9, 1e-9, s)
    neg = s < 0
    nf = neg[F]
    cnt = nf.sum(1)
    whole = (cnt == 0) | (cnt == 3)
    mixed = np.nonzero(~whole)[0]
    if len(mixed) == 0:
        return V, F, cnt == 3, np.arange(len(F)), N
    Fm = F[mixed]
    nm = nf[mixed]
    lone_neg = cnt[mixed] == 1
    lone_mask = np.where(lone_neg[:, None], nm, ~nm)
    li = np.argmax(lone_mask, axis=1)
    r = np.arange(len(Fm))
    A = Fm[r, li]
    B = Fm[r, (li + 1) % 3]
    C = Fm[r, (li + 2) % 3]
    # Unique crossed edges and their crossing points.
    ea = np.concatenate([A, A])
    eb = np.concatenate([B, C])
    keys = _edge_key(ea, eb, n)
    ukeys, first = np.unique(keys, return_index=True)
    a = ea[first]
    b = eb[first]
    sa, sb = s[a], s[b]
    lo = np.zeros(len(a))
    hi = np.ones(len(a))
    slo, shi = sa.copy(), sb.copy()
    t = slo / (slo - shi)
    for _ in range(refine):
        P = V[a] + t[:, None] * (V[b] - V[a])
        sp = region(P, N[a] + t[:, None] * (N[b] - N[a]))
        same = (sp < 0) == (slo < 0)
        lo = np.where(same, t, lo)
        slo = np.where(same, sp, slo)
        hi = np.where(same, hi, t)
        shi = np.where(same, shi, sp)
        den = slo - shi
        t = np.where(np.abs(den) > 1e-15, lo + (hi - lo) * slo / np.where(np.abs(den) > 1e-15, den, 1), (lo + hi) / 2)
        t = np.clip(t, lo, hi)
    P = V[a] + t[:, None] * (V[b] - V[a])
    NP = unit(N[a] + t[:, None] * (N[b] - N[a]))
    if surface is not None:
        P = project(surface, P, iters=2)
    new_idx = n + np.arange(len(a))

    def lookup(i, j):
        pos = np.searchsorted(ukeys, _edge_key(i, j, n))
        return new_idx[pos]

    pab = lookup(A, B)
    pac = lookup(A, C)
    t1 = np.stack([A, pab, pac], 1)
    t2 = np.stack([pab, B, C], 1)
    t3 = np.stack([pab, C, pac], 1)
    keep = np.nonzero(whole)[0]
    F2 = np.concatenate([F[keep], t1, t2, t3])
    inside = np.concatenate([cnt[keep] == 3, lone_neg, ~lone_neg, ~lone_neg])
    parent = np.concatenate([keep, mixed, mixed, mixed])
    return np.concatenate([V, P]), F2, inside, parent, np.concatenate([N, NP])


def compact(V, F):
    """Drop unused vertices."""
    used = np.unique(F)
    remap = np.full(len(V), -1, dtype=np.int64)
    remap[used] = np.arange(len(used))
    return V[used], remap[F]


def subset(V, F, mask):
    return compact(V, F[mask])


def boundary_edges(F):
    """Directed boundary edges (a -> b) of the faces: edges whose reverse is not used."""
    e = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
    n = int(F.max()) + 1 if len(F) else 1
    fwd = e[:, 0].astype(np.int64) * n + e[:, 1]
    rev = e[:, 1].astype(np.int64) * n + e[:, 0]
    is_b = ~np.isin(fwd, rev)
    return e[is_b]


def wall(V, F, normals, depth, shrink=0.0):
    """Quads hanging from every boundary edge into the surface (along -normal).

    Returns (Vw, Fw) as a separate mesh; the wall faces look into the hole.
    shrink pulls the bottom edge in towards the hole, so the wall leans.
    """
    be = boundary_edges(F)
    if len(be) == 0:
        return np.zeros((0, 3)), np.zeros((0, 3), dtype=np.int64)
    verts = np.unique(be)
    loc = np.full(int(be.max()) + 1, -1, dtype=np.int64)
    loc[verts] = np.arange(len(verts))
    top = V[verts]
    nrm = normals[verts]
    bot = top - nrm * depth
    if shrink:
        # Direction across the boundary, towards the hole: average of the edge "right" vectors.
        acc = np.zeros_like(top)
        for k in (0, 1):
            x = V[be[:, 1]] - V[be[:, 0]]
            right = np.cross(x, normals[be[:, k]])
            np.add.at(acc, loc[be[:, k]], right)
        bot += unit(acc) * shrink
    Vw = np.concatenate([top, bot])
    m = len(verts)
    a, b = loc[be[:, 0]], loc[be[:, 1]]
    # Quad (b, a, a', b') faces into the hole.
    Fw = np.concatenate([np.stack([b, a, a + m], 1), np.stack([b, a + m, b + m], 1)])
    return Vw, Fw


def offset(V, normals, d):
    return V - normals * d


def area(V, F):
    return 0.5 * np.linalg.norm(np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]), axis=1)


def merge(meshes):
    """Concatenate [(V, F), ...] into one mesh; returns (V, F, part index per face)."""
    Vs, Fs, parts = [], [], []
    off = 0
    for k, (v, f) in enumerate(meshes):
        if len(f) == 0:
            continue
        Vs.append(v)
        Fs.append(f + off)
        parts.append(np.full(len(f), k))
        off += len(v)
    if not Vs:
        return np.zeros((0, 3)), np.zeros((0, 3), dtype=np.int64), np.zeros(0, dtype=np.int64)
    return np.concatenate(Vs), np.concatenate(Fs), np.concatenate(parts)
