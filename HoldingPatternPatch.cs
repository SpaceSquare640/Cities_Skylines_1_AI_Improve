using System.Reflection;
using UnityEngine;

namespace AIImprove
{
    // Per-tick driver for HoldingPatternManager - called from FlexibleReroutePatch.Aircraft's
    // existing SimulationStep Postfix (reusing that hook rather than registering a second
    // Harmony patch on the same method) so holding planes don't also get evaluated for
    // congestion-based rerouting, which doesn't apply to them (they aren't on a real path).
    internal static class HoldingPatternPatch
    {
        // How often to re-run the gate candidate search while holding. Every tick would call
        // PathManager.FindPathPosition CandidateCount times per holding plane per frame - a real
        // cost - so this is throttled per vehicle, reusing the same realtime-based approach as
        // StuckRerouteTracker's cooldown.
        private const float RecheckIntervalSeconds = 5f;
        private static readonly System.Collections.Generic.Dictionary<ushort, float> LastRecheck =
            new System.Collections.Generic.Dictionary<ushort, float>();

        // Returns true if this vehicle was holding (and this call handled it - whether by
        // updating its circle position or by ending the hold), so the caller knows to skip its
        // own logic for this tick. Returns false immediately for any vehicle not holding.
        public static bool TryUpdateHolding(MethodInfo startPathFind, AircraftAI aiInstance, ushort vehicleID, ref Vehicle vehicleData)
        {
            ushort targetBuilding;
            bool timedOut;
            if (!HoldingPatternManager.IsHolding(vehicleID, out targetBuilding, out timedOut))
            {
                return false;
            }

            if (timedOut)
            {
                EndHoldingAndLand(startPathFind, aiInstance, vehicleID, ref vehicleData, targetBuilding, forcedByTimeout: true);
                return true;
            }

            if (ShouldRecheckNow(vehicleID) && TargetHasCapacity(aiInstance, targetBuilding))
            {
                EndHoldingAndLand(startPathFind, aiInstance, vehicleID, ref vehicleData, targetBuilding, forcedByTimeout: false);
                return true;
            }

            Vector4 trailing;
            Vector4 circlePos = HoldingPatternManager.GetCirclePosition(vehicleID, out trailing);
            vehicleData.m_targetPos1 = trailing;
            vehicleData.SetTargetPos(0, circlePos);
            vehicleData.m_flags |= Vehicle.Flags.Flying;
            vehicleData.m_flags &= ~(Vehicle.Flags.Landing | Vehicle.Flags.TakingOff | Vehicle.Flags.WaitingPath);

            return true;
        }

        private static bool ShouldRecheckNow(ushort vehicleID)
        {
            float now = Time.realtimeSinceStartup;
            float last;
            if (LastRecheck.TryGetValue(vehicleID, out last) && now - last < RecheckIntervalSeconds)
            {
                return false;
            }

            LastRecheck[vehicleID] = now;
            return true;
        }

        // Re-runs AircraftGateAssignmentPatch's own candidate search to see whether a gate has
        // actually opened up - not just a timer, a real occupancy check against the same data
        // the initial refusal was based on.
        private static bool TargetHasCapacity(AircraftAI aiInstance, ushort targetBuilding)
        {
            Vector3 airportCenter = ColossalFramework.Singleton<BuildingManager>.instance.m_buildings.m_buffer[targetBuilding].m_position;

            Vector3 bestPos;
            ushort bestSegment;
            int bestOccupancy;
            bool found = AircraftGateAssignmentPatch.TryFindBestGate(aiInstance.m_info, airportCenter, out bestPos, out bestSegment, out bestOccupancy);

            return found && !AircraftGateAssignmentPatch.IsSaturated(bestOccupancy);
        }

        private static void EndHoldingAndLand(
            MethodInfo startPathFind,
            AircraftAI aiInstance,
            ushort vehicleID,
            ref Vehicle vehicleData,
            ushort targetBuilding,
            bool forcedByTimeout)
        {
            HoldingPatternManager.EndHolding(vehicleID);
            LastRecheck.Remove(vehicleID);

            // Restore the real destination we cleared on entering the hold (see
            // AircraftGateAssignmentPatch's saturation branch) before attempting the real
            // landing path, so StartPathFind and everything downstream of it sees correct
            // state instead of the "no target" placeholder used while circling.
            vehicleData.m_targetBuilding = targetBuilding;

            Vector3 endPos = ColossalFramework.Singleton<BuildingManager>.instance.m_buildings.m_buffer[targetBuilding].m_position;
            Vector3 startPos = vehicleData.m_targetPos0;

            // This reflectively invokes the same Harmony-patched StartPathFind, which re-enters
            // AircraftGateAssignmentPatch.Prefix - and the airport is almost always still
            // saturated right when a hold times out (that's why it was holding). Without this,
            // the forced landing attempt just hit the same saturation branch again and either
            // re-entered holding or failed outright, so timed-out planes could never actually
            // land (confirmed live: 785 holds started, only 26 ever exited, 100% of those
            // failed). Forcing gate assignment here guarantees this attempt always lands somewhere
            // rather than bouncing back into another hold.
            AircraftGateAssignmentPatch.BeginForceAssign(vehicleID);
            bool success;
            try
            {
                object[] args = { vehicleID, vehicleData, startPos, endPos, true, true };
                success = (bool)startPathFind.Invoke(aiInstance, args);
                vehicleData = (Vehicle)args[1];
            }
            finally
            {
                AircraftGateAssignmentPatch.EndForceAssign(vehicleID);
            }

            Debug.Log(
                "[AIImprove] Aircraft " + vehicleID + " ending holding pattern (" +
                (forcedByTimeout ? "timed out" : "gate now available") + "), landing attempt: " +
                (success ? "accepted" : "failed"));
        }
    }
}
