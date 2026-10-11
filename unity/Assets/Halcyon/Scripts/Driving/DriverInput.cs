// Reads the keyboard and gamepad into the simulation's Controls once per
// frame. Uses the Input System package when it is installed and enabled,
// otherwise the legacy Input Manager.
//   Keyboard: W/S or arrows (throttle/brake), A/D or arrows (steer), Space
//   (handbrake), E/Q (shift up/down), R (reset to road), C (camera),
//   B (look back), L (lights), Esc (pause), G (garage), T (events),
//   [ and ] (an hour back or forward), \ (the weather: dynamic, then each sky).
//   Gamepad: left stick (steer), RT/LT (throttle/brake), A (handbrake),
//   B/X (shift), Y (camera), Back (reset), Start (pause), LB (garage),
//   d-pad up (events).
//   Menus (the garage): arrows / WASD / d-pad, Enter / Space / A to pick,
//   Esc / Backspace / B to go back.
using Halcyon.Core;
using UnityEngine;
#if HALCYON_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Halcyon
{
    public sealed class DriverInput
    {
        public readonly Controls Controls = new Controls();
        readonly ActionStates prevHeld = new ActionStates();
        readonly PadSettings pad = PadSettings.Default();

        /// <summary>Menu keys (the garage): true on the frame they go down.</summary>
        public struct MenuKeys
        {
            public bool left, right, up, down, select, back, garage, events, hourBack, hourForward, weather;
        }

        public MenuKeys Menu;
        MenuKeys menuHeld;

        /// <summary>Menu keys held down right now (for hold-to-confirm actions).</summary>
        public MenuKeys MenuHeld => menuHeld;

        /// <summary>Poll devices; call once per rendered frame.</summary>
        public Controls Poll()
        {
            var c = Controls;
            prevHeld.CopyFrom(c.held);
            c.SetNeutral();
            c.held.Clear();
            bool any = false;
#if HALCYON_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            any |= ReadInputSystem(c);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!any) any |= ReadLegacy(c);
#endif
            var m = new MenuKeys();
#if HALCYON_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            ReadMenuInputSystem(ref m);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            ReadMenuLegacy(ref m);
#endif
            Menu = new MenuKeys
            {
                left = m.left && !menuHeld.left,
                right = m.right && !menuHeld.right,
                up = m.up && !menuHeld.up,
                down = m.down && !menuHeld.down,
                select = m.select && !menuHeld.select,
                back = m.back && !menuHeld.back,
                garage = m.garage && !menuHeld.garage,
                events = m.events && !menuHeld.events,
                hourBack = m.hourBack && !menuHeld.hourBack,
                hourForward = m.hourForward && !menuHeld.hourForward,
                weather = m.weather && !menuHeld.weather,
            };
            menuHeld = m;
            // Edges: pressed this frame and not the last.
            c.pressed.Clear();
            for (int i = 0; i < ActionStates.Count; i++)
            {
                var a = (ControlAction)i;
                c.pressed[a] = c.held[a] && !prevHeld[a];
            }
            return c;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        static void ReadMenuLegacy(ref MenuKeys m)
        {
            bool Key(KeyCode k) => Input.GetKey(k);
            m.left |= Key(KeyCode.LeftArrow) || Key(KeyCode.A);
            m.right |= Key(KeyCode.RightArrow) || Key(KeyCode.D);
            m.up |= Key(KeyCode.UpArrow) || Key(KeyCode.W);
            m.down |= Key(KeyCode.DownArrow) || Key(KeyCode.S);
            m.select |= Key(KeyCode.Return) || Key(KeyCode.KeypadEnter) || Key(KeyCode.Space);
            m.back |= Key(KeyCode.Escape) || Key(KeyCode.Backspace);
            m.garage |= Key(KeyCode.G);
            m.events |= Key(KeyCode.T);
            m.hourBack |= Key(KeyCode.LeftBracket);
            m.hourForward |= Key(KeyCode.RightBracket);
            m.weather |= Key(KeyCode.Backslash);
        }

        bool ReadLegacy(Controls c)
        {
            bool Key(KeyCode k) => Input.GetKey(k);
            c.held[ControlAction.Throttle] = Key(KeyCode.W) || Key(KeyCode.UpArrow);
            c.held[ControlAction.Brake] = Key(KeyCode.S) || Key(KeyCode.DownArrow);
            c.held[ControlAction.SteerLeft] = Key(KeyCode.A) || Key(KeyCode.LeftArrow);
            c.held[ControlAction.SteerRight] = Key(KeyCode.D) || Key(KeyCode.RightArrow);
            c.held[ControlAction.Handbrake] = Key(KeyCode.Space);
            c.held[ControlAction.ShiftUp] = Key(KeyCode.E);
            c.held[ControlAction.ShiftDown] = Key(KeyCode.Q);
            c.held[ControlAction.Reset] = Key(KeyCode.R);
            c.held[ControlAction.Camera] = Key(KeyCode.C);
            c.held[ControlAction.LookBack] = Key(KeyCode.B);
            c.held[ControlAction.Pause] = Key(KeyCode.Escape);
            c.held[ControlAction.Lights] = Key(KeyCode.L);
            c.throttle = c.held[ControlAction.Throttle] ? 1 : 0;
            c.brake = c.held[ControlAction.Brake] ? 1 : 0;
            c.handbrake = c.held[ControlAction.Handbrake] ? 1 : 0;
            c.steer = (c.held[ControlAction.SteerRight] ? 1 : 0) - (c.held[ControlAction.SteerLeft] ? 1 : 0);
            c.analogSteer = false;
            c.source = InputSource.Keyboard;
            return true;
        }
#endif

#if HALCYON_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        static void ReadMenuInputSystem(ref MenuKeys m)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                m.left |= kb.leftArrowKey.isPressed || kb.aKey.isPressed;
                m.right |= kb.rightArrowKey.isPressed || kb.dKey.isPressed;
                m.up |= kb.upArrowKey.isPressed || kb.wKey.isPressed;
                m.down |= kb.downArrowKey.isPressed || kb.sKey.isPressed;
                m.select |= kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed;
                m.back |= kb.escapeKey.isPressed || kb.backspaceKey.isPressed;
                m.garage |= kb.gKey.isPressed;
                m.events |= kb.tKey.isPressed;
                m.hourBack |= kb.leftBracketKey.isPressed;
                m.hourForward |= kb.rightBracketKey.isPressed;
                m.weather |= kb.backslashKey.isPressed;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                var stick = gp.leftStick.ReadValue();
                m.left |= gp.dpad.left.isPressed || stick.x < -0.6f;
                m.right |= gp.dpad.right.isPressed || stick.x > 0.6f;
                m.up |= gp.dpad.up.isPressed || stick.y > 0.6f;
                m.down |= gp.dpad.down.isPressed || stick.y < -0.6f;
                m.select |= gp.buttonSouth.isPressed;
                m.back |= gp.buttonEast.isPressed;
                m.garage |= gp.leftShoulder.isPressed;
                m.events |= gp.dpad.up.isPressed && !gp.leftStick.IsActuated();
            }
        }

        bool ReadInputSystem(Controls c)
        {
            bool used = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                c.held[ControlAction.Throttle] = kb.wKey.isPressed || kb.upArrowKey.isPressed;
                c.held[ControlAction.Brake] = kb.sKey.isPressed || kb.downArrowKey.isPressed;
                c.held[ControlAction.SteerLeft] = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                c.held[ControlAction.SteerRight] = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                c.held[ControlAction.Handbrake] = kb.spaceKey.isPressed;
                c.held[ControlAction.ShiftUp] = kb.eKey.isPressed;
                c.held[ControlAction.ShiftDown] = kb.qKey.isPressed;
                c.held[ControlAction.Reset] = kb.rKey.isPressed;
                c.held[ControlAction.Camera] = kb.cKey.isPressed;
                c.held[ControlAction.LookBack] = kb.bKey.isPressed;
                c.held[ControlAction.Pause] = kb.escapeKey.isPressed;
                c.held[ControlAction.Lights] = kb.lKey.isPressed;
                c.throttle = c.held[ControlAction.Throttle] ? 1 : 0;
                c.brake = c.held[ControlAction.Brake] ? 1 : 0;
                c.handbrake = c.held[ControlAction.Handbrake] ? 1 : 0;
                c.steer = (c.held[ControlAction.SteerRight] ? 1 : 0) - (c.held[ControlAction.SteerLeft] ? 1 : 0);
                c.analogSteer = false;
                c.source = InputSource.Keyboard;
                used = true;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                double steer = InputShaping.ShapeAxis(gp.leftStick.x.ReadValue(), pad);
                double rt = InputShaping.ShapeTrigger(gp.rightTrigger.ReadValue(), pad);
                double lt = InputShaping.ShapeTrigger(gp.leftTrigger.ReadValue(), pad);
                bool active = System.Math.Abs(steer) > 0 || rt > 0 || lt > 0 || gp.buttonSouth.isPressed;
                if (active || !(c.throttle > 0 || c.brake > 0 || c.steer != 0))
                {
                    if (System.Math.Abs(steer) > 0 || c.steer == 0)
                    {
                        c.steer = steer;
                        c.analogSteer = true;
                    }
                    c.throttle = System.Math.Max(c.throttle, rt);
                    c.brake = System.Math.Max(c.brake, lt);
                    if (gp.buttonSouth.isPressed) c.handbrake = 1;
                    if (active) c.source = InputSource.Gamepad;
                }
                c.held[ControlAction.ShiftUp] |= gp.buttonEast.isPressed;
                c.held[ControlAction.ShiftDown] |= gp.buttonWest.isPressed;
                c.held[ControlAction.Camera] |= gp.buttonNorth.isPressed;
                c.held[ControlAction.Reset] |= gp.selectButton.isPressed;
                c.held[ControlAction.Pause] |= gp.startButton.isPressed;
                c.held[ControlAction.LookBack] |= gp.rightShoulder.isPressed;
                used = true;
            }
            return used;
        }
#endif
    }
}
