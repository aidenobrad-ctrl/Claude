// Halcyon in Unity: press Play in any scene and the island builds itself
// (terrain, roads, water, trees, buildings, the festival), the car spawns
// on the festival road, and the 240 Hz simulation from Halcyon.Core drives
// it. Set HalcyonGame.AutoBoot = false (or add your own HalcyonGame) to
// control start-up yourself.
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        /// <summary>The player's settings (graphics, assists, HUD and accessibility, sound), saved beside the career.</summary>
        public Settings Settings { get; private set; }
        GraphicsPreset? appliedGraphics;
        PauseMenu pause;
        PhotoMode photo;
        GameSound sound;
        VegetationRenderer veg;
        /// <summary>Photo mode hides the HUD (the race HUD, the maps, the GPS line, the racing line and the stunt panels).</summary>
        public bool HideHud => photo != null && photo.Active;
        /// <summary>A menu over the road (the festival, the map, the pause menu, the garage).</summary>
        public bool InMenu => (races != null && (races.MenuOpen || races.Busy)) || (mapScreen != null && mapScreen.Open) || (pause != null && pause.Open) || (garage != null && garage.IsOpen);

        IslandRuntime island;
        SkyDirector sky;
        TrafficView traffic;
        MapScreen mapScreen;
        float discoverAt, discoverSaveAt;
        /// <summary>What the player has found on the island (places, landmarks, roads, the fog of war).</summary>
        public Discoveries Discover { get; private set; }
        /// <summary>The GPS: a route to a destination set on the map, shown on the road, the maps and the HUD.</summary>
        public Gps Gps { get; private set; }
        GpsRibbon gpsRibbon;
        /// <summary>The stunts on the island (speed traps, speed zones, drift zones, danger signs) and the player's runs at them.</summary>
        public StuntTracker Stunts { get; private set; }
        StuntResult lastStunt;
        float lastStuntAt = -10;
        bool wasRacing;
        /// <summary>Accolades earned and not yet shown, and when the one showing went up.</summary>
        readonly List<Accolade> accoladeQueue = new List<Accolade>();
        float accoladeAt = -10, accoladeCheckAt;
        bool garageWasOpen;
        /// <summary>The corner minimap while driving.</summary>
        public bool ShowMinimap = true;
        public bool Racing => races != null && races.Racing;
        Light sunLight;
        /// <summary>Ambient traffic on the island's roads (off: an empty island).</summary>
        public bool Traffic = true;
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
        GUIStyle big, small, shadow, help;

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
            Settings = LoadSettings();
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

            loading = "Paving 62 km of road";
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
                veg = world.gameObject.AddComponent<VegetationRenderer>();
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
            loading = "Raising the landmarks";
            yield return null;
            var marks = LandmarkBuilder.Build(Landmarks.Find(data, Sim.Ground), world);
            marks.Sim = Sim;
            Discover = new Discoveries(data, Sim.Ground, Career.Profile);
            Career.BeforeSave = () => Discover?.Save();
            Gps = new Gps(data, Sim.Ground);
            gpsRibbon = GpsRibbon.Create(world, Gps);
            loading = "Marking the stunts";
            yield return null;
            Stunts = new StuntTracker(data, Career.Profile, Sim.Ground);
            StuntBuilder.Build(data, Stunts.All, world);
            mapScreen = MapScreen.Create(this, world);
            if (Traffic)
            {
                Sim.EnableTraffic();
                traffic = TrafficView.Create(world, Sim);
            }
            garage = Garage.Create(this, data, world);
            races = RaceController.Create(this, world);
            pause = PauseMenu.Create(this, world);
            photo = PhotoMode.Create(this, world);
            try
            {
                sound = GameSound.Create(this, world);
            }
            catch (System.Exception e)
            {
                // No audio device, or audio disabled: the game plays silent.
                Debug.LogWarning("Halcyon: no sound: " + e.Message);
            }
            ApplySettings();
            // A career from before the accolades may already have some.
            CheckAccolades();
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
            Sim.Player.vehicle.aids = Settings.Aids();
            Sim.Events.Clear();
            if (PlayerRig != null) Destroy(PlayerRig.gameObject);
            PlayerRig = CarRig.Create(Sim.Player.vehicle, specId, paint ?? (OverridePaint ? Paint : (Color?)null));
            CameraRig.Snap();
            accumulator = 0;
        }

        // --- Settings -------------------------------------------------------------------------------

        static string SettingsPath => Path.Combine(Application.persistentDataPath, "halcyon-settings.json");

        static Settings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath)) return Settings.FromJson(File.ReadAllText(SettingsPath));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Halcyon: settings not read, using the defaults: " + e.Message);
            }
            return new Settings();
        }

        public void SaveSettings()
        {
            try
            {
                File.WriteAllText(SettingsPath, Settings.ToJson());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Halcyon: settings not saved: " + e.Message);
            }
        }

        /// <summary>Put the settings into effect: units, HUD, palette, assists, camera, post-processing, lightning, sound, graphics.</summary>
        public void ApplySettings()
        {
            var s = Settings;
            Units.Imperial = s.Imperial;
            Ui.HudScale = (float)s.HudScale;
            Ui.Vision = s.Colours;
            if (Sim != null) Sim.Player.vehicle.aids = s.Aids();
            if (CameraRig != null)
            {
                CameraRig.Shake = (float)s.Shake;
                CameraRig.FovBase = (float)s.Fov;
            }
            EnvironmentSetup.SetMotionBlur(s.MotionBlur);
            SkyDirector.FlashScale = s.ReduceFlashes ? 0.25f : 1f;
            SkyDirector.Ambience = (float)s.Ambience;
            AudioListener.volume = (float)s.Master;
            ShowMinimap = s.Minimap;
            if (appliedGraphics != s.Graphics)
            {
                GraphicsQuality.Apply(s.Graphics, veg);
                appliedGraphics = s.Graphics;
            }
        }

        public void StartPhotoMode() => photo.Begin();

        /// <summary>The height of the ground or road under a point (for cameras).</summary>
        public float GroundY(Vector3 p) => island.SurfaceY(p);

        void OnApplicationQuit()
        {
            Career?.SaveNow();
            EnvironmentSetup.RestorePipeline();
        }

        // Leaving Play mode in the Editor destroys the game: the project's URP asset gets its settings back.
        void OnDestroy() => EnvironmentSetup.RestorePipeline();

        void Update()
        {
            if (!Ready) return;
            Career.Tick();
            // The GPS line on the road: not in a race (it has its own route), behind the garage or in photo mode.
            gpsRibbon.Show = !races.Racing && !garage.IsOpen && !HideHud;
            if (scripted == null && garage != null)
            {
                input.Poll();
                if (garage.IsOpen)
                {
                    garage.Tick(); // the garage reads the menu keys; the car waits
                    garageWasOpen = true;
                    return;
                }
                if (garageWasOpen)
                {
                    // Cars bought, parts fitted: the garage's accolades.
                    garageWasOpen = false;
                    CheckAccolades();
                }
                if (photo.Active)
                {
                    photo.Tick(); // the world is stopped; the camera is free
                    return;
                }
                if (pause.Open)
                {
                    pause.Tick();
                    return;
                }
                if (races.Busy) return;
                if (races.MenuOpen)
                {
                    races.TickMenu();
                    return;
                }
                if (mapScreen.Open)
                {
                    mapScreen.Tick(); // the world waits while the map is open
                    return;
                }
                if (input.Menu.map)
                {
                    mapScreen.Show();
                    return;
                }
                if (input.Menu.photo)
                {
                    photo.Begin();
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
            if (c.pressed[ControlAction.Pause])
            {
                if (scripted == null)
                {
                    pause.Show();
                    return;
                }
                Paused = !Paused;
            }
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
                Stunts.Reset();
            }
            // Into or out of a race the car is moved: whatever stunt was under way is off.
            if (races.Racing != wasRacing)
            {
                wasRacing = races.Racing;
                Stunts.Reset();
            }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (!Paused)
            {
                accumulator += dt;
                int steps = 0;
                while (accumulator >= Loop.SIM_DT && steps < 32)
                {
                    if (races.Racing) races.Step(c);
                    else
                    {
                        Sim.Step(c);
                        var stunt = Stunts.Step(Sim.Player.vehicle, Loop.SIM_DT);
                        if (stunt != null) OnStunt(stunt);
                    }
                    PlayerRig.Capture();
                    traffic?.Capture();
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
                    if (e.kind == SimEventKind.Impact && e.carId == Sim.Player.id)
                    {
                        CameraRig.Impact((float)System.Math.Min(1, e.value / 20000));
                        sound?.Impact(e.value);
                    }
                    else if (e.kind == SimEventKind.Lightning) sky.Lightning(e.x, e.z, e.value);
                }
                Sim.Events.Clear();
            }
            PlayerRig.Apply((float)(accumulator / Loop.SIM_DT));
            traffic?.Apply((float)(accumulator / Loop.SIM_DT));
            if (!Paused)
            {
                Explore();
                FollowRoute(dt);
            }
            if (races.Racing) races.Apply((float)(accumulator / Loop.SIM_DT));
        }

        /// <summary>Discoveries four times a second: the fog, the roads driven, places and landmarks found (with their rewards).</summary>
        void Explore()
        {
            if (Discover == null || Time.unscaledTime - discoverAt < 0.25f) return;
            discoverAt = Time.unscaledTime;
            // An imported career brings its own island.
            if (Discover.P != Career.Profile)
            {
                Discover = new Discoveries(Sim.World, Sim.Ground, Career.Profile);
                Stunts = new StuntTracker(Sim.World, Career.Profile, Sim.Ground);
            }
            var v = Sim.Player.vehicle;
            var notes = Discover.Update(v.pos.x, v.pos.z);
            foreach (var n in notes) Career.Say(n);
            if (notes.Count > 0) Career.MarkDirty();
            else if (Time.unscaledTime - discoverSaveAt > 30)
            {
                // The fog and the roads, now and then.
                discoverSaveAt = Time.unscaledTime;
                Career.MarkDirty();
            }
            // Places found and roads driven count towards accolades (checked at once after a find, and every few seconds).
            if (notes.Count > 0 || Time.unscaledTime - accoladeCheckAt > 5) CheckAccolades();
        }

        /// <summary>Fast travel to a place found: onto its road (or the proving ground's paddock), the traffic around it anew.</summary>
        public void FastTravel(Place p)
        {
            if (Racing || !Discover.FastTravel(p, out double x, out double z, out double yaw)) return;
            Sim.Teleport(x, z, yaw);
            Sim.Traffic?.Suspend();
            Sim.Traffic?.Resume();
            PlayerRig.Snap();
            CameraRig.Snap();
            accumulator = 0;
            Career.Say($"Travelled to {p.Name}");
            if (Gps.Active) Gps.Plan(x, z);
            Stunts.Reset();
        }

        /// <summary>A stunt done: the result on screen, the rewards saved.</summary>
        void OnStunt(StuntResult r)
        {
            lastStunt = r;
            lastStuntAt = Time.unscaledTime;
            sound?.Ui(r.Stars > 0 ? Core.Audio.UiSound.Reward : Core.Audio.UiSound.Checkpoint);
            if (r.Best || r.Xp > 0) Career.MarkDirty();
            if (r.Stars > 0) CheckAccolades();
        }

        /// <summary>Accolades earned in free roam (places, roads, stunts, the garage): paid, saved, and shown one at a time.</summary>
        void CheckAccolades()
        {
            accoladeCheckAt = Time.unscaledTime;
            var got = Accolades.Check(Career.Profile, Discover, Stunts);
            if (got.Count == 0) return;
            accoladeQueue.AddRange(got);
            Career.MarkDirty();
        }

        /// <summary>The accolade just earned, for four and a half seconds each.</summary>
        void DrawAccoladeBanner()
        {
            if (accoladeQueue.Count == 0 || HideHud) return;
            if (Time.unscaledTime - accoladeAt > 4.5f)
            {
                if (accoladeAt > 0) accoladeQueue.RemoveAt(0);
                accoladeAt = accoladeQueue.Count > 0 ? Time.unscaledTime : -10;
                if (accoladeQueue.Count == 0) return;
                sound?.Ui(Core.Audio.UiSound.Reward);
            }
            var a = accoladeQueue[0];
            float t = Time.unscaledTime - accoladeAt;
            float fade = Mathf.Clamp01(Mathf.Min(t * 4, (4.5f - t) * 2));
            Ui.Begin(hud: true);
            float w = 600, x = Ui.W / 2 - w / 2, y = Ui.H - 280;
            GUI.color = new Color(1, 1, 1, fade);
            Ui.Panel(new Rect(x, y, w, 104), 0.7f * fade);
            Ui.Fill(new Rect(x, y, w, 5), new Color(1f, 0.82f, 0.2f, fade));
            GUI.color = new Color(1f, 0.82f, 0.2f, fade);
            GUI.DrawTexture(new Rect(x + 22, y + 24, 56, 56), ProceduralTextures.Star());
            GUI.color = new Color(1, 1, 1, fade);
            GUI.Label(new Rect(x + 100, y + 10, w - 120, 28), $"ACCOLADE  ·  +{a.Xp:N0} XP", Ui.Sub);
            GUI.Label(new Rect(x + 100, y + 34, w - 120, 44), a.Name, Ui.Title);
            GUI.Label(new Rect(x + 100, y + 76, w - 120, 24), a.Description, Ui.Small);
            GUI.color = Color.white;
            Ui.End();
        }

        /// <summary>The stunt under way (its live score), and the last one done (score, stars, rewards) for a few seconds.</summary>
        void DrawStunts()
        {
            if (Stunts == null || races.Racing || mapScreen.Open || HideHud) return;
            Ui.Begin(hud: true);
            var star = ProceduralTextures.Star();
            var a = Stunts.Active;
            if (a != null)
            {
                float w = 340, x = Ui.W / 2 - w / 2, y = 112;
                Ui.Panel(new Rect(x, y, w, 70), 0.55f);
                Ui.Fill(new Rect(x, y, 5, 70), StuntBuilder.Colour(a.Kind));
                GUI.DrawTexture(new Rect(x + 16, y + 11, 48, 48), StuntBuilder.Icon(a.Kind));
                string live = a.Kind == StuntKind.DriftZone ? $"{Stunts.Live:N0}  ×{Stunts.Chain:0.0}" : a.Kind == StuntKind.DangerSign ? (Stunts.Live > 0 ? Units.Short(Stunts.Live) : "Hit it!") : Units.Speed(Stunts.Live / 3.6);
                GUI.Label(new Rect(x + 76, y + 6, w - 90, 30), a.KindName.ToUpperInvariant(), Ui.Sub);
                GUI.Label(new Rect(x + 76, y + 30, w - 90, 36), live, Ui.Title);
                // The targets: which star the score so far has reached.
                for (int k = 0; k < 3; k++)
                {
                    GUI.color = Stunts.Live >= a.Stars[k] ? new Color(1f, 0.82f, 0.2f) : new Color(1, 1, 1, 0.25f);
                    GUI.DrawTexture(new Rect(x + w - 96 + k * 28, y + 10, 24, 24), star);
                }
                GUI.color = Color.white;
            }
            if (lastStunt != null && Time.unscaledTime - lastStuntAt < 4f)
            {
                var r = lastStunt;
                float fade = Mathf.Clamp01((4f - (Time.unscaledTime - lastStuntAt)) * 2);
                float w = 520, x = Ui.W / 2 - w / 2, y = 200;
                GUI.color = new Color(1, 1, 1, fade);
                Ui.Panel(new Rect(x, y, w, 132), 0.65f * fade);
                Ui.Fill(new Rect(x, y, w, 5), StuntBuilder.Colour(r.Stunt.Kind) * new Color(1, 1, 1, fade));
                GUI.DrawTexture(new Rect(x + 20, y + 22, 72, 72), StuntBuilder.Icon(r.Stunt.Kind));
                GUI.Label(new Rect(x + 110, y + 14, w - 130, 30), r.Stunt.Name.ToUpperInvariant(), Ui.Sub);
                GUI.Label(new Rect(x + 110, y + 40, w - 130, 50), Score(r.Stunt, r.Score), Ui.Title);
                for (int k = 0; k < 3; k++)
                {
                    GUI.color = k < r.Stars ? new Color(1f, 0.82f, 0.2f, fade) : new Color(1, 1, 1, 0.22f * fade);
                    GUI.DrawTexture(new Rect(x + w - 128 + k * 36, y + 46, 32, 32), star);
                }
                GUI.color = new Color(1, 1, 1, fade);
                string paid = r.Xp > 0 ? $"+{r.Xp:N0} XP · +{r.Credits:N0} cr" : "";
                string best = r.Best ? "New best" : $"Best {Score(r.Stunt, Stunts.Best(r.Stunt))}";
                GUI.Label(new Rect(x + 110, y + 92, w - 130, 30), paid.Length > 0 ? $"{best} · {paid}" : best, Ui.Small);
                GUI.color = Color.white;
            }
            Ui.End();
        }

        /// <summary>A stunt's score in the player's units (speeds and jumps; drift points are points).</summary>
        static string Score(Stunt s, double score) =>
            s.Kind == StuntKind.DriftZone ? s.Format(score) : s.Kind == StuntKind.DangerSign ? Units.Short(score) : Units.Speed(score / 3.6);

        /// <summary>A GPS route to a place or landmark (the proving ground: its paddock).</summary>
        public void SetRoute(Place p)
        {
            double x = p.X, z = p.Z;
            if (p.Kind == "track")
            {
                x = Sim.World.Proving.spawnX;
                z = Sim.World.Proving.spawnZ;
            }
            SetRoute(x, z, p.Name);
        }

        /// <summary>A GPS route over the roads from the car to (x, z), reached at the road nearest it.</summary>
        public void SetRoute(double x, double z, string name)
        {
            if (Racing) return;
            var v = Sim.Player.vehicle;
            if (Gps.Set(v.pos.x, v.pos.z, x, z, name)) Career.Say($"GPS: {name}, {Gps.Distance(Gps.Route.Length)} by road");
            else Career.Say($"No road leads to {name}");
        }

        public void ClearRoute()
        {
            if (!Gps.Active) return;
            Gps.Clear();
            Career.Say("GPS route cleared");
        }

        /// <summary>The GPS follows the car every frame (not while racing: races keep their own route).</summary>
        void FollowRoute(float dt)
        {
            if (Gps == null || !Gps.Active || races.Racing) return;
            var v = Sim.Player.vehicle;
            string name = Gps.TargetName;
            if (Gps.Update(v.pos.x, v.pos.z, v.fwd.x, v.fwd.z, dt)) Career.Say($"You have arrived at {name}");
        }

        /// <summary>The GPS at the top of the screen: the next turn and how far to it, or how far to go.</summary>
        void DrawGps()
        {
            var g = Gps;
            if (g == null || !g.Active || races.Racing || mapScreen.Open || HideHud) return;
            string head, sub;
            if (g.Route == null)
            {
                head = "No road route";
                sub = $"to {g.TargetName}";
            }
            else if (g.OffBy > Core.Gps.OffRoute)
            {
                head = "Off the route";
                sub = $"finding a new way to {g.TargetName}";
            }
            else if (g.WrongWay)
            {
                head = "Turn around";
                sub = $"{g.TargetName} · {Core.Gps.Distance(g.Remaining)}";
            }
            else if (g.NextTurn(out var t, out double d))
            {
                head = $"{(t.Right ? "→" : "←")}  {Core.Gps.Distance(d)}";
                sub = $"{(t.Keep ? "Keep" : "Turn")} {(t.Right ? "right" : "left")} · {g.TargetName} {Core.Gps.Distance(g.Remaining)}";
            }
            else
            {
                head = $"↑  {Core.Gps.Distance(g.Remaining)}";
                sub = $"to {g.TargetName}";
            }
            Ui.Begin(hud: true);
            float w = 380, x = Ui.W / 2 - w / 2, y = 18;
            Ui.Panel(new Rect(x, y, w, 80), 0.55f);
            Ui.Fill(new Rect(x, y, 5, 80), GpsRibbon.Tint);
            GUI.Label(new Rect(x + 22, y + 4, w - 34, 46), head, Ui.Title);
            GUI.Label(new Rect(x + 22, y + 50, w - 34, 26), sub, Ui.Small);
            Ui.End();
        }

        /// <summary>
        /// [ and ] move the clock an hour; \ steps the weather through each sky and
        /// back to dynamic. The weather holds during a race (the AI plans for the
        /// road as it is), so it is not changed then.
        /// </summary>
        void WorldKeys(DriverInput.MenuKeys m, bool racing)
        {
            if (m.radio && sound != null) Career.Say(GameSound.StationName(sound.CycleStation()));
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
            // The garage has its own camera, and photo mode flies this one.
            if (!Ready || (garage != null && garage.IsOpen) || (photo != null && photo.Active)) return;
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
                big = new GUIStyle(GUI.skin.label) { fontSize = 60, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
                big.normal.textColor = Color.white;
                small = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.LowerRight };
                small.normal.textColor = new Color(1, 1, 1, 0.85f);
                shadow = new GUIStyle(big);
                shadow.normal.textColor = new Color(0, 0, 0, 0.5f);
                help = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperLeft };
                help.normal.textColor = new Color(1, 1, 1, 0.85f);
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
            if (HideHud) return;
            Ui.Begin(hud: true);
            var v = Sim.Player.vehicle;
            int speed = Units.SpeedValue(v.vLong);
            string gear = v.gear == -1 ? "R" : v.gear == 0 ? "N" : v.gear.ToString();
            float right = Ui.W - 34, bottom = Ui.H - 22;
            var r = new Rect(right - 300, bottom - 108, 300, 76);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), speed.ToString(), shadow);
            GUI.Label(r, speed.ToString(), big);
            GUI.Label(new Rect(right - 300, bottom - 36, 300, 28), $"{Units.SpeedUnit}   gear {gear}   {v.Rpm:F0} rpm", small);
            // Rev bar.
            float frac = Mathf.Clamp01((float)(v.Rpm / (v.Params.engine.redline * 1.05)));
            Ui.Fill(new Rect(right - 260, bottom - 4, 260, 6), new Color(1, 1, 1, 0.25f));
            Ui.Fill(new Rect(right - 260, bottom - 4, 260 * frac, 6), frac > 0.92f ? Ui.Bad : new Color(0.3f, 0.85f, 1f));
            float y = 10;
            if (Settings.KeyHints && !InMenu)
            {
                GUI.Label(new Rect(14, y, Ui.W - 28, 22), "W/S throttle & brake · A/D steer · Space handbrake · E/Q shift · R reset · C camera · B look back · L lights · N radio · G garage · T events · M map · P photo · [ ] time · \\ weather · Esc menu", help);
                y += 22;
            }
            var car = Career.Profile.CurrentCar;
            var (level, _, _) = Economy.Level(Career.Profile.Xp);
            string pi = car.Pi > 0 ? $" · {Performance.ClassOf(car.Pi)} {car.Pi}" : "";
            var wx = Sim.Weather;
            string skyText = $"{Sim.Clock.Label} · {Weather.Name(wx.State)}{(wx.Locked ? " (set)" : "")}{(Sim.Wetness > 0.15 ? $" · roads {(Sim.Wetness > 0.7 ? "soaked" : "wet")}" : "")}";
            GUI.Label(new Rect(14, y, Ui.W - 28, 22), $"{PlayerRig.Visual.Look.name}{pi} · {Career.Profile.Credits:N0} cr · level {level} · {skyText}", help);
            y += 22;
            if (Career.Notice != null && Time.unscaledTime - Career.NoticeAt < 5f)
                GUI.Label(new Rect(14, y, Ui.W - 28, 22), Career.Notice, help);
            if (Paused && !pause.Open)
                GUI.Label(new Rect(0, Ui.H / 2 - 50, Ui.W, 100), "PAUSED", Ui.Big);
            Ui.End();
            DrawGps();
            DrawStunts();
            DrawAccoladeBanner();
        }
    }
}
