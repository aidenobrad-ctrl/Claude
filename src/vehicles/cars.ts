// The car list. All makes and models are fictional. Performance figures are
// measured by tests/physics.test.ts and recorded in docs/CARS.md.
import type { CarSpec } from './spec';

export const CARS: CarSpec[] = [
  {
    id: 'halden-aster-gt',
    make: 'Halden',
    model: 'Aster GT',
    year: 2024,
    category: 'sports',
    body: 'coupe',
    description: 'Front-engined, rear-drive coupe with a twin-turbo straight six. Balanced and quick, with a rear end that rewards a careful right foot.',
    mass: 1490,
    weightFront: 0.52,
    wheelbase: 2.47,
    trackFront: 1.59,
    trackRear: 1.6,
    cgHeight: 0.46,
    length: 4.38,
    width: 1.86,
    height: 1.3,
    groundClearance: 0.12,
    engine: {
      layout: 'I6',
      cylinders: 6,
      displacement: 3.0,
      aspiration: 'twinturbo',
      torque: [[800, 230], [1500, 420], [1800, 500], [4600, 500], [5500, 478], [6500, 420], [7100, 360]],
      idle: 800,
      redline: 7000,
      limiter: 7200,
      inertia: 0.2,
      turboLag: 0.35,
    },
    drivetrain: 'RWD',
    diff: { type: 'lsd', preload: 80, accel: 0.35, decel: 0.2 },
    gears: [3.32, 2.13, 1.53, 1.21, 1.0, 0.82],
    final: 3.46,
    reverse: 3.2,
    shiftTime: 0.2,
    tires: { compound: 'sport', widthF: 255, widthR: 275, radius: 0.335 },
    brakes: { torqueF: 3000, torqueR: 1700 },
    suspension: { freqF: 1.9, freqR: 2.0, dampF: 0.33, dampR: 0.33, arbF: 26000, arbR: 14000, travel: 0.16 },
    aero: { cdA: 0.66, clA: 0.12, balance: 0.45 },
    steering: { maxAngle: 34, ratio: 13.5 },
    visual: { paint: 0xc7262e, style: 'coupe' },
  },
];

export function carById(id: string): CarSpec {
  const c = CARS.find((x) => x.id === id);
  if (!c) throw new Error(`unknown car ${id}`);
  return c;
}
