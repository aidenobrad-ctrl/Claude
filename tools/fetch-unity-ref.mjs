// Fetch UnityEngine reference assemblies (2021.3 LTS API, netstandard2.0)
// into .cache/unityref so dotnet/Halcyon.Unity can compile-check the Unity
// scripts outside Unity. They are only compiled against, never committed.
//   node tools/fetch-unity-ref.mjs
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const out = path.resolve('.cache/unityref');
const editorOut = path.join(out, 'editor');
if (fs.existsSync(path.join(out, 'UnityEngine.CoreModule.dll')) && fs.existsSync(path.join(editorOut, 'UnityEditor.dll'))) {
  console.log(`already present: ${out}`);
  process.exit(0);
}
fs.mkdirSync(out, { recursive: true });
const url = 'https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg';
const pkg = path.join(out, 'modules.nupkg');
const res = await fetch(url);
if (!res.ok) throw new Error(`download failed: ${res.status}`);
fs.writeFileSync(pkg, Buffer.from(await res.arrayBuffer()));
execFileSync('python3', ['-c', `
import zipfile, os, sys
z = zipfile.ZipFile(sys.argv[1])
for n in z.namelist():
    if n.startswith('lib/netstandard2.0/') and n.endswith('.dll'):
        open(os.path.join(sys.argv[2], os.path.basename(n)), 'wb').write(z.read(n))
`, pkg, out]);
fs.unlinkSync(pkg);
// UnityEditor.dll (2021.1 API) for compile-checking editor scripts.
fs.mkdirSync(editorOut, { recursive: true });
const sdk = path.join(out, 'sdk.nupkg');
const res2 = await fetch('https://api.nuget.org/v3-flatcontainer/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg');
if (!res2.ok) throw new Error(`download failed: ${res2.status}`);
fs.writeFileSync(sdk, Buffer.from(await res2.arrayBuffer()));
execFileSync('python3', ['-c', `
import zipfile, os, sys
z = zipfile.ZipFile(sys.argv[1])
open(os.path.join(sys.argv[2], 'UnityEditor.dll'), 'wb').write(z.read('lib/UnityEditor.dll'))
`, sdk, editorOut]);
fs.unlinkSync(sdk);
console.log(`${fs.readdirSync(out).length} assemblies in ${out}, UnityEditor.dll in ${editorOut}`);
