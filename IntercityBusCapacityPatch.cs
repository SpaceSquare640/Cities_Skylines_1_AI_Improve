using System.Collections.Generic;
using UnityEngine;

namespace AIImprove
{
    // "城際巴士...上限客量/載客量相關調整" (2026-08-14) - doubles intercity bus passenger capacity,
    // same mechanism as TrainPassengerCapacityPatch/PassengerHelicopterCapacityPatch (see those
    // files for the full rationale: mutate the field directly since it's read straight by
    // CreateVehicle when allocating citizen units, remember each AI instance's original value to
    // avoid compounding across repeated CreateVehicle calls, and the demand-vs-supply caveat -
    // this only visibly changes actual ridership once the route is genuinely turning citizens
    // away, not before).
    //
    // Scoped to intercity buses only via TransportStationAI.IsIntercity(m_info.m_class) - the
    // same check used elsewhere in this project to distinguish intercity buses from ordinary
    // in-city routes, which share the exact same BusAI class and are deliberately left untouched
    // here.
    internal static class IntercityBusCapacityPatch
    {
        private const float Multiplier = 2f;

        private static readonly Dictionary<BusAI, int> OriginalCapacity = new Dictionary<BusAI, int>();

        private static bool loggedFirstCall;

        public static void Prefix(BusAI __instance)
        {
            if (!TransportStationAI.IsIntercity(__instance.m_info.m_class))
            {
                return;
            }

            int original;
            if (!OriginalCapacity.TryGetValue(__instance, out original))
            {
                original = __instance.m_passengerCapacity;
                OriginalCapacity[__instance] = original;
            }

            __instance.m_passengerCapacity = Mathf.RoundToInt(original * Multiplier);

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log(
                    "[AIImprove] IntercityBusCapacityPatch is executing (e.g. " + original +
                    " -> " + __instance.m_passengerCapacity + ").");
            }
        }
    }
}
