using System.Collections.Generic;
using UnityEngine;

namespace AIImprove
{
    // "城際列車客量增加" (2026-08-14): boosts intercity/regional passenger train capacity.
    // PassengerTrainAI.m_passengerCapacity (public int, default 30, prefab-customizable) is read
    // directly by CreateVehicle when allocating citizen units for a newly spawned train, and by
    // several other internal methods - it's the real source of truth for boarding capacity, not
    // just a displayed number, so this has to change the field itself rather than intercept a
    // read-only accessor.
    //
    // Scoped to real trains only, not metro: MetroTrainAI : PassengerTrainAI overrides
    // CreateVehicle with its own separate implementation (confirmed via dnSpy), so patching
    // PassengerTrainAI's own CreateVehicle - the "must patch the declaring/overriding type" rule
    // this project has hit before - simply never fires for metro vehicles. No explicit type
    // exclusion needed.
    //
    // m_passengerCapacity lives on the AI object, which is shared by every vehicle instance of a
    // given train prefab (not per-vehicle data) - so boosting it is a one-time-per-prefab-type
    // operation in spirit, but this Prefix runs on every CreateVehicle call regardless. Naively
    // multiplying on every call would compound (2x, 4x, 8x, ...) since the field persists between
    // calls. OriginalCapacity remembers each AI instance's true starting value the first time it's
    // seen, so every subsequent call recomputes from that fixed baseline instead of the
    // already-boosted current value - idempotent regardless of call count.
    internal static class TrainPassengerCapacityPatch
    {
        // Interim value pending live-test calibration, same as every other tunable in this
        // project.
        private const float Multiplier = 2f;

        private static readonly Dictionary<PassengerTrainAI, int> OriginalCapacity =
            new Dictionary<PassengerTrainAI, int>();

        private static bool loggedFirstCall;

        public static void Prefix(PassengerTrainAI __instance)
        {
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
                    "[AIImprove] TrainPassengerCapacityPatch is executing (e.g. " + original +
                    " -> " + __instance.m_passengerCapacity + ").");
            }
        }
    }
}
