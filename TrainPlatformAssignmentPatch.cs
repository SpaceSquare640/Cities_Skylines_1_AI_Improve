using UnityEngine;

namespace AIImprove
{
    // Occupancy-aware ATC-style platform assignment for TrainAI, same idea as
    // AircraftGateAssignmentPatch.cs (see that file for the full rationale) - upgraded from the
    // earlier blind-jitter PlatformGateJitterPatch after in-game testing showed random spreading
    // alone wasn't enough. Shares AirTrafficControlManager with the aircraft patch: segment IDs
    // are a single global namespace in this game, so one occupancy tracker safely covers both
    // trains and planes at once.
    //
    // Uses TrainAI's own FindPathPosition overload (train pathfinding takes m_netService /
    // m_secondaryNetService instead of a single ItemClass.Service like aircraft) so probed
    // candidates resolve to the same kind of lane the real pathfind would pick.
    internal static class TrainPlatformAssignmentPatch
    {
        private const int CandidateCount = 8;
        private const float SearchRadius = 30f;
        private const float ProbeMaxDistance = 32f; // matches TrainAI's own FindPathPosition call

        private static bool loggedFirstCall;

        public static void Prefix(ushort vehicleID, TrainAI __instance, ref Vector3 endPos)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] TrainPlatformAssignmentPatch is executing.");
            }

            VehicleInfo info = __instance.m_info;
            TransportInfo transportInfo = __instance.m_transportInfo;
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
                    transportInfo.m_netService,
                    transportInfo.m_secondaryNetService,
                    NetInfo.LaneType.Vehicle,
                    info.m_vehicleType,
                    info.vehicleCategory,
                    VehicleInfo.VehicleType.None,
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

            Debug.Log("[AIImprove] Train " + vehicleID + " assigned platform segment " + bestSegment + " (occupancy was " + bestOccupancy + ").");
        }
    }
}
