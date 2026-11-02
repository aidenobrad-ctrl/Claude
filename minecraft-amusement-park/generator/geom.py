"""Geometry helpers: lines, circles, discs, spheres, cones, domes, splines."""
import math

import numpy as np


def line(p0, p1):
    """Integer points on a 3D line (inclusive), dense enough to have no diagonal gaps along the main axis."""
    x0, y0, z0 = p0
    x1, y1, z1 = p1
    n = int(max(abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)))
    if n == 0:
        return [(int(round(x0)), int(round(y0)), int(round(z0)))]
    pts = []
    last = None
    for i in range(n + 1):
        t = i / n
        p = (int(round(x0 + (x1 - x0) * t)), int(round(y0 + (y1 - y0) * t)), int(round(z0 + (z1 - z0) * t)))
        if p != last:
            pts.append(p)
            last = p
    return pts


def thick_line(p0, p1):
    """3D line where consecutive points are face-connected (no diagonal-only steps)."""
    pts = line(p0, p1)
    out = [pts[0]]
    for p in pts[1:]:
        q = out[-1]
        # step axis by axis
        cur = list(q)
        for ax in range(3):
            if cur[ax] != p[ax]:
                cur[ax] = p[ax]
                out.append(tuple(cur))
    return out


def line2(x0, z0, x1, z1):
    return [(x, z) for x, _, z in line((x0, 0, z0), (x1, 0, z1))]


def disc(cx, cz, r):
    """Cells (x,z) with center distance <= r. cx/cz may be .5 for even diameters."""
    out = []
    for x in range(int(math.floor(cx - r)), int(math.ceil(cx + r)) + 1):
        for z in range(int(math.floor(cz - r)), int(math.ceil(cz + r)) + 1):
            if (x - cx) ** 2 + (z - cz) ** 2 <= r * r + 0.25:
                out.append((x, z))
    return out


def ring(cx, cz, r_out, r_in):
    out = []
    for x in range(int(math.floor(cx - r_out)), int(math.ceil(cx + r_out)) + 1):
        for z in range(int(math.floor(cz - r_out)), int(math.ceil(cz + r_out)) + 1):
            d2 = (x - cx) ** 2 + (z - cz) ** 2
            if r_in * r_in + 0.25 < d2 <= r_out * r_out + 0.25:
                out.append((x, z))
    return out


def circle(cx, cz, r):
    """Closed (4-connected) one-block-thick circle outline."""
    inside = set(disc(cx, cz, r))
    out = []
    for (x, z) in inside:
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if (x + dx, z + dz) not in inside:
                out.append((x, z))
                break
    return out


def angle_of(x, z, cx, cz):
    return math.atan2(z - cz, x - cx)


def facing_toward(dx, dz):
    """Cardinal direction name closest to the vector (dx, dz)."""
    if abs(dx) >= abs(dz):
        return 'east' if dx > 0 else 'west'
    return 'south' if dz > 0 else 'north'


def facing_away(dx, dz):
    return facing_toward(-dx, -dz)


def sphere_shell(cx, cy, cz, r, thick=1.0):
    out = []
    R = int(math.ceil(r))
    for x in range(-R, R + 1):
        for y in range(-R, R + 1):
            for z in range(-R, R + 1):
                d = math.sqrt(x * x + y * y + z * z)
                if r - thick < d <= r + 0.0001:
                    out.append((int(cx + x), int(cy + y), int(cz + z)))
    return out


def ball(cx, cy, cz, r):
    out = []
    R = int(math.ceil(r))
    for x in range(-R, R + 1):
        for y in range(-R, R + 1):
            for z in range(-R, R + 1):
                if x * x + y * y + z * z <= r * r + 0.3:
                    out.append((int(round(cx + x)), int(round(cy + y)), int(round(cz + z))))
    return out


def ellipsoid(cx, cy, cz, rx, ry, rz):
    out = []
    for x in range(-int(rx) - 1, int(rx) + 2):
        for y in range(-int(ry) - 1, int(ry) + 2):
            for z in range(-int(rz) - 1, int(rz) + 2):
                if (x / rx) ** 2 + (y / ry) ** 2 + (z / rz) ** 2 <= 1.0:
                    out.append((int(cx + x), int(cy + y), int(cz + z)))
    return out


def catmull_rom(points, samples_per_unit=3.0, closed=False):
    """Catmull-Rom spline through 3D points; returns dense float points."""
    pts = [np.array(p, dtype=float) for p in points]
    if closed:
        pts = [pts[-1]] + pts + [pts[0], pts[1]]
    else:
        pts = [pts[0]] + pts + [pts[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        L = np.linalg.norm(p2 - p1)
        n = max(2, int(L * samples_per_unit))
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            p = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
            out.append(p)
    if not closed:
        out.append(pts[-2])
    return out


def poly_dense(points, step=0.25):
    """Densify a 2D/3D polyline."""
    out = []
    for a, b in zip(points[:-1], points[1:]):
        a = np.array(a, dtype=float)
        b = np.array(b, dtype=float)
        L = np.linalg.norm(b - a)
        n = max(1, int(L / step))
        for i in range(n):
            out.append(a + (b - a) * (i / n))
    out.append(np.array(points[-1], dtype=float))
    return out


def point_seg_dist(px, pz, ax, az, bx, bz):
    vx, vz = bx - ax, bz - az
    L2 = vx * vx + vz * vz
    if L2 == 0:
        return math.hypot(px - ax, pz - az), 0.0
    t = max(0.0, min(1.0, ((px - ax) * vx + (pz - az) * vz) / L2))
    cx, cz = ax + vx * t, az + vz * t
    return math.hypot(px - cx, pz - cz), t
