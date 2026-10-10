import { Island, newSample } from '../../src/world/island';
const isl = new Island(1);
const s = newSample();
// For each x, find the southmost z where coast >= 250 m and base h within a few m of 12.
for (let x = 1400; x <= 3400; x += 200) {
  let lastGood = NaN;
  for (let z = 1500; z <= 4200; z += 20) {
    isl.sample(x, z, s);
    if (s.coast >= 300) lastGood = z;
  }
  console.log('x', x, 'coast>=300 until z', lastGood);
}
