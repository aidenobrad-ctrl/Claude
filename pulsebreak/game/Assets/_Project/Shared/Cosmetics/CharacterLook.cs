using System;
using System.Collections.Generic;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Cosmetics
{
    /// <summary>A skin chosen for one weapon, overriding the skin for every weapon.</summary>
    [Serializable]
    public sealed class WeaponSkinChoice
    {
        public string weaponId = "";
        public string skinId = "";
    }

    /// <summary>
    /// How a player looks: their suit, trim, pattern, headgear, visor and back item, and the
    /// skins on their weapons. Stored and sent by item id, so a save outlives changes to the
    /// catalog; <see cref="LookRules.Sanitize"/> turns anything unknown back into a default.
    /// It changes nothing about how anyone plays: bodies are shot by the same hitboxes
    /// whatever they wear.
    /// </summary>
    [Serializable]
    public sealed class CharacterLook
    {
        public string suitColor = "graphite";
        public string trimColor = "glacier";
        public string pattern = "plain";
        public string headgear = "none";
        public string visor = "arc";
        public string back = "none";
        /// <summary>The skin on every weapon without one of its own.</summary>
        public string weaponSkin = "factory";
        public WeaponSkinChoice[] weaponSkins = new WeaponSkinChoice[0];
        /// <summary>When it last changed, seconds since 1970 (UTC). For syncing between devices later.</summary>
        public long updatedAt;

        public CharacterLook Clone()
        {
            var copy = (CharacterLook)MemberwiseClone();
            copy.weaponSkins = new WeaponSkinChoice[weaponSkins?.Length ?? 0];
            for (int i = 0; i < copy.weaponSkins.Length; i++)
                copy.weaponSkins[i] = new WeaponSkinChoice { weaponId = weaponSkins[i].weaponId, skinId = weaponSkins[i].skinId };
            return copy;
        }

        /// <summary>The id of the item chosen for a slot. For the weapon skin slot, the skin for every weapon.</summary>
        public string Get(CosmeticSlot slot)
        {
            switch (slot)
            {
                case CosmeticSlot.SuitColor: return suitColor;
                case CosmeticSlot.TrimColor: return trimColor;
                case CosmeticSlot.Pattern: return pattern;
                case CosmeticSlot.Headgear: return headgear;
                case CosmeticSlot.Visor: return visor;
                case CosmeticSlot.Back: return back;
                default: return weaponSkin;
            }
        }

        public void Set(CosmeticSlot slot, string id)
        {
            switch (slot)
            {
                case CosmeticSlot.SuitColor: suitColor = id; break;
                case CosmeticSlot.TrimColor: trimColor = id; break;
                case CosmeticSlot.Pattern: pattern = id; break;
                case CosmeticSlot.Headgear: headgear = id; break;
                case CosmeticSlot.Visor: visor = id; break;
                case CosmeticSlot.Back: back = id; break;
                default: weaponSkin = id; break;
            }
        }

        public CosmeticItem Item(CosmeticSlot slot) => CosmeticCatalog.FindOrDefault(slot, Get(slot));

        /// <summary>The skin on a weapon: its own if it has one, otherwise the one for every weapon.</summary>
        public CosmeticItem SkinFor(string weaponId)
        {
            if (weaponSkins != null)
            {
                foreach (WeaponSkinChoice choice in weaponSkins)
                {
                    if (choice != null && choice.weaponId == weaponId) return CosmeticCatalog.FindOrDefault(CosmeticSlot.WeaponSkin, choice.skinId);
                }
            }
            return CosmeticCatalog.FindOrDefault(CosmeticSlot.WeaponSkin, weaponSkin);
        }

        /// <summary>Gives one weapon a skin of its own. Choosing the skin every weapon already has removes the override.</summary>
        public void SetSkin(string weaponId, string skinId)
        {
            var kept = new List<WeaponSkinChoice>();
            foreach (WeaponSkinChoice choice in weaponSkins ?? new WeaponSkinChoice[0])
            {
                if (choice != null && choice.weaponId != weaponId) kept.Add(choice);
            }
            if (skinId != weaponSkin) kept.Add(new WeaponSkinChoice { weaponId = weaponId, skinId = skinId });
            weaponSkins = kept.ToArray();
        }

        /// <summary>One skin on every weapon, replacing any each had of its own.</summary>
        public void SetSkinForAll(string skinId)
        {
            weaponSkin = skinId;
            weaponSkins = new WeaponSkinChoice[0];
        }
    }

    /// <summary>What a legal look is, and a few ways to make one.</summary>
    public static class LookRules
    {
        /// <summary>Weapons with a skin of their own, at most. Enough for every weapon in the game, with room to spare.</summary>
        public const int MaxWeaponSkins = 40;
        public const int MaxWeaponIdLength = 24;

        /// <summary>
        /// A copy with every unknown item replaced by its slot's default, and the per-weapon
        /// skins tidied: no duplicates, none that only repeat the skin for every weapon, no
        /// more than <see cref="MaxWeaponSkins"/>. Never null.
        /// </summary>
        public static CharacterLook Sanitize(CharacterLook look)
        {
            var clean = new CharacterLook();
            if (look == null) return clean;

            foreach (CosmeticSlot slot in CosmeticCatalog.Slots) clean.Set(slot, CosmeticCatalog.FindOrDefault(slot, look.Get(slot)).Id);
            clean.updatedAt = Math.Max(0, look.updatedAt);

            var skins = new List<WeaponSkinChoice>();
            var seen = new HashSet<string>();
            foreach (WeaponSkinChoice choice in look.weaponSkins ?? new WeaponSkinChoice[0])
            {
                if (skins.Count >= MaxWeaponSkins) break;
                if (choice == null || string.IsNullOrEmpty(choice.weaponId) || choice.weaponId.Length > MaxWeaponIdLength) continue;
                CosmeticItem skin = CosmeticCatalog.Find(CosmeticSlot.WeaponSkin, choice.skinId);
                if (skin == null || skin.Id == clean.weaponSkin || !seen.Add(choice.weaponId)) continue;
                skins.Add(new WeaponSkinChoice { weaponId = choice.weaponId, skinId = skin.Id });
            }
            clean.weaponSkins = skins.ToArray();
            return clean;
        }

        /// <summary>A random look, for bots and the Randomise button. The same seed gives the same look.</summary>
        public static CharacterLook Random(uint seed)
        {
            var random = new DeterministicRandom(seed | 1u);
            var look = new CharacterLook();
            foreach (CosmeticSlot slot in CosmeticCatalog.Slots)
            {
                int count = CosmeticCatalog.Count(slot);
                look.Set(slot, CosmeticCatalog.At(slot, (int)(random.NextUInt() % (uint)count)).Id);
            }
            // Trim the same as the suit is dull: nudge it along the palette.
            if (look.trimColor == look.suitColor)
                look.trimColor = CosmeticCatalog.At(CosmeticSlot.TrimColor, (CosmeticCatalog.Find(CosmeticSlot.TrimColor, look.trimColor).Index + 3) %
                                                                            CosmeticCatalog.Count(CosmeticSlot.TrimColor)).Id;
            return look;
        }
    }
}
