// Reads the keyboard and gamepad into the simulation's Controls once per
// frame. Uses the Input System package when it is installed and enabled,
// otherwise the legacy Input Manager.
//   Keyboard: W/S or arrows (throttle/brake), A/D or arrows (steer), Space
//   (handbrake), E/Q (shift up/down), R (reset to road), C (camera),
//   B (look back), Esc (pause).
//   Gamepad: left stick (steer), RT/LT (throttle/brake), A (handbrake),
//   B/X (shift), Y (camera), Back (reset), Start (pause).
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
