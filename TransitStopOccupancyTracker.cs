using System.Collections.Generic;

namespace AIImprove
{
    // Lightweight "how many vehicles currently regard this as their stop" counter for
    // TransitStationSkipPatch (see that file for the feature). Two independent id spaces since
    // BusAI/PassengerHelicopterAI stops are Building ids but PassengerTrainAI/MetroTrainAI stops
    // are NetNode ids - Building #500 and NetNode #500 are unrelated entities, so mixing them in
    // one dictionary would conflate counts.
    //
    // Deliberately NOT backed by CreateVehicle/ReleaseVehicle hooks: TransitStationSkipPatch
    // calls Enter/Leave itself at the exact spot each <TransportAI>.ArriveAtTarget already
    // decides "leaving old stop, heading to new stop" - so every Enter this tracker ever records
    // is guaranteed a matching Leave from that same vehicle's next arrival (or the recursive
    // fly-by re-arrival within the same tick), keeping counts self-balancing with no separate
    // spawn/despawn bookkeeping needed. The only gap is the very first stop of a vehicle's life
    // (assigned by SetTarget before its first ArriveAtTarget call), which is never counted as an
    // Enter - a harmless, momentary undercount, not a leak.
    internal static class TransitStopOccupancyTracker
    {
        private static readonly Dictionary<ushort, int> BuildingOccupancy = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, int> NodeOccupancy = new Dictionary<ushort, int>();

        public static void EnterBuilding(ushort buildingId) => Enter(BuildingOccupancy, buildingId);
        public static void LeaveBuilding(ushort buildingId) => Leave(BuildingOccupancy, buildingId);
        public static int GetBuildingOccupancy(ushort buildingId) => Get(BuildingOccupancy, buildingId);

        public static void EnterNode(ushort nodeId) => Enter(NodeOccupancy, nodeId);
        public static void LeaveNode(ushort nodeId) => Leave(NodeOccupancy, nodeId);
        public static int GetNodeOccupancy(ushort nodeId) => Get(NodeOccupancy, nodeId);

        private static void Enter(Dictionary<ushort, int> table, ushort id)
        {
            if (id == 0)
            {
                return;
            }

            int count;
            table.TryGetValue(id, out count);
            table[id] = count + 1;
        }

        private static void Leave(Dictionary<ushort, int> table, ushort id)
        {
            if (id == 0)
            {
                return;
            }

            int count;
            if (!table.TryGetValue(id, out count))
            {
                return;
            }

            if (count <= 1)
            {
                table.Remove(id);
            }
            else
            {
                table[id] = count - 1;
            }
        }

        private static int Get(Dictionary<ushort, int> table, ushort id)
        {
            int count;
            return id != 0 && table.TryGetValue(id, out count) ? count : 0;
        }
    }
}
