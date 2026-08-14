using UnityEngine;

namespace AIImprove
{
    // First step of "市民 AI" improvements (2026-08-13, per user request): make the walk/drive/
    // public-transit split respond to real-time congestion instead of being a fixed policy-driven
    // probability roll. Full multimodal-route intelligence (e.g. picking the least-congested
    // transit line for a trip) lives deep inside PathFind's own cost function - the same
    // multi-ref-struct method this project already established (months ago, see
    // Cities_Skylines_1_AI_Improve_Document/01) that Harmony/Mono cannot patch. This patch instead
    // targets the one safe, well-scoped lever available before that wall: ResidentAI's own
    // GetCarProbability(ushort, ref CitizenInstance, Citizen.AgeGroup) - a private method with a
    // single ref-struct parameter (CitizenInstance), called once per trip decision, well before
    // any pathfinding happens.
    //
    // Mechanism: Postfix reads the vanilla-computed probability (0-100, from age/policy/wealth
    // factors) and scales it down when the road network near the citizen's destination is already
    // heavily congested - nudging more citizens toward walking or public transit on trips that
    // would otherwise pile more cars onto an already-jammed area, without touching pathfinding
    // itself or removing anyone's ability to still choose to drive.
    internal static class CitizenCarProbabilityPatch
    {
        // Below this NetSegment.m_trafficDensity (0-100 scale) reading, vanilla's probability is
        // left untouched entirely - only meaningfully congested destinations are affected.
        private const float DensityThreshold = 70f;

        // At density == 100, vanilla's car probability is cut by up to this fraction. Interim
        // value pending live-test calibration, same as every other threshold in this project.
        private const float MaxReductionFraction = 0.6f;

        // Root cause of a second stutter report (2026-08-13), same shape as the one
        // RerouteRateLimiter already fixed for vehicle reroutes: GetNearbyRoadDensity is a real
        // PathManager.FindPathPosition call, and this Postfix ran completely unthrottled once per
        // citizen trip decision - a busy city can start many trips in the same simulation frame.
        // Denied citizens simply keep vanilla's unmodified probability for this one decision -
        // there's no cooldown state to preserve here (unlike vehicle reroutes), so no special
        // handling is needed beyond just skipping the density check for this call.
        private static readonly PerFrameBudget Budget = new PerFrameBudget(5);

        private static bool loggedFirstCall;

        public static void Postfix(ref CitizenInstance citizenData, ref int __result)
        {
            if (!ModSettings.CitizensEnabled.value)
            {
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] CitizenCarProbabilityPatch is executing.");
            }

            if (__result <= 0 || !Budget.TryConsume())
            {
                return;
            }

            float density = CitizenCongestionQuery.GetNearbyRoadDensity(citizenData.m_targetPos);
            if (density < DensityThreshold)
            {
                return;
            }

            float t = Mathf.Clamp01((density - DensityThreshold) / (100f - DensityThreshold));
            float reduction = t * MaxReductionFraction;
            __result = Mathf.Max(0, Mathf.RoundToInt(__result * (1f - reduction)));
        }
    }
}
