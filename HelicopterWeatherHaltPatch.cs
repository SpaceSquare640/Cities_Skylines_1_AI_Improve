using UnityEngine;

namespace AIImprove
{
    // "Shut down all helicopter services during a thunderstorm" (per user request, 2026-08-13).
    // Prefixes the same HelicopterAI.StartPathFind(ushort, ref Vehicle, Vector3, Vector3, float)
    // overload HelicopterDispatchTrackingPatch already observes (see that file's notes: this
    // never calls PathManager.CreatePath, just sets target waypoints directly and always returns
    // true, so refusing here means faking a false return rather than causing a real pathfind
    // failure - the caller treats it the same way either way).
    //
    // Only blocks vehicles that aren't airborne yet (Vehicle.Flags.Spawned unset) - a helicopter
    // already in the air can still have this called again for an ordinary mid-flight retarget,
    // and blocking that would strand it stuck facing a stale destination instead of just refusing
    // a fresh dispatch.
    internal static class HelicopterWeatherHaltPatch
    {
        private static bool loggedFirstCall;

        public static bool Prefix(ushort vehicleID, HelicopterAI __instance, ref Vehicle vehicleData, ref bool __result)
        {
            if (!ModSettings.EmergencyVehiclesEnabled.value)
            {
                return true;
            }

            if (!(__instance is AmbulanceCopterAI) && !(__instance is FireCopterAI) && !(__instance is PoliceCopterAI))
            {
                return true;
            }

            if ((vehicleData.m_flags & Vehicle.Flags.Spawned) != 0)
            {
                // Already airborne - let it continue/retarget normally, don't strand it mid-air.
                return true;
            }

            if (!WeatherDisasterDetector.IsThunderstormActive())
            {
                return true;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] HelicopterWeatherHaltPatch is executing.");
            }

            Debug.Log("[AIImprove] Helicopter " + vehicleID + " dispatch refused - grounded for thunderstorm.");
            __result = false;
            return false;
        }
    }
}
