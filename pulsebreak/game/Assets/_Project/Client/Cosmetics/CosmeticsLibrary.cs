using System;
using System.IO;
using UnityEngine;
using Pulsebreak.Shared.Cosmetics;

namespace Pulsebreak.Client.Cosmetics
{
    /// <summary>
    /// The player's look, kept in a JSON file in the game's save folder beside their
    /// loadouts. Every cosmetic is free to pick for now, so there is nothing to own: just
    /// what is chosen. Every change is checked against the catalog and saved straight away.
    /// </summary>
    public sealed class CosmeticsLibrary
    {
        public const string FileName = "cosmetics.json";

        readonly Func<long> _clock;

        /// <summary>The local player's library. Set by the game's services; null in a bare test.</summary>
        public static CosmeticsLibrary Local { get; set; }

        public string FilePath { get; }
        /// <summary>The current look. A new object after every change; treat it as read-only.</summary>
        public CharacterLook Look { get; private set; }

        /// <summary>The look changed.</summary>
        public event Action Changed;

        /// <param name="filePath">Leave empty to use the game's save folder.</param>
        /// <param name="clock">Seconds since 1970 (UTC). Replaceable for tests.</param>
        public CosmeticsLibrary(string filePath = null, Func<long> clock = null)
        {
            FilePath = string.IsNullOrEmpty(filePath) ? Path.Combine(Application.persistentDataPath, FileName) : filePath;
            _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Look = Load();
        }

        /// <summary>Changes the look. Anything not in the catalog is replaced with a default.</summary>
        public void Set(CharacterLook look)
        {
            CharacterLook clean = LookRules.Sanitize(look);
            clean.updatedAt = Math.Max(_clock(), Look.updatedAt + 1);
            Look = clean;
            Save();
            Changed?.Invoke();
        }

        /// <summary>Applies one change to a copy of the look and saves it.</summary>
        public void Edit(Action<CharacterLook> change)
        {
            CharacterLook copy = Look.Clone();
            change(copy);
            Set(copy);
        }

        /// <summary>The local player's skin for a weapon, or null when there is no library (the factory finish).</summary>
        public static CosmeticItem SkinFor(string weaponId) => Local?.Look.SkinFor(weaponId);

        CharacterLook Load()
        {
            if (!File.Exists(FilePath)) return new CharacterLook();
            try
            {
                var look = JsonUtility.FromJson<CharacterLook>(File.ReadAllText(FilePath));
                if (look == null) throw new InvalidDataException("The file is empty.");
                return LookRules.Sanitize(look);
            }
            catch (Exception error)
            {
                string aside = FilePath + ".unreadable";
                try { File.Copy(FilePath, aside, true); }
                catch (Exception) { /* Nothing more can be done about it here. */ }
                Debug.LogWarning("Pulsebreak: could not read the saved look (" + error.Message + "). Starting from the default; " +
                                 "the old file was kept as " + aside + ".");
                return new CharacterLook();
            }
        }

        void Save()
        {
            try
            {
                string folder = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                // Beside the real file, then swapped in, so a crash mid-write leaves the old look intact.
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(Look, true));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            catch (Exception error)
            {
                Debug.LogWarning("Pulsebreak: could not save the look to " + FilePath + " (" + error.Message + "). " +
                                 "It will last until the game closes.");
            }
        }
    }
}
