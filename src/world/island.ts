// Halcyon Island: the authored geography and the base terrain height.
// Everything here is a pure function of the seed, so the physics, the
// renderer and the offline road baker all see the same island.
import { Noise2 } from '../engine/noise';
import { datan2, dcos, dexp, dsin } from '../engine/dmath';
import { clamp, lerp, smoothstep } from '../engine/math';

export const WORLD_HALF = 5120;
export const WORLD_SIZE = WORLD_HALF * 2;
export const SEA_LEVEL = 0;

export const REGION = {
  plains: 0,
  farmland: 1,
  forest: 2,
  mountains: 3,
  desert: 4,
  city: 5,
  harbor: 6,
  hub: 7,
  proving: 8,
  rally: 9,
  beach: 10,
  lake: 11,
} as const;
export type RegionId = (typeof REGION)[keyof typeof REGION];
export const REGION_NAMES: Record<RegionId, string> = {
  0: 'Plains', 1: 'Farmland', 2: 'Forest', 3: 'Mountains', 4: 'Red Canyon', 5: 'Port Halcyon', 6: 'Harbor',
  7: 'Festival', 8: 'Proving Ground', 9: 'Rally Hills', 10: 'Coast', 11: 'Lake',
};

export interface Pt {
  x: number;
  z: number;
}

/** Flat authored platforms: festival hub, proving ground, city, harbor. */
export interface Plateau {
  region: RegionId;
  x0: number;
  z0: number;
  x1: number;
  z1: number;
  height: number;
  /** Width of the blend back to natural terrain, m. */
  blend: number;
}

export const PLATEAUS: Plateau[] = [
  { region: REGION.hub, x0: 520, z0: 3050, x1: 1300, z1: 3620, height: 14, blend: 260 },
  // The M1 proving ground, placed with its origin at PROVING_ORIGIN.
  { region: REGION.proving, x0: 1500, z0: 2830, x1: 3120, z1: 3660, height: 12, blend: 240 },
  { region: REGION.city, x0: 2560, z0: -900, x1: 3900, z1: 560, height: 22, blend: 420 },
  { region: REGION.harbor, x0: 3600, z0: -1700, x1: 4350, z1: -900, height: 5, blend: 180 },
];

export const PROVING_ORIGIN: Pt = { x: 2350, z: 3080 };

/** Minimum land around the proving ground's plateau, m. */
const HEADLAND = 430;

/** Smooth maximum (polynomial), blending over k. */
function smax(a: number, b: number, k: number): number {
  const h = Math.max(k - Math.abs(a - b), 0) / k;
  return Math.max(a, b) + h * h * k * 0.25;
}

/** The northern range: a ridge polyline with crest heights. */
const RIDGE: { x: number; z: number; h: number }[] = [
  { x: -3300, z: -2100, h: 520 },
  { x: -2300, z: -2700, h: 880 },
  { x: -1200, z: -3050, h: 960 },
  { x: -150, z: -3150, h: 700 },
  { x: 350, z: -3150, h: 610 }, // the pass saddle, just above the snow line
  { x: 1100, z: -3050, h: 820 },
  { x: 2200, z: -2800, h: 900 },
  { x: 3200, z: -2350, h: 560 },
];

export const LAKE = { x: 250, z: -520, r: 620, level: 38 };

/** River from the lake outlet to the south coast. */
export const RIVER: Pt[] = [
  { x: 330, z: 60 },
  { x: 380, z: 650 },
  { x: 230, z: 1250 },
  { x: -60, z: 1850 },
  { x: -180, z: 2500 },
  { x: -420, z: 3150 },
  { x: -520, z: 3800 },
  { x: -560, z: 4700 },
];

/** Red Canyon: a winding gorge through the western desert. */
const CANYON: Pt[] = [
  { x: -4100, z: -500 },
  { x: -3550, z: 50 },
  { x: -3250, z: 650 },
  { x: -2800, z: 1100 },
  { x: -2500, z: 1800 },
];

interface Blob {
  region: RegionId;
  x: number;
  z: number;
  r: number;
  blend: number;
}

const BLOBS: Blob[] = [
  { region: REGION.farmland, x: 600, z: 1700, r: 1700, blend: 700 },
  { region: REGION.farmland, x: 2200, z: 1500, r: 1100, blend: 600 },
  { region: REGION.forest, x: -800, z: -1700, r: 1500, blend: 700 },
  { region: REGION.forest, x: 1900, z: -1700, r: 1100, blend: 600 },
  { region: REGION.desert, x: -3150, z: 700, r: 1350, blend: 600 },
  { region: REGION.rally, x: -2500, z: -1100, r: 900, blend: 500 },
];

const segOut = { d: 0, t: 0 };
/** Distance from (px, pz) to segment ab; the result object is reused between calls. */
function segDist(px: number, pz: number, a: Pt, b: Pt): { d: number; t: number } {
  const dx = b.x - a.x;
  const dz = b.z - a.z;
  const l2 = dx * dx + dz * dz;
  let t = l2 > 0 ? ((px - a.x) * dx + (pz - a.z) * dz) / l2 : 0;
  t = clamp(t, 0, 1);
  const cx = a.x + dx * t;
  const cz = a.z + dz * t;
  segOut.d = Math.sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz));
  segOut.t = t;
  return segOut;
}

const polyOut = { d: 0, s: 0 };
/** Distance to a polyline, with the fractional index of the nearest point (result reused between calls). */
export function polylineDist(px: number, pz: number, pts: Pt[]): { d: number; s: number } {
  let best = Infinity;
  let bs = 0;
  for (let i = 0; i + 1 < pts.length; i++) {
    const r = segDist(px, pz, pts[i], pts[i + 1]);
    if (r.d < best) {
      best = r.d;
      bs = i + r.t;
    }
  }
  polyOut.d = best;
  polyOut.s = bs;
  return polyOut;
}

export interface TerrainSample {
  h: number;
  /** Dominant region and its weight, for surfaces and colors. */
  region: RegionId;
  /** Distance inland from the coastline (negative at sea), m. */
  coast: number;
  /** Water surface height here, or -Infinity where there is no lake/river. */
  water: number;
  /** 0..1 forest density hint for scenery. */
  forest: number;
  /** Desert weight 0..1. */
  desert: number;
}

export class Island {
  readonly seed: number;
  private nCoast: Noise2;
  private nBase: Noise2;
  private nDetail: Noise2;
  private nRidge: Noise2;
  private nWarp: Noise2;
  private nDesert: Noise2;

  constructor(seed = 1) {
    this.seed = seed;
    this.nCoast = new Noise2(`${seed}:coast`);
    this.nBase = new Noise2(`${seed}:base`);
    this.nDetail = new Noise2(`${seed}:detail`);
    this.nRidge = new Noise2(`${seed}:ridge`);
    this.nWarp = new Noise2(`${seed}:warp`);
    this.nDesert = new Noise2(`${seed}:desert`);
  }

  /** Approximate distance inland from the coast, m (negative offshore). */
  coastDistance(x: number, z: number): number {
    const r = Math.sqrt(x * x + z * z);
    const a = datan2(z, x);
    const cx = dcos(a);
    const sz = dsin(a);
    // Base radius with a warped noise outline, a long eastern peninsula for
    // the city and harbor, a broad southern shore for the festival and a
    // western bulge for the desert.
    let R = 4250 + 520 * this.nCoast.fbm(cx * 1.3 + 3.1, sz * 1.3 - 1.7, 4) + 190 * this.nCoast.fbm(cx * 4.5 + 9, sz * 4.5 + 2, 3);
    // A bay on the south-west shore and a cove in the north-east.
    R -= 420 * Math.max(0, 1 - Math.sqrt((cx + 0.55) * (cx + 0.55) + (sz - 0.83) * (sz - 0.83)) * 3.2);
    R -= 300 * Math.max(0, 1 - Math.sqrt((cx - 0.62) * (cx - 0.62) + (sz + 0.78) * (sz + 0.78)) * 3.5);
    R += 420 * Math.max(0, cx) * Math.max(0, cx) * Math.max(0, 1 - Math.abs(sz) * 1.4);
    R += 300 * Math.max(0, sz) * Math.max(0, 1 - Math.abs(cx) * 1.2);
    R += 260 * Math.max(0, -cx) * Math.max(0, 1 - Math.abs(sz + 0.1) * 1.6);
    // A low headland carries the proving ground's airfield out to sea: the
    // union with a rounded rectangle around the facility guarantees at least
    // HEADLAND m of land beyond its edges.
    const pg = PLATEAUS[1];
    const hx = Math.max(pg.x0 - x, 0, x - pg.x1);
    const hz = Math.max(pg.z0 - z, 0, z - pg.z1);
    const head = HEADLAND + 90 * this.nCoast.fbm(x / 700 + 5, z / 700 - 2, 3) - Math.sqrt(hx * hx + hz * hz);
    const land = smax(R - r, head, 220);
    // Keep the corners of the square map as open sea.
    return Math.min(land, WORLD_HALF - 300 - Math.max(Math.abs(x), Math.abs(z)));
  }

  /** Height of the northern range at (x, z): crest profile times a falloff across it. */
  private mountains(x: number, z: number): number {
    let best = Infinity;
    let crest = 0;
    for (let i = 0; i + 1 < RIDGE.length; i++) {
      const a = RIDGE[i];
      const b = RIDGE[i + 1];
      const r = segDist(x, z, a, b);
      if (r.d < best) {
        best = r.d;
        crest = lerp(a.h, b.h, r.t);
      }
    }
    // Warp the falloff so the range has spurs and valleys.
    const warp = 1 + 0.35 * this.nWarp.fbm(x / 900, z / 900, 3);
    const w = best / (1350 * warp);
    const shape = dexp(-w * w * 1.6);
    if (shape < 0.002) return 0;
    // Eroded fBm carves drainage valleys into the flanks and leaves a few
    // dominant peaks; a light ridged term sharpens the crest lines.
    const e = this.nRidge.eroded(x / 1700 + 3.7, z / 1700 - 1.3, 7);
    const r = this.nDetail.ridged(x / 520, z / 520, 3);
    const raw = 0.64 + 1.15 * e + 0.12 * (r - 0.4);
    // Compress the top end so single noise maxima do not become needles.
    const relief = raw > 1 ? 1 + (1 - dexp(-(raw - 1) * 2.5)) * 0.22 : Math.max(0.22, raw);
    return shape * crest * relief + shape * 14 * this.nDetail.fbm(x / 140, z / 140, 2);
  }

  private plains(x: number, z: number): number {
    return 34 + 26 * this.nBase.fbm(x / 1300, z / 1300, 4) + 7 * this.nDetail.fbm(x / 300, z / 300, 3);
  }

  private farmland(x: number, z: number): number {
    return 26 + 18 * this.nBase.fbm(x / 1100 + 4, z / 1100, 4) + 4 * this.nDetail.fbm(x / 240, z / 240, 2);
  }

  private forest(x: number, z: number): number {
    return 120 + 105 * this.nBase.fbm(x / 800 - 7, z / 800, 5) + 14 * this.nDetail.fbm(x / 160, z / 160, 3);
  }

  private rally(x: number, z: number): number {
    return 150 + 120 * this.nBase.fbm(x / 520 + 11, z / 520 - 3, 5) + 18 * this.nDetail.fbm(x / 120, z / 120, 3);
  }

  /** Mesas: terraced noise with steep steps, cut by the canyon. */
  private desert(x: number, z: number): number {
    const n = 0.5 + 0.5 * this.nDesert.fbm(x / 900, z / 900, 4);
    const steps = 4;
    const v = n * steps;
    const fl = Math.floor(v);
    const fr = v - fl;
    // Mostly flat terraces joined by steep risers.
    const terraced = (fl + smoothstep(0.78, 0.96, fr)) / steps;
    let h = 70 + 170 * terraced + 6 * this.nDetail.fbm(x / 200, z / 200, 3);
    const c = polylineDist(x, z, CANYON);
    const cut = smoothstep(150, 30, c.d + 25 * this.nWarp.fbm(x / 120, z / 120, 2));
    h = lerp(h, 46 + 4 * this.nDetail.fbm(x / 60, z / 60, 2), cut);
    return h;
  }

  /** Weight of an authored region at the (already warped) point (wx, wz). */
  private blobWeight(b: Blob, wx: number, wz: number): number {
    const d = Math.sqrt((wx - b.x) * (wx - b.x) + (wz - b.z) * (wz - b.z));
    return smoothstep(b.r + b.blend, b.r - b.blend * 0.3, d);
  }

  private plateauWeight(p: Plateau, x: number, z: number): number {
    const dx = Math.max(p.x0 - x, 0, x - p.x1);
    const dz = Math.max(p.z0 - z, 0, z - p.z1);
    const d = Math.sqrt(dx * dx + dz * dz);
    return smoothstep(p.blend, 0, d);
  }

  /** Full base terrain sample (no roads). */
  sample(x: number, z: number, out: TerrainSample): TerrainSample {
    const coast = this.coastDistance(x, z);
    out.coast = coast;
    out.water = -Infinity;
    out.forest = 0;
    out.desert = 0;
    if (coast < -700) {
      out.h = -38 + 6 * this.nBase.fbm(x / 600, z / 600, 2);
      out.region = REGION.beach;
      return out;
    }
    // Blend the regional landforms.
    let wSum = 0.15;
    let h = this.plains(x, z) * 0.15;
    let bestW = 0.15;
    let region: RegionId = REGION.plains;
    // One domain warp shared by every region outline.
    const wx = x + 260 * this.nWarp.fbm(x / 1500, z / 1500, 3);
    const wz = z + 260 * this.nWarp.fbm(x / 1500 + 7, z / 1500 - 3, 3);
    for (const b of BLOBS) {
      const w = this.blobWeight(b, wx, wz);
      if (w <= 0) continue;
      let hb: number;
      switch (b.region) {
        case REGION.farmland:
          hb = this.farmland(x, z);
          break;
        case REGION.forest:
          hb = this.forest(x, z);
          out.forest = Math.max(out.forest, w);
          break;
        case REGION.desert:
          hb = this.desert(x, z);
          out.desert = Math.max(out.desert, w);
          break;
        case REGION.rally:
          hb = this.rally(x, z);
          out.forest = Math.max(out.forest, w * 0.8);
          break;
        default:
          hb = this.plains(x, z);
      }
      h += hb * w;
      wSum += w;
      if (w > bestW) {
        bestW = w;
        region = b.region;
      }
    }
    h /= wSum;
    // The northern range rises out of whatever lies below it.
    const m = this.mountains(x, z);
    if (m > h * 0.6) {
      const t = smoothstep(h * 0.6, h * 0.6 + 120, m);
      h = lerp(h, Math.max(h, m), t);
      if (t > 0.5 && m > 260) region = REGION.mountains;
      out.forest = Math.max(out.forest, (1 - smoothstep(380, 620, h)) * t * 0.9);
    }
    // Lake basin and river channel.
    const dl = Math.sqrt((x - LAKE.x) * (x - LAKE.x) + (z - LAKE.z) * (z - LAKE.z));
    const lakeEdge = LAKE.r + 40 * this.nWarp.fbm(x / 200, z / 200, 2);
    if (dl < lakeEdge + 450) {
      const shore = smoothstep(lakeEdge + 450, lakeEdge, dl);
      h = lerp(h, Math.min(h, LAKE.level + 6 + (dl - lakeEdge) * 0.04), shore);
      if (dl < lakeEdge) {
        h = Math.min(h, LAKE.level - 1 - 14 * smoothstep(lakeEdge, lakeEdge * 0.3, dl));
        out.water = LAKE.level;
        region = REGION.lake;
      }
    }
    const rv = polylineDist(x, z, RIVER);
    if (rv.d < 260) {
      // River level falls from the lake to the sea along the channel.
      const frac = rv.s / (RIVER.length - 1);
      const level = lerp(LAKE.level - 2, 0.5, smoothstep(0, 1, frac));
      const bank = smoothstep(260, 40, rv.d);
      h = lerp(h, Math.min(h, level + 3 + rv.d * 0.05), bank);
      if (rv.d < 26) {
        h = Math.min(h, level - 2.2 * smoothstep(26, 4, rv.d) - 0.4);
        out.water = Math.max(out.water, level);
      }
    }
    // Coast: low land runs down to beaches; high land meets the sea as cliffs.
    if (coast < 600) {
      const beachH = Math.max(-1.5, coast * 0.012) + 1.5;
      const t = smoothstep(-40, 600, coast);
      const cliff = smoothstep(80, 200, h);
      const shaped = lerp(beachH, h, Math.max(t * t, cliff * smoothstep(-60, 120, coast)));
      h = coast < 0 ? Math.min(shaped, lerp(beachH, -38, smoothstep(0, -700, coast))) : shaped;
      if (coast < 140 && h < 8) region = REGION.beach;
    }
    // Authored flat platforms win over everything, fading out towards the shore.
    for (const p of PLATEAUS) {
      const w = this.plateauWeight(p, x, z) * smoothstep(0, 120, coast);
      if (w > 0) {
        h = lerp(h, p.height, w);
        if (w > 0.6) region = p.region;
      }
    }
    out.h = h;
    out.region = region;
    return out;
  }

  /** Base height only. */
  height(x: number, z: number): number {
    return this.sample(x, z, scratch).h;
  }
}

const scratch: TerrainSample = { h: 0, region: REGION.plains, coast: 0, water: -Infinity, forest: 0, desert: 0 };

export function newSample(): TerrainSample {
  return { h: 0, region: REGION.plains, coast: 0, water: -Infinity, forest: 0, desert: 0 };
}
