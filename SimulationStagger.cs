using ColossalFramework;

namespace AIImprove
{
    // "進行模組效能優化" (2026-08-15): the dominant cost in this mod turned out to be
    // FlexibleReroutePatch and TrainSingleTrackConflictDetector both walking up to 6 path
    // positions (SegmentCongestionQuery.GetAverageAheadDensity / the lookahead loop in
    // TrainSingleTrackConflictDetector) on every single SimulationStep call, for every vehicle in
    // the city - StuckRerouteTracker.IsOnCooldown only short-circuits a vehicle that has already
    // rerouted at least once, so ordinary traffic that never crosses the density threshold pays
    // the full walk every tick, forever.
    //
    // Vanilla itself uses exactly this trick for per-entity work that doesn't need to happen
    // every single tick (e.g. spreading building/vehicle simulation across frames by ID) - stagger
    // which tick each vehicle is actually checked on, keyed by vehicle ID so the load spreads
    // evenly across frames instead of every vehicle doing its check on the same tick.
    internal static class SimulationStagger
    {
        // Congestion doesn't meaningfully change tick-to-tick, and every caller here already sits
        // behind a many-second cooldown once triggered - checking each vehicle roughly once every
        // 32 ticks instead of every tick cuts the walk's total cost by ~32x with no meaningful
        // loss of responsiveness.
        public const int DefaultIntervalFrames = 32;

        public static bool ShouldRunThisFrame(ushort vehicleId, int intervalFrames = DefaultIntervalFrames)
        {
            uint frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            return (frame + vehicleId) % (uint)intervalFrames == 0U;
        }
    }
}
