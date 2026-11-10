using UnityEngine;

namespace Pulsebreak.Shared.Weapons
{
    public enum WeaponClass : byte
    {
        AssaultRifle,
        Smg,
        Lmg,
        Shotgun,
        Sniper,
        Marksman,
        Pistol,
        Launcher,
        Special,
        Melee,
    }

    public enum FireMode : byte
    {
        Automatic,
        SemiAuto,
        Burst,
    }

    /// <summary>
    /// All the numbers that make one weapon. To add a weapon, create one of these
    /// (Assets ▸ Create ▸ Pulsebreak ▸ Weapon Definition) and fill it in; no code changes.
    /// Angles are degrees, distances metres, times seconds.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulsebreak/Weapon Definition", fileName = "WPN_New")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used in saves, loadouts and the backend. Never change it once shipped.")]
        public string weaponId = "weapon_id";
        public string displayName = "New Weapon";
        public WeaponClass weaponClass = WeaponClass.AssaultRifle;

        [Header("Fire")]
        public FireMode fireMode = FireMode.Automatic;
        [Min(1f)] public float roundsPerMinute = 720f;
        [Tooltip("Burst mode only: shots per trigger pull.")]
        [Min(1)] public int burstCount = 3;
        [Tooltip("Burst mode only: pause after a burst before the next can start.")]
        [Min(0f)] public float burstCooldown = 0.2f;

        [Header("Damage")]
        [Min(0f)] public float damage = 26f;
        [Min(0f)] public float headMultiplier = 1.5f;
        [Min(0f)] public float limbMultiplier = 0.9f;
        [Tooltip("Full damage up to this range.")]
        [Min(0f)] public float falloffStart = 28f;
        [Tooltip("Minimum damage from this range on.")]
        [Min(0f)] public float falloffEnd = 55f;
        [Range(0f, 1f)] public float minDamageMultiplier = 0.65f;
        [Min(1f)] public float maxRange = 300f;

        [Header("Ammo")]
        [Min(1)] public int magazineSize = 30;
        [Min(0)] public int reserveAmmo = 120;
        [Tooltip("Reload with a round still in the magazine.")]
        [Min(0.05f)] public float reloadTime = 1.9f;
        [Tooltip("Reload from an empty magazine.")]
        [Min(0.05f)] public float emptyReloadTime = 2.3f;

        [Header("Handling")]
        [Min(0.01f)] public float adsTime = 0.2f;
        [Tooltip("Magnification when aiming. 1.25 narrows the field of view by a quarter.")]
        [Min(1f)] public float adsZoom = 1.25f;
        [Tooltip("Movement speed multiplier while carrying this weapon.")]
        [Range(0.5f, 1.2f)] public float hipMoveMultiplier = 1f;
        [Tooltip("Movement speed multiplier while fully aimed.")]
        [Range(0.3f, 1.2f)] public float adsMoveMultiplier = 0.72f;
        [Tooltip("Delay after sprinting before the weapon can fire.")]
        [Min(0f)] public float sprintToFireTime = 0.14f;
        [Min(0f)] public float equipTime = 0.35f;

        [Header("Spread (cone half-angle)")]
        [Min(0f)] public float hipSpread = 1.1f;
        [Min(0f)] public float adsSpread = 0.06f;
        [Tooltip("Added to hip spread at full run speed.")]
        [Min(0f)] public float moveSpreadPenalty = 1.2f;
        [Tooltip("Added to hip spread while airborne. Half of it applies when aiming.")]
        [Min(0f)] public float airSpreadPenalty = 3f;
        [Range(0.1f, 1f)] public float crouchSpreadMultiplier = 0.8f;
        [Tooltip("Spread added by each shot.")]
        [Min(0f)] public float bloomPerShot = 0.22f;
        [Min(0f)] public float maxBloom = 1.6f;
        [Tooltip("How fast bloom settles, in degrees per second.")]
        [Min(0f)] public float bloomRecovery = 5f;
        [Tooltip("Fraction of bloom that applies while aiming.")]
        [Range(0f, 1f)] public float adsBloomMultiplier = 0.2f;

        [Header("Recoil")]
        [Tooltip("View kick per shot. x = right, y = up. Shot 1 uses element 0, and so on.")]
        public Vector2[] recoilPattern =
        {
            new Vector2(0f, 0.6f),
        };
        [Tooltip("After the last element the pattern repeats from here.")]
        [Min(0)] public int recoilLoopStart = 0;
        [Tooltip("Random variation of each kick, as a fraction of its size.")]
        [Range(0f, 1f)] public float recoilRandomness = 0.12f;
        [Range(0f, 1.5f)] public float adsRecoilMultiplier = 0.75f;
        [Range(0f, 1.5f)] public float crouchRecoilMultiplier = 0.85f;
        [Tooltip("How quickly the view reaches the kick. Higher is snappier.")]
        [Min(1f)] public float recoilSnappiness = 28f;
        [Tooltip("How fast the view returns after firing stops, in degrees per second.")]
        [Min(0f)] public float recoilRecovery = 9f;
        [Tooltip("Pause after the last shot before the view starts returning.")]
        [Min(0f)] public float recoilRecoveryDelay = 0.12f;
        [Tooltip("Time without firing before the pattern starts over from shot 1.")]
        [Min(0f)] public float recoilResetTime = 0.35f;

        public float SecondsPerShot => 60f / Mathf.Max(roundsPerMinute, 1f);

        /// <summary>Kick for the n-th consecutive shot (0-based), following the loop once the pattern runs out.</summary>
        public Vector2 GetRecoilKick(int shotIndex)
        {
            if (recoilPattern == null || recoilPattern.Length == 0) return Vector2.zero;
            if (shotIndex < recoilPattern.Length) return recoilPattern[Mathf.Max(shotIndex, 0)];

            int loopStart = Mathf.Clamp(recoilLoopStart, 0, recoilPattern.Length - 1);
            int loopLength = recoilPattern.Length - loopStart;
            return recoilPattern[loopStart + (shotIndex - recoilPattern.Length) % loopLength];
        }
    }
}
