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
        // OPEN BUG A0 (2026-09-05): a 14.7-minute session logged 558 dispatch starts and exactly
        // ZERO arrival records. Something between "the Postfix runs" and "an elapsed time comes
        // back" drops every single trip, and until now this file had no sanity log at all, so a
        // whole play session could not tell us which of the four possible stages it fails at.
        //
        // These counters exist to answer that in ONE session rather than four: they separate
        // "never called" from "called but __result false" from "filtered as the return leg" from
        // "no matching dispatch start". They are cheap (a few increments per arrival, an Info
        // line every 50) and deliberately NOT gated behind verbose logging - a diagnostic that
        // only fires when someone remembered to turn on a setting is a diagnostic that will be
        // missing from the log you actually need. Remove them once A0 is closed.
        private static bool loggedFirstCall;
        private static int loggedMisses;
        private static int callCount;
        private static int notArrivedCount;
        private static int goingBackCount;
        private static int noStartTimeCount;
        private static int recordedCount;

        private static void RecordArrival(string ownerTypeName, bool arrived, ushort vehicleId, ref Vehicle vehicleData)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Log.Info("[AIImprove] ArrivalTrackingPatch is executing.");
            }

            callCount++;

            if (!arrived)
            {
                notArrivedCount++;
                ReportIfDue();
                return;
            }

            // Only log the outbound leg (arriving at the emergency), not the return-to-depot leg.
            if ((vehicleData.m_flags & Vehicle.Flags.GoingBack) != 0)
            {
                goingBackCount++;
                ReportIfDue();
                return;
            }

            float? elapsed = EmergencyDispatchTracker.TakeElapsedSeconds(vehicleId);
            if (!elapsed.HasValue)
            {
                noStartTimeCount++;

                // A0: say WHY the lookup missed, for the first few misses only. If the ID was
                // never recorded by the dispatch side at all, the two sides are not agreeing on
                // what a vehicle ID is - which points at the injected IL that supplies it, not at
                // anything here. If it WAS recorded, something removed the entry too early.
                if (loggedMisses < 10)
                {
                    loggedMisses++;
                    Log.Info(
                        "[AIImprove] ArrivalTracking miss: " + ownerTypeName + " vehicle " +
                        vehicleId + " arrived with no dispatch start. Ever recorded by the " +
                        "dispatch side: " + EmergencyDispatchTracker.WasEverRecorded(vehicleId) +
                        ". Dispatch side currently holds " + EmergencyDispatchTracker.LiveCount +
                        " entries, e.g. [" + EmergencyDispatchTracker.SampleLiveIds(8) + "].");
                }

                ReportIfDue();
                return;
            }

            recordedCount++;
            ReportIfDue();

            if (Log.VerboseEnabled)
            {
                Log.Verbose(
                    "[AIImprove] " + ownerTypeName + " vehicle " + vehicleId + " arrived " +
                    elapsed.Value.ToString("F1") + "s after dispatch.");
            }
        }

        private static void ReportIfDue()
        {
            if (callCount % 50 != 0)
            {
                return;
            }

            Log.Info(
                "[AIImprove] ArrivalTracking diagnostic: " + callCount + " ArriveAtDestination " +
                "call(s) - " + recordedCount + " recorded, " + notArrivedCount + " result=false, " +
                goingBackCount + " return leg, " + noStartTimeCount + " no dispatch start on record.");
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
