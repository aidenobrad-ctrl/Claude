// Parametric car model generator. A body is lofted through cross-sections
// placed along the car; each section is a rounded shape whose height, width
// and shoulder follow side and plan profiles for the body style. Wheel
// arches are cut into the sections' outer lower corners. A separate loft
// makes the glasshouse. Lights, grille, mirrors, spoilers, exhausts, plates
// and detailed wheels are added on top.
import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import type { CarSpec } from '../spec';
import type { VehicleParams } from '../params';

export interface BodyProfile {
  /** Stations as fractions of body length (0 = front bumper). */
  wsBase: number;
  wsTop: number;
  roofEnd: number;
  rwBase: number;
  /** Heights as fractions of overall height. */
  noseTop: number;
  hoodTop: number;
  belt: number;
  deckTop: number;
  tailTop: number;
  /** Bumper bottom and sill heights above the ground, m. */
  bumperBottom: number;
  sill: number;
  /** Roof width relative to the beltline width. */
  tumblehome: number;
  /** Half-width at the nose and tail relative to the maximum. */
  noseW: number;
  tailW: number;
  /** Fender bulge over the wheels, m. */
  flare: number;
  spoiler: 'none' | 'lip' | 'ducktail' | 'wing';
  /** Extra length of the front/rear overhang share (0..1, 0.5 = even). */
  frontShare: number;
  /** Open bed (pickup) from this station to the tail, or 0. */
  bedFrom: number;
}

export const PROFILES: Record<string, BodyProfile> = {
  coupe: {
    wsBase: 0.36, wsTop: 0.5, roofEnd: 0.64, rwBase: 0.86,
    noseTop: 0.43, hoodTop: 0.62, belt: 0.66, deckTop: 0.73, tailTop: 0.7,
    bumperBottom: 0.17, sill: 0.25, tumblehome: 0.78, noseW: 0.86, tailW: 0.9, flare: 0.04,
    spoiler: 'lip', frontShare: 0.45, bedFrom: 0,
  },
  sedan: {
    wsBase: 0.33, wsTop: 0.46, roofEnd: 0.7, rwBase: 0.82,
    noseTop: 0.47, hoodTop: 0.6, belt: 0.64, deckTop: 0.7, tailTop: 0.68,
    bumperBottom: 0.2, sill: 0.28, tumblehome: 0.82, noseW: 0.88, tailW: 0.9, flare: 0.025,
    spoiler: 'none', frontShare: 0.45, bedFrom: 0,
  },
  hatch: {
    wsBase: 0.3, wsTop: 0.45, roofEnd: 0.84, rwBase: 0.96,
    noseTop: 0.45, hoodTop: 0.56, belt: 0.6, deckTop: 0.66, tailTop: 0.62,
    bumperBottom: 0.2, sill: 0.28, tumblehome: 0.84, noseW: 0.88, tailW: 0.93, flare: 0.03,
    spoiler: 'lip', frontShare: 0.52, bedFrom: 0,
  },
  wagon: {
    wsBase: 0.31, wsTop: 0.44, roofEnd: 0.9, rwBase: 0.97,
    noseTop: 0.46, hoodTop: 0.58, belt: 0.62, deckTop: 0.68, tailTop: 0.66,
    bumperBottom: 0.2, sill: 0.28, tumblehome: 0.86, noseW: 0.88, tailW: 0.94, flare: 0.025,
    spoiler: 'none', frontShare: 0.45, bedFrom: 0,
  },
  suv: {
    wsBase: 0.3, wsTop: 0.43, roofEnd: 0.88, rwBase: 0.97,
    noseTop: 0.52, hoodTop: 0.62, belt: 0.64, deckTop: 0.7, tailTop: 0.68,
    bumperBottom: 0.32, sill: 0.42, tumblehome: 0.87, noseW: 0.9, tailW: 0.95, flare: 0.05,
    spoiler: 'lip', frontShare: 0.47, bedFrom: 0,
  },
  pickup: {
    wsBase: 0.28, wsTop: 0.39, roofEnd: 0.5, rwBase: 0.53,
    noseTop: 0.55, hoodTop: 0.64, belt: 0.66, deckTop: 0.66, tailTop: 0.66,
    bumperBottom: 0.36, sill: 0.46, tumblehome: 0.9, noseW: 0.92, tailW: 0.98, flare: 0.06,
    spoiler: 'none', frontShare: 0.42, bedFrom: 0.56,
  },
  midengine: {
    wsBase: 0.26, wsTop: 0.44, roofEnd: 0.56, rwBase: 0.8,
    noseTop: 0.36, hoodTop: 0.5, belt: 0.6, deckTop: 0.74, tailTop: 0.72,
    bumperBottom: 0.13, sill: 0.2, tumblehome: 0.7, noseW: 0.8, tailW: 0.94, flare: 0.06,
    spoiler: 'wing', frontShare: 0.5, bedFrom: 0,
  },
  roadster: {
    wsBase: 0.38, wsTop: 0.47, roofEnd: 0.49, rwBase: 0.6,
    noseTop: 0.45, hoodTop: 0.64, belt: 0.7, deckTop: 0.74, tailTop: 0.72,
    bumperBottom: 0.17, sill: 0.25, tumblehome: 0.8, noseW: 0.86, tailW: 0.9, flare: 0.04,
    spoiler: 'ducktail', frontShare: 0.45, bedFrom: 0,
  },
};

export interface WheelParts {
  /** At the wheel center; rotate about Y to steer. */
  pivot: THREE.Group;
  /** Child of pivot; rotate about X to spin. */
  spin: THREE.Group;
}

export interface CarModel {
  root: THREE.Group;
  body: THREE.Group;
  wheels: WheelParts[];
  paint: THREE.MeshPhysicalMaterial;
  brakeLights: THREE.MeshStandardMaterial;
  headLights: THREE.MeshStandardMaterial;
  reverseLights: THREE.MeshStandardMaterial;
  /** Offset from body origin (COM) to the ground plane at rest, m. */
  groundY: number;
}

interface Dim {
  L: number;
  W: number;
  H: number;
  zf: number;
  zr: number;
  aF: number;
  aR: number;
  r: number;
  trackF: number;
  trackR: number;
  tireWF: number;
  tireWR: number;
}

const smooth = (a: number, b: number, t: number): number => {
  const x = Math.min(1, Math.max(0, (t - a) / (b - a)));
  return x * x * (3 - 2 * x);
};

/** Piecewise smooth interpolation through (t, v) keys. */
function curve(keys: [number, number][], t: number): number {
  if (t <= keys[0][0]) return keys[0][1];
  for (let i = 1; i < keys.length; i++) {
    if (t <= keys[i][0]) {
      const [t0, v0] = keys[i - 1];
      const [t1, v1] = keys[i];
      const u = (t - t0) / (t1 - t0);
      const s = u * u * (3 - 2 * u);
      return v0 + (v1 - v0) * s;
    }
  }
  return keys[keys.length - 1][1];
}

/**
 * Build a lofted surface from sections (each an array of [x, y]) at z
 * positions. Sections must run clockwise when viewed from behind (+Z) so
 * faces point outward. `group(k, j)` assigns each quad a material index.
 */
function loft(sections: [number, number][][], zs: number[], closeEnds: boolean, group?: (k: number, j: number) => number): THREE.BufferGeometry {
  const m = sections[0].length;
  const pos: number[] = [];
  const uv: number[] = [];
  const idx: number[] = [];
  for (let k = 0; k < sections.length; k++) {
    for (let j = 0; j < m; j++) {
      pos.push(sections[k][j][0], sections[k][j][1], zs[k]);
      uv.push(j / (m - 1), k / (sections.length - 1));
    }
  }
  const byGroup: number[][] = [[], [], [], []];
  for (let k = 0; k + 1 < sections.length; k++) {
    for (let j = 0; j + 1 < m; j++) {
      const a = k * m + j;
      const b = a + 1;
      const c = a + m;
      const d = c + 1;
      const gi = group ? group(k, j) : 0;
      byGroup[gi].push(a, c, b, b, c, d);
    }
  }
  for (const list of byGroup) idx.push(...list);
  if (closeEnds) {
    for (const k of [0, sections.length - 1]) {
      const center = pos.length / 3;
      let cx = 0;
      let cy = 0;
      for (const p of sections[k]) {
        cx += p[0];
        cy += p[1];
      }
      pos.push(cx / m, cy / m, zs[k]);
      uv.push(0.5, k === 0 ? 0 : 1);
      for (let j = 0; j + 1 < m; j++) {
        const a = k * m + j;
        if (k === 0) idx.push(center, a, a + 1);
        else idx.push(center, a + 1, a);
      }
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx);
  let start = 0;
  byGroup.forEach((list, gi) => {
    if (list.length) g.addGroup(start, list.length, gi);
    start += list.length;
  });
  // End caps go into group 0.
  if (idx.length > start) g.addGroup(start, idx.length - start, 0);
  g.computeVertexNormals();
  return g;
}

function dims(spec: CarSpec, p: VehicleParams, prof: BodyProfile): Dim {
  const over = spec.length - spec.wheelbase;
  const aF = -p.a;
  const aR = p.b;
  const zf = aF - over * prof.frontShare;
  const zr = aR + over * (1 - prof.frontShare);
  return {
    L: zr - zf,
    W: spec.width,
    H: spec.height,
    zf,
    zr,
    aF,
    aR,
    r: spec.tires.radius,
    trackF: spec.trackFront,
    trackR: spec.trackRear,
    tireWF: spec.tires.widthF / 1000,
    tireWR: spec.tires.widthR / 1000,
  };
}

/** Lower body: nose to tail, sill to hood/deck line, with wheel arches. */
function lowerBody(d: Dim, prof: BodyProfile, lod: number): THREE.BufferGeometry {
  const H = d.H;
  const stations = lod === 0 ? 64 : lod === 1 ? 32 : 16;
  const halfW = d.W / 2;
  const archR = d.r + 0.05;
  const sections: [number, number][][] = [];
  const zs: number[] = [];
  for (let k = 0; k <= stations; k++) {
    // Denser stations at the ends where the shape changes fastest.
    const u = k / stations;
    const t = 0.5 - 0.5 * Math.cos(u * Math.PI);
    const z = d.zf + t * d.L;
    const topKeys: [number, number][] = [
      [0, prof.noseTop * 0.82],
      [0.025, prof.noseTop],
      [0.09, (prof.noseTop + prof.hoodTop) / 2 + 0.02],
      [prof.wsBase, prof.hoodTop],
      [prof.rwBase, prof.deckTop],
      [0.975, prof.tailTop],
      [1, prof.tailTop * 0.85],
    ];
    let yTop = curve(topKeys, t) * H;
    if (prof.bedFrom > 0 && t > prof.bedFrom) yTop = prof.deckTop * H;
    // Plan view: taper at both ends, full width in the middle.
    const wEnd = curve([[0, prof.noseW * 0.92], [0.06, prof.noseW], [0.2, 0.98], [0.5, 1], [0.82, 0.99], [0.95, prof.tailW], [1, prof.tailW * 0.94]], t);
    // Fender flares over the wheels.
    const dzF = Math.abs(z - d.aF);
    const dzR = Math.abs(z - d.aR);
    const flare = prof.flare * (Math.max(0, 1 - dzF / (archR * 1.6)) + Math.max(0, 1 - dzR / (archR * 1.6)));
    const endRound0 = Math.max(1 - smooth(0, 0.045, t), 1 - smooth(1, 0.955, t));
    const w = (halfW * wEnd - prof.flare + flare) * (1 - 0.1 * endRound0);
    // Wheel arch opening height at this station.
    let arch = 0;
    if (dzF < archR) arch = Math.max(arch, d.r + Math.sqrt(archR * archR - dzF * dzF));
    if (dzR < archR) arch = Math.max(arch, d.r + Math.sqrt(archR * archR - dzR * dzR));
    const endBlend = smooth(0, 0.06, t) * smooth(1, 0.94, t);
    // Round the bumpers: the last few centimeters pull in toward the middle.
    const endRound = Math.max(1 - smooth(0, 0.045, t), 1 - smooth(1, 0.955, t));
    const yBot = prof.bumperBottom + (prof.sill - prof.bumperBottom) * endBlend + 0.05 * endRound;
    yTop -= (yTop - yBot) * 0.16 * endRound;
    const yFloor = Math.min(yBot, prof.sill) - 0.02;
    const yLow = arch > 0 ? Math.max(yBot, arch) : yBot;
    const yBelt = yTop;
    const wIn = Math.min(d.trackF, d.trackR) / 2 - Math.max(d.tireWF, d.tireWR) / 2 - 0.06;
    const crown = 0.035 + 0.02 * endBlend;
    const shoulder = yBelt - Math.min(0.09, (yBelt - yLow) * 0.3);
    // Half section from bottom center around to top center.
    const half: [number, number][] = [
      [0, yFloor],
      [wIn, yFloor],
      [wIn + 0.001, yLow - 0.001],
      [w - 0.05, yLow],
      [w - 0.01, yLow + 0.04],
      [w, yLow + (shoulder - yLow) * 0.35],
      [w + 0.01, yLow + (shoulder - yLow) * 0.7],
      [w - 0.01, shoulder],
      [w - 0.06, yBelt - crown * 0.4],
      [w * 0.6, yBelt - crown * 0.1],
      [0, yBelt],
    ];
    const full: [number, number][] = [];
    for (let j = 0; j < half.length; j++) full.push([-half[j][0], half[j][1]]);
    for (let j = half.length - 2; j >= 0; j--) full.push([half[j][0], half[j][1]]);
    // Loop runs bottom-center -> left -> top -> right -> bottom-center:
    // clockwise seen from behind, so faces point outward.
    sections.push(full);
    zs.push(z);
  }
  return loft(sections, zs, true);
}

/**
 * Glasshouse: one loft from the windshield base to the rear window base.
 * Quads are assigned to glass (0), paint (1) or black trim (2): painted
 * roof, A-pillars and C-pillar flanks, trim B-pillars on four-door styles.
 */
function cabin(d: Dim, prof: BodyProfile, lod: number, bPillar: boolean): THREE.BufferGeometry {
  const H = d.H;
  const n = lod === 0 ? 30 : 14;
  const sections: [number, number][][] = [];
  const zs: number[] = [];
  const ts: number[] = [];
  const t0 = prof.wsBase;
  const t1 = prof.rwBase;
  for (let k = 0; k <= n; k++) {
    const t = t0 + ((t1 - t0) * k) / n;
    const z = d.zf + t * d.L;
    const belt = curve([[t0, prof.hoodTop], [t1, prof.deckTop]], t) * H + 0.008;
    // Roof line: rises through the windshield, flat roof, falls through the rear glass.
    let top: number;
    if (t <= prof.wsTop) top = belt + (H - belt) * smooth(t0, prof.wsTop, t) * 0.999 + 0.001;
    else if (t <= prof.roofEnd) top = H;
    else top = belt + (H - belt) * (1 - smooth(prof.roofEnd, t1, t)) * 0.999 + 0.001;
    const wb = (d.W / 2) * 0.92;
    const wt = wb * prof.tumblehome;
    const h = top - belt;
    sections.push([
      [-wb, belt],
      [-wb + (wb - wt) * 0.55, belt + h * 0.62],
      [-wt, top - h * 0.12],
      [-wt * 0.6, top],
      [0, top + 0.012],
      [wt * 0.6, top],
      [wt, top - h * 0.12],
      [wb - (wb - wt) * 0.55, belt + h * 0.62],
      [wb, belt],
    ]);
    zs.push(z);
    ts.push(t);
  }
  const bMid = (prof.wsTop + prof.roofEnd) / 2;
  const fastback = prof.rwBase - prof.roofEnd > 0.12;
  return loft(sections, zs, false, (k, j) => {
    const t = (ts[k] + ts[k + 1]) / 2;
    const side = j <= 1 || j >= 6;
    if (t < prof.wsTop) return side ? 1 : 0; // windshield, painted A-pillars at its sides
    if (t <= prof.roofEnd) {
      if (!side) return 1; // roof
      if (bPillar && Math.abs(t - bMid) < 0.012) return 2;
      return 0; // side windows
    }
    // Rear slope: glass in the middle; a fastback's flanks are bodywork.
    if (side) return fastback ? 1 : 0;
    return 0;
  });
}

/** Lower-body surface at station t: top height, half-width and sill height (model space). */
interface BodySlice {
  z: number;
  yTop: number;
  w: number;
  yBot: number;
}

function bodyAt(d: Dim, prof: BodyProfile, t: number): BodySlice {
  const H = d.H;
  const z = d.zf + t * d.L;
  let yTop = curve(
    [
      [0, prof.noseTop * 0.82],
      [0.025, prof.noseTop],
      [0.09, (prof.noseTop + prof.hoodTop) / 2 + 0.02],
      [prof.wsBase, prof.hoodTop],
      [prof.rwBase, prof.deckTop],
      [0.975, prof.tailTop],
      [1, prof.tailTop * 0.85],
    ],
    t,
  ) * H;
  if (prof.bedFrom > 0 && t > prof.bedFrom) yTop = prof.deckTop * H;
  const wEnd = curve([[0, prof.noseW * 0.92], [0.06, prof.noseW], [0.2, 0.98], [0.5, 1], [0.82, 0.99], [0.95, prof.tailW], [1, prof.tailW * 0.94]], t);
  const w = (d.W / 2) * wEnd - prof.flare;
  const endBlend = smooth(0, 0.06, t) * smooth(1, 0.94, t);
  const yBot = prof.bumperBottom + (prof.sill - prof.bumperBottom) * endBlend;
  return { z, yTop, w, yBot };
}

function wheel(radius: number, width: number, spokes: number, lod: number, rimMat: THREE.Material, tireMat: THREE.Material, discMat: THREE.Material): THREE.Group {
  const g = new THREE.Group();
  const seg = lod === 0 ? 28 : 14;
  // Tire: lathe of a rounded rectangle profile, axis along X.
  const rr = radius;
  const ri = radius * 0.66;
  const hw = width / 2;
  const prof: THREE.Vector2[] = [];
  const steps = 6;
  for (let i = 0; i <= steps; i++) {
    const a = (i / steps) * Math.PI;
    // Profile in (radius, axial) space: bulging sidewall.
    prof.push(new THREE.Vector2(ri + (rr - ri) * (0.5 - 0.5 * Math.cos(a)), -hw * 0.92 + Math.sin(a) * 0.0));
  }
  const tireProfile = [
    new THREE.Vector2(ri, -hw * 0.9),
    new THREE.Vector2(rr * 0.93, -hw),
    new THREE.Vector2(rr, -hw * 0.82),
    new THREE.Vector2(rr, hw * 0.82),
    new THREE.Vector2(rr * 0.93, hw),
    new THREE.Vector2(ri, hw * 0.9),
  ];
  void prof;
  const tireGeo = new THREE.LatheGeometry(tireProfile.map((v) => new THREE.Vector2(v.x, v.y)), seg);
  // Lathe revolves around Y; rotate so the axle is X.
  tireGeo.rotateZ(Math.PI / 2);
  const tire = new THREE.Mesh(tireGeo, tireMat);
  tire.castShadow = true;
  g.add(tire);
  // Rim barrel and face.
  const barrel = new THREE.CylinderGeometry(ri * 0.98, ri * 0.98, width * 0.82, seg, 1, true).rotateZ(Math.PI / 2);
  g.add(new THREE.Mesh(barrel, rimMat));
  const faceX = hw * 0.62;
  const lip = new THREE.TorusGeometry(ri * 0.96, ri * 0.045, 6, seg).rotateY(Math.PI / 2).translate(faceX, 0, 0);
  const hub = new THREE.CylinderGeometry(ri * 0.2, ri * 0.24, 0.05, 12).rotateZ(Math.PI / 2).translate(faceX - 0.01, 0, 0);
  const spokeGeos: THREE.BufferGeometry[] = [lip, hub];
  if (lod <= 1) {
    for (let i = 0; i < spokes; i++) {
      const a = (i / spokes) * Math.PI * 2;
      const len = ri * 0.76;
      const s = new THREE.BoxGeometry(0.035, len, ri * (spokes > 8 ? 0.07 : 0.12));
      s.translate(0, ri * 0.2 + len / 2, 0);
      // Slight dish: spokes lean outward toward the lip.
      s.rotateZ(-0.08);
      s.rotateX(a);
      s.translate(faceX - 0.012, 0, 0);
      spokeGeos.push(s);
    }
  } else {
    spokeGeos.push(new THREE.CircleGeometry(ri * 0.92, 12).rotateY(Math.PI / 2).translate(faceX, 0, 0));
  }
  const rim = new THREE.Mesh(mergeGeometries(spokeGeos.map((x) => x.toNonIndexed())), rimMat);
  rim.castShadow = true;
  g.add(rim);
  // Brake disc inside the rim.
  if (lod === 0) {
    const disc = new THREE.CylinderGeometry(ri * 0.8, ri * 0.8, 0.028, seg).rotateZ(Math.PI / 2).translate(faceX - 0.07, 0, 0);
    g.add(new THREE.Mesh(disc, discMat));
  }
  return g;
}

export interface ModelOptions {
  paint?: number;
  lod?: 0 | 1 | 2;
  caliper?: number;
  rim?: number;
}

export function buildCarModel(spec: CarSpec, params: VehicleParams, opts: ModelOptions = {}): CarModel {
  const lod = opts.lod ?? 0;
  const prof = PROFILES[spec.visual.style] ?? PROFILES.coupe;
  const d = dims(spec, params, prof);
  const paint = new THREE.MeshPhysicalMaterial({
    color: opts.paint ?? spec.visual.paint,
    metalness: 0.25,
    roughness: 0.42,
    clearcoat: 1,
    clearcoatRoughness: 0.05,
    envMapIntensity: 0.85,
  });
  const black = new THREE.MeshStandardMaterial({ color: 0x0d0e10, roughness: 0.55, metalness: 0.2 });
  const trim = new THREE.MeshStandardMaterial({ color: 0x15171a, roughness: 0.35, metalness: 0.5 });
  // Dielectric glass: dark body, Fresnel reflections of the sky from the clearcoat layer.
  const glass = new THREE.MeshPhysicalMaterial({ color: 0x0a1016, roughness: 0.05, metalness: 0, clearcoat: 1, clearcoatRoughness: 0.02, envMapIntensity: 1.6 });
  const chrome = new THREE.MeshStandardMaterial({ color: 0xd8dde2, roughness: 0.12, metalness: 1 });
  const headLights = new THREE.MeshStandardMaterial({ color: 0xf4f6ff, emissive: 0xdfe8ff, emissiveIntensity: 0.6, roughness: 0.1, metalness: 0.3 });
  const brakeLights = new THREE.MeshStandardMaterial({ color: 0xa3120e, emissive: 0xff1a12, emissiveIntensity: 0.5, roughness: 0.2, metalness: 0.1 });
  const reverseLights = new THREE.MeshStandardMaterial({ color: 0xdedede, emissive: 0xffffff, emissiveIntensity: 0, roughness: 0.2 });
  const rimMat = new THREE.MeshStandardMaterial({ color: opts.rim ?? 0xb9bec6, roughness: 0.25, metalness: 0.9 });
  const tireMat = new THREE.MeshStandardMaterial({ color: 0x121214, roughness: 0.9, metalness: 0 });
  const discMat = new THREE.MeshStandardMaterial({ color: 0x6b6e73, roughness: 0.5, metalness: 0.8 });
  const caliperMat = new THREE.MeshStandardMaterial({ color: opts.caliper ?? 0xd92b1c, roughness: 0.4, metalness: 0.2 });

  const root = new THREE.Group();
  root.name = `car:${spec.id}`;
  const body = new THREE.Group();
  root.add(body);
  // Model space has the ground at y = 0; the body frame has the COM at 0.
  const groundY = -params.cgHeight;
  body.position.y = groundY;

  const lower = new THREE.Mesh(lowerBody(d, prof, lod), paint);
  lower.castShadow = true;
  lower.receiveShadow = true;
  body.add(lower);
  const fourDoor = spec.body === 'sedan' || spec.body === 'wagon' || spec.body === 'suv' || spec.body === 'pickup';
  const glassMesh = new THREE.Mesh(cabin(d, prof, lod, fourDoor), [glass, paint, trim]);
  glassMesh.castShadow = true;
  body.add(glassMesh);
  const H = d.H;

  // Details are stuck onto the generated body by raycasting, so they sit on
  // the real surface whatever the style: aligned to the surface normal and
  // pushed out by half their depth.
  const probe = new THREE.Mesh(lower.geometry);
  probe.updateMatrixWorld(true);
  const ray = new THREE.Raycaster();
  const hitN = new THREE.Vector3();
  const stick = (obj: THREE.Object3D, x: number, y: number, z: number, dir: THREE.Vector3, depth: number, spinZ = 0): boolean => {
    ray.set(new THREE.Vector3(x, y, z), dir.clone().normalize());
    const hit = ray.intersectObject(probe, false)[0];
    if (!hit || !hit.face) return false;
    hitN.copy(hit.face.normal).normalize();
    obj.position.copy(hit.point).addScaledVector(hitN, depth / 2 - 0.004);
    obj.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), hitN);
    if (spinZ) obj.rotateZ(spinZ);
    body.add(obj);
    return true;
  };
  const nose = bodyAt(d, prof, 0.012);
  const tail = bodyAt(d, prof, 0.992);
  const noseZ = d.zf;
  const tailZ = d.zr;
  const toBack = new THREE.Vector3(0, 0, 1);
  const toFront = new THREE.Vector3(0, 0, -1);
  const fromFront = new THREE.Vector3(0, -0.45, 1);
  const fromBack = new THREE.Vector3(0, -0.25, -1);
  const front = (x: number, y: number): [number, number, number] => [x, y, noseZ - 1];
  const back = (x: number, y: number): [number, number, number] => [x, y, tailZ + 1];
  const noseMid = nose.yBot + (nose.yTop - nose.yBot) * 0.5;
  const tailMid = tail.yBot + (tail.yTop - tail.yBot) * 0.5;

  // Front: intake, grille, running lights, headlights, plate.
  stick(new THREE.Mesh(new THREE.BoxGeometry(nose.w * 1.5, (nose.yTop - nose.yBot) * 0.24, 0.03), trim), ...front(0, nose.yBot + (nose.yTop - nose.yBot) * 0.16), toBack, 0.03);
  stick(new THREE.Mesh(new THREE.BoxGeometry(nose.w * 0.95, (nose.yTop - nose.yBot) * 0.26, 0.03), trim), ...front(0, noseMid), toBack, 0.03);
  for (const sd of [-1, 1]) {
    stick(new THREE.Mesh(new THREE.BoxGeometry(nose.w * 0.42, 0.04, 0.03), headLights), sd * nose.w * 0.62, nose.yTop + 0.6, noseZ - 0.9, fromFront, 0.03, 0);
    stick(new THREE.Mesh(new THREE.BoxGeometry(nose.w * 0.34, 0.016, 0.02), headLights), ...front(sd * nose.w * 0.6, noseMid + (nose.yTop - nose.yBot) * 0.12), toBack, 0.02);
  }
  const plateTex = plateTexture(spec);
  const plateMat = new THREE.MeshStandardMaterial({ map: plateTex, roughness: 0.5 });
  const plateGeo = new THREE.BoxGeometry(0.52, 0.12, 0.012);
  stick(new THREE.Mesh(plateGeo, plateMat), ...front(0, nose.yBot + (nose.yTop - nose.yBot) * 0.3), toBack, 0.012);
  const splitter = new THREE.Mesh(new THREE.BoxGeometry(nose.w * 1.7, 0.022, 0.12), black);
  splitter.position.set(0, nose.yBot + 0.03, noseZ + 0.1);
  body.add(splitter);

  // Rear: tail-light bar and clusters, reverse lights, valance, plate, diffuser, exhausts.
  stick(new THREE.Mesh(new THREE.BoxGeometry(tail.w * 1.6, 0.045, 0.03), brakeLights), 0, tail.yTop + 0.4, tailZ + 0.9, fromBack, 0.03);
  for (const sd of [-1, 1]) {
    stick(new THREE.Mesh(new THREE.BoxGeometry(tail.w * 0.34, 0.08, 0.03), brakeLights), sd * tail.w * 0.74, tail.yTop + 0.4, tailZ + 0.9, fromBack, 0.03);
    stick(new THREE.Mesh(new THREE.BoxGeometry(0.08, 0.035, 0.02), reverseLights), ...back(sd * tail.w * 0.4, tailMid - (tail.yTop - tail.yBot) * 0.05), toFront, 0.02);
  }
  stick(new THREE.Mesh(new THREE.BoxGeometry(tail.w * 1.8, (tail.yTop - tail.yBot) * 0.3, 0.03), trim), ...back(0, tail.yBot + (tail.yTop - tail.yBot) * 0.15), toFront, 0.03);
  stick(new THREE.Mesh(plateGeo, plateMat), ...back(0, tailMid - 0.02), toFront, 0.012);
  const diffuser = new THREE.Mesh(new THREE.BoxGeometry(tail.w * 1.15, 0.05, 0.3), black);
  diffuser.position.set(0, tail.yBot + 0.04, tailZ - 0.2);
  diffuser.rotation.x = 0.16;
  body.add(diffuser);
  if (lod === 0) {
    for (let i = -2; i <= 2; i++) {
      const fin = new THREE.Mesh(new THREE.BoxGeometry(0.015, 0.08, 0.28), black);
      fin.position.set(i * tail.w * 0.2, tail.yBot + 0.07, tailZ - 0.2);
      fin.rotation.x = 0.16;
      body.add(fin);
    }
  }
  const exhausts = spec.engine.cylinders >= 8 || spec.category === 'super' || spec.category === 'hyper' ? 4 : spec.engine.cylinders > 0 ? 2 : 0;
  for (let i = 0; i < exhausts; i++) {
    const side = i % 2 ? 1 : -1;
    const inner = exhausts === 4 && i >= 2;
    const ex = new THREE.Mesh(new THREE.CylinderGeometry(0.042, 0.042, 0.22, 12, 1, true), chrome);
    ex.rotation.x = Math.PI / 2;
    ex.position.set(side * (tail.w * 0.62 - (inner ? 0.1 : 0)), tail.yBot + 0.08, tailZ - 0.06);
    body.add(ex);
  }

  // Mirrors on short stalks at the base of the A-pillars.
  {
    const at = bodyAt(d, prof, prof.wsBase + 0.03);
    const beltY = prof.hoodTop * H + 0.02;
    for (const s of [-1, 1]) {
      const stalk = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.025, 0.05), black);
      stalk.position.set(s * (at.w * 0.94 + 0.04), beltY + 0.06, at.z + 0.08);
      body.add(stalk);
      const mirror = new THREE.Mesh(new THREE.BoxGeometry(0.15, 0.085, 0.1), paint);
      mirror.position.set(s * (at.w * 0.94 + 0.13), beltY + 0.08, at.z + 0.08);
      body.add(mirror);
      const glassM = new THREE.Mesh(new THREE.PlaneGeometry(0.12, 0.065), chrome);
      glassM.position.set(s * (at.w * 0.94 + 0.13), beltY + 0.08, at.z + 0.131);
      body.add(glassM);
    }
  }

  // Spoilers.
  if (prof.spoiler === 'wing') {
    const wing = new THREE.Mesh(new THREE.BoxGeometry(d.W * 0.86, 0.03, 0.34), black);
    wing.position.set(0, prof.deckTop * H + 0.26, tailZ - 0.24);
    wing.rotation.x = -0.12;
    wing.castShadow = true;
    body.add(wing);
    for (const s of [-1, 1]) {
      const strut = new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.26, 0.12), black);
      strut.position.set(s * d.W * 0.28, prof.deckTop * H + 0.13, tailZ - 0.25);
      body.add(strut);
      const plate = new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.14, 0.38), black);
      plate.position.set(s * d.W * 0.43, prof.deckTop * H + 0.25, tailZ - 0.24);
      body.add(plate);
    }
  } else if (prof.spoiler === 'lip' || prof.spoiler === 'ducktail') {
    const lip = new THREE.Mesh(new THREE.BoxGeometry(d.W * prof.tailW * 0.84, prof.spoiler === 'ducktail' ? 0.06 : 0.025, 0.12), prof.spoiler === 'ducktail' ? paint : black);
    ray.set(new THREE.Vector3(0, H + 1, tailZ - 0.1), new THREE.Vector3(0, -1, 0));
    const deck = ray.intersectObject(probe, false)[0];
    lip.position.set(0, (deck ? deck.point.y : tail.yTop) + 0.015, tailZ - 0.1);
    lip.rotation.x = -0.25;
    body.add(lip);
  }

  // Wheels with calipers. Positions are updated every frame by CarView.
  const wheels: WheelParts[] = [];
  const spokes = 5 + (spec.id.length % 6);
  for (let i = 0; i < 4; i++) {
    const wp = params.wheels[i];
    const front = wp.front;
    const width = front ? d.tireWF : d.tireWR;
    const pivot = new THREE.Group();
    const spin = new THREE.Group();
    const w = wheel(wp.radius, width, spokes, lod, rimMat, tireMat, discMat);
    // Rims face outward: mirror the left wheels.
    if (wp.left) w.scale.x = -1;
    spin.add(w);
    pivot.add(spin);
    if (lod === 0) {
      const cal = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.16, 0.11), caliperMat);
      cal.position.set((wp.left ? -1 : 1) * (width * 0.31 - 0.07), wp.radius * 0.38, front ? 0.12 : -0.12);
      pivot.add(cal);
    }
    pivot.position.copy(wp.hardpoint as unknown as THREE.Vector3);
    root.add(pivot);
    wheels.push({ pivot, spin });
  }

  return { root, body, wheels, paint, brakeLights, headLights, reverseLights, groundY };
}

function plateTexture(spec: CarSpec): THREE.Texture {
  const c = document.createElement('canvas');
  c.width = 256;
  c.height = 60;
  const ctx = c.getContext('2d') as CanvasRenderingContext2D;
  ctx.fillStyle = '#f3f1e8';
  ctx.fillRect(0, 0, 256, 60);
  ctx.strokeStyle = '#1b1b1b';
  ctx.lineWidth = 3;
  ctx.strokeRect(2, 2, 252, 56);
  ctx.fillStyle = '#1b1b1b';
  ctx.font = '700 38px ui-monospace, Menlo, Consolas, monospace';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  let h = 0;
  for (const ch of spec.id) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  const letters = 'ABCDEFGHJKLMNPRSTUVWXYZ';
  const plate = `HR ${letters[h % 23]}${letters[(h >> 5) % 23]} ${(h % 9000) + 1000}`;
  ctx.fillText(plate, 128, 32);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}
