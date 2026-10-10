// Render the island overview to a PNG for layout review:
//   node tools/run-ts.mjs tools/render-map.ts [size] [out.png]
import fs from 'node:fs';
import { Island, WORLD_HALF, REGION, newSample } from '../src/world/island';
// @ts-expect-error plain JS helper
import { encodePng } from './png.mjs';

const size = Number(process.argv[2] ?? 512);
const out = process.argv[3] ?? 'artifacts/map.png';
const island = new Island(1);
const s = newSample();
const px = new Uint8Array(size * size * 4);
const t0 = Date.now();
const H = new Float32Array(size * size);
for (let j = 0; j < size; j++) {
  for (let i = 0; i < size; i++) {
    const x = -WORLD_HALF + ((i + 0.5) / size) * WORLD_HALF * 2;
    const z = -WORLD_HALF + ((j + 0.5) / size) * WORLD_HALF * 2;
    island.sample(x, z, s);
    H[j * size + i] = s.h;
    let r = 0;
    let g = 0;
    let b = 0;
    if (s.water > s.h) {
      [r, g, b] = [54, 120, 170];
    } else if (s.h < 0) {
      const d = Math.min(1, -s.h / 40);
      [r, g, b] = [40 - d * 20, 110 - d * 40, 160 - d * 30];
    } else {
      const h = s.h;
      if (s.region === REGION.beach && h < 6) [r, g, b] = [222, 206, 160];
      else if (h > 640) [r, g, b] = [238, 240, 245];
      else if (h > 430) [r, g, b] = [128, 122, 114];
      else if (s.desert > 0.5) [r, g, b] = [196 - h * 0.1, 128 - h * 0.08, 82];
      else if (s.forest > 0.45) [r, g, b] = [52, 92 - h * 0.03, 50];
      else [r, g, b] = [104 + h * 0.08, 140 - h * 0.05, 70];
      if (s.region === REGION.city) [r, g, b] = [150, 150, 155];
      if (s.region === REGION.harbor) [r, g, b] = [120, 124, 130];
      if (s.region === REGION.hub) [r, g, b] = [230, 170, 70];
      if (s.region === REGION.proving) [r, g, b] = [120, 120, 112];
    }
    const k = (j * size + i) * 4;
    px[k] = r;
    px[k + 1] = g;
    px[k + 2] = b;
    px[k + 3] = 255;
  }
}
// Hillshade from the height differences.
for (let j = 1; j < size - 1; j++) {
  for (let i = 1; i < size - 1; i++) {
    const dx = H[j * size + i + 1] - H[j * size + i - 1];
    const dz = H[(j + 1) * size + i] - H[(j - 1) * size + i];
    const cell = (WORLD_HALF * 2) / size;
    const shade = Math.max(0.55, Math.min(1.25, 1 + (-dx - dz) / (cell * 2) * 1.2));
    const k = (j * size + i) * 4;
    if (H[j * size + i] > 0) for (let c = 0; c < 3; c++) px[k + c] = Math.min(255, px[k + c] * shade);
  }
}
// Overlay: 1 km grid, baked roads and nodes (if present).
const toPx = (x: number, z: number): [number, number] => [((x + WORLD_HALF) / (WORLD_HALF * 2)) * size, ((z + WORLD_HALF) / (WORLD_HALF * 2)) * size];
const plot = (x: number, y: number, c: number[], a = 1): void => {
  const i = Math.round(x);
  const j = Math.round(y);
  if (i < 0 || j < 0 || i >= size || j >= size) return;
  const k = (j * size + i) * 4;
  for (let q = 0; q < 3; q++) px[k + q] = px[k + q] * (1 - a) + c[q] * a;
};
for (let km = -5; km <= 5; km++) {
  const [gx] = toPx(km * 1000, 0);
  for (let j = 0; j < size; j += 2) plot(gx, j, [255, 255, 255], 0.25);
  const [, gy] = toPx(0, km * 1000);
  for (let i = 0; i < size; i += 2) plot(i, gy, [255, 255, 255], 0.25);
}
try {
  const roads = await import('../src/world/data/roads');
  const colors = [[255, 150, 40], [250, 250, 250], [255, 230, 80], [200, 200, 210], [150, 100, 60]];
  for (const [ci, a, b, mid] of roads.ROAD_EDGES) {
    const pts: [number, number][] = [[roads.ROAD_NODES[a][1], roads.ROAD_NODES[a][2]]];
    for (let k = 0; k < mid.length; k += 2) pts.push([mid[k], mid[k + 1]]);
    pts.push([roads.ROAD_NODES[b][1], roads.ROAD_NODES[b][2]]);
    for (let k = 0; k + 1 < pts.length; k++) {
      const [x0, y0] = toPx(pts[k][0], pts[k][1]);
      const [x1, y1] = toPx(pts[k + 1][0], pts[k + 1][1]);
      const steps = Math.ceil(Math.hypot(x1 - x0, y1 - y0) * 2) + 1;
      for (let t = 0; t <= steps; t++) plot(x0 + ((x1 - x0) * t) / steps, y0 + ((y1 - y0) * t) / steps, colors[ci] ?? [255, 0, 255]);
    }
  }
  for (const [, x, z] of roads.ROAD_NODES) {
    const [nx, ny] = toPx(x, z);
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) plot(nx + dx, ny + dy, [255, 40, 40]);
  }
} catch {
  // No baked roads yet.
}
let min = Infinity;
let max = -Infinity;
for (const h of H) {
  min = Math.min(min, h);
  max = Math.max(max, h);
}
fs.mkdirSync('artifacts', { recursive: true });
fs.writeFileSync(out, encodePng(size, size, px));
console.log(`${out}: ${size}x${size} in ${Date.now() - t0} ms, height ${min.toFixed(0)}..${max.toFixed(0)} m, ${(((Date.now() - t0) * 1000) / (size * size)).toFixed(2)} µs/sample`);
