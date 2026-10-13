import { Island, newSample } from '../../src/world/island';
const isl = new Island(1) as unknown as { mountains(x: number, z: number): number; plains(x: number, z: number): number; sample: Island['sample']; forest(x: number, z: number): number };
const s = newSample();
const px = Number(process.argv[2] ?? -1480);
const pz = Number(process.argv[3] ?? -3600);
for (const d of [0, 50, 100, 150, 200, 300]) {
  const row: string[] = [];
  for (let k = 0; k < 4; k++) {
    const a = (k / 4) * Math.PI * 2;
    const x = px + Math.cos(a) * d;
    const z = pz + Math.sin(a) * d;
    isl.sample(x, z, s);
    row.push(`h=${s.h.toFixed(0)} m=${isl.mountains(x, z).toFixed(0)} reg=${s.region}`);
  }
  console.log(d, row.join(' | '));
}
