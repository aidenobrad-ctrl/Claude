// The player's career in Unity. The profile (credits, XP, the garage with
// each car's parts, tune and paint) is JSON in Application.persistentDataPath:
//   - saved a moment after every change, and when the game quits;
//   - written to a temporary file first, the previous save kept as .bak;
//   - a save that cannot be read is set aside (never overwritten) and the
//     backup is tried before a new career starts;
//   - exported to and imported from the clipboard.
// Measurements (a car's PI after a change) run on worker threads: the Core
// is thread-safe, and nothing here touches Unity objects off the main thread.
using System;
using System.IO;
using System.Threading.Tasks;
using Halcyon.Core;
using UnityEngine;

namespace Halcyon
{
    public sealed class Career
    {
        public Profile Profile { get; private set; }
        /// <summary>Measured PIs of every car and upgrade step, shared by the dealer and the rival fields.</summary>
        public readonly PiTable Pis = new PiTable();
        /// <summary>A message for the screen (save restored, career imported...), and when it was set.</summary>
        public string Notice { get; private set; }
        public float NoticeAt { get; private set; } = -100;
        readonly string path;
        float dirtyAt = -1;

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "halcyon-career.json");

        public Career(string path = null)
        {
            this.path = path ?? DefaultPath;
            Load();
            foreach (var car in Profile.Garage) if (car.Pi <= 0) Remeasure(car);
            // Warm the stock PIs for the dealer in the background.
            Task.Run(() =>
            {
                foreach (var c in Cars.All) Pis.Get(c.id, Rivals.Ladder[0]);
            });
        }

        public void Say(string message)
        {
            Notice = message;
            NoticeAt = Time.unscaledTime;
        }

        void Load()
        {
            try
            {
                if (File.Exists(path))
                {
                    Profile = Profile.FromJson(File.ReadAllText(path));
                    return;
                }
            }
            catch (Exception e)
            {
                string aside = path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Move(path, aside); }
                catch (Exception) { /* leave it where it is */ }
                Debug.LogWarning($"Halcyon: the career save could not be read ({e.Message}); kept as {aside}");
                try
                {
                    if (File.Exists(path + ".bak"))
                    {
                        Profile = Profile.FromJson(File.ReadAllText(path + ".bak"));
                        Say("Your save was damaged, so the previous one was restored.");
                        SaveNow();
                        return;
                    }
                }
                catch (Exception e2)
                {
                    Debug.LogWarning($"Halcyon: the backup save could not be read either ({e2.Message})");
                }
                Say("Your save was damaged and has been set aside. A new career has started.");
            }
            Profile = Profile.New();
            SaveNow();
        }

        /// <summary>Something changed: save a moment from now (several changes make one write).</summary>
        public void MarkDirty() => dirtyAt = Time.unscaledTime;

        /// <summary>Called every frame by the game.</summary>
        public void Tick()
        {
            if (dirtyAt >= 0 && Time.unscaledTime - dirtyAt > 0.5f) SaveNow();
        }

        public void SaveNow()
        {
            dirtyAt = -1;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Profile.ToJson());
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Halcyon: could not save the career to {path}: {e.Message}");
                Say("The career could not be saved: " + e.Message);
            }
        }

        public void ExportToClipboard()
        {
            GUIUtility.systemCopyBuffer = Profile.ToJson();
            Say("Career copied to the clipboard.");
        }

        /// <summary>Replaces the career with one from the clipboard (the current one is kept as .bak).</summary>
        public bool ImportFromClipboard(out string why)
        {
            why = null;
            string text = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("{"))
            {
                why = "the clipboard does not hold a career";
                return false;
            }
            try
            {
                var p = Profile.FromJson(text);
                Profile = p;
                foreach (var car in Profile.Garage) if (car.Pi <= 0) Remeasure(car);
                SaveNow();
                Say("Career imported from the clipboard.");
                return true;
            }
            catch (Exception e)
            {
                why = "the career in the clipboard is damaged (" + e.Message + ")";
                return false;
            }
        }

        /// <summary>Measures a car's PI with its parts and tune in the background; the save follows.</summary>
        public void Remeasure(OwnedCar car)
        {
            var spec = car.Spec;
            var tune = car.TuneOrDefault.Clone();
            var up = car.Upgrades;
            Task.Run(() =>
            {
                var s = Performance.Measure(spec, tune, up);
                car.Pi = s.Pi;
                dirtyAt = 0; // saved on the next frame
            });
        }

        /// <summary>The car's PI, measuring it if it is not known yet (0 until then).</summary>
        public int PiOf(OwnedCar car) => car.Pi;
    }
}
