// Vehicle.Step, PlayerControlFilter.Update and the collision functions must
// not allocate (Unity's GC would stutter at 240 Hz). Measured with the
// runtime's per-thread allocation counter after a warm-up.
using System;

namespace Halcyon.Core.Tests
{
    static class AllocationChecks
    {
        public static Check Run()
        {
            var c = new Check("alloc", "Step/filter/collisions allocate 0 bytes");
            VehicleParams p = VehicleParams.Build(Cars.All[0]);
            var a = new Vehicle(p);
            var b = new Vehicle(VehicleParams.Build(Cars.All[0]));
            b.Place(0.5, 0, -6, 0.3);
            var ground = new FlatGround((x, z) => z < -20 ? Surface.Grass : Surface.Asphalt);
            var world = new Colliders();
            world.AddSegment(new Segment { ax = -20, az = -30, bx = 20, bz = -30, top = 2, bottom = -1, bounce = 0.3, friction = 0.5, kind = SegmentKind.Wall });
            world.AddCircle(new Circle { x = 1, z = -15, r = 0.3, top = 2, bottom = 0, kind = CircleKind.Post });
            var filter = new PlayerControlFilter();
            Controls ctl = Controls.Neutral();
            ctl.analogSteer = false;
            int breaks = 0;
            Action<Circle> onBreak = _ => breaks++;

            void Steps(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    ctl.throttle = 1;
                    ctl.steer = (i / 120) % 3 - 1;
                    a.input = filter.Update(ctl, a, Loop.SIM_DT);
                    a.Step(Loop.SIM_DT, ground);
                    b.Step(Loop.SIM_DT, ground);
                    CarCollision.CollideWorld(a, world, onBreak);
                    CarCollision.CollideWorld(b, world, onBreak);
                    CarCollision.CollideCars(a, b);
                }
            }

            Steps(600); // warm-up: JIT, static constructors, delegate caches
            long before = GC.GetAllocatedBytesForCurrentThread();
            Steps(2400);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            c.Num(0, bytes, "bytes allocated over 2400 steps of two cars with filter and collisions");
            return c;
        }
    }
}
