// Probe: replay the 200 km/h keyboard lane change and print where it goes wrong.
import { Sim } from '../../src/sim';
import { neutralControls } from '../../src/engine/input';
import { datan2 } from '../../src/engine/dmath';
import { buildTestTrack, TestTrackGround } from '../../src/world/testtrack';

for (const mode of ['world', 'flat'] as const) {
  const sim = new Sim(3);
  if (mode === 'flat') {
    // Old behaviour: the flat facility ground everywhere.
    const tg = new TestTrackGround(sim.track);
    (sim as unknown as { ground: unknown }).ground = tg;
  }
  const road = sim.track.road;
  const at = { x: 0, y: 0, z: 0, tx: 0, tz: 0, i: 0 };
  road.at(sim.track.startS - 1500, at);
  sim.teleport(at.x, at.z, datan2(-at.tx, -at.tz));
  const v = sim.player.vehicle;
  v.setSpeed(55);
  let maxBeta = 0;
  for (let i = 0; i < 240 * 3; i++) {
    const c = { ...neutralControls(), throttle: 0.6, steer: i < 96 ? 1 : 0, analogSteer: false, source: 'keyboard' as const };
    sim.step(c);
    const beta = Math.abs(datan2(v.vLat, Math.max(1, v.vLong))) * 57.3;
    maxBeta = Math.max(maxBeta, beta);
    if (i % 24 === 0) {
      const st = sim.getState();
      console.log(mode, (i / 240).toFixed(2), 'beta', beta.toFixed(1), 'y', v.pos.y.toFixed(2), 'lat', st.lateral.toFixed(1), 'surf', v.wheels.map((w) => w.surface).join(','), 'yawRate', v.yawRate.toFixed(2), 'steer', v.input.steer.toFixed(2));
    }
  }
  console.log(mode, 'maxBeta', maxBeta.toFixed(1));
}
void buildTestTrack;
