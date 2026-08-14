using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "一鍵檢測沒有乘客的車輛...選擇是否直接刪除" (2026-08-15): manual maintenance tool for
    // intercity trains/buses, triggered from the settings panel buttons (see
    // AIImproveMod.OnSettingsUI). Deliberately NOT gated on IntercityTrainEnabled/
    // IntercityBusEnabled - this is a one-off cleanup action the player asks for directly, not
    // continuous AI behavior, so it should work regardless of whether those patches are toggled
    // on.
    //
    // "No passengers" = Vehicle.m_transferSize == 0, the same field TrainPassengerCapacityPatch/
    // IntercityBusCapacityPatch read for the current-boarded count shown in the game's own info
    // panel (see those files' notes on why this field is safe to trust: it's recomputed from
    // scratch at each real stop, not an approximate counter).
    internal static class EmptyVehicleAuditor
    {
        public struct ScanResult
        {
            public readonly List<ushort> LeadVehicleIds;
            public readonly int TotalVehicleCount;

            public ScanResult(List<ushort> leadVehicleIds, int totalVehicleCount)
            {
                LeadVehicleIds = leadVehicleIds;
                TotalVehicleCount = totalVehicleCount;
            }
        }

        public static ScanResult ScanIntercityTrains()
        {
            return Scan(IsEmptyIntercityTrain);
        }

        public static ScanResult ScanIntercityBuses()
        {
            return Scan(IsEmptyIntercityBus);
        }

        private static bool IsEmptyIntercityTrain(ref Vehicle data)
        {
            VehicleInfo info = data.Info;
            return info != null &&
                   info.m_vehicleAI is PassengerTrainAI &&
                   !(info.m_vehicleAI is MetroTrainAI) &&
                   data.m_transferSize == 0;
        }

        private static bool IsEmptyIntercityBus(ref Vehicle data)
        {
            VehicleInfo info = data.Info;
            return info != null &&
                   info.m_vehicleAI is BusAI &&
                   TransportStationAI.IsIntercity(info.m_class) &&
                   data.m_transferSize == 0;
        }

        private delegate bool MatchPredicate(ref Vehicle data);

        private static ScanResult Scan(MatchPredicate matches)
        {
            Vehicle[] buffer = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            List<ushort> leadVehicleIds = new List<ushort>();
            int totalVehicleCount = 0;

            for (ushort i = 0; i < buffer.Length; i++)
            {
                ref Vehicle data = ref buffer[i];

                if ((data.m_flags & Vehicle.Flags.Created) == 0 ||
                    (data.m_flags & Vehicle.Flags.Deleted) != 0)
                {
                    continue;
                }

                if (!matches(ref data))
                {
                    continue;
                }

                totalVehicleCount++;

                // Only the lead vehicle of a chain (leading == 0) - DeleteVehicles walks the
                // trailer chain itself, so counting/collecting trailers separately would double
                // up.
                if (data.m_leadingVehicle == 0)
                {
                    leadVehicleIds.Add(i);
                }
            }

            Debug.Log(
                "[AIImprove] EmptyVehicleAuditor scan found " + leadVehicleIds.Count +
                " empty vehicle chain(s) (" + totalVehicleCount + " vehicle instance(s) total, " +
                "including trailers).");

            return new ScanResult(leadVehicleIds, totalVehicleCount);
        }

        public static void DeleteVehicles(List<ushort> leadVehicleIds)
        {
            VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
            int releasedCount = 0;

            foreach (ushort leadId in leadVehicleIds)
            {
                ushort current = leadId;
                int safety = 0;

                while (current != 0 && safety < 64)
                {
                    ushort next = vehicleManager.m_vehicles.m_buffer[current].m_trailingVehicle;

                    if ((vehicleManager.m_vehicles.m_buffer[current].m_flags & Vehicle.Flags.Created) != 0)
                    {
                        vehicleManager.ReleaseVehicle(current);
                        releasedCount++;
                    }

                    current = next;
                    safety++;
                }
            }

            Debug.Log(
                "[AIImprove] EmptyVehicleAuditor deleted " + releasedCount +
                " vehicle instance(s) across " + leadVehicleIds.Count + " chain(s).");
        }
    }
}
