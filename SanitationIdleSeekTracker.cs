using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "垃圾車／殯儀車調度" (2026-08-17). Same idea as FireResponseTracker's idle-seek half, applied
    // to garbage trucks and hearses.
    //
    // dnSpy showed GarbageTruckAI/HearseAI already get mid-route congestion rerouting for free
    // (they're plain CarAI subtypes, covered by the shared FlexibleReroutePatch.Car registration) -
    // that part didn't need new work. The real gap is upstream of routing entirely: when a truck
    // finishes a job and has room for more, vanilla's own SetTarget doesn't look for nearby work
    // itself - it posts an AddIncomingOffer and waits for TransferManager.MatchOffers to pair it
    // with something, which (per the TransferManager research in
    // Cities_Skylines_1_AI_Improve_Document/10) is priority-bucket and distance-*weighted*, not a
    // literal nearest-first search. A truck can sit waiting for a match while a much closer
    // building with the same need goes unserved. This is the same class of problem the fire idle-
    // seek feature solves, using the same "search a self-maintained bucket, don't touch
    // MatchOffers" approach that avoided the risk of patching TransferManager directly.
    //
    // Generic over TransferManager.TransferReason (Garbage or Dead) rather than one tracker per
    // vehicle type, since the logic is identical - only the material differs.
    internal static class SanitationIdleSeekTracker
    {
        private static readonly HashSet<ushort> KnownGarbageBuildings = new HashSet<ushort>();
        private static readonly HashSet<ushort> KnownDeadBuildings = new HashSet<ushort>();
        private static readonly List<ushort> StaleBuildings = new List<ushort>();

        private static HashSet<ushort> SetFor(TransferManager.TransferReason material)
        {
            return material == TransferManager.TransferReason.Dead ? KnownDeadBuildings : KnownGarbageBuildings;
        }

        // Call whenever vanilla itself assigns a real (non-zero) target for this material - the
        // building has just proven it has the need, so it becomes a candidate for the next truck
        // that goes looking.
        public static void Observe(TransferManager.TransferReason material, ushort buildingId)
        {
            if (buildingId != 0)
            {
                SetFor(material).Add(buildingId);
            }
        }

        // Searches the known-buildings pool for the closest one to fromPosition that still
        // genuinely has the need (live GetMaterialAmount check, not trusting the pool alone) and
        // isn't already the vehicle's own current source/target. Returns 0 if nothing suitable is
        // found. Self-cleaning: buildings confirmed to no longer need the material are dropped
        // during the walk - see FireResponseTracker's TryFindAlternateBurningBuilding for the same
        // pattern and the reasoning ("skipping a stale entry is not the same as removing it").
        public static ushort TryFindNearby(TransferManager.TransferReason material, ushort excludeBuilding, Vector3 fromPosition)
        {
            HashSet<ushort> pool = SetFor(material);
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

            ushort best = 0;
            float bestDistSqr = float.MaxValue;

            StaleBuildings.Clear();

            foreach (ushort candidate in pool)
            {
                if (candidate == 0 || candidate == excludeBuilding)
                {
                    continue;
                }

                BuildingInfo info = buildings[candidate].Info;
                if (info == null || info.m_buildingAI == null)
                {
                    StaleBuildings.Add(candidate);
                    continue;
                }

                int amount;
                int max;
                info.m_buildingAI.GetMaterialAmount(candidate, ref buildings[candidate], material, out amount, out max);
                if (amount <= 0)
                {
                    StaleBuildings.Add(candidate);
                    continue;
                }

                float distSqr = (buildings[candidate].m_position - fromPosition).sqrMagnitude;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = candidate;
                }
            }

            for (int i = 0; i < StaleBuildings.Count; i++)
            {
                pool.Remove(StaleBuildings[i]);
            }

            StaleBuildings.Clear();
            return best;
        }
    }
}
