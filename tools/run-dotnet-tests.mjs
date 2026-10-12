// The Unity side's checks for npm run verify: compile every Unity script
// against the UnityEngine reference assemblies, then the headless .NET
// suites (physics bit-exact with the TypeScript, the island, the career,
// the living island of M6 with the stunts, events and progression of M7,
// and the AI). --quick runs shorter subsets.
// Skips (with a message) when dotnet is not installed.
//   node tools/run-dotnet-tests.mjs [--quick]
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const quick = process.argv.includes('--quick');

const probe = spawnSync('dotnet', ['--version'], { encoding: 'utf8' });
if (probe.status !== 0) {
  console.log('dotnet is not installed: the .NET suites and the Unity compile check are SKIPPED.');
  process.exit(0);
}

const steps = [];
const refs = fs.existsSync(path.join(root, '.cache/unityref/UnityEngine.CoreModule.dll'));
if (!refs) {
  const fetch = spawnSync('node', ['tools/fetch-unity-ref.mjs'], { cwd: root, stdio: 'inherit' });
  if (fetch.status !== 0) console.log('UnityEngine reference assemblies unavailable: the Unity compile check is SKIPPED.');
}
if (fs.existsSync(path.join(root, '.cache/unityref/UnityEngine.CoreModule.dll'))) {
  steps.push(['Unity scripts compile', ['build', 'dotnet/Halcyon.Unity', '-v', 'q', '-nologo']]);
  steps.push(['Unity editor scripts compile', ['build', 'dotnet/Halcyon.UnityEditor', '-v', 'q', '-nologo']]);
}
const run = (project, ...args) => ['run', '-c', 'Release', '--project', `dotnet/${project}`, ...(args.length ? ['--', ...args] : [])];
steps.push(['physics (bit-exact)', run('Halcyon.Core.Tests')]);
steps.push(['island', run('Halcyon.World.Tests')]);
if (quick) {
  steps.push(['career: saves', run('Halcyon.Career.Tests', 'save')]);
  steps.push(['career: parts', run('Halcyon.Career.Tests', 'parts')]);
  for (const mode of ['clock', 'weather', 'sim', 'festival', 'discover', 'highcrest', 'gps', 'stunts', 'progression']) steps.push([`island life: ${mode}`, run('Halcyon.Sim.Tests', mode)]);
  steps.push(['AI: sprints', run('Halcyon.AI.Tests', 'sprint')]);
} else {
  steps.push(['career', run('Halcyon.Career.Tests')]);
  steps.push(['island life and M7', run('Halcyon.Sim.Tests')]);
  steps.push(['AI', run('Halcyon.AI.Tests')]);
}

const summary = [];
let failed = false;
for (const [name, args] of steps) {
  const t0 = Date.now();
  const r = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', maxBuffer: 1 << 28 });
  const out = (r.stdout ?? '') + (r.stderr ?? '');
  const ok = r.status === 0;
  // Keep the log short: the failures and the last lines.
  const lines = out.split('\n').filter((l) => l.trim());
  const fails = lines.filter((l) => /\bFAIL\b|error/.test(l));
  console.log(`${ok ? '✓' : '✗'} ${name} (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
  if (!ok) {
    for (const l of (fails.length ? fails : lines).slice(-25)) console.log('    ' + l);
    failed = true;
  }
  summary.push(ok);
}
console.log(`\n.NET: ${summary.filter(Boolean).length} passed, ${summary.filter((x) => !x).length} failed${quick ? ' (quick)' : ''}`);
process.exit(failed ? 1 : 0);
