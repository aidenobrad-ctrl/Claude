// The device-independent part of the web version's src/engine/input.ts: the
// Controls snapshot the game produces once per frame, its neutral state, and
// the pad deadzone/response shaping. Keyboard, gamepad and touch reading
// stay in the engine (Unity produces Controls itself).
using System;

namespace Halcyon.Core
{
    /// <summary>The TypeScript's Action union ('throttle' | 'brake' | ...), in the same order as ACTIONS.</summary>
    public enum ControlAction
    {
        Throttle, Brake, SteerLeft, SteerRight, Handbrake, Clutch, ShiftUp, ShiftDown,
        Camera, LookBack, Reset, Rewind, Pause, Map, Photo, Horn, Lights, Radio, Confirm,
    }

    /// <summary>'keyboard' | 'gamepad' | 'touch' | 'script'.</summary>
    public enum InputSource { Keyboard, Gamepad, Touch, Script }

    /// <summary>A Record&lt;Action, boolean&gt;: one flag per ControlAction.</summary>
    public sealed class ActionStates
    {
        /// <summary>Number of ControlAction values.</summary>
        public const int Count = (int)ControlAction.Confirm + 1;

        readonly bool[] flags = new bool[Count];

        public bool this[ControlAction a]
        {
            get => flags[(int)a];
            set => flags[(int)a] = value;
        }

        public void Clear() => Array.Clear(flags, 0, Count);

        public void CopyFrom(ActionStates o) => Array.Copy(o.flags, flags, Count);
    }

    public sealed class Controls
    {
        /// <summary>-1 full left .. +1 full right.</summary>
        public double steer;
        /// <summary>0..1.</summary>
        public double throttle;
        /// <summary>0..1.</summary>
        public double brake;
        /// <summary>0..1.</summary>
        public double handbrake;
        /// <summary>0..1.</summary>
        public double clutch;
        /// <summary>True when steering is analog (pad, touch, script); keyboard steering gets smoothed.</summary>
        public bool analogSteer = true;
        public readonly ActionStates held = new ActionStates();
        /// <summary>Actions that went down this frame.</summary>
        public readonly ActionStates pressed = new ActionStates();
        public InputSource source = InputSource.Script;

        /// <summary>neutralControls().</summary>
        public static Controls Neutral() => new Controls();

        /// <summary>Reset this snapshot to neutralControls() in place (no allocation).</summary>
        public void SetNeutral()
        {
            steer = 0;
            throttle = 0;
            brake = 0;
            handbrake = 0;
            clutch = 0;
            analogSteer = true;
            held.Clear();
            pressed.Clear();
            source = InputSource.Script;
        }
    }

    public sealed class PadSettings
    {
        public double deadzone;
        public double outerDeadzone;
        public double triggerDeadzone;
        /// <summary>1 = linear, &gt;1 = finer near center.</summary>
        public double steerLinearity;

        /// <summary>The web version's defaults (Input.pad).</summary>
        public static PadSettings Default() => new PadSettings { deadzone = 0.1, outerDeadzone = 0.03, triggerDeadzone = 0.04, steerLinearity = 1.25 };
    }

    public static class InputShaping
    {
        /// <summary>shapeAxis: apply radial inner/outer deadzone and a response curve to a stick axis.</summary>
        public static double ShapeAxis(double v, PadSettings s)
        {
            double a = Math.Abs(v);
            if (a <= s.deadzone) return 0;
            double t = JsMath.Min(1, (a - s.deadzone) / JsMath.Max(1e-6, 1 - s.deadzone - s.outerDeadzone));
            return M.Sign(v) * DMath.Pow(t, s.steerLinearity);
        }

        /// <summary>shapeTrigger: trigger deadzone, rescaled to 0..1.</summary>
        public static double ShapeTrigger(double v, PadSettings s)
        {
            if (v <= s.triggerDeadzone) return 0;
            return JsMath.Min(1, (v - s.triggerDeadzone) / (1 - s.triggerDeadzone));
        }
    }
}
