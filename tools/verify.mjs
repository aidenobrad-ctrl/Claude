// npm run verify: typecheck, build, simulation tests, browser tests.
// Pass --quick to shorten the long-running suites.
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const quick = process.argv.includes('--quick') ? ['--quick'] : [];
const steps = [
  ['typecheck', 'node', ['node_modules/typescript/bin/tsc', '--noEmit', '-p', 'tsconfig.json']],
  ['build', 'node', ['tools/build.mjs']],
  ['sim tests', 'node', ['tools/run-sim-tests.mjs', ...quick]],
  ['browser tests', 'node', ['tools/browser-tests.mjs', ...quick]],
];

const summary = [];
let failed = false;
for (const [name, cmd, args] of steps) {
  console.log(`\n━━ ${name} ━━`);
  const t0 = Date.now();
  const r = spawnSync(cmd, args, { cwd: root, stdio: 'inherit' });
  const ok = r.status === 0;
  summary.push(`${ok ? '✓' : '✗'} ${name.padEnd(14)} ${((Date.now() - t0) / 1000).toFixed(1)} s`);
  if (!ok) {
    failed = true;
    if (name === 'typecheck' || name === 'build') break;
  }
}
console.log(`\n━━ verify summary ━━\n${summary.join('\n')}\n${failed ? 'VERIFY FAILED' : 'VERIFY PASSED'}`);
process.exit(failed ? 1 : 0);
