using UnityEngine;

namespace AIImprove
{
    // Effect measurement for emergency helicopters (AmbulanceCopterAI, FireCopterAI,
    // PoliceCopterAI), mirroring EmergencyIgnoreCostsPatch/ArrivalTrackingPatch for their ground
    // counterparts - see Cities_Skylines_1_AI_Improve_Document/03, "直升機" entry (2026-08-12).
    //
    // IMPORTANT DIFFERENCE from ground vehicles: HelicopterAI.StartPathFind does NOT call
    // PathManager.FindPathPosition/CreatePath at all - it just sets m_targetPos0..3 directly to
    // the destination and always returns true (confirmed via dnSpy). Helicopters already fly a
    // direct line ignoring roads and congestion entirely, so there is no "path cost" for an
    // ignore-costs-style patch to bypass - that whole category of fix does not apply here. This
    // patch is purely observational (dispatch-to-arrival timing), not a behavior change.
    //
    // None of the three copter types override the 5-arg StartPathFind themselves (only the
    // 2-arg one, which calls into HelicopterAI's implementation) - same "declared on the base
    // type" situation as the VehicleAI.ReleaseVehicle fix in Patcher.cs, so this Postfixes
    // HelicopterAI.StartPathFind directly and filters by __instance type. That runs for every
    // helicopter in the game (including PassengerHelicopterAI, DisasterResponseCopterAI), but
    // the type check makes it a no-op for anything that isn't one of the three we care about.
    internal static class HelicopterDispatchTrackingPatch
    {
        private static bool loggedFirstCall;

        public static void Postfix(ushort vehicleID, HelicopterAI __instance, ref Vehicle vehicleData)
        {
            if (!(__instance is AmbulanceCopterAI) && !(__instance is FireCopterAI) && !(__instance is PoliceCopterAI))
            {
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] HelicopterDispatchTrackingPatch is executing.");
            }

            // Only the outbound leg (heading to the incident), matching ArrivalTrackingPatch's
            // own outbound-only filter so dispatch/arrival timestamps pair up correctly.
            if ((vehicleData.m_flags & Vehicle.Flags.GoingBack) != 0)
            {
                return;
            }

            EmergencyDispatchTracker.RecordDispatchStart(vehicleID);
        }
    }
}
