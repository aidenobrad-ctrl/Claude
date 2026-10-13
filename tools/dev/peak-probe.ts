// Find sharp peaks: local maxima with steep average slope around them.
import { Island } from '../../src/world/island';
const isl = new Island(1);
const S = 40;
const peaks: { x: number; z: number; h: number; drop: number }[] = [];
for (let x = -4000; x <= 4000; x += S) {
  for (let z = -4200; z <= -1000; z += S) {
    const h = isl.height(x, z);
    if (h < 500) continue;
    let isMax = true;
    for (const [dx, dz] of [[S, 0], [-S, 0], [0, S], [0, -S], [S, S], [-S, -S], [S, -S], [-S, S]]) if (isl.height(x + dx, z + dz) > h) isMax = false;
    if (!isMax) continue;
    // Mean drop 150 m away.
    let drop = 0;
    for (let k = 0; k < 8; k++) {
      const a = (k / 8) * Math.PI * 2;
      drop += h - isl.height(x + Math.cos(a) * 150, z + Math.sin(a) * 150);
    }
    peaks.push({ x, z, h, drop: drop / 8 });
  }
}
peaks.sort((a, b) => b.drop - a.drop);
for (const p of peaks.slice(0, 12)) console.log(`peak (${p.x}, ${p.z}) h=${p.h.toFixed(0)} drop@150m=${p.drop.toFixed(0)} slope=${(p.drop / 150).toFixed(2)}`);
