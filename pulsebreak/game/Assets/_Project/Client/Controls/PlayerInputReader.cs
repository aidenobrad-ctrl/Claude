using System;
using UnityEngine;
using Pulsebreak.Client.Settings;
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
        readonly InputAction _jump, _sprint, _crouch, _fire, _ads, _reload;
        readonly InputAction _utility;
        readonly InputAction _scroll;
        readonly InputAction[] _slotKeys;
        InputButtons _latched;
        // Toggle sprint: on after a press of the key, until something calls it off.
        bool _sprintOn;
        int _latchedSlot;
        int _scrollSteps;

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
            _utility = AddButton("Utility", "<Keyboard>/g");

            _scroll = _map.AddAction("WeaponScroll", InputActionType.Value, "<Mouse>/scroll");
            _slotKeys = new[]
            {
                AddButton("Weapon1", "<Keyboard>/1"),
                AddButton("Weapon2", "<Keyboard>/2"),
                AddButton("Weapon3", "<Keyboard>/3"),
            };

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
            if (GameSettings.ToggleSprint)
            {
                if (_sprint.WasPressedThisFrame()) _sprintOn = !_sprintOn;
            }
            else
            {
                _sprintOn = false;
                Latch(_sprint, InputButtons.Sprint);
            }
            Latch(_crouch, InputButtons.Crouch);
            Latch(_fire, InputButtons.Fire);
            Latch(_ads, InputButtons.Ads);
            Latch(_reload, InputButtons.Reload);
            Latch(_utility, InputButtons.Utility1);

            for (int i = 0; i < _slotKeys.Length; i++)
            {
                if (_slotKeys[i].WasPressedThisFrame()) _latchedSlot = i + 1;
            }

            // Wheel up is the previous weapon, wheel down the next.
            float wheel = _scroll.ReadValue<Vector2>().y;
            if (wheel > 0.01f) _scrollSteps--;
            else if (wheel < -0.01f) _scrollSteps++;
        }

        void Latch(InputAction action, InputButtons button)
        {
            if (action.WasPressedThisFrame()) _latched |= button;
        }

        /// <summary>Call once per simulation tick.</summary>
        /// <param name="currentSlot">The weapon slot in hand (or being switched to), counting from 0.</param>
        /// <param name="slotCount">How many weapon slots there are, for wrapping the mouse wheel.</param>
        /// <param name="usable">One bit per slot that has a weapon in it. The wheel skips the others.</param>
        public PlayerInputCmd BuildCmd(int tick, int currentSlot, int slotCount, int usable = ~0)
        {
            InputButtons held = InputButtons.None;
            if (_jump.IsPressed()) held |= InputButtons.Jump;
            if (GameSettings.ToggleSprint)
            {
                // A toggled sprint ends when the player stops running forward, or wants to
                // aim or shoot, the same moments a held sprint would be let go.
                Vector2 move = _move.ReadValue<Vector2>();
                if (move.y < 0.3f || _ads.IsPressed() || _fire.IsPressed()) _sprintOn = false;
                if (_sprintOn) held |= InputButtons.Sprint;
            }
            else if (_sprint.IsPressed())
            {
                held |= InputButtons.Sprint;
            }
            if (_crouch.IsPressed()) held |= InputButtons.Crouch;
            if (_fire.IsPressed()) held |= InputButtons.Fire;
            if (_ads.IsPressed()) held |= InputButtons.Ads;
            if (_reload.IsPressed()) held |= InputButtons.Reload;
            if (_utility.IsPressed()) held |= InputButtons.Utility1;

            var cmd = new PlayerInputCmd
            {
                Tick = tick,
                Move = _move.ReadValue<Vector2>(),
                Yaw = Yaw,
                Pitch = Pitch,
                Buttons = held | _latched,
                SelectSlot = ConsumeSlotRequest(currentSlot, slotCount, usable),
            };
            _latched = InputButtons.None;
            return cmd;
        }

        byte ConsumeSlotRequest(int currentSlot, int slotCount, int usable)
        {
            int request = 0;
            if (_latchedSlot > 0 && _latchedSlot <= slotCount)
            {
                request = _latchedSlot;
            }
            else if (_scrollSteps != 0 && slotCount > 1)
            {
                int step = _scrollSteps > 0 ? 1 : -1;
                int slot = currentSlot;
                for (int tries = 0; tries < slotCount; tries++)
                {
                    slot = ((slot + step) % slotCount + slotCount) % slotCount;
                    if ((usable & (1 << slot)) == 0) continue;
                    request = slot + 1;
                    break;
                }
            }

            _latchedSlot = 0;
            _scrollSteps = 0;
            return (byte)request;
        }

        /// <summary>A command with no input, keeping the current view. Used while a menu is open.</summary>
        public PlayerInputCmd BuildIdleCmd(int tick)
        {
            _latched = InputButtons.None;
            _latchedSlot = 0;
            _scrollSteps = 0;
            return new PlayerInputCmd { Tick = tick, Yaw = Yaw, Pitch = Pitch };
        }

        public void Dispose()
        {
            _map.Disable();
            _map.Dispose();
        }
    }
}
