using System;

// This file is also compiled into the backend (backend/src/Pulsebreak.Api), so the game and
// the server agree on the stored format by construction. Keep it free of UnityEngine.
#nullable disable

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>A weapon and its attachments, by id. The saved form of a WeaponBuild.</summary>
    [Serializable]
    public sealed class WeaponBuildData
    {
        public string weaponId = "";
        public string[] attachments = new string[0];

        public WeaponBuildData Clone() =>
            new WeaponBuildData { weaponId = weaponId, attachments = (string[])attachments.Clone() };
    }

    /// <summary>
    /// A loadout as it is stored and sent: nothing but ids and a name. It holds no
    /// references to assets, so it can be written to disk, sent to a server, or kept in a
    /// database, and still be read back after weapons have been added, renamed or removed.
    /// Turn it into something playable with <see cref="LoadoutRules.Resolve"/>.
    /// </summary>
    [Serializable]
    public sealed class LoadoutData
    {
        /// <summary>Unique and permanent. Presets use their preset id.</summary>
        public string id = "";
        public string name = "";
        public WeaponBuildData primary = new WeaponBuildData();
        public WeaponBuildData secondary = new WeaponBuildData();
        public WeaponBuildData melee = new WeaponBuildData();
        public string utility1 = "";
        public string utility2 = "";
        public string perkSet = "";
        /// <summary>When it was last changed, in seconds since 1970 (UTC). Decides which copy wins once loadouts sync between devices.</summary>
        public long updatedAt;

        public LoadoutData Clone()
        {
            return new LoadoutData
            {
                id = id,
                name = name,
                primary = primary.Clone(),
                secondary = secondary.Clone(),
                melee = melee.Clone(),
                utility1 = utility1,
                utility2 = utility2,
                perkSet = perkSet,
                updatedAt = updatedAt,
            };
        }

        /// <summary>The weapon in a slot: 0 primary, 1 secondary, 2 melee.</summary>
        public WeaponBuildData GetWeapon(int slot)
        {
            switch (slot)
            {
                case 0: return primary;
                case 1: return secondary;
                default: return melee;
            }
        }
    }

    /// <summary>A record that a loadout was deleted, kept so the deletion can reach the player's other devices.</summary>
    [Serializable]
    public sealed class DeletedLoadout
    {
        public string id = "";
        /// <summary>Seconds since 1970 (UTC).</summary>
        public long deletedAt;
    }

    /// <summary>Everything one player has saved. The unit a loadout store reads and writes.</summary>
    [Serializable]
    public sealed class LoadoutCollection
    {
        public const int CurrentVersion = 2;

        /// <summary>Format version, so a later change to the layout can migrate old saves.</summary>
        public int version = CurrentVersion;
        /// <summary>Id of the loadout in use. May be a preset id.</summary>
        public string equippedId = "";
        /// <summary>When the equipped loadout was last chosen. Seconds since 1970 (UTC). Added in version 2.</summary>
        public long equippedUpdatedAt;
        public LoadoutData[] loadouts = new LoadoutData[0];
        /// <summary>Loadouts that were deleted, newest first. Added in version 2.</summary>
        public DeletedLoadout[] deleted = new DeletedLoadout[0];
    }
}
