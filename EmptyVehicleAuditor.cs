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

            // Carried through to the deferred delete so each vehicle can be re-checked against
            // the same criteria it was scanned with - see DeleteVehicles.
            internal readonly MatchPredicate Matches;

            internal ScanResult(List<ushort> leadVehicleIds, int totalVehicleCount, MatchPredicate matches)
            {
                LeadVehicleIds = leadVehicleIds;
                TotalVehicleCount = totalVehicleCount;
                Matches = matches;
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

        internal delegate bool MatchPredicate(ref Vehicle data);

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

            return new ScanResult(leadVehicleIds, totalVehicleCount, matches);
        }

        // BUG FOUND VIA AUDIT (2026-08-15): this used to run inline on the UI thread, straight
        // from the settings-panel button's click handler. VehicleManager.ReleaseVehicle mutates
        // the vehicle buffer and its free-item pool, and (via our own VehicleAI.ReleaseVehicle
        // postfix) writes several unsynchronized static Dictionaries that the simulation thread
        // reads and writes concurrently every tick - a textbook data race that can corrupt those
        // dictionaries or the vehicle pool itself. Deferring onto the simulation thread via
        // SimulationManager.AddAction is the standard CS1 pattern for exactly this, and is what
        // vanilla's own UI does whenever a button has to change simulation state.
        public static void DeleteVehicles(ScanResult result)
        {
            Singleton<SimulationManager>.instance.AddAction(() => DeleteVehiclesOnSimulationThread(result));
        }

        private static void DeleteVehiclesOnSimulationThread(ScanResult result)
        {
            VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
            Vehicle[] buffer = vehicleManager.m_vehicles.m_buffer;
            int releasedCount = 0;
            int skippedCount = 0;

            foreach (ushort leadId in result.LeadVehicleIds)
            {
                // Re-validate against the original scan criteria before touching anything. Time
                // passes between the scan, the player reading the confirm dialog, and this
                // deferred action running on the simulation thread - the scanned vehicle may have
                // despawned and had its ID handed to a completely different, non-empty vehicle in
                // the meantime. Checking Vehicle.Flags.Created alone would NOT catch that, since
                // the replacement vehicle has it set too.
                if ((buffer[leadId].m_flags & Vehicle.Flags.Created) == 0 ||
                    !result.Matches(ref buffer[leadId]))
                {
                    skippedCount++;
                    continue;
                }

                ushort current = leadId;
                int safety = 0;

                while (current != 0 && safety < 64)
                {
                    ushort next = buffer[current].m_trailingVehicle;

                    if ((buffer[current].m_flags & Vehicle.Flags.Created) != 0)
                    {
                        vehicleManager.ReleaseVehicle(current);
                        releasedCount++;
                    }

                    current = next;
                    safety++;
                }
            }

            Log.Info(
                "[AIImprove] EmptyVehicleAuditor deleted " + releasedCount +
                " vehicle instance(s) across " + (result.LeadVehicleIds.Count - skippedCount) +
                " chain(s)" +
                (skippedCount > 0
                    ? ", skipped " + skippedCount + " that no longer matched (despawned or ID reused)."
                    : "."));
        }
    }
}
