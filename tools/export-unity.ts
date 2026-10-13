// Export the island for the Unity version: terrain grids, roads, guardrails,
// trees, colliders and layout, from the same deterministic World the web game
// builds. Formats are documented in unity/Assets/Halcyon/Data/README.md.
//
//   node tools/run-ts.mjs tools/export-unity.ts [outDir]
//
// Every binary file is a 16-byte header (magic, format version, payload
// length, flags) followed by the raw-DEFLATE-compressed payload, all
// little-endian. Coordinates are in the web game's frame: x east, y up,
// z south (cars face -z at yaw 0). Unity maps (x, y, z) -> (x, y, -z).
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { Sim } from '../src/sim';
import { WORLD_HALF, LAKE, RIVER, REGION_NAMES, type RegionId } from '../src/world/island';
import { ROAD_CLASSES, ROAD_SHOULDER, CURB_H, PARAPET_W, type RoadClassId } from '../src/world/road-classes';
import { SURFACES } from '../src/world/surfaces';
import { TILE, TILE_SPACING, TREE, TREE_STRIDE } from '../src/world/world';
import { BUILDING, ROOF } from '../src/world/settlements';
import { FESTIVAL_COLORS } from '../src/world/festival';
import { TRACK_HALF_WIDTH, CURB_WIDTH, GRAVEL_WIDTH } from '../src/world/testtrack';
import { mapPois } from '../src/ui/map';

const VERSION = 1;
const outDir = process.argv[2] ?? 'unity/Assets/Halcyon/Data/Resources/HalcyonIsland';
fs.mkdirSync(outDir, { recursive: true });

const t0 = Date.now();
const sim = new Sim(1);
const world = sim.world;

// --- A tiny little-endian writer ---------------------------------------------
class Writer {
  private buf = Buffer.alloc(1 << 20);
  len = 0;
  private need(n: number): void {
    if (this.len + n <= this.buf.length) return;
    let size = this.buf.length * 2;
    while (size < this.len + n) size *= 2;
    const b = Buffer.alloc(size);
    this.buf.copy(b, 0, 0, this.len);
    this.buf = b;
  }
  u8(v: number): void {
    this.need(1);
    this.buf.writeUInt8(v & 0xff, this.len++);
  }
  i8(v: number): void {
    this.need(1);
    this.buf.writeInt8(v, this.len++);
  }
  u16(v: number): void {
    this.need(2);
    this.buf.writeUInt16LE(v & 0xffff, this.len);
    this.len += 2;
  }
  i16(v: number): void {
    this.need(2);
    this.buf.writeInt16LE(v, this.len);
    this.len += 2;
  }
  i32(v: number): void {
    this.need(4);
    this.buf.writeInt32LE(v, this.len);
    this.len += 4;
  }
  f32(v: number): void {
    this.need(4);
    this.buf.writeFloatLE(v, this.len);
    this.len += 4;
  }
  str(s: string): void {
    const b = Buffer.from(s, 'utf8');
    if (b.length > 255) throw new Error('string too long');
    this.u8(b.length);
    this.bytes(b);
  }
  bytes(b: Uint8Array): void {
    this.need(b.length);
    Buffer.from(b.buffer, b.byteOffset, b.length).copy(this.buf, this.len);
    this.len += b.length;
  }
  done(): Buffer {
    return this.buf.subarray(0, this.len);
  }
}

const sizes: Record<string, number> = {};
function writeFile(name: string, magic: string, payload: Buffer): void {
  const z = zlib.deflateRawSync(payload, { level: 9, memLevel: 9 });
  const head = Buffer.alloc(16);
  head.write(magic, 0, 4, 'ascii');
  head.writeUInt32LE(VERSION, 4);
  head.writeUInt32LE(payload.length, 8);
  head.writeUInt32LE(0, 12);
  const file = Buffer.concat([head, z]);
  fs.writeFileSync(path.join(outDir, name), file);
  sizes[name] = file.length;
  console.log(`${name}: ${(payload.length / 1e6).toFixed(1)} MB raw -> ${(file.length / 1e6).toFixed(2)} MB`);
}

/** Planar split of 16-bit values: all low bytes, then all high bytes (deflates far better). */
function planes16(v: Uint16Array): Buffer {
  const out = Buffer.alloc(v.length * 2);
  for (let i = 0; i < v.length; i++) {
    out[i] = v[i] & 0xff;
    out[v.length + i] = v[i] >>> 8;
  }
  return out;
}

// --- Terrain grids ---------------------------------------------------------------
const TILES = (WORLD_HALF * 2) / TILE; // 160
const PER = TILE / TILE_SPACING; // 16 quads per tile
const N = TILES * PER + 1; // 2561 vertices per side, 4 m apart
const NN = N * N;
const height = new Float32Array(NN);
const surface = new Uint8Array(NN);
const water = new Int16Array(NN).fill(-32768);
const region = new Uint8Array(NN);
const forest = new Uint8Array(NN);
const desert = new Uint8Array(NN);
const roadDist = new Uint8Array(NN);
const splat = [new Uint8Array(NN), new Uint8Array(NN), new Uint8Array(NN), new Uint8Array(NN)];
const q8 = (v: number): number => Math.max(0, Math.min(255, Math.round(v * 255)));

const trees = new Writer();
let treeCount = 0;
let hmin = Infinity;
let hmax = -Infinity;
const tileTrees: Float32Array[] = [];
for (let tz = 0; tz < TILES; tz++) {
  for (let tx = 0; tx < TILES; tx++) {
    const t = world.tile(tx - TILES / 2, tz - TILES / 2);
    for (let j = 0; j <= PER; j++) {
      for (let i = 0; i <= PER; i++) {
        const gi = tx * PER + i;
        const gj = tz * PER + j;
        const k = j * (PER + 1) + i;
        const g = gj * N + gi;
        const h = t.h[k];
        height[g] = h;
        if (h < hmin) hmin = h;
        if (h > hmax) hmax = h;
        surface[g] = t.surf[k];
        if (t.water[k] > -1e8) water[g] = Math.round(t.water[k] * 100);
        region[g] = t.regions[k];
        forest[g] = q8(t.forest[k]);
        desert[g] = q8(t.desert[k]);
        roadDist[g] = Math.min(255, Math.round(t.roadDist[k] * 10));
        for (let c = 0; c < 4; c++) splat[c][g] = q8(t.splat[k * 4 + c]);
      }
    }
    tileTrees.push(t.trees);
  }
  if (tz % 20 === 19) console.log(`terrain rows ${tz + 1}/${TILES} (${((Date.now() - t0) / 1000).toFixed(0)} s)`);
}
hmin = Math.floor(hmin) - 1;
hmax = Math.ceil(hmax) + 1;
const hq = (y: number): number => Math.max(0, Math.min(65535, Math.round(((y - hmin) / (hmax - hmin)) * 65535)));

{
  // Heights: uint16 over [hmin, hmax], each row delta-coded, planar bytes.
  const w = new Writer();
  w.i32(N);
  w.f32(TILE_SPACING);
  w.f32(-WORLD_HALF);
  w.f32(hmin);
  w.f32(hmax);
  const q = new Uint16Array(NN);
  for (let j = 0; j < N; j++) {
    let prev = 0;
    for (let i = 0; i < N; i++) {
      const v = hq(height[j * N + i]);
      q[j * N + i] = (v - prev) & 0xffff;
      prev = v;
    }
  }
  w.bytes(planes16(q));
  writeFile('height.bytes', 'HLHT', w.done());
}
{
  const w = new Writer();
  w.i32(N);
  w.bytes(surface);
  const wq = new Uint16Array(NN);
  for (let g = 0; g < NN; g++) wq[g] = water[g] & 0xffff;
  w.bytes(planes16(wq));
  w.bytes(region);
  w.bytes(forest);
  w.bytes(desert);
  w.bytes(roadDist);
  writeFile('ground.bytes', 'HLGR', w.done());
}
{
  const w = new Writer();
  w.i32(N);
  for (let c = 0; c < 4; c++) w.bytes(splat[c]);
  writeFile('splat.bytes', 'HLSP', w.done());
}

// --- Trees -------------------------------------------------------------------------
{
  trees.i32(TILES);
  trees.i32(TILES);
  trees.f32(TILE);
  trees.f32(-WORLD_HALF);
  trees.f32(hmin);
  trees.f32(hmax);
  let k = 0;
  for (let tz = 0; tz < TILES; tz++) {
    for (let tx = 0; tx < TILES; tx++) {
      const tr = tileTrees[k++];
      const n = tr.length / TREE_STRIDE;
      trees.u16(n);
      const x0 = (tx - TILES / 2) * TILE;
      const z0 = (tz - TILES / 2) * TILE;
      for (let m = 0; m < n; m++) {
        const o = m * TREE_STRIDE;
        trees.u16(Math.round(((tr[o] - x0) / TILE) * 65535));
        trees.u16(Math.round(((tr[o + 2] - z0) / TILE) * 65535));
        trees.u16(hq(tr[o + 1]));
        trees.u8(Math.round(((tr[o + 3] - 0.6) / 0.8) * 255));
        trees.u8(Math.round((tr[o + 4] / (Math.PI * 2)) * 256) & 255);
        trees.u8(tr[o + 5]);
        trees.u8(Math.min(255, Math.floor(tr[o + 6] * 256)));
        treeCount++;
      }
    }
  }
  writeFile('trees.bytes', 'HLTR', trees.done());
}

// --- Roads, guardrails and the proving circuit ---------------------------------------
const classIds = Object.keys(ROAD_CLASSES) as RoadClassId[];
const CENTER = { none: 0, dashed: 1, double: 2, solid: 3 } as const;
{
  const w = new Writer();
  w.i32(world.nodes.length);
  for (const n of world.nodes) {
    w.str(n.id);
    w.f32(n.x);
    w.f32(n.y);
    w.f32(n.z);
  }
  w.i32(classIds.length);
  for (const id of classIds) {
    const c = ROAD_CLASSES[id];
    w.str(id);
    w.f32(c.halfWidth);
    w.f32(c.shoulder);
    w.f32(c.maxGrade);
    w.f32(c.traffic);
    w.u8(c.surface);
    w.u8(c.lanes);
    w.u8(CENTER[c.center]);
    w.u8(c.edgeLines ? 1 : 0);
    w.u8(c.sidewalk ? 1 : 0);
  }
  w.i32(world.edges.length);
  for (const e of world.edges) {
    const r = e.road;
    w.u8(classIds.indexOf(e.cls));
    w.u16(e.a);
    w.u16(e.b);
    w.i32(r.n);
    w.f32(r.length);
    for (const arr of [r.x, r.y, r.z, r.tx, r.tz, r.s, e.base]) for (let i = 0; i < r.n; i++) w.f32(arr[i]);
    w.bytes(e.bridge);
  }
  const rails = world.rails;
  w.i32(rails.length);
  for (const run of rails) {
    w.u16(run.edge);
    w.i8(run.side);
    w.i32(run.i0);
    w.i32(run.i1);
    w.f32(run.offset);
  }
  // The proving ground's circuit.
  const tr = sim.track;
  const r = tr.road;
  w.f32(tr.origin.x);
  w.f32(tr.origin.z);
  w.f32(tr.height);
  w.i32(r.n);
  w.f32(r.length);
  w.f32(tr.startS);
  for (const arr of [r.x, r.z, r.tx, r.tz, r.s]) for (let i = 0; i < r.n; i++) w.f32(arr[i]);
  w.bytes(tr.cornerKind);
  w.bytes(new Uint8Array(tr.outside.buffer, tr.outside.byteOffset, tr.outside.length));
  writeFile('roads.bytes', 'HLRD', w.done());
}

// --- Static colliders (trees come from trees.bytes) -------------------------------------
const SEG_KINDS = ['wall', 'barrier', 'tires', 'rail', 'fence'];
const CIRC_KINDS = ['post', 'tree', 'rock', 'pier', 'building'];
{
  const w = new Writer();
  const c = sim.colliders;
  w.i32(c.segments.length);
  for (const s of c.segments) {
    for (const v of [s.ax, s.az, s.bx, s.bz, s.top, s.bottom, s.bounce, s.friction]) w.f32(v);
    w.u8(SEG_KINDS.indexOf(s.kind));
  }
  w.i32(c.circles.length);
  for (const s of c.circles) {
    for (const v of [s.x, s.z, s.r, s.top, s.bottom]) w.f32(v);
    w.u8(CIRC_KINDS.indexOf(s.kind));
    w.u8(s.breakable ? 1 : 0);
  }
  writeFile('colliders.bytes', 'HLCL', w.done());
}

// --- meta.json: everything small, human-readable --------------------------------------
const spawn = world.festival.spawn;
const spawnY = sim.groundHeight(spawn.x, spawn.z);
const tk = sim.track;
const meta = {
  format: 'halcyon-island',
  version: VERSION,
  seed: sim.seed,
  frame: 'x east, y up, z south; yaw 0 faces -z; Unity (x, y, z) = (x, y, -z)',
  worldHalf: WORLD_HALF,
  grid: { n: N, spacing: TILE_SPACING, origin: -WORLD_HALF, tile: TILE, tiles: TILES },
  height: { min: hmin, max: hmax },
  seaLevel: 0,
  lake: LAKE,
  river: RIVER,
  spawn: { x: spawn.x, y: spawnY, z: spawn.z, yaw: spawn.yaw },
  pois: mapPois(world).map((p) => ({ name: p.name, kind: p.kind, x: p.x, z: p.z, y: sim.groundHeight(p.x, p.z) })),
  road: { shoulderMax: ROAD_SHOULDER, curbHeight: CURB_H, parapetWidth: PARAPET_W, classes: classIds.map((id) => ({ id, ...ROAD_CLASSES[id] })) },
  surfaces: SURFACES.map((s, id) => ({ id, ...s })),
  regions: Object.entries(REGION_NAMES).map(([id, name]) => ({ id: +id as RegionId, name })),
  treeKinds: Object.entries(TREE).map(([name, id]) => ({ id, name })),
  buildingKinds: Object.entries(BUILDING).map(([name, id]) => ({ id, name })),
  roofKinds: Object.entries(ROOF).map(([name, id]) => ({ id, name })),
  buildingFields: ['x', 'z', 'fx', 'fz', 'w', 'd', 'h', 'y0', 'floor', 'kind', 'roof', 'seed'],
  buildings: world.buildings.list.map((b) => [b.x, b.z, b.fx, b.fz, b.w, b.d, b.h, b.y0, b.floor, b.kind, b.roof, b.seed].map((v) => +v.toFixed(4))),
  festival: { ...world.festival, colors: FESTIVAL_COLORS },
  proving: {
    origin: tk.origin,
    height: tk.height,
    halfWidth: TRACK_HALF_WIDTH,
    curbWidth: CURB_WIDTH,
    gravelWidth: GRAVEL_WIDTH,
    skidpad: tk.skidpad,
    paddock: tk.paddock,
    spawn: tk.spawn,
    startS: tk.startS,
    length: tk.road.length,
  },
  counts: {
    nodes: world.nodes.length,
    edges: world.edges.length,
    roadKm: +(world.edges.reduce((a, e) => a + e.road.length, 0) / 1000).toFixed(2),
    rails: world.rails.length,
    trees: treeCount,
    buildings: world.buildings.list.length,
    segments: sim.colliders.segments.length,
    circles: sim.colliders.circles.length,
  },
  files: sizes,
};
fs.writeFileSync(path.join(outDir, 'meta.json'), JSON.stringify(meta, null, 1) + '\n');
console.log(`meta.json: ${JSON.stringify(meta.counts)}`);
console.log(`height range ${hmin}..${hmax} m; done in ${((Date.now() - t0) / 1000).toFixed(1)} s`);
