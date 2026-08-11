using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AIImprove
{
    // Vehicle-level (not PathFind-level) emergency priority patch. TMPE-independent: TMPE
    // replaces PathFind/PathManager, but never AmbulanceAI/FireTruckAI/PoliceCarAI, so this
    // works the same whether or not TMPE is installed - see Cities_Skylines_1_AI_Improve_
    // Document/03, "策略性轉向" entry (2026-08-12), written after both PathFind-level insertion
    // points (vanilla and TMPE) turned out to be either dead code (vanilla, when TMPE is active)
    // or unpatchable (TMPE, Mono JIT "Invalid IL code" limitation on methods with many `ref`
    // struct parameters).
    //
    // Targets <VehicleAI>.StartPathFind(ushort, ref Vehicle, Vector3, Vector3, bool, bool, bool),
    // which calls PathManager.CreatePath(...) with a hardcoded `ignoreCosts: false` argument.
    // This transpiler swaps that literal for a call to <VehicleAI>.IsEmergency(...), so emergency
    // dispatches ask the pathfinder to ignore path costs entirely - a coarser lever than the
    // originally planned "reduce congestion weighting", but the only one that survived contact
    // with Mono/TMPE reality.
    //
    // AmbulanceAI, FireTruckAI and PoliceCarAI all compile to byte-identical IL for this method
    // (confirmed via dnSpy) - same StartPathFind override shape, same IsEmergency signature -
    // so one shared transpiler body handles all three via a per-type wrapper method (Harmony
    // transpilers must be plain static methods, so the owner type can't be a runtime parameter
    // of the patched delegate itself).
    //
    // Only one `ref` struct parameter (Vehicle) per target method - same safe shape as the
    // vanilla ProcessItemCosts transpiler that was confirmed working in-game.
    internal static class EmergencyIgnoreCostsPatch
    {
        public static IEnumerable<CodeInstruction> TranspileFor(IEnumerable<CodeInstruction> instructions, Type ownerType)
        {
            var code = new List<CodeInstruction>(instructions);
            var matcher = new CodeMatcher(code);

            // Four back-to-back `false` literals (stablePath, skipQueue, randomParking,
            // ignoreCosts) followed by `this.CombustionEngine()` (ignoreFlooded) then the
            // CreatePath call itself - a distinctive, unlikely-to-collide anchor.
            matcher.MatchStartForward(
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(i => i.opcode == OpCodes.Callvirt && ((MethodInfo)i.operand).Name == "CombustionEngine"),
                new CodeMatch(i => i.opcode == OpCodes.Callvirt && ((MethodInfo)i.operand).Name == "CreatePath"));

            if (!matcher.IsValid)
            {
                Debug.LogWarning(
                    "[AIImprove] EmergencyIgnoreCostsPatch (" + ownerType.Name + "): CreatePath call " +
                    "pattern not found - game version may have changed StartPathFind. Skipping " +
                    "transpiler changes; rest of the mod is unaffected.");
                return code;
            }

            MethodInfo isEmergency = AccessTools.Method(ownerType, "IsEmergency", new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });
            if (isEmergency == null)
            {
                Debug.LogWarning(
                    "[AIImprove] EmergencyIgnoreCostsPatch (" + ownerType.Name + "): IsEmergency method " +
                    "not found. Skipping transpiler changes; rest of the mod is unaffected.");
                return code;
            }

            // Advance to the 4th Ldc_I4_0 (ignoreCosts) and replace it with:
            //   LogAndReturn(this.IsEmergency(vehicleID, ref vehicleData), "<ownerType.Name>")
            // The logging wrapper dup's the bool onto the stack so CreatePath still gets the
            // real value - see LogAndReturn for why this exists (lesson from the dead PathFind
            // patches: "applied successfully" only proves the IL compiled, not that it runs).
            MethodInfo logAndReturn = AccessTools.Method(typeof(EmergencyIgnoreCostsPatch), nameof(LogAndReturn));
            matcher.Advance(3);
            matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_0));
            matcher.InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, isEmergency),
                new CodeInstruction(OpCodes.Ldstr, ownerType.Name),
                new CodeInstruction(OpCodes.Call, logAndReturn));

            Debug.Log("[AIImprove] EmergencyIgnoreCostsPatch (" + ownerType.Name + ") transpiler applied successfully.");

            return matcher.InstructionEnumeration();
        }

        // How often to log an emergency hit while validating the patch actually fires during
        // real play. StartPathFind runs far less often than PathFind's per-segment cost calc,
        // so a short interval is fine here (unlike VanillaEmergencyCongestionPatch's 200).
        private const int DiagnosticLogInterval = 5;
        private static readonly Dictionary<string, long> HitCounts = new Dictionary<string, long>();
        private static readonly HashSet<string> LoggedFirstCall = new HashSet<string>();

        // Called from the injected IL, once per StartPathFind call. Returns isEmergency
        // unchanged - this only exists to prove the patch is actually executing.
        private static bool LogAndReturn(bool isEmergency, string ownerTypeName)
        {
            if (LoggedFirstCall.Add(ownerTypeName))
            {
                Debug.Log("[AIImprove] EmergencyIgnoreCostsPatch (" + ownerTypeName + ") is executing (sanity check, fires for every path request, not just emergencies).");
            }

            if (!isEmergency)
            {
                return false;
            }

            long count;
            HitCounts.TryGetValue(ownerTypeName, out count);
            count++;
            HitCounts[ownerTypeName] = count;

            if (count % DiagnosticLogInterval == 1)
            {
                Debug.Log("[AIImprove] " + ownerTypeName + " emergency dispatch #" + count + " requested ignoreCosts=true.");
            }

            return true;
        }
    }

    internal static class AmbulanceIgnoreCostsPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            EmergencyIgnoreCostsPatch.TranspileFor(instructions, typeof(AmbulanceAI));
    }

    internal static class FireTruckIgnoreCostsPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            EmergencyIgnoreCostsPatch.TranspileFor(instructions, typeof(FireTruckAI));
    }

    internal static class PoliceCarIgnoreCostsPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            EmergencyIgnoreCostsPatch.TranspileFor(instructions, typeof(PoliceCarAI));
    }
}
