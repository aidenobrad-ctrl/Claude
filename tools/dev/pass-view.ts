// Renders the Highcrest pass roads over a hillshade with 20 m contours.
//   node tools/run-ts.mjs tools/dev/pass-view.ts out.png
import fs from 'node:fs';
import { Island, newSample } from '../../src/world/island';
import * as NEW from '../../src/world/data/roads';
// @ts-expect-error plain JS helper
import { encodePng } from '../png.mjs';

const X0 = -250, X1 = 1150, Z0 = -4350, Z1 = -1150, MPP = 2;
const W = Math.round((X1 - X0) / MPP), H = Math.round((Z1 - Z0) / MPP);
const island = new Island(1);
const s = newSample();
const hts = new Float32Array(W * H);
for (let j = 0; j < H; j++) for (let i = 0; i < W; i++) {
  island.sample(X0 + (i + 0.5) * MPP, Z0 + (j + 0.5) * MPP, s);
  hts[j * W + i] = s.h;
}
const px = new Uint8Array(W * H * 4);
for (let j = 0; j < H; j++) for (let i = 0; i < W; i++) {
  const h = hts[j * W + i];
  const dx = hts[j * W + Math.min(W - 1, i + 1)] - hts[j * W + Math.max(0, i - 1)];
  const dz = hts[Math.min(H - 1, j + 1) * W + i] - hts[Math.max(0, j - 1) * W + i];
  const shade = Math.max(0.25, Math.min(1, 0.75 - (dx - dz) * 0.08));
  const contour = Math.floor(h / 20) !== Math.floor(hts[j * W + Math.min(W - 1, i + 1)] / 20) || Math.floor(h / 20) !== Math.floor(hts[Math.min(H - 1, j + 1) * W + i] / 20);
  const base = 150 + Math.min(80, h * 0.12);
  const k = (j * W + i) * 4;
  px[k] = contour ? 90 : base * shade;
  px[k + 1] = contour ? 90 : (base + 10) * shade;
  px[k + 2] = contour ? 90 : (base - 20) * shade;
  px[k + 3] = 255;
}
function draw(data: typeof NEW, r: number, g: number, b: number, w: number): void {
  data.ROAD_EDGES.forEach(([ci, a, bb, mid]) => {
    if (data.ROAD_CLASS_ORDER[ci] !== 'pass') return;
    const pts: number[] = [data.ROAD_NODES[a][1], data.ROAD_NODES[a][2], ...mid, data.ROAD_NODES[bb][1], data.ROAD_NODES[bb][2]];
    for (let k = 0; k + 3 < pts.length; k += 2) {
      const n = Math.ceil(Math.hypot(pts[k + 2] - pts[k], pts[k + 3] - pts[k + 1]) / 0.5);
      for (let t = 0; t <= n; t++) {
        const x = pts[k] + ((pts[k + 2] - pts[k]) * t) / n, z = pts[k + 1] + ((pts[k + 3] - pts[k + 1]) * t) / n;
        const ci0 = Math.round((x - X0) / MPP), cj0 = Math.round((z - Z0) / MPP);
        for (let dj = -w; dj <= w; dj++) for (let di = -w; di <= w; di++) {
          const ii = ci0 + di, jj = cj0 + dj;
          if (ii < 0 || jj < 0 || ii >= W || jj >= H) continue;
          const kk = (jj * W + ii) * 4;
          px[kk] = r; px[kk + 1] = g; px[kk + 2] = b;
        }
      }
    }
  });
}
draw(NEW, 30, 80, 230, 1);
fs.writeFileSync(process.argv[2] ?? 'artifacts/pass.png', encodePng(W, H, px));
console.log(`wrote ${W}x${H}`);
