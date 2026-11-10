namespace Pulsebreak.Shared.Weapons
{
    // Values are saved in assets as numbers. Add new entries at the end; never reorder or remove.

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

    /// <summary>Which loadout slot a weapon is carried in.</summary>
    public enum WeaponSlotType : byte
    {
        Primary,
        Secondary,
        Melee,
    }

    public enum FireMode : byte
    {
        Automatic,
        SemiAuto,
        Burst,
        /// <summary>Hold the trigger to build power, release to fire.</summary>
        Charge,
    }

    /// <summary>How a shot reaches its target.</summary>
    public enum BallisticsType : byte
    {
        /// <summary>Instant ray.</summary>
        Hitscan,
        /// <summary>A simulated object with travel time, and optionally drop, bounces and an explosion.</summary>
        Projectile,
        /// <summary>A short sweep in front of the player.</summary>
        Melee,
    }

    public enum ReloadStyle : byte
    {
        /// <summary>One action refills the whole magazine.</summary>
        Magazine,
        /// <summary>Rounds go in one at a time, and firing interrupts the reload.</summary>
        PerRound,
    }

    /// <summary>What is drawn at screen centre while aiming.</summary>
    public enum ReticleStyle : byte
    {
        /// <summary>Iron sights: nothing is drawn, the weapon model's sights do the work.</summary>
        None,
        Dot,
        Cross,
        Chevron,
        /// <summary>Full-screen scope view.</summary>
        Scope,
    }

    public enum AttachmentSlot : byte
    {
        Sight,
        Barrel,
        Magazine,
        Grip,
        Stock,
        Laser,
    }

    /// <summary>A weapon number that attachments can change. See <see cref="WeaponStats.Apply"/> for what each one touches.</summary>
    public enum WeaponStat : byte
    {
        Damage,
        FireRate,
        Range,
        MagazineSize,
        ReloadTime,
        AdsTime,
        MoveSpeed,
        AdsMoveSpeed,
        SprintToFireTime,
        SwapTime,
        HipSpread,
        AdsSpread,
        Bloom,
        RecoilVertical,
        RecoilHorizontal,
        RecoilRecovery,
        ProjectileSpeed,
    }

    public enum ModifierOp : byte
    {
        /// <summary>Scales the stat. 0.9 is ten percent less.</summary>
        Multiply,
        /// <summary>Adds to the stat before any multipliers.</summary>
        Add,
    }

    /// <summary>Which placeholder part an attachment adds to the weapon model.</summary>
    public enum AttachmentVisual : byte
    {
        None,
        RedDot,
        Holo,
        Scope,
        Suppressor,
        LongBarrel,
        MuzzleDevice,
        ExtendedMagazine,
        DrumMagazine,
        VerticalGrip,
        AngledGrip,
        LightStock,
        HeavyStock,
        NoStock,
        Laser,
    }

    public enum ViewModelKind : byte
    {
        Gun,
        Launcher,
        Blade,
        Thrown,
    }

    public enum ShotSoundKind : byte
    {
        Ballistic,
        Energy,
        Thump,
        Swing,
        Twang,
    }
}
