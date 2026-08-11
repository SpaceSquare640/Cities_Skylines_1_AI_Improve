using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AIImprove
{
    // Kept separate from AIImproveMod so HarmonyLib is never touched unless CitiesHarmony reported ready.
    public static class Patcher
    {
        private const string HarmonyId = "spacesquare.aiimprove";

        private static bool patched = false;

        public static void PatchAll()
        {
            if (patched)
            {
                return;
            }

            patched = true;

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            TryPatchTmpeEmergencyVehiclePriority(harmony);

            Debug.Log("[AIImprove] Harmony patches applied.");
        }

        // Reflection-only patch: no compile-time reference to TMPE exists anywhere in this
        // project, so this is skipped harmlessly (not an error) when TMPE isn't installed,
        // or if TMPE's internals have changed since this was written.
        //
        // KNOWN ISSUE (2026-08-12): currently fails at runtime with
        // "FormatException: ... cannot be patched. Reason: Invalid IL code".
        // Root cause: CalculateAdvancedAiCostFactors has 4 `ref` struct parameters
        // (BufferItem, NetSegment, NetLane, NetNode), and Harmony's dynamic-method wrapper
        // generation hits a known Mono JIT limitation with methods shaped like this -
        // see https://github.com/pardeike/Harmony/issues/105. This is a Mono/Harmony
        // limitation, not a logic bug in this patch. Wrapped in try/catch so it fails
        // loudly in the log without ever taking down PatchAll() or the game. Needs a
        // different insertion point (see Cities_Skylines_1_AI_Improve_Document/03).
        private static void TryPatchTmpeEmergencyVehiclePriority(Harmony harmony)
        {
            try
            {
                Type customPathFindType = TmpeCompat.FindCustomPathFindType();
                if (customPathFindType == null)
                {
                    Debug.Log("[AIImprove] TMPE not detected, skipping emergency vehicle priority patch.");
                    return;
                }

                MethodInfo original = TmpeCompat.FindCostFactorsMethod(customPathFindType);
                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] TMPE detected but CalculateAdvancedAiCostFactors not found - " +
                        "TMPE may not have Advanced Vehicle AI compiled in, or its internals changed. " +
                        "Skipping emergency vehicle priority patch.");
                    return;
                }

                MethodInfo postfix = typeof(EmergencyVehiclePriorityPatch).GetMethod(
                    nameof(EmergencyVehiclePriorityPatch.Postfix),
                    BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] TMPE detected, emergency vehicle priority patch applied.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Emergency vehicle priority patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
            }
        }

        public static void UnpatchAll()
        {
            if (!patched)
            {
                return;
            }

            var harmony = new Harmony(HarmonyId);
            harmony.UnpatchAll(HarmonyId);

            patched = false;

            Debug.Log("[AIImprove] Harmony patches removed.");
        }
    }
}
