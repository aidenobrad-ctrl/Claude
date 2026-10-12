// Photo mode (P, right stick click, or from the pause menu): the world
// stops and the camera comes free. It orbits the car (the right mouse
// button, the right stick, J/L and I/K) and zooms (the wheel, the triggers,
// U/O); a panel sets the lens (field of view, depth of field focused on the
// car, aperture), the look (exposure, a filter, vignette) and the hour.
// Enter saves a PNG at twice the screen's size in the Photos folder, X hides
// the panel, Esc (or P) goes back to the road with the hour it had.
using System.Collections;
using System.IO;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class PhotoMode : MonoBehaviour
    {
        HalcyonGame game;
        public bool Active { get; private set; }
        bool pausedBefore;
        double hourBefore;
        // The orbit round the car: degrees round and up, metres away, the height looked at.
        float yaw, pitch = 10, dist = 7, lookHeight = 0.7f;
        int pick;
        bool hidePanel, shooting;
        float fov = 45, aperture = 4, exposure, vignette;
        bool dofOn = true;
        int filter;
        float shotAt = -10;
        string lastShot;

        static readonly string[] Filters = { "None", "Vivid", "Black and white", "Sepia", "Cool", "Warm", "Faded" };
        static readonly float[] Stops = { 1.4f, 2f, 2.8f, 4f, 5.6f, 8f, 11f, 16f, 22f };
        static readonly string[] Rows = { "Field of view", "Depth of field", "Aperture", "Exposure", "Filter", "Vignette", "Time of day", "Look at" };
        const int RowCount = 8;

        public static PhotoMode Create(HalcyonGame game, Transform parent)
        {
            var go = new GameObject("PhotoMode");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<PhotoMode>();
            p.game = game;
            return p;
        }

        /// <summary>Stop the world and free the camera, starting where the chase camera is.</summary>
        public void Begin()
        {
            if (Active) return;
            Active = true;
            pausedBefore = game.Paused;
            game.Paused = true;
            hourBefore = game.Sim.Clock.Hours;
            var car = game.PlayerRig.transform.position + Vector3.up * lookHeight;
            var cam = game.CameraRig.Cam.transform;
            var d = cam.position - car;
            dist = Mathf.Clamp(d.magnitude, 2, 40);
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 180;
            pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.y / Mathf.Max(0.01f, d.magnitude), -1, 1)) * Mathf.Rad2Deg, -10, 85);
            fov = Mathf.Round(game.CameraRig.Cam.fieldOfView / 5) * 5;
            hidePanel = false;
        }

        /// <summary>Back to the road: the island's own grade, no depth of field, the hour as it was.</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            EnvironmentSetup.ResetLook();
            EnvironmentSetup.SetDepthOfField(false);
            game.Sim.Clock.Set(hourBefore);
            game.Paused = pausedBefore;
            game.CameraRig.Snap();
        }

        /// <summary>The keys while photo mode is on (called by the game after it polls input).</summary>
        public void Tick()
        {
            var m = game.Driver.Menu;
            if (shooting) return;
            if (m.back || m.photo)
            {
                End();
                return;
            }
            if (m.clear) hidePanel = !hidePanel;
            if (m.select)
            {
                StartCoroutine(Shoot());
                return;
            }
            if (m.up) pick = (pick + RowCount - 1) % RowCount;
            if (m.down) pick = (pick + 1) % RowCount;
            if (m.left) Change(-1);
            if (m.right) Change(1);
            var look = game.Driver.Look;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -10, 85);
            dist = Mathf.Clamp(dist * Mathf.Exp(-0.12f * game.Driver.Zoom), 1.6f, 60);
        }

        void Change(int dir)
        {
            switch (pick)
            {
                case 0: fov = Mathf.Clamp(fov + 5 * dir, 15, 100); break;
                case 1: dofOn = !dofOn; break;
                case 2:
                    int i = System.Array.IndexOf(Stops, aperture);
                    aperture = Stops[Mathf.Clamp((i < 0 ? 3 : i) + dir, 0, Stops.Length - 1)];
                    break;
                case 3: exposure = Mathf.Clamp(exposure + 0.25f * dir, -2, 2); break;
                case 4: filter = (filter + dir + Filters.Length) % Filters.Length; break;
                case 5: vignette = Mathf.Clamp(vignette + 0.05f * dir, 0, 0.5f); break;
                case 6:
                    double h = System.Math.Floor(game.Sim.Clock.Hours) + dir;
                    game.Sim.Clock.Set((h % 24 + 24) % 24);
                    break;
                case 7: lookHeight = Mathf.Clamp(lookHeight + 0.2f * dir, 0, 3); break;
            }
        }

        string Value(int row)
        {
            switch (row)
            {
                case 0: return $"{fov:0}°";
                case 1: return dofOn ? (EnvironmentSetup.HasPost ? "On, on the car" : "On (needs URP)") : "Off";
                case 2: return $"f/{aperture:0.#}";
                case 3: return $"{(exposure > 0 ? "+" : "")}{exposure:0.00} EV";
                case 4: return Filters[filter];
                case 5: return $"{vignette * 100:0}%";
                case 6: return game.Sim.Clock.Label;
                default: return $"{lookHeight:0.0} m up";
            }
        }

        /// <summary>The filter as additions to the island's grade: saturation, contrast, warmth, a colour.</summary>
        void Look(out float sat, out float con, out float temp, out Color tint)
        {
            sat = con = temp = 0;
            tint = Color.white;
            switch (filter)
            {
                case 1: sat = 35; con = 18; break;
                case 2: sat = -100; con = 20; break;
                case 3: sat = -100; con = 8; tint = new Color(1f, 0.86f, 0.66f); break;
                case 4: temp = -35; tint = new Color(0.9f, 0.97f, 1.08f); break;
                case 5: temp = 35; sat = 8; break;
                case 6: sat = -35; con = -28; tint = new Color(1f, 0.97f, 0.92f); break;
            }
        }

        void LateUpdate()
        {
            if (!Active || game.PlayerRig == null) return;
            var cam = game.CameraRig.Cam;
            var focus = game.PlayerRig.transform.position + Vector3.up * lookHeight;
            var pos = focus + Quaternion.Euler(pitch, yaw, 0) * (Vector3.back * dist);
            float gy = game.GroundY(pos);
            if (pos.y < gy + 0.35f) pos.y = gy + 0.35f;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(focus - pos, Vector3.up));
            cam.fieldOfView = fov;
            // A full-frame lens for this field of view (24 mm sensor height), focused on the car.
            float focal = 12f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            EnvironmentSetup.SetDepthOfField(dofOn, Vector3.Distance(pos, focus), aperture, focal);
            Look(out float sat, out float con, out float temp, out Color tint);
            EnvironmentSetup.SetLook(exposure, sat, con, temp, tint, vignette);
        }

        IEnumerator Shoot()
        {
            shooting = true;
            yield return null; // a frame without the panel
            string dir = Path.Combine(Application.persistentDataPath, "Photos");
            string path = null;
            try
            {
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, $"halcyon-{System.DateTime.Now:yyyyMMdd-HHmmss}.png");
                ScreenCapture.CaptureScreenshot(path, 2);
            }
            catch (System.Exception e)
            {
                game.Career.Say("The photo could not be saved: " + e.Message);
                path = null;
            }
            yield return null;
            yield return null;
            shooting = false;
            if (path != null)
            {
                lastShot = path;
                shotAt = Time.unscaledTime;
                GameSound.Instance?.Ui(Core.Audio.UiSound.Photo);
            }
        }

        void OnGUI()
        {
            if (!Active || shooting) return;
            Ui.Begin();
            if (!hidePanel)
            {
                float w = 470, h = 120 + RowCount * 44, x = 30, y = Ui.H - h - 30;
                var r = new Rect(x, y, w, h);
                Ui.Panel(r, 0.62f);
                GUI.Label(new Rect(x + 24, y + 14, w - 48, 44), "Photo mode", Ui.Title);
                for (int i = 0; i < RowCount; i++)
                    Ui.Item(new Rect(x + 10, y + 66 + i * 44, w - 20, 40), Rows[i], i == pick, Value(i));
                GUI.Label(new Rect(x + 24, r.yMax - 46, w - 48, 40), "Right mouse / right stick: orbit · wheel: zoom · ↑↓←→ settings · Enter: photo · X: hide · Esc: back", Ui.Small);
            }
            if (lastShot != null && Time.unscaledTime - shotAt < 4)
            {
                var r = new Rect(Ui.W / 2 - 360, 40, 720, 60);
                Ui.Panel(r, 0.65f);
                GUI.Label(new Rect(r.x + 20, r.y + 6, r.width - 40, 26), "Photo saved", Ui.Sub);
                GUI.Label(new Rect(r.x + 20, r.y + 32, r.width - 40, 24), lastShot, Ui.Small);
            }
            Ui.End();
        }
    }
}
