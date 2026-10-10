// Closest approach between far-apart parts of the baked control polylines (dense sampling).
import { ROAD_EDGES, ROAD_NODES } from '../../src/world/data/roads';
for (const [cls, a, b, mid] of ROAD_EDGES) {
  const ctl: [number, number][] = [[ROAD_NODES[a][1], ROAD_NODES[a][2]]];
  for (let k = 0; k < mid.length; k += 2) ctl.push([mid[k], mid[k + 1]]);
  ctl.push([ROAD_NODES[b][1], ROAD_NODES[b][2]]);
  const pts: [number, number][] = [];
  for (let k = 0; k + 1 < ctl.length; k++) {
    const len = Math.hypot(ctl[k + 1][0] - ctl[k][0], ctl[k + 1][1] - ctl[k][1]);
    const n = Math.max(1, Math.ceil(len / 3));
    for (let t = 0; t < n; t++) pts.push([ctl[k][0] + ((ctl[k + 1][0] - ctl[k][0]) * t) / n, ctl[k][1] + ((ctl[k + 1][1] - ctl[k][1]) * t) / n]);
  }
  const s = [0];
  for (let k = 1; k < pts.length; k++) s.push(s[k - 1] + Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]));
  let worst = Infinity;
  let at = '';
  for (let i = 0; i < pts.length; i += 2) for (let j = i + 1; j < pts.length; j += 2) {
    if (s[j] - s[i] < 60) continue;
    const d = Math.hypot(pts[i][0] - pts[j][0], pts[i][1] - pts[j][1]);
    if (d < worst) { worst = d; at = `s=${s[i].toFixed(0)} / ${s[j].toFixed(0)} (${pts[i][0].toFixed(0)}, ${pts[i][1].toFixed(0)})`; }
  }
  if (worst < 22) console.log(`${ROAD_NODES[a][0]}->${ROAD_NODES[b][0]} cls ${cls}: ${worst.toFixed(1)} m at ${at}`);
}
