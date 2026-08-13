using System.Collections.Generic;

namespace AIImprove
{
    // Minimal "virtual ATC": the game has no gate-occupancy concept at all (see
    // PlatformGateJitterPatch.cs for the root cause), so this tracks it ourselves. Keyed by
    // NetSegment ID (the gate lane FindPathPosition resolves to), not by a proper "Gate" entity
    // the game doesn't have.
    //
    // Scope (2026-08-12, per user request to build this as an actual ATC rather than the earlier
    // blind-jitter workaround): real-time occupancy-aware gate assignment for planes. Does NOT
    // do landing-acceptance throttling ("problem 3" - refusing new landings when the airport is
    // already saturated) or taxiway sequencing - those need hooking into flight
    // creation/acceptance, a different and larger problem, deferred (see
    // Cities_Skylines_1_AI_Improve_Document/01).
    //
    // REVISED (2026-08-14): added optional per-BUILDING aggregate tracking alongside the existing
    // per-segment tracking, for "機場部分需要重新處理...要跟據每一個機場進行流量分配" - a map with
    // multiple airports at different locations was seeing one airport go completely dead, because
    // saturation was being decided against a single segment's occupancy compared to one flat
    // global constant that took no account of how many real gates a given airport actually has.
    // A small airport's one or two gates could hit that constant from light traffic alone, while a
    // big hub's load spread thinly across many segments never would - see
    // AircraftGateAssignmentPatch's notes on how the building-level aggregate is now used to scale
    // the saturation threshold per airport instead of applying one number to every airport on the
    // map. buildingId is optional (0 = not tracked) so this stays backward compatible for callers
    // (trains, passenger helicopters) that only care about per-segment spreading, not a
    // per-building saturation decision.
    internal static class AirTrafficControlManager
    {
        private static readonly Dictionary<ushort, int> GateOccupancy = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, ushort> VehicleGateAssignment = new Dictionary<ushort, ushort>();
        private static readonly Dictionary<ushort, int> BuildingOccupancy = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, ushort> VehicleBuildingAssignment = new Dictionary<ushort, ushort>();
        private static readonly object Lock = new object();

        public static int GetOccupancy(ushort segmentId)
        {
            lock (Lock)
            {
                int count;
                return GateOccupancy.TryGetValue(segmentId, out count) ? count : 0;
            }
        }

        // How many vehicles are currently assigned to ANY gate belonging to buildingId - the
        // whole-airport aggregate, not one segment's count.
        public static int GetBuildingOccupancy(ushort buildingId)
        {
            lock (Lock)
            {
                int count;
                return buildingId != 0 && BuildingOccupancy.TryGetValue(buildingId, out count) ? count : 0;
            }
        }

        // Moves vehicleId's assignment to segmentId (and, if provided, buildingId), freeing
        // whatever it was previously assigned to (if anything). Safe to call repeatedly for the
        // same vehicle across re-paths.
        public static void AssignGate(ushort vehicleId, ushort segmentId, ushort buildingId = 0)
        {
            lock (Lock)
            {
                ushort previousSegment;
                if (VehicleGateAssignment.TryGetValue(vehicleId, out previousSegment))
                {
                    if (previousSegment != segmentId)
                    {
                        Decrement(GateOccupancy, previousSegment);
                        VehicleGateAssignment[vehicleId] = segmentId;
                        Increment(GateOccupancy, segmentId);
                    }
                }
                else
                {
                    VehicleGateAssignment[vehicleId] = segmentId;
                    Increment(GateOccupancy, segmentId);
                }

                ushort previousBuilding;
                bool hadBuilding = VehicleBuildingAssignment.TryGetValue(vehicleId, out previousBuilding);
                if (hadBuilding && previousBuilding == buildingId)
                {
                    return;
                }

                if (hadBuilding)
                {
                    Decrement(BuildingOccupancy, previousBuilding);
                }

                if (buildingId != 0)
                {
                    VehicleBuildingAssignment[vehicleId] = buildingId;
                    Increment(BuildingOccupancy, buildingId);
                }
                else
                {
                    VehicleBuildingAssignment.Remove(vehicleId);
                }
            }
        }

        // Call when a vehicle despawns/is released, so its gate (and building, if tracked) don't
        // stay "occupied" forever.
        public static void ReleaseVehicle(ushort vehicleId)
        {
            lock (Lock)
            {
                ushort segmentId;
                if (VehicleGateAssignment.TryGetValue(vehicleId, out segmentId))
                {
                    Decrement(GateOccupancy, segmentId);
                    VehicleGateAssignment.Remove(vehicleId);
                }

                ushort buildingId;
                if (VehicleBuildingAssignment.TryGetValue(vehicleId, out buildingId))
                {
                    Decrement(BuildingOccupancy, buildingId);
                    VehicleBuildingAssignment.Remove(vehicleId);
                }
            }
        }

        private static void Increment(Dictionary<ushort, int> counts, ushort key)
        {
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + 1;
        }

        private static void Decrement(Dictionary<ushort, int> counts, ushort key)
        {
            int count;
            if (!counts.TryGetValue(key, out count))
            {
                return;
            }

            if (count <= 1)
            {
                counts.Remove(key);
            }
            else
            {
                counts[key] = count - 1;
            }
        }
    }
}
