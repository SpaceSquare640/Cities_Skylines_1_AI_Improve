using UnityEngine;

namespace AIImprove
{
    // Prefixes GarbageTruckAI/HearseAI.SetTarget(ushort, ref Vehicle, ushort) - see
    // SanitationIdleSeekTracker.cs for the full rationale. Mirrors FireResponseCapPatch's idle-seek
    // half: record every real (non-zero) target vanilla assigns, and when a vehicle with room to
    // spare is about to go idle (targetBuilding == 0, still collecting, not full), search that pool
    // for the nearest building that still genuinely needs the material and send it there directly
    // instead of leaving it to TransferManager's own priority/distance-weighted matching.
    //
    // WRONG, CORRECTED 2026-09-05: the note that used to sit here said no per-building cap was
    // needed "unlike fire", because "TransferManager's own Amount-based offer matching already
    // prevents over-collection at one building". That matching is exactly what this patch bypasses
    // when it sets targetBuilding directly, so nothing was limiting how many idle vehicles piled
    // onto the same building - the player-reported swarm. The cap now lives in
    // SanitationIdleSeekTracker (MaxVehiclesPerBuilding), applied via Assign/Observe here.
    internal static class SanitationIdleSeekPatch
    {
        private static bool loggedFirstCall;

        private static void Apply(
            string ownerTypeName, TransferManager.TransferReason material, VehicleAI aiInstance,
            ushort vehicleID, ref Vehicle data, ref ushort targetBuilding)
        {
            bool enabled = material == TransferManager.TransferReason.Dead
                ? ModSettings.HearseIdleSeekEnabled.value
                : ModSettings.GarbageIdleSeekEnabled.value;

            // BUG FOUND VIA AUDIT (2026-08-23): Observe used to run unconditionally, including
            // while the toggle is off, with the "pool stays warm for whenever it's turned on"
            // rationale left in the comment below. That comment missed that the *only* place
            // stale entries get pruned is inside TryFindNearby, a few lines down - which never
            // runs at all while the toggle is off. Net effect: with idle-seek disabled, this
            // silently grew KnownGarbageBuildings/KnownDeadBuildings for the entire session with
            // no cleanup ever running - the opposite of every other feature's "off = never
            // written" contract (see FireResponseTracker.TryAssign for the pattern this was
            // supposed to mirror). Gating Observe behind the same toggle fixes both at once.
            if (!enabled)
            {
                return;
            }

            if (targetBuilding != 0)
            {
                // Vanilla itself just proved this building has the need - record it as a future
                // idle-seek candidate.
                SanitationIdleSeekTracker.Observe(material, vehicleID, targetBuilding);
                return;
            }

            // Only the outbound "collecting" leg goes looking for more work. TransferToSource is
            // vanilla's own flag for that leg (dnSpy-confirmed against GarbageTruckAI/HearseAI's
            // SimulationStep and SetTarget) - the return-to-depot leg (GoingBack, or
            // TransferToTarget after picking up a full load) is left alone entirely.
            if ((data.m_flags & Vehicle.Flags.TransferToSource) == 0)
            {
                return;
            }

            int size;
            int max;
            aiInstance.GetSize(vehicleID, ref data, out size, out max);
            if (size >= max)
            {
                // Full - vanilla's own "return to source" branch is what should run next, not us
                // sending it to one more building.
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] SanitationIdleSeekPatch is executing.");
            }

            ushort nearby = SanitationIdleSeekTracker.TryFindNearby(material, data.m_sourceBuilding, data.GetLastFramePosition());
            if (nearby == 0)
            {
                // Nothing to send it to - make sure it isn't still counted against whatever it was
                // previously assigned to, or the cap would drift upward over the session.
                SanitationIdleSeekTracker.Assign(material, vehicleID, 0);

                // Nothing found - fall through to vanilla's own AddIncomingOffer-and-wait path
                // unchanged.
                return;
            }

            if (Log.VerboseEnabled)
            {
                Log.Verbose(
                    "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " was going idle - " +
                    "retargeted to nearby building " + nearby + " still needing collection instead.");
            }

            SanitationIdleSeekTracker.Assign(material, vehicleID, nearby);
            targetBuilding = nearby;
        }

        internal static class Garbage
        {
            public static void Prefix(GarbageTruckAI __instance, ushort vehicleID, ref Vehicle data, ref ushort targetBuilding) =>
                Apply(nameof(GarbageTruckAI), TransferManager.TransferReason.Garbage, __instance, vehicleID, ref data, ref targetBuilding);
        }

        internal static class Hearse
        {
            public static void Prefix(HearseAI __instance, ushort vehicleID, ref Vehicle data, ref ushort targetBuilding) =>
                Apply(nameof(HearseAI), TransferManager.TransferReason.Dead, __instance, vehicleID, ref data, ref targetBuilding);
        }
    }
}
