using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AIImprove
{
    // Fixes reported "all trains pile onto one platform" / "planes never pick a free gate":
    // TrainAI.StartPathFind and AircraftAI.StartPathFind both compute their destination as the
    // target BUILDING's center point (Building.m_position), then call
    // PathManager.FindPathPosition(endPos, ...), which deterministically finds the single
    // nearest platform/gate lane to that point. Every train or plane heading to the same
    // station/airport gets the exact same endPos, so they all resolve to the exact same
    // platform/gate lane - see Cities_Skylines_1_AI_Improve_Document/03, "火車/飛機" entry
    // (2026-08-12).
    //
    // This does not do true occupancy-based load balancing (that needs enumerating a station's
    // platform tracks and checking current occupancy - a separate, larger feature). Instead it
    // adds a small deterministic per-vehicle offset to endPos before FindPathPosition runs, so
    // different vehicles' nearest-lane searches land on different platforms/gates rather than
    // all agreeing on the exact same one. Same category of fix as TMPE's own
    // LaneRandomizationCostFactor for junction lane selection (see
    // Cities_Skylines_1_AI_Improve_Document/01).
    //
    // Prefix, not Transpiler: endPos is a plain (non-ref-struct) Vector3 parameter on this
    // overload, so a Prefix declaring it `ref` can modify it in place before the original method
    // runs - Harmony supports this for any by-value parameter of the patched method.
    internal static class PlatformGateJitterPatch
    {
        // Radius within which the jittered point still resolves to a "reasonably close"
        // platform/gate rather than an unrelated one several blocks away. Real-world platform/
        // gate spacing in this game is usually well under this, so a fixed radius (rather than
        // querying actual platform positions, which needs the larger occupancy-aware rewrite)
        // is a reasonable first pass.
        private const float JitterRadius = 20f;

        // Keyed by MethodBase.DeclaringType.Name (set at patch time via a closure-free lookup
        // in LogFirstCall) so TrainAI and AircraftAI each log their own first-call sanity check
        // independently.
        private static readonly HashSet<string> LoggedFirstCall = new HashSet<string>();

        public static void Prefix(ushort vehicleID, ref Vector3 endPos, MethodBase __originalMethod)
        {
            LogFirstCall(__originalMethod.DeclaringType.Name);
            endPos += JitterOffset(vehicleID);
        }

        private static void LogFirstCall(string ownerTypeName)
        {
            if (LoggedFirstCall.Add(ownerTypeName))
            {
                Debug.Log("[AIImprove] PlatformGateJitterPatch (" + ownerTypeName + ") is executing.");
            }
        }

        // Deterministic per vehicle ID (not per call) so repeated re-pathing for the same
        // vehicle consistently aims at roughly the same platform/gate instead of flip-flopping
        // between them on every recalculation.
        private static Vector3 JitterOffset(ushort vehicleId)
        {
            uint hash = (uint)vehicleId * 2654435761u; // Knuth multiplicative hash
            float angle = (hash % 360u) * Mathf.Deg2Rad;
            float distance = (hash / 360u % 100u) * 0.01f * JitterRadius;

            return new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        }
    }
}
