using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // Cheap, one-shot road congestion sample used to inform citizen trip-mode decisions (see
    // CitizenCarProbabilityPatch.cs). Unlike SegmentCongestionQuery (which walks several
    // positions along a vehicle's already-chosen path), a citizen deciding whether to drive
    // hasn't pathed yet - there's nothing to walk ahead along. This just checks the nearest road
    // segment to a given point (typically the citizen's destination) and reads its live
    // NetSegment.m_trafficDensity (0-100 scale), the same field every other congestion check in
    // this project already reads.
    internal static class CitizenCongestionQuery
    {
        private const float ProbeMaxDistance = 48f;

        // Returns -1f if no nearby road segment resolved (e.g. position is off the road network
        // entirely), matching the "no data" convention used elsewhere in this project.
        public static float GetNearbyRoadDensity(Vector3 position)
        {
            PathUnit.Position posA;
            PathUnit.Position posB;
            float distA;
            float distB;

            bool resolved = PathManager.FindPathPosition(
                position,
                ItemClass.Service.Road,
                NetInfo.LaneType.Vehicle,
                VehicleInfo.VehicleType.Car,
                VehicleInfo.VehicleCategory.None,
                false,
                false,
                ProbeMaxDistance,
                false,
                false,
                out posA,
                out posB,
                out distA,
                out distB);

            if (!resolved)
            {
                return -1f;
            }

            return Singleton<NetManager>.instance.m_segments.m_buffer[posA.m_segment].m_trafficDensity;
        }
    }
}
