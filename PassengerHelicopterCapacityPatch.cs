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
        private const float Multiplier = 2f;

        private static readonly Dictionary<PassengerHelicopterAI, int> OriginalCapacity =
            new Dictionary<PassengerHelicopterAI, int>();

        private static bool loggedFirstCall;

        public static void Prefix(PassengerHelicopterAI __instance)
        {
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
