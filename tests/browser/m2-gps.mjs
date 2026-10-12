// M2: the GPS. A waypoint gives a road route, and a waypoint far from any
// road (the Cape Lighthouse, about 600 m off) is reached at the road nearest it.

export const scenarios = [
  {
    name: 'GPS: a route to a waypoint off the road, arriving at the road nearest it',
    async run(t) {
      const g = await t.open({ viewport: 'desktop', query: 'test=1&q=low' });
      const r = await g.page.evaluate(() => {
        const api = window.__game;
        const game = api.game;
        const sp = game.sim.islandSpawn();
        api.teleport(sp.x, sp.z, sp.yaw);
        api.setInput({});
        game.setWaypoint(4064, -1936);
        const route = game.route;
        if (!route) return { routed: false };
        const pts = route.points;
        const n = pts.length;
        const first = pts[0];
        const end = pts[n - 1];
        const start = Math.hypot(first.x - sp.x, first.z - sp.z);
        // Drive up to 12 m before the end of the route, facing along it.
        let k = n - 1;
        let d = 0;
        while (k > 0 && d < 12) {
          d += Math.hypot(pts[k].x - pts[k - 1].x, pts[k].z - pts[k - 1].z);
          k--;
        }
        const a = pts[k];
        const c = pts[Math.min(n - 1, k + 1)];
        api.teleport(a.x, a.z, Math.atan2(-(c.x - a.x), -(c.z - a.z)));
        for (let i = 0; i < 12; i++) api.step(20);
        return { routed: true, length: route.length, start, endOff: Math.hypot(end.x - 4064, end.z + 1936), cleared: game.waypoint === null && game.route === null };
      });
      t.assert(r.routed, 'no route to the waypoint');
      t.range(r.length, 3000, 20000, 'route length (m)');
      t.assert(r.start < 60, `the route starts ${r.start.toFixed(0)} m from the car`);
      t.range(r.endOff, 300, 800, 'distance from the route end to the waypoint (m)');
      t.assert(r.cleared, 'the GPS did not say it had arrived at the end of the route');
    },
  },
];
