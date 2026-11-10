using System;
using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// Runs one weapon: trigger logic, fire rate, ammo, reload, aim-down-sights, spread and recoil.
    ///
    /// It is pure logic. It decides when a bullet leaves and at what angle offset, and lists
    /// those in <see cref="Shots"/>; tracing the bullet through the world is the caller's job
    /// (see <see cref="Hitscan"/>). Fire rate and ammo are enforced here, per tick, which is
    /// exactly what a server needs in order to refuse shots a cheating client invents.
    /// </summary>
    public sealed class WeaponSimulator
    {
        const float SpreadReferenceSpeed = 6f;
        const float TriggerBufferTime = 0.1f;
        const int MaxShotsPerTick = 8;

        readonly WeaponDefinition _definition;
        readonly uint _seed;
        readonly List<ShotEvent> _shots = new List<ShotEvent>(4);
        WeaponState _state;

        /// <summary>Presentation hooks (audio, viewmodel). Never drive gameplay from these.</summary>
        public event Action ReloadStarted;
        public event Action ReloadFinished;
        public event Action DryFired;

        public WeaponDefinition Definition => _definition;
        public WeaponState State => _state;

        /// <summary>Shots produced by the most recent <see cref="Simulate"/> call.</summary>
        public IReadOnlyList<ShotEvent> Shots => _shots;

        /// <summary>Spread cone half-angle right now, in degrees. Drives the crosshair.</summary>
        public float CurrentSpread { get; private set; }

        /// <summary>Reloads never drain the reserve. Used by the firing range.</summary>
        public bool InfiniteReserve { get; set; }

        bool HasReserve => InfiniteReserve || _state.AmmoReserve > 0;

        public WeaponSimulator(WeaponDefinition definition, uint seed)
        {
            _definition = definition;
            _seed = seed;
            _state.AmmoInMagazine = definition.magazineSize;
            _state.AmmoReserve = definition.reserveAmmo;
            _state.TimeSinceLastShot = 10f;
            if (definition.equipTime > 0f)
            {
                _state.Phase = WeaponPhase.Equipping;
                _state.PhaseTimer = _state.PhaseDuration = definition.equipTime;
            }
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
            if (firePressed)
            {
                // A pull made while still coming out of a sprint waits for the weapon to come up.
                bool sprintBlocked = context.Sprinting || context.TimeSinceSprint < _definition.sprintToFireTime;
                _state.TriggerBuffer = TriggerBufferTime + (sprintBlocked ? _definition.sprintToFireTime : 0f);
            }

            AdvancePhase(dt);

            if (_state.Phase == WeaponPhase.Ready && _state.BurstShotsRemaining == 0)
            {
                bool manual = (pressed & InputButtons.Reload) != 0 && _state.AmmoInMagazine < _definition.magazineSize;
                bool automatic = _state.AmmoInMagazine == 0 && _state.FireCooldown <= 0f;
                if ((manual || automatic) && HasReserve) BeginReload();
            }

            bool wantsAds = cmd.Has(InputButtons.Ads) && context.CanUseWeapon && !context.Sprinting;
            _state.AdsAmount = Mathf.MoveTowards(
                _state.AdsAmount, wantsAds ? 1f : 0f, dt / Mathf.Max(_definition.adsTime, 0.01f));

            CurrentSpread = ComputeSpread(context);

            bool canShoot = _state.Phase == WeaponPhase.Ready
                            && context.CanUseWeapon
                            && !context.Sprinting
                            && context.TimeSinceSprint >= _definition.sprintToFireTime;

            bool fired = false;
            if (canShoot) fired = FireShots(cmd, context, fireHeld);
            else _state.BurstShotsRemaining = 0;

            // Only consecutive shots carry their timing remainder. Idle time is never banked.
            if (!fired && _state.FireCooldown < 0f) _state.FireCooldown = 0f;

            if (firePressed && _state.Phase == WeaponPhase.Ready && _state.AmmoInMagazine == 0 && !HasReserve)
                DryFired?.Invoke();

            SettleRecoil(dt);
        }

        bool FireShots(in PlayerInputCmd cmd, in WeaponContext context, bool fireHeld)
        {
            bool fired = false;
            for (int i = 0; i < MaxShotsPerTick && _state.FireCooldown <= 0f; i++)
            {
                if (_state.AmmoInMagazine <= 0)
                {
                    _state.BurstShotsRemaining = 0;
                    break;
                }

                bool wantsShot;
                switch (_definition.fireMode)
                {
                    case FireMode.Automatic:
                        wantsShot = fireHeld;
                        break;
                    case FireMode.SemiAuto:
                        wantsShot = _state.TriggerBuffer > 0f;
                        break;
                    default:
                        if (_state.BurstShotsRemaining == 0 && _state.TriggerBuffer > 0f)
                            _state.BurstShotsRemaining = Mathf.Max(1, _definition.burstCount);
                        wantsShot = _state.BurstShotsRemaining > 0;
                        break;
                }
                if (!wantsShot) break;

                _state.TriggerBuffer = 0f;
                FireOne(cmd, context);
                fired = true;

                // Adding (not assigning) keeps the average rate exact even when the
                // interval is not a whole number of ticks.
                _state.FireCooldown += _definition.SecondsPerShot;

                if (_definition.fireMode == FireMode.Burst && --_state.BurstShotsRemaining == 0)
                    _state.FireCooldown += _definition.burstCooldown;
            }
            return fired;
        }

        void FireOne(in PlayerInputCmd cmd, in WeaponContext context)
        {
            _state.AmmoInMagazine--;

            // Seeded per shot: the same weapon seed and shot number always give the same bullet.
            var random = new DeterministicRandom(DeterministicRandom.Combine(_seed, _state.ShotCounter));

            float spread = ComputeSpread(context);
            float radius = Mathf.Sqrt(random.NextFloat()) * spread;
            float angle = random.NextFloat() * (2f * Mathf.PI);
            var spreadOffset = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

            _shots.Add(new ShotEvent
            {
                Tick = cmd.Tick,
                ShotNumber = _state.ShotCounter,
                AimOffset = _state.RecoilCurrent + spreadOffset,
                Spread = spread,
            });

            Vector2 kick = _definition.GetRecoilKick(_state.SprayIndex);
            float jitter = kick.magnitude * _definition.recoilRandomness;
            kick += new Vector2(random.NextSigned(), random.NextSigned()) * jitter;

            float scale = Mathf.Lerp(1f, _definition.adsRecoilMultiplier, _state.AdsAmount);
            if (context.Crouched && context.Grounded) scale *= _definition.crouchRecoilMultiplier;

            _state.RecoilTarget += kick * scale;
            _state.SprayIndex++;
            _state.Bloom = Mathf.Min(_state.Bloom + _definition.bloomPerShot, _definition.maxBloom);
            _state.TimeSinceLastShot = 0f;
            _state.ShotCounter++;
        }

        float ComputeSpread(in WeaponContext context)
        {
            float moving = Mathf.Clamp01(context.HorizontalSpeed / SpreadReferenceSpeed);
            float air = context.Grounded ? 0f : _definition.airSpreadPenalty;

            float hip = _definition.hipSpread + _definition.moveSpreadPenalty * moving + air;
            if (context.Crouched && context.Grounded) hip *= _definition.crouchSpreadMultiplier;
            hip += _state.Bloom;

            float ads = _definition.adsSpread + air * 0.5f + _state.Bloom * _definition.adsBloomMultiplier;
            return Mathf.Lerp(hip, ads, _state.AdsAmount);
        }

        void SettleRecoil(float dt)
        {
            if (_state.TimeSinceLastShot >= _definition.recoilRecoveryDelay)
                _state.RecoilTarget = Vector2.MoveTowards(_state.RecoilTarget, Vector2.zero, _definition.recoilRecovery * dt);

            float blend = 1f - Mathf.Exp(-_definition.recoilSnappiness * dt);
            _state.RecoilCurrent = Vector2.Lerp(_state.RecoilCurrent, _state.RecoilTarget, blend);

            if (_state.TimeSinceLastShot >= _definition.recoilResetTime) _state.SprayIndex = 0;
            if (_state.TimeSinceLastShot > _definition.SecondsPerShot * 1.5f)
                _state.Bloom = Mathf.MoveTowards(_state.Bloom, 0f, _definition.bloomRecovery * dt);
        }

        void AdvancePhase(float dt)
        {
            if (_state.Phase == WeaponPhase.Ready) return;

            _state.PhaseTimer -= dt;
            if (_state.PhaseTimer > 0f) return;

            bool wasReloading = _state.Phase == WeaponPhase.Reloading;
            _state.Phase = WeaponPhase.Ready;
            _state.PhaseTimer = 0f;
            if (wasReloading) FinishReload();
        }

        void BeginReload()
        {
            _state.Phase = WeaponPhase.Reloading;
            _state.PhaseDuration = _state.AmmoInMagazine == 0 ? _definition.emptyReloadTime : _definition.reloadTime;
            _state.PhaseTimer = _state.PhaseDuration;
            _state.BurstShotsRemaining = 0;
            ReloadStarted?.Invoke();
        }

        void FinishReload()
        {
            int needed = _definition.magazineSize - _state.AmmoInMagazine;
            int taken = InfiniteReserve ? needed : Mathf.Min(needed, _state.AmmoReserve);
            _state.AmmoInMagazine += taken;
            if (!InfiniteReserve) _state.AmmoReserve -= taken;
            ReloadFinished?.Invoke();
        }
    }
}
