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
            if (elapsed.HasValue && Log.VerboseEnabled)
            {
                Log.Verbose(
                    "[AIImprove] " + ownerTypeName + " vehicle " + vehicleId + " arrived " +
                    elapsed.Value.ToString("F1") + "s after dispatch.");
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

        // Helicopters: dispatch start comes from HelicopterDispatchTrackingPatch instead of
        // EmergencyIgnoreCostsPatch (helicopters have no path-cost concept to hook - see that
        // file), but arrival tracking itself is identical since all three copter types override
        // ArriveAtDestination(ushort, ref Vehicle) individually.
        internal static class AmbulanceCopter
        {
            public static void Postfix(bool __result, ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(AmbulanceCopterAI), __result, __0, ref __1);
        }

        internal static class FireCopter
        {
            public static void Postfix(bool __result, ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(FireCopterAI), __result, __0, ref __1);
        }

        internal static class PoliceCopter
        {
            public static void Postfix(bool __result, ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(PoliceCopterAI), __result, __0, ref __1);
        }
    }
}
