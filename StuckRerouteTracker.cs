using System.Collections.Generic;
using UnityEngine;

namespace AIImprove
{
    // Reroute cooldown/hysteresis for FlexibleReroutePatch (trains/metro, both handled by
    // TrainAI - and aircraft taxiing). Decision input is real-time segment congestion density
    // from SegmentCongestionQuery, not a speed-based "is it stuck" proxy - upgraded per user
    // request (2026-08-12) from the original v1 stuck-detection heuristic. See
    // Cities_Skylines_1_AI_Improve_Document/01, "中途動態改道" entry.
    //
    // Class name kept as StuckRerouteTracker for continuity with existing references; despite
    // the name it now gates purely on density, not "stuck" in the speed sense.
    internal static class StuckRerouteTracker
    {
        // NetSegment.m_trafficDensity is on a vanilla 0-100 scale.
        //
        // TUNED (2026-08-12) from live test data at threshold=60/cooldown=20: 2431 reroute
        // attempts in ~17 minutes, 91% failed (no alternate route found - expected on rail,
        // which is far more topologically constrained than roads: many segments genuinely have
        // only one track, so "failed" there isn't a bug, just no alternative to find). Every
        // attempt costs a real PathFind computation even when it fails, so that volume is
        // wasted work. Raised both values to cut down how often we even try, while keeping the
        // mechanism responsive to genuinely severe congestion. Still not a principled
        // derivation - needs further real-world calibration.
        private const float DensityThreshold = 80f;
        private const float RerouteCooldownSeconds = 40f;

        private class State
        {
            public float LastRerouteTime = float.NegativeInfinity;
        }

        private static readonly Dictionary<ushort, State> States = new Dictionary<ushort, State>();

        // Cheap pre-check with no density input, so callers can skip the real (segment-walking)
        // density computation entirely for a vehicle that's still on cooldown. Added 2026-08-13
        // as a hot-path optimization once FlexibleReroutePatch started running for every ordinary
        // CarAI vehicle in the city every tick - SegmentCongestionQuery.GetAverageAheadDensity
        // was being computed unconditionally even for the common case of a vehicle that just
        // rerouted and can't act again for another ~40 seconds regardless.
        public static bool IsOnCooldown(ushort vehicleId)
        {
            State state;
            if (!States.TryGetValue(vehicleId, out state))
            {
                return false;
            }

            return Time.realtimeSinceStartup - state.LastRerouteTime < RerouteCooldownSeconds;
        }

        // Call every SimulationStep with the average congestion density ahead of the vehicle
        // (see SegmentCongestionQuery.GetAverageAheadDensity). Returns true at most once per
        // RerouteCooldownSeconds, whenever that density is at or above DensityThreshold.
        public static bool ShouldReroute(ushort vehicleId, float aheadDensity)
        {
            if (aheadDensity < DensityThreshold)
            {
                return false;
            }

            State state;
            if (!States.TryGetValue(vehicleId, out state))
            {
                state = new State();
                States[vehicleId] = state;
            }

            float now = Time.realtimeSinceStartup;
            if (now - state.LastRerouteTime < RerouteCooldownSeconds)
            {
                return false;
            }

            state.LastRerouteTime = now;
            return true;
        }

        public static void Clear(ushort vehicleId)
        {
            States.Remove(vehicleId);
        }
    }
}
