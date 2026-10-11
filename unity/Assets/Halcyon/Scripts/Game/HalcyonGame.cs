// Halcyon in Unity: press Play in any scene and the island builds itself
// (terrain, roads, water, trees, buildings, the festival), the car spawns
// on the festival road, and the 240 Hz simulation from Halcyon.Core drives
// it. Set HalcyonGame.AutoBoot = false (or add your own HalcyonGame) to
// control start-up yourself.
using System.Collections;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class HalcyonGame : MonoBehaviour
    {
        public static bool AutoBoot = true;
        public static HalcyonGame Instance { get; private set; }

        public string CarId = "halden-aster-gt";
        /// <summary>Repaint the car (otherwise it wears its own colour from the Blender style file).</summary>
        public bool OverridePaint;
        public Color Paint = new Color(0.78f, 0.15f, 0.18f);
        public bool BuildTrees = true;
        public bool BuildBuildings = true;

        public Simulation Sim { get; private set; }
        /// <summary>The player's career: credits, XP and the garage, saved as they change.</summary>
        public Career Career { get; private set; }
        public CarRig PlayerRig { get; private set; }
        public ChaseCamera CameraRig { get; private set; }
        public bool Ready { get; private set; }
        public bool Paused;

        IslandRuntime island;
        SkyDirector sky;
        Light sunLight;
        DriverInput input;
        Garage garage;
        RaceController races;
        /// <summary>The keyboard and gamepad (menus read their keys here).</summary>
        public DriverInput Driver => input;
        string captureDir;
        Controls scripted;
        double accumulator;
        string loading = "Starting";
        float loadStart;
        GUIStyle big, small, shadow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!AutoBoot || FindFirstObjectByType<HalcyonGame>() != null) return;
            new GameObject("Halcyon").AddComponent<HalcyonGame>();
        }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
        }

        IEnumerator Start()
        {
            loadStart = Time.realtimeSinceStartup;
            SkyDirector.ClearRegistry();
            Career = new Career();
            var world = new GameObject("World").transform;
            world.SetParent(transform, false);
            loading = "Lighting the sky";
            yield return null;
            var sun = FindFirstObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional) Destroy(sun.gameObject);
            sunLight = EnvironmentSetup.Build(world);
            var cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            EnvironmentSetup.SetupCamera(cam, world);

            loading = "Reading the island";
            yield return null;
            island = gameObject.AddComponent<IslandRuntime>();
            var data = island.Data;

            loading = "Raising the terrain";
            yield return null;
            TerrainBuilder.Build(data, world);

            loading = "Paving 63 km of road";
            yield return null;
            RoadBuilder.Build(data, world);

            loading = "Filling the sea and lakes";
            yield return null;
            WaterBuilder.Build(data, world);

            if (BuildBuildings)
            {
                loading = "Building the towns";
                yield return null;
                BuildingBuilder.Build(data, world);
            }

            loading = "Setting up the festival";
            yield return null;
            FestivalBuilder.Build(data, world);

            if (BuildTrees)
            {
                loading = "Planting 185,000 trees";
                yield return null;
                var veg = world.gameObject.AddComponent<VegetationRenderer>();
                veg.Init(data);
            }

            loading = "Fuelling the car";
            yield return null;
            // The career's current car, with its parts and tune.
            var mine = Career.Profile.CurrentCar;
            Sim = new Simulation(data, SpawnAt.Island, mine.Id);
            Sim.PlayerTune = mine.Tune?.Clone();
            Sim.PlayerUpgrades = mine.Upgrades;
            Sim.Reset();
            input = new DriverInput();
            PlayerRig = CarRig.Create(Sim.Player.vehicle, Sim.Player.specId, PaintOf(mine) ?? (OverridePaint ? Paint : (Color?)null));
            CameraRig = new ChaseCamera(cam);
            CameraRig.Snap();
            UpdateCamera(0.016f);
            sky = SkyDirector.Create(world, Sim, cam, sunLight);
            garage = Garage.Create(this, data, world);
            races = RaceController.Create(this, world);
            Ready = true;
            Debug.Log($"Halcyon: ready in {Time.realtimeSinceStartup - loadStart:F1} s");
            captureDir = Arg("-halcyon-capture");
            if (captureDir != null) StartCoroutine(Capture(captureDir));
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        /// <summary>Drive a little, then render the standard viewpoints to PNGs and quit (playtest screenshots).</summary>
        IEnumerator Capture(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            scripted = new Controls { throttle = 1, source = InputSource.Script };
            float t0 = Time.time;
            while (Time.time - t0 < 4f) yield return null;
            scripted.throttle = 0.4;
            yield return Shot(dir, "chase");
            var cam = CameraRig.Cam;
            var data = island.Data;
            CameraRig.Mode = CamMode.Far;
            CameraRig.Snap();
            yield return Shot(dir, "chase-far");
            enabled = false; // freeze the camera rig for fixed viewpoints
            var f = data.Festival.stage;
            var stage = Frame.Pos(f.x, f.y, f.z);
            yield return Pose(dir, "festival", stage + new Vector3(90, 55, -70), stage);
            yield return Pose(dir, "island", new Vector3(-600, 2600, -5200), new Vector3(0, 0, 0));
            foreach (var poi in data.Pois)
            {
                if (poi.kind == "city") yield return Pose(dir, "city", Frame.Pos(poi.x, poi.y, poi.z) + new Vector3(160, 90, 160), Frame.Pos(poi.x, poi.y, poi.z));
                if (poi.name == "Highcrest") yield return Pose(dir, "village", Frame.Pos(poi.x, poi.y, poi.z) + new Vector3(-120, 60, 110), Frame.Pos(poi.x, poi.y, poi.z));
            }
            Debug.Log("Halcyon: screenshots written to " + dir);
            Application.Quit();
        }

        IEnumerator Pose(string dir, string name, Vector3 at, Vector3 lookAt)
        {
            var t = CameraRig.Cam.transform;
            t.position = at;
            t.rotation = Quaternion.LookRotation(lookAt - at, Vector3.up);
            CameraRig.Cam.fieldOfView = 55;
            yield return Shot(dir, name);
        }

        IEnumerator Shot(string dir, string name)
        {
            for (int i = 0; i < 10; i++) yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{name}-{Screen.width}x{Screen.height}.png"));
            yield return null;
        }

        static Color? PaintOf(OwnedCar car) => car.Paint == null ? (Color?)null : new Color((float)car.Paint[0], (float)car.Paint[1], (float)car.Paint[2]);

        /// <summary>Put the player in the career's current car (with its parts, tune and paint) where they stand.</summary>
        public void DriveCurrentCar()
        {
            var car = Career.Profile.CurrentCar;
            SwitchCar(car.Id, PaintOf(car), car.Tune, car.Upgrades);
        }

        /// <summary>Put the player in another car where they stand.</summary>
        public void SwitchCar(string specId, Color? paint, Tune tune = null, Upgrades up = null)
        {
            Sim.ChangePlayerCar(specId, tune, up);
            Sim.Events.Clear();
            if (PlayerRig != null) Destroy(PlayerRig.gameObject);
            PlayerRig = CarRig.Create(Sim.Player.vehicle, specId, paint ?? (OverridePaint ? Paint : (Color?)null));
            CameraRig.Snap();
            accumulator = 0;
        }

        void OnApplicationQuit()
        {
            Career?.SaveNow();
        }

        void Update()
        {
            if (!Ready) return;
            Career.Tick();
            if (scripted == null && garage != null)
            {
                input.Poll();
                if (garage.IsOpen)
                {
                    garage.Tick(); // the garage reads the menu keys; the car waits
                    return;
                }
                if (races.Busy) return;
                if (races.MenuOpen)
                {
                    races.TickMenu();
                    return;
                }
                if (races.Racing) races.TickRace();
                else if (input.Menu.garage)
                {
                    garage.Open();
                    return;
                }
                else if (input.Menu.events)
                {
                    races.OpenMenu();
                    return;
                }
                WorldKeys(input.Menu, races.Racing);
            }
            var c = scripted ?? input.Controls;
            if (c.pressed[ControlAction.Pause]) Paused = !Paused;
            if (c.pressed[ControlAction.Camera]) CameraRig.Cycle();
            if (c.pressed[ControlAction.Lights])
            {
                PlayerRig.ToggleLights();
                Career.Say(PlayerRig.LightsOverride.HasValue ? $"Headlights {(PlayerRig.Headlights ? "on" : "off")} (L again: automatic)" : "Headlights automatic");
            }
            CameraRig.LookBack = c.held[ControlAction.LookBack];
            if (c.pressed[ControlAction.Reset])
            {
                if (races.Racing) races.ResetPlayer();
                else Sim.ResetToRoad();
                PlayerRig.Snap();
                CameraRig.Snap();
            }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (!Paused)
            {
                accumulator += dt;
                int steps = 0;
                while (accumulator >= Loop.SIM_DT && steps < 32)
                {
                    if (races.Racing) races.Step(c);
                    else Sim.Step(c);
                    PlayerRig.Capture();
                    // Gear-shift edges apply to the first step of a frame only.
                    c.pressed[ControlAction.ShiftUp] = false;
                    c.pressed[ControlAction.ShiftDown] = false;
                    accumulator -= Loop.SIM_DT;
                    steps++;
                }
                if (steps == 32) accumulator = 0;
                // Only the player's own hits shake the camera (the player is the first car, so a
                // contact between the player and an AI car carries the player's id).
                foreach (var e in Sim.Events)
                {
                    if (e.kind == SimEventKind.Impact && e.carId == Sim.Player.id) CameraRig.Impact((float)System.Math.Min(1, e.value / 20000));
                    else if (e.kind == SimEventKind.Lightning) sky.Lightning(e.x, e.z, e.value);
                }
                Sim.Events.Clear();
            }
            PlayerRig.Apply((float)(accumulator / Loop.SIM_DT));
            if (races.Racing) races.Apply((float)(accumulator / Loop.SIM_DT));
        }

        /// <summary>
        /// [ and ] move the clock an hour; \ steps the weather through each sky and
        /// back to dynamic. The weather holds during a race (the AI plans for the
        /// road as it is), so it is not changed then.
        /// </summary>
        void WorldKeys(DriverInput.MenuKeys m, bool racing)
        {
            if (m.hourBack || m.hourForward)
            {
                Sim.Clock.Set(System.Math.Floor(Sim.Clock.Hours) + (m.hourForward ? 1 : -1));
                Career.Say($"Time {Sim.Clock.Label}");
            }
            if (m.weather)
            {
                if (racing)
                {
                    Career.Say("The weather is set for the race.");
                    return;
                }
                var w = Sim.Weather;
                if (!w.Locked) w.Force(Sky.Clear, snap: true);
                else if (w.State == Sky.Fog) w.Release();
                else w.Force(w.State + 1, snap: true);
                Career.Say(w.Locked ? $"Weather: {Weather.Name(w.State)} (\\ for the next)" : "Weather: dynamic");
            }
        }

        void LateUpdate()
        {
            if (!Ready || (garage != null && garage.IsOpen)) return;
            UpdateCamera(Mathf.Min(Time.deltaTime, 0.1f));
        }

        void UpdateCamera(float dt)
        {
            var v = Sim.Player.vehicle;
            var t = PlayerRig.transform;
            var vel = Frame.Dir(v.vel.x, v.vel.y, v.vel.z);
            CameraRig.Update(dt, t.position, t.rotation, vel, 0.55f, 0.35f, p => island.SurfaceY(p));
        }

        void OnGUI()
        {
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
                big.normal.textColor = Color.white;
                small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.LowerRight };
                small.normal.textColor = new Color(1, 1, 1, 0.85f);
                shadow = new GUIStyle(big);
                shadow.normal.textColor = new Color(0, 0, 0, 0.5f);
            }
            if (garage != null && garage.IsOpen) return;
            if (!Ready)
            {
                GUI.color = new Color(0.05f, 0.06f, 0.08f, 1);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                var title = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = 52 };
                GUI.Label(new Rect(0, Screen.height * 0.38f, Screen.width, 70), "HALCYON", title);
                var sub = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
                GUI.Label(new Rect(0, Screen.height * 0.38f + 70, Screen.width, 30), loading + "...", sub);
                return;
            }
            var v = Sim.Player.vehicle;
            int kmh = Mathf.RoundToInt((float)(System.Math.Abs(v.vLong) * 3.6));
            string gear = v.gear == -1 ? "R" : v.gear == 0 ? "N" : v.gear.ToString();
            var r = new Rect(Screen.width - 260, Screen.height - 110, 230, 60);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), $"{kmh} km/h", shadow);
            GUI.Label(r, $"{kmh} km/h", big);
            GUI.Label(new Rect(Screen.width - 260, Screen.height - 48, 230, 30), $"gear {gear}   {v.Rpm:F0} rpm", small);
            // Rev bar.
            float frac = Mathf.Clamp01((float)(v.Rpm / (v.Params.engine.redline * 1.05)));
            GUI.color = new Color(1, 1, 1, 0.25f);
            GUI.DrawTexture(new Rect(Screen.width - 250, Screen.height - 18, 220, 6), Texture2D.whiteTexture);
            GUI.color = frac > 0.92f ? new Color(1f, 0.3f, 0.2f) : new Color(0.3f, 0.85f, 1f);
            GUI.DrawTexture(new Rect(Screen.width - 250, Screen.height - 18, 220 * frac, 6), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var help = new GUIStyle(small) { alignment = TextAnchor.UpperLeft, fontSize = 13 };
            GUI.Label(new Rect(14, 10, 1100, 22), "W/S throttle & brake · A/D steer · Space handbrake · E/Q shift · R reset · C camera · B look back · L lights · G garage · T events · [ ] time · \\ weather · Esc pause", help);
            var car = Career.Profile.CurrentCar;
            var (level, _, _) = Economy.Level(Career.Profile.Xp);
            string pi = car.Pi > 0 ? $" · {Performance.ClassOf(car.Pi)} {car.Pi}" : "";
            var wx = Sim.Weather;
            string skyText = $"{Sim.Clock.Label} · {Weather.Name(wx.State)}{(wx.Locked ? " (set)" : "")}{(Sim.Wetness > 0.15 ? $" · roads {(Sim.Wetness > 0.7 ? "soaked" : "wet")}" : "")}";
            GUI.Label(new Rect(14, 30, 1100, 22), $"{PlayerRig.Visual.Look.name}{pi} · {Career.Profile.Credits:N0} cr · level {level} · {skyText}", help);
            if (Career.Notice != null && Time.unscaledTime - Career.NoticeAt < 5f)
            {
                GUI.Label(new Rect(14, 50, 900, 22), Career.Notice, help);
            }
            if (Paused)
            {
                var p = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, Screen.height / 2 - 40, Screen.width, 80), "PAUSED", p);
            }
        }
    }
}
