// Check close legs on the baked control polylines themselves.
import { ROAD_EDGES, ROAD_NODES } from '../../src/world/data/roads';
for (const [cls, a, b, mid] of ROAD_EDGES) {
  const pts: [number, number][] = [[ROAD_NODES[a][1], ROAD_NODES[a][2]]];
  for (let k = 0; k < mid.length; k += 2) pts.push([mid[k], mid[k + 1]]);
  pts.push([ROAD_NODES[b][1], ROAD_NODES[b][2]]);
  const s = [0];
  for (let k = 1; k < pts.length; k++) s.push(s[k - 1] + Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]));
  let worst = Infinity;
  let at = '';
  for (let i = 0; i < pts.length; i++) for (let j = i + 1; j < pts.length; j++) {
    if (s[j] - s[i] < 60) continue;
    const d = Math.hypot(pts[i][0] - pts[j][0], pts[i][1] - pts[j][1]);
    if (d < worst) { worst = d; at = `s=${s[i].toFixed(0)} / ${s[j].toFixed(0)} pts ${i}/${j} of ${pts.length}`; }
  }
  if (worst < 30) console.log(`${ROAD_NODES[a][0]}->${ROAD_NODES[b][0]} cls ${cls}: control points ${worst.toFixed(1)} m apart at ${at}`);
}
import { World } from '../../src/world/world';
const w = new World(1);
for (const e of w.edges) {
  const [cls, a, b, mid] = ROAD_EDGES[e.id];
  let len = 0;
  let px = ROAD_NODES[a][1];
  let pz = ROAD_NODES[a][2];
  for (let k = 0; k <= mid.length; k += 2) {
    const x = k < mid.length ? mid[k] : ROAD_NODES[b][1];
    const z = k < mid.length ? mid[k + 1] : ROAD_NODES[b][2];
    len += Math.hypot(x - px, z - pz);
    px = x;
    pz = z;
  }
  const ratio = e.road.length / len;
  if (ratio > 1.03) console.log(`${w.nodes[e.a].id}->${w.nodes[e.b].id}: spline ${e.road.length.toFixed(0)} m vs polyline ${len.toFixed(0)} m (x${ratio.toFixed(2)}) cls ${cls}`);
}
