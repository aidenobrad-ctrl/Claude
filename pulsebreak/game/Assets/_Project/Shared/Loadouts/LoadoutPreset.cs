using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>
    /// A ready-made loadout that ships with the game. Players can use one as it is, or copy
    /// it as the starting point for their own. Built from asset references so it can be put
    /// together by dragging in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Loadout Preset", fileName = "LDT_New")]
    public sealed class LoadoutPreset : ScriptableObject
    {
        [Tooltip("Stable id. Never change it once shipped: saves remember which preset was equipped.")]
        public string presetId = "preset_id";
        public string displayName = "New Preset";
        [TextArea] public string description = "";

        [Header("Weapons")]
        public WeaponDefinition primary;
        public AttachmentDefinition[] primaryAttachments = new AttachmentDefinition[0];
        public WeaponDefinition secondary;
        public AttachmentDefinition[] secondaryAttachments = new AttachmentDefinition[0];
        public WeaponDefinition melee;

        [Header("Equipment")]
        public UtilityDefinition utility1;
        public UtilityDefinition utility2;
        public PerkSetDefinition perkSet;

        /// <summary>The preset in its stored form.</summary>
        public LoadoutData ToData()
        {
            return new LoadoutData
            {
                id = presetId,
                name = displayName,
                primary = BuildData(primary, primaryAttachments),
                secondary = BuildData(secondary, secondaryAttachments),
                melee = BuildData(melee, null),
                utility1 = utility1 != null ? utility1.utilityId : "",
                utility2 = utility2 != null ? utility2.utilityId : "",
                perkSet = perkSet != null ? perkSet.perkSetId : "",
            };
        }

        static WeaponBuildData BuildData(WeaponDefinition weapon, AttachmentDefinition[] attachments)
        {
            var data = new WeaponBuildData { weaponId = weapon != null ? weapon.weaponId : "" };
            if (attachments == null) return data;

            data.attachments = new string[attachments.Length];
            for (int i = 0; i < attachments.Length; i++)
                data.attachments[i] = attachments[i] != null ? attachments[i].attachmentId : "";
            return data;
        }
    }
}
