// The fixed simulation step, from the web version's src/engine/loop.ts.
// The simulation always advances in exact 1/240 s steps. Unity's
// Time.fixedDeltaTime is a float, so step the Core with Loop.SIM_DT (a
// double) rather than with Unity's delta, or results drift from the web build.
namespace Halcyon.Core
{
    public static class Loop
    {
        public const double SIM_HZ = 240;
        public const double SIM_DT = 1 / SIM_HZ;
    }
}
