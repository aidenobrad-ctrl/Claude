using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Client.Controls
{
    /// <summary>
    /// Turns keyboard and mouse into <see cref="PlayerInputCmd"/>s. This is the only class
    /// that talks to the Input System, so rebinding and gamepad support have one home.
    ///
    /// Looking is sampled every rendered frame so aiming is as smooth as the display allows;
    /// buttons are latched so a tap between two simulation ticks is never lost.
    /// </summary>
    public sealed class PlayerInputReader : IDisposable
    {
        /// <summary>Degrees of turn per mouse count at sensitivity 1. Same convention as Source-engine games.</summary>
        public const float DegreesPerCount = 0.022f;
        const float MaxPitch = 89f;

        readonly InputActionMap _map;
        readonly InputAction _move, _look;
        readonly InputAction _jump, _sprint, _crouch, _fire, _ads, _reload, _leanLeft, _leanRight;
        InputButtons _latched;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }

        public PlayerInputReader()
        {
            _map = new InputActionMap("Gameplay");

            _move = _map.AddAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _look = _map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");

            _jump = AddButton("Jump", "<Keyboard>/space");
            _sprint = AddButton("Sprint", "<Keyboard>/leftShift");
            _crouch = AddButton("Crouch", "<Keyboard>/leftCtrl", "<Keyboard>/c");
            _fire = AddButton("Fire", "<Mouse>/leftButton");
            _ads = AddButton("Aim", "<Mouse>/rightButton");
            _reload = AddButton("Reload", "<Keyboard>/r");
            _leanLeft = AddButton("LeanLeft", "<Keyboard>/q");
            _leanRight = AddButton("LeanRight", "<Keyboard>/e");

            _map.Enable();
        }

        InputAction AddButton(string name, params string[] bindings)
        {
            InputAction action = _map.AddAction(name, InputActionType.Button);
            foreach (string binding in bindings) action.AddBinding(binding);
            return action;
        }

        public void SetView(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = Mathf.Clamp(pitch, -MaxPitch, MaxPitch);
        }

        /// <summary>
        /// Call once per rendered frame. <paramref name="lookScale"/> is the final sensitivity
        /// (hip sensitivity, scaled down while aiming).
        /// </summary>
        public void SampleFrame(float lookScale)
        {
            Vector2 delta = _look.ReadValue<Vector2>() * (DegreesPerCount * lookScale);
            Yaw = Mathf.Repeat(Yaw + delta.x + 180f, 360f) - 180f;
            Pitch = Mathf.Clamp(Pitch - delta.y, -MaxPitch, MaxPitch);

            Latch(_jump, InputButtons.Jump);
            Latch(_sprint, InputButtons.Sprint);
            Latch(_crouch, InputButtons.Crouch);
            Latch(_fire, InputButtons.Fire);
            Latch(_ads, InputButtons.Ads);
            Latch(_reload, InputButtons.Reload);
            Latch(_leanLeft, InputButtons.LeanLeft);
            Latch(_leanRight, InputButtons.LeanRight);
        }

        void Latch(InputAction action, InputButtons button)
        {
            if (action.WasPressedThisFrame()) _latched |= button;
        }

        /// <summary>Call once per simulation tick.</summary>
        public PlayerInputCmd BuildCmd(int tick)
        {
            InputButtons held = InputButtons.None;
            if (_jump.IsPressed()) held |= InputButtons.Jump;
            if (_sprint.IsPressed()) held |= InputButtons.Sprint;
            if (_crouch.IsPressed()) held |= InputButtons.Crouch;
            if (_fire.IsPressed()) held |= InputButtons.Fire;
            if (_ads.IsPressed()) held |= InputButtons.Ads;
            if (_reload.IsPressed()) held |= InputButtons.Reload;
            if (_leanLeft.IsPressed()) held |= InputButtons.LeanLeft;
            if (_leanRight.IsPressed()) held |= InputButtons.LeanRight;

            var cmd = new PlayerInputCmd
            {
                Tick = tick,
                Move = _move.ReadValue<Vector2>(),
                Yaw = Yaw,
                Pitch = Pitch,
                Buttons = held | _latched,
            };
            _latched = InputButtons.None;
            return cmd;
        }

        /// <summary>A command with no input, keeping the current view. Used while a menu is open.</summary>
        public PlayerInputCmd BuildIdleCmd(int tick)
        {
            _latched = InputButtons.None;
            return new PlayerInputCmd { Tick = tick, Yaw = Yaw, Pitch = Pitch };
        }

        public void Dispose()
        {
            _map.Disable();
            _map.Dispose();
        }
    }
}
