"""Car bodies as smooth signed distance fields.

A car is sculpted from smooth profile functions instead of a control cage:
a lower body (plan shape with haunches, a shoulder crease, tucked sills,
hood and deck heights), a greenhouse (windshield, roof, fastback), wheel
arches with flared lips, mirrors on stalks. It is polygonized with surface
nets; materials are assigned per face from regions (glass, lights, trim)
which are inset slightly.

Car frame: u forward, v left, z up, metres, ground at z = 0. The body is
built around the wheelbase centre (self.mid); Blender coordinates are
x = v (left), y = -(u - mid) (the car faces -Y), z.
"""
from dataclasses import dataclass, field
import numpy as np


# --- Smooth operators ---------------------------------------------------------------
def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def bump(x, w):
    """Smooth bump: 1 at 0, 0 beyond +-w (cosine window)."""
    t = np.clip(np.abs(x) / w, 0.0, 1.0)
    return 0.5 + 0.5 * np.cos(np.pi * t)


def rbox2(px, py, hx, hy, r):
    """2D rounded box SDF centred at the origin."""
    qx = np.abs(px) - hx + r
    qy = np.abs(py) - hy + r
    return np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0) - r


# --- Parameters ------------------------------------------------------------------------
@dataclass
class CarShape:
    """Dimensions from the car spec plus styling, all in metres."""
    length: float = 4.38
    width: float = 1.86
    height: float = 1.30
    wheelbase: float = 2.47
    track_f: float = 1.59
    track_r: float = 1.60
    tire_r: float = 0.335
    tire_w_f: float = 0.255
    tire_w_r: float = 0.275
    clearance: float = 0.12
    # Styling (heights above ground; u positions relative to the front axle where noted).
    front_overhang: float = 0.88
    nose_z: float = 0.46          # height of the nose tip
    hood_front_z: float = 0.66    # hood height at the front edge
    cowl_back: float = 0.62       # windshield base behind the front axle
    cowl_z: float = 0.86
    windshield_slope: float = 0.58  # rise per run
    roof_len: float = 0.72        # flat-ish roof length
    rear_slope: float = 0.36      # fastback rise per run
    deck_z: float = 0.93
    tail_z: float = 0.90
    belt_z: float = 0.81
    shoulder_z: float = 0.70      # height of the side crease
    shoulder_out: float = 0.018   # how far the crease stands out
    shoulder_inset: float = 0.075  # greenhouse inset from the body side at the beltline
    tumblehome: float = 0.42      # greenhouse side lean (run per rise)
    haunch_r: float = 0.05        # rear fender bulge
    haunch_f: float = 0.03        # front fender bulge
    fender_crown: float = 0.035   # fenders stand above the hood
    arch_gap: float = 0.04
    arch_lip: float = 0.012
    ducktail: float = 0.03
    mirror: bool = True
    black_roof: bool = False
    extra: dict = field(default_factory=dict)

    @property
    def u_front(self):
        return self.length / 2

    @property
    def u_rear(self):
        return -self.length / 2

    @property
    def axle_f(self):
        return self.u_front - self.front_overhang

    @property
    def axle_r(self):
        return self.axle_f - self.wheelbase

    @property
    def cowl_u(self):
        return self.axle_f - self.cowl_back


# --- The body --------------------------------------------------------------------------------
class CarBody:
    def __init__(self, s: CarShape):
        self.s = s
        self.mid = (s.axle_f + s.axle_r) / 2
        # Greenhouse key points.
        self.roof_front_u = s.cowl_u - (s.height - s.cowl_z) / s.windshield_slope
        self.roof_rear_u = self.roof_front_u - s.roof_len
        self.rear_base_u = self.roof_rear_u - (s.height - 0.04 - s.deck_z) / s.rear_slope

    # Lower body ------------------------------------------------------------------------------
    def plan_width(self, u):
        """Half width at the shoulder crease along the car (plan view)."""
        s = self.s
        hw0 = s.width / 2 - 0.045
        hw = hw0 - 0.025 + s.haunch_r * bump(u - s.axle_r, 0.8) + s.haunch_f * bump(u - s.axle_f, 0.7)
        nose = sstep(s.axle_f + 0.30, s.u_front, u)
        tail = sstep(s.axle_r - 0.55, s.u_rear, u)
        return hw - 0.12 * nose ** 2 - 0.05 * tail ** 2

    def half_width(self, u, z):
        """Body half width at height z: tucked at the sill, out at the crease, in above it."""
        s = self.s
        hw = self.plan_width(u)
        below = np.clip((s.shoulder_z - z) / (s.shoulder_z - s.clearance), 0.0, 1.0)
        above = np.clip((z - s.shoulder_z) / 0.25, 0.0, 1.0)
        crease = s.shoulder_out * np.maximum(0.0, 1 - np.abs(z - s.shoulder_z) / 0.06) ** 2
        return hw - 0.075 * below ** 1.7 - 0.05 * above ** 1.3 + crease

    def top_z(self, u, v):
        s = self.s
        # Hood: rises from the front edge to the cowl.
        hood_t = np.clip((s.u_front - 0.12 - u) / (s.u_front - 0.12 - s.cowl_u), 0.0, 1.0)
        hood = s.hood_front_z + (s.cowl_z - 0.03 - s.hood_front_z) * (1 - (1 - hood_t) ** 1.7)
        # Deck from the cabin to the tail, with a ducktail lip.
        deck_t = np.clip((u - s.u_rear) / 0.75, 0.0, 1.0)
        deck = s.tail_z + (s.deck_z - s.tail_z) * deck_t + s.ducktail * bump(u - (s.u_rear + 0.10), 0.16)
        belt = s.belt_z + 0.02
        w_hood = sstep(s.cowl_u - 0.12, s.cowl_u + 0.12, u)
        w_deck = sstep(self.rear_base_u + 0.2, self.rear_base_u - 0.25, u)
        z = belt * (1 - w_hood) * (1 - w_deck) + hood * w_hood + deck * w_deck
        # Crown across the width; fenders stand proud over the wheels; a hood bulge.
        hw = s.width / 2
        crown = 0.05 * (v / hw) ** 2
        fender = s.fender_crown * sstep(0.42, 0.72, np.abs(v)) * (bump(u - s.axle_f, 0.8) + 0.9 * bump(u - s.axle_r, 0.85))
        bulge = 0.012 * bump(v, 0.32) * sstep(s.u_front - 0.1, s.axle_f, u) * w_hood
        return z - crown + fender + bulge

    def bottom_z(self, u):
        s = self.s
        front_rise = 0.04 * sstep(s.axle_f + 0.25, s.u_front, u)
        rear_rise = 0.08 * sstep(s.axle_r - 0.35, s.u_rear, u)
        return s.clearance + front_rise + rear_rise

    def nose_u(self, z, v):
        s = self.s
        hw = s.width / 2
        # Undercut below the tip, a sloping front face above it.
        below = np.minimum(z - s.nose_z, 0.0)
        above = np.maximum(z - s.nose_z, 0.0)
        return s.u_front - 0.9 * below ** 2 / 0.32 - 0.55 * above - 0.30 * (v / hw) ** 4

    def tail_u(self, z, v):
        s = self.s
        hw = s.width / 2
        # Near-vertical rear face that tucks under towards the diffuser.
        below = np.minimum(z - 0.32, 0.0)
        return s.u_rear + 0.35 * below ** 2 / 0.2 + 0.06 * np.maximum(z - 0.72, 0.0) + 0.15 * (v / hw) ** 4

    def lower(self, u, v, z):
        d_side = np.abs(v) - self.half_width(u, z)
        d_top = z - self.top_z(u, v)
        d = smax(d_side, d_top, 0.04)
        d = smax(d, u - self.nose_u(z, v), 0.06)
        d = smax(d, self.tail_u(z, v) - u, 0.05)
        d = smax(d, self.bottom_z(u) - z, 0.025)
        return d

    # Greenhouse -------------------------------------------------------------------------------
    def windshield_line(self, u):
        s = self.s
        return s.cowl_z + (s.cowl_u - u) * s.windshield_slope

    def rear_line(self, u):
        s = self.s
        return s.deck_z + (u - self.rear_base_u) * s.rear_slope

    def roof_arc(self, u):
        s = self.s
        mid = (self.roof_front_u + self.roof_rear_u) / 2
        half = s.roof_len / 2 + 0.3
        return s.height - 0.05 * ((u - mid) / half) ** 2

    def roof_z(self, u, v):
        z = smin(self.windshield_line(u), self.roof_arc(u), 0.12)
        z = smin(z, self.rear_line(u), 0.14)
        cw = np.maximum(self.cabin_half_width(u, self.s.height), 0.3)
        return z - 0.07 * (v / cw) ** 2

    def cabin_half_width(self, u, z):
        s = self.s
        base = self.plan_width(u) - 0.06 - s.shoulder_inset
        # Narrower towards the windshield base and the rear deck (teardrop plan).
        base = base - 0.03 * sstep(self.roof_front_u, s.cowl_u + 0.2, u) - 0.07 * sstep(self.roof_rear_u, self.rear_base_u - 0.1, u)
        return base - np.maximum(z - s.belt_z, 0.0) * s.tumblehome

    def cabin(self, u, v, z):
        s = self.s
        d = smax(np.abs(v) - self.cabin_half_width(u, z), z - self.roof_z(u, v), 0.045)
        d = smax(d, s.belt_z - 0.25 - z, 0.02)
        # The greenhouse ends inside the body: no slab running out of the nose or tail.
        d = smax(d, u - (s.cowl_u + 0.25), 0.02)
        d = smax(d, (self.rear_base_u - 0.25) - u, 0.02)
        return d

    # Arches and details -------------------------------------------------------------------
    def _arch(self, u, v, z, axle, track, tw):
        s = self.s
        r = s.tire_r + s.arch_gap
        cyl = np.sqrt((u - axle) ** 2 + (z - s.tire_r) ** 2) - r
        inner = track / 2 - tw / 2 - 0.08
        return smax(cyl, inner - np.abs(v), 0.02), cyl

    def arches(self, u, v, z):
        s = self.s
        a1, _ = self._arch(u, v, z, s.axle_f, s.track_f, s.tire_w_f)
        a2, _ = self._arch(u, v, z, s.axle_r, s.track_r, s.tire_w_r)
        return np.minimum(a1, a2)

    def arch_lips(self, u, v, z):
        """A rounded lip around each arch, standing out from the body side."""
        s = self.s
        out = None
        for axle in (s.axle_f, s.axle_r):
            r = s.tire_r + s.arch_gap + 0.02
            ring = np.sqrt((np.sqrt((u - axle) ** 2 + (z - s.tire_r) ** 2) - r) ** 2 + (np.abs(v) - (self.plan_width(axle) - 0.035)) ** 2) - 0.026
            ring = smax(ring, s.tire_r * 0.35 - z, 0.02)  # not below the axle line
            out = ring if out is None else np.minimum(out, ring)
        return out

    def mirrors(self, u, v, z):
        s = self.s
        cu = s.cowl_u - 0.20
        cz = s.belt_z + 0.065
        side = self.cabin_half_width(cu, cz) - 0.01
        cv = self.half_width(cu, s.belt_z) + 0.035
        q = np.stack([(u - cu) / 0.11, (np.abs(v) - cv) / 0.085, (z - cz) / 0.052])
        head = (np.sqrt((q ** 2).sum(axis=0)) - 1.0) * 0.052
        # Flat back face (towards the rear of the car).
        head = smax(head, cu - 0.06 - u, 0.012)
        # A short, stout stalk from the greenhouse side into the head.
        stalk = rbox2(u - (cu + 0.02), z - (cz - 0.03), 0.04, 0.018, 0.01)
        stalk = smax(stalk, np.abs(np.abs(v) - (side + cv) / 2) - (cv - side) / 2, 0.005)
        return smin(head, stalk, 0.03)

    def base(self, u, v, z):
        """The body before features are cut in."""
        s = self.s
        d = smin(self.lower(u, v, z), self.cabin(u, v, z), 0.035)
        if s.arch_lip > 0:
            d = smin(d, self.arch_lips(u, v, z), 0.04)
        if s.mirror:
            d = smin(d, self.mirrors(u, v, z), 0.012)
        return smax(d, -self.arches(u, v, z), 0.010)

    def sdf(self, u, v, z):
        """The painted body: the base with every feature recessed into it."""
        d = self.base(u, v, z)
        for f in self.features():
            cut = smax(f.region(u, v, z), -(d + f.depth), 0.004)
            d = smax(d, -cut, 0.004)
        return d

    def sdf_blender(self, x, y, z):
        """Field in Blender coordinates (x left, y back, z up), origin mid-wheelbase."""
        return self.sdf(-y + self.mid, x, z)

    # Features: windows, lights and trim, recessed into the body with an insert ----------------
    def features(self):
        if getattr(self, '_features', None) is None:
            self._features = self._make_features()
        return self._features

    def _make_features(self):
        s = self.s
        F = []
        cw = self.cabin_half_width
        hw = s.width / 2

        def side_window(u, v, z):
            rail = self.roof_z(u, cw(u, z)) - 0.065
            a_pillar = s.cowl_u - (z - s.cowl_z) / s.windshield_slope - 0.10
            c_pillar = self.rear_base_u + (z - s.deck_z) / s.rear_slope + 0.24
            d = smax(s.belt_z + 0.035 - z, z - rail, 0.025)
            d = smax(d, smax(u - a_pillar, c_pillar - u, 0.03), 0.03)
            return smax(d, (cw(u, z) - 0.07) - np.abs(v), 0.01)

        def windshield(u, v, z):
            d = smax(np.abs(v) - (cw(u, z) - 0.075), s.cowl_z + 0.03 - z, 0.03)
            d = smax(d, z - (s.height - 0.075), 0.03)
            return smax(d, (self.roof_front_u - 0.2) - u, 0.01)

        def rear_window(u, v, z):
            d = smax(np.abs(v) - (cw(u, z) - 0.115), s.deck_z + 0.055 - z, 0.03)
            d = smax(d, z - (s.height - 0.09), 0.03)
            return smax(d, u - (self.roof_rear_u + 0.1), 0.01)

        glass = lambda u, v, z: np.minimum(np.minimum(side_window(u, v, z), windshield(u, v, z)), rear_window(u, v, z))
        F.append(Feature('Glass', 'Glass', glass, 0.013))

        hl_v0, hl_v1 = 0.40, hw - 0.09

        def headlight(u, v, z, grow=0.0):
            av = np.abs(v)
            t = np.clip((av - hl_v0) / (hl_v1 - hl_v0), 0, 1)
            zc = s.hood_front_z - 0.07 + 0.045 * t
            h = 0.026 + 0.016 * (1 - t) ** 2 + grow
            d = smax(np.abs(z - zc) - h, smax(hl_v0 - grow - av, av - hl_v1 - grow, 0.02), 0.02)
            return smax(d, (s.axle_f + 0.3) - u, 0.01)

        F.append(Feature('LightHousingF', 'Trim', lambda u, v, z: smax(headlight(u, v, z, 0.018), -headlight(u, v, z), 0.002), 0.010))
        F.append(Feature('LightFront', 'LightFront', headlight, 0.008))

        tl_z = s.tail_z - 0.13

        def taillight(u, v, z, grow=0.0):
            av = np.abs(v)
            # Wrap-around clusters at the corners, a slim bar between them.
            cluster = smax(np.abs(z - tl_z) - 0.038 - grow, smax((hw - 0.42) - av, av - (hw - 0.02), 0.02) - grow, 0.02)
            bar = np.abs(z - (tl_z + 0.012)) - 0.010 - grow
            d = np.minimum(cluster, bar)
            return smax(d, u - (s.axle_r - 0.55), 0.01)

        F.append(Feature('LightHousingR', 'Trim', lambda u, v, z: smax(taillight(u, v, z, 0.016), -taillight(u, v, z), 0.002), 0.010))
        F.append(Feature('LightRear', 'LightRear', taillight, 0.008))

        def trim(u, v, z):
            av = np.abs(v)
            front = (s.axle_f + 0.35) - u
            intake_c = smax(rbox2(v, z - (s.clearance + 0.15), 0.40, 0.07, 0.05), front, 0.01)
            intake_s = smax(rbox2(av - 0.67, z - (s.clearance + 0.17), 0.10, 0.085, 0.04), front, 0.01)
            splitter = smax(z - (s.clearance + 0.04), front, 0.01)
            sill = smax(z - (s.clearance + 0.10), smax((s.axle_r + s.tire_r + 0.07) - u, u - (s.axle_f - s.tire_r - 0.07), 0.02), 0.01)
            vent_u = s.axle_f - s.tire_r - s.arch_gap - 0.17
            vent = smax(rbox2(u - vent_u, z - (s.shoulder_z - 0.17), 0.09, 0.028, 0.025), 0.5 - av, 0.01)
            rear = u - (s.axle_r - 0.45)
            diffuser = smax(z - (s.clearance + 0.19), rear, 0.01)
            plate = smax(rbox2(v, z - (s.clearance + 0.37), 0.26, 0.068, 0.02), rear, 0.01)
            d = np.minimum(np.minimum(intake_c, intake_s), np.minimum(splitter, sill))
            return np.minimum(np.minimum(d, vent), np.minimum(diffuser, plate))

        F.append(Feature('Trim', 'Trim', trim, 0.012))
        return F


class Feature:
    """A region recessed into the body and filled with an insert of another material."""

    def __init__(self, name, material, region, depth):
        self.name = name
        self.material = material
        self.region = region
        self.depth = depth
