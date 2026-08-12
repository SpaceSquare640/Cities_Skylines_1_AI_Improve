using UnityEngine;

namespace AIImprove
{
    // Real ATC-style gate assignment for AircraftAI.StartPathFind, replacing the blind jitter
    // PlatformGateJitterPatch still uses for trains. Probes several candidate points around the
    // airport building's center, resolves each to a real gate lane via the same
    // PathManager.FindPathPosition the game itself uses, and picks whichever candidate's lane
    // currently has the fewest aircraft assigned (per AirTrafficControlManager) - actively
    // avoiding occupied gates instead of just spreading vehicles out randomly.
    //
    // Prefix, same reasoning as PlatformGateJitterPatch: endPos is a plain Vector3 parameter on
    // this overload, not a ref struct, so a Prefix can safely rewrite it before the original
    // method (and its own FindPathPosition call) runs.
    internal static class AircraftGateAssignmentPatch
    {
        private const int CandidateCount = 8;
        private const float SearchRadius = 40f;
        private const float ProbeMaxDistance = 16f; // matches AircraftAI's own FindPathPosition call

        // "Problem 3": if every candidate gate is at or above this occupancy, the airport is
        // treated as saturated and the landing is refused (see below) rather than piling the
        // plane in anyway.
        //
        // INTERIM VALUE (2026-08-12): first live test at threshold=8 refused 1889/2332 landings
        // (81%) - far too aggressive, planes were vanishing constantly instead of this being a
        // rare "truly jammed" fallback. Raised substantially as a stopgap. The real fix is a
        // proper holding-pattern system (queue + periodic gate re-check + a real place for the
        // plane to loiter) instead of an outright refusal that despawns the plane - deferred,
        // see Cities_Skylines_1_AI_Improve_Document/01 "未來規劃：真正的盤旋等待 ATC".
        private const int SaturationThreshold = 40;

        private static bool loggedFirstCall;

        // Returning false skips AircraftAI's own StartPathFind body entirely and forces its
        // result to whatever __result is set to (Harmony convention for boolean Prefixes).
        // Callers (SetTarget etc.) already handle a false StartPathFind result by calling
        // data.Unspawn(vehicleID) - the same vanilla path used for "no path found" - so refusing
        // a landing here is not a new failure mode, just reusing an existing, well-exercised one.
        public static bool Prefix(ushort vehicleID, AircraftAI __instance, ref Vector3 endPos, ref bool __result)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] AircraftGateAssignmentPatch is executing.");
            }

            VehicleInfo info = __instance.m_info;
            Vector3 originalEndPos = endPos;

            Vector3 bestPos = originalEndPos;
            ushort bestSegment = 0;
            int bestOccupancy = int.MaxValue;
            bool found = false;

            for (int i = 0; i < CandidateCount; i++)
            {
                float angle = i * (360f / CandidateCount) * Mathf.Deg2Rad;
                Vector3 candidate = originalEndPos + new Vector3(Mathf.Cos(angle) * SearchRadius, 0f, Mathf.Sin(angle) * SearchRadius);

                PathUnit.Position position;
                PathUnit.Position position2;
                float distance;
                float distance2;
                bool resolved = PathManager.FindPathPosition(
                    candidate,
                    ItemClass.Service.PublicTransport,
                    NetInfo.LaneType.Vehicle,
                    info.m_vehicleType,
                    info.vehicleCategory,
                    false,
                    false,
                    ProbeMaxDistance,
                    false,
                    false,
                    out position,
                    out position2,
                    out distance,
                    out distance2);

                if (!resolved)
                {
                    continue;
                }

                int occupancy = AirTrafficControlManager.GetOccupancy(position.m_segment);
                if (occupancy < bestOccupancy)
                {
                    bestOccupancy = occupancy;
                    bestPos = candidate;
                    bestSegment = position.m_segment;
                    found = true;

                    if (occupancy == 0)
                    {
                        break;
                    }
                }
            }

            if (!found)
            {
                return true;
            }

            if (bestOccupancy >= SaturationThreshold)
            {
                Debug.Log(
                    "[AIImprove] Aircraft " + vehicleID + " refused landing - airport saturated " +
                    "(best candidate gate occupancy " + bestOccupancy + " >= " + SaturationThreshold + ").");
                __result = false;
                return false;
            }

            AirTrafficControlManager.AssignGate(vehicleID, bestSegment);
            endPos = bestPos;

            Debug.Log("[AIImprove] Aircraft " + vehicleID + " assigned gate segment " + bestSegment + " (occupancy was " + bestOccupancy + ").");
            return true;
        }
    }

    // Frees the gate assignment when a plane despawns, so occupancy counts don't leak upward
    // forever. AircraftAI.ReleaseVehicle has a single `ref Vehicle` parameter - safe to Postfix.
    internal static class AircraftReleasePatch
    {
        public static void Postfix(ushort vehicleID)
        {
            AirTrafficControlManager.ReleaseVehicle(vehicleID);
        }
    }
}
