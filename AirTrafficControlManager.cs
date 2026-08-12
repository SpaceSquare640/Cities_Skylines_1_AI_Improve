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
    internal static class AirTrafficControlManager
    {
        private static readonly Dictionary<ushort, int> GateOccupancy = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, ushort> VehicleGateAssignment = new Dictionary<ushort, ushort>();
        private static readonly object Lock = new object();

        public static int GetOccupancy(ushort segmentId)
        {
            lock (Lock)
            {
                int count;
                return GateOccupancy.TryGetValue(segmentId, out count) ? count : 0;
            }
        }

        // Moves vehicleId's assignment to segmentId, freeing whatever it was previously assigned
        // to (if anything). Safe to call repeatedly for the same vehicle across re-paths.
        public static void AssignGate(ushort vehicleId, ushort segmentId)
        {
            lock (Lock)
            {
                ushort previous;
                if (VehicleGateAssignment.TryGetValue(vehicleId, out previous))
                {
                    if (previous == segmentId)
                    {
                        return;
                    }

                    Decrement(previous);
                }

                VehicleGateAssignment[vehicleId] = segmentId;
                Increment(segmentId);
            }
        }

        // Call when a vehicle despawns/is released, so its gate doesn't stay "occupied" forever.
        public static void ReleaseVehicle(ushort vehicleId)
        {
            lock (Lock)
            {
                ushort segmentId;
                if (VehicleGateAssignment.TryGetValue(vehicleId, out segmentId))
                {
                    Decrement(segmentId);
                    VehicleGateAssignment.Remove(vehicleId);
                }
            }
        }

        private static void Increment(ushort segmentId)
        {
            int count;
            GateOccupancy.TryGetValue(segmentId, out count);
            GateOccupancy[segmentId] = count + 1;
        }

        private static void Decrement(ushort segmentId)
        {
            int count;
            if (!GateOccupancy.TryGetValue(segmentId, out count))
            {
                return;
            }

            if (count <= 1)
            {
                GateOccupancy.Remove(segmentId);
            }
            else
            {
                GateOccupancy[segmentId] = count - 1;
            }
        }
    }
}
