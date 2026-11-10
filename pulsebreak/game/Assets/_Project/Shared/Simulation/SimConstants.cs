namespace Pulsebreak.Shared.Simulation
{
    /// <summary>
    /// Fixed simulation timing. Movement and weapons advance in whole ticks, never in
    /// frame time, so the same code can later run on the dedicated server and be
    /// re-simulated on the client for prediction.
    /// </summary>
    public static class SimConstants
    {
        public const int TickRate = 60;
        public const float TickDelta = 1f / TickRate;

        /// <summary>Cap on catch-up ticks in one rendered frame, so a long hitch cannot snowball.</summary>
        public const int MaxTicksPerFrame = 6;
    }
}
