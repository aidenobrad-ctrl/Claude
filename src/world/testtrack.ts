// Halcyon Proving Ground: a flat test facility with a 3.6 km circuit, a
// skidpad and a paddock. Used for the driving slice (M1) and, later, as the
// airfield test track on the island.
import { applyBumps, type Ground, type GroundHit } from './ground';
import { Road, newNearest, sampleSpline, type NearestResult } from './road';
import { Colliders } from './colliders';
import { SURFACE, type SurfaceId } from './surfaces';

export interface Rect {
  x0: number;
  z0: number;
  x1: number;
  z1: number;
}

export interface TrackLayout {
  road: Road;
  skidpad: { x: number; z: number; r: number; width: number };
  paddock: Rect;
  spawn: { x: number; z: number; yaw: number };
  colliders: Colliders;
  /** Arc length of the start/finish line on the circuit. */
  startS: number;
  /** Curbs and run-off per road sample: 0 none, 1 curb, 2 curb + gravel trap. */
  cornerKind: Uint8Array;
  /** Which side the gravel trap is on (+1 right, -1 left). */
  outside: Int8Array;
}

export const TRACK_HALF_WIDTH = 6;
export const CURB_WIDTH = 1.2;
export const GRAVEL_WIDTH = 13;

const CIRCUIT: [number, number][] = [
  [-700, 0], [-300, 0], [100, 0], [500, 0],
  [620, 30], [660, 130], [640, 260], [680, 380], [650, 470], [560, 500], [480, 450],
  [420, 360], [330, 330], [240, 400], [150, 460], [40, 430], [-60, 360], [-170, 380],
  [-280, 470], [-420, 480], [-560, 420], [-640, 300], [-700, 180], [-760, 80], [-740, 10],
];

export function buildTestTrack(): TrackLayout {
  const pts = sampleSpline(CIRCUIT.map(([x, z]) => ({ x, y: 0, z })), true, 2);
  const road = new Road(pts, { closed: true, halfWidth: TRACK_HALF_WIDTH });
  const n = road.n;
  const cornerKind = new Uint8Array(n);
  const outside = new Int8Array(n);
  // Smooth curvature over ±10 m so curbs run the length of each corner.
  for (let i = 0; i < n; i++) {
    let k = 0;
    for (let d = -5; d <= 5; d++) k += road.curvature[(i + d + n) % n];
    k /= 11;
    const ak = Math.abs(k);
    cornerKind[i] = ak > 1 / 70 ? 2 : ak > 1 / 220 ? 1 : 0;
    // Turning right (k > 0) throws the car to the left, so run-off goes left.
    outside[i] = k > 0 ? -1 : 1;
  }
  const colliders = new Colliders();
  // Tire walls behind the gravel traps.
  let run: { x: number; z: number }[] = [];
  const flush = (): void => {
    if (run.length > 1) colliders.addPolyline(run, { top: 1.1, bottom: -1, bounce: 0.35, friction: 0.5, kind: 'tires' });
    run = [];
  };
  for (let i = 0; i < n; i++) {
    if (cornerKind[i] === 2) {
      const off = (TRACK_HALF_WIDTH + CURB_WIDTH + GRAVEL_WIDTH + 1.5) * outside[i];
      // Right normal is (-tz, tx).
      run.push({ x: road.x[i] - road.tz[i] * off, z: road.z[i] + road.tx[i] * off });
      if (i > 0 && cornerKind[i - 1] === 2 && outside[i - 1] !== outside[i]) {
        flush();
      }
    } else flush();
  }
  flush();
  // Armco along both sides of the main straight.
  for (const side of [-1, 1]) {
    const line: { x: number; z: number }[] = [];
    for (let x = -640; x <= 460; x += 20) line.push({ x, z: side * (TRACK_HALF_WIDTH + 9) });
    colliders.addPolyline(line, { top: 0.8, bottom: -1, bounce: 0.25, friction: 0.35, kind: 'rail' });
  }
  const startS = nearestS(road, -400, 0);
  return {
    road,
    skidpad: { x: -150, z: 200, r: 40, width: 14 },
    paddock: { x0: -650, z0: -95, x1: -350, z1: -30 },
    spawn: { x: -470, z: -60, yaw: -Math.PI / 2 },
    colliders,
    startS,
    cornerKind,
    outside,
  };
}

function nearestS(road: Road, x: number, z: number): number {
  const r = newNearest();
  road.nearest(x, z, r);
  return r.s;
}

/** Ground for the proving ground: flat, with surfaces from the layout. */
export class TestTrackGround implements Ground {
  private near: NearestResult = newNearest();
  constructor(readonly t: TrackLayout) {}

  surfaceAt(x: number, z: number): SurfaceId {
    const t = this.t;
    const pd = t.paddock;
    if (x >= pd.x0 && x <= pd.x1 && z >= pd.z0 && z <= pd.z1) return SURFACE.asphalt;
    const sk = t.skidpad;
    const dr = Math.sqrt((x - sk.x) * (x - sk.x) + (z - sk.z) * (z - sk.z));
    if (Math.abs(dr - sk.r) <= sk.width / 2) return SURFACE.asphalt;
    if (dr < sk.r - sk.width / 2 && dr > sk.r - sk.width / 2 - 3) return SURFACE.concrete;
    const near = this.near;
    if (!t.road.nearest(x, z, near)) return SURFACE.grass;
    const lat = near.lateral;
    const a = Math.abs(lat);
    if (a <= TRACK_HALF_WIDTH) return SURFACE.asphalt;
    const i = near.i;
    const kind = t.cornerKind[i];
    if (kind > 0 && a <= TRACK_HALF_WIDTH + CURB_WIDTH) return SURFACE.curb;
    if (kind === 2 && Math.sign(lat) === t.outside[i] && a <= TRACK_HALF_WIDTH + CURB_WIDTH + GRAVEL_WIDTH) return SURFACE.gravel;
    // Paved shoulder on the straight.
    if (kind === 0 && a <= TRACK_HALF_WIDTH + 2 && Math.abs(z) < 20 && x > -720 && x < 520) return SURFACE.concrete;
    return SURFACE.grass;
  }

  sample(x: number, z: number, _yRef: number, out: GroundHit): boolean {
    out.y = 0;
    out.nx = 0;
    out.ny = 1;
    out.nz = 0;
    out.water = 0;
    out.surface = this.surfaceAt(x, z);
    applyBumps(x, z, out);
    return true;
  }
}
