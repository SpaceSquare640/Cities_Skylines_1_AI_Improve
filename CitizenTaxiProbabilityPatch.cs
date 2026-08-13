using UnityEngine;

namespace AIImprove
{
    // Increases taxi usage (per user request, 2026-08-13). ResidentAI.GetTaxiProbability(ushort,
    // ref CitizenInstance, Citizen.AgeGroup) - same safe shape as GetCarProbability - only ever
    // returns a flat 0/2/4/6 based purely on age group, no policy or context factors at all. Only
    // consulted when a citizen has already decided not to drive their own car (see
    // ResidentAI.GetVehicleInfo), so this only affects the walk/bike/transit-vs-taxi split on
    // those trips, not whether someone drives their own car in the first place.
    internal static class CitizenTaxiProbabilityPatch
    {
        // Interim values pending live-test calibration, same as every other tunable in this
        // project. Multiplier first (2% -> 5%, 6% -> 15%), then a flat bonus on top so even the
        // Child case (which vanilla hardcodes to a flat 0, unaffected by the multiplier) still
        // gets some baseline taxi usage.
        private const float Multiplier = 2.5f;
        private const int FlatBonus = 5;

        private static bool loggedFirstCall;

        public static void Postfix(ref int __result)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] CitizenTaxiProbabilityPatch is executing.");
            }

            __result = Mathf.Clamp(Mathf.RoundToInt(__result * Multiplier) + FlatBonus, 0, 100);
        }
    }
}
