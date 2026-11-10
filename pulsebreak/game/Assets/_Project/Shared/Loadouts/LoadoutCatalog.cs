using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>
    /// Everything a loadout can be made from: the weapon catalog, plus utility items, perk
    /// sets and the ready-made presets. This one asset is all that is needed to turn saved
    /// ids back into something playable.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Loadout Catalog", fileName = "LoadoutCatalog")]
    public sealed class LoadoutCatalog : ScriptableObject
    {
        public WeaponCatalog weapons;
        public UtilityDefinition[] utilities = new UtilityDefinition[0];
        public PerkSetDefinition[] perkSets = new PerkSetDefinition[0];
        public LoadoutPreset[] presets = new LoadoutPreset[0];

        public UtilityDefinition FindUtility(string utilityId)
        {
            foreach (UtilityDefinition utility in utilities)
            {
                if (utility != null && utility.utilityId == utilityId) return utility;
            }
            return null;
        }

        public PerkSetDefinition FindPerkSet(string perkSetId)
        {
            foreach (PerkSetDefinition perkSet in perkSets)
            {
                if (perkSet != null && perkSet.perkSetId == perkSetId) return perkSet;
            }
            return null;
        }

        public LoadoutPreset FindPreset(string presetId)
        {
            foreach (LoadoutPreset preset in presets)
            {
                if (preset != null && preset.presetId == presetId) return preset;
            }
            return null;
        }
    }
}
