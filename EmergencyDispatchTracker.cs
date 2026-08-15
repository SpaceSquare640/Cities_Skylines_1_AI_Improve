using System.Collections.Generic;
using UnityEngine;

namespace AIImprove
{
    // Shared between EmergencyIgnoreCostsPatch (records dispatch start) and
    // ArrivalTrackingPatch (records arrival, computes elapsed). Vehicle IDs are unique across
    // the whole game (single ushort namespace shared by all vehicle types), so one dictionary
    // safely covers AmbulanceAI/FireTruckAI/PoliceCarAI at once.
    //
    // Plain Dictionary + lock, not ConcurrentDictionary - this project targets net35 (matching
    // the game's Mono runtime) and System.Collections.Concurrent isn't available there.
    internal static class EmergencyDispatchTracker
    {
        private static readonly Dictionary<ushort, float> DispatchStartTime = new Dictionary<ushort, float>();
        private static readonly object Lock = new object();

        public static void RecordDispatchStart(ushort vehicleId)
        {
            lock (Lock)
            {
                DispatchStartTime[vehicleId] = Time.realtimeSinceStartup;
            }
        }

        // Returns the elapsed seconds since RecordDispatchStart(vehicleId), or null if no
        // matching dispatch was recorded (e.g. mod was enabled mid-trip, or this arrival is the
        // return-to-depot leg rather than the outbound emergency leg).
        public static float? TakeElapsedSeconds(ushort vehicleId)
        {
            lock (Lock)
            {
                float startTime;
                if (!DispatchStartTime.TryGetValue(vehicleId, out startTime))
                {
                    return null;
                }

                DispatchStartTime.Remove(vehicleId);
                return Time.realtimeSinceStartup - startTime;
            }
        }

        // BUG FOUND VIA AUDIT (2026-08-15): TakeElapsedSeconds is the only thing that removed
        // entries, so any emergency vehicle that despawned WITHOUT reaching its target (recalled,
        // bulldozed depot, mod toggled mid-trip) left its dispatch timestamp behind forever.
        // Vehicle IDs come from a fixed reused pool, so a later vehicle handed the same ID would
        // "arrive" against the dead vehicle's start time and report a wildly inflated response
        // time. Wired into the existing global VehicleAI.ReleaseVehicle postfix alongside the
        // other per-vehicle trackers.
        public static void ReleaseVehicle(ushort vehicleId)
        {
            lock (Lock)
            {
                DispatchStartTime.Remove(vehicleId);
            }
        }
    }
}
