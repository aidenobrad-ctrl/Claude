using System;
using UnityEngine;
using Pulsebreak.Client.Controls;
using Pulsebreak.Client.Settings;
using Pulsebreak.Client.Weapons;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Player
{
    /// <summary>
    /// Drives the local player. Each rendered frame it:
    ///   1. samples input,
    ///   2. runs as many fixed simulation ticks as have come due (movement, then weapon, then shots),
    ///   3. presents the result, blending between the last two ticks so motion is smooth at any frame rate.
    ///
    /// In Phase 1 this object is also the authority: it applies damage itself. Once the game
    /// is networked, step 2 is what moves into the netcode's tick callback, and damage is
    /// applied by the server instead.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class LocalPlayerController : MonoBehaviour
    {
        const float KillPlaneY = -30f;

        PlayerInputReader _input;
        PlayerMotor _motor;
        WeaponSimulator _weapon;
        WeaponDefinition _weaponDefinition;
        FirstPersonCamera _camera;
        WeaponViewModel _viewModel;

        int _tick;
        float _accumulator;
        Vector3 _spawnPosition;
        float _spawnYaw;

        // The last two simulated states, blended for presentation.
        MotorState _previousMotor, _currentMotor;
        Vector2 _previousRecoil, _currentRecoil;

        public PlayerMotor Motor => _motor;
        public WeaponSimulator Weapon => _weapon;
        public MotorState MotorState => _currentMotor;

        /// <summary>False while a menu is open: the player keeps simulating but receives no input.</summary>
        public bool InputEnabled { get; set; } = true;

        public event Action<ShotPresentation> ShotFired;
        public event Action<HitFeedback> HitConfirmed;

        public void Initialize(MovementConfig movement, WeaponDefinition weapon,
            FirstPersonCamera playerCamera, WeaponViewModel viewModel, float yaw)
        {
            _weaponDefinition = weapon;
            _camera = playerCamera;
            _viewModel = viewModel;
            _spawnPosition = transform.position;
            _spawnYaw = yaw;

            _input = new PlayerInputReader();
            _motor = new PlayerMotor(GetComponent<CharacterController>(), movement);
            _weapon = new WeaponSimulator(weapon, seed: 0x5EEDu);

            Respawn();
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

            Present(Mathf.Clamp01(_accumulator / SimConstants.TickDelta));
        }

        /// <summary>One fixed simulation tick.</summary>
        void Step()
        {
            const float dt = SimConstants.TickDelta;
            PlayerInputCmd cmd = InputEnabled ? _input.BuildCmd(_tick) : _input.BuildIdleCmd(_tick);

            _previousMotor = _currentMotor;
            _previousRecoil = _currentRecoil;

            var modifiers = new MoveModifiers
            {
                SpeedMultiplier = Mathf.Lerp(
                    _weaponDefinition.hipMoveMultiplier, _weaponDefinition.adsMoveMultiplier, _weapon.State.AdsAmount),
            };
            _motor.Simulate(cmd, modifiers, dt);
            _currentMotor = _motor.State;

            var context = new WeaponContext
            {
                CanUseWeapon = !_currentMotor.Mantling,
                Sprinting = _currentMotor.Sprinting,
                TimeSinceSprint = _currentMotor.TimeSinceSprint,
                HorizontalSpeed = _currentMotor.HorizontalSpeed,
                Grounded = _currentMotor.Grounded,
                Crouched = _currentMotor.Stance != Stance.Standing,
            };
            _weapon.Simulate(cmd, context, dt);
            _currentRecoil = _weapon.State.RecoilCurrent;

            if (_weapon.Shots.Count > 0)
            {
                // Targets move in Update; make sure the physics scene has their latest poses.
                Physics.SyncTransforms();
                for (int i = 0; i < _weapon.Shots.Count; i++) ResolveShot(cmd, _weapon.Shots[i]);
            }

            _tick++;
        }

        void ResolveShot(in PlayerInputCmd cmd, in ShotEvent shot)
        {
            Vector3 origin = _currentMotor.EyePosition;
            Vector3 direction = AimMath.Direction(cmd.Yaw + shot.AimOffset.x, cmd.Pitch - shot.AimOffset.y);
            HitscanHit hit = Hitscan.Trace(origin, direction, _weaponDefinition.maxRange);

            bool hitTarget = false;
            if (hit.Hitbox != null)
            {
                IDamageable target = hit.Hitbox.Owner;
                if (target != null && target.IsAlive)
                {
                    HitZone zone = hit.Hitbox.zone;
                    var info = new DamageInfo
                    {
                        Amount = DamageCalculator.Compute(_weaponDefinition, hit.Distance, zone),
                        Zone = zone,
                        Point = hit.Point,
                        Direction = direction,
                        Distance = hit.Distance,
                        WeaponId = _weaponDefinition.weaponId,
                    };
                    DamageResult result = target.ApplyDamage(info);
                    hitTarget = true;

                    HitConfirmed?.Invoke(new HitFeedback
                    {
                        Tick = cmd.Tick,
                        Point = hit.Point,
                        Damage = result.Applied,
                        Zone = zone,
                        Killed = result.Killed,
                        Distance = hit.Distance,
                        Target = target,
                    });
                }
            }

            ShotFired?.Invoke(new ShotPresentation
            {
                Origin = origin,
                End = hit.Point,
                Normal = hit.Normal,
                HitWorld = hit.HasHit && hit.Hitbox == null,
                HitTarget = hitTarget,
            });
        }

        /// <summary>Renders the state between the last two ticks. <paramref name="alpha"/> is 0..1 between them.</summary>
        void Present(float alpha)
        {
            MotorState a = _previousMotor, b = _currentMotor;
            WeaponState weapon = _weapon.State;

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
                AdsZoom = _weaponDefinition.adsZoom,
                Sprinting = b.Sprinting,
                Sliding = b.Stance == Stance.Sliding,
            }, Time.deltaTime);

            _viewModel.Tick(new ViewModelInput
            {
                AdsAmount = weapon.AdsAmount,
                Sprinting = b.Sprinting,
                Lowered = b.Mantling,
                Reloading = weapon.Phase == WeaponPhase.Reloading,
                ReloadProgress = weapon.ReloadProgress,
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
            float adsScale = GameSettings.AdsSensitivityMultiplier / Mathf.Max(_weaponDefinition.adsZoom, 1f);
            return GameSettings.Sensitivity * Mathf.Lerp(1f, adsScale, _weapon.State.AdsAmount);
        }
    }
}
