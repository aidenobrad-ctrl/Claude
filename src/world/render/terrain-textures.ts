// Procedural terrain textures: albedo layers and a detail normal map, drawn
// once on canvases with fast tileable value noise.
import * as THREE from 'three';

/** Tileable multi-octave value noise into a Float32Array, values ~0..1. */
export function fbmTile(size: number, seed: number, baseCells: number, octaves: number, gain = 0.5): Float32Array {
  const out = new Float32Array(size * size);
  let s = seed >>> 0 || 1;
  const rnd = (): number => {
    s ^= s << 13;
    s ^= s >>> 17;
    s ^= s << 5;
    return (s >>> 0) / 4294967296;
  };
  let amp = 1;
  let norm = 0;
  let cells = baseCells;
  for (let o = 0; o < octaves; o++) {
    const g = new Float32Array(cells * cells);
    for (let i = 0; i < g.length; i++) g[i] = rnd();
    const k = cells / size;
    for (let y = 0; y < size; y++) {
      const fy = y * k;
      const y0 = Math.floor(fy);
      const ty = fy - y0;
      const sy = ty * ty * (3 - 2 * ty);
      const r0 = (y0 % cells) * cells;
      const r1 = ((y0 + 1) % cells) * cells;
      for (let x = 0; x < size; x++) {
        const fx = x * k;
        const x0 = Math.floor(fx);
        const tx = fx - x0;
        const sx = tx * tx * (3 - 2 * tx);
        const c0 = x0 % cells;
        const c1 = (x0 + 1) % cells;
        const top = g[r0 + c0] + (g[r0 + c1] - g[r0 + c0]) * sx;
        const bot = g[r1 + c0] + (g[r1 + c1] - g[r1 + c0]) * sx;
        out[y * size + x] += (top + (bot - top) * sy) * amp;
      }
    }
    norm += amp;
    amp *= gain;
    cells *= 2;
  }
  for (let i = 0; i < out.length; i++) out[i] /= norm;
  return out;
}

function toTexture(size: number, fill: (i: number, x: number, y: number) => [number, number, number, number?], srgb = true): THREE.DataTexture {
  const data = new Uint8Array(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const i = y * size + x;
      const [r, g, b, a] = fill(i, x, y);
      data[i * 4] = Math.max(0, Math.min(255, r));
      data[i * 4 + 1] = Math.max(0, Math.min(255, g));
      data[i * 4 + 2] = Math.max(0, Math.min(255, b));
      data[i * 4 + 3] = a === undefined ? 255 : Math.max(0, Math.min(255, a));
    }
  }
  const t = new THREE.DataTexture(data, size, size, THREE.RGBAFormat);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.magFilter = THREE.LinearFilter;
  t.minFilter = THREE.LinearMipmapLinearFilter;
  t.generateMipmaps = true;
  t.anisotropy = 8;
  t.colorSpace = srgb ? THREE.SRGBColorSpace : THREE.NoColorSpace;
  t.needsUpdate = true;
  return t;
}

export interface TerrainTextures {
  grass: THREE.Texture;
  rock: THREE.Texture;
  dirt: THREE.Texture;
  sand: THREE.Texture;
  snow: THREE.Texture;
  /** RGB detail normal (tangent space), A = height for blending. */
  normal: THREE.Texture;
  /** Large-scale variation, sampled at ~1/600 m. */
  macro: THREE.Texture;
}

let cached: TerrainTextures | null = null;

export function terrainTextures(size = 512): TerrainTextures {
  if (cached) return cached;
  const n1 = fbmTile(size, 11, 8, 6, 0.55);
  const n2 = fbmTile(size, 23, 32, 4, 0.6);
  const n3 = fbmTile(size, 37, 64, 3, 0.5);
  const blades = fbmTile(size, 41, 128, 2, 0.4);
  // Grass: mottled greens with fine blade streaks; alpha = height.
  // Olive summer grass: darker clumps, sun-dried straw patches, fine blades.
  const grass = toTexture(size, (i) => {
    const v = n1[i] * 0.6 + n2[i] * 0.4;
    const b = blades[i];
    const dry = Math.min(1, Math.max(0, n3[i] - 0.5) * 2.6);
    const clump = Math.max(0, 0.45 - n2[i]) * 1.6;
    const r = 66 + v * 34 + b * 22 + dry * 58 - clump * 22;
    const g = 80 + v * 40 + b * 26 + dry * 34 - clump * 18;
    const bl = 34 + v * 14 + b * 8 + dry * 8 - clump * 8;
    return [r, g, bl, 80 + b * 175];
  });
  // Rock: strata and cracks.
  const rock = toTexture(size, (i, x, y) => {
    const v = n1[i];
    const strata = 0.5 + 0.5 * Math.sin((y / size) * Math.PI * 2 * 9 + n2[i] * 6);
    const crack = Math.max(0, 1 - Math.abs(n3[i] - 0.5) * 14);
    const k = 0.62 + v * 0.42 + strata * 0.1 - crack * 0.35;
    void x;
    return [118 * k, 112 * k, 104 * k, 90 + v * 140 - crack * 60];
  });
  const dirt = toTexture(size, (i) => {
    const v = n1[i] * 0.5 + n2[i] * 0.5;
    const pebble = n3[i] > 0.68 ? (n3[i] - 0.68) * 3 : 0;
    const k = 0.75 + v * 0.45 + pebble * 0.5;
    return [124 * k, 96 * k, 68 * k, 60 + v * 120 + pebble * 200];
  });
  const sand = toTexture(size, (i, x, y) => {
    const ripple = 0.5 + 0.5 * Math.sin(((x + y * 0.3) / size) * Math.PI * 2 * 22 + n1[i] * 5);
    const k = 0.9 + n2[i] * 0.16 + ripple * 0.06;
    return [214 * k, 194 * k, 152 * k, 90 + ripple * 100];
  });
  const snow = toTexture(size, (i) => {
    const k = 0.9 + n1[i] * 0.1 + (n3[i] > 0.8 ? 0.08 : 0);
    return [236 * k, 242 * k, 252 * k, 120 + n2[i] * 100];
  });
  // Detail normal from a height field (the grass/dirt/rock mix).
  const hgt = new Float32Array(size * size);
  for (let i = 0; i < hgt.length; i++) hgt[i] = n2[i] * 0.6 + n3[i] * 0.3 + blades[i] * 0.1;
  const normal = toTexture(
    size,
    (i, x, y) => {
      const xl = hgt[y * size + ((x + size - 1) % size)];
      const xr = hgt[y * size + ((x + 1) % size)];
      const yu = hgt[((y + size - 1) % size) * size + x];
      const yd = hgt[((y + 1) % size) * size + x];
      const dx = (xr - xl) * 6;
      const dy = (yd - yu) * 6;
      const l = Math.sqrt(dx * dx + dy * dy + 1);
      return [(-dx / l) * 127 + 128, (-dy / l) * 127 + 128, (1 / l) * 127 + 128, hgt[i] * 255];
    },
    false,
  );
  const m = fbmTile(256, 51, 4, 5, 0.6);
  const macro = toTexture(
    256,
    (i) => {
      const v = m[i];
      return [v * 255, v * 255, v * 255];
    },
    false,
  );
  cached = { grass, rock, dirt, sand, snow, normal, macro };
  return cached;
}
