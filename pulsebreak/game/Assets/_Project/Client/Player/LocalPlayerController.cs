using System;
using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Client.Controls;
using Pulsebreak.Client.Settings;
using Pulsebreak.Client.Weapons;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Player
{
    /// <summary>
    /// Drives the local player. Each rendered frame it:
    ///   1. samples input,
    ///   2. runs as many fixed simulation ticks as have come due (movement, utility, the weapon
    ///      in hand, its shots, then projectiles in flight),
    ///   3. presents the result, blending between the last two ticks so motion is smooth at any frame rate.
    ///
    /// Offline this object is also the authority: it applies damage itself. Once the game is
    /// networked, step 2 is what moves into the netcode's tick callback, and damage is applied
    /// by the server instead.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class LocalPlayerController : MonoBehaviour
    {
        const float KillPlaneY = -30f;

        PlayerInputReader _input;
        PlayerMotor _motor;
        ProjectileSystem _projectiles;
        FirstPersonCamera _camera;
        WeaponViewModel _viewModel;

        // The loadout, and the running state of everything in it.
        Loadout _loadout;
        WeaponBuild[] _builds;
        WeaponSimulator[] _weapons;
        WeaponInventory _inventory;
        UtilityBelt _belt;
        bool _infiniteReserve;
        bool _rechargeUtilities;

        readonly List<HitReport> _reports = new List<HitReport>(8);

        int _tick;
        float _accumulator;
        Vector3 _spawnPosition;
        float _spawnYaw;

        // The last two simulated states, blended for presentation.
        MotorState _previousMotor, _currentMotor;
        Vector2 _previousRecoil, _currentRecoil;

        public PlayerMotor Motor => _motor;
        public MotorState MotorState => _currentMotor;
        public WeaponInventory Inventory => _inventory;
        public ProjectileSystem Projectiles => _projectiles;
        public Loadout Loadout => _loadout;
        public UtilityBelt Utilities => _belt;

        /// <summary>The weapon in hand.</summary>
        public WeaponSimulator ActiveWeapon => _inventory.Active;
        public WeaponBuild ActiveBuild => _builds[_inventory.ActiveIndex];
        public int SlotCount => _builds.Length;

        /// <summary>How far the current frame sits between the last two ticks, 0..1.</summary>
        public float TickAlpha { get; private set; }

        /// <summary>False while a menu is open: the player keeps simulating but receives no input.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>Reloads never drain the reserve. Used by the firing range.</summary>
        public bool InfiniteReserve
        {
            get => _infiniteReserve;
            set
            {
                _infiniteReserve = value;
                if (_weapons == null) return;
                foreach (WeaponSimulator weapon in _weapons)
                {
                    if (weapon != null) weapon.InfiniteReserve = value;
                }
            }
        }

        /// <summary>Used utility items come back over time. On in the firing range.</summary>
        public bool RechargeUtilities
        {
            get => _rechargeUtilities;
            set
            {
                _rechargeUtilities = value;
                if (_belt != null) _belt.Recharges = value;
            }
        }

        // Presentation events. They keep working when the loadout changes, so listeners
        // subscribe once to the player rather than to individual weapons.
        /// <summary>The trigger produced a shot (once per shot, however many pellets).</summary>
        public event Action<WeaponBuild> WeaponFired;
        /// <summary>The path of one bullet or pellet.</summary>
        public event Action<ShotPresentation> ShotFired;
        /// <summary>A projectile was launched: its id, the stats it flies with, and its body and accent colours.</summary>
        public event Action<int, WeaponStats, Color, Color> ProjectileLaunched;
        /// <summary>The wind-up of a utility throw began.</summary>
        public event Action<UtilityDefinition> UtilityThrowStarted;
        /// <summary>A utility item left the hand.</summary>
        public event Action<UtilityDefinition> UtilityThrown;
        /// <summary>A shot (bullet, blast, projectile or swing) damaged at least one target.</summary>
        public event Action ShotLanded;
        public event Action<HitReport> HitConfirmed;
        public event Action<WeaponBuild> ActiveWeaponChanged;
        public event Action LoadoutChanged;
        public event Action ReloadStarted;
        public event Action RoundInserted;
        public event Action ReloadFinished;
        public event Action DryFired;

        public void Initialize(MovementConfig movement, Loadout loadout, ProjectileSystem projectiles,
            FirstPersonCamera playerCamera, WeaponViewModel viewModel, float yaw)
        {
            _camera = playerCamera;
            _viewModel = viewModel;
            _projectiles = projectiles;
            _spawnPosition = transform.position;
            _spawnYaw = yaw;

            _input = new PlayerInputReader();
            _motor = new PlayerMotor(GetComponent<CharacterController>(), movement);

            _projectiles.HitConfirmed += report => HitConfirmed?.Invoke(report);
            _projectiles.Happened += OnProjectileEvent;

            SetLoadout(loadout, 0);
            Respawn();
        }

        // ------------------------------------------------------------------ loadout

        public WeaponBuild GetBuild(int slot) => _builds[slot];

        /// <summary>Replaces everything the player carries. Weapons and utility start full.</summary>
        public void SetLoadout(Loadout loadout, int activeSlot)
        {
            _loadout = loadout;
            _builds = loadout.Weapons;
            _weapons = new WeaponSimulator[_builds.Length];
            for (int i = 0; i < _builds.Length; i++)
            {
                if (_builds[i] == null) continue;

                var weapon = new WeaponSimulator(loadout.ResolveStats(i), seed: 0x5EED0000u + (uint)i)
                {
                    InfiniteReserve = _infiniteReserve,
                };
                int slot = i;
                weapon.Fired += () => WeaponFired?.Invoke(_builds[slot]);
                weapon.ReloadStarted += () => ReloadStarted?.Invoke();
                weapon.RoundInserted += () => RoundInserted?.Invoke();
                weapon.ReloadFinished += () => ReloadFinished?.Invoke();
                weapon.DryFired += () => DryFired?.Invoke();
                _weapons[i] = weapon;
            }

            _inventory = new WeaponInventory(_weapons, activeSlot);
            _inventory.ActiveChanged += _ => OnActiveWeaponChanged();
            _previousRecoil = _currentRecoil = Vector2.zero;

            var charges = new int[loadout.Utilities.Length];
            for (int i = 0; i < charges.Length; i++) charges[i] = loadout.UtilityCharges(i);
            _belt = new UtilityBelt(loadout.Utilities, charges)
            {
                Recharges = _rechargeUtilities,
                RechargeMultiplier = loadout.UtilityRechargeMultiplier,
            };
            _belt.ThrowStarted += slot => UtilityThrowStarted?.Invoke(_loadout.Utilities[slot]);

            LoadoutChanged?.Invoke();
            OnActiveWeaponChanged();
        }

        void OnActiveWeaponChanged()
        {
            _viewModel.Show(ActiveBuild);
            ActiveWeaponChanged?.Invoke(ActiveBuild);
        }

        public void Respawn()
        {
            _input.SetView(_spawnYaw, 0f);
            _motor.Teleport(_spawnPosition, _spawnYaw, 0f);
            _previousMotor = _currentMotor = _motor.State;
            _accumulator = 0f;
        }

        void OnDestroy()
        {
            _input?.Dispose();
        }

        // ------------------------------------------------------------------ simulation

        void Update()
        {
            if (_motor == null) return;

            if (InputEnabled) _input.SampleFrame(LookScale());

            _accumulator += Mathf.Min(Time.deltaTime, 0.25f);
            int steps = 0;
            while (_accumulator >= SimConstants.TickDelta)
            {
                if (steps == SimConstants.MaxTicksPerFrame)
                {
                    // Too far behind to catch up; drop the backlog rather than spiral.
                    _accumulator = 0f;
                    break;
                }
                Step();
                _accumulator -= SimConstants.TickDelta;
                steps++;
            }

            if (_currentMotor.Position.y < KillPlaneY) Respawn();

            TickAlpha = Mathf.Clamp01(_accumulator / SimConstants.TickDelta);
            Present(TickAlpha);
        }

        /// <summary>One fixed simulation tick.</summary>
        void Step()
        {
            const float dt = SimConstants.TickDelta;
            PlayerInputCmd cmd = InputEnabled
                ? _input.BuildCmd(_tick, _inventory.TargetIndex, _inventory.SlotCount)
                : _input.BuildIdleCmd(_tick);

            _previousMotor = _currentMotor;
            _previousRecoil = _currentRecoil;

            WeaponSimulator inHand = _inventory.Active;
            var modifiers = new MoveModifiers
            {
                SpeedMultiplier = _loadout.MoveSpeedMultiplier * Mathf.Lerp(
                    inHand.Stats.hipMoveMultiplier, inHand.Stats.adsMoveMultiplier, inHand.State.AdsAmount),
            };
            _motor.Simulate(cmd, modifiers, dt);
            _currentMotor = _motor.State;

            // A throw winding up keeps the hands off the weapon.
            _belt.Simulate(cmd, !_currentMotor.Mantling, dt);

            var context = new WeaponContext
            {
                CanUseWeapon = !_currentMotor.Mantling && !_belt.IsThrowing,
                Sprinting = _currentMotor.Sprinting,
                TimeSinceSprint = _currentMotor.TimeSinceSprint,
                HorizontalSpeed = _currentMotor.HorizontalSpeed,
                Grounded = _currentMotor.Grounded,
                Crouched = _currentMotor.Stance != Stance.Standing,
            };
            _inventory.Simulate(cmd, context, dt);
            _currentRecoil = _inventory.Active.State.RecoilCurrent;

            // Shots belong to the weapon that was simulated, which is not necessarily the one
            // now in hand if a switch completed this tick.
            WeaponSimulator fired = _inventory.Simulated;
            bool anythingToHit = fired.Shots.Count > 0 || _projectiles.Live.Count > 0 || _belt.ReleasedSlot >= 0;

            // Targets move in Update; make sure the physics scene has their latest poses.
            if (anythingToHit) Physics.SyncTransforms();

            if (fired.Shots.Count > 0) ResolveShots(cmd, fired, _builds[Array.IndexOf(_weapons, fired)]);
            if (_belt.ReleasedSlot >= 0) ThrowUtility(cmd, _loadout.Utilities[_belt.ReleasedSlot]);
            _projectiles.Step(_tick, dt);

            _tick++;
        }

        void ResolveShots(in PlayerInputCmd cmd, WeaponSimulator weapon, WeaponBuild build)
        {
            WeaponStats stats = weapon.Stats;
            string weaponId = build.Weapon.weaponId;
            Vector3 origin = _currentMotor.EyePosition;
            bool bulletLanded = false;

            for (int i = 0; i < weapon.Shots.Count; i++)
            {
                ShotEvent shot = weapon.Shots[i];
                Vector3 direction = AimMath.Direction(cmd.Yaw + shot.AimOffset.x, cmd.Pitch - shot.AimOffset.y);

                switch (shot.Kind)
                {
                    case ShotKind.Bullet:
                    {
                        _reports.Clear();
                        HitscanHit end = CombatResolver.FireBullet(origin, direction, stats, weaponId, shot.DamageScale, cmd.Tick, _reports);
                        foreach (HitReport report in _reports) HitConfirmed?.Invoke(report);
                        bulletLanded |= _reports.Count > 0;

                        ShotFired?.Invoke(new ShotPresentation
                        {
                            Origin = origin,
                            End = end.Point,
                            Normal = end.Normal,
                            HitWorld = end.HasHit && end.Hitbox == null,
                            HitTarget = _reports.Count > 0,
                            Build = build,
                        });
                        break;
                    }

                    case ShotKind.Projectile:
                    {
                        int id = _projectiles.Spawn(origin, direction, stats, weaponId, shot.DamageScale);
                        ProjectileLaunched?.Invoke(id, stats, build.Weapon.view.bodyColor, build.Weapon.view.accentColor);
                        break;
                    }

                    case ShotKind.Melee:
                    {
                        if (CombatResolver.MeleeStrike(origin, direction, stats, weaponId, cmd.Tick, out HitReport report))
                        {
                            HitConfirmed?.Invoke(report);
                            ShotLanded?.Invoke();
                        }
                        break;
                    }
                }
            }

            // A shotgun blast counts as one shot landing, however many pellets connected.
            if (bulletLanded) ShotLanded?.Invoke();
        }

        /// <summary>A utility item leaves the hand: thrown where the player is looking, lobbed slightly upward.</summary>
        void ThrowUtility(in PlayerInputCmd cmd, UtilityDefinition item)
        {
            Vector3 direction = AimMath.Direction(cmd.Yaw, cmd.Pitch - item.tossPitch);
            int id = _projectiles.Spawn(_currentMotor.EyePosition, direction, item.projectile, item.utilityId, 1f);
            ProjectileLaunched?.Invoke(id, item.projectile, item.color, item.color);
            UtilityThrown?.Invoke(item);
        }

        void OnProjectileEvent(ProjectileEvent projectileEvent)
        {
            if (projectileEvent.HitTarget) ShotLanded?.Invoke();
        }

        // ------------------------------------------------------------------ presentation

        /// <summary>Renders the state between the last two ticks. <paramref name="alpha"/> is 0..1 between them.</summary>
        void Present(float alpha)
        {
            MotorState a = _previousMotor, b = _currentMotor;
            WeaponSimulator inHand = _inventory.Active;
            WeaponState weapon = inHand.State;
            WeaponStats stats = inHand.Stats;

            // Position is blended between ticks; the view angles are not, they come straight
            // from this frame's mouse input so aiming has no added latency.
            float yaw = _input.Yaw, pitch = _input.Pitch;
            Vector2 recoil = Vector2.Lerp(_previousRecoil, _currentRecoil, alpha);
            Vector3 feet = Vector3.Lerp(a.Position, b.Position, alpha);
            Vector3 eye = feet
                          + Vector3.up * Mathf.Lerp(a.EyeHeight, b.EyeHeight, alpha)
                          + AimMath.YawRight(yaw) * Mathf.Lerp(a.LeanOffset, b.LeanOffset, alpha);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            float viewYaw = yaw + recoil.x, viewPitch = pitch - recoil.y;
            _camera.Apply(new CameraPose
            {
                EyePosition = eye,
                Yaw = viewYaw,
                Pitch = viewPitch,
                Lean = Mathf.Lerp(a.Lean, b.Lean, alpha),
                LeanAngle = _motor.Config.leanAngle,
                AdsAmount = weapon.AdsAmount,
                AdsZoom = stats.adsZoom,
                Sprinting = b.Sprinting,
                Sliding = b.Stance == Stance.Sliding,
            }, Time.deltaTime);

            bool swapping = weapon.Phase == WeaponPhase.Holstering || weapon.Phase == WeaponPhase.Equipping;
            _viewModel.Tick(new ViewModelInput
            {
                AdsAmount = weapon.AdsAmount,
                Sprinting = b.Sprinting,
                Lowered = b.Mantling || _belt.IsThrowing || weapon.Phase == WeaponPhase.Holstering,
                Reloading = weapon.Phase == WeaponPhase.Reloading,
                ReloadProgress = weapon.ReloadProgress,
                Charge = weapon.ChargeAmount,
                // A full scope view replaces the weapon model while it is up.
                Hidden = !swapping && ActiveBuild.Reticle == ReticleStyle.Scope && weapon.AdsAmount > 0.9f,
                MoveAmount = b.Grounded ? Mathf.Clamp01(b.HorizontalSpeed / _motor.Config.sprintSpeed) : 0f,
                Yaw = viewYaw,
                Pitch = viewPitch,
            }, Time.deltaTime);
        }

        /// <summary>
        /// Final mouse sensitivity. While aiming it scales with the zoom, so the same hand
        /// movement covers the same distance on screen at any magnification.
        /// </summary>
        float LookScale()
        {
            WeaponSimulator inHand = _inventory.Active;
            float adsScale = GameSettings.AdsSensitivityMultiplier / Mathf.Max(inHand.Stats.adsZoom, 1f);
            return GameSettings.Sensitivity * Mathf.Lerp(1f, adsScale, inHand.State.AdsAmount);
        }
    }
}
