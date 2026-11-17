using System;
using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Movement
{
    /// <summary>
    /// Kinematic first-person character motor: run, sprint, crouch, slide, jump, mantle and lean.
    ///
    /// It advances one fixed tick at a time from a <see cref="PlayerInputCmd"/> and keeps all of
    /// its state in a <see cref="MotorState"/>. Nothing here reads input devices, the clock or
    /// the camera, so the same class can run on the server and be re-simulated on the client.
    /// </summary>
    public sealed class PlayerMotor
    {
        const float SkinWidth = 0.035f;
        const float GroundSnapExtra = 0.1f;

        readonly CharacterController _controller;
        readonly Transform _transform;
        readonly MovementConfig _config;
        readonly int _worldMask;
        MotorState _state;

        /// <summary>Presentation hooks (camera, audio). Never drive gameplay from these.</summary>
        public event Action Jumped;
        /// <summary>Argument is the downward speed at touchdown, in m/s.</summary>
        public event Action<float> Landed;
        public event Action SlideStarted;
        public event Action MantleStarted;

        public MotorState State => _state;
        public MovementConfig Config => _config;

        public PlayerMotor(CharacterController controller, MovementConfig config, int worldMask = GameLayers.WorldMask)
        {
            _controller = controller;
            _transform = controller.transform;
            _config = config;
            _worldMask = worldMask;

            _controller.radius = config.radius;
            _controller.stepOffset = config.stepOffset;
            _controller.slopeLimit = config.slopeLimit;
            _controller.skinWidth = SkinWidth;
            _controller.minMoveDistance = 0f;

            Teleport(_transform.position, 0f, 0f);
        }

        /// <summary>Places the character and clears all momentum and timers.</summary>
        public void Teleport(Vector3 position, float yaw, float pitch)
        {
            _state = default;
            _state.Yaw = yaw;
            _state.Pitch = pitch;
            _state.Stance = Stance.Standing;
            _state.EyeHeight = _config.standEyeHeight;
            _state.GroundNormal = Vector3.up;
            _state.TimeSinceSprint = 10f;
            ApplyCapsule();
            MoveTransform(position);
            _state.Position = position;
        }

        /// <summary>
        /// Puts the character into a saved state, body and all. This is what prediction uses
        /// to go back to what the server said and replay from there.
        /// </summary>
        public void SetState(in MotorState state)
        {
            _state = state;
            ApplyCapsule();
            MoveTransform(state.Position);
        }

        /// <summary>Advances the character by one tick.</summary>
        public void Simulate(in PlayerInputCmd cmd, in MoveModifiers modifiers, float dt)
        {
            InputButtons pressed = cmd.Buttons & ~_state.PrevButtons;
            _state.PrevButtons = cmd.Buttons;
            _state.Yaw = cmd.Yaw;
            _state.Pitch = cmd.Pitch;

            _state.CoyoteTimer = Mathf.Max(0f, _state.CoyoteTimer - dt);
            _state.JumpBufferTimer = Mathf.Max(0f, _state.JumpBufferTimer - dt);
            _state.SlideCooldown = Mathf.Max(0f, _state.SlideCooldown - dt);
            _state.MantleCooldown = Mathf.Max(0f, _state.MantleCooldown - dt);
            if ((pressed & InputButtons.Jump) != 0) _state.JumpBufferTimer = _config.jumpBufferTime;

            Vector3 forward = AimMath.YawForward(cmd.Yaw);
            Vector3 right = AimMath.YawRight(cmd.Yaw);

            if (_state.Mantling)
                SimulateMantle(forward, dt);
            else if (!TryBeginMantle(cmd, forward))
                SimulateLocomotion(cmd, pressed, modifiers, forward, right, dt);

            _state.EyeHeight = Mathf.MoveTowards(
                _state.EyeHeight, _config.EyeHeightFor(_state.Stance), _config.eyeHeightSpeed * dt);
            _state.Position = _transform.position;
        }

        // ------------------------------------------------------------------ locomotion

        void SimulateLocomotion(in PlayerInputCmd cmd, InputButtons pressed, in MoveModifiers modifiers,
            Vector3 forward, Vector3 right, float dt)
        {
            Vector2 move = Vector2.ClampMagnitude(cmd.Move, 1f);
            Vector3 wishDir = forward * move.y + right * move.x;
            float wishScale = wishDir.magnitude;
            wishDir = wishScale > 0.0001f ? wishDir / wishScale : Vector3.zero;

            Vector3 horizontal = new Vector3(_state.Velocity.x, 0f, _state.Velocity.z);
            float vertical = _state.Velocity.y;

            UpdateStance(cmd, pressed, move, ref horizontal, dt);

            float multiplier = modifiers.SpeedMultiplier > 0f ? modifiers.SpeedMultiplier : 1f;
            float wishSpeed = BaseSpeed() * multiplier * wishScale;

            if (_state.Grounded)
            {
                if (_state.Stance == Stance.Sliding)
                {
                    horizontal = SlideVelocity(horizontal, forward, move.x, dt);
                }
                else
                {
                    // Friction runs on every grounded tick, including the one a jump leaves on.
                    // That is what stops slide speed being carried forever by hopping.
                    horizontal = MoveMath.ApplyFriction(horizontal, _config.groundFriction, _config.stopSpeed, dt);
                    horizontal = MoveMath.Accelerate(horizontal, wishDir, wishSpeed, _config.groundAcceleration, dt);
                }
            }
            else
            {
                horizontal = MoveMath.Accelerate(horizontal, wishDir, wishSpeed, _config.airAcceleration, dt);
            }

            bool jumped = false;
            if (_state.JumpBufferTimer > 0f && (_state.Grounded || _state.CoyoteTimer > 0f) && TryStandForJump())
            {
                vertical = _config.JumpSpeed;
                jumped = true;
                _state.JumpBufferTimer = 0f;
                _state.CoyoteTimer = 0f;
            }

            // Vertical motion. On the ground a constant downward push keeps the capsule
            // pressed onto slopes and steps. In the air (including the tick a jump starts)
            // gravity is integrated with the average of the start and end velocity, which
            // makes the jump reach exactly the configured height at any tick rate.
            float verticalMove;
            if (_state.Grounded && !jumped)
            {
                vertical = -_config.groundStickSpeed;
                verticalMove = vertical;
            }
            else
            {
                float next = Mathf.Max(vertical - _config.gravity * dt, -_config.terminalVelocity);
                verticalMove = (vertical + next) * 0.5f;
                vertical = next;
            }

            Vector3 before = _transform.position;
            CollisionFlags flags = _controller.Move((horizontal + Vector3.up * verticalMove) * dt);
            Vector3 moved = _transform.position - before;

            // The controller slides along walls but does not tell us; read the real result back
            // so speed is lost against a wall instead of being stored up behind it.
            Vector3 actual = new Vector3(moved.x, 0f, moved.z) / dt;
            if (actual.sqrMagnitude < horizontal.sqrMagnitude) horizontal = actual;
            if ((flags & CollisionFlags.Above) != 0 && vertical > 0f) vertical = 0f;

            bool wasGrounded = _state.Grounded;
            bool grounded = false;
            Vector3 normal = Vector3.up;
            if (!jumped && vertical <= 0f)
            {
                if ((flags & CollisionFlags.Below) != 0) grounded = IsWalkableBelow(out normal);
                else if (wasGrounded) grounded = SnapToGround(out normal);
            }

            if (grounded && !wasGrounded)
            {
                _state.JustLanded = true;
                Landed?.Invoke(-vertical);
            }
            else
            {
                _state.JustLanded = false;
            }

            if (grounded)
            {
                _state.CoyoteTimer = _config.coyoteTime;
                vertical = 0f;
            }

            _state.Grounded = grounded;
            _state.GroundNormal = normal;
            _state.Velocity = horizontal + Vector3.up * vertical;
            if (jumped) Jumped?.Invoke();
        }

        void UpdateStance(in PlayerInputCmd cmd, InputButtons pressed, Vector2 move, ref Vector3 horizontal, float dt)
        {
            bool crouchHeld = cmd.Has(InputButtons.Crouch);
            bool sprintHeld = cmd.Has(InputButtons.Sprint);
            bool crouchPressed = (pressed & InputButtons.Crouch) != 0;
            bool wasSprinting = _state.Sprinting;
            float speed = horizontal.magnitude;

            if (_state.Stance == Stance.Sliding)
            {
                bool spent = speed < _config.slideEndSpeed
                             || _state.SlideTimer >= _config.slideMaxDuration
                             || !_state.Grounded;
                bool released = !crouchHeld && _state.SlideTimer >= _config.slideMinDuration;
                if (spent || released) EndSlide(crouchHeld);
            }
            else
            {
                // Two ways into a slide: tap crouch while sprinting, or land with crouch and
                // sprint already held.
                bool wantsSlide = (crouchPressed && wasSprinting)
                                  || (_state.JustLanded && crouchHeld && sprintHeld);
                bool canSlide = _state.Grounded
                                && _state.SlideCooldown <= 0f
                                && move.y > 0.1f
                                && speed >= _config.slideMinEntrySpeed;

                if (wantsSlide && canSlide)
                {
                    horizontal = horizontal / speed * Mathf.Max(speed, _config.slideBoostSpeed);
                    _state.SlideTimer = 0f;
                    SetStance(Stance.Sliding);
                    SlideStarted?.Invoke();
                }
                else if (crouchHeld)
                {
                    SetStance(Stance.Crouching);
                }
                else if (_state.Stance == Stance.Crouching && HasHeadroom())
                {
                    SetStance(Stance.Standing);
                }
            }

            // Shooting and aiming both drop you out of a sprint.
            _state.Sprinting = _state.Stance == Stance.Standing
                               && sprintHeld
                               && move.y > 0.1f
                               && !cmd.Has(InputButtons.Fire)
                               && !cmd.Has(InputButtons.Ads);
            _state.TimeSinceSprint = _state.Sprinting ? 0f : _state.TimeSinceSprint + dt;
        }

        Vector3 SlideVelocity(Vector3 horizontal, Vector3 forward, float strafe, float dt)
        {
            _state.SlideTimer += dt;

            float speed = horizontal.magnitude;
            Vector3 direction = speed > 0.01f ? horizontal / speed : forward;
            direction = MoveMath.RotateYaw(direction, strafe * _config.slideSteerDegPerSec * dt);
            speed = Mathf.Max(0f, speed - _config.slideFriction * dt);

            Vector3 velocity = direction * speed;

            // Gravity along the slope: sliding downhill speeds you up, uphill slows you down.
            Vector3 n = _state.GroundNormal;
            if (_config.slideSlopeFactor > 0f && n.y < 0.999f)
                velocity += new Vector3(n.x, 0f, n.z) * (_config.gravity * n.y * _config.slideSlopeFactor * dt);

            return Vector3.ClampMagnitude(velocity, _config.slideMaxSpeed);
        }

        void EndSlide(bool crouchHeld)
        {
            _state.SlideCooldown = _config.slideCooldown;
            SetStance(crouchHeld || !HasHeadroom() ? Stance.Crouching : Stance.Standing);
        }

        /// <summary>A jump always leaves from a standing capsule; fails under a low ceiling.</summary>
        bool TryStandForJump()
        {
            if (_state.Stance == Stance.Standing) return true;
            if (!HasHeadroom()) return false;
            if (_state.Stance == Stance.Sliding) _state.SlideCooldown = _config.slideCooldown;
            SetStance(Stance.Standing);
            return true;
        }

        float BaseSpeed()
        {
            if (_state.Stance == Stance.Crouching) return _config.crouchSpeed;
            return _state.Sprinting ? _config.sprintSpeed : _config.runSpeed;
        }

        // ------------------------------------------------------------------ ground

        bool IsWalkableBelow(out Vector3 normal)
        {
            normal = Vector3.up;
            Vector3 origin = _transform.position + Vector3.up * 0.3f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.6f, _worldMask, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                // Too steep to stand on: stay airborne and let gravity slide us off.
                if (Vector3.Angle(normal, Vector3.up) > _config.slopeLimit + 1f) return false;
            }
            return true;
        }

        /// <summary>
        /// Keeps the capsule on the ground when running down steps and slopes, where one tick
        /// of movement would otherwise leave it hanging in the air for a frame.
        /// </summary>
        bool SnapToGround(out Vector3 normal)
        {
            normal = Vector3.up;
            float radius = _controller.radius * 0.95f;
            const float lift = 0.05f;
            Vector3 origin = _transform.position + Vector3.up * (radius + lift);
            float reach = lift + _config.stepOffset + GroundSnapExtra;

            if (!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, reach, _worldMask, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y < 0.5f) return false;

            float drop = hit.distance - lift;
            if (drop > 0.001f) _controller.Move(Vector3.down * (drop + SkinWidth));
            return IsWalkableBelow(out normal);
        }

        bool HasHeadroom() => CapsuleFits(_transform.position, _config.standHeight);

        bool CapsuleFits(Vector3 feet, float height)
        {
            float radius = _config.radius - 0.02f;
            Vector3 bottom = feet + Vector3.up * (radius + 0.05f);
            Vector3 top = feet + Vector3.up * (height - radius);
            return !Physics.CheckCapsule(bottom, top, radius, _worldMask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ mantle

        bool TryBeginMantle(in PlayerInputCmd cmd, Vector3 forward)
        {
            if (!cmd.Has(InputButtons.Jump) || cmd.Move.y < 0.3f || _state.MantleCooldown > 0f) return false;

            // On the ground only a fresh jump press vaults. In the air, holding jump and
            // forward is enough, so you grab a ledge as soon as you reach it.
            if (_state.Grounded && _state.JumpBufferTimer <= 0f) return false;

            if (!FindLedge(forward, out Vector3 target, out float height, out bool crouchOnly)) return false;

            _state.Mantling = true;
            _state.MantleTimer = 0f;
            _state.MantleFrom = _transform.position;
            _state.MantleTo = target;
            _state.MantleDuration = Mathf.Lerp(
                _config.mantleMinDuration, _config.mantleMaxDuration,
                Mathf.InverseLerp(_config.mantleMinHeight, _config.mantleMaxHeight, height));

            _state.Velocity = Vector3.zero;
            _state.Grounded = false;
            _state.Sprinting = false;
            _state.JustLanded = false;
            _state.JumpBufferTimer = 0f;
            _state.CoyoteTimer = 0f;
            SetStance(crouchOnly ? Stance.Crouching : Stance.Standing);
            MantleStarted?.Invoke();
            return true;
        }

        bool FindLedge(Vector3 forward, out Vector3 target, out float height, out bool crouchOnly)
        {
            target = default;
            height = 0f;
            crouchOnly = false;

            Vector3 feet = _transform.position;
            float probeHeight = _config.mantleMaxHeight + 0.1f;
            float castLength = probeHeight - _config.mantleMinHeight;

            // Look down onto the ledge from above. The near probe catches thin walls that the
            // far one overshoots.
            float far = _config.radius + _config.mantleReach;
            float near = _config.radius + _config.mantleReach * 0.4f;
            float distance = far;
            if (!ProbeLedge(feet, forward, far, probeHeight, castLength, out RaycastHit top))
            {
                distance = near;
                if (!ProbeLedge(feet, forward, near, probeHeight, castLength, out top)) return false;
            }

            height = top.point.y - feet.y;

            // A walkable ramp also puts a surface above the feet a step ahead. Tell the two
            // apart by what faces us halfway up: a wall is a ledge, a slope is just a slope.
            Vector3 faceOrigin = feet + Vector3.up * (height * 0.5f);
            if (Physics.Raycast(faceOrigin, forward, out RaycastHit face, distance, _worldMask, QueryTriggerInteraction.Ignore)
                && face.normal.y > 0.5f)
                return false;

            target = top.point + Vector3.up * 0.03f;
            if (CapsuleFits(target, _config.standHeight)) crouchOnly = false;
            else if (CapsuleFits(target, _config.crouchHeight)) crouchOnly = true;
            else return false;

            // There must also be room to rise straight up to the ledge from where we are.
            float topHeight = crouchOnly ? _config.crouchHeight : _config.standHeight;
            return CapsuleFits(new Vector3(feet.x, target.y, feet.z), topHeight);
        }

        bool ProbeLedge(Vector3 feet, Vector3 forward, float distance, float probeHeight, float castLength, out RaycastHit hit)
        {
            Vector3 origin = feet + forward * distance + Vector3.up * probeHeight;
            return Physics.Raycast(origin, Vector3.down, out hit, castLength, _worldMask, QueryTriggerInteraction.Ignore)
                   && hit.normal.y >= 0.7f;
        }

        void SimulateMantle(Vector3 forward, float dt)
        {
            _state.MantleTimer += dt;
            float t = Mathf.Clamp01(_state.MantleTimer / Mathf.Max(_state.MantleDuration, 0.01f));

            // Up first, then over the edge.
            float rise = Smooth(Mathf.Clamp01(t / 0.65f));
            float advance = Smooth(Mathf.Clamp01((t - 0.4f) / 0.6f));
            Vector3 from = _state.MantleFrom, to = _state.MantleTo;
            MoveTransform(new Vector3(
                Mathf.Lerp(from.x, to.x, advance),
                Mathf.Lerp(from.y, to.y, rise),
                Mathf.Lerp(from.z, to.z, advance)));

            if (t < 1f) return;

            _state.Mantling = false;
            _state.Grounded = true;
            _state.GroundNormal = Vector3.up;
            _state.Velocity = forward * _config.mantleExitSpeed;
            _state.MantleCooldown = _config.mantleCooldown;
            _state.CoyoteTimer = _config.coyoteTime;
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        // ------------------------------------------------------------------ helpers

        void SetStance(Stance stance)
        {
            if (_state.Stance == stance) return;
            _state.Stance = stance;
            ApplyCapsule();
        }

        void ApplyCapsule()
        {
            float height = _config.CapsuleHeightFor(_state.Stance);
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>A CharacterController ignores direct transform writes unless it is disabled first.</summary>
        void MoveTransform(Vector3 position)
        {
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            _transform.position = position;
            _controller.enabled = wasEnabled;
        }
    }
}
