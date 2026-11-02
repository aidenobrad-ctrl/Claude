"""Roller-coaster track designer and renderer.

A Track is driven like a turtle: straight(), pitch(), turn(), loop(), corkscrew(), hill(), connect().
Each element appends dense samples (position, forward, up). render() turns samples into blocks:
two rails, a spine under the track (or above it when inverted), cross ties, plus supports.
"""
import math

import numpy as np

from .const import G
from .geom import line

Y = np.array([0.0, 1.0, 0.0])
STEP = 0.2


def _norm(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v


def heading_vec(yaw):
    """yaw in radians, 0 = north (-z), pi/2 = east (+x)."""
    return np.array([math.sin(yaw), 0.0, -math.cos(yaw)])


def upright(f, bank=0.0):
    up = _norm(Y - np.dot(Y, f) * f)
    if bank:
        right = _norm(np.cross(f, up))
        up = math.cos(bank) * up + math.sin(bank) * right
    return up


class Track:
    def __init__(self, x, y, z, yaw_deg):
        self.p = np.array([x, y, z], dtype=float)
        self.yaw = math.radians(yaw_deg)
        self.pitch = 0.0
        self.samples = []
        self.marks = {}
        self._add(self.p.copy(), self.fwd(), upright(self.fwd()))

    def fwd(self):
        h = heading_vec(self.yaw)
        return _norm(h * math.cos(self.pitch) + Y * math.sin(self.pitch))

    def _add(self, p, f, u):
        self.samples.append((p.copy(), _norm(f), _norm(u)))

    def mark(self, name):
        self.marks[name] = len(self.samples) - 1
        return self

    # ------------------------------------------------------------------------------------
    def straight(self, L):
        f = self.fwd()
        n = max(1, int(L / STEP))
        for i in range(1, n + 1):
            self._add(self.p + f * (L * i / n), f, upright(f))
        self.p = self.p + f * L
        return self

    def pitch_to(self, deg, radius):
        target = math.radians(deg)
        d = target - self.pitch
        if abs(d) < 1e-6:
            return self
        n = max(1, int(abs(d) * radius / STEP))
        ds = abs(d) * radius / n
        for i in range(n):
            self.pitch += d / n
            f = self.fwd()
            self.p = self.p + f * ds
            self._add(self.p, f, upright(f))
        self.pitch = target
        return self

    def climb_to(self, y_target, angle_deg, r_in=14, r_out=12):
        """Pitch up (or down) to angle, run straight, and level out exactly at y_target."""
        sgn = 1 if y_target > self.p[1] else -1
        a = math.radians(abs(angle_deg))
        dy_arcs = r_in * (1 - math.cos(a)) + r_out * (1 - math.cos(a))
        dy_straight = abs(y_target - self.p[1]) - dy_arcs
        if dy_straight < 0:
            dy_straight = 0
        self.pitch_to(sgn * abs(angle_deg), r_in)
        self.straight(dy_straight / math.sin(a))
        self.pitch_to(0, r_out)
        self.p[1] = y_target
        return self

    def turn(self, deg, radius, dy=0.0, bank_deg=0.0):
        """Horizontal arc (positive = right turn) with optional height change and banking."""
        d = math.radians(deg)
        arc = abs(d) * radius
        n = max(2, int(math.hypot(arc, dy) / STEP))
        y0 = self.p[1]
        for i in range(1, n + 1):
            t = i / n
            self.yaw += d / n
            h = heading_vec(self.yaw)
            step = h * (arc / n)
            self.p = self.p + step
            # smooth height profile (zero slope at both ends)
            self.p[1] = y0 + dy * (0.5 - 0.5 * math.cos(math.pi * t))
            slope = dy * 0.5 * math.pi * math.sin(math.pi * t) / max(arc, 1e-6)
            f = _norm(h + Y * slope)
            bank = math.radians(bank_deg) * math.sin(math.pi * t) ** 0.7 * (1 if deg > 0 else -1)
            self._add(self.p, f, upright(f, bank))
        self.pitch = 0.0
        return self

    def helix(self, turns, radius, dy, bank_deg=35, right=True):
        """Multi-revolution helix with constant slope and banking."""
        total = 360.0 * turns * (1 if right else -1)
        d = math.radians(total)
        arc = abs(d) * radius
        n = max(2, int(math.hypot(arc, dy) / STEP))
        y0 = self.p[1]
        for i in range(1, n + 1):
            t = i / n
            self.yaw += d / n
            h = heading_vec(self.yaw)
            self.p = self.p + h * (arc / n)
            ease = min(1.0, t * 8, (1 - t) * 8)
            self.p[1] = y0 + dy * t
            f = _norm(h + Y * (dy / arc) * 1.0)
            bank = math.radians(bank_deg) * ease * (1 if right else -1)
            self._add(self.p, f, upright(f, bank))
        self.pitch = 0.0
        return self

    def loop(self, radius, offset=5.0, right=True):
        """Vertical loop; exits `offset` blocks to the side so the track clears itself."""
        h = heading_vec(self.yaw)
        side = _norm(np.cross(h, Y)) * (1 if right else -1)
        p0 = self.p.copy()
        n = max(8, int(math.tau * radius / STEP))
        for i in range(1, n + 1):
            th = math.tau * i / n
            lat = offset * (th - math.sin(th)) / math.tau
            pos = p0 + h * radius * math.sin(th) + Y * radius * (1 - math.cos(th)) + side * lat
            centre = p0 + Y * radius + side * lat
            dpos = h * radius * math.cos(th) + Y * radius * math.sin(th) + side * offset * (1 - math.cos(th)) / math.tau
            f = _norm(dpos)
            up = _norm(centre - pos)
            up = _norm(up - np.dot(up, f) * f)
            self._add(pos, f, up)
        self.p = p0 + side * offset
        self.pitch = 0.0
        return self

    def corkscrew(self, length, radius, right=True):
        h = heading_vec(self.yaw)
        side = _norm(np.cross(h, Y)) * (1 if right else -1)
        p0 = self.p.copy()
        n = max(8, int(math.hypot(length, math.tau * radius) / STEP))
        for i in range(1, n + 1):
            s = length * i / n
            ph = math.tau * i / n
            axis = p0 + h * s + Y * radius
            pos = axis - Y * radius * math.cos(ph) + side * radius * math.sin(ph)
            dpos = h * length / math.tau + Y * radius * math.sin(ph) + side * radius * math.cos(ph)
            f = _norm(dpos)
            up = _norm(axis - pos)
            up = _norm(up - np.dot(up, f) * f)
            self._add(pos, f, up)
        self.p = p0 + h * length
        self.pitch = 0.0
        return self

    def hill(self, height, length):
        """Camelback / airtime hill (sin^2 profile)."""
        h = heading_vec(self.yaw)
        p0 = self.p.copy()
        n = max(4, int(length / STEP))
        for i in range(1, n + 1):
            s = length * i / n
            t = s / length
            pos = p0 + h * s + Y * height * math.sin(math.pi * t) ** 2
            slope = height * math.pi / length * math.sin(2 * math.pi * t)
            f = _norm(h + Y * slope)
            self._add(pos, f, upright(f))
        self.p = p0 + h * length
        self.pitch = 0.0
        return self

    def connect(self, x, y, z, yaw_deg, tension=1.0, bank=True):
        """Cubic Hermite connector to a target position/heading (level at both ends)."""
        p0 = self.p.copy()
        p1 = np.array([x, y, z], dtype=float)
        h0 = heading_vec(self.yaw)
        h1 = heading_vec(math.radians(yaw_deg))
        L = np.linalg.norm(p1 - p0) * tension
        m0, m1 = h0 * L, h1 * L
        n = max(8, int(np.linalg.norm(p1 - p0) * 1.6 / STEP))
        prev_h = None
        pts = []
        for i in range(1, n + 1):
            t = i / n
            t2, t3 = t * t, t * t * t
            pos = (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1
            d = (6 * t2 - 6 * t) * p0 + (3 * t2 - 4 * t + 1) * m0 + (-6 * t2 + 6 * t) * p1 + (3 * t2 - 2 * t) * m1
            pts.append((pos, _norm(d)))
        for k, (pos, f) in enumerate(pts):
            bank_a = 0.0
            if bank and 0 < k < len(pts) - 1:
                fa, fb = pts[k - 1][1], pts[k + 1][1]
                ya = math.atan2(fa[0], -fa[2])
                yb = math.atan2(fb[0], -fb[2])
                dyaw = (yb - ya + math.pi) % math.tau - math.pi
                ds = np.linalg.norm(pts[k + 1][0] - pts[k - 1][0])
                curv = dyaw / max(ds, 1e-6)
                bank_a = max(-0.8, min(0.8, curv * 9.0))
            self._add(pos, f, upright(f, bank_a))
        self.p = p1
        self.yaw = math.radians(yaw_deg)
        self.pitch = 0.0
        return self

    # ------------------------------------------------------------------------------------
    def frames(self):
        out = []
        for p, f, u in self.samples:
            r = _norm(np.cross(f, u))
            u2 = _norm(np.cross(r, f))
            out.append((p, f, u2, r))
        return out


def render(w, track, rail='white_concrete', spine='cyan_concrete', tie='light_gray_concrete', gauge=1.0,
           support='light_gray_concrete', support_every=7.0, footing='polished_andesite', no_support=(),
           tunnel_ranges=(), skip_ranges=()):
    """Draw the track; returns the set of occupied cells."""
    fr = track.frames()
    cells = {}
    prio = {'rail': 3, 'spine': 2, 'tie': 1}

    def put(p, kind):
        c = (int(round(p[0])), int(round(p[1])), int(round(p[2])))
        if c not in cells or prio[kind] > prio[cells[c]]:
            cells[c] = kind

    arc = 0.0
    last = None
    arcs = []
    for i, (p, f, u, r) in enumerate(fr):
        if last is not None:
            arc += np.linalg.norm(p - last)
        last = p
        arcs.append(arc)
        if any(a <= i <= b for a, b in skip_ranges):
            continue
        put(p + r * gauge, 'rail')
        put(p - r * gauge, 'rail')
        put(p - u * 1.0, 'spine')
        if int(arc / 1.6) != int((arc - STEP) / 1.6) or i == 0:
            for t in np.linspace(-gauge, gauge, 5):
                put(p - u * 0.45 + r * t, 'tie')
    # clear a tunnel bore along tunnel ranges before placing track
    for a, b in tunnel_ranges:
        for i in range(a, b + 1):
            p, f, u, r = fr[i]
            for du in (-2, -1, 0, 1, 2):
                for dv in (-1, 0, 1, 2, 3):
                    q = p + r * du + u * dv
                    c = (int(round(q[0])), int(round(q[1])), int(round(q[2])))
                    if c not in cells:
                        w.set(c[0], c[1], c[2], 'air')
    mat = {'rail': rail, 'spine': spine, 'tie': tie}
    for c, k in cells.items():
        w.set(c[0], c[1], c[2], mat[k])
    # supports
    occupied = set(cells)
    next_support = support_every * 0.5
    for i, (p, f, u, r) in enumerate(fr):
        if arcs[i] < next_support:
            continue
        if any(a <= i <= b for a, b in no_support) or any(a <= i <= b for a, b in tunnel_ranges):
            continue
        if u[1] < 0.55:
            continue
        top = p - u * 1.0
        base = (int(round(top[0])), int(round(top[1])) - 1, int(round(top[2])))
        hgt = base[1] - G
        if hgt < 2:
            continue
        next_support = arcs[i] + support_every
        rh = np.array([r[0], 0.0, r[2]])
        rh = _norm(rh) if np.linalg.norm(rh) > 1e-6 else np.array([1.0, 0, 0])
        if hgt > 30:
            spread = hgt * 0.22
            legs = []
            ok = True
            for sgn in (-1, 1):
                foot = np.array([base[0], G + 1, base[2]]) + rh * spread * sgn
                pts = line(base, (foot[0], foot[1], foot[2]))
                for q in pts[1:]:
                    if q in occupied or not w.is_air(*q):
                        ok = False
                        break
                legs.append(pts)
            if ok:
                for pts in legs:
                    for q in pts:
                        w.set(*q, support)
                        occupied.add(q)
                    fx, fy, fz = pts[-1]
                    w.set(fx, G, fz, footing)
                # cross braces
                for k in range(8, hgt - 2, 9):
                    yb = base[1] - k
                    a_ = [q for q in legs[0] if q[1] == yb]
                    b_ = [q for q in legs[1] if q[1] == yb]
                    if a_ and b_:
                        for q in line(a_[0], b_[0]):
                            if w.is_air(*q) and q not in occupied:
                                w.set(*q, support)
                continue
        # single column
        col = [(base[0], y, base[2]) for y in range(G + 1, base[1] + 1)]
        if all(q not in occupied and w.is_air(*q) for q in col[:-1]):
            for q in col:
                w.set(*q, support)
                occupied.add(q)
            w.set(base[0], G, base[2], footing)
    return cells, fr, arcs


def tunnel(w, fr, a, b, shell='gray_concrete', glass='cyan_stained_glass', light='sea_lantern', width=3, height=4):
    """Rectangular tunnel shell around track samples a..b (call before render)."""
    for i in range(a, b + 1):
        p, f, u, r = fr[i]
        for du in range(-width, width + 1):
            for dv in range(-2, height + 1):
                edge = abs(du) == width or dv in (-2, height)
                if not edge:
                    continue
                q = p + r * du + u * dv
                c = (int(round(q[0])), int(round(q[1])), int(round(q[2])))
                blk = shell
                if dv == height and du == 0 and i % 30 < 6:
                    blk = light
                elif abs(du) == width and dv == 1 and i % 12 < 6:
                    blk = glass
                if c[1] >= G:
                    w.set(c[0], c[1], c[2], blk)


def train(w, track, start_idx, cars=5, car_len=3, body='cyan_concrete', seat='quartz_stairs', trim='white_concrete'):
    """Place a parked train along the track starting at sample index start_idx going backward."""
    fr = track.frames()
    p0, f, u, r = fr[start_idx]
    back = (-f[0], -f[2])
    face = 'north'
    if abs(f[0]) > abs(f[2]):
        face = 'east' if f[0] > 0 else 'west'
    else:
        face = 'south' if f[2] > 0 else 'north'
    opp = {'north': 'south', 'south': 'north', 'east': 'west', 'west': 'east'}[face]
    fx, fz = int(round(f[0])), int(round(f[2]))
    rx, rz = int(round(r[0])), int(round(r[2]))
    x0, y0, z0 = int(round(p0[0])), int(round(p0[1])) + 1, int(round(p0[2]))
    for c in range(cars):
        for k in range(car_len + 1):
            off = c * (car_len + 1) + k
            x, z = x0 - fx * off, z0 - fz * off
            if k == car_len:
                continue  # gap between cars
            for s in (-1, 0, 1):
                X, Z = x + rx * s, z + rz * s
                if k == 0:
                    w.set(X, y0, Z, body if s else trim)
                    w.set(X, y0 + 1, Z, 'light_blue_stained_glass' if c == 0 else body if s else 'iron_bars')
                else:
                    w.set(X, y0, Z, f'{seat}[facing={opp},half=bottom]' if s else body)
                    if s == 0:
                        w.set(X, y0 + 1, Z, 'iron_bars')
    return face
