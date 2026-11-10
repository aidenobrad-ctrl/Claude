using System.Collections.Generic;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>
    /// The rules for what a legal loadout is, and the one place stored loadouts are turned
    /// into playable ones. Anything that does not check out is replaced with a sensible
    /// default instead of failing, so a save from an older version, a hand-edited file or
    /// (later) a tampered request from a client always produces something valid.
    /// </summary>
    public static class LoadoutRules
    {
        public const int MaxNameLength = 24;
        public const string DefaultName = "Loadout";

        static readonly WeaponSlotType[] SlotTypes = { WeaponSlotType.Primary, WeaponSlotType.Secondary, WeaponSlotType.Melee };

        /// <summary>Builds a playable loadout from stored data.</summary>
        /// <param name="notes">If given, receives a line for everything that had to be corrected.</param>
        public static Loadout Resolve(LoadoutData data, LoadoutCatalog catalog, List<string> notes = null)
        {
            var loadout = new Loadout
            {
                Id = data.id,
                Name = CleanName(data.name),
            };

            for (int slot = 0; slot < Loadout.WeaponSlots; slot++)
                loadout.Weapons[slot] = ResolveWeapon(data.GetWeapon(slot), SlotTypes[slot], catalog, notes);

            loadout.Utilities[0] = ResolveUtility(data.utility1, null, catalog, notes);
            loadout.Utilities[1] = ResolveUtility(data.utility2, loadout.Utilities[0], catalog, notes);

            loadout.PerkSet = catalog.FindPerkSet(data.perkSet);
            if (loadout.PerkSet == null && catalog.perkSets.Length > 0)
            {
                loadout.PerkSet = catalog.perkSets[0];
                Note(notes, "Perk set '" + data.perkSet + "' is not available; using " + loadout.PerkSet.displayName + ".");
            }

            return loadout;
        }

        /// <summary>A playable loadout back in its stored form.</summary>
        public static LoadoutData ToData(Loadout loadout, long updatedAt = 0)
        {
            return new LoadoutData
            {
                id = loadout.Id,
                name = loadout.Name,
                primary = ToData(loadout.Weapons[0]),
                secondary = ToData(loadout.Weapons[1]),
                melee = ToData(loadout.Weapons[2]),
                utility1 = loadout.Utilities[0] != null ? loadout.Utilities[0].utilityId : "",
                utility2 = loadout.Utilities[1] != null ? loadout.Utilities[1].utilityId : "",
                perkSet = loadout.PerkSet != null ? loadout.PerkSet.perkSetId : "",
                updatedAt = updatedAt,
            };
        }

        /// <summary>
        /// Stored data with everything illegal corrected. Running a loadout through this
        /// before saving keeps bad data from ever reaching the file.
        /// </summary>
        public static LoadoutData Sanitize(LoadoutData data, LoadoutCatalog catalog, List<string> notes = null) =>
            ToData(Resolve(data, catalog, notes), data.updatedAt);

        /// <summary>Trims a name, strips line breaks, and cuts it to length. Never returns an empty name.</summary>
        public static string CleanName(string name)
        {
            if (name == null) return DefaultName;

            var clean = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (!char.IsControl(c)) clean.Append(c);
            }

            string trimmed = clean.ToString().Trim();
            if (trimmed.Length > MaxNameLength) trimmed = trimmed.Substring(0, MaxNameLength).TrimEnd();
            return trimmed.Length > 0 ? trimmed : DefaultName;
        }

        static WeaponBuild ResolveWeapon(WeaponBuildData data, WeaponSlotType slotType, LoadoutCatalog catalog, List<string> notes)
        {
            WeaponCatalog weapons = catalog.weapons;
            if (weapons == null) return null;

            WeaponDefinition weapon = data != null ? weapons.FindWeapon(data.weaponId) : null;
            if (weapon == null || weapon.slot != slotType)
            {
                WeaponDefinition fallback = weapons.FirstForSlot(slotType);
                if (fallback == null) return null;
                Note(notes, "Weapon '" + (data != null ? data.weaponId : "") + "' cannot go in the " + slotType + " slot; using " + fallback.displayName + ".");
                return new WeaponBuild(fallback);
            }

            var build = new WeaponBuild(weapon);
            if (data.attachments == null) return build;

            foreach (string attachmentId in data.attachments)
            {
                AttachmentDefinition attachment = weapons.FindAttachment(attachmentId);
                if (attachment == null || !build.Accepts(attachment))
                {
                    Note(notes, "Attachment '" + attachmentId + "' does not fit " + weapon.displayName + "; removed.");
                    continue;
                }
                if (build.Get(attachment.slot) != null)
                {
                    Note(notes, weapon.displayName + " had two " + attachment.slot + " attachments; kept the first.");
                    continue;
                }
                build.Fit(attachment);
            }
            return build;
        }

        /// <summary>The two utility slots must hold different items.</summary>
        static UtilityDefinition ResolveUtility(string utilityId, UtilityDefinition other, LoadoutCatalog catalog, List<string> notes)
        {
            UtilityDefinition utility = catalog.FindUtility(utilityId);
            if (utility != null && utility != other) return utility;

            foreach (UtilityDefinition candidate in catalog.utilities)
            {
                if (candidate == null || candidate == other) continue;
                Note(notes, utility == null
                    ? "Utility '" + utilityId + "' is not available; using " + candidate.displayName + "."
                    : "Both utility slots held " + utility.displayName + "; changed one to " + candidate.displayName + ".");
                return candidate;
            }
            return null;
        }

        static WeaponBuildData ToData(WeaponBuild build)
        {
            if (build == null) return new WeaponBuildData();
            return new WeaponBuildData { weaponId = build.Weapon.weaponId, attachments = build.FittedIds() };
        }

        static void Note(List<string> notes, string message)
        {
            if (notes != null) notes.Add(message);
        }
    }
}
