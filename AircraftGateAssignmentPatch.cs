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

        private static bool loggedFirstCall;

        public static void Prefix(ushort vehicleID, AircraftAI __instance, ref Vector3 endPos)
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
                return;
            }

            AirTrafficControlManager.AssignGate(vehicleID, bestSegment);
            endPos = bestPos;

            Debug.Log("[AIImprove] Aircraft " + vehicleID + " assigned gate segment " + bestSegment + " (occupancy was " + bestOccupancy + ").");
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
