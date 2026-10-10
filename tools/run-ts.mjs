// Run a TypeScript tool script: bundles it with esbuild for Node and runs it.
//   node tools/run-ts.mjs tools/render-map.ts [args...]
import * as esbuild from 'esbuild';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import fs from 'node:fs';

const [entry, ...args] = process.argv.slice(2);
if (!entry) {
  console.error('usage: node tools/run-ts.mjs <script.ts> [args]');
  process.exit(1);
}
const out = path.join('.cache/tools', path.basename(entry).replace(/\.ts$/, '.mjs'));
fs.mkdirSync(path.dirname(out), { recursive: true });
await esbuild.build({
  entryPoints: [entry],
  outfile: out,
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node22',
  sourcemap: 'inline',
  define: { __DEV__: 'true', __BUILD_TIME__: '"tool"' },
  loader: { '.css': 'text', '.glsl': 'text' },
  logLevel: 'warning',
});
const r = spawnSync(process.execPath, ['--enable-source-maps', '--stack-size=4000', out, ...args], { stdio: 'inherit' });
process.exit(r.status ?? 1);
