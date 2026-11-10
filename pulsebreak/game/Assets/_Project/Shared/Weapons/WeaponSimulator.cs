using System;
using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// Runs one weapon: trigger logic, fire rate, ammo, reloading, aim-down-sights, spread and
    /// recoil, for every fire mode and delivery type.
    ///
    /// It is pure logic. It decides when something leaves the weapon and at what angle offset,
    /// and lists those in <see cref="Shots"/>; turning a shot into a ray, a projectile or a
    /// melee sweep is the caller's job. Fire rate and ammo are enforced here, per tick, which
    /// is exactly what a server needs in order to refuse shots a cheating client invents.
    /// </summary>
    public sealed class WeaponSimulator
    {
        const float SpreadReferenceSpeed = 6f;
        const float TriggerBufferTime = 0.1f;
        const int MaxShotsPerTick = 8;

        readonly WeaponStats _stats;
        readonly uint _seed;
        readonly List<ShotEvent> _shots = new List<ShotEvent>(12);
        WeaponState _state;

        /// <summary>Presentation hooks (audio, weapon model). Never drive gameplay from these.</summary>
        public event Action Fired;
        public event Action ReloadStarted;
        public event Action RoundInserted;
        public event Action ReloadFinished;
        public event Action DryFired;

        /// <summary>The numbers this weapon runs on, attachments already applied.</summary>
        public WeaponStats Stats => _stats;
        public WeaponState State => _state;

        /// <summary>Shots produced by the most recent <see cref="Simulate"/> call.</summary>
        public IReadOnlyList<ShotEvent> Shots => _shots;

        /// <summary>Spread cone half-angle right now, in degrees. Drives the crosshair.</summary>
        public float CurrentSpread { get; private set; }

        /// <summary>Reloads never drain the reserve. Used by the firing range.</summary>
        public bool InfiniteReserve { get; set; }

        /// <summary>The weapon has been put away and the next one can come up.</summary>
        public bool HolsterComplete => _state.Phase == WeaponPhase.Holstering && _state.PhaseTimer <= 0f;

        bool HasReserve => InfiniteReserve || _state.AmmoReserve > 0;

        public WeaponSimulator(WeaponStats stats, uint seed)
        {
            _stats = stats;
            _seed = seed;
            _state.AmmoInMagazine = stats.magazineSize;
            _state.AmmoReserve = stats.reserveAmmo;
            _state.TimeSinceLastShot = 10f;
        }

        /// <summary>
        /// Starts bringing the weapon up. Recoil still on the view from the previous weapon is
        /// carried over so the camera does not snap, and buttons already held do not count as
        /// fresh presses.
        /// </summary>
        public void BeginEquip(Vector2 recoilCurrent, Vector2 recoilTarget, InputButtons heldButtons)
        {
            _state.Phase = _stats.equipTime > 0f ? WeaponPhase.Equipping : WeaponPhase.Ready;
            _state.PhaseTimer = _state.PhaseDuration = _stats.equipTime;
            ResetTransientState();
            _state.RecoilCurrent = recoilCurrent;
            _state.RecoilTarget = recoilTarget;
            _state.PrevButtons = heldButtons;
        }

        /// <summary>Starts putting the weapon away. Cancels a reload, burst, charge or swing in progress.</summary>
        public void BeginHolster()
        {
            _state.Phase = WeaponPhase.Holstering;
            _state.PhaseTimer = _state.PhaseDuration = _stats.holsterTime;
            _state.BurstShotsRemaining = 0;
            _state.ChargeAmount = 0f;
            _state.MeleePending = false;
            _state.TriggerBuffer = 0f;
        }

        void ResetTransientState()
        {
            _state.FireCooldown = 0f;
            _state.TriggerBuffer = 0f;
            _state.BurstShotsRemaining = 0;
            _state.ChargeAmount = 0f;
            _state.MeleePending = false;
            _state.AdsAmount = 0f;
            _state.Bloom = 0f;
            _state.SprayIndex = 0;
            _state.TimeSinceLastShot = 10f;
        }

        /// <summary>Advances the weapon by one tick.</summary>
        public void Simulate(in PlayerInputCmd cmd, in WeaponContext context, float dt)
        {
            _shots.Clear();

            InputButtons pressed = cmd.Buttons & ~_state.PrevButtons;
            _state.PrevButtons = cmd.Buttons;
            bool fireHeld = cmd.Has(InputButtons.Fire);
            bool firePressed = (pressed & InputButtons.Fire) != 0;

            _state.TimeSinceLastShot += dt;
            _state.FireCooldown -= dt;
            _state.TriggerBuffer = Mathf.Max(0f, _state.TriggerBuffer - dt);
            if (firePressed && _state.Phase != WeaponPhase.Holstering)
            {
                // A pull made while still coming out of a sprint waits for the weapon to come up.
                bool sprintBlocked = context.Sprinting || context.TimeSinceSprint < _stats.sprintToFireTime;
                _state.TriggerBuffer = TriggerBufferTime + (sprintBlocked ? _stats.sprintToFireTime : 0f);
            }

            AdvancePhase(dt, firePressed);

            if (_state.Phase == WeaponPhase.Ready && _stats.UsesAmmo
                && _state.BurstShotsRemaining == 0 && _state.ChargeAmount <= 0f)
            {
                bool manual = (pressed & InputButtons.Reload) != 0 && _state.AmmoInMagazine < _stats.magazineSize;
                bool automatic = _state.AmmoInMagazine == 0 && _state.FireCooldown <= 0f;
                if ((manual || automatic) && HasReserve) BeginReload();
            }

            bool wantsAds = cmd.Has(InputButtons.Ads) && _stats.CanAim && context.CanUseWeapon
                            && !context.Sprinting && _state.Phase != WeaponPhase.Holstering;
            _state.AdsAmount = Mathf.MoveTowards(
                _state.AdsAmount, wantsAds ? 1f : 0f, dt / Mathf.Max(_stats.adsTime, 0.01f));

            CurrentSpread = ComputeSpread(context);

            bool canShoot = _state.Phase == WeaponPhase.Ready
                            && context.CanUseWeapon
                            && !context.Sprinting
                            && context.TimeSinceSprint >= _stats.sprintToFireTime;

            bool fired = false;
            if (canShoot)
            {
                fired = _stats.fireMode == FireMode.Charge
                    ? SimulateCharge(cmd, context, fireHeld, dt)
                    : FireShots(cmd, context, fireHeld);
            }
            else
            {
                _state.BurstShotsRemaining = 0;
                _state.ChargeAmount = 0f;
            }

            // Only consecutive shots carry their timing remainder. Idle time is never banked.
            if (!fired && _state.FireCooldown < 0f) _state.FireCooldown = 0f;

            // A swing started earlier connects once its wind-up has passed.
            if (_state.MeleePending)
            {
                _state.MeleeTimer -= dt;
                if (_state.MeleeTimer <= 0f)
                {
                    _state.MeleePending = false;
                    _shots.Add(new ShotEvent
                    {
                        Tick = cmd.Tick,
                        ShotNumber = _state.ShotCounter,
                        Kind = ShotKind.Melee,
                        DamageScale = 1f,
                    });
                }
            }

            if (firePressed && _stats.UsesAmmo && _state.Phase == WeaponPhase.Ready
                && _state.AmmoInMagazine == 0 && !HasReserve)
                DryFired?.Invoke();

            SettleRecoil(dt);
        }

        // ------------------------------------------------------------------ firing

        bool FireShots(in PlayerInputCmd cmd, in WeaponContext context, bool fireHeld)
        {
            bool fired = false;
            for (int i = 0; i < MaxShotsPerTick && _state.FireCooldown <= 0f; i++)
            {
                if (_stats.UsesAmmo && _state.AmmoInMagazine <= 0)
                {
                    _state.BurstShotsRemaining = 0;
                    break;
                }

                bool wantsShot;
                switch (_stats.fireMode)
                {
                    case FireMode.Automatic:
                        wantsShot = fireHeld;
                        break;
                    case FireMode.SemiAuto:
                        wantsShot = _state.TriggerBuffer > 0f;
                        break;
                    default:
                        if (_state.BurstShotsRemaining == 0 && _state.TriggerBuffer > 0f)
                            _state.BurstShotsRemaining = Mathf.Max(1, _stats.burstCount);
                        wantsShot = _state.BurstShotsRemaining > 0;
                        break;
                }
                if (!wantsShot) break;

                _state.TriggerBuffer = 0f;
                FireOne(cmd, context, 1f);
                fired = true;

                // Adding (not assigning) keeps the average rate exact even when the
                // interval is not a whole number of ticks.
                _state.FireCooldown += _stats.SecondsPerShot;

                if (_stats.fireMode == FireMode.Burst && --_state.BurstShotsRemaining == 0)
                    _state.FireCooldown += _stats.burstCooldown;
            }
            return fired;
        }

        /// <summary>Charge weapons build power while the trigger is held and fire when it is let go.</summary>
        bool SimulateCharge(in PlayerInputCmd cmd, in WeaponContext context, bool fireHeld, float dt)
        {
            if (_stats.UsesAmmo && _state.AmmoInMagazine <= 0)
            {
                _state.ChargeAmount = 0f;
                return false;
            }

            if (fireHeld)
            {
                if (_state.FireCooldown <= 0f)
                    _state.ChargeAmount = Mathf.Min(1f, _state.ChargeAmount + dt / Mathf.Max(_stats.chargeTime, 0.01f));
                return false;
            }

            if (_state.ChargeAmount <= 0f) return false;

            float charge = _state.ChargeAmount;
            _state.ChargeAmount = 0f;
            FireOne(cmd, context, Mathf.Lerp(_stats.minChargeDamage, 1f, charge));
            _state.FireCooldown = _stats.SecondsPerShot;
            return true;
        }

        void FireOne(in PlayerInputCmd cmd, in WeaponContext context, float damageScale)
        {
            if (_stats.UsesAmmo) _state.AmmoInMagazine--;

            // Seeded per shot: the same weapon seed and shot number always give the same result.
            var random = new DeterministicRandom(DeterministicRandom.Combine(_seed, _state.ShotCounter));

            if (_stats.ballistics == BallisticsType.Melee)
            {
                _state.MeleePending = true;
                _state.MeleeTimer = _stats.meleeHitDelay;
            }
            else
            {
                float spread = ComputeSpread(context);
                ShotKind kind = _stats.ballistics == BallisticsType.Projectile ? ShotKind.Projectile : ShotKind.Bullet;
                int pellets = Mathf.Max(1, _stats.pellets);
                for (int pellet = 0; pellet < pellets; pellet++)
                {
                    float radius = Mathf.Sqrt(random.NextFloat()) * spread;
                    float angle = random.NextFloat() * (2f * Mathf.PI);
                    var spreadOffset = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

                    _shots.Add(new ShotEvent
                    {
                        Tick = cmd.Tick,
                        ShotNumber = _state.ShotCounter,
                        Kind = kind,
                        Pellet = pellet,
                        AimOffset = _state.RecoilCurrent + spreadOffset,
                        Spread = spread,
                        DamageScale = damageScale,
                    });
                }
            }

            Vector2 kick = _stats.GetRecoilKick(_state.SprayIndex);
            float jitter = kick.magnitude * _stats.recoilRandomness;
            kick += new Vector2(random.NextSigned(), random.NextSigned()) * jitter;

            float scale = Mathf.Lerp(1f, _stats.adsRecoilMultiplier, _state.AdsAmount);
            if (context.Crouched && context.Grounded) scale *= _stats.crouchRecoilMultiplier;

            _state.RecoilTarget += kick * scale;
            _state.SprayIndex++;
            _state.Bloom = Mathf.Min(_state.Bloom + _stats.bloomPerShot, _stats.maxBloom);
            _state.TimeSinceLastShot = 0f;
            _state.ShotCounter++;
            Fired?.Invoke();
        }

        float ComputeSpread(in WeaponContext context)
        {
            if (_stats.ballistics == BallisticsType.Melee) return 0f;

            float moving = Mathf.Clamp01(context.HorizontalSpeed / SpreadReferenceSpeed);
            float air = context.Grounded ? 0f : _stats.airSpreadPenalty;

            float hip = _stats.hipSpread + _stats.moveSpreadPenalty * moving + air;
            if (context.Crouched && context.Grounded) hip *= _stats.crouchSpreadMultiplier;
            hip += _state.Bloom;

            float ads = _stats.adsSpread + air * 0.5f + _state.Bloom * _stats.adsBloomMultiplier;
            return Mathf.Lerp(hip, ads, _state.AdsAmount);
        }

        void SettleRecoil(float dt)
        {
            if (_state.TimeSinceLastShot >= _stats.recoilRecoveryDelay)
                _state.RecoilTarget = Vector2.MoveTowards(_state.RecoilTarget, Vector2.zero, _stats.recoilRecovery * dt);

            float blend = 1f - Mathf.Exp(-_stats.recoilSnappiness * dt);
            _state.RecoilCurrent = Vector2.Lerp(_state.RecoilCurrent, _state.RecoilTarget, blend);

            if (_state.TimeSinceLastShot >= _stats.recoilResetTime) _state.SprayIndex = 0;
            if (_state.TimeSinceLastShot > _stats.SecondsPerShot * 1.5f)
                _state.Bloom = Mathf.MoveTowards(_state.Bloom, 0f, _stats.bloomRecovery * dt);
        }

        // ------------------------------------------------------------------ phases and reloading

        void AdvancePhase(float dt, bool firePressed)
        {
            switch (_state.Phase)
            {
                case WeaponPhase.Ready:
                    return;

                case WeaponPhase.Holstering:
                    _state.PhaseTimer = Mathf.Max(0f, _state.PhaseTimer - dt);
                    return;

                case WeaponPhase.Equipping:
                    _state.PhaseTimer -= dt;
                    if (_state.PhaseTimer <= 0f) SetReady();
                    return;

                case WeaponPhase.Reloading:
                    _state.ReloadElapsed += dt;

                    // Loading round by round can be cut short to shoot what is already in.
                    if (_stats.reloadStyle == ReloadStyle.PerRound && firePressed && _state.AmmoInMagazine > 0)
                    {
                        FinishReload();
                        return;
                    }

                    _state.PhaseTimer -= dt;
                    if (_state.PhaseTimer > 0f) return;

                    if (_stats.reloadStyle == ReloadStyle.PerRound)
                    {
                        _state.AmmoInMagazine += TakeFromReserve(1);
                        RoundInserted?.Invoke();
                        if (_state.AmmoInMagazine >= _stats.magazineSize || !HasReserve) FinishReload();
                        else _state.PhaseTimer += _stats.reloadTime;
                    }
                    else
                    {
                        _state.AmmoInMagazine += TakeFromReserve(_stats.magazineSize - _state.AmmoInMagazine);
                        FinishReload();
                    }
                    return;
            }
        }

        void SetReady()
        {
            _state.Phase = WeaponPhase.Ready;
            _state.PhaseTimer = 0f;
        }

        void BeginReload()
        {
            _state.Phase = WeaponPhase.Reloading;
            _state.BurstShotsRemaining = 0;
            _state.ReloadElapsed = 0f;

            if (_stats.reloadStyle == ReloadStyle.PerRound)
            {
                int missing = _stats.magazineSize - _state.AmmoInMagazine;
                int rounds = InfiniteReserve ? missing : Mathf.Min(missing, _state.AmmoReserve);
                _state.PhaseTimer = _stats.reloadIntroTime + _stats.reloadTime;
                _state.ReloadTotal = _stats.reloadIntroTime + rounds * _stats.reloadTime;
            }
            else
            {
                _state.PhaseTimer = _state.AmmoInMagazine == 0 ? _stats.emptyReloadTime : _stats.reloadTime;
                _state.ReloadTotal = _state.PhaseTimer;
            }

            _state.PhaseDuration = _state.PhaseTimer;
            ReloadStarted?.Invoke();
        }

        void FinishReload()
        {
            SetReady();
            ReloadFinished?.Invoke();
        }

        int TakeFromReserve(int wanted)
        {
            if (wanted <= 0) return 0;
            if (InfiniteReserve) return wanted;

            int taken = Mathf.Min(wanted, _state.AmmoReserve);
            _state.AmmoReserve -= taken;
            return taken;
        }
    }
}
