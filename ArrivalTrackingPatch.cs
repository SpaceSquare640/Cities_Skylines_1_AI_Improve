using UnityEngine;

namespace AIImprove
{
    // Pairs with EmergencyIgnoreCostsPatch: Prefixes <VehicleAI>.ArriveAtDestination(ushort,
    // ref Vehicle) to log dispatch-to-arrival time for emergency trips, using the start time
    // EmergencyIgnoreCostsPatch recorded. This is the actual effect measurement for
    // Cities_Skylines_1_AI_Improve_Document/03's "待辦：效果驗收" - proving the dispatch side
    // changes real outcomes, not just that it runs.
    //
    // A0 CLOSED (2026-09-12) - and the hypothesis on record was wrong.
    //
    // The symptom: 558 dispatches and zero arrivals in one session; later, 2500 dispatches against
    // 2698 arrivals that passed every filter, with NOT ONE match. 14 - 現況總表 recorded the
    // suspicion that the injected IL passed a vehicle ID from the wrong argument position, so the
    // two sides were never talking about the same vehicle. That was checked against the game
    // assembly and it does not hold: ArriveAtDestination is (ushort vehicleID, ref Vehicle), the
    // patched StartPathFind is (ushort vehicleID, ref Vehicle, Vector3, Vector3, bool, bool,
    // bool), and Ldarg_1 is vehicleID in both. The IDs were always right.
    //
    // The real cause is what ArriveAtDestination RETURNS. Decompiled, all three ground types are
    // the same shape:
    //
    //     if (WaitingTarget) return false;
    //     if (GoingBack)     return ArriveAtSource(...);   // the return leg, into the depot
    //     return ArriveAtTarget(...);                      // the outbound leg, at the emergency
    //
    // ArriveAtTarget - arriving at the emergency, the one moment this file exists to time - loads
    // its casualty, calls SetTarget(vehicleID, ref data, 0) and returns FALSE. It returns false
    // because the vehicle is not finished: false is how it tells the caller "do not Unspawn me".
    // ArriveAtSource returns true, but by then GoingBack is set.
    //
    // The old Postfix required __result == true AND GoingBack == 0. In vanilla exactly one path
    // satisfies both:
    //
    //     if (data.m_targetBuilding == 0) {
    //         Singleton<VehicleManager>.instance.ReleaseVehicle(vehicleID);   // wipes the stamp
    //         return true;
    //     }
    //
    // That is not an arrival at all - it is a dispatch whose target disappeared - and it calls
    // ReleaseVehicle first, which reaches EmergencyDispatchTracker through VehicleStateCleanup and
    // deletes the dispatch timestamp before the Postfix ever runs. So the filter admitted only the
    // cases whose evidence it had just destroyed. Zero was not a rare miss; it was the only
    // arithmetic available.
    //
    // THE FIX, and why it is a Prefix: the measurement now runs BEFORE vanilla, which is the only
    // place where both facts still exist - the flags have not been rewritten by SetTarget, and the
    // timestamp has not been wiped by ReleaseVehicle. __result is not consulted at all, because a
    // return value meaning "do not despawn me" was never evidence about arriving.
    //
    // Called repeatedly while a vehicle sits at its destination (CarAI only calls this when the
    // vehicle is stationary at the end of its path, but it does so on every frame it stays there).
    // That needs no de-duplication: TakeElapsedSeconds removes the entry it returns, so the second
    // call onwards finds nothing and falls through as a miss with nothing recorded.
    //
    // THE BUG CLASS (12 - 開發準則, 準則 2): a Postfix that reads state the original method has
    // already rewritten, or that judges "did the thing happen" from a return value that means
    // something else. See 11 - 程式碼稽核紀錄.
    //
    // Simple Prefix (not a Transpiler): ArriveAtDestination has only primitive/single-ref-struct
    // parameters, no need to touch the method body itself.
    internal static class ArrivalTrackingPatch
    {
        // These counters separate the remaining outcomes from each other, so one session says
        // which stage drops a trip rather than four sessions saying it in turn. They are cheap
        // (a few increments per arrival) and the one-off "is executing" line is deliberately NOT
        // gated behind verbose logging - a diagnostic that only fires when someone remembered to
        // turn on a setting is a diagnostic that will be missing from the log you actually need.
        private static bool loggedFirstCall;
        private static int loggedMisses;
        private static int callCount;
        private static int waitingTargetCount;
        private static int goingBackCount;
        private static int noStartTimeCount;
        private static int recordedCount;

        // Called by TrackerReset when a save is unloaded, so counts from the previous city are not
        // read back as if they described the new one - see 12 - 開發準則, 準則 3.
        public static void ResetForNewLevel()
        {
            loggedFirstCall = false;
            loggedMisses = 0;
            callCount = 0;
            waitingTargetCount = 0;
            goingBackCount = 0;
            noStartTimeCount = 0;
            recordedCount = 0;
        }

        private static void RecordArrival(string ownerTypeName, ushort vehicleId, ref Vehicle vehicleData)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Log.Info("[AIImprove] ArrivalTrackingPatch is executing.");
            }

            callCount++;

            // Vanilla's own first check: no target assigned yet, so nothing has been arrived at.
            if ((vehicleData.m_flags & Vehicle.Flags.WaitingTarget) != 0)
            {
                waitingTargetCount++;
                ReportIfDue();
                return;
            }

            // Only time the outbound leg (arriving at the emergency), not the return-to-depot leg.
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

                // Say WHY the lookup missed, for the first few only. Now that the structural cause
                // is fixed, the expected miss is the benign one: a repeat call for a vehicle
                // already timed on an earlier frame, so the ID WAS recorded and we took it
                // ourselves. An ID that was never recorded at all would mean something new.
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
            // RELEASE GATE (2026-09-09): investigation diagnostics run at Info so they cannot be
            // missing from the log that matters (12 - 開發準則, 準則 10.3) - but that rule was
            // written for OUR test sessions, where we control the setting. Shipping it to players
            // meant roughly 16,000 lines a session of numbers only we can act on. The periodic
            // reports are behind verbose from here on; the one-off "is executing" and inventory
            // lines stay at Info, because those are what makes a player's bug report usable.
            if (!Log.VerboseEnabled)
            {
                return;
            }

            if (callCount % 50 != 0)
            {
                return;
            }

            Log.Info(
                "[AIImprove] ArrivalTracking diagnostic: " + callCount + " ArriveAtDestination " +
                "call(s) - " + recordedCount + " timed, " + waitingTargetCount + " no target yet, " +
                goingBackCount + " return leg, " + noStartTimeCount + " no dispatch start on record " +
                "(expected: repeat calls for a vehicle already timed).");
        }

        internal static class Ambulance
        {
            public static void Prefix(ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(AmbulanceAI), __0, ref __1);
        }

        internal static class FireTruck
        {
            public static void Prefix(ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(FireTruckAI), __0, ref __1);
        }

        internal static class PoliceCar
        {
            public static void Prefix(ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(PoliceCarAI), __0, ref __1);
        }

        // Helicopters: dispatch start comes from HelicopterDispatchTrackingPatch instead of
        // EmergencyIgnoreCostsPatch (helicopters have no path-cost concept to hook - see that
        // file), but arrival tracking itself is identical since all three copter types override
        // ArriveAtDestination(ushort, ref Vehicle) individually.
        internal static class AmbulanceCopter
        {
            public static void Prefix(ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(AmbulanceCopterAI), __0, ref __1);
        }

        internal static class FireCopter
        {
            public static void Prefix(ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(FireCopterAI), __0, ref __1);
        }

        internal static class PoliceCopter
        {
            public static void Prefix(ushort __0, ref Vehicle __1) =>
                RecordArrival(nameof(PoliceCopterAI), __0, ref __1);
        }
    }
}
