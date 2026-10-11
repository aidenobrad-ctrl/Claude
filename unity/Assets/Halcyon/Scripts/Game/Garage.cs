// The garage: the roster on a turntable on the festival stage. Browse with
// left/right, pick a paint with up/down, Enter drives the car away, Esc goes
// back. Each car's figures are measured live by the same tests that write
// docs/CARS.md (Halcyon.Core.Telemetry), on a worker thread.
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class Garage : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        HalcyonGame game;
        Transform turntable;
        CarVisual display;
        int index;
        int paint = -1; // -1: the car's own colour
        float spin;
        Vector3 top, fw, rt;
        GUIStyle title, sub, body, keys;
        Task measuring;

        sealed class Perf
        {
            public double t100, t200, top, brake, g;
        }

        readonly ConcurrentDictionary<string, Perf> perf = new ConcurrentDictionary<string, Perf>();

        static readonly (string name, Color c)[] Paints =
        {
            ("Festival red", new Color(0.78f, 0.12f, 0.14f)),
            ("Sunrise orange", new Color(0.95f, 0.45f, 0.08f)),
            ("Lemon", new Color(0.93f, 0.78f, 0.12f)),
            ("Lagoon", new Color(0.05f, 0.66f, 0.64f)),
            ("Deep blue", new Color(0.08f, 0.22f, 0.62f)),
            ("Racing green", new Color(0.08f, 0.32f, 0.18f)),
            ("Pearl white", new Color(0.92f, 0.92f, 0.9f)),
            ("Graphite", new Color(0.22f, 0.23f, 0.25f)),
            ("Midnight", new Color(0.03f, 0.035f, 0.045f)),
            ("Magenta", new Color(0.78f, 0.12f, 0.45f)),
        };

        public static Garage Create(HalcyonGame game, WorldData data, Transform parent)
        {
            var go = new GameObject("Garage");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<Garage>();
            g.game = game;
            g.Build(data);
            return g;
        }

        void Build(WorldData data)
        {
            var s = data.Festival.stage;
            fw = Frame.Dir(s.fx, 0, s.fz);
            fw.y = 0;
            fw.Normalize();
            rt = Vector3.Cross(Vector3.up, fw);
            // On the stage platform (1.4 m high), a little forward of its centre.
            top = Frame.Pos(s.x, s.y, s.z) + Vector3.up * 1.4f + fw * 2.5f;
            var mb = new MeshBuilder(2);
            const int n = 64;
            const float R = 3.4f, H = 0.12f, Rin = R - 0.12f;
            var up = Vector3.up;
            int centre = mb.Add(up * H, up, new Vector2(0.5f, 0.5f));
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                // Dark disc, a glowing rim on top, and the side.
                mb.Tri(0, centre, mb.Add(d1 * Rin + up * H, up, Vector2.zero), mb.Add(d0 * Rin + up * H, up, Vector2.zero));
                mb.Quad(1, mb.Add(d0 * Rin + up * H, up, Vector2.zero), mb.Add(d1 * Rin + up * H, up, Vector2.zero), mb.Add(d1 * R + up * H, up, Vector2.zero), mb.Add(d0 * R + up * H, up, Vector2.zero));
                mb.Quad(0, mb.Add(d0 * R + up * H, d0, Vector2.zero), mb.Add(d1 * R + up * H, d1, Vector2.zero), mb.Add(d1 * R, d1, Vector2.zero), mb.Add(d0 * R, d0, Vector2.zero));
            }
            var disc = Mats.Make("Turntable", new Color(0.05f, 0.05f, 0.055f), 0.85f, 0.4f);
            var rim = Mats.Make("TurntableRim", new Color(0.9f, 0.95f, 1f), 0.5f);
            Mats.SetEmission(rim, new Color(1f, 0.55f, 0.25f) * 2.5f);
            var go = mb.ToObject("Turntable", transform, new[] { disc, rim });
            turntable = go.transform;
            turntable.position = top;
            top += Vector3.up * H;
        }

        public void Open(string currentId)
        {
            IsOpen = true;
            index = 0;
            for (int i = 0; i < Halcyon.Core.Cars.All.Length; i++) if (Halcyon.Core.Cars.All[i].id == currentId) index = i;
            paint = -1;
            Show();
            if (measuring == null) measuring = Task.Run(MeasureAll);
        }

        public void Close()
        {
            IsOpen = false;
            if (display != null) Destroy(display.Root);
            display = null;
        }

        void MeasureAll()
        {
            foreach (var spec in Halcyon.Core.Cars.All)
            {
                var p = VehicleParams.Build(spec);
                var a = Telemetry.AccelTest(p);
                var b = Telemetry.BrakeTest(p, 100);
                var s = Telemetry.SkidpadTest(p, 40);
                perf[spec.id] = new Perf { t100 = a.t100, t200 = a.t200, top = a.topKmh, brake = b.distance, g = s.g };
            }
        }

        CarSpec Spec => Halcyon.Core.Cars.All[index];

        void Show()
        {
            if (display != null) Destroy(display.Root);
            var spec = Spec;
            display = CarVisual.Create(spec.id, VehicleParams.Build(spec), paint >= 0 ? Paints[paint].c : (Color?)null);
            display.Root.transform.position = top;
            display.Root.transform.rotation = Quaternion.LookRotation(-fw) * Quaternion.Euler(0, spin, 0);
            display.SetLights(true, false);
        }

        /// <summary>Called by the game each frame while open, after it polls the input.</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            var m = game.Driver.Menu;
            int n = Halcyon.Core.Cars.All.Length;
            if (m.left) { index = (index + n - 1) % n; paint = -1; Show(); }
            if (m.right) { index = (index + 1) % n; paint = -1; Show(); }
            if (m.up || m.down)
            {
                paint = paint < 0 ? (m.up ? 0 : Paints.Length - 1) : paint + (m.up ? 1 : -1);
                if (paint >= Paints.Length || paint < -1) paint = -1;
                if (paint < 0) display.SetPaint(display.Look.paint = CarVisual.LoadStyle(Spec.id).paint);
                else display.SetPaint(Paints[paint].c);
            }
            if (m.select)
            {
                var color = paint >= 0 ? Paints[paint].c : (Color?)null;
                string id = Spec.id;
                Close();
                game.SwitchCar(id, color);
                return;
            }
            if (m.back)
            {
                Close();
                return;
            }
            spin += Time.unscaledDeltaTime * 14f;
            var rot = Quaternion.LookRotation(-fw) * Quaternion.Euler(0, spin, 0);
            turntable.rotation = Quaternion.Euler(0, spin, 0);
            if (display != null) display.Root.transform.rotation = rot;
        }

        void LateUpdate()
        {
            if (!IsOpen) return;
            var cam = game.CameraRig.Cam;
            var at = top + Vector3.up * 0.65f;
            var eye = top + fw * 8.8f + rt * 2.6f + Vector3.up * 2.1f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
            cam.fieldOfView = 30f;
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
                title.normal.textColor = Color.white;
                sub = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                sub.normal.textColor = new Color(1f, 0.62f, 0.3f);
                body = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                body.normal.textColor = new Color(1, 1, 1, 0.9f);
                keys = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerCenter };
                keys.normal.textColor = new Color(1, 1, 1, 0.7f);
            }
            var spec = Spec;
            var p = VehicleParams.Build(spec);
            float x = 36, y = 30;
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(new Rect(20, 20, 470, 330), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, 450, 44), $"{spec.make} {spec.model}", title);
            GUI.Label(new Rect(x, y + 44, 450, 24), $"{spec.year} · {ClassName(spec.category)} · {index + 1} of {Halcyon.Core.Cars.All.Length}", sub);
            string drive = spec.drivetrain.ToString();
            double kw = p.engine.peakPower / 1000.0;
            string engine = spec.engine.cylinders == 0 ? $"{spec.engine.layout} electric" : $"{spec.engine.displacement:0.0} L {spec.engine.layout} {Aspiration(spec.engine.aspiration)}";
            string lines =
                $"{engine}\n" +
                $"{kw:0} kW ({kw * 1.341:0} hp) · {p.engine.peakTorque:0} N·m · {drive}\n" +
                $"{spec.mass:0} kg · {spec.weightFront * 100:0}% front · {spec.tires.compound} tires\n";
            if (perf.TryGetValue(spec.id, out var f))
            {
                lines += $"\n0–100 km/h  {f.t100:0.00} s" + (f.t200 > 0 && f.top > 215 ? $"     0–200  {f.t200:0.0} s" : "") +
                         $"\nTop speed  {f.top:0} km/h\n100–0 km/h  {f.brake:0.0} m     Skidpad  {f.g:0.00} g";
            }
            else lines += "\nMeasuring on the test track…";
            GUI.Label(new Rect(x, y + 76, 450, 200), lines, body);
            GUI.Label(new Rect(x, y + 260, 450, 50), spec.description, new GUIStyle(body) { fontSize = 13, wordWrap = true });
            string paintName = paint >= 0 ? Paints[paint].name : "Factory colour";
            GUI.Label(new Rect(0, Screen.height - 60, Screen.width, 40),
                $"←/→ car   ↑/↓ paint: {paintName}   Enter drive   Esc back", keys);
        }

        static string ClassName(Category c) => c switch
        {
            Category.Compact => "Compact",
            Category.HotHatch => "Hot hatch",
            Category.Classic => "Classic",
            Category.Muscle => "Muscle",
            Category.Sports => "Sports",
            Category.GT => "GT",
            Category.Super => "Super",
            Category.Hyper => "Hyper",
            Category.Rally => "Rally",
            Category.OffRoad => "Off-road",
            Category.Truck => "Truck",
            Category.SUV => "SUV",
            _ => c.ToString(),
        };

        static string Aspiration(Halcyon.Core.Aspiration a) => a switch
        {
            Halcyon.Core.Aspiration.TwinTurbo => "twin-turbo",
            Halcyon.Core.Aspiration.Turbo => "turbo",
            Halcyon.Core.Aspiration.Supercharged => "supercharged",
            _ => "",
        };
    }
}
