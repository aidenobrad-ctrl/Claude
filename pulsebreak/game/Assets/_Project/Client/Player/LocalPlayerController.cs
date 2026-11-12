using System;
using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Client.Controls;
using Pulsebreak.Client.Net;
using Pulsebreak.Client.Settings;
using Pulsebreak.Client.Weapons;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Net;
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
    /// It runs in one of two ways.
    ///
    /// Offline (the firing range) this object is the authority: it applies damage itself.
    ///
    /// Online it predicts. The same simulation runs here immediately, so the player never
    /// waits for the server to see themselves move or fire, but nothing it works out counts:
    /// each command also goes to the server, shots are drawn but deal no damage, and hits,
    /// projectiles and health all come back from the server through the <see cref="ClientSession"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class LocalPlayerController : MonoBehaviour
    {
        const float KillPlaneY = -30f;
        /// <summary>A correction bigger than this is shown at once; smaller ones are eased in.</summary>
        const float SnapDistance = 1.5f;
        /// <summary>How quickly an eased correction is absorbed. Higher is quicker.</summary>
        const float CorrectionEase = 14f;

        PlayerInputReader _input;
        PlayerSimulation _sim;
        ProjectileSystem _projectiles;
        FirstPersonCamera _camera;
        WeaponViewModel _viewModel;
        ClientSession _session;

        readonly List<HitReport> _reports = new List<HitReport>(8);

        int _tick;
        float _accumulator;
        Vector3 _spawnPosition;
        float _spawnYaw;

        // The last two simulated states, blended for presentation.
        MotorState _previousMotor, _currentMotor;
        Vector2 _previousRecoil, _currentRecoil;
        // Where the player is drawn, minus where the simulation has them. Not zero just after
        // the server corrects a prediction: the view then slides to the truth instead of jumping.
        Vector3 _viewError;

        public PlayerSimulation Simulation => _sim;
        public PlayerMotor Motor => _sim.Motor;
        public MotorState MotorState => _currentMotor;
        public WeaponInventory Inventory => _sim != null ? _sim.Inventory : null;
        public ProjectileSystem Projectiles => _projectiles;
        public Loadout Loadout => _sim.Loadout;
        public UtilityBelt Utilities => _sim.Belt;
        public ClientSession Session => _session;

        /// <summary>The weapon in hand.</summary>
        public WeaponSimulator ActiveWeapon => _sim.ActiveWeapon;
        public WeaponBuild ActiveBuild => _sim.ActiveBuild;
        public int SlotCount => _sim.SlotCount;

        /// <summary>Playing on a server, as opposed to the firing range.</summary>
        public bool Online => _session != null;

        /// <summary>In the world and able to act. Always true offline.</summary>
        public bool Alive => _session == null || _session.LocalAlive;

        /// <summary>How far the current frame sits between the last two ticks, 0..1.</summary>
        public float TickAlpha { get; private set; }

        /// <summary>False while a menu is open: the player keeps simulating but receives no input.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>Reloads never drain the reserve. Used by the firing range.</summary>
        public bool InfiniteReserve
        {
            get => _sim.InfiniteReserve;
            set => _sim.InfiniteReserve = value;
        }

        /// <summary>Used utility items come back over time. On in the firing range.</summary>
        public bool RechargeUtilities
        {
            get => _sim.RechargeUtilities;
            set => _sim.RechargeUtilities = value;
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
        public event Action Jumped;
        /// <summary>Argument: how fast the player was falling.</summary>
        public event Action<float> Landed;
        public event Action SlideStarted;
        public event Action MantleStarted;

        /// <param name="session">The connection to play through, or null to play offline.</param>
        public void Initialize(MovementConfig movement, Loadout loadout, ProjectileSystem projectiles,
            FirstPersonCamera playerCamera, WeaponViewModel viewModel, float yaw, ClientSession session = null)
        {
            _camera = playerCamera;
            _viewModel = viewModel;
            _projectiles = projectiles;
            _session = session;
            _spawnPosition = transform.position;
            _spawnYaw = yaw;

            _input = new PlayerInputReader();
            _sim = new PlayerSimulation(GetComponent<CharacterController>(), movement, seed: 0x5EED0000u);

            // The simulation's events carry slot numbers; listeners want the things in those slots.
            _sim.WeaponFired += slot => WeaponFired?.Invoke(_sim.Loadout.Weapons[slot]);
            _sim.ReloadStarted += () => ReloadStarted?.Invoke();
            _sim.RoundInserted += () => RoundInserted?.Invoke();
            _sim.ReloadFinished += () => ReloadFinished?.Invoke();
            _sim.DryFired += () => DryFired?.Invoke();
            _sim.ThrowStarted += slot => UtilityThrowStarted?.Invoke(_sim.Loadout.Utilities[slot]);
            _sim.ActiveChanged += _ => OnActiveWeaponChanged();
            _sim.Jumped += () => Jumped?.Invoke();
            _sim.Landed += impact => Landed?.Invoke(impact);
            _sim.SlideStarted += () => SlideStarted?.Invoke();
            _sim.MantleStarted += () => MantleStarted?.Invoke();
            _sim.LoadoutApplied += () =>
            {
                _previousRecoil = _currentRecoil = Vector2.zero;
                LoadoutChanged?.Invoke();
                OnActiveWeaponChanged();
            };

            if (_session == null)
            {
                _projectiles.HitConfirmed += report => HitConfirmed?.Invoke(report);
                _projectiles.Happened += OnProjectileEvent;
            }
            else
            {
                // Online, projectiles here are pictures of the ones the server is flying.
                _projectiles.VisualOnly = true;
                _session.Attach(_sim);
                _session.PlayerSpawned += OnPlayerSpawned;
                _session.Corrected += OnCorrected;
                _session.HitConfirmed += OnServerHit;
            }

            SetLoadout(loadout, 0);
            Respawn();
        }

        // ------------------------------------------------------------------ loadout

        public WeaponBuild GetBuild(int slot) => _sim.Loadout.Weapons[slot];

        /// <summary>
        /// Replaces everything the player carries. Weapons and utility start full.
        /// Online the server does this when it places the player; do not call it directly.
        /// </summary>
        public void SetLoadout(Loadout loadout, int activeSlot) => _sim.SetLoadout(loadout, activeSlot);

        void OnActiveWeaponChanged()
        {
            _viewModel.Show(ActiveBuild);
            ActiveWeaponChanged?.Invoke(ActiveBuild);
        }

        public void Respawn()
        {
            _input.SetView(_spawnYaw, 0f);
            _sim.Motor.Teleport(_spawnPosition, _spawnYaw, 0f);
            _previousMotor = _currentMotor = _sim.Motor.State;
            _viewError = Vector3.zero;
            _accumulator = 0f;
        }

        void OnDestroy()
        {
            _input?.Dispose();
            if (_session == null) return;
            _session.PlayerSpawned -= OnPlayerSpawned;
            _session.Corrected -= OnCorrected;
            _session.HitConfirmed -= OnServerHit;
        }

        // ------------------------------------------------------------------ online

        /// <summary>The server placed someone. If it was us, start looking the way it faced us.</summary>
        void OnPlayerSpawned(ClientPlayer player, SpawnMessage spawn)
        {
            if (player.Id != _session.PlayerId) return;
            _input.SetView(spawn.Yaw, 0f);
            _previousMotor = _currentMotor = _sim.Motor.State;
            _previousRecoil = _currentRecoil = Vector2.zero;
            _viewError = Vector3.zero;
        }

        /// <summary>The server disagreed with the prediction and the simulation has been put right.</summary>
        void OnCorrected(Vector3 moved)
        {
            _currentMotor = _sim.Motor.State;
            _currentRecoil = _sim.ActiveWeapon.State.RecoilCurrent;
            _previousMotor.Position -= moved;

            // Keep drawing the player where they were and let the difference drain away, unless
            // it is so large that sliding there would look worse than arriving.
            _viewError += moved;
            if (_viewError.sqrMagnitude > SnapDistance * SnapDistance)
            {
                _viewError = Vector3.zero;
                _previousMotor = _currentMotor;
            }
        }

        void OnServerHit(HitMessage hit)
        {
            HitConfirmed?.Invoke(new HitReport
            {
                Tick = _tick,
                Point = hit.Point,
                Damage = hit.Damage,
                Zone = hit.Zone,
                Killed = hit.Killed,
                Distance = Vector3.Distance(_currentMotor.EyePosition, hit.Point),
            });
            ShotLanded?.Invoke();
        }

        // ------------------------------------------------------------------ simulation

        void Update()
        {
            if (_sim == null) return;

            // Everything the server sent since last frame: corrections, spawns, hits.
            double now = Time.realtimeSinceStartupAsDouble;
            if (_session != null) _session.Update(now);

            if (InputEnabled && Alive) _input.SampleFrame(LookScale());

            // Online the clock must not stop for anything: the server is not waiting for us.
            float frame = _session != null ? Time.unscaledDeltaTime : Time.deltaTime;
            _accumulator += Mathf.Min(frame, 0.25f);
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

            if (_session != null) _session.Flush(now);
            else if (_currentMotor.Position.y < KillPlaneY) Respawn();

            _viewError *= Mathf.Exp(-CorrectionEase * frame);
            TickAlpha = Mathf.Clamp01(_accumulator / SimConstants.TickDelta);
            Present(TickAlpha);
        }

        /// <summary>One fixed simulation tick.</summary>
        void Step()
        {
            const float dt = SimConstants.TickDelta;
            WeaponInventory inventory = _sim.Inventory;
            PlayerInputCmd cmd = InputEnabled && Alive
                ? _input.BuildCmd(_tick, inventory.TargetIndex, inventory.SlotCount)
                : _input.BuildIdleCmd(_tick);

            if (_session != null)
            {
                if (!_session.LocalAlive)
                {
                    // Out of the round. Nothing of ours to simulate, but the world carries on.
                    _projectiles.Step(_tick++, dt);
                    return;
                }
                cmd = _session.Prepare(cmd);
            }

            _previousMotor = _currentMotor;
            _previousRecoil = _currentRecoil;

            _sim.Step(cmd, dt);
            _currentMotor = _sim.Motor.State;
            _currentRecoil = _sim.ActiveWeapon.State.RecoilCurrent;

            // Shots belong to the weapon that was simulated, which is not necessarily the one
            // now in hand if a switch completed this tick.
            WeaponSimulator fired = _sim.SteppedWeapon;
            int thrown = _sim.Belt.ReleasedSlot;

            if (_session != null)
            {
                if (fired.Shots.Count > 0) DrawPredictedShots(cmd, fired, _sim.SteppedBuild);
                if (thrown >= 0) UtilityThrown?.Invoke(_sim.Loadout.Utilities[thrown]);
                _session.Commit(cmd);
            }
            else
            {
                // Targets move in Update; make sure the physics scene has their latest poses.
                if (fired.Shots.Count > 0 || _projectiles.Live.Count > 0 || thrown >= 0) Physics.SyncTransforms();
                if (fired.Shots.Count > 0) ResolveShots(cmd, fired, _sim.SteppedBuild);
                if (thrown >= 0) ThrowUtility(cmd, _sim.Loadout.Utilities[thrown]);
            }

            _projectiles.Step(_tick, dt);
            _tick++;
        }

        /// <summary>
        /// Online: draws where our bullets went, straight away. Whether they hurt anyone is
        /// for the server to say; projectiles appear when the server launches them.
        /// </summary>
        void DrawPredictedShots(in PlayerInputCmd cmd, WeaponSimulator weapon, WeaponBuild build)
        {
            Vector3 origin = _currentMotor.EyePosition;
            bool synced = false;

            for (int i = 0; i < weapon.Shots.Count; i++)
            {
                ShotEvent shot = weapon.Shots[i];
                if (shot.Kind != ShotKind.Bullet) continue;

                if (!synced)
                {
                    Physics.SyncTransforms();
                    synced = true;
                }
                Vector3 direction = AimMath.Direction(cmd.Yaw + shot.AimOffset.x, cmd.Pitch - shot.AimOffset.y);
                HitscanHit end = Hitscan.Trace(origin, direction, weapon.Stats.maxRange);
                ShotFired?.Invoke(new ShotPresentation
                {
                    Origin = origin,
                    End = end.Point,
                    Normal = end.Normal,
                    HitWorld = end.HasHit && end.Hitbox == null,
                    HitTarget = false,
                    Build = build,
                });
            }
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
            WeaponSimulator inHand = _sim.ActiveWeapon;
            WeaponState weapon = inHand.State;
            WeaponStats stats = inHand.Stats;

            // Position is blended between ticks; the view angles are not, they come straight
            // from this frame's mouse input so aiming has no added latency.
            float yaw = _input.Yaw, pitch = _input.Pitch;
            Vector2 recoil = Vector2.Lerp(_previousRecoil, _currentRecoil, alpha);
            Vector3 feet = Vector3.Lerp(a.Position, b.Position, alpha) + _viewError;
            Vector3 eye = feet + Vector3.up * Mathf.Lerp(a.EyeHeight, b.EyeHeight, alpha);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            float viewYaw = yaw + recoil.x, viewPitch = pitch - recoil.y;
            _camera.Apply(new CameraPose
            {
                EyePosition = eye,
                Yaw = viewYaw,
                Pitch = viewPitch,
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
                Lowered = b.Mantling || _sim.Belt.IsThrowing || weapon.Phase == WeaponPhase.Holstering,
                Reloading = weapon.Phase == WeaponPhase.Reloading,
                ReloadProgress = weapon.ReloadProgress,
                Charge = weapon.ChargeAmount,
                // A full scope view replaces the weapon model while it is up.
                // And there is nothing to hold while out of the round.
                Hidden = !Alive || (!swapping && ActiveBuild.Reticle == ReticleStyle.Scope && weapon.AdsAmount > 0.9f),
                MoveAmount = b.Grounded ? Mathf.Clamp01(b.HorizontalSpeed / _sim.Motor.Config.sprintSpeed) : 0f,
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
            WeaponSimulator inHand = _sim.ActiveWeapon;
            float adsScale = GameSettings.AdsSensitivityMultiplier / Mathf.Max(inHand.Stats.adsZoom, 1f);
            return GameSettings.Sensitivity * Mathf.Lerp(1f, adsScale, inHand.State.AdsAmount);
        }
    }
}
