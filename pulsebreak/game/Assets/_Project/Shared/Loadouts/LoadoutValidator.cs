using System.Collections.Generic;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>
    /// Checks utility items, perk sets and presets for mistakes, and runs the weapon checks
    /// too, so one call covers all loadout content.
    /// </summary>
    public static class LoadoutValidator
    {
        public static List<string> Validate(LoadoutCatalog catalog)
        {
            var problems = new List<string>();
            if (catalog.weapons == null)
            {
                problems.Add("loadout catalog: no weapon catalog assigned.");
                return problems;
            }
            problems.AddRange(WeaponValidator.Validate(catalog.weapons));

            var ids = new HashSet<string>();
            foreach (UtilityDefinition utility in catalog.utilities)
            {
                if (utility == null) { problems.Add("loadout catalog: empty entry in the utilities list."); continue; }
                string name = utility.utilityId;
                if (!ids.Add("u:" + name)) problems.Add("loadout catalog: utility id '" + name + "' is used more than once.");
                if (!IsValidId(name)) problems.Add(name + ": utilityId must be lower-case letters, digits and underscores.");
                if (string.IsNullOrEmpty(utility.displayName)) problems.Add(name + ": displayName is empty.");
                if (utility.charges < 1) problems.Add(name + ": charges must be at least 1.");
                if (utility.projectile == null) { problems.Add(name + ": has no projectile stats."); continue; }
                if (utility.projectile.ballistics != BallisticsType.Projectile) problems.Add(name + ": projectile.ballistics must be Projectile.");
                if (utility.projectile.damage <= 0f) problems.Add(name + ": projectile.damage must be above 0.");
                if (utility.projectile.fuseTime > utility.projectile.projectileLifetime) problems.Add(name + ": fuseTime is longer than projectileLifetime.");
                if (utility.projectile.fuseTime > 0f && utility.projectile.explosionRadius <= 0f) problems.Add(name + ": has a fuse but no explosionRadius.");
            }
            if (catalog.utilities.Length < Loadout.UtilitySlots)
                problems.Add("loadout catalog: needs at least " + Loadout.UtilitySlots + " utility items to fill both slots.");

            foreach (PerkSetDefinition perkSet in catalog.perkSets)
            {
                if (perkSet == null) { problems.Add("loadout catalog: empty entry in the perk sets list."); continue; }
                string name = perkSet.perkSetId;
                if (!ids.Add("p:" + name)) problems.Add("loadout catalog: perk set id '" + name + "' is used more than once.");
                if (!IsValidId(name)) problems.Add(name + ": perkSetId must be lower-case letters, digits and underscores.");
                if (string.IsNullOrEmpty(perkSet.displayName)) problems.Add(name + ": displayName is empty.");
                if (perkSet.moveSpeedMultiplier <= 0f) problems.Add(name + ": moveSpeedMultiplier must be above 0.");
                if (perkSet.weaponModifiers == null) continue;
                foreach (StatModifier modifier in perkSet.weaponModifiers)
                {
                    if (modifier.op == ModifierOp.Multiply && modifier.value <= 0f)
                        problems.Add(name + ": multiplies " + modifier.stat + " by " + modifier.value + ", which would zero it out.");
                }
            }
            if (catalog.perkSets.Length == 0) problems.Add("loadout catalog: needs at least one perk set.");

            foreach (LoadoutPreset preset in catalog.presets)
            {
                if (preset == null) { problems.Add("loadout catalog: empty entry in the presets list."); continue; }
                string name = preset.presetId;
                if (!ids.Add("l:" + name)) problems.Add("loadout catalog: preset id '" + name + "' is used more than once.");
                if (!IsValidId(name)) problems.Add(name + ": presetId must be lower-case letters, digits and underscores.");
                if (string.IsNullOrEmpty(preset.displayName)) problems.Add(name + ": displayName is empty.");

                // A preset must already be legal: resolving it should have nothing to correct.
                var notes = new List<string>();
                LoadoutRules.Resolve(preset.ToData(), catalog, notes);
                foreach (string note in notes) problems.Add("preset " + name + ": " + note);
            }
            if (catalog.presets.Length == 0) problems.Add("loadout catalog: needs at least one preset.");

            return problems;
        }

        static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }
    }
}
