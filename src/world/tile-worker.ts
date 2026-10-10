// Tile worker: builds terrain tile meshes and tree cells off the main
// thread from its own copy of the (deterministic) world.
import { World } from './world';
import { buildTerrainTile, buildTreeCell } from './terrain-data';

interface Req {
  type: 'init' | 'terrain' | 'trees';
  id: number;
  seed?: number;
  level?: number;
  ix?: number;
  iz?: number;
  cx?: number;
  cz?: number;
  keep?: number;
}

let world: World | null = null;
const ctx = self as unknown as { postMessage(m: unknown, transfer?: Transferable[]): void; onmessage: ((e: MessageEvent<Req>) => void) | null };

ctx.onmessage = (e) => {
  const m = e.data;
  if (m.type === 'init') {
    world = new World(m.seed ?? 1);
    ctx.postMessage({ type: 'ready', id: m.id });
    return;
  }
  if (!world) return;
  if (m.type === 'terrain') {
    const d = buildTerrainTile(world, m.level ?? 0, m.ix ?? 0, m.iz ?? 0);
    const transfer: Transferable[] = [];
    for (const a of [d.pos, d.nrm, d.col, d.spl, d.waterPos, d.waterAttr]) if (a) transfer.push(a.buffer);
    ctx.postMessage({ type: 'terrain', id: m.id, data: d }, transfer);
  } else if (m.type === 'trees') {
    const t = buildTreeCell(world, m.cx ?? 0, m.cz ?? 0, m.keep ?? 1);
    ctx.postMessage({ type: 'trees', id: m.id, cx: m.cx, cz: m.cz, keep: m.keep, trees: t }, [t.buffer]);
  }
};
