using System.Collections.Generic;
using ColossalFramework.Math;
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

        public static void Prefix(PassengerTrainAI __instance, ushort vehicleID, ref Vehicle data)
        {
            // Defer to Advanced Vehicle Options if it's installed - see CompanionModCompat.cs.
            // AVO lets players set an explicit custom capacity per vehicle asset; doubling
            // whatever we find on top of that stacks unpredictably (confirmed via a real user
            // screenshot showing 31968 capacity on one train).
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

            // "新生成時的已載客量" (2026-08-14): a newly spawned intercity train came from outside
            // the map, so it's unrealistic for it to show 0 passengers already aboard - real
            // trains arrive already carrying out-of-town riders. data.m_transferSize (the
            // "current" half of GetBufferStatus's current/max display) is purely a display/stat
            // counter here, not something LoadPassengers/UnloadPassengers increment
            // arithmetically - TransportArriveAtTarget recomputes it from scratch by counting
            // actual occupied citizen units at the very first real stop (see BusAI.cs's identical
            // pattern), so seeding it with a fake starting number is safe: it just gets
            // overwritten with the real count the moment the train reaches its first stop, no
            // underflow/desync risk. Same random-half-to-full range GetBufferStatus's own
            // DummyTraffic special case already uses for the same cosmetic purpose.
            data.m_transferSize = (ushort)new Randomizer(vehicleID).Int32(
                __instance.m_passengerCapacity >> 1, __instance.m_passengerCapacity);

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log(
                    "[AIImprove] TrainPassengerCapacityPatch is executing (e.g. " + original +
                    " -> " + __instance.m_passengerCapacity + ", initial boarded " +
                    data.m_transferSize + ").");
            }
        }
    }
}
