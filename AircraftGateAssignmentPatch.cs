using System.Collections.Generic;
using ColossalFramework;
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
        // INTERIM VALUES (2026-08-12, revised): live screenshot showed dozens of aircraft
        // piled up nose-to-tail at a single taxiway junction instead of spreading across the
        // airport's actual gate/stand capacity. With only 8 candidate points on a tight 40m
        // radius, many candidates were resolving to the same handful of segments (or missing
        // real stands further out entirely), so "least occupied of 8" kept picking from a small
        // pool instead of using the whole airport. Widened both knobs so the search actually
        // reaches distinct stands around a large airport footprint.
        // TUNED (2026-08-13, +10% per request): CandidateCount 24->26, both search radii and
        // SaturationThreshold also raised ~10% - widening the search a bit further and giving a
        // bit more headroom before treating the airport as saturated.
        private const int CandidateCount = 26;
        private const float ProbeMaxDistance = 24f;

        // Two concentric rings instead of one - a single ring at a fixed radius tends to skim
        // past an airport's actual stand layout (too tight and it only reaches taxiways right at
        // the terminal; too wide and it misses close-in stands). Splitting candidates across an
        // inner and outer ring covers both without doubling the per-search cost.
        private static readonly float[] SearchRadii = { 66f, 132f };

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
        // TUNED (2026-08-13, +10% again per request): 44 -> 48.
        private const int SaturationThreshold = 48;

        private static bool loggedFirstCall;

        // HoldingPatternPatch.EndHoldingAndLand invokes this same StartPathFind (via reflection)
        // to force a real landing after a hold times out. Harmony detours the method in place,
        // so that reflected call re-enters this exact Prefix - and because the airport is almost
        // always still saturated at that point (that's *why* the plane was holding), it just hit
        // the same saturation branch again and either re-entered holding or failed outright,
        // instead of ever actually landing. Root cause of the "785 entered holding, only 26 ever
        // exited, 100% of those failed" live-test result. A vehicle listed here bypasses the
        // saturation check entirely and is forced onto whatever candidate gate is least occupied,
        // guaranteeing the timeout path always actually lands the plane somewhere.
        private static readonly HashSet<ushort> ForceAssign = new HashSet<ushort>();

        public static void BeginForceAssign(ushort vehicleId)
        {
            ForceAssign.Add(vehicleId);
        }

        public static void EndForceAssign(ushort vehicleId)
        {
            ForceAssign.Remove(vehicleId);
        }

        // Extracted so HoldingPatternPatch can periodically re-run the exact same search while
        // a plane is holding, to find out whether it's actually worth trying to land yet -
        // rather than only ever exiting a hold via the timeout.
        public static bool TryFindBestGate(VehicleInfo info, Vector3 airportCenter, out Vector3 bestPos, out ushort bestSegment, out int bestOccupancy)
        {
            bestPos = airportCenter;
            bestSegment = 0;
            bestOccupancy = int.MaxValue;
            bool found = false;
            var seenSegments = new HashSet<ushort>();

            foreach (float searchRadius in SearchRadii)
            {
                for (int i = 0; i < CandidateCount; i++)
                {
                    float angle = i * (360f / CandidateCount) * Mathf.Deg2Rad;
                    Vector3 candidate = airportCenter + new Vector3(Mathf.Cos(angle) * searchRadius, 0f, Mathf.Sin(angle) * searchRadius);

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
                            return true;
                        }
                    }
                }
            }

            return found;
        }

        public static bool IsSaturated(int occupancy)
        {
            return occupancy >= SaturationThreshold;
        }

        // Gate assignment / holding only make sense when this StartPathFind call is choosing
        // where to land AT an airport. AircraftAI.StartPathFind is also called for the opposite
        // leg - taking off and heading out to an outside connection to leave the map entirely -
        // and that endPos is nowhere near an airport at all. Before this check existed, every
        // departing flight got run through the same gate search anyway; virtually all of them
        // converge on the same one or two outside-connection segments at the map edge, so that
        // segment's tracked occupancy looked permanently "saturated" and departing aircraft were
        // wrongly shoved into a holding pattern meant only for planes trying to land. Root cause
        // of the "planes leaving the city are circling instead of departing" bug report
        // (2026-08-12).
        private static bool IsAirportBuilding(ushort buildingId)
        {
            if (buildingId == 0)
            {
                return false;
            }

            BuildingInfo info = Singleton<BuildingManager>.instance.m_buildings.m_buffer[buildingId].Info;
            return info != null
                && info.m_class.m_service == ItemClass.Service.PublicTransport
                && info.m_class.m_subService == ItemClass.SubService.PublicTransportPlane;
        }

        // Which of m_sourceBuilding/m_targetBuilding is actually this leg's destination.
        // REVISED (2026-08-12): originally picked via the Vehicle.Flags.GoingBack flag, on the
        // same source/target round-trip assumption used elsewhere in this project for ground
        // emergency vehicles. Live test showed that assumption doesn't hold for AircraftAI - only
        // 1 gate assignment fired in an 11-minute session on a 200+-flight airport, meaning the
        // flag-based guess was resolving to the wrong building (or 0) for nearly every real
        // landing call, not just departures. Determining the destination by whichever building's
        // actual position matches endPos is immune to whatever AircraftAI's real flag convention
        // turns out to be.
        private static ushort ResolveDestinationBuilding(ref Vehicle vehicleData, Vector3 endPos)
        {
            ushort target = vehicleData.m_targetBuilding;
            ushort source = vehicleData.m_sourceBuilding;

            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            float targetDistSqr = target != 0 ? (buildings[target].m_position - endPos).sqrMagnitude : float.MaxValue;
            float sourceDistSqr = source != 0 ? (buildings[source].m_position - endPos).sqrMagnitude : float.MaxValue;

            return targetDistSqr <= sourceDistSqr ? target : source;
        }

        // On saturation, instead of returning false (which the caller turns into an
        // Unspawn - the plane just vanishes), this now enters a real holding pattern -
        // see HoldingPatternManager.cs. Requires `ref Vehicle vehicleData` (not part of the
        // original gate-search logic, added specifically to set flight state directly when
        // entering holding) - Harmony matches it by name against StartPathFind's own
        // `vehicleData` parameter.
        public static bool Prefix(ushort vehicleID, AircraftAI __instance, ref Vehicle vehicleData, ref Vector3 endPos, ref bool __result)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] AircraftGateAssignmentPatch is executing.");
            }

            // "Close any airports" during a thunderstorm (per user request, 2026-08-13): checked
            // against both source and target so it catches this leg regardless of direction -
            // landing (target is the airport) or departing (source is the airport, target is an
            // outside connection and would otherwise skip the check below entirely). Refusing
            // here reuses the same "return false, __result stays false" refusal path as ordinary
            // saturation - the caller Unspawns the vehicle either way.
            if (WeatherDisasterDetector.IsThunderstormActive() &&
                (IsAirportBuilding(vehicleData.m_sourceBuilding) || IsAirportBuilding(vehicleData.m_targetBuilding)))
            {
                Debug.Log("[AIImprove] Aircraft " + vehicleID + " refused - airport closed for thunderstorm.");
                return false;
            }

            ushort targetBuilding = ResolveDestinationBuilding(ref vehicleData, endPos);

            if (!IsAirportBuilding(targetBuilding))
            {
                // Not landing at an airport this leg (e.g. departing to an outside connection) -
                // let vanilla handle it untouched, no gate search, no saturation/holding logic.
                return true;
            }

            VehicleInfo info = __instance.m_info;
            Vector3 originalEndPos = endPos;

            Vector3 bestPos;
            ushort bestSegment;
            int bestOccupancy;
            bool found = TryFindBestGate(info, originalEndPos, out bestPos, out bestSegment, out bestOccupancy);

            if (!found)
            {
                return true;
            }

            if (IsSaturated(bestOccupancy))
            {
                // REVERTED (2026-08-13): saturation used to put the plane into a real, visible
                // holding pattern (HoldingPatternManager/HoldingPatternPatch) instead of refusing
                // the landing outright. Two things changed that: (1) live logs showed a real,
                // non-fatal vanilla bug this exposed - PassengerPlaneAI.SimulationStep reads
                // vehicleData.m_targetBuilding as a NetManager *node* index (not a building index)
                // whenever GoingBack/DummyTraffic aren't set, and restoring a real building ID
                // into that field while holding (in EndHoldingAndLand) left the vehicle in exactly
                // that state for the following tick - "Array index is out of range" whenever that
                // building ID happened to exceed the map's node buffer size; (2) per explicit user
                // request, cutting the affected planes instead of keeping them alive and circling
                // also reduces the live vehicle-model count during saturation, which was
                // contributing to lag on top of the crash risk. Reverted to the original refusal
                // behavior: return false with __result left false, which the caller (AircraftAI's
                // own StartPathFind wrapper) turns into an Unspawn - the plane simply vanishes
                // instead of entering holding. HoldingPatternManager/HoldingPatternPatch are kept
                // in the codebase (unused) as a record, same convention as this project's other
                // deprecated patches - nothing calls BeginHolding anymore, so IsHolding always
                // returns false and TryUpdateHolding's own call site is a cheap no-op.
                Debug.Log(
                    "[AIImprove] Aircraft " + vehicleID + " refused landing - airport saturated " +
                    "(best candidate gate occupancy " + bestOccupancy + " >= " + SaturationThreshold +
                    "), vehicle will unspawn.");
                return false;
            }

            AirTrafficControlManager.AssignGate(vehicleID, bestSegment);
            endPos = bestPos;

            Debug.Log(
                "[AIImprove] Aircraft " + vehicleID + " assigned gate segment " + bestSegment +
                " (occupancy was " + bestOccupancy + (ForceAssign.Contains(vehicleID) ? ", forced after hold timeout" : "") + ").");
            return true;
        }
    }

    // Frees per-vehicle tracking state when any vehicle despawns, so counts don't leak upward
    // forever. Patched on VehicleAI (the actual declaring type, not AircraftAI - see
    // Patcher.cs) so this fires for every vehicle type; each tracker's ReleaseVehicle is a
    // no-op for vehicle IDs it never assigned anything to.
    internal static class AircraftReleasePatch
    {
        public static void Postfix(ushort vehicleID)
        {
            AirTrafficControlManager.ReleaseVehicle(vehicleID);
            HoldingPatternManager.EndHolding(vehicleID);
            FireResponseTracker.ReleaseVehicle(vehicleID);
        }
    }
}
