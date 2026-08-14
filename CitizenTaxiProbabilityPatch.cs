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
        // TUNED DOWN (2026-08-14): was Multiplier 2.5 / FlatBonus 5, i.e. vanilla's 0/2/4/6 became
        // 5/10/15/20. Investigating a "public transport carries no passengers" report showed that
        // was working directly against this mod's own goals. Per ResidentAI.GetVehicleInfo, the
        // citizens who end up using public transport are exactly those who roll "no car" AND then
        // "no taxi" - GetTaxiProbability is only consulted for people who already decided not to
        // drive, so every point added here is taken straight out of the walk/transit pool. That
        // also cannibalized CitizenCarProbabilityPatch, which pushes people out of cars precisely
        // to move them toward transit - only for this patch to catch up to 20% of them and put
        // them back on the road in a taxi.
        //
        // Halved rather than reverted, per user decision: still noticeably more taxi usage than
        // vanilla (0/2/4/6 -> 2/5/8/11), without eating so much of the transit ridership.
        private const float Multiplier = 1.5f;
        private const int FlatBonus = 2;

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
