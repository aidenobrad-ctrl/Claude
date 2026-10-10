// Headless simulation test runner. Bundles tests/*.test.ts for Node with
// esbuild and runs each file in its own process, in parallel.
//
//   node tools/run-sim-tests.mjs                 all tests
//   node tools/run-sim-tests.mjs physics         only files whose name contains "physics"
//   node tools/run-sim-tests.mjs --quick         fewer seeds and shorter soaks
//   node tools/run-sim-tests.mjs --grep "0-100"  only test cases whose name matches
import * as esbuild from 'esbuild';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const quick = argv.includes('--quick');
const grepIdx = argv.indexOf('--grep');
const grep = grepIdx >= 0 ? argv[grepIdx + 1] : '';
const fileFilters = argv.filter((a, i) => !a.startsWith('--') && argv[i - 1] !== '--grep');

const testDir = path.join(root, 'tests');
const files = fs
  .readdirSync(testDir)
  .filter((f) => f.endsWith('.test.ts'))
  .filter((f) => fileFilters.length === 0 || fileFilters.some((x) => f.includes(x)))
  .sort();
if (files.length === 0) {
  console.error('no test files matched');
  process.exit(1);
}

const outDir = path.join(root, '.cache/sim-tests');
fs.rmSync(outDir, { recursive: true, force: true });
await esbuild.build({
  entryPoints: files.map((f) => path.join(testDir, f)),
  outdir: outDir,
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node22',
  sourcemap: 'inline',
  outExtension: { '.js': '.mjs' },
  loader: { '.css': 'text', '.glsl': 'text' },
  define: { __DEV__: 'true', __BUILD_TIME__: '"test"', __TILE_WORKER__: '""' },
  logLevel: 'warning',
});

function runFile(f) {
  return new Promise((resolve) => {
    const t0 = Date.now();
    const js = path.join(outDir, f.replace(/\.ts$/, '.mjs'));
    const child = spawn(process.execPath, ['--enable-source-maps', '--stack-size=4000', js], {
      cwd: root,
      env: { ...process.env, SIM_QUICK: quick ? '1' : '0', TEST_FILTER: grep },
    });
    let out = '';
    child.stdout.on('data', (d) => (out += d));
    child.stderr.on('data', (d) => (out += d));
    child.on('close', (code) => {
      const m = out.match(/@@RESULT (.*)/);
      const result = m ? JSON.parse(m[1]) : { pass: 0, fail: 1, metrics: {} };
      if (!m) out += `\n  ✗ crashed with exit code ${code}`;
      resolve({ file: f, code, out: out.replace(/@@RESULT .*\n?/, ''), result, ms: Date.now() - t0 });
    });
  });
}

const concurrency = Math.max(1, Math.min(os.cpus().length, 4));
const queue = [...files];
const results = [];
await Promise.all(
  Array.from({ length: concurrency }, async () => {
    while (queue.length) {
      const f = queue.shift();
      const r = await runFile(f);
      results.push(r);
      console.log(`\n● ${r.file} (${(r.ms / 1000).toFixed(1)} s)\n${r.out.trimEnd()}`);
    }
  }),
);

const metricsDir = path.join(root, 'artifacts/metrics');
fs.mkdirSync(metricsDir, { recursive: true });
let pass = 0;
let fail = 0;
for (const r of results) {
  pass += r.result.pass;
  fail += r.result.fail;
  if (Object.keys(r.result.metrics).length) {
    fs.writeFileSync(path.join(metricsDir, r.file.replace(/\.test\.ts$/, '.json')), JSON.stringify(r.result.metrics, null, 2));
  }
}
console.log(`\nsim tests: ${pass} passed, ${fail} failed${quick ? ' (quick)' : ''}`);
process.exit(fail ? 1 : 0);
