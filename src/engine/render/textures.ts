// Procedural textures drawn on canvases at startup (no image files).
import * as THREE from 'three';
import { RNG } from '../rng';
import { Noise2 } from '../noise';

export type TexKind = 'asphalt' | 'grass' | 'gravel' | 'concrete' | 'curb' | 'dirt' | 'sand' | 'snow' | 'rock' | 'tires';

function canvas(w: number, h: number): [HTMLCanvasElement, CanvasRenderingContext2D] {
  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  const ctx = c.getContext('2d') as CanvasRenderingContext2D;
  return [c, ctx];
}

/** Fill with tileable noise between two colors. */
function noiseFill(ctx: CanvasRenderingContext2D, w: number, h: number, seed: number, c0: number[], c1: number[], scale: number, speckle: number, speckColor?: number[]): void {
  const img = ctx.createImageData(w, h);
  const n = new Noise2(seed);
  const rng = new RNG(seed);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      // Tileable: blend four noise samples across the wrap.
      const u = x / w;
      const v = y / h;
      const f = (a: number, b: number): number => n.fbm(a * scale, b * scale, 4);
      const s = scale;
      const val =
        f(u, v) * (1 - u) * (1 - v) + f(u - 1, v) * u * (1 - v) + f(u, v - 1) * (1 - u) * v + f(u - 1, v - 1) * u * v;
      void s;
      let t = Math.min(1, Math.max(0, 0.5 + val * 0.6));
      const sp = rng.next();
      let r = c0[0] + (c1[0] - c0[0]) * t;
      let g = c0[1] + (c1[1] - c0[1]) * t;
      let b = c0[2] + (c1[2] - c0[2]) * t;
      if (sp < speckle) {
        const k = speckColor ?? [255, 255, 255];
        const m = 0.25 + rng.next() * 0.45;
        r += (k[0] - r) * m;
        g += (k[1] - g) * m;
        b += (k[2] - b) * m;
      } else {
        const j = (rng.next() - 0.5) * 14;
        r += j;
        g += j;
        b += j;
      }
      t = 0;
      const i = (y * w + x) * 4;
      img.data[i] = r;
      img.data[i + 1] = g;
      img.data[i + 2] = b;
      img.data[i + 3] = 255;
    }
  }
  ctx.putImageData(img, 0, 0);
}

function finish(c: HTMLCanvasElement, repeat = true, anisotropy = 4): THREE.CanvasTexture {
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  if (repeat) t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = anisotropy;
  t.needsUpdate = true;
  return t;
}

const cache = new Map<string, THREE.Texture>();

export function texture(kind: TexKind): THREE.Texture {
  const hit = cache.get(kind);
  if (hit) return hit;
  let tex: THREE.Texture;
  switch (kind) {
    case 'asphalt': {
      const [c, ctx] = canvas(256, 256);
      noiseFill(ctx, 256, 256, 11, [52, 54, 58], [74, 76, 80], 6, 0.08, [140, 140, 140]);
      tex = finish(c);
      break;
    }
    case 'grass': {
      const [c, ctx] = canvas(256, 256);
      noiseFill(ctx, 256, 256, 23, [70, 104, 44], [104, 136, 60], 5, 0.05, [140, 160, 80]);
      tex = finish(c);
      break;
    }
    case 'gravel': {
      const [c, ctx] = canvas(256, 256);
      noiseFill(ctx, 256, 256, 37, [150, 138, 116], [190, 178, 154], 12, 0.35, [230, 222, 205]);
      tex = finish(c);
      break;
    }
    case 'concrete': {
      const [c, ctx] = canvas(256, 256);
      noiseFill(ctx, 256, 256, 41, [150, 150, 146], [176, 175, 170], 4, 0.03, [120, 120, 120]);
      ctx.strokeStyle = 'rgba(70,70,70,0.45)';
      ctx.lineWidth = 2;
      ctx.strokeRect(0, 0, 256, 256);
      tex = finish(c);
      break;
    }
    case 'dirt': {
      const [c, ctx] = canvas(256, 256);
      noiseFill(ctx, 256, 256, 53, [118, 92, 64], [150, 120, 86], 5, 0.12, [180, 160, 130]);
      tex = finish(c);
      break;
    }
    case 'sand': {
      const [c, ctx] = canvas(256, 256);
      noiseFill(ctx, 256, 256, 59, [204, 184, 138], [226, 210, 168], 4, 0.1, [240, 230, 200]);
      tex = finish(c);
      break;
    }
    case 'snow': {
      const [c, ctx] = canvas(256, 256);
      noiseFill(ctx, 256, 256, 61, [222, 228, 238], [246, 248, 252], 4, 0.05, [255, 255, 255]);
      tex = finish(c);
      break;
    }
    case 'rock': {
      const [c, ctx] = canvas(256, 256);
      noiseFill(ctx, 256, 256, 67, [96, 94, 92], [138, 134, 128], 7, 0.1, [170, 168, 160]);
      tex = finish(c);
      break;
    }
    case 'curb': {
      // Red and white blocks along v; u runs across the curb.
      const [c, ctx] = canvas(64, 256);
      for (let i = 0; i < 4; i++) {
        ctx.fillStyle = i % 2 ? '#f2f2f0' : '#c8231d';
        ctx.fillRect(0, i * 64, 64, 64);
      }
      ctx.fillStyle = 'rgba(0,0,0,0.12)';
      ctx.fillRect(0, 0, 6, 256);
      tex = finish(c);
      break;
    }
    case 'tires': {
      const [c, ctx] = canvas(128, 64);
      ctx.fillStyle = '#18191b';
      ctx.fillRect(0, 0, 128, 64);
      for (let x = 0; x < 128; x += 16) {
        ctx.fillStyle = '#26272a';
        ctx.fillRect(x + 2, 4, 12, 56);
      }
      ctx.fillStyle = '#d8d8d8';
      ctx.fillRect(0, 26, 128, 10);
      ctx.fillStyle = '#c8231d';
      for (let x = 0; x < 128; x += 32) ctx.fillRect(x, 26, 16, 10);
      tex = finish(c);
      break;
    }
  }
  cache.set(kind, tex);
  return tex;
}

/** Track surface: asphalt with white edge lines. u across (0..1), v along (meters / 8). */
export function trackTexture(): THREE.Texture {
  const key = 'track';
  const hit = cache.get(key);
  if (hit) return hit;
  const [c, ctx] = canvas(256, 512);
  noiseFill(ctx, 256, 512, 13, [50, 52, 56], [72, 74, 78], 7, 0.08, [140, 140, 140]);
  // Worn racing line: slightly darker band, rubbered in.
  const g = ctx.createLinearGradient(0, 0, 256, 0);
  g.addColorStop(0.2, 'rgba(0,0,0,0)');
  g.addColorStop(0.45, 'rgba(0,0,0,0.16)');
  g.addColorStop(0.6, 'rgba(0,0,0,0.12)');
  g.addColorStop(0.8, 'rgba(0,0,0,0)');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, 256, 512);
  ctx.fillStyle = 'rgba(240,240,236,0.92)';
  ctx.fillRect(6, 0, 7, 512);
  ctx.fillRect(243, 0, 7, 512);
  const t = finish(c);
  t.anisotropy = 8;
  cache.set(key, t);
  return t;
}

/** Soft round sprite for smoke and dust particles. */
export function puffTexture(): THREE.Texture {
  const key = 'puff';
  const hit = cache.get(key);
  if (hit) return hit;
  const [c, ctx] = canvas(64, 64);
  const g = ctx.createRadialGradient(32, 32, 2, 32, 32, 31);
  g.addColorStop(0, 'rgba(255,255,255,1)');
  g.addColorStop(0.5, 'rgba(255,255,255,0.45)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, 64, 64);
  const t = finish(c, false);
  t.colorSpace = THREE.NoColorSpace;
  cache.set(key, t);
  return t;
}

/** Text or pattern texture for signs and boards. */
export function signTexture(text: string, bg: string, fg: string, w = 512, h = 128): THREE.Texture {
  const key = `sign:${text}:${bg}:${fg}:${w}x${h}`;
  const hit = cache.get(key);
  if (hit) return hit;
  const [c, ctx] = canvas(w, h);
  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, w, h);
  ctx.fillStyle = fg;
  ctx.font = `800 ${Math.floor(h * 0.56)}px system-ui, -apple-system, "Segoe UI", sans-serif`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, w / 2, h / 2 + h * 0.03, w * 0.92);
  const t = finish(c, false);
  cache.set(key, t);
  return t;
}
