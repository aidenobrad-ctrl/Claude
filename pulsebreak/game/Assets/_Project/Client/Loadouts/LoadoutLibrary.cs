using System;
using System.Collections.Generic;
using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Client.Loadouts
{
    /// <summary>
    /// A player's loadouts: the presets that ship with the game, the custom ones they have
    /// made, and which one is equipped. Every change is checked against the rules and saved
    /// straight away through the store.
    /// </summary>
    public sealed class LoadoutLibrary
    {
        public const int MaxCustomLoadouts = 10;

        readonly LoadoutCatalog _catalog;
        readonly ILoadoutStore _store;
        readonly Func<long> _clock;
        readonly List<LoadoutData> _presets = new List<LoadoutData>();
        readonly List<LoadoutData> _custom = new List<LoadoutData>();
        string _equippedId = "";

        /// <summary>The list, a name, or a loadout's contents changed.</summary>
        public event Action Changed;
        /// <summary>What the player should be carrying changed: another loadout was equipped, or the equipped one was edited.</summary>
        public event Action EquippedChanged;

        /// <summary>Entries are the library's own copies. Read them; to change one, edit a Clone and pass it to <see cref="Replace"/>.</summary>
        public IReadOnlyList<LoadoutData> Presets => _presets;
        public IReadOnlyList<LoadoutData> Custom => _custom;
        public LoadoutCatalog Catalog => _catalog;
        public bool CanCreate => _custom.Count < MaxCustomLoadouts;

        /// <param name="clock">Seconds since 1970 (UTC). Replaceable so tests can control time.</param>
        public LoadoutLibrary(LoadoutCatalog catalog, ILoadoutStore store, Func<long> clock = null)
        {
            _catalog = catalog;
            _store = store;
            _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            foreach (LoadoutPreset preset in catalog.presets)
            {
                if (preset != null) _presets.Add(LoadoutRules.Sanitize(preset.ToData(), catalog));
            }

            LoadoutCollection saved = store.Load();
            if (saved != null)
            {
                foreach (LoadoutData data in saved.loadouts)
                {
                    // Skip anything unusable: no id, an id already taken, or more than the limit.
                    if (data == null || string.IsNullOrEmpty(data.id) || Find(data.id) != null) continue;
                    if (_custom.Count >= MaxCustomLoadouts) break;
                    _custom.Add(LoadoutRules.Sanitize(data, catalog));
                }
                _equippedId = saved.equippedId ?? "";
            }

            if (Find(_equippedId) == null) _equippedId = _presets.Count > 0 ? _presets[0].id : "";
        }

        public LoadoutData Equipped => Find(_equippedId);
        public string EquippedId => _equippedId;

        public bool IsPreset(string id)
        {
            foreach (LoadoutData preset in _presets)
            {
                if (preset.id == id) return true;
            }
            return false;
        }

        public LoadoutData Find(string id)
        {
            foreach (LoadoutData preset in _presets)
            {
                if (preset.id == id) return preset;
            }
            foreach (LoadoutData custom in _custom)
            {
                if (custom.id == id) return custom;
            }
            return null;
        }

        /// <summary>Presets first, then custom loadouts, in the order they are shown.</summary>
        public List<LoadoutData> All()
        {
            var all = new List<LoadoutData>(_presets);
            all.AddRange(_custom);
            return all;
        }

        /// <summary>The equipped loadout, ready to play. Null only if the catalog has no presets and nothing is saved.</summary>
        public Loadout ResolveEquipped()
        {
            LoadoutData equipped = Equipped;
            return equipped != null ? LoadoutRules.Resolve(equipped, _catalog) : null;
        }

        public void Equip(string id)
        {
            if (id == _equippedId || Find(id) == null) return;
            _equippedId = id;
            Persist();
            EquippedChanged?.Invoke();
            Changed?.Invoke();
        }

        /// <summary>Makes a new custom loadout as a copy of an existing one. Returns null when the limit is reached.</summary>
        public LoadoutData CreateFrom(string sourceId)
        {
            LoadoutData source = Find(sourceId);
            if (source == null || !CanCreate) return null;

            LoadoutData copy = source.Clone();
            copy.id = Guid.NewGuid().ToString("N");
            copy.name = UniqueName(source.name);
            copy.updatedAt = _clock();
            _custom.Add(copy);

            Persist();
            Changed?.Invoke();
            return copy;
        }

        public void Rename(string id, string name)
        {
            LoadoutData target = FindCustom(id);
            if (target == null) return;

            string clean = LoadoutRules.CleanName(name);
            if (clean == target.name) return;
            target.name = clean;
            target.updatedAt = _clock();

            Persist();
            Changed?.Invoke();
        }

        /// <summary>
        /// Saves an edited copy of a custom loadout over the original with the same id.
        /// Presets cannot be replaced; copy one with <see cref="CreateFrom"/> first.
        /// </summary>
        public void Replace(LoadoutData edited)
        {
            int index = _custom.FindIndex(l => l.id == edited.id);
            if (index < 0) return;

            LoadoutData clean = LoadoutRules.Sanitize(edited, _catalog);
            clean.updatedAt = _clock();
            _custom[index] = clean;

            Persist();
            if (clean.id == _equippedId) EquippedChanged?.Invoke();
            Changed?.Invoke();
        }

        public bool Delete(string id)
        {
            LoadoutData target = FindCustom(id);
            if (target == null) return false;
            _custom.Remove(target);

            bool wasEquipped = id == _equippedId;
            if (wasEquipped) _equippedId = _presets.Count > 0 ? _presets[0].id : (_custom.Count > 0 ? _custom[0].id : "");

            Persist();
            if (wasEquipped) EquippedChanged?.Invoke();
            Changed?.Invoke();
            return true;
        }

        LoadoutData FindCustom(string id)
        {
            foreach (LoadoutData custom in _custom)
            {
                if (custom.id == id) return custom;
            }
            return null;
        }

        /// <summary>"Assault" becomes "Assault 2", then "Assault 3", so no two loadouts share a name.</summary>
        string UniqueName(string wanted)
        {
            string stem = LoadoutRules.CleanName(wanted);
            for (int number = 2; number < 100; number++)
            {
                string suffix = " " + number;
                string trimmedStem = stem.Length + suffix.Length > LoadoutRules.MaxNameLength
                    ? stem.Substring(0, LoadoutRules.MaxNameLength - suffix.Length).TrimEnd()
                    : stem;
                string candidate = trimmedStem + suffix;
                if (!NameTaken(candidate)) return candidate;
            }
            return stem;
        }

        bool NameTaken(string name)
        {
            foreach (LoadoutData preset in _presets)
            {
                if (preset.name == name) return true;
            }
            foreach (LoadoutData custom in _custom)
            {
                if (custom.name == name) return true;
            }
            return false;
        }

        void Persist()
        {
            _store.Save(new LoadoutCollection
            {
                version = LoadoutCollection.CurrentVersion,
                equippedId = _equippedId,
                loadouts = _custom.ToArray(),
            });
        }
    }
}
