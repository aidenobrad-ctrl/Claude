using UnityEngine;

namespace Pulsebreak.Shared.Combat
{
    /// <summary>
    /// Marks a collider as one damageable body part. Put it on a collider on the Hitbox
    /// layer, under an object that has an <see cref="IDamageable"/> (for example <see cref="Health"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Hitbox : MonoBehaviour
    {
        public HitZone zone = HitZone.Body;

        IDamageable _owner;

        public IDamageable Owner
        {
            get
            {
                if (_owner == null) _owner = GetComponentInParent<IDamageable>();
                return _owner;
            }
        }
    }
}
