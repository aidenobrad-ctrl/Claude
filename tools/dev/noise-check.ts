// Check simplexD's gradient against finite differences.
import { Noise2 } from '../../src/engine/noise';
const n = new Noise2(5);
const d = new Float64Array(2);
let worst = 0;
for (let k = 0; k < 2000; k++) {
  const x = Math.random() * 50 - 25;
  const y = Math.random() * 50 - 25;
  const v = n.simplexD(x, y, d);
  if (Math.abs(v - n.simplex(x, y)) > 1e-12) throw new Error('value mismatch');
  const e = 1e-5;
  const fx = (n.simplex(x + e, y) - n.simplex(x - e, y)) / (2 * e);
  const fy = (n.simplex(x, y + e) - n.simplex(x, y - e)) / (2 * e);
  worst = Math.max(worst, Math.abs(fx - d[0]), Math.abs(fy - d[1]));
}
console.log('max gradient error', worst.toExponential(2));
let mn = Infinity, mx = -Infinity;
for (let k = 0; k < 20000; k++) { const v = n.eroded(Math.random() * 100, Math.random() * 100, 6); mn = Math.min(mn, v); mx = Math.max(mx, v); }
console.log('eroded range', mn.toFixed(3), mx.toFixed(3));
