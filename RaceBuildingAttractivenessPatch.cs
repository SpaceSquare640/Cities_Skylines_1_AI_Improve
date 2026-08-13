using UnityEngine;

namespace AIImprove
{
    // "增加比賽中心的吸引力" (2026-08-14): increases the tourism draw of the motorsport race
    // complex. RaceBuildingAI.GetAttractivenessAccumulation(ushort, ref Building) is the shared
    // base for every building making up the complex (start/end buildings, grandstands, road -
    // confirmed via dnSpy: none of RaceStandAI / RaceStartBuildingAI / RaceEndBuildingAI / etc.
    // override it themselves, only RaceBuildingAI defines it), feeding directly into
    // ImmaterialResourceManager.Resource.Attractiveness generation.
    //
    // Postfix on the compute method itself rather than the underlying m_attractivenessAccumulation
    // field: this method already recomputes the value fresh (including policy bonuses via
    // UniqueFacultyAI.IncreaseByBonus) every time it's called, so scaling its result has no
    // compounding-on-repeated-calls risk the way mutating a persisted field would (see
    // TrainPassengerCapacityPatch's notes on that exact problem for a different feature).
    internal static class RaceBuildingAttractivenessPatch
    {
        // Interim value pending live-test calibration, same as every other tunable in this
        // project.
        private const float Multiplier = 2f;

        private static bool loggedFirstCall;

        public static void Postfix(ref int __result)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] RaceBuildingAttractivenessPatch is executing.");
            }

            __result = Mathf.RoundToInt(__result * Multiplier);
        }
    }
}
