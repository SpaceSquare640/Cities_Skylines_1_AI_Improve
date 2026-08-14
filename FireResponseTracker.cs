using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // Caps how many fire trucks / fire helicopters respond to any single burning building.
    // Vanilla CommonBuildingAI keeps generating TransferReason.Fire/Fire2 outgoing offers as
    // long as `existingResponderCount * 25 < fireIntensity-based demand` - no hard cap, so a
    // severe enough fire can pull in an unbounded number of vehicles. Per user request
    // (2026-08-12): cap at 10 trucks and 10 helicopters per building, independently.
    //
    // Tracks assignment the same shape as AirTrafficControlManager (per-vehicle assignment +
    // per-target count, so reassigning a vehicle correctly moves its count rather than double-
    // counting or leaking).
    //
    // Two follow-up refinements, same day, both per explicit user request:
    //  - Vehicles blocked by the cap are force-redirected to another building that is still
    //    burning and still has capacity, instead of being sent back to idle at the station
    //    (see KnownBurningBuildings / TryFindAlternateBurningBuilding, used by
    //    FireResponseCapPatch).
    //  - If a single building keeps burning for more than 15 minutes straight, the cap is
    //    lifted entirely for that building (a fire that stubborn needs everything available,
    //    not an artificial ceiling) - see FireStartTime / UnlimitedAfterSeconds.
    internal static class FireResponseTracker
    {
        // TUNED (2026-08-14, raised per user request): 10 -> 20. Real-world feedback was that
        // severe fires were outpacing the fire-growth-vs-extinguish-rate balance at 10 - see
        // FireResponseCapPatch.cs's notes on the same-day dispatch-conflict fix, which likely
        // explains part of why fires felt unwinnable (trucks fighting over targets instead of
        // actually converging on one), but the cap itself was also raised independently.
        public const int MaxRespondersPerBuilding = 20;
        private const float UnlimitedAfterSeconds = 15f * 60f;

        private static readonly Dictionary<ushort, int> TruckResponseCount = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, ushort> TruckAssignment = new Dictionary<ushort, ushort>();

        private static readonly Dictionary<ushort, int> CopterResponseCount = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, ushort> CopterAssignment = new Dictionary<ushort, ushort>();

        // How long (real time) each building has been continuously on fire, first observed at
        // its first responder assignment. Cleared whenever the building is seen with no fire
        // intensity left, so a re-ignition later starts a fresh 15-minute count.
        private static readonly Dictionary<ushort, float> FireStartTime = new Dictionary<ushort, float>();

        // Buildings we've dispatched a truck/copter to recently - candidate pool for
        // FireResponseCapPatch to search when a vehicle needs to be redirected away from a
        // building that's already at cap. Self-cleaning: entries are only ever trusted after
        // re-checking live m_fireIntensity, so a stale entry (fire already out) is just skipped
        // rather than needing explicit removal here.
        private static readonly HashSet<ushort> KnownBurningBuildings = new HashSet<ushort>();

        // Call before actually setting a vehicle's target building. Returns true if the
        // assignment is allowed (building is 0, i.e. clearing target, is always allowed).
        // Returns false if buildingId is already at the responder cap - caller should redirect
        // the vehicle elsewhere instead of proceeding.
        public static bool TryAssign(bool isCopter, ushort vehicleId, ushort buildingId)
        {
            Dictionary<ushort, int> counts = isCopter ? CopterResponseCount : TruckResponseCount;
            Dictionary<ushort, ushort> assignments = isCopter ? CopterAssignment : TruckAssignment;

            ushort previous;
            if (assignments.TryGetValue(vehicleId, out previous) && previous != 0)
            {
                Decrement(counts, previous);
                assignments.Remove(vehicleId);
            }

            if (buildingId == 0)
            {
                return true;
            }

            KnownBurningBuildings.Add(buildingId);

            if (!IsCapped(buildingId))
            {
                int uncappedCurrent;
                counts.TryGetValue(buildingId, out uncappedCurrent);
                counts[buildingId] = uncappedCurrent + 1;
                assignments[vehicleId] = buildingId;
                return true;
            }

            int current;
            counts.TryGetValue(buildingId, out current);
            if (current >= MaxRespondersPerBuilding)
            {
                return false;
            }

            counts[buildingId] = current + 1;
            assignments[vehicleId] = buildingId;
            return true;
        }

        // False once a building has been continuously on fire for >= 15 minutes - at that
        // point the cap is lifted for it entirely.
        private static bool IsCapped(ushort buildingId)
        {
            float intensity = GetFireIntensity(buildingId);
            if (intensity <= 0f)
            {
                FireStartTime.Remove(buildingId);
                return true;
            }

            float startTime;
            if (!FireStartTime.TryGetValue(buildingId, out startTime))
            {
                FireStartTime[buildingId] = Time.realtimeSinceStartup;
                return true;
            }

            return Time.realtimeSinceStartup - startTime < UnlimitedAfterSeconds;
        }

        private static float GetFireIntensity(ushort buildingId)
        {
            return Singleton<BuildingManager>.instance.m_buildings.m_buffer[buildingId].m_fireIntensity;
        }

        // Searches the known-burning-building pool for one other than excludeBuilding that is
        // still actually on fire (live check, not trusting the pool alone) and still has
        // capacity for this vehicle type (or has passed the 15-minute uncapped threshold).
        // Returns 0 if nothing suitable is found - caller should fall back to idling.
        //
        // REVISED (2026-08-14): now picks the CLOSEST valid candidate to fromPosition instead of
        // just the first one found in the pool's (arbitrary hash-set) iteration order - "距離" is
        // the entire point of retargeting an idle vehicle rather than letting it idle/return, per
        // Smarter Firefighters' own stated rationale (a truck finishing a job should look for
        // fires *nearby*, not get sent clear across the map). Used both for cap-overflow
        // redirects and, since this same call, for genuinely idle vehicles - see
        // FireResponseCapPatch's targetBuilding == 0 branch.
        public static ushort TryFindAlternateBurningBuilding(bool isCopter, ushort excludeBuilding, Vector3 fromPosition)
        {
            Dictionary<ushort, int> counts = isCopter ? CopterResponseCount : TruckResponseCount;
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            ushort best = 0;
            float bestDistSqr = float.MaxValue;

            foreach (ushort candidate in KnownBurningBuildings)
            {
                if (candidate == 0 || candidate == excludeBuilding)
                {
                    continue;
                }

                if (GetFireIntensity(candidate) <= 0f)
                {
                    continue;
                }

                if (!IsCapped(candidate))
                {
                    // Already past the 15-minute uncapped threshold - always eligible, but still
                    // compete on distance against other eligible candidates below.
                }
                else
                {
                    int current;
                    counts.TryGetValue(candidate, out current);
                    if (current >= MaxRespondersPerBuilding)
                    {
                        continue;
                    }
                }

                float distSqr = (buildings[candidate].m_position - fromPosition).sqrMagnitude;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = candidate;
                }
            }

            return best;
        }

        // Call when a vehicle despawns, so counts don't leak upward forever.
        public static void ReleaseVehicle(ushort vehicleId)
        {
            ReleaseFrom(TruckResponseCount, TruckAssignment, vehicleId);
            ReleaseFrom(CopterResponseCount, CopterAssignment, vehicleId);
        }

        private static void ReleaseFrom(Dictionary<ushort, int> counts, Dictionary<ushort, ushort> assignments, ushort vehicleId)
        {
            ushort building;
            if (assignments.TryGetValue(vehicleId, out building))
            {
                Decrement(counts, building);
                assignments.Remove(vehicleId);
            }
        }

        private static void Decrement(Dictionary<ushort, int> counts, ushort buildingId)
        {
            int count;
            if (!counts.TryGetValue(buildingId, out count))
            {
                return;
            }

            if (count <= 1)
            {
                counts.Remove(buildingId);
            }
            else
            {
                counts[buildingId] = count - 1;
            }
        }
    }
}
