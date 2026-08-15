using ColossalFramework;
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
    //
    // REVISED (2026-08-13), mirroring the aircraft fixes from the same day:
    //  - StartPathFind is called for both arriving AT a station and departing to an outside
    //    connection. The old code ran the same platform search either way with no way to tell
    //    them apart, so a departing train's endPos (an outside connection, whose track segment
    //    every departing train on that connection converges on) could get treated as just
    //    another "platform" candidate and pulled into this logic pointlessly. Now resolves the
    //    real destination building by matching endPos against source/target position (position-
    //    based, not a GoingBack-flag guess - see AircraftGateAssignmentPatch's notes on why that
    //    guess turned out wrong) and skips entirely unless it's a real station, not an outside
    //    connection.
    //  - Widened from a single 30m/8-candidate ring to two rings (matching the aircraft gate
    //    search), so a busy multi-platform station actually gets spread across more of its real
    //    track capacity instead of a small nearby sample.
    //  - Added a saturation threshold: if every candidate is heavily occupied, this now leaves
    //    endPos untouched (same as the !found case) instead of actively directing the train into
    //    the least-bad but still-jammed option. Trains don't have the aircraft's despawn risk on
    //    a rejected path (real track/signal blocking already queues them safely), so no
    //    holding-pattern equivalent is needed - just not making the jam worse by picking for it.
    internal static class TrainPlatformAssignmentPatch
    {
        private static int CandidateCount => ModSettings.TrainPlatformCandidateCount.value;
        private static readonly float[] SearchRadii = { 30f, 60f };
        private const float ProbeMaxDistance = 32f; // matches TrainAI's own FindPathPosition call

        // Same rationale as AircraftGateAssignmentPatch.SaturationThreshold - "every nearby
        // candidate already has this many trains assigned" is treated as saturated. This also
        // gates TrainSpawnThrottlePatch (via IsStationLikelySaturated), so it's the actual lever
        // for "城際火車入城流量限制" - lowering it makes stations count as saturated sooner, both
        // refusing to force a train onto an already-busy platform and skipping new incoming
        // intercity train spawns toward that station.
        //
        // TUNED (2026-08-14, tightened per request): 40 -> 25.
        // "每個功能中的調整設定及數據可以拆開以及詳細調整" (2026-08-15): now a slider
        // (ModSettings.TrainStationSaturationThreshold), default still 25.
        private static int SaturationThreshold => ModSettings.TrainStationSaturationThreshold.value;

        private static bool loggedFirstCall;

        // Records the most recent saturation reading per station, taken as a side effect of this
        // Prefix's own real candidate search below - not a separate check. TrainSpawnThrottlePatch
        // reads this to decide whether OutsideConnectionAI should even bother spawning a new
        // incoming intercity train, without needing to duplicate the (relatively expensive)
        // FindPathPosition candidate search itself, and without needing a VehicleInfo/TransportInfo
        // at spawn time (StartTransfer doesn't have one yet - the vehicle prefab is picked inside
        // the very call this would need to run before). Absence of an entry (a station nothing has
        // recently pathed to) defaults to "not saturated" in the reader - fails open, same
        // philosophy as every occupancy estimate elsewhere in this project.
        private static readonly System.Collections.Generic.Dictionary<ushort, bool> StationSaturated =
            new System.Collections.Generic.Dictionary<ushort, bool>();

        public static bool IsStationLikelySaturated(ushort stationBuildingId)
        {
            bool saturated;
            return StationSaturated.TryGetValue(stationBuildingId, out saturated) && saturated;
        }

        private static bool IsRealStation(ushort buildingId)
        {
            if (buildingId == 0)
            {
                return false;
            }

            BuildingInfo info = Singleton<BuildingManager>.instance.m_buildings.m_buffer[buildingId].Info;
            return info != null && !(info.m_buildingAI is OutsideConnectionAI);
        }

        // Which of m_sourceBuilding/m_targetBuilding is actually this leg's destination,
        // determined by matching endPos against each building's real position rather than
        // guessing from Vehicle.Flags.GoingBack - see AircraftGateAssignmentPatch's
        // ResolveDestinationBuilding for why the flag-based guess isn't reliable here.
        private static ushort ResolveDestinationBuilding(ref Vehicle vehicleData, Vector3 endPos)
        {
            ushort target = vehicleData.m_targetBuilding;
            ushort source = vehicleData.m_sourceBuilding;

            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            float targetDistSqr = target != 0 ? (buildings[target].m_position - endPos).sqrMagnitude : float.MaxValue;
            float sourceDistSqr = source != 0 ? (buildings[source].m_position - endPos).sqrMagnitude : float.MaxValue;

            return targetDistSqr <= sourceDistSqr ? target : source;
        }

        public static void Prefix(ushort vehicleID, TrainAI __instance, ref Vehicle vehicleData, ref Vector3 endPos)
        {
            // Metro and intercity trains got split into independent toggles (2026-08-15, per user
            // request) - both share this one method (declared on TrainAI, not overridden per
            // subtype), so the instance's actual runtime type decides which setting applies.
            // Anything that's neither (e.g. cargo trains) falls back to the metro switch.
            bool categoryEnabled = __instance is MetroTrainAI
                ? ModSettings.MetroPlatformAssignmentEnabled.value
                : (__instance is PassengerTrainAI
                    ? ModSettings.IntercityTrainPlatformAssignmentEnabled.value
                    : ModSettings.MetroPlatformAssignmentEnabled.value);
            if (!categoryEnabled)
            {
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] TrainPlatformAssignmentPatch is executing.");
            }

            // BUG FOUND VIA PLAYER REPORT (2026-08-14): "地鐵無法移動" (metro cannot move), still
            // present after the stop-skipping patch was disabled, so a separate cause. Root cause
            // is a type confusion in this patch: Vehicle.m_targetBuilding is only a Building index
            // on *some* legs. Confirmed in PassengerTrainAI.StartPathFind (which MetroTrainAI
            // inherits): for a train running a transport line - i.e. neither GoingBack nor
            // DummyTraffic - vanilla reads it as a NetManager **node** index
            // (`m_nodes.m_buffer[m_targetBuilding].m_position`), because a line's stop is a net
            // node, not a building. ResolveDestinationBuilding below indexes the *buildings*
            // buffer with that value, so it silently reads an unrelated building's position, and
            // IsRealStation then judges that unrelated building. Node ids stay well inside the
            // building buffer's range, so this never threw - it just quietly let the patch run on
            // garbage and overwrite endPos, moving the destination off the real stop node.
            //
            // Intercity trains were unaffected (they carry DummyTraffic, where m_targetBuilding
            // really is a Building), which is exactly why this went unnoticed - the patch was
            // developed and tested against them.
            //
            // m_transportLine != 0 is the definitive discriminator: a vehicle running a player
            // transport line is precisely the case where m_targetBuilding means a node. That also
            // naturally excludes all metro, which always runs on a line. Platform spreading was
            // only ever asked for at intercity/regional stations anyway.
            if (vehicleData.m_transportLine != 0)
            {
                return;
            }

            ushort targetBuilding = ResolveDestinationBuilding(ref vehicleData, endPos);
            if (!IsRealStation(targetBuilding))
            {
                // Not arriving at a real station this leg (e.g. departing to an outside
                // connection) - leave vanilla's own endPos untouched.
                return;
            }

            VehicleInfo info = __instance.m_info;
            TransportInfo transportInfo = __instance.m_transportInfo;
            Vector3 originalEndPos = endPos;

            Vector3 bestPos = originalEndPos;
            ushort bestSegment = 0;
            int bestOccupancy = int.MaxValue;
            bool found = false;
            var seenSegments = new System.Collections.Generic.HashSet<ushort>();

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

            bool saturated = bestOccupancy >= SaturationThreshold;
            StationSaturated[targetBuilding] = saturated;

            if (saturated)
            {
                if (Log.VerboseEnabled)
                {
                    Log.Verbose(
                        "[AIImprove] Train " + vehicleID + " approaching saturated station (best candidate " +
                        "occupancy " + bestOccupancy + " >= " + SaturationThreshold + ") - leaving vanilla " +
                        "platform choice untouched instead of forcing it into the least-bad option.");
                }

                return;
            }

            AirTrafficControlManager.AssignGate(vehicleID, bestSegment);
            endPos = bestPos;

            if (Log.VerboseEnabled)
            {
                Log.Verbose(
                    "[AIImprove] Train " + vehicleID + " assigned platform segment " + bestSegment +
                    " (occupancy was " + bestOccupancy + ").");
            }
        }
    }
}
