using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AIImprove
{
    // Vanilla (no-TMPE) fallback for emergency vehicle priority. Targets PathFind's private
    // ProcessItemCosts, which has only one `ref` struct parameter (NetSegment) - unlike TMPE's
    // CalculateAdvancedAiCostFactors (4 ref structs), which hits a known Mono/Harmony IL
    // generation limit (see EmergencyVehiclePriorityPatch.cs and Cities_Skylines_1_AI_Improve_
    // Document/03). Uses a Transpiler because the congestion-derived cost is a local variable,
    // never exposed through any parameter or return value a Prefix/Postfix could reach.
    //
    // ProcessItemCosts' first parameter is PathFind.BufferItem, a *private* nested struct, so
    // it can't be named in a [HarmonyPatch] attribute or typeof() from this assembly. The
    // target method is located by name via AccessTools instead (see Patcher.cs) - safe here
    // because PathFind only declares one method named ProcessItemCosts.
    internal static class VanillaEmergencyCongestionPatch
    {
        // How much of the congestion penalty to keep for emergency-priority path requests.
        // 0 = ignore congestion entirely, 1 = no change from vanilla's calculated cost.
        private const float EmergencyCongestionRetention = 0.25f;

        // How often to log a hit while validating that this patch actually fires during real
        // play (see Cities_Skylines_1_AI_Improve_Document/03, "待驗證：效果驗收"). ProcessItemCosts
        // runs extremely often, so logging every single hit would flood the log and cost real
        // frame time - only every Nth hit is logged.
        private const int DiagnosticLogInterval = 200;
        private static long emergencyHitCount;
        private static bool loggedFirstCall;

        // Cached once instead of using Traverse per-call (this method runs on a very hot path -
        // every path cost calculation, for every candidate segment, for every vehicle).
        private static readonly Func<PathFind, VehicleInfo.VehicleCategory> GetVehicleCategory =
            BuildVehicleCategoryGetter();

        private static Func<PathFind, VehicleInfo.VehicleCategory> BuildVehicleCategoryGetter()
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(PathFind), "vehicleCategory");
            if (getter == null)
            {
                Debug.LogWarning(
                    "[AIImprove] PathFind.vehicleCategory getter not found - emergency vehicle " +
                    "detection will always report false. Game version may have changed.");
                return _ => VehicleInfo.VehicleCategory.None;
            }

            return (Func<PathFind, VehicleInfo.VehicleCategory>)Delegate.CreateDelegate(
                typeof(Func<PathFind, VehicleInfo.VehicleCategory>), getter);
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            LocalBuilder preCongestionCost = generator.DeclareLocal(typeof(float));
            var matcher = new CodeMatcher(code);

            // Base distance cost: "... * 0.003921569f * averageLength", stored to a local.
            // That store is our "pre-congestion" snapshot point. Matched by opcode shape only
            // (not the float literal itself) since float constants can round-trip with
            // different bit patterns than a C# source literal would compile to.
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Ldc_R4),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(i => i.opcode == OpCodes.Ldloc_S || i.opcode == OpCodes.Ldloc),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(i => i.opcode == OpCodes.Stloc_S || i.opcode == OpCodes.Stloc));

            if (!matcher.IsValid)
            {
                LogSkipped("pre-congestion cost store not found");
                return code;
            }

            object costLocalOperand = matcher.Instruction.operand;

            matcher.Advance(1);
            matcher.InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldloc_S, costLocalOperand),
                new CodeInstruction(OpCodes.Stloc_S, preCongestionCost));

            // Congestion multiplication is driven by NetSegment::m_trafficDensity - a distinctive,
            // stable anchor even if unrelated code around it shifts between game versions.
            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(NetSegment), nameof(NetSegment.m_trafficDensity))),
                new CodeMatch(OpCodes.Ldc_I4_S, (sbyte)10),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Add));

            if (!matcher.IsValid)
            {
                LogSkipped("congestion multiplication (m_trafficDensity) not found");
                return code;
            }

            // The next store after that is where the congestion-adjusted cost gets finalized.
            matcher.MatchStartForward(
                new CodeMatch(i => i.opcode == OpCodes.Stloc_S || i.opcode == OpCodes.Stloc));

            if (!matcher.IsValid)
            {
                LogSkipped("post-congestion cost store not found");
                return code;
            }

            matcher.Advance(1);
            matcher.InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldloc_S, preCongestionCost),
                new CodeInstruction(OpCodes.Ldloc_S, costLocalOperand),
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.Call(typeof(VanillaEmergencyCongestionPatch), nameof(AdjustCost)),
                new CodeInstruction(OpCodes.Stloc_S, costLocalOperand));

            Debug.Log("[AIImprove] Vanilla emergency congestion transpiler applied successfully.");

            return matcher.InstructionEnumeration();
        }

        private static void LogSkipped(string reason)
        {
            Debug.LogWarning(
                "[AIImprove] VanillaEmergencyCongestionPatch: " + reason +
                " - game version may have changed ProcessItemCosts. Skipping transpiler " +
                "changes; rest of the mod is unaffected.");
        }

        // Called from the injected IL. Keeps the base distance cost untouched and only pulls
        // the congestion-driven portion back toward 1x for emergency-priority path requests.
        private static float AdjustCost(float preCongestionCost, float postCongestionCost, PathFind pathFind)
        {
            // Sanity check: proves the injected call itself is executing at all, independent of
            // whether any emergency vehicle has been dispatched yet. Logged once ever, not
            // throttled, since it should fire almost immediately after load if the transpiler
            // worked - if this line never appears, the transpiler patched nothing, silently.
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] VanillaEmergencyCongestionPatch.AdjustCost is executing (sanity check, fires for all vehicles, not just emergency ones).");
            }

            if (!IsEmergencyPriorityRequest(pathFind))
            {
                return postCongestionCost;
            }

            long hitNumber = System.Threading.Interlocked.Increment(ref emergencyHitCount);

            // Verbose-gated in the same 2026-08-16 audit that caught the identical bug in
            // EmergencyIgnoreCostsPatch (see its notes). This one sits on the per-segment path
            // cost calculation - the hottest path this mod touches at all - so an unconditional
            // Debug.Log here is the worst possible place to leave one, even at interval 200.
            if (hitNumber % DiagnosticLogInterval == 1)
            {
                Log.Verbose(
                    "[AIImprove] Emergency priority cost adjustment fired (hit #" + hitNumber +
                    "): " + postCongestionCost + " -> " +
                    (preCongestionCost + (postCongestionCost - preCongestionCost) * EmergencyCongestionRetention));
            }

            return preCongestionCost + (postCongestionCost - preCongestionCost) * EmergencyCongestionRetention;
        }

        private static bool IsEmergencyPriorityRequest(PathFind pathFind)
        {
            // AmbulanceAI/FireTruckAI widen this to VehicleCategory.All specifically when
            // responding to an emergency (see Cities_Skylines_1_AI_Improve_Document/03).
            return GetVehicleCategory(pathFind) == VehicleInfo.VehicleCategory.All;
        }
    }
}
