using System;
using UnityEngine;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>One change an attachment makes to a weapon.</summary>
    [Serializable]
    public struct StatModifier
    {
        public WeaponStat stat;
        public ModifierOp op;
        public float value;

        public StatModifier(WeaponStat stat, ModifierOp op, float value)
        {
            this.stat = stat;
            this.op = op;
            this.value = value;
        }
    }

    /// <summary>
    /// Every number that decides how a weapon behaves. A <see cref="WeaponDefinition"/> holds the
    /// base values; a <see cref="WeaponBuild"/> copies them and applies attachment modifiers to
    /// get the stats a weapon is actually simulated with.
    ///
    /// It is a plain class, not a Unity object, so the simulation, the tests and later the
    /// server can all use it without touching assets. Angles are degrees, distances metres,
    /// times seconds.
    /// </summary>
    [Serializable]
    public sealed class WeaponStats
    {
        [Header("Fire")]
        public FireMode fireMode = FireMode.Automatic;
        [Min(1f)] public float roundsPerMinute = 720f;
        [Tooltip("Burst mode: shots per trigger pull.")]
        [Min(1)] public int burstCount = 3;
        [Tooltip("Burst mode: pause after a burst before the next can start.")]
        [Min(0f)] public float burstCooldown = 0.2f;
        [Tooltip("Charge mode: seconds of holding the trigger for a full-power shot.")]
        [Min(0.05f)] public float chargeTime = 0.6f;
        [Tooltip("Charge mode: damage of an uncharged shot, as a fraction of a full one.")]
        [Range(0f, 1f)] public float minChargeDamage = 0.35f;

        [Header("Delivery")]
        public BallisticsType ballistics = BallisticsType.Hitscan;
        [Tooltip("Rays or projectiles per shot. More than 1 makes a shotgun.")]
        [Min(1)] public int pellets = 1;
        [Tooltip("Hitscan: how many extra targets a bullet carries on through.")]
        [Min(0)] public int pierceTargets = 0;
        [Tooltip("Hitscan: fraction of damage kept after each target passed through.")]
        [Range(0f, 1f)] public float pierceDamageKept = 0.75f;

        [Header("Damage")]
        [Tooltip("Per pellet.")]
        [Min(0f)] public float damage = 26f;
        [Min(0f)] public float headMultiplier = 1.5f;
        [Min(0f)] public float limbMultiplier = 0.9f;
        [Tooltip("Full damage up to this range.")]
        [Min(0f)] public float falloffStart = 28f;
        [Tooltip("Minimum damage from this range on.")]
        [Min(0f)] public float falloffEnd = 55f;
        [Range(0f, 1f)] public float minDamageMultiplier = 0.65f;
        [Min(1f)] public float maxRange = 300f;

        [Header("Projectile")]
        [Min(1f)] public float projectileSpeed = 60f;
        [Tooltip("Downward acceleration in m/s². 0 flies straight.")]
        [Min(0f)] public float projectileGravity = 0f;
        [Min(0.1f)] public float projectileLifetime = 5f;
        [Min(0f)] public float projectileRadius = 0.05f;
        [Tooltip("How many times it bounces off the level before stopping.")]
        [Min(0)] public int bounces = 0;
        [Tooltip("Fraction of speed kept after a bounce.")]
        [Range(0f, 1f)] public float bounceSpeedKept = 0.6f;
        [Tooltip("Damage multiplier gained on each bounce. Above 1 rewards trick shots.")]
        [Min(0f)] public float bounceDamageMultiplier = 1f;
        [Tooltip("0 for no explosion.")]
        [Min(0f)] public float explosionRadius = 0f;
        [Tooltip("Damage at the edge of the explosion, as a fraction of the centre.")]
        [Range(0f, 1f)] public float explosionEdgeDamage = 0.25f;
        [Tooltip("0 explodes on impact. Above 0 it bounces around and explodes when the fuse runs out.")]
        [Min(0f)] public float fuseTime = 0f;

        [Header("Melee")]
        [Min(0.1f)] public float meleeRange = 2.2f;
        [Min(0.05f)] public float meleeRadius = 0.45f;
        [Tooltip("Time from starting the swing to the moment it connects.")]
        [Min(0f)] public float meleeHitDelay = 0.12f;

        [Header("Ammo")]
        [Tooltip("0 means the weapon uses no ammunition.")]
        [Min(0)] public int magazineSize = 30;
        [Min(0)] public int reserveAmmo = 120;
        public ReloadStyle reloadStyle = ReloadStyle.Magazine;
        [Tooltip("Magazine: reload with a round left. Per round: time to load each round.")]
        [Min(0.05f)] public float reloadTime = 1.9f;
        [Tooltip("Magazine: reload from empty. Unused per round.")]
        [Min(0.05f)] public float emptyReloadTime = 2.3f;
        [Tooltip("Per round: extra time before the first round goes in.")]
        [Min(0f)] public float reloadIntroTime = 0f;

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
        [Tooltip("Time to bring the weapon up.")]
        [Min(0f)] public float equipTime = 0.35f;
        [Tooltip("Time to put the weapon away.")]
        [Min(0f)] public float holsterTime = 0.2f;

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
        public Vector2[] recoilPattern = { new Vector2(0f, 0.6f) };
        [Tooltip("After the last element the pattern repeats from here.")]
        [Min(0)] public int recoilLoopStart = 0;
        [Tooltip("Random variation of each kick, as a fraction of its size.")]
        [Range(0f, 1f)] public float recoilRandomness = 0.12f;
        [Tooltip("Scales the upward part of every kick. Attachments change this, not the pattern.")]
        [Min(0f)] public float recoilVerticalScale = 1f;
        [Tooltip("Scales the sideways part of every kick.")]
        [Min(0f)] public float recoilHorizontalScale = 1f;
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
        public bool UsesAmmo => magazineSize > 0;
        public bool CanAim => ballistics != BallisticsType.Melee;

        /// <summary>
        /// Kick for the n-th consecutive shot (0-based), following the loop once the pattern
        /// runs out, with the vertical and horizontal scales applied.
        /// </summary>
        public Vector2 GetRecoilKick(int shotIndex)
        {
            if (recoilPattern == null || recoilPattern.Length == 0) return Vector2.zero;

            Vector2 kick;
            if (shotIndex < recoilPattern.Length)
            {
                kick = recoilPattern[Mathf.Max(shotIndex, 0)];
            }
            else
            {
                int loopStart = Mathf.Clamp(recoilLoopStart, 0, recoilPattern.Length - 1);
                int loopLength = recoilPattern.Length - loopStart;
                kick = recoilPattern[loopStart + (shotIndex - recoilPattern.Length) % loopLength];
            }
            return new Vector2(kick.x * recoilHorizontalScale, kick.y * recoilVerticalScale);
        }

        /// <summary>A copy that can be modified freely. The recoil pattern array is shared, and is never written to.</summary>
        public WeaponStats Clone() => (WeaponStats)MemberwiseClone();

        /// <summary>Applies one attachment modifier. Some stats cover several related fields.</summary>
        public void Apply(StatModifier modifier)
        {
            switch (modifier.stat)
            {
                case WeaponStat.Damage:
                    damage = Modified(damage, modifier);
                    break;
                case WeaponStat.FireRate:
                    roundsPerMinute = Modified(roundsPerMinute, modifier);
                    break;
                case WeaponStat.Range:
                    falloffStart = Modified(falloffStart, modifier);
                    falloffEnd = Modified(falloffEnd, modifier);
                    break;
                case WeaponStat.MagazineSize:
                    if (magazineSize > 0) magazineSize = Mathf.RoundToInt(Modified(magazineSize, modifier));
                    break;
                case WeaponStat.ReloadTime:
                    reloadTime = Modified(reloadTime, modifier);
                    emptyReloadTime = Modified(emptyReloadTime, modifier);
                    reloadIntroTime = Modified(reloadIntroTime, modifier);
                    break;
                case WeaponStat.AdsTime:
                    adsTime = Modified(adsTime, modifier);
                    break;
                case WeaponStat.MoveSpeed:
                    hipMoveMultiplier = Modified(hipMoveMultiplier, modifier);
                    break;
                case WeaponStat.AdsMoveSpeed:
                    adsMoveMultiplier = Modified(adsMoveMultiplier, modifier);
                    break;
                case WeaponStat.SprintToFireTime:
                    sprintToFireTime = Modified(sprintToFireTime, modifier);
                    break;
                case WeaponStat.SwapTime:
                    equipTime = Modified(equipTime, modifier);
                    holsterTime = Modified(holsterTime, modifier);
                    break;
                case WeaponStat.HipSpread:
                    hipSpread = Modified(hipSpread, modifier);
                    moveSpreadPenalty = Modified(moveSpreadPenalty, modifier);
                    break;
                case WeaponStat.AdsSpread:
                    adsSpread = Modified(adsSpread, modifier);
                    break;
                case WeaponStat.Bloom:
                    bloomPerShot = Modified(bloomPerShot, modifier);
                    maxBloom = Modified(maxBloom, modifier);
                    break;
                case WeaponStat.RecoilVertical:
                    recoilVerticalScale = Modified(recoilVerticalScale, modifier);
                    break;
                case WeaponStat.RecoilHorizontal:
                    recoilHorizontalScale = Modified(recoilHorizontalScale, modifier);
                    break;
                case WeaponStat.RecoilRecovery:
                    recoilRecovery = Modified(recoilRecovery, modifier);
                    break;
                case WeaponStat.ProjectileSpeed:
                    projectileSpeed = Modified(projectileSpeed, modifier);
                    break;
            }
        }

        /// <summary>Pulls values back into a workable range after modifiers have been applied.</summary>
        public void Sanitize()
        {
            roundsPerMinute = Mathf.Max(roundsPerMinute, 1f);
            pellets = Mathf.Max(pellets, 1);
            damage = Mathf.Max(damage, 0f);
            if (magazineSize < 0) magazineSize = 0;
            falloffStart = Mathf.Max(falloffStart, 0f);
            falloffEnd = Mathf.Max(falloffEnd, falloffStart);
            reloadTime = Mathf.Max(reloadTime, 0.05f);
            emptyReloadTime = Mathf.Max(emptyReloadTime, 0.05f);
            reloadIntroTime = Mathf.Max(reloadIntroTime, 0f);
            adsTime = Mathf.Max(adsTime, 0.01f);
            adsZoom = Mathf.Max(adsZoom, 1f);
            hipMoveMultiplier = Mathf.Clamp(hipMoveMultiplier, 0.3f, 1.3f);
            adsMoveMultiplier = Mathf.Clamp(adsMoveMultiplier, 0.2f, 1.3f);
            sprintToFireTime = Mathf.Max(sprintToFireTime, 0f);
            equipTime = Mathf.Max(equipTime, 0f);
            holsterTime = Mathf.Max(holsterTime, 0f);
            hipSpread = Mathf.Max(hipSpread, 0f);
            adsSpread = Mathf.Max(adsSpread, 0f);
            moveSpreadPenalty = Mathf.Max(moveSpreadPenalty, 0f);
            bloomPerShot = Mathf.Max(bloomPerShot, 0f);
            maxBloom = Mathf.Max(maxBloom, 0f);
            recoilVerticalScale = Mathf.Max(recoilVerticalScale, 0f);
            recoilHorizontalScale = Mathf.Max(recoilHorizontalScale, 0f);
            recoilRecovery = Mathf.Max(recoilRecovery, 0f);
            projectileSpeed = Mathf.Max(projectileSpeed, 1f);
        }

        static float Modified(float value, StatModifier modifier) =>
            modifier.op == ModifierOp.Add ? value + modifier.value : value * modifier.value;
    }
}
