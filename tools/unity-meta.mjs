// Give every file and folder under the Unity project's Halcyon assets a
// .meta with a GUID derived from its path, so .meta files never differ
// between machines and never need to be created by the Editor.
//   node tools/unity-meta.mjs [root ...]   (default: unity/Assets/Halcyon)
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const unityRoot = path.resolve('unity');
const roots = process.argv.slice(2).length ? process.argv.slice(2) : ['unity/Assets/Halcyon'];

function guid(p) {
  const rel = path.relative(unityRoot, p).split(path.sep).join('/');
  return crypto.createHash('md5').update('halcyon:' + rel).digest('hex');
}

function body(p, isDir) {
  const head = `fileFormatVersion: 2\nguid: ${guid(p)}\n`;
  const tail = '  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n';
  if (isDir) return head + 'folderAsset: yes\nDefaultImporter:\n' + tail;
  const ext = path.extname(p).toLowerCase();
  if (ext === '.cs')
    return head + 'MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n';
  if (ext === '.asmdef') return head + 'AssemblyDefinitionImporter:\n' + tail;
  if (['.bytes', '.json', '.md', '.txt', '.csv', '.xml', '.yaml'].includes(ext)) return head + 'TextScriptImporter:\n' + tail;
  return null; // models, textures, shaders: let Unity pick the importer
}

let made = 0;
function walk(dir) {
  for (const name of fs.readdirSync(dir)) {
    if (name.startsWith('.') || name.endsWith('.meta') || name.endsWith('~')) continue;
    const p = path.join(dir, name);
    const isDir = fs.statSync(p).isDirectory();
    const meta = p + '.meta';
    if (!fs.existsSync(meta)) {
      const b = body(p, isDir);
      if (b) {
        fs.writeFileSync(meta, b);
        made++;
      }
    }
    if (isDir) walk(p);
  }
}
for (const r of roots) {
  const abs = path.resolve(r);
  // Folders above the root inside Assets need metas too.
  let up = path.dirname(abs);
  const assets = path.join(unityRoot, 'Assets');
  const chain = [];
  for (let d = abs; d.startsWith(assets) && d !== assets; d = path.dirname(d)) chain.push(d);
  for (const d of chain) if (!fs.existsSync(d + '.meta')) { fs.writeFileSync(d + '.meta', body(d, true)); made++; }
  walk(abs);
  void up;
}
console.log(`${made} .meta files written`);
