using UnityEngine;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// The list of every weapon and attachment in the game. Anything that needs to turn an
    /// id back into an asset (a saved loadout, a message from the server) looks it up here.
    /// A new weapon only exists for the game once it is in this list.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Weapon Catalog", fileName = "WeaponCatalog")]
    public sealed class WeaponCatalog : ScriptableObject
    {
        public WeaponDefinition[] weapons = new WeaponDefinition[0];
        public AttachmentDefinition[] attachments = new AttachmentDefinition[0];

        public WeaponDefinition FindWeapon(string weaponId)
        {
            foreach (WeaponDefinition weapon in weapons)
            {
                if (weapon != null && weapon.weaponId == weaponId) return weapon;
            }
            return null;
        }

        public AttachmentDefinition FindAttachment(string attachmentId)
        {
            foreach (AttachmentDefinition attachment in attachments)
            {
                if (attachment != null && attachment.attachmentId == attachmentId) return attachment;
            }
            return null;
        }

        /// <summary>The first weapon that fits a loadout slot, for filling a slot that has nothing chosen.</summary>
        public WeaponDefinition FirstForSlot(WeaponSlotType slot)
        {
            foreach (WeaponDefinition weapon in weapons)
            {
                if (weapon != null && weapon.slot == slot) return weapon;
            }
            return null;
        }
    }
}
