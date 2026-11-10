using System;
using System.IO;
using UnityEngine;
using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Client.Loadouts
{
    /// <summary>
    /// Keeps loadouts in a JSON file in the game's save folder, so they are still there the
    /// next time the game starts. The file is readable and can be deleted to start over.
    /// </summary>
    public sealed class LocalFileLoadoutStore : ILoadoutStore
    {
        public const string FileName = "loadouts.json";

        public string FilePath { get; }

        /// <param name="filePath">Leave empty to use the game's save folder.</param>
        public LocalFileLoadoutStore(string filePath = null)
        {
            FilePath = string.IsNullOrEmpty(filePath)
                ? Path.Combine(Application.persistentDataPath, FileName)
                : filePath;
        }

        public LoadoutCollection Load()
        {
            if (!File.Exists(FilePath)) return null;

            try
            {
                var collection = JsonUtility.FromJson<LoadoutCollection>(File.ReadAllText(FilePath));
                if (collection == null || collection.loadouts == null) throw new InvalidDataException("The file has no loadouts in it.");
                return collection;
            }
            catch (Exception error)
            {
                // Set the unreadable file aside rather than overwrite it, in case it can be rescued.
                string aside = FilePath + ".unreadable";
                try { File.Copy(FilePath, aside, true); }
                catch (Exception) { /* Nothing more can be done about it here. */ }

                Debug.LogWarning("Pulsebreak: could not read saved loadouts (" + error.Message + "). Starting with the defaults; " +
                                 "the old file was kept as " + aside + ".");
                return null;
            }
        }

        public void Save(LoadoutCollection collection)
        {
            try
            {
                string folder = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                // Write beside the real file and swap it in, so a crash mid-write cannot
                // leave half a file where the loadouts used to be.
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(collection, true));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            catch (Exception error)
            {
                Debug.LogWarning("Pulsebreak: could not save loadouts to " + FilePath + " (" + error.Message + "). " +
                                 "Changes will last until the game closes.");
            }
        }
    }
}
