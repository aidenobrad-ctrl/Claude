// Playwright helpers for driving the built game in headless Chromium with
// software WebGL (SwiftShader). The page runs in test mode (?test=1): no
// requestAnimationFrame loop, the test steps the simulation explicitly.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

process.env.PLAYWRIGHT_BROWSERS_PATH ??= '/opt/pw-browsers';
const { chromium } = await import('playwright-core');

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const shotDir = path.join(root, 'artifacts/screenshots');

const PHONE_UA = 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Mobile Safari/537.36';

export const VIEWPORTS = {
  desktop: { width: 1280, height: 720, isMobile: false, hasTouch: false },
  portrait: { width: 390, height: 844, isMobile: true, hasTouch: true, userAgent: PHONE_UA },
  landscape: { width: 844, height: 390, isMobile: true, hasTouch: true, userAgent: PHONE_UA },
};

export async function launchBrowser() {
  return chromium.launch({
    headless: true,
    args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
  });
}

/**
 * Open dist/index.html. Every non-file request is blocked so a missing
 * network resource fails loudly instead of being fetched.
 */
export async function openGame(browser, { viewport = 'desktop', query = 'test=1', waitReady = true, initScript = null, dpr = 1 } = {}) {
  const vp = typeof viewport === 'string' ? VIEWPORTS[viewport] : viewport;
  const ctx = await browser.newContext({
    viewport: { width: vp.width, height: vp.height },
    deviceScaleFactor: dpr,
    isMobile: vp.isMobile,
    hasTouch: vp.hasTouch,
    ...(vp.userAgent ? { userAgent: vp.userAgent } : {}),
  });
  const blocked = [];
  await ctx.route('**/*', (route) => {
    const u = route.request().url();
    if (u.startsWith('file:') || u.startsWith('data:') || u.startsWith('blob:')) return route.continue();
    blocked.push(u);
    return route.abort();
  });
  const page = await ctx.newPage();
  if (initScript) await page.addInitScript(initScript);
  const errors = [];
  const logs = [];
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(m.text());
    else logs.push(`[${m.type()}] ${m.text()}`);
  });
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
  const url = 'file://' + path.join(root, 'dist/index.html') + (query ? '?' + query : '');
  await page.goto(url);
  if (waitReady) await page.waitForFunction(() => window.__game && (window.__game.ready ?? true), null, { timeout: 120000 });
  return {
    ctx,
    page,
    errors,
    blocked,
    logs,
    async close() {
      await ctx.close();
    },
  };
}

/** Render a frame and save a screenshot of the page (canvas plus DOM UI). */
export async function shot(page, name) {
  fs.mkdirSync(shotDir, { recursive: true });
  await page.evaluate(() => window.__game.render());
  const file = path.join(shotDir, `${name}.png`);
  await page.screenshot({ path: file });
  return file;
}

/**
 * Bundle a TypeScript module from src/ for Node and import it, so browser
 * tests can compare results against the same code running outside Chromium.
 */
export async function importSrc(rel) {
  const esbuild = await import('esbuild');
  const out = path.join(root, '.cache/browser-imports', rel.replace(/[\\/]/g, '_').replace(/\.ts$/, '.mjs'));
  await esbuild.build({
    entryPoints: [path.join(root, 'src', rel)],
    outfile: out,
    bundle: true,
    platform: 'node',
    format: 'esm',
    define: { __DEV__: 'true', __BUILD_TIME__: '"test"', __TILE_WORKER__: '""' },
    loader: { '.css': 'text', '.glsl': 'text' },
    logLevel: 'warning',
  });
  return import(pathToFileURL(out).href + '?t=' + Date.now());
}
