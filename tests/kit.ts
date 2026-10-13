// Minimal test framework for headless simulation tests. Each *.test.ts file
// is bundled and run in its own Node process by tools/run-sim-tests.mjs.
import fs from 'node:fs';
import path from 'node:path';

type Fn = () => void | Promise<void>;
interface TestCase {
  name: string;
  fn: Fn;
}

const cases: TestCase[] = [];
const metrics: Record<string, unknown> = {};

/** True when the runner was started with --quick (fewer seeds, shorter soaks). */
export const QUICK = process.env.SIM_QUICK === '1';

export function test(name: string, fn: Fn): void {
  cases.push({ name, fn });
}

/** Record a measured value; the runner writes these to artifacts/metrics. */
export function metric(key: string, value: unknown): void {
  metrics[key] = value;
}

export class AssertionError extends Error {}

export function assert(cond: unknown, msg: string): asserts cond {
  if (!cond) throw new AssertionError(msg);
}

export function assertRange(v: number, lo: number, hi: number, what: string): void {
  if (!(v >= lo && v <= hi)) throw new AssertionError(`${what} = ${fmt(v)}, expected ${fmt(lo)}..${fmt(hi)}`);
}

export function assertNear(v: number, expected: number, tol: number, what: string): void {
  if (!(Math.abs(v - expected) <= tol)) throw new AssertionError(`${what} = ${fmt(v)}, expected ${fmt(expected)} ± ${fmt(tol)}`);
}

export function fmt(v: number): string {
  return Number.isFinite(v) ? (Math.abs(v) >= 100 ? v.toFixed(1) : v.toFixed(3)) : String(v);
}

export function log(...args: unknown[]): void {
  console.log('    ', ...args);
}

export function writeArtifact(name: string, content: string | Uint8Array): string {
  const p = path.join(process.cwd(), 'artifacts', name);
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, content);
  return p;
}

setTimeout(async () => {
  const filter = process.env.TEST_FILTER;
  let pass = 0;
  let fail = 0;
  for (const c of cases) {
    if (filter && !c.name.toLowerCase().includes(filter.toLowerCase())) continue;
    const t0 = performance.now();
    try {
      await c.fn();
      pass++;
      console.log(`  ✓ ${c.name} (${(performance.now() - t0).toFixed(0)} ms)`);
    } catch (e) {
      fail++;
      const err = e as Error;
      const stack = err instanceof AssertionError ? err.message : (err.stack ?? String(err)).split('\n').slice(0, 6).join('\n      ');
      console.log(`  ✗ ${c.name}\n      ${stack}`);
    }
  }
  console.log(`@@RESULT ${JSON.stringify({ pass, fail, metrics })}`);
  process.exit(fail ? 1 : 0);
}, 0);
