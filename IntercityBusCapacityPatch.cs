using System.Collections.Generic;
using ColossalFramework.Math;
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
        private const float Multiplier = 2f; // disabled feature, not exposed as a setting

        private static readonly Dictionary<BusAI, int> OriginalCapacity = new Dictionary<BusAI, int>();

        private static bool loggedFirstCall;

        public static void Prefix(BusAI __instance, ushort vehicleID, ref Vehicle data)
        {
            if (!TransportStationAI.IsIntercity(__instance.m_info.m_class))
            {
                return;
            }

            // Not currently registered (see Patcher.PatchAll's comment), but wired to the
            // intercity bus toggle now that it exists, so it respects the panel immediately if
            // ever re-enabled (2026-08-15).
            if (!ModSettings.IntercityBusRerouteEnabled.value)
            {
                return;
            }

            // Defer to Advanced Vehicle Options if it's installed - see CompanionModCompat.cs
            // and TrainPassengerCapacityPatch.cs's notes for the real-world case this fixed.
            if (CompanionModCompat.IsAdvancedVehicleOptionsLoaded())
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

            // "新生成時的已載客量" (2026-08-14) - see TrainPassengerCapacityPatch.cs's notes for
            // the full rationale (why this is safe: m_transferSize is recomputed from scratch at
            // the vehicle's first real stop, not decremented arithmetically from this seed value).
            data.m_transferSize = (ushort)new Randomizer(vehicleID).Int32(
                __instance.m_passengerCapacity >> 1, __instance.m_passengerCapacity);

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log(
                    "[AIImprove] IntercityBusCapacityPatch is executing (e.g. " + original +
                    " -> " + __instance.m_passengerCapacity + ", initial boarded " +
                    data.m_transferSize + ").");
            }
        }
    }
}
