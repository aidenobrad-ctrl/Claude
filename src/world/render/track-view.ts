// Three.js meshes for the proving ground. Static geometry is merged per
// material so the whole facility costs a handful of draw calls.
import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { texture, trackTexture, signTexture } from '../../engine/render/textures';
import { CURB_WIDTH, GRAVEL_WIDTH, TRACK_HALF_WIDTH, type TrackLayout } from '../testtrack';
import type { Road } from '../road';

/** A strip along the road between two lateral offsets for samples i0..i1 (inclusive, wrapping). */
export function ribbon(road: Road, i0: number, i1: number, latA: number | ((i: number) => number), latB: number | ((i: number) => number), y: (i: number, side: 0 | 1) => number, vScale: number): THREE.BufferGeometry {
  const idx: number[] = [];
  let i = i0;
  for (;;) {
    idx.push(i);
    if (i === i1) break;
    i = (i + 1) % road.n;
    if (idx.length > road.n + 1) break;
  }
  const pos: number[] = [];
  const uv: number[] = [];
  const nrm: number[] = [];
  const tris: number[] = [];
  let s0 = road.s[idx[0]];
  let prevS = s0;
  let acc = 0;
  idx.forEach((k, j) => {
    const la = typeof latA === 'number' ? latA : latA(k);
    const lb = typeof latB === 'number' ? latB : latB(k);
    // Right normal is (-tz, tx).
    const rx = -road.tz[k];
    const rz = road.tx[k];
    let s = road.s[k];
    if (s < prevS) acc += road.length;
    prevS = s;
    s += acc;
    pos.push(road.x[k] + rx * la, y(k, 0), road.z[k] + rz * la);
    pos.push(road.x[k] + rx * lb, y(k, 1), road.z[k] + rz * lb);
    uv.push(0, (s - s0) / vScale, 1, (s - s0) / vScale);
    nrm.push(0, 1, 0, 0, 1, 0);
    if (j > 0) {
      const a = (j - 1) * 2;
      // Winding so the top face points up whichever side is "A".
      if (la < lb) tris.push(a, a + 1, a + 2, a + 1, a + 3, a + 2);
      else tris.push(a, a + 2, a + 1, a + 1, a + 2, a + 3);
    }
  });
  s0 = 0;
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(nrm, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(tris);
  return g;
}

function runs(flags: (i: number) => boolean, n: number): [number, number][] {
  const out: [number, number][] = [];
  let start = -1;
  for (let i = 0; i < n; i++) {
    if (flags(i)) {
      if (start < 0) start = i;
    } else if (start >= 0) {
      out.push([start, i - 1]);
      start = -1;
    }
  }
  if (start >= 0) {
    // Merge a run that wraps past the end with one at the start.
    if (out.length && out[0][0] === 0) out[0][0] = start;
    else out.push([start, n - 1]);
  }
  return out;
}

function decal(mat: THREE.Material, layer: number): THREE.Material {
  mat.polygonOffset = true;
  mat.polygonOffsetFactor = -layer;
  mat.polygonOffsetUnits = -layer * 2;
  return mat;
}

export function buildTrackView(t: TrackLayout): THREE.Group {
  const g = new THREE.Group();
  g.name = 'proving-ground';
  const road = t.road;
  const n = road.n;

  const H = t.height;

  // Track surface.
  {
    const geo = ribbon(road, 0, n - 1, -TRACK_HALF_WIDTH, TRACK_HALF_WIDTH, () => H + 0.03, 8);
    // Close the loop with one more quad.
    const closing = ribbon(road, n - 1, 0, -TRACK_HALF_WIDTH, TRACK_HALF_WIDTH, () => H + 0.03, 8);
    const mat = decal(new THREE.MeshStandardMaterial({ map: trackTexture(), roughness: 0.92, metalness: 0, envMapIntensity: 0.35 }), 2);
    const m = new THREE.Mesh(mergeGeometries([geo, closing]), mat);
    m.receiveShadow = true;
    m.name = 'track';
    g.add(m);
  }

  // Curbs (both sides of corners) and gravel traps (outside of tight corners).
  {
    const curbGeos: THREE.BufferGeometry[] = [];
    const gravelGeos: THREE.BufferGeometry[] = [];
    for (const [a, b] of runs((i) => t.cornerKind[i] > 0, n)) {
      for (const side of [-1, 1]) {
        const inner = side * TRACK_HALF_WIDTH;
        const outer = side * (TRACK_HALF_WIDTH + CURB_WIDTH);
        curbGeos.push(ribbon(road, a, b, inner, outer, (_i, s) => H + (s === 0 ? 0.035 : 0.07), 4));
      }
    }
    for (const [a, b] of runs((i) => t.cornerKind[i] === 2, n)) {
      // Split each run where the outside switches sides.
      let start = a;
      let i = a;
      const flush = (end: number): void => {
        const side = t.outside[start];
        gravelGeos.push(ribbon(road, start, end, side * (TRACK_HALF_WIDTH + CURB_WIDTH), side * (TRACK_HALF_WIDTH + CURB_WIDTH + GRAVEL_WIDTH), () => H + 0.015, 10));
      };
      for (;;) {
        const nx = (i + 1) % n;
        if (i === b) {
          flush(i);
          break;
        }
        if (t.outside[nx] !== t.outside[start]) {
          flush(i);
          start = nx;
        }
        i = nx;
      }
    }
    if (curbGeos.length) {
      const m = new THREE.Mesh(mergeGeometries(curbGeos), decal(new THREE.MeshStandardMaterial({ map: texture('curb'), roughness: 0.7 }), 3));
      m.receiveShadow = true;
      g.add(m);
    }
    if (gravelGeos.length) {
      const m = new THREE.Mesh(mergeGeometries(gravelGeos), decal(new THREE.MeshLambertMaterial({ map: texture('gravel') }), 1));
      m.receiveShadow = true;
      g.add(m);
    }
  }

  // Start/finish line: checkered strip across the track.
  {
    const at = { x: 0, y: 0, z: 0, tx: 0, tz: 0, i: 0 };
    road.at(t.startS, at);
    const c = document.createElement('canvas');
    c.width = 128;
    c.height = 16;
    const ctx = c.getContext('2d') as CanvasRenderingContext2D;
    for (let x = 0; x < 16; x++) for (let y = 0; y < 2; y++) {
      ctx.fillStyle = (x + y) % 2 ? '#111' : '#f4f4f4';
      ctx.fillRect(x * 8, y * 8, 8, 8);
    }
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace;
    const geo = new THREE.PlaneGeometry(TRACK_HALF_WIDTH * 2, 1.6).rotateX(-Math.PI / 2);
    const m = new THREE.Mesh(geo, decal(new THREE.MeshStandardMaterial({ map: tex, roughness: 0.8 }), 4));
    m.position.set(at.x, H + 0.035, at.z);
    m.rotation.y = Math.atan2(at.tx, at.tz);
    g.add(m);
    // Gantry over the line.
    const steel = new THREE.MeshStandardMaterial({ color: 0x2b2f36, roughness: 0.5, metalness: 0.6 });
    const gantry = new THREE.Group();
    for (const side of [-1, 1]) {
      const post = new THREE.Mesh(new THREE.BoxGeometry(0.6, 7.5, 0.6), steel);
      post.position.set(side * (TRACK_HALF_WIDTH + 2.5), 3.75, 0);
      post.castShadow = true;
      gantry.add(post);
    }
    const beam = new THREE.Mesh(new THREE.BoxGeometry(TRACK_HALF_WIDTH * 2 + 6, 1.6, 0.8), steel);
    beam.position.y = 7.2;
    beam.castShadow = true;
    gantry.add(beam);
    const sign = new THREE.Mesh(new THREE.PlaneGeometry(TRACK_HALF_WIDTH * 2 + 4, 1.3), new THREE.MeshBasicMaterial({ map: signTexture('HALCYON PROVING GROUND', '#12161d', '#ffb547', 1024, 96) }));
    sign.position.set(0, 7.2, 0.41);
    const sign2 = sign.clone();
    sign2.position.z = -0.41;
    sign2.rotation.y = Math.PI;
    gantry.add(sign, sign2);
    gantry.position.set(at.x, H, at.z);
    gantry.rotation.y = Math.atan2(at.tx, at.tz) + Math.PI / 2;
    g.add(gantry);
  }

  // Barriers from the collision segments.
  {
    const tires: THREE.BufferGeometry[] = [];
    const rails: THREE.BufferGeometry[] = [];
    const posts: THREE.BufferGeometry[] = [];
    for (const s of t.colliders.segments) {
      const dx = s.bx - s.ax;
      const dz = s.bz - s.az;
      const len = Math.sqrt(dx * dx + dz * dz);
      const yaw = Math.atan2(dx, dz);
      if (s.kind === 'tires') {
        const b = new THREE.BoxGeometry(0.9, 1.0, len + 0.05);
        const uv = b.getAttribute('uv') as THREE.BufferAttribute;
        for (let i = 0; i < uv.count; i++) uv.setX(i, uv.getX(i) * (len / 1.2));
        b.rotateY(yaw).translate((s.ax + s.bx) / 2, H + 0.5, (s.az + s.bz) / 2);
        tires.push(b);
      } else if (s.kind === 'rail') {
        const b = new THREE.BoxGeometry(0.08, 0.32, len + 0.02).rotateY(yaw).translate((s.ax + s.bx) / 2, H + 0.62, (s.az + s.bz) / 2);
        rails.push(b);
        const p = new THREE.BoxGeometry(0.12, 0.75, 0.12).translate(s.ax, H + 0.37, s.az);
        posts.push(p);
      }
    }
    if (tires.length) {
      const m = new THREE.Mesh(mergeGeometries(tires), new THREE.MeshStandardMaterial({ map: texture('tires'), roughness: 0.85 }));
      m.castShadow = m.receiveShadow = true;
      g.add(m);
    }
    if (rails.length) {
      const m = new THREE.Mesh(mergeGeometries(rails), new THREE.MeshStandardMaterial({ color: 0xc4cad2, roughness: 0.35, metalness: 0.85 }));
      m.castShadow = true;
      g.add(m);
      g.add(new THREE.Mesh(mergeGeometries(posts), new THREE.MeshStandardMaterial({ color: 0x7c828a, roughness: 0.5, metalness: 0.6 })));
    }
  }

  // Paddock, pit building and skidpad.
  {
    const pd = t.paddock;
    const w = pd.x1 - pd.x0;
    const d = pd.z1 - pd.z0;
    // Clone shared textures before changing their repeat.
    const ct = texture('concrete').clone();
    ct.repeat.set(w / 12, d / 12);
    const con = new THREE.Mesh(new THREE.PlaneGeometry(w, d).rotateX(-Math.PI / 2), decal(new THREE.MeshStandardMaterial({ map: ct, roughness: 0.85 }), 1));
    con.position.set((pd.x0 + pd.x1) / 2, H + 0.02, (pd.z0 + pd.z1) / 2);
    con.receiveShadow = true;
    g.add(con);
    const pit = buildPitBuilding(w - 40);
    pit.position.set((pd.x0 + pd.x1) / 2, H, pd.z0 - 9);
    g.add(pit);

    const sk = t.skidpad;
    // RingGeometry UVs span the whole diameter; repeat the asphalt every 8 m.
    const at = texture('asphalt').clone();
    const diameter = (sk.r + sk.width / 2) * 2;
    at.repeat.set(diameter / 8, diameter / 8);
    const ring = new THREE.Mesh(new THREE.RingGeometry(sk.r - sk.width / 2, sk.r + sk.width / 2, 128, 1).rotateX(-Math.PI / 2), decal(new THREE.MeshStandardMaterial({ map: at, roughness: 0.92, envMapIntensity: 0.35 }), 2));
    ring.position.set(sk.x, H + 0.025, sk.z);
    ring.receiveShadow = true;
    g.add(ring);
    const line = new THREE.Mesh(new THREE.RingGeometry(sk.r - 0.12, sk.r + 0.12, 160, 1).rotateX(-Math.PI / 2), decal(new THREE.MeshBasicMaterial({ color: 0xf2f2ee }), 4));
    line.position.set(sk.x, H + 0.03, sk.z);
    g.add(line);
    // Cones around the inner edge of the pad.
    const cone = new THREE.ConeGeometry(0.18, 0.5, 10).translate(0, 0.25, 0);
    const cones = new THREE.InstancedMesh(cone, new THREE.MeshStandardMaterial({ color: 0xff6a1a, roughness: 0.6 }), 32);
    const m4 = new THREE.Matrix4();
    for (let i = 0; i < 32; i++) {
      const a = (i / 32) * Math.PI * 2;
      m4.makeTranslation(sk.x + Math.cos(a) * (sk.r - sk.width / 2 - 1), H, sk.z + Math.sin(a) * (sk.r - sk.width / 2 - 1));
      cones.setMatrixAt(i, m4);
    }
    cones.castShadow = true;
    g.add(cones);
  }

  return g;
}

function buildPitBuilding(length: number): THREE.Group {
  const g = new THREE.Group();
  const wall = new THREE.MeshStandardMaterial({ color: 0xe6e3dc, roughness: 0.8 });
  const dark = new THREE.MeshStandardMaterial({ color: 0x1c2027, roughness: 0.4, metalness: 0.3 });
  const glass = new THREE.MeshStandardMaterial({ color: 0x24364a, roughness: 0.08, metalness: 0.9 });
  const body = new THREE.Mesh(new THREE.BoxGeometry(length, 6, 14), wall);
  body.position.y = 3;
  body.castShadow = body.receiveShadow = true;
  g.add(body);
  // Garage doors along the front.
  const doors = Math.floor(length / 12);
  const doorGeo = new THREE.PlaneGeometry(8, 4);
  const doorMesh = new THREE.InstancedMesh(doorGeo, dark, doors);
  const m4 = new THREE.Matrix4();
  for (let i = 0; i < doors; i++) {
    m4.makeTranslation(-length / 2 + 6 + i * 12, 2.05, 7.02);
    doorMesh.setMatrixAt(i, m4);
  }
  g.add(doorMesh);
  // Glass upper floor with a roof overhang.
  const upper = new THREE.Mesh(new THREE.BoxGeometry(length * 0.6, 3.2, 10), glass);
  upper.position.set(0, 7.6, -1);
  upper.castShadow = true;
  g.add(upper);
  const roof = new THREE.Mesh(new THREE.BoxGeometry(length * 0.64, 0.4, 13), wall);
  roof.position.set(0, 9.4, 0);
  roof.castShadow = true;
  g.add(roof);
  return g;
}
