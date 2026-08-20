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
    // No per-building responder cap here, unlike fire - garbage/dead collection doesn't have fire's
    // "many vehicles pile onto the same target" problem; TransferManager's own Amount-based offer
    // matching already prevents over-collection at one building.
    internal static class SanitationIdleSeekPatch
    {
        private static bool loggedFirstCall;

        private static void Apply(
            string ownerTypeName, TransferManager.TransferReason material, VehicleAI aiInstance,
            ushort vehicleID, ref Vehicle data, ref ushort targetBuilding)
        {
            if (targetBuilding != 0)
            {
                // Vanilla itself just proved this building has the need - record it as a future
                // idle-seek candidate regardless of whether idle-seek is enabled, so the pool is
                // already warm the moment a player turns the toggle on.
                SanitationIdleSeekTracker.Observe(material, targetBuilding);
                return;
            }

            bool enabled = material == TransferManager.TransferReason.Dead
                ? ModSettings.HearseIdleSeekEnabled.value
                : ModSettings.GarbageIdleSeekEnabled.value;
            if (!enabled)
            {
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
