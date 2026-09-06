using ColossalFramework;
using UnityEngine;

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
        // "每個功能中的調整設定及數據可以拆開以及詳細調整" (2026-08-15): now a slider
        // (ModSettings.RerouteCheckIntervalFrames), default still 32.
        // The clamp is not defensive dressing - without it this is a DivideByZeroException on the
        // simulation thread, thrown for every vehicle in the city on the very next tick, which
        // stops the simulation dead the moment a city loads. The slider's own minimum is 1, but
        // the value is read back from a plain text settings file under the player's local app
        // data: hand-edited, or truncated by a crash mid-write, it can arrive as 0.
        //
        // This became more urgent on 2026-09-05: FlexibleReroutePatch.Car.Postfix now calls this
        // ahead of its own enabled check (so the settings reads behind it can be skipped), which
        // means a corrupt value would take down players who have every reroute feature switched
        // OFF - people who were previously never running this line at all. Clamping here fixes it
        // for all five call sites at once rather than reordering one of them back.
        public static bool ShouldRunThisFrame(ushort vehicleId)
        {
            uint frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            uint interval = (uint)Mathf.Max(1, ModSettings.RerouteCheckIntervalFrames.value);
            return (frame + vehicleId) % interval == 0U;
        }
    }
}
