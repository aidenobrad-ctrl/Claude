// Driving slice: the player-facing layer (controls filter, laps, resets,
// collisions) on the proving ground, plus a long random soak.
import { test, assert, assertRange, log, QUICK, metric } from './kit';
import { Sim } from '../src/sim';
import { neutralControls, type Controls } from '../src/engine/input';
import { RNG } from '../src/engine/rng';
import { datan2 } from '../src/engine/dmath';
import { SURFACE } from '../src/world/surfaces';

function controls(p: Partial<Controls> = {}): Controls {
  return { ...neutralControls(), ...p };
}

/** Place the player on the circuit at arc length s (from the start line), facing along it. */
function placeOnTrack(sim: Sim, s: number, reverse = false): void {
  const road = sim.track.road;
  const at = { x: 0, y: 0, z: 0, tx: 0, tz: 0, i: 0 };
  road.at(sim.track.startS + s, at);
  const tx = reverse ? -at.tx : at.tx;
  const tz = reverse ? -at.tz : at.tz;
  sim.teleport(at.x, at.z, datan2(-tx, -tz));
}

test('keyboard steering at 200 km/h stays controllable with standard assists', () => {
  const sim = new Sim(3);
  placeOnTrack(sim, -1500);
  const v = sim.player.vehicle;
  v.setSpeed(55);
  let maxBeta = 0;
  let maxYaw = 0;
  // Hold right for 0.4 s, then let go: a digital lane change at 198 km/h.
  for (let i = 0; i < 240 * 3; i++) {
    const c = controls({ throttle: 0.6, steer: i < 96 ? 1 : 0, analogSteer: false, source: 'keyboard' });
    sim.step(c);
    maxBeta = Math.max(maxBeta, Math.abs(datan2(v.vLat, Math.max(1, v.vLong))));
    maxYaw = Math.max(maxYaw, Math.abs(v.yawRate));
  }
  log(`max sideslip ${(maxBeta * 57.3).toFixed(1)} deg, max yaw rate ${maxYaw.toFixed(2)} rad/s`);
  assert(maxBeta * 57.3 < 6, 'a keyboard tap at speed should not slide the car');
  assert(maxYaw > 0.05, 'the car should respond to steering');
});

test('laps count only when driven forward through all sectors', () => {
  const sim = new Sim(4);
  // Cross the line forward from just behind it: starts the clock, no lap time yet.
  placeOnTrack(sim, -40);
  const v = sim.player.vehicle;
  v.setSpeed(20);
  for (let i = 0; i < 240 * 4; i++) sim.step(controls({ throttle: 0.3 }));
  assert(sim.lap.laps === 1 && Number.isNaN(sim.lap.last), `first crossing should start lap 1 (${JSON.stringify(sim.lap)})`);
  // Reverse back over the line and forward again: no lap time.
  placeOnTrack(sim, 30, true);
  v.setSpeed(15);
  for (let i = 0; i < 240 * 5; i++) sim.step(controls({ throttle: 0.3 }));
  placeOnTrack(sim, -30);
  v.setSpeed(15);
  for (let i = 0; i < 240 * 5; i++) sim.step(controls({ throttle: 0.3 }));
  assert(Number.isNaN(sim.lap.last), `wrong-way shuffle over the line produced a lap time ${sim.lap.last}`);
  // Teleporting to the far side of the circuit and back must not count either.
  placeOnTrack(sim, sim.track.road.length * 0.5);
  sim.step(controls());
  placeOnTrack(sim, -20);
  v.setSpeed(15);
  for (let i = 0; i < 240 * 4; i++) sim.step(controls({ throttle: 0.3 }));
  assert(Number.isNaN(sim.lap.last), 'skipping a sector produced a lap time');
});

test('gravel trap slows a car that runs wide', () => {
  const sim = new Sim(5);
  const t = sim.track;
  // Find a gravel sample and drive into it from the track edge.
  const road = t.road;
  let k = -1;
  for (let i = 0; i < road.n; i++) if (t.cornerKind[i] === 2) { k = i; break; }
  assert(k >= 0, 'no gravel trap found');
  const side = t.outside[k];
  const rx = -road.tz[k] * side;
  const rz = road.tx[k] * side;
  sim.teleport(road.x[k] + rx * 4, road.z[k] + rz * 4, datan2(-rx, -rz));
  const v = sim.player.vehicle;
  v.setSpeed(25);
  let onGravel = 0;
  for (let i = 0; i < 240 * 2; i++) {
    sim.step(controls());
    if (v.wheels.some((w) => w.surface === SURFACE.gravel)) onGravel++;
  }
  log(`speed after 2 s into the gravel: ${(v.speed * 3.6).toFixed(0)} km/h`);
  assert(onGravel > 100, 'the car never reached the gravel');
  assert(v.speed < 18, `gravel barely slowed the car (${(v.speed * 3.6).toFixed(0)} km/h)`);
});

test('soak: random driving with resets stays finite, on the ground and in bounds', () => {
  const minutes = QUICK ? 2 : 10;
  const sim = new Sim(7, 'proving');
  const o = sim.track.origin;
  const rng = new RNG('soak');
  const v = sim.player.vehicle;
  let c = controls();
  let worstY = Infinity;
  let maxSpeed = 0;
  let resets = 0;
  let airborneLong = 0;
  const t0 = performance.now();
  for (let i = 0; i < minutes * 60 * 240; i++) {
    if (i % 120 === 0) {
      c = controls({
        throttle: rng.chance(0.7) ? rng.range(0.3, 1) : 0,
        brake: rng.chance(0.15) ? rng.range(0.3, 1) : 0,
        steer: rng.range(-1, 1),
        handbrake: rng.chance(0.08) ? 1 : 0,
        analogSteer: rng.chance(0.5),
      });
      v.aids.esc = rng.chance(0.5);
      v.aids.tcs = rng.chance(0.5);
      v.aids.abs = rng.chance(0.7);
    }
    sim.step(c);
    if (i % 2400 === 0 && rng.chance(0.3)) {
      sim.resetToRoad();
      resets++;
    }
    if (!v.pos.isFinite() || !v.vel.isFinite() || !v.quat.isFinite()) throw new Error(`NaN at tick ${i}`);
    worstY = Math.min(worstY, v.pos.y - sim.groundHeight(v.pos.x, v.pos.z));
    maxSpeed = Math.max(maxSpeed, v.speed);
    if (v.airTime > 3) airborneLong++;
    // Keep the bot inside the facility.
    if (Math.abs(v.pos.x - o.x) > 900 || Math.abs(v.pos.z - o.z - 200) > 500) {
      sim.resetToRoad();
      resets++;
    }
  }
  const ms = performance.now() - t0;
  log(`${minutes} min simulated in ${(ms / 1000).toFixed(1)} s: lowest COM ${worstY.toFixed(2)} m above ground, top ${(maxSpeed * 3.6).toFixed(0)} km/h, ${resets} resets`);
  metric('soakMinutes', minutes);
  metric('soakSeconds', ms / 1000);
  assertRange(worstY, 0.15, 10, 'lowest COM height above ground (m)');
  assert(airborneLong === 0, 'car was airborne for over 3 s on flat ground');
  assert(maxSpeed < 120, `implausible speed ${(maxSpeed * 3.6).toFixed(0)} km/h`);
});
