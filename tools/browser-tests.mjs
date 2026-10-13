// Browser test runner: loads every tests/browser/*.mjs module and runs the
// scenarios it exports against the built dist/index.html.
//
//   node tools/browser-tests.mjs            all scenarios
//   node tools/browser-tests.mjs hud        only files whose name contains "hud"
//   node tools/browser-tests.mjs --quick    skip scenarios marked slow
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { launchBrowser, openGame, shot, root } from './browser.mjs';

const argv = process.argv.slice(2);
const quick = argv.includes('--quick');
const filters = argv.filter((a) => !a.startsWith('--'));
const dir = path.join(root, 'tests/browser');
const files = fs.readdirSync(dir).filter((f) => f.endsWith('.mjs')).filter((f) => !filters.length || filters.some((x) => f.includes(x))).sort();

if (!fs.existsSync(path.join(root, 'dist/index.html'))) {
  console.error('dist/index.html missing: run npm run build first');
  process.exit(1);
}

class Fail extends Error {}
const t = {
  assert(cond, msg) {
    if (!cond) throw new Fail(msg);
  },
  range(v, lo, hi, what) {
    if (!(v >= lo && v <= hi)) throw new Fail(`${what} = ${v}, expected ${lo}..${hi}`);
  },
};

const browser = await launchBrowser();
let pass = 0;
let fail = 0;
let skipped = 0;
for (const f of files) {
  const mod = await import(pathToFileURL(path.join(dir, f)).href);
  console.log(`\n● ${f}`);
  for (const sc of mod.scenarios ?? []) {
    if (quick && sc.slow) {
      skipped++;
      continue;
    }
    const t0 = Date.now();
    const opened = [];
    const helpers = {
      ...t,
      shot,
      async open(opts) {
        const g = await openGame(browser, opts);
        opened.push(g);
        return g;
      },
    };
    try {
      await sc.run(helpers);
      for (const g of opened) {
        t.assert(g.errors.length === 0, `console errors: ${g.errors.slice(0, 3).join(' | ')}`);
        t.assert(g.blocked.length === 0, `blocked network requests: ${g.blocked.slice(0, 3).join(', ')}`);
      }
      pass++;
      console.log(`  ✓ ${sc.name} (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
    } catch (e) {
      fail++;
      console.log(`  ✗ ${sc.name}\n      ${e instanceof Fail ? e.message : (e.stack ?? String(e)).split('\n').slice(0, 5).join('\n      ')}`);
      for (const g of opened) if (g.errors.length) console.log(`      console errors: ${g.errors.slice(0, 5).join('\n      ')}`);
    } finally {
      for (const g of opened) await g.close().catch(() => {});
    }
  }
}
await browser.close();
console.log(`\nbrowser tests: ${pass} passed, ${fail} failed${skipped ? `, ${skipped} skipped` : ''}`);
process.exit(fail ? 1 : 0);
