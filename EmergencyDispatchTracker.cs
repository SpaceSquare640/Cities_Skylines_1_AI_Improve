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
        // Called by TrackerReset when a save is unloaded. Building, vehicle and node IDs are
        // recycled from fixed pools, so anything left here from the previous city would be read
        // back as if it described the new one. Registered centrally rather than relied on being
        // remembered per class - see 12 - 開發準則, 準則 3.
        public static void ResetForNewLevel()
        {
            lock (Lock)
            {
                DispatchStartTime.Clear();
                EverRecorded.Clear();
            }
        }

        private static readonly Dictionary<ushort, float> DispatchStartTime = new Dictionary<ushort, float>();

        // OPEN BUG A0 (2026-09-07): an 83-minute session recorded 2500 emergency dispatches and
        // 66900 arrivals, of which 2698 were genuine outbound arrivals with no dispatch start on
        // record - and NOT ONE match. Two sets of numbers of the same magnitude that never
        // intersect at all is not a timing problem, it is a sign the two sides are not talking
        // about the same vehicle: the dispatch side receives its vehicle ID from injected IL, and
        // an argument taken from the wrong position there would record a meaningless number that
        // simply never collides with a real arrival.
        //
        // This remembers every ID the dispatch side has ever recorded, so a miss on the arrival
        // side can distinguish "this ID was never recorded" (the two sides disagree about what a
        // vehicle ID is) from "it was recorded and something removed it" (a lifetime bug).
        // A ushort has 65536 possible values, so the set is bounded and small.
        //
        // Remove once A0 is closed.
        private static readonly System.Collections.Generic.HashSet<ushort> EverRecorded =
            new System.Collections.Generic.HashSet<ushort>();

        public static bool WasEverRecorded(ushort vehicleId)
        {
            lock (Lock)
            {
                return EverRecorded.Contains(vehicleId);
            }
        }

        public static int LiveCount
        {
            get { lock (Lock) { return DispatchStartTime.Count; } }
        }

        // A few IDs currently held, for eyeballing against the IDs the arrival side reports.
        public static string SampleLiveIds(int max)
        {
            lock (Lock)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (ushort id in DispatchStartTime.Keys)
                {
                    if (n++ >= max)
                    {
                        break;
                    }

                    if (n > 1)
                    {
                        sb.Append(',');
                    }

                    sb.Append(id);
                }

                return sb.ToString();
            }
        }
        private static readonly object Lock = new object();

        public static void RecordDispatchStart(ushort vehicleId)
        {
            lock (Lock)
            {
                DispatchStartTime[vehicleId] = Time.realtimeSinceStartup;
                EverRecorded.Add(vehicleId);
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
