// Turns raw player controls into driver input. Digital (keyboard) steering
// is ramped at a speed-dependent rate so it feels like turning a wheel;
// analog steering passes through with light smoothing. Ported from the web
// version's src/vehicles/controls.ts.
using System;

namespace Halcyon.Core
{
    public sealed class ControlSettings
    {
        /// <summary>Steering sensitivity multiplier (0.5 .. 1.5).</summary>
        public double steerSensitivity = 1;
        /// <summary>Throttle stays applied while no brake is pressed (touch accessibility).</summary>
        public bool autoThrottle;
    }

    /// <summary>
    /// Call Update once per simulation step, before Vehicle.Step, and give the
    /// vehicle its result (vehicle.input = filter.Update(controls, vehicle, Loop.SIM_DT)).
    /// The result is the same <see cref="Out"/> object every time.
    /// </summary>
    public sealed class PlayerControlFilter
    {
        public double steer;
        public ControlSettings settings = new ControlSettings();
        /// <summary>The filtered input (the TypeScript's `out`).</summary>
        public readonly DriverInput Out = DriverInput.Neutral();

        public void Reset()
        {
            steer = 0;
        }

        public DriverInput Update(Controls c, Vehicle v, double dt)
        {
            double sens = settings.steerSensitivity;
            if (c.analogSteer)
            {
                // Light smoothing removes jitter from sticks and touch without lag.
                steer += (M.Clamp(c.steer * sens, -1, 1) - steer) * JsMath.Min(1, dt * 30);
            }
            else
            {
                double target = M.Clamp(c.steer, -1, 1);
                double speed = Math.Abs(v.vLong);
                // Turning in slows from 4.5/s at a crawl to 1.6/s at 200 km/h;
                // returning to center is always quicker.
                double inRate = (4.5 - JsMath.Min(1, speed / 55) * 2.9) * sens;
                bool returning = target == 0 || M.Sign(target) != M.Sign(steer);
                steer = M.MoveTowards(steer, target, (returning ? 6 : inRate) * dt);
            }
            DriverInput o = Out;
            o.steer = steer;
            o.throttle = M.Clamp(c.throttle, 0, 1);
            if (settings.autoThrottle && c.brake < 0.05 && c.handbrake < 0.05) o.throttle = JsMath.Max(o.throttle, 0.85);
            o.brake = M.Clamp(c.brake, 0, 1);
            o.handbrake = M.Clamp(c.handbrake, 0, 1);
            o.clutch = M.Clamp(c.clutch, 0, 1);
            o.shiftUp = c.pressed[ControlAction.ShiftUp];
            o.shiftDown = c.pressed[ControlAction.ShiftDown];
            return o;
        }
    }
}
