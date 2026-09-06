using System.Collections.Generic;
using UnityEngine;

namespace AIImprove
{
    // "客量/載客量相關調整" (2026-08-14) for passenger helicopters - same mechanism and same
    // multiplier as TrainPassengerCapacityPatch (see that file's notes for the full rationale:
    // why this has to mutate the field rather than intercept a read-only accessor, why the
    // per-instance "original value" bookkeeping is needed to avoid compounding on repeated
    // CreateVehicle calls, and the demand-vs-supply caveat - a higher capacity only visibly
    // changes actual boarding counts once the route is genuinely turning citizens away, not
    // before). PassengerHelicopterAI.m_passengerCapacity (public int, default 30) is read
    // directly by CreateVehicle when allocating citizen units, so the field itself is the real
    // source of truth here too.
    internal static class PassengerHelicopterCapacityPatch
    {
        // "每個功能中的調整設定及數據可以拆開以及詳細調整" (2026-08-15): now a slider, default
        // unchanged (200% = 2x).
        private static float Multiplier => ModSettings.PassengerHelicopterCapacityPercent.value / 100f;

        private static readonly Dictionary<PassengerHelicopterAI, int> OriginalCapacity =
            new Dictionary<PassengerHelicopterAI, int>();

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

        private static bool RestoreAndReturn(PassengerHelicopterAI instance)
        {
            int original;
            if (OriginalCapacity.TryGetValue(instance, out original))
            {
                instance.m_passengerCapacity = original;
                OriginalCapacity.Remove(instance);
            }

            return true;
        }

        public static void Prefix(PassengerHelicopterAI __instance)
        {
            if (!ModSettings.PassengerHelicopterCapacityEnabled.value)
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

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log(
                    "[AIImprove] PassengerHelicopterCapacityPatch is executing (e.g. " + original +
                    " -> " + __instance.m_passengerCapacity + ").");
            }
        }
    }
}
