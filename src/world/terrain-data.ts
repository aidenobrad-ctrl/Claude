// Render data for terrain tiles and tree cells, as plain typed arrays. Pure
// world code (no three.js) so it runs in the tile worker or, as a fallback
// and for tests, on the main thread with identical results.
import { World, TILE, TILE_SAMPLES, TILE_SPACING, TREE_STRIDE, type Layers } from './world';
import { WORLD_HALF, REGION, RIVER, LAKE, newSample, polylineDist } from './island';

/** Quads per tile side, finest tile size and number of coarser levels. */
export const T_QUADS = 64;
export const T_LEAF = 256;
export const T_MAX_LEVEL = 3; // 256 * 2^3 = 2048 m roots
export const T_ROOT = T_LEAF * 2 ** T_MAX_LEVEL;
/** Vegetation cell size, m (4 x 4 physics tiles). */
export const VEG_CELL = 256;

export interface TerrainTileData {
  level: number;
  ix: number;
  iz: number;
  /** Terrain mesh: (N*N + skirt) vertices; null when the tile is deep sea floor. */
  pos: Float32Array | null;
  nrm: Float32Array | null;
  col: Float32Array | null;
  spl: Float32Array | null;
  /** Water mesh on the same N*N grid: y = water level. */
  waterPos: Float32Array | null;
  /** Per water vertex: depth (m, negative on land), flow x, flow z, kind (0 sea, 1 lake, 2 river). */
  waterAttr: Float32Array | null;
}

const N = T_QUADS + 1;
const B = N + 2;

/** Ring of border vertex indices, used for skirts (same order the renderer indexes). */
export function skirtRing(): number[] {
  const ring: number[] = [];
  for (let i = 0; i < T_QUADS; i++) ring.push(i);
  for (let j = 0; j < T_QUADS; j++) ring.push(j * N + T_QUADS);
  for (let i = T_QUADS; i > 0; i--) ring.push(T_QUADS * N + i);
  for (let j = T_QUADS; j > 0; j--) ring.push(j * N);
  return ring;
}

export function buildTerrainTile(world: World, level: number, ix: number, iz: number): TerrainTileData {
  const size = T_LEAF * 2 ** level;
  const step = size / T_QUADS;
  const x0 = -WORLD_HALF + ix * size;
  const z0 = -WORLD_HALF + iz * size;
  const H = new Float32Array(B * B);
  const s = newSample();
  // Per interior vertex: base sample fields and layers.
  const reg = new Uint8Array(N * N);
  const forest = new Float32Array(N * N);
  const desert = new Float32Array(N * N);
  const coast = new Float32Array(N * N);
  const wlev = new Float32Array(N * N);
  const roadD = new Float32Array(N * N);
  const bwat = new Float32Array(N * N);
  const spl = new Float32Array((N * N + T_QUADS * 4) * 4);
  for (let j = 0; j < B; j++) {
    for (let i = 0; i < B; i++) {
      const x = x0 + (i - 1) * step;
      const z = z0 + (j - 1) * step;
      const interior = i >= 1 && j >= 1 && i <= N && j <= N;
      if (level === 0 && interior) {
        // Exactly the physics heightfield, with its stored samples.
        const tx = Math.floor(x / TILE);
        const tz = Math.floor(z / TILE);
        let lx = Math.round((x - tx * TILE) / TILE_SPACING);
        let lz = Math.round((z - tz * TILE) / TILE_SPACING);
        const t = world.tile(tx, tz);
        lx = Math.min(TILE_SAMPLES - 1, lx);
        lz = Math.min(TILE_SAMPLES - 1, lz);
        const k = lz * TILE_SAMPLES + lx;
        const v = (j - 1) * N + (i - 1);
        H[j * B + i] = t.h[k];
        reg[v] = t.regions[k];
        forest[v] = t.forest[k];
        desert[v] = t.desert[k];
        coast[v] = t.coast[k];
        wlev[v] = t.water[k] > -1e8 ? t.water[k] : t.coast[k] < 120 ? 0 : NaN;
        roadD[v] = t.roadDist[k];
        spl[v * 4] = t.splat[k * 4];
        spl[v * 4 + 1] = t.splat[k * 4 + 1];
        spl[v * 4 + 2] = t.splat[k * 4 + 2];
        spl[v * 4 + 3] = t.splat[k * 4 + 3];
        continue;
      }
      H[j * B + i] = world.terrainHeight(x, z, s);
      if (!interior) continue;
      const v = (j - 1) * N + (i - 1);
      reg[v] = s.region;
      forest[v] = s.forest;
      desert[v] = s.desert;
      coast[v] = s.coast;
      wlev[v] = s.water > -Infinity ? s.water : s.coast < 120 ? 0 : NaN;
      bwat[v] = s.water;
      roadD[v] = world.lastRoadDist;
      // Keep the sample for the layer pass below.
      s.h = H[j * B + i];
    }
  }
  const pos = new Float32Array((N * N + T_QUADS * 4) * 3);
  const nrm = new Float32Array((N * N + T_QUADS * 4) * 3);
  const col = new Float32Array((N * N + T_QUADS * 4) * 3);
  const lay: Layers = { grass: 0, rock: 0, dirt: 0, sand: 0, snow: 0 };
  let anyWater = false;
  let deepAll = true;
  for (let j = 0; j < N; j++) {
    for (let i = 0; i < N; i++) {
      const v = j * N + i;
      const h = H[(j + 1) * B + i + 1];
      pos[v * 3] = x0 + i * step;
      pos[v * 3 + 1] = h;
      pos[v * 3 + 2] = z0 + j * step;
      const hx = H[(j + 1) * B + i + 2] - H[(j + 1) * B + i];
      const hz = H[(j + 2) * B + i + 1] - H[j * B + i + 1];
      let nx = -hx / (2 * step);
      let nz = -hz / (2 * step);
      const l = Math.sqrt(nx * nx + 1 + nz * nz);
      nx /= l;
      nz /= l;
      nrm[v * 3] = nx;
      nrm[v * 3 + 1] = 1 / l;
      nrm[v * 3 + 2] = nz;
      if (level > 0) {
        s.h = h;
        s.region = reg[v] as typeof s.region;
        s.forest = forest[v];
        s.desert = desert[v];
        s.coast = coast[v];
        s.water = bwat[v];
        const slope = Math.sqrt(hx * hx + hz * hz) / (2 * step);
        world.layers(s, h, slope, roadD[v], lay);
        spl[v * 4] = lay.rock;
        spl[v * 4 + 1] = lay.dirt;
        spl[v * 4 + 2] = lay.sand;
        spl[v * 4 + 3] = lay.snow;
      }
      // Biome tint: lush forest floor, sun-bleached farmland, red desert.
      let r = 1;
      let g = 1;
      let b = 1;
      const de = desert[v];
      if (de > 0) {
        r = 1 + 0.32 * de;
        g = 1 - 0.1 * de;
        b = 1 - 0.28 * de;
      }
      const fo = forest[v];
      if (fo > 0) {
        r *= 1 - 0.3 * fo;
        g *= 1 - 0.12 * fo;
        b *= 1 - 0.2 * fo;
      }
      if (reg[v] === REGION.farmland) {
        r *= 1.1;
        g *= 1.05;
      }
      // Cheap baked occlusion from curvature: hollows darker, crests lighter.
      const lap = H[(j + 1) * B + i] + H[(j + 1) * B + i + 2] + H[j * B + i + 1] + H[(j + 2) * B + i + 1] - 4 * h;
      const ao = Math.max(0.7, Math.min(1.08, 1 - (lap * 0.02) / Math.max(1, step / 4)));
      col[v * 3] = r * ao;
      col[v * 3 + 1] = g * ao;
      col[v * 3 + 2] = b * ao;
      const wl = wlev[v];
      if (!Number.isNaN(wl) && wl - h > -2) anyWater = true;
      if (Number.isNaN(wl) || wl - h < 14) deepAll = false;
    }
  }
  // Skirts: the border ring dropped down, hiding cracks between levels.
  const ring = skirtRing();
  const drop = Math.max(2, step * 1.5);
  let sv = N * N;
  for (const v of ring) {
    pos[sv * 3] = pos[v * 3];
    pos[sv * 3 + 1] = pos[v * 3 + 1] - drop;
    pos[sv * 3 + 2] = pos[v * 3 + 2];
    for (let c = 0; c < 3; c++) {
      nrm[sv * 3 + c] = nrm[v * 3 + c];
      col[sv * 3 + c] = col[v * 3 + c] * 0.9;
    }
    for (let c = 0; c < 4; c++) spl[sv * 4 + c] = spl[v * 4 + c];
    sv++;
  }
  const out: TerrainTileData = { level, ix, iz, pos, nrm, col, spl, waterPos: null, waterAttr: null };
  // Deep sea floor is invisible under opaque water: skip the terrain mesh.
  if (deepAll) {
    out.pos = out.nrm = out.col = out.spl = null;
  }
  if (anyWater) {
    // Water levels where defined; dry vertices drop just under the ground,
    // so the surface meets the bank and never floods low land nearby.
    const wp = new Float32Array(N * N * 3);
    const wa = new Float32Array(N * N * 4);
    for (let v = 0; v < N * N; v++) {
      const dry = Number.isNaN(wlev[v]) || wlev[v] < pos[v * 3 + 1] - 0.05;
      const lvl = dry ? pos[v * 3 + 1] - 1.5 : wlev[v];
      wp[v * 3] = pos[v * 3];
      wp[v * 3 + 1] = lvl;
      wp[v * 3 + 2] = pos[v * 3 + 2];
      const h = pos[v * 3 + 1];
      wa[v * 4] = lvl - h;
      const x = pos[v * 3];
      const z = pos[v * 3 + 2];
      let kind = 0;
      let fx = 0;
      let fz = 0;
      if (lvl > 0.6) {
        const dl = Math.sqrt((x - LAKE.x) * (x - LAKE.x) + (z - LAKE.z) * (z - LAKE.z));
        if (dl < LAKE.r + 120) kind = 1;
        else {
          kind = 2;
          const rv = polylineDist(x, z, RIVER);
          const a = Math.min(RIVER.length - 2, Math.floor(rv.s));
          const dx = RIVER[a + 1].x - RIVER[a].x;
          const dz = RIVER[a + 1].z - RIVER[a].z;
          const l = Math.sqrt(dx * dx + dz * dz) || 1;
          fx = dx / l;
          fz = dz / l;
        }
      }
      wa[v * 4 + 1] = fx;
      wa[v * 4 + 2] = fz;
      wa[v * 4 + 3] = kind;
    }
    out.waterPos = wp;
    out.waterAttr = wa;
  }
  return out;
}

/**
 * Trees in one vegetation cell (4 x 4 physics tiles), TREE_STRIDE floats
 * each. `keep` thins the set by each tree's rank for distant cells, so far
 * cells hold a subset of the same trees.
 */
export function buildTreeCell(world: World, cx: number, cz: number, keep = 1): Float32Array {
  const per = VEG_CELL / TILE;
  const parts: Float32Array[] = [];
  let total = 0;
  for (let j = 0; j < per; j++) {
    for (let i = 0; i < per; i++) {
      const t = world.tile(cx * per + i, cz * per + j).trees;
      if (keep >= 1) {
        parts.push(t);
        total += t.length;
        continue;
      }
      const kept: number[] = [];
      for (let k = 0; k < t.length; k += TREE_STRIDE) {
        if (t[k + 6] < keep) for (let c = 0; c < TREE_STRIDE; c++) kept.push(t[k + c]);
      }
      const a = new Float32Array(kept);
      parts.push(a);
      total += a.length;
    }
  }
  const out = new Float32Array(total);
  let o = 0;
  for (const p of parts) {
    out.set(p, o);
    o += p.length;
  }
  return out;
}
