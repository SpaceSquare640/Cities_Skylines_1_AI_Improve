using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "直升機埠/登機點分散" (2026-08-14): occupancy-aware landing-point assignment for passenger
    // helicopters, same idea as AircraftGateAssignmentPatch/TrainPlatformAssignmentPatch. Unlike
    // those, PassengerHelicopterAI is a genuinely different vehicle class (VehicleAI directly, not
    // HelicopterAI - the emergency copters' base) that really does call
    // PathManager.FindPathPosition/CreatePath, confirmed via dnSpy - it's not a "fly direct, no
    // real path" vehicle like AmbulanceCopterAI/FireCopterAI/PoliceCopterAI.
    //
    // Also unlike trains/aircraft, this vehicle's own StartPathFind(ushort, ref Vehicle) resolves
    // vehicleData.m_targetBuilding as a NetManager *node* ID for a normal leg (same as BusAI - see
    // FlexibleReroutePatch's notes on that), not a Building ID - so there's no reliable building
    // reference to search "around" the way TrainPlatformAssignmentPatch/AircraftGateAssignmentPatch
    // do. Instead this Prefixes the 7-arg StartPathFind directly and searches candidates around
    // whatever endPos vanilla's own 2-arg convenience method already resolved (a real lane
    // position either way, landing or departing) - no building lookup needed. For a genuinely
    // single-pad departure point this just finds the same lane vanilla would have anyway (nothing
    // else to compare against); for a busy multi-pad heliport building it actually spreads arrivals
    // across more of the pad capacity.
    internal static class PassengerHelicopterGateAssignmentPatch
    {
        private const int CandidateCount = 16;
        private static readonly float[] SearchRadii = { 40f, 80f };
        private const float ProbeMaxDistance = 32f;

        private static bool loggedFirstCall;

        public static bool Prefix(ushort vehicleID, PassengerHelicopterAI __instance, ref Vector3 endPos)
        {
            if (!ModSettings.PassengerHelicopterGateAssignmentEnabled.value)
            {
                return true;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] PassengerHelicopterGateAssignmentPatch is executing.");
            }

            VehicleInfo info = __instance.m_info;
            Vector3 originalEndPos = endPos;

            Vector3 bestPos = originalEndPos;
            ushort bestSegment = 0;
            int bestOccupancy = int.MaxValue;
            bool found = false;
            var seenSegments = new HashSet<ushort>();

            foreach (float searchRadius in SearchRadii)
            {
                for (int i = 0; i < CandidateCount; i++)
                {
                    float angle = i * (360f / CandidateCount) * Mathf.Deg2Rad;
                    Vector3 candidate = originalEndPos + new Vector3(Mathf.Cos(angle) * searchRadius, 0f, Mathf.Sin(angle) * searchRadius);

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

                    if (!resolved || !seenSegments.Add(position.m_segment))
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
                            goto searchDone;
                        }
                    }
                }
            }

            searchDone:
            if (!found)
            {
                return true;
            }

            AirTrafficControlManager.AssignGate(vehicleID, bestSegment);
            endPos = bestPos;

            // Same unconditional-Debug.Log-on-a-hot-path bug as HelicopterWeatherHaltPatch, found
            // in the same 2026-08-16 audit - one line per landing, ignoring the Verbose gate.
            Log.Verbose("[AIImprove] Passenger helicopter " + vehicleID + " assigned landing segment " + bestSegment + " (occupancy was " + bestOccupancy + ").");
            return true;
        }
    }
}
