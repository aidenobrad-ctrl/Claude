import fs from 'node:fs';
import path from 'node:path';
import { test, assert, metric } from './kit';
import { dsin, dcos, dtan, datan, datan2, dexp, dlog, dpow, dtanh, dasin } from '../src/engine/dmath';

function maxRelErr(f: (x: number) => number, ref: (x: number) => number, xs: number[]): number {
  let worst = 0;
  for (const x of xs) {
    const a = f(x);
    const b = ref(x);
    const err = Math.abs(a - b) / Math.max(1e-300, Math.abs(b) > 1e-8 ? Math.abs(b) : 1);
    if (err > worst) worst = err;
  }
  return worst;
}

const xs: number[] = [];
for (let i = -20000; i <= 20000; i++) xs.push(i * 0.00513);
for (let i = 0; i < 2000; i++) xs.push((i - 1000) * 37.1);
xs.push(0, -0, 1e-12, Math.PI, -Math.PI, Math.PI / 2, Math.PI / 4, 1e4);

test('deterministic sin/cos/tan match libm closely', () => {
  const s = maxRelErr(dsin, Math.sin, xs);
  const c = maxRelErr(dcos, Math.cos, xs);
  const tn = maxRelErr(dtan, Math.tan, xs.filter((x) => Math.abs(Math.cos(x)) > 1e-3));
  metric('dsin_max_err', s);
  metric('dcos_max_err', c);
  assert(s < 1e-12 && c < 1e-12, `sin err ${s}, cos err ${c}`);
  assert(tn < 1e-9, `tan err ${tn}`);
});

test('deterministic atan/atan2/asin match libm closely', () => {
  const a = maxRelErr(datan, Math.atan, xs);
  assert(a < 1e-14, `atan err ${a}`);
  let worst = 0;
  for (let i = 0; i < 5000; i++) {
    const y = Math.sin(i * 1.7) * (i % 7) * 3;
    const x = Math.cos(i * 0.9) * (i % 5) * 2;
    worst = Math.max(worst, Math.abs(datan2(y, x) - Math.atan2(y, x)));
  }
  assert(worst < 1e-14, `atan2 abs err ${worst}`);
  const as = maxRelErr(dasin, Math.asin, xs.filter((x) => Math.abs(x) < 0.999));
  assert(as < 1e-12, `asin err ${as}`);
});

test('deterministic exp/log/pow/tanh match libm closely', () => {
  const e = maxRelErr(dexp, Math.exp, xs.filter((x) => Math.abs(x) < 700));
  const l = maxRelErr(dlog, Math.log, xs.filter((x) => x > 0).concat([1e-300, 5e-324, 1e300, 1, 2, 0.5]));
  let p = 0;
  for (let i = 1; i < 3000; i++) {
    const x = i * 0.0731;
    for (const y of [1.37, -0.5, 2.5, 0.25, 3, -2, 1 / 3]) p = Math.max(p, Math.abs(dpow(x, y) - Math.pow(x, y)) / Math.pow(x, y));
  }
  const th = maxRelErr(dtanh, Math.tanh, xs);
  metric('dexp_max_err', e);
  metric('dlog_max_err', l);
  assert(e < 1e-14, `exp err ${e}`);
  assert(l < 1e-14, `log err ${l}`);
  assert(p < 1e-12, `pow err ${p}`);
  assert(th < 1e-12, `tanh err ${th}`);
  assert(dpow(2, 10) === 1024 && dpow(3, -2) === 1 / 9, 'integer pow must be exact');
});

// Simulation code must not call engine-dependent Math functions. Files that
// import three.js, and the ui/ and audio/ folders, are presentation-only.
test('simulation sources do not use engine-dependent Math functions', () => {
  const root = path.join(process.cwd(), 'src');
  const banned = /\bMath\.(sin|cos|tan|atan|atan2|asin|acos|exp|expm1|log|log2|log10|log1p|pow|tanh|sinh|cosh|hypot|cbrt)\s*\(/;
  const offenders: string[] = [];
  const walk = (dir: string): void => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) {
        if (e.name === 'ui' || e.name === 'audio' || e.name === 'render') continue;
        walk(p);
      } else if (e.name.endsWith('.ts') && !e.name.endsWith('.d.ts')) {
        const src = fs.readFileSync(p, 'utf8');
        if (/from 'three'/.test(src) || /\/\/ @presentation-only/.test(src)) continue;
        if (p.endsWith(path.join('engine', 'dmath.ts'))) continue;
        src.split('\n').forEach((line, i) => {
          if (banned.test(line) && !line.includes('// det-ok')) offenders.push(`${path.relative(process.cwd(), p)}:${i + 1}: ${line.trim()}`);
        });
      }
    }
  };
  walk(root);
  assert(offenders.length === 0, `use src/engine/dmath.ts instead:\n      ${offenders.join('\n      ')}`);
});
