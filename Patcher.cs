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

            PatchEmergencyVehiclePriority(harmony);
            TryPatchEmergencyIgnoreCosts(harmony, typeof(AmbulanceAI), typeof(AmbulanceIgnoreCostsPatch));
            TryPatchEmergencyIgnoreCosts(harmony, typeof(FireTruckAI), typeof(FireTruckIgnoreCostsPatch));
            TryPatchEmergencyIgnoreCosts(harmony, typeof(PoliceCarAI), typeof(PoliceCarIgnoreCostsPatch));
            TryPatchArrivalTracking(harmony, typeof(AmbulanceAI), typeof(ArrivalTrackingPatch.Ambulance));
            TryPatchArrivalTracking(harmony, typeof(FireTruckAI), typeof(ArrivalTrackingPatch.FireTruck));
            TryPatchArrivalTracking(harmony, typeof(PoliceCarAI), typeof(ArrivalTrackingPatch.PoliceCar));

            Debug.Log("[AIImprove] Harmony patches applied.");
        }

        // Effect measurement for the ignore-costs patch above - see ArrivalTrackingPatch.cs.
        private static bool TryPatchArrivalTracking(Harmony harmony, Type vehicleAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    vehicleAiType,
                    "ArriveAtDestination",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + vehicleAiType.Name + ".ArriveAtDestination not found - game " +
                        "version may have changed. Skipping arrival tracking patch for " + vehicleAiType.Name + ".");
                    return false;
                }

                MethodInfo postfix = patchWrapperType.GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Arrival tracking patch applied for " + vehicleAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Arrival tracking patch failed to apply for " + vehicleAiType.Name +
                    ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Soft dependency: prefer the TMPE-aware patch when TMPE is present and it actually
        // applies cleanly, otherwise fall back to the vanilla PathFind transpiler. Resilient
        // to either path failing for any reason - the other is still attempted.
        //
        // KNOWN DEAD as of 2026-08-12 (kept in place; see Cities_Skylines_1_AI_Improve_Document/03
        // "策略性轉向"): the TMPE branch fails on a Mono JIT limitation, and the vanilla branch
        // patches successfully but is never actually called when TMPE is active - TMPE's
        // CustomPathFind replaces PathFind entirely for real pathfinding. Superseded by
        // TryPatchAmbulanceIgnoreCosts below, which works whether or not TMPE is installed.
        private static void PatchEmergencyVehiclePriority(Harmony harmony)
        {
            if (TryPatchTmpeEmergencyVehiclePriority(harmony))
            {
                return;
            }

            TryPatchVanillaEmergencyCongestion(harmony);
        }

        // Vehicle-level, TMPE-independent emergency priority patch - see EmergencyIgnoreCostsPatch.cs.
        // Same shared transpiler body applied to AmbulanceAI, FireTruckAI and PoliceCarAI, each
        // via its own thin wrapper class (Harmony transpilers must be plain static methods).
        private static bool TryPatchEmergencyIgnoreCosts(Harmony harmony, Type vehicleAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    vehicleAiType,
                    "StartPathFind",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(bool) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + vehicleAiType.Name + ".StartPathFind(7-arg) not found - game " +
                        "version may have changed. Skipping ignore-costs patch for " + vehicleAiType.Name + ".");
                    return false;
                }

                MethodInfo transpiler = patchWrapperType.GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));

                Debug.Log("[AIImprove] Ignore-costs patch applied for " + vehicleAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Ignore-costs patch failed to apply for " + vehicleAiType.Name +
                    ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
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
        private static bool TryPatchTmpeEmergencyVehiclePriority(Harmony harmony)
        {
            try
            {
                Type customPathFindType = TmpeCompat.FindCustomPathFindType();
                if (customPathFindType == null)
                {
                    Debug.Log("[AIImprove] TMPE not detected, skipping TMPE emergency vehicle priority patch.");
                    return false;
                }

                MethodInfo original = TmpeCompat.FindCostFactorsMethod(customPathFindType);
                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] TMPE detected but CalculateAdvancedAiCostFactors not found - " +
                        "TMPE may not have Advanced Vehicle AI compiled in, or its internals changed. " +
                        "Skipping TMPE emergency vehicle priority patch.");
                    return false;
                }

                MethodInfo postfix = typeof(EmergencyVehiclePriorityPatch).GetMethod(
                    nameof(EmergencyVehiclePriorityPatch.Postfix),
                    BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] TMPE detected, TMPE emergency vehicle priority patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] TMPE emergency vehicle priority patch failed to apply, falling back " +
                    "to the vanilla PathFind transpiler. Reason: " + ex.Message);
                return false;
            }
        }

        // Vanilla fallback: patches PathFind.ProcessItemCosts directly. Located by name since
        // its first parameter (PathFind.BufferItem) is a private nested struct and can't be
        // named from this assembly - see VanillaEmergencyCongestionPatch.cs.
        private static bool TryPatchVanillaEmergencyCongestion(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(typeof(PathFind), "ProcessItemCosts");
                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] PathFind.ProcessItemCosts not found - game version may have " +
                        "changed. Skipping vanilla emergency congestion patch.");
                    return false;
                }

                MethodInfo transpiler = typeof(VanillaEmergencyCongestionPatch).GetMethod(
                    nameof(VanillaEmergencyCongestionPatch.Transpiler),
                    BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));

                Debug.Log("[AIImprove] Vanilla emergency congestion patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Vanilla emergency congestion patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
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
