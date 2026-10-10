// Bundles src/main.ts with esbuild and inlines it, with the CSS, into one
// self-contained dist/index.html. No runtime CDN or network dependency.
//
//   node tools/build.mjs            production build (minified)
//   node tools/build.mjs --dev      unminified, with __DEV__ checks
//   node tools/build.mjs --watch --serve   rebuild on change, serve on :8080
import * as esbuild from 'esbuild';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const args = new Set(process.argv.slice(2));
const watch = args.has('--watch');
const serve = args.has('--serve');
const dev = args.has('--dev') || watch;
const outDir = path.join(root, 'dist');
const outFile = path.join(outDir, 'index.html');

function inlineHtml(js) {
  const template = fs.readFileSync(path.join(root, 'src/index.html'), 'utf8');
  // Escape any closing script tag inside the bundle so the HTML parser cannot
  // terminate the inline script early.
  const safe = js.replace(/<\/script/gi, '<\\/script');
  return template.replace('<!--APP_SCRIPT-->', () => `<script>${safe}</script>`);
}

// The game must not fetch anything at runtime. Fail the build if the output
// contains a script/link/img pointing at a remote URL.
function checkSelfContained(html) {
  const bad = html.match(/<(script|link|img|iframe)[^>]+(src|href)\s*=\s*["']?(https?:)?\/\//gi);
  if (bad) throw new Error(`dist/index.html references remote resources: ${bad.join(', ')}`);
}

const buildOptions = {
  entryPoints: [path.join(root, 'src/main.ts')],
  bundle: true,
  write: false,
  format: 'iife',
  target: ['es2020', 'safari15'],
  minify: !dev,
  legalComments: 'eof',
  sourcemap: dev ? 'inline' : false,
  loader: { '.css': 'text', '.glsl': 'text' },
  define: {
    __DEV__: dev ? 'true' : 'false',
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
  },
  logLevel: 'warning',
  metafile: true,
};

function writeOutput(result) {
  const js = result.outputFiles.find((f) => f.path.endsWith('.js'))?.text ?? result.outputFiles[0].text;
  const html = inlineHtml(js);
  checkSelfContained(html);
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(outFile, html);
  return html.length;
}

if (watch) {
  const ctx = await esbuild.context({
    ...buildOptions,
    plugins: [{
      name: 'inline-html',
      setup(build) {
        build.onEnd((result) => {
          if (result.errors.length) return;
          const bytes = writeOutput(result);
          console.log(`[build] ${new Date().toLocaleTimeString()} dist/index.html ${(bytes / 1024).toFixed(0)} KB`);
        });
      },
    }],
  });
  await ctx.watch();
  if (serve) {
    http.createServer((req, res) => {
      const p = path.join(outDir, decodeURIComponent((req.url ?? '/').split('?')[0]));
      const file = fs.existsSync(p) && fs.statSync(p).isFile() ? p : outFile;
      res.writeHead(200, { 'content-type': file.endsWith('.html') ? 'text/html' : 'application/octet-stream' });
      fs.createReadStream(file).pipe(res);
    }).listen(8080, () => console.log('[serve] http://localhost:8080'));
  }
} else {
  const t0 = Date.now();
  const result = await esbuild.build(buildOptions);
  const bytes = writeOutput(result);
  if (args.has('--analyze')) console.log(await esbuild.analyzeMetafile(result.metafile));
  console.log(`[build] dist/index.html ${(bytes / 1024).toFixed(0)} KB in ${Date.now() - t0} ms${dev ? ' (dev)' : ''}`);
}
