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
        // Re-enabled 2026-09-06 at user request, behind its own toggle (default OFF - it was
        // switched off by an explicit user decision on 2026-08-14, so it must not come back on
        // by itself) and with the multiplier exposed as a slider like every other tunable here.
        private static float Multiplier => ModSettings.IntercityBusCapacityPercent.value / 100f;

        private static readonly Dictionary<BusAI, int> OriginalCapacity = new Dictionary<BusAI, int>();

        private static bool loggedFirstCall;

        // BUG FOUND VIA AUDIT (Medium #5), fixed 2026-09-06: m_passengerCapacity lives on the
        // shared VehicleInfo AI instance, not on the vehicle. Boosting it wrote a value that
        // simply stayed there - turning the feature off, or unsubscribing the mod, left every
        // affected prefab permanently modified until the game was restarted. The player had no
        // way to undo it and nothing in the log said so.
        //
        // Two repair points, both structural rather than "remember to call this" (準則 3):
        // the disabled branch of the Prefix itself restores as it returns, so the very act of
        // turning the feature off is what repairs it; and RestoreAll covers level unload and mod
        // disable, where no Prefix will run again to notice.
        public static void RestoreAll()
        {
            foreach (var pair in OriginalCapacity)
            {
                if (pair.Key != null)
                {
                    pair.Key.m_passengerCapacity = pair.Value;
                }
            }

            OriginalCapacity.Clear();
        }

        private static bool RestoreAndReturn(BusAI instance)
        {
            int original;
            if (OriginalCapacity.TryGetValue(instance, out original))
            {
                instance.m_passengerCapacity = original;
                OriginalCapacity.Remove(instance);
            }

            return true;
        }

        public static void Prefix(BusAI __instance, ushort vehicleID, ref Vehicle data)
        {
            if (!TransportStationAI.IsIntercity(__instance.m_info.m_class))
            {
                return;
            }

            if (!ModSettings.IntercityBusCapacityEnabled.value)
            {
                RestoreAndReturn(__instance);
                return;
            }

            // Defer to Advanced Vehicle Options if it's installed - see CompanionModCompat.cs
            // and TrainPassengerCapacityPatch.cs's notes for the real-world case this fixed.
            if (CompanionModCompat.IsAdvancedVehicleOptionsLoaded())
            {
                RestoreAndReturn(__instance);
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
