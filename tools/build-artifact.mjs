// Turn dist/index.html into a page body for publishing as a claude.ai
// artifact: the host wraps the page in its own document skeleton, so this
// keeps the title, styles, markup and the inline bundle, and drops the
// doctype, html/head/body tags and meta tags.
//   node tools/build-artifact.mjs <out.html>
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const out = process.argv[2];
if (!out) {
  console.error('usage: node tools/build-artifact.mjs <out.html>');
  process.exit(1);
}
const html = fs.readFileSync(path.join(root, 'dist/index.html'), 'utf8');
const title = html.match(/<title>[\s\S]*?<\/title>/)?.[0] ?? '<title>Halcyon Roads</title>';
const style = html.match(/<style>[\s\S]*?<\/style>/)?.[0] ?? '';
const bodyStart = html.indexOf('<body>') + '<body>'.length;
const bodyEnd = html.lastIndexOf('</body>');
const body = html.slice(bodyStart, bodyEnd).trim();
// A single dark visual world: say so for native controls and scrollbars.
const page = `${title}\n${style.replace('<style>', '<style>\n:root{color-scheme:dark;background:#0b0f17}')}\n${body}\n`;
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, page);
console.log(`${out}: ${(page.length / 1024).toFixed(0)} KB`);
