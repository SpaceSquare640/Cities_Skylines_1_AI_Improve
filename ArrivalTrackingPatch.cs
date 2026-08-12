using UnityEngine;

namespace AIImprove
{
    // Pairs with EmergencyIgnoreCostsPatch: Postfixes <VehicleAI>.ArriveAtDestination(ushort,
    // ref Vehicle) to log dispatch-to-arrival time for emergency trips, using the start time
    // EmergencyIgnoreCostsPatch recorded. This is the actual effect measurement for
    // Cities_Skylines_1_AI_Improve_Document/03's "待辦：效果驗收" - proving the ignore-costs
    // patch changes real outcomes, not just that it runs.
    //
    // Simple Postfix (not a Transpiler): ArriveAtDestination has only primitive/single-ref-struct
    // parameters and a normal bool return, no need to touch the method body itself.
    internal static class ArrivalTrackingPatch
    {
        private static void RecordArrival(string ownerTypeName, bool arrived, ushort vehicleId, ref Vehicle vehicleData)
        {
            if (!arrived)
            {
                return;
            }

            // Only log the outbound leg (arriving at the emergency), not the return-to-depot leg.
            if ((vehicleData.m_flags & Vehicle.Flags.GoingBack) != 0)
            {
                return;
            }

            float? elapsed = EmergencyDispatchTracker.TakeElapsedSeconds(vehicleId);
            if (elapsed.HasValue)
            {
                Debug.Log("[AIImprove] " + ownerTypeName + " vehicle " + vehicleId + " arrived " + elapsed.Value.ToString("F1") + "s after dispatch (ignoreCosts patch active).");
            }
        }

        internal static class Ambulance
        {
            public static void Postfix(bool __result, ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(AmbulanceAI), __result, __0, ref __1);
        }

        internal static class FireTruck
        {
            public static void Postfix(bool __result, ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(FireTruckAI), __result, __0, ref __1);
        }

        internal static class PoliceCar
        {
            public static void Postfix(bool __result, ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(PoliceCarAI), __result, __0, ref __1);
        }
    }
}
