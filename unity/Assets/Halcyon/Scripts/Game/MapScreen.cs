// The island map (M, or d-pad down): the island painted from its data
// (terrain shading, forests, desert, water, towns and the roads by class)
// under the fog of war the player has not yet driven through, with the
// roads already driven picked out, the places and landmarks found, the
// player's car, road and map completion, a GPS route to any destination
// (one from the list, or anywhere clicked on the map) and fast travel to
// any place found. The same picture makes a north-up minimap in the
// corner while driving, with the GPS route on it. The painting runs on a
// worker thread at start-up; the overlays update as the player explores.
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class MapScreen : MonoBehaviour
    {
        const int Size = 1024;
        HalcyonGame game;
        /// <summary>Roads already driven, on the full map.</summary>
        public static readonly Color32 DrivenColour = new Color32(92, 214, 255, 255);
        Texture2D map, fogTex, dot, roadsTex;
        Task<Color32[]> paint;
        readonly byte[] fogBytes = new byte[Discoveries.FogN * Discoveries.FogN];
        Color32[] fogPixels, roadsPixels;
        int roadsPainted = -1;
        Discoveries roadsFor;
        float fogAt = -10;
        int pick;
        float confirmAt = -10;
        // The GPS route, simplified for drawing (world x, z), and the route it was made from.
        readonly List<Vector2> routeLine = new List<Vector2>();
        int routeVersion = -1;
        public bool Open { get; private set; }

        public static MapScreen Create(HalcyonGame game, Transform parent)
        {
            var go = new GameObject("Map");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<MapScreen>();
            m.game = game;
            var w = game.Sim.World;
            m.paint = Task.Run(() => Paint(w));
            m.fogTex = new Texture2D(Discoveries.FogN, Discoveries.FogN, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            m.fogPixels = new Color32[Discoveries.FogN * Discoveries.FogN];
            m.roadsTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "RoadsDriven", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            m.roadsPixels = new Color32[Size * Size];
            m.dot = ProceduralTextures.SoftDot();
            return m;
        }

        // --- Painting (worker thread: plain arrays only) ----------------------------------------------

        static Color32[] Paint(WorldData w)
        {
            var px = new Color32[Size * Size];
            double half = w.WorldHalf, mpp = 2 * half / Size;
            for (int py = 0; py < Size; py++)
            {
                // Row 0 is the bottom of the texture: the south (+z in Core axes).
                double z = half - (py + 0.5) * mpp;
                for (int pxi = 0; pxi < Size; pxi++)
                {
                    double x = -half + (pxi + 0.5) * mpp;
                    px[py * Size + pxi] = Ground(w, x, z, mpp);
                }
            }
            // Roads, widest first so junctions read cleanly.
            foreach (var e in w.Edges)
            {
                var c = e.info.id == "highway" ? new Color32(243, 199, 107, 255) : e.info.id == "dirt" ? new Color32(201, 167, 122, 255) : new Color32(244, 239, 228, 255);
                double width = e.info.id == "highway" ? 3.2 : e.info.id == "dirt" ? 1.6 : 2.2;
                for (int i = 0; i + 1 < e.n; i++) Line(px, e.x[i], e.z[i], e.x[i + 1], e.z[i + 1], half, mpp, width, c);
            }
            // The proving circuit.
            var road = w.Proving.road;
            for (double s = 0; s < road.length; s += 4)
            {
                road.At(s, out double ax, out _, out double az, out _, out _, out _);
                road.At(s + 4, out double bx, out _, out double bz, out _, out _, out _);
                Line(px, ax, az, bx, bz, half, mpp, 2.2, new Color32(90, 92, 98, 255));
            }
            // Buildings as dark specks.
            foreach (var b in w.Buildings) Line(px, b.x - 4, b.z, b.x + 4, b.z, half, mpp, 1.4, new Color32(110, 104, 98, 255));
            return px;
        }

        static Color32 Ground(WorldData w, double x, double z, double mpp)
        {
            int n = w.N;
            int i = (int)System.Math.Round((x - w.Origin) / w.Spacing), j = (int)System.Math.Round((z - w.Origin) / w.Spacing);
            if (i < 1 || j < 1 || i >= n - 1 || j >= n - 1) return new Color32(28, 72, 108, 255);
            int k = j * n + i;
            double h = w.Height[k];
            bool water = w.Water[k] != short.MinValue && h < w.Water[k] / 100.0 - 0.2;
            if (water || h < 0)
            {
                double depth = System.Math.Min(1, System.Math.Max(0, (w.Water[k] == short.MinValue ? -h : w.Water[k] / 100.0 - h) / 30));
                return Lerp(new Color32(70, 140, 180, 255), new Color32(26, 70, 108, 255), depth);
            }
            // Hillshade from the north-west.
            double dx = w.Height[k + 1] - w.Height[k - 1], dz = w.Height[k + n] - w.Height[k - n];
            double shade = System.Math.Max(0.55, System.Math.Min(1.25, 1 + (dx + dz) * 0.04 / w.Spacing * 4));
            Color32 c;
            if (w.Desert[k] > 120) c = new Color32(196, 150, 104, 255);
            else if (h > 520) c = new Color32(205, 205, 200, 255);
            else if (h > 300) c = new Color32(140, 132, 116, 255);
            else if (w.Forest[k] > 120) c = new Color32(70, 106, 64, 255);
            else if (h < 3) c = new Color32(218, 205, 160, 255); // beaches
            else c = new Color32(128, 152, 92, 255);
            return new Color32((byte)System.Math.Min(255, c.r * shade), (byte)System.Math.Min(255, c.g * shade), (byte)System.Math.Min(255, c.b * shade), 255);
        }

        static Color32 Lerp(Color32 a, Color32 b, double t) =>
            new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), 255);

        static void Line(Color32[] px, double ax, double az, double bx, double bz, double half, double mpp, double width, Color32 c)
        {
            double len = System.Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int steps = System.Math.Max(1, (int)(len / (mpp * 0.5)));
            int r = (int)System.Math.Ceiling(width / 2);
            for (int s = 0; s <= steps; s++)
            {
                double x = ax + (bx - ax) * s / steps, z = az + (bz - az) * s / steps;
                int cx = (int)((x + half) / mpp), cy = (int)((half - z) / mpp);
                for (int dy = -r; dy <= r; dy++)
                    for (int ddx = -r; ddx <= r; ddx++)
                    {
                        if (ddx * ddx + dy * dy > width * width / 4 + 0.3) continue;
                        int xx = cx + ddx, yy = cy + dy;
                        if (xx < 0 || yy < 0 || xx >= Size || yy >= Size) continue;
                        px[yy * Size + xx] = c;
                    }
            }
        }

        // --- State ----------------------------------------------------------------------------------

        public void Show()
        {
            Open = true;
            pick = 0;
            confirmAt = -10;
            RefreshFog();
            RefreshRoads();
        }

        public void Hide() => Open = false;

        void Update()
        {
            if (map == null && paint != null && paint.IsCompleted)
            {
                if (!paint.IsFaulted)
                {
                    map = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "IslandMap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                    map.SetPixels32(paint.Result);
                    map.Apply(true, true);
                }
                else Debug.LogWarning("Halcyon: the map could not be painted: " + paint.Exception?.GetBaseException().Message);
                paint = null;
            }
            if (Time.unscaledTime - fogAt > 1f) RefreshFog();
        }

        void RefreshFog()
        {
            var d = game.Discover;
            if (d == null) return;
            fogAt = Time.unscaledTime;
            d.FogBytes(fogBytes);
            int n = Discoveries.FogN;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    // Fog row j runs north to south; texture rows run south to north.
                    byte f = fogBytes[j * n + i];
                    fogPixels[(n - 1 - j) * n + i] = new Color32(14, 18, 26, (byte)(f > 0 ? 0 : 215));
                }
            fogTex.SetPixels32(fogPixels);
            fogTex.Apply(false);
        }

        /// <summary>The roads driven, painted over a clear texture (when the map opens and something new was driven).</summary>
        void RefreshRoads()
        {
            var d = game.Discover;
            // (An imported career brings new discoveries, so the count alone is not enough.)
            if (d == null || (d == roadsFor && d.DrivenSamples == roadsPainted)) return;
            roadsFor = d;
            roadsPainted = d.DrivenSamples;
            System.Array.Clear(roadsPixels, 0, roadsPixels.Length);
            var w = game.Sim.World;
            double half = w.WorldHalf, mpp = 2 * half / Size;
            foreach (var e in w.Edges)
                for (int i = 0; i + 1 < e.n; i++)
                    if (d.Driven(e.id, i) && d.Driven(e.id, i + 1)) Line(roadsPixels, e.x[i], e.z[i], e.x[i + 1], e.z[i + 1], half, mpp, 1.8, DrivenColour);
            roadsTex.SetPixels32(roadsPixels);
            roadsTex.Apply(false);
        }

        /// <summary>The places found (and the festival) then the landmarks found: where the GPS can take the player.</summary>
        List<Place> Destinations()
        {
            var d = game.Discover;
            var list = new List<Place>();
            foreach (var p in d.Places) if (!p.Landmark && (p.Kind == "festival" || d.Found(p))) list.Add(p);
            foreach (var p in d.Places) if (p.Landmark && d.Found(p)) list.Add(p);
            return list;
        }

        /// <summary>Keys while the map is open (called by the game after it polls the input).</summary>
        public void Tick()
        {
            var m = game.Driver.Menu;
            if (m.back || m.map)
            {
                Hide();
                return;
            }
            if (m.clear && game.Gps.Active)
            {
                game.ClearRoute();
                return;
            }
            var list = Destinations();
            if (list.Count == 0) return;
            if (m.up)
            {
                pick = (pick + list.Count - 1) % list.Count;
                confirmAt = -10;
            }
            if (m.down)
            {
                pick = (pick + 1) % list.Count;
                confirmAt = -10;
            }
            pick = Mathf.Clamp(pick, 0, list.Count - 1);
            var p = list[pick];
            if (m.select) game.SetRoute(p);
            if (m.travel && !p.Landmark && !game.Racing)
            {
                // Press twice: once to choose, again within 3 s to go.
                if (Time.unscaledTime - confirmAt < 3f)
                {
                    game.FastTravel(p);
                    confirmAt = -10;
                    Hide();
                }
                else confirmAt = Time.unscaledTime;
            }
        }

        // --- Drawing --------------------------------------------------------------------------------

        Vector2 ToMap(Rect r, double x, double z)
        {
            double half = game.Sim.World.WorldHalf;
            return new Vector2(r.x + (float)((x + half) / (2 * half)) * r.width, r.y + (float)((z + half) / (2 * half)) * r.height);
        }

        void ToWorld(Rect r, Vector2 p, out double x, out double z)
        {
            double half = game.Sim.World.WorldHalf;
            x = (p.x - r.x) / r.width * 2 * half - half;
            z = (p.y - r.y) / r.height * 2 * half - half;
        }

        /// <summary>The GPS route as few points as draw it to within 4 m (Douglas-Peucker), made again when the route changes.</summary>
        List<Vector2> RouteLine()
        {
            var gps = game.Gps;
            if (gps.Version == routeVersion) return routeLine;
            routeVersion = gps.Version;
            routeLine.Clear();
            var r = gps.Route;
            if (r == null || r.N < 2) return routeLine;
            var keep = new bool[r.N];
            keep[0] = keep[r.N - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, r.N - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                double dx = r.X[b] - r.X[a], dz = r.Z[b] - r.Z[a], len = System.Math.Sqrt(dx * dx + dz * dz);
                int worst = -1;
                double far = 4;
                for (int i = a + 1; i < b; i++)
                {
                    double ex = r.X[i] - r.X[a], ez = r.Z[i] - r.Z[a];
                    double off = len > 0 ? System.Math.Abs(ex * dz - ez * dx) / len : System.Math.Sqrt(ex * ex + ez * ez);
                    if (off > far)
                    {
                        far = off;
                        worst = i;
                    }
                }
                if (worst < 0) continue;
                keep[worst] = true;
                stack.Push((a, worst));
                stack.Push((worst, b));
            }
            for (int i = 0; i < r.N; i++) if (keep[i]) routeLine.Add(new Vector2((float)r.X[i], (float)r.Z[i]));
            return routeLine;
        }

        /// <summary>Clip the segment a-b to the rectangle (Liang-Barsky); false when none of it is inside.</summary>
        static bool Clip(Rect r, ref Vector2 a, ref Vector2 b)
        {
            float t0 = 0, t1 = 1;
            var d = b - a;
            bool Edge(float p, float q)
            {
                if (p == 0) return q >= 0;
                float t = q / p;
                if (p < 0)
                {
                    if (t > t1) return false;
                    if (t > t0) t0 = t;
                }
                else
                {
                    if (t < t0) return false;
                    if (t < t1) t1 = t;
                }
                return true;
            }
            if (!Edge(-d.x, a.x - r.xMin) || !Edge(d.x, r.xMax - a.x) || !Edge(-d.y, a.y - r.yMin) || !Edge(d.y, r.yMax - a.y)) return false;
            var a0 = a;
            a = a0 + d * t0;
            b = a0 + d * t1;
            return true;
        }

        /// <summary>The route drawn through a world-to-screen mapping, clipped to a rectangle: a dark edge, then the GPS colour.</summary>
        void DrawRoute(System.Func<Vector2, Vector2> at, Rect clip, float width)
        {
            if (Event.current.type != EventType.Repaint || !game.Gps.Active) return;
            var pts = RouteLine();
            var dark = new Color(0.08f, 0.04f, 0.16f, 0.85f);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = at(pts[i]);
                    var b = at(pts[i + 1]);
                    if (!Clip(clip, ref a, ref b)) continue;
                    if (pass == 0) Ui.Line(a, b, width + 3, dark);
                    else Ui.Line(a, b, width, GpsRibbon.Tint);
                }
        }

        void Marker(Vector2 p, float size, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(p.x - size / 2, p.y - size / 2, size, size), dot);
            GUI.color = Color.white;
        }

        void PlayerArrow(Vector2 p, float size)
        {
            var v = game.Sim.Player.vehicle;
            float yaw = Mathf.Atan2((float)v.fwd.x, (float)-v.fwd.z) * Mathf.Rad2Deg; // 0 = north (up)
            var keep = GUI.matrix;
            Ui.Rotate(yaw, p);
            Marker(p + new Vector2(0, -size * 0.35f), size * 0.55f, new Color(1f, 0.55f, 0.2f));
            Marker(p, size, new Color(1f, 0.6f, 0.25f));
            GUI.matrix = keep;
        }

        void OnGUI()
        {
            if (map == null || game.Discover == null) return;
            Ui.Begin();
            if (Open) DrawMap();
            else if (game.ShowMinimap) DrawMinimap();
            Ui.End();
        }

        void DrawMap()
        {
            var d = game.Discover;
            var gps = game.Gps;
            float side = Ui.H - 120;
            var r = new Rect(60, 60, side, side);
            bool racing = game.Racing;
            // A click on the map: a route to there (left), or no route (right).
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition) && !racing)
            {
                if (ev.button == 0)
                {
                    ToWorld(r, ev.mousePosition, out double wx, out double wz);
                    game.SetRoute(wx, wz, "the map marker");
                }
                else if (ev.button == 1) game.ClearRoute();
                ev.Use();
            }
            Ui.Fill(new Rect(0, 0, Ui.W, Ui.H), new Color(0.03f, 0.04f, 0.06f, 0.92f));
            GUI.DrawTexture(r, map);
            GUI.DrawTexture(r, roadsTex);
            GUI.DrawTexture(r, fogTex);
            DrawRoute(q => ToMap(r, q.x, q.y), r, 3.5f);
            if (gps.Active) Marker(ToMap(r, gps.TargetX, gps.TargetZ), 26, GpsRibbon.Tint);
            // Places and landmarks found; places not yet found show as a question mark once the fog over them lifts.
            foreach (var p in d.Places)
            {
                var at = ToMap(r, p.X, p.Z);
                bool found = d.Found(p);
                if (!found && !d.Uncovered(p.X, p.Z)) continue;
                Marker(at, p.Landmark ? 18 : 22, found ? (p.Landmark ? new Color(0.5f, 0.85f, 1f) : Color.white) : new Color(1, 1, 1, 0.45f));
                GUI.Label(new Rect(at.x + 12, at.y - 12, 260, 26), found ? p.Name : "?", Ui.Small);
            }
            PlayerArrow(ToMap(r, game.Sim.Player.vehicle.pos.x, game.Sim.Player.vehicle.pos.z), 22);

            // The side panel: completion, the GPS, and the destinations.
            float x = r.xMax + 40, w = Mathf.Max(360, Ui.W - x - 60);
            Ui.Panel(new Rect(x - 16, 60, w + 32, side));
            GUI.Label(new Rect(x, 76, w, 44), "Halcyon Island", Ui.Title);
            int places = 0, foundPlaces = 0, marks = 0, foundMarks = 0;
            foreach (var p in d.Places)
            {
                if (p.Landmark) { marks++; if (d.Found(p)) foundMarks++; }
                else { places++; if (d.Found(p)) foundPlaces++; }
            }
            GUI.Label(new Rect(x, 128, w, 26), $"Roads driven  {d.RoadShare * 100:0.0}%", Ui.Body);
            GUI.Label(new Rect(x, 156, w, 26), $"Map uncovered  {d.MapShare * 100:0.0}%", Ui.Body);
            GUI.Label(new Rect(x, 184, w, 26), $"Places  {foundPlaces} / {places}     Landmarks  {foundMarks} / {marks}", Ui.Body);
            GUI.Label(new Rect(x, 212, w, 26), $"{game.Sim.Clock.Label} · {Weather.Name(game.Sim.Weather.State)}", Ui.Small);
            // The key: the GPS route and the roads driven.
            Ui.Fill(new Rect(x, 252, 26, 4), GpsRibbon.Tint);
            GUI.Label(new Rect(x + 34, 240, w - 34, 26), gps.Active ? (gps.Route != null ? $"GPS to {gps.TargetName} · {Gps.Distance(gps.Remaining)}" : $"No road leads to {gps.TargetName}") : "GPS: no route set", Ui.Small);
            Ui.Fill(new Rect(x, 280, 26, 4), DrivenColour);
            GUI.Label(new Rect(x + 34, 268, w - 34, 26), "Roads you have driven", Ui.Small);
            GUI.Label(new Rect(x, 304, w, 30), "Destinations", Ui.Sub);
            var list = Destinations();
            float y = 340;
            var me = game.Sim.Player.vehicle.pos;
            for (int i = 0; i < list.Count && y < r.yMax - 100; i++)
            {
                var p = list[i];
                bool on = i == pick;
                double km = System.Math.Sqrt((p.X - me.x) * (p.X - me.x) + (p.Z - me.z) * (p.Z - me.z)) / 1000;
                string value = on && Time.unscaledTime - confirmAt < 3f ? "F again to go" : gps.Active && gps.TargetName == p.Name ? "GPS" : $"{km:0.0} km";
                if (Ui.Item(new Rect(x, y, w, 34), p.Landmark ? p.Name + "  ◊" : p.Name, on, value, !racing))
                {
                    pick = i;
                    if (!racing) game.SetRoute(p);
                }
                y += 36;
            }
            GUI.Label(new Rect(x, r.yMax - 84, w, 80), racing ? "No GPS or fast travel during a race." :
                "↑/↓ choose · Enter: GPS route · F twice: fast travel (places, not landmarks ◊) · X: clear the route · click the map: a route to there · M or Esc: close", Ui.Small);
        }

        void DrawMinimap()
        {
            // North-up, 900 m across, bottom left, with the fog and the GPS route.
            const float side = 230, span = 900;
            var v = game.Sim.Player.vehicle;
            double half = game.Sim.World.WorldHalf;
            var r = new Rect(24, Ui.H - side - 24, side, side);
            Ui.Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(0, 0, 0, 0.55f));
            float u = (float)((v.pos.x + half) / (2 * half)), vv = (float)((half - v.pos.z) / (2 * half));
            float k = (float)(span / (2 * half));
            var tc = new Rect(u - k / 2, vv - k / 2, k, k);
            GUI.DrawTextureWithTexCoords(r, map, tc);
            GUI.DrawTextureWithTexCoords(r, fogTex, tc);
            // World (x, z) to the minimap: the car at the centre, north up.
            float scale = side / span;
            Vector2 At(double x, double z) => new Vector2(r.center.x + (float)(x - v.pos.x) * scale, r.center.y + (float)(z - v.pos.z) * scale);
            var gps = game.Gps;
            if (gps.Active && !game.Racing)
            {
                DrawRoute(q => At(q.x, q.y), new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), 4f);
                var t = At(gps.TargetX, gps.TargetZ);
                if (r.Contains(t)) Marker(t, 16, GpsRibbon.Tint);
            }
            var d = game.Discover;
            foreach (var p in d.Places)
            {
                if (!d.Found(p)) continue;
                var at = At(p.X, p.Z);
                if (!r.Contains(at)) continue;
                Marker(at, p.Landmark ? 10 : 12, p.Landmark ? new Color(0.5f, 0.85f, 1f) : Color.white);
            }
            PlayerArrow(r.center, 18);
        }
    }
}
