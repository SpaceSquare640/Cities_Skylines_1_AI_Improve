using System.Collections.Generic;
using UnityEngine;

namespace AIImprove
{
    // Real, visible circling for aircraft waiting for a free gate - v2 of the "problem 3"
    // airport-saturation handling (see AircraftGateAssignmentPatch.cs and
    // Cities_Skylines_1_AI_Improve_Document/01). Directly manipulates the same flight target
    // waypoints (Vehicle.m_targetPos0/m_targetPos1, a Vector4 where .w is altitude) and
    // Flying/Landing/TakingOff flags that AircraftAI.SimulationStep's own flight physics reads
    // every tick - the same technique HelicopterAI itself uses to fly point-to-point (see
    // PlatformGateJitterPatch's notes on that), applied here to hold a circular pattern instead
    // of a single destination.
    //
    // EXPERIMENTAL: this overlaps with the game's own flight-state machine in a way that hasn't
    // been exercised by any known mod - expect this to need live-test iteration, unlike most of
    // this project's other patches which worked close to first try.
    internal static class HoldingPatternManager
    {
        private const float HoldRadius = 300f;

        // Comfortably above the game's own `m_targetPos0.w < 40f` "close enough to land" check
        // (see AircraftAI.SimulationStep's Flying-state landing trigger) so holding never
        // accidentally self-triggers a landing attempt.
        private const float HoldAltitude = 200f;

        private const float AngularSpeedDegPerSecond = 15f;

        // Safety valve: force a real landing attempt after this long regardless of gate
        // occupancy, so a saturated airport can never hold a plane forever.
        private const float MaxHoldSeconds = 90f;

        private class HoldState
        {
            public ushort TargetBuilding;
            public Vector3 Center;
            public float StartTime;
        }

        private static readonly Dictionary<ushort, HoldState> Holding = new Dictionary<ushort, HoldState>();

        public static void BeginHolding(ushort vehicleId, ushort targetBuilding, Vector3 center)
        {
            Holding[vehicleId] = new HoldState
            {
                TargetBuilding = targetBuilding,
                Center = center,
                StartTime = Time.realtimeSinceStartup
            };
        }

        public static bool IsHolding(ushort vehicleId, out ushort targetBuilding, out bool timedOut)
        {
            HoldState state;
            if (!Holding.TryGetValue(vehicleId, out state))
            {
                targetBuilding = 0;
                timedOut = false;
                return false;
            }

            targetBuilding = state.TargetBuilding;
            timedOut = (Time.realtimeSinceStartup - state.StartTime) >= MaxHoldSeconds;
            return true;
        }

        // Current point on the holding circle plus a "trailing" point a few degrees behind, so
        // the caller can set both m_targetPos0 (where to go) and m_targetPos1 (used by the
        // game's own turn/bank calculation to figure out heading) the same way the vanilla
        // Flying-state code already reads them.
        public static Vector4 GetCirclePosition(ushort vehicleId, out Vector4 trailingPosition)
        {
            HoldState state = Holding[vehicleId];
            float elapsedSeconds = Time.realtimeSinceStartup - state.StartTime;
            float angleDeg = elapsedSeconds * AngularSpeedDegPerSecond;
            float angleRad = angleDeg * Mathf.Deg2Rad;
            float trailingRad = (angleDeg - 5f) * Mathf.Deg2Rad;

            Vector3 pos = state.Center + new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)) * HoldRadius;
            Vector3 trailing = state.Center + new Vector3(Mathf.Cos(trailingRad), 0f, Mathf.Sin(trailingRad)) * HoldRadius;

            trailingPosition = new Vector4(trailing.x, HoldAltitude, trailing.z, HoldAltitude);
            return new Vector4(pos.x, HoldAltitude, pos.z, HoldAltitude);
        }

        public static void EndHolding(ushort vehicleId)
        {
            Holding.Remove(vehicleId);
        }
    }
}
