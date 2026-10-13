// Generates unity/Assets/Halcyon/Core/Vehicles/Cars.cs from src/vehicles/cars.ts,
// so the C# car list can never drift from the TypeScript one.
//
//   node tools/run-ts.mjs tools/dev/emit-cars-cs.ts           write Cars.cs
//   node tools/run-ts.mjs tools/dev/emit-cars-cs.ts --check   exit 1 if Cars.cs is out of date
//
// Enum members are looked up in the C# declarations (Spec.cs, Tires.cs) by
// case-insensitive name. Every value is written as a C# literal that parses
// back to the identical double (JavaScript's shortest round-trip repr), so
// dotnet/Halcyon.Core.Tests can compare CARS with Cars.All bit for bit. A
// CarSpec field this script does not know about is an error rather than
// being dropped silently.
import fs from 'node:fs';
import path from 'node:path';
import { CARS } from '../../src/vehicles/cars';
import type { CarSpec } from '../../src/vehicles/spec';

const CORE = 'unity/Assets/Halcyon/Core/Vehicles';
const OUT = path.join(CORE, 'Cars.cs');

/** Members of `public enum <name> { A, B, ... }` in a C# source file. */
function csEnum(file: string, name: string): Map<string, string> {
  const src = fs.readFileSync(path.join(CORE, file), 'utf8');
  const m = new RegExp(`public enum ${name}\\s*\\{([^}]*)\\}`).exec(src);
  if (!m) throw new Error(`enum ${name} not found in ${file}`);
  const members = new Map<string, string>();
  for (const raw of m[1].split(',')) {
    const member = raw.trim();
    if (member) members.set(member.toLowerCase(), member);
  }
  return members;
}
const ENUMS = {
  Category: csEnum('Spec.cs', 'Category'),
  BodyType: csEnum('Spec.cs', 'BodyType'),
  Drivetrain: csEnum('Spec.cs', 'Drivetrain'),
  Aspiration: csEnum('Spec.cs', 'Aspiration'),
  DiffType: csEnum('Spec.cs', 'DiffType'),
  Compound: csEnum('Tires.cs', 'Compound'),
};
function en(type: keyof typeof ENUMS, value: string): string {
  const member = ENUMS[type].get(value.toLowerCase());
  if (!member) throw new Error(`no C# ${type} member for '${value}'`);
  return `${type}.${member}`;
}

/** A quantity that is a whole number by nature (kg, N·m, rpm, mm, N/m): written as is. */
function num(x: number): string {
  if (!Number.isFinite(x)) throw new Error(`non-finite value ${x}`);
  return String(x);
}
/** A real-valued quantity: always written with a decimal point (3.0, not 3). */
function real(x: number): string {
  const s = num(x);
  return /[.e]/.test(s) ? s : `${s}.0`;
}
function int(x: number): string {
  if (!Number.isInteger(x)) throw new Error(`expected an integer, got ${x}`);
  return String(x);
}
function str(s: string): string {
  return `"${s.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`;
}

/** Throw if an object carries a field the generator does not write. */
function known(what: string, obj: object, fields: string[]): void {
  for (const k of Object.keys(obj)) {
    if (!fields.includes(k)) throw new Error(`${what}: unknown field '${k}'; teach tools/dev/emit-cars-cs.ts about it`);
  }
}

let needsCollections = false;

function car(c: CarSpec): string {
  known(c.id, c, [
    'id', 'make', 'model', 'year', 'category', 'body', 'description', 'mass', 'weightFront', 'wheelbase', 'trackFront', 'trackRear',
    'cgHeight', 'length', 'width', 'height', 'groundClearance', 'yawInertiaScale', 'engine', 'drivetrain', 'awdFront', 'diff', 'gears',
    'final', 'reverse', 'shiftTime', 'tires', 'brakes', 'suspension', 'aero', 'steering', 'visual',
  ]);
  const e = c.engine;
  known(`${c.id}.engine`, e, ['layout', 'cylinders', 'displacement', 'aspiration', 'torque', 'idle', 'redline', 'limiter', 'inertia', 'turboLag']);
  known(`${c.id}.diff`, c.diff, ['type', 'preload', 'accel', 'decel']);
  known(`${c.id}.tires`, c.tires, ['compound', 'widthF', 'widthR', 'radius']);
  known(`${c.id}.brakes`, c.brakes, ['torqueF', 'torqueR']);
  known(`${c.id}.suspension`, c.suspension, ['freqF', 'freqR', 'dampF', 'dampR', 'arbF', 'arbR', 'travel']);
  known(`${c.id}.aero`, c.aero, ['cdA', 'clA', 'balance']);
  known(`${c.id}.steering`, c.steering, ['maxAngle', 'ratio']);
  known(`${c.id}.visual`, c.visual, ['paint', 'style', 'params']);

  const L: string[] = [];
  const f = (line: string): number => L.push(`                ${line}`);
  const g = (line: string): number => L.push(`                    ${line}`);
  L.push('            new CarSpec');
  L.push('            {');
  f(`id = ${str(c.id)},`);
  f(`make = ${str(c.make)},`);
  f(`model = ${str(c.model)},`);
  f(`year = ${int(c.year)},`);
  f(`category = ${en('Category', c.category)},`);
  f(`body = ${en('BodyType', c.body)},`);
  f(`description = ${str(c.description)},`);
  f(`mass = ${num(c.mass)},`);
  f(`weightFront = ${real(c.weightFront)},`);
  f(`wheelbase = ${real(c.wheelbase)},`);
  f(`trackFront = ${real(c.trackFront)},`);
  f(`trackRear = ${real(c.trackRear)},`);
  f(`cgHeight = ${real(c.cgHeight)},`);
  f(`length = ${real(c.length)},`);
  f(`width = ${real(c.width)},`);
  f(`height = ${real(c.height)},`);
  f(`groundClearance = ${real(c.groundClearance)},`);
  if (c.yawInertiaScale !== undefined) f(`yawInertiaScale = ${real(c.yawInertiaScale)},`);
  f('engine = new EngineSpec');
  f('{');
  g(`layout = ${str(e.layout)},`);
  g(`cylinders = ${int(e.cylinders)},`);
  g(`displacement = ${real(e.displacement)},`);
  g(`aspiration = ${en('Aspiration', e.aspiration)},`);
  g(`torque = new (double rpm, double nm)[] { ${e.torque.map(([rpm, nm]) => `(${num(rpm)}, ${num(nm)})`).join(', ')} },`);
  g(`idle = ${num(e.idle)},`);
  g(`redline = ${num(e.redline)},`);
  g(`limiter = ${num(e.limiter)},`);
  g(`inertia = ${real(e.inertia)},`);
  if (e.turboLag !== undefined) g(`turboLag = ${real(e.turboLag)},`);
  f('},');
  f(`drivetrain = ${en('Drivetrain', c.drivetrain)},`);
  if (c.awdFront !== undefined) f(`awdFront = ${real(c.awdFront)},`);
  f(`diff = new DiffSpec { type = ${en('DiffType', c.diff.type)}, preload = ${num(c.diff.preload)}, accel = ${real(c.diff.accel)}, decel = ${real(c.diff.decel)} },`);
  f(`gears = new[] { ${c.gears.map(real).join(', ')} },`);
  f(`final = ${real(c.final)},`);
  f(`reverse = ${real(c.reverse)},`);
  f(`shiftTime = ${real(c.shiftTime)},`);
  const t = c.tires;
  f(`tires = new TireSpec { compound = ${en('Compound', t.compound)}, widthF = ${num(t.widthF)}, widthR = ${num(t.widthR)}, radius = ${real(t.radius)} },`);
  f(`brakes = new BrakeSpec { torqueF = ${num(c.brakes.torqueF)}, torqueR = ${num(c.brakes.torqueR)} },`);
  const s = c.suspension;
  f(
    `suspension = new SuspensionSpec { freqF = ${real(s.freqF)}, freqR = ${real(s.freqR)}, dampF = ${real(s.dampF)}, dampR = ${real(s.dampR)}, ` +
      `arbF = ${num(s.arbF)}, arbR = ${num(s.arbR)}, travel = ${real(s.travel)} },`,
  );
  f(`aero = new AeroSpec { cdA = ${real(c.aero.cdA)}, clA = ${real(c.aero.clA)}, balance = ${real(c.aero.balance)} },`);
  f(`steering = new SteeringSpec { maxAngle = ${num(c.steering.maxAngle)}, ratio = ${real(c.steering.ratio)} },`);
  const v = c.visual;
  if (!Number.isInteger(v.paint) || v.paint < 0 || v.paint > 0xffffff) throw new Error(`${c.id}: paint must be a 24-bit RGB integer`);
  const paint = `0x${v.paint.toString(16).padStart(6, '0')}`;
  if (v.params) {
    needsCollections = true;
    const ps = Object.entries(v.params).map(([k, x]) => `{ ${str(k)}, ${real(x)} }`).join(', ');
    f(`visual = new CarVisual { paint = ${paint}, style = ${str(v.style)}, Params = new Dictionary<string, double> { ${ps} } },`);
  } else {
    f(`visual = new CarVisual { paint = ${paint}, style = ${str(v.style)} },`);
  }
  L.push('            },');
  return L.join('\n');
}

const ids = new Set<string>();
for (const c of CARS) {
  if (ids.has(c.id)) throw new Error(`duplicate car id ${c.id}`);
  ids.add(c.id);
}
const body = CARS.map(car).join('\n');
const usings = ['using System;', ...(needsCollections ? ['using System.Collections.Generic;'] : [])].join('\n');
const cs = `// The car list. All makes and models are fictional. Performance figures are
// measured by the web version's tests/physics.test.ts and recorded in
// docs/CARS.md. Generated from src/vehicles/cars.ts by
// tools/dev/emit-cars-cs.ts with identical values: edit the TypeScript and
// re-run \`node tools/run-ts.mjs tools/dev/emit-cars-cs.ts\` rather than
// editing this file. dotnet/Halcyon.Core.Tests checks every field against the
// TypeScript.
${usings}

namespace Halcyon.Core
{
    public static class Cars
    {
        /// <summary>CARS.</summary>
        public static readonly CarSpec[] All =
        {
${body}
        };

        /// <summary>carById(id). Throws for an unknown id.</summary>
        public static CarSpec ById(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].id == id) return All[i];
            }
            throw new ArgumentException($"unknown car {id}", nameof(id));
        }
    }
}
`;

const current = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : '';
if (process.argv.includes('--check')) {
  if (current !== cs) {
    console.error(`${OUT} is out of date: run node tools/run-ts.mjs tools/dev/emit-cars-cs.ts`);
    process.exit(1);
  }
  console.log(`${OUT} is up to date (${CARS.length} cars)`);
} else if (current === cs) {
  console.log(`${OUT} already up to date (${CARS.length} cars)`);
} else {
  fs.writeFileSync(OUT, cs);
  console.log(`wrote ${OUT} (${CARS.length} cars)`);
}
