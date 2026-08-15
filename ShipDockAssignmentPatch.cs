using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "現在我們專注深入研究整個貨運系統" (2026-08-15/16): occupancy-aware dock assignment for
    // ShipAI, closing the one real gap found while researching the freight chain - see
    // Cities_Skylines_1_AI_Improve_Document/10.
    //
    // Passenger trains and passenger/cargo aircraft already got this treatment
    // (TrainPlatformAssignmentPatch.cs / AircraftGateAssignmentPatch.cs). Ships never did:
    // ShipAI (dnSpy-confirmed base of both CargoShipAI and any passenger ferry AI) has no
    // equivalent patch anywhere in this project, so every harbor's dock traffic has always been
    // whatever vanilla's own blind pathfind picks - no spreading, no saturation awareness.
    //
    // Reuses AirTrafficControlManager exactly as-is (segment IDs are one global namespace in this
    // game, same reasoning already applied to share it between trains and planes) and mirrors
    // TrainPlatformAssignmentPatch's structure almost verbatim: probe a ring of candidates around
    // the harbor, resolve each via the same PathManager.FindPathPosition call ShipAI.StartPathFind
    // itself uses (dnSpy-confirmed: ItemClass.Service.PublicTransport, NetInfo.LaneType.Vehicle),
    // pick the least-occupied resolved segment, and leave endPos untouched (not a forced failure)
    // when every candidate is already saturated - same "don't make the jam worse" philosophy as
    // trains, since ships have no aircraft-style despawn risk on a merely-suboptimal path.
    internal static class ShipDockAssignmentPatch
    {
        private static int CandidateCount => ModSettings.ShipDockCandidateCount.value;
        private static readonly float[] SearchRadii = { 60f, 120f };
        private const float ProbeMaxDistance = 64f; // matches ShipAI's own FindPathPosition call

        private static int SaturationThreshold => ModSettings.ShipDockSaturationThreshold.value;

        private static bool loggedFirstCall;

        private static bool IsHarborBuilding(ushort buildingId)
        {
            if (buildingId == 0)
            {
                return false;
            }

            BuildingInfo info = Singleton<BuildingManager>.instance.m_buildings.m_buffer[buildingId].Info;
            return info != null && !(info.m_buildingAI is OutsideConnectionAI);
        }

        // Same position-based resolution as TrainPlatformAssignmentPatch/AircraftGateAssignmentPatch
        // - see either for why a Vehicle.Flags.GoingBack guess isn't reliable here.
        private static ushort ResolveDestinationBuilding(ref Vehicle vehicleData, Vector3 endPos)
        {
            ushort target = vehicleData.m_targetBuilding;
            ushort source = vehicleData.m_sourceBuilding;

            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            float targetDistSqr = target != 0 ? (buildings[target].m_position - endPos).sqrMagnitude : float.MaxValue;
            float sourceDistSqr = source != 0 ? (buildings[source].m_position - endPos).sqrMagnitude : float.MaxValue;

            return targetDistSqr <= sourceDistSqr ? target : source;
        }

        public static void Prefix(ushort vehicleID, ShipAI __instance, ref Vehicle vehicleData, ref Vector3 endPos)
        {
            if (!ModSettings.ShipDockAssignmentEnabled.value)
            {
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] ShipDockAssignmentPatch is executing.");
            }

            // Same m_transportLine type-confusion guard as TrainPlatformAssignmentPatch - a
            // vehicle running a player transport line (a passenger ferry route) reads
            // m_targetBuilding as a net node, not a building, on that leg.
            if (vehicleData.m_transportLine != 0)
            {
                return;
            }

            ushort targetBuilding = ResolveDestinationBuilding(ref vehicleData, endPos);
            if (!IsHarborBuilding(targetBuilding))
            {
                // Not arriving at a real harbor this leg (e.g. departing to an outside
                // connection out to sea) - leave vanilla's own endPos untouched.
                return;
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
                return;
            }

            if (bestOccupancy >= SaturationThreshold)
            {
                if (Log.VerboseEnabled)
                {
                    Log.Verbose(
                        "[AIImprove] Ship " + vehicleID + " approaching saturated harbor (best candidate " +
                        "occupancy " + bestOccupancy + " >= " + SaturationThreshold + ") - leaving vanilla " +
                        "dock choice untouched instead of forcing it into the least-bad option.");
                }

                return;
            }

            AirTrafficControlManager.AssignGate(vehicleID, bestSegment);
            endPos = bestPos;

            if (Log.VerboseEnabled)
            {
                Log.Verbose(
                    "[AIImprove] Ship " + vehicleID + " assigned dock segment " + bestSegment +
                    " (occupancy was " + bestOccupancy + ").");
            }
        }
    }
}
