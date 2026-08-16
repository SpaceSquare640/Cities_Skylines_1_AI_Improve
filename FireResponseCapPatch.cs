using System.Collections.Generic;
using UnityEngine;

namespace AIImprove
{
    // Prefixes FireTruckAI/FireCopterAI.SetTarget(ushort, ref Vehicle, ushort) - called every
    // time a truck/helicopter's target building is (re)assigned, including the initial
    // dispatch. If the target building already has FireResponseTracker.MaxRespondersPerBuilding
    // vehicles of this type responding (and hasn't passed the 15-minute uncapped threshold -
    // see FireResponseTracker), the vehicle is force-redirected to another building that is
    // still burning and still has room, per explicit user request ("被擋下的車輛應該強制改派去
    // 「其他」還在燒的建築物，而不是回站待命"). Only falls back to targetBuilding = 0 (idle) if
    // no such alternate building exists.
    //
    // REVISED (2026-08-14): also retargets genuinely IDLE vehicles - i.e. targetBuilding == 0,
    // vanilla's own dispatch logic found no further work for this vehicle - not just ones
    // blocked by the cap. This is the core idea behind the Steam Workshop mod "Smarter
    // Firefighters: Improved AI" (id 2346565561): a truck/helicopter that just finished a job
    // and is heading back to the station, or waiting idle, should check for nearby fires first
    // instead of always accepting idle and waiting for vanilla's own (distance-blind)
    // offer-matching to maybe send it clear across the map next time. This mod already had the
    // building-search infrastructure (TryFindAlternateBurningBuilding) from the cap-overflow
    // case above; the only change needed was calling it here too, now with the vehicle's own
    // position so it genuinely prioritizes *nearby* fires (see FireResponseTracker's notes) - the
    // same core behavior as Smarter Firefighters, but combined with this mod's own 10-responder
    // cap and 15-minute-uncap system, which that mod doesn't have at all.
    //
    // Only one `ref Vehicle` parameter on this method - safe Prefix shape, same as everything
    // else in this project.
    //
    // REVISED (2026-08-14): defers target *selection* to Transfer Manager CE when its own fire
    // dispatch is active (see CompanionModCompat.IsTmceFireDispatchActive) - both mods used to
    // independently pick/overwrite `targetBuilding` on the exact same SetTarget call, with no
    // awareness of each other, causing wrong-truck/not-nearest/bad-multi-fire-allocation
    // dispatch per a real user report. TMCE's own nearest-fire search is more capable than this
    // mod's; when it's active this patch only still enforces the cap (still blocks/redirects to
    // idle when a building is over MaxRespondersPerBuilding) but stops trying to pick an
    // alternate building itself - TMCE's own periodic idle-vehicle rescan (see its
    // FireTruckAISimulationStepPostfix) picks the vehicle back up on its own next pass.
    internal static class FireResponseCapPatch
    {
        private static bool loggedFirstCall;

        // BUG FOUND VIA PLAYER REPORT (2026-08-16): "建築物火災，但沒有派遣任何消防車輛或直升機".
        // A real log confirmed the cause: 663 consecutive redirects, all for the same building,
        // all "leaving target selection to Transfer Manager CE" (see below) - TMCE kept sending
        // fresh helicopters at that same already-saturated building instead of ever picking a
        // different one, so the fire never actually got serviced despite the mod "handling" every
        // single dispatch attempt. The assumption in the 2026-08-14 revision below - that TMCE's
        // own idle-vehicle rescan would eventually try somewhere else - doesn't hold in practice.
        //
        // Fix: track how many *consecutive* times a building has been punted to TMCE with no
        // successful assignment landing there in between. Past this threshold, TMCE has
        // demonstrably failed to move on from this building on its own, so this patch stops
        // trusting it for that specific building and falls back to its own
        // TryFindAlternateBurningBuilding search instead - same as the non-TMCE path already
        // does. Resets the moment a real assignment succeeds there again (fire went out, cap
        // lifted after the uncap timer, or TMCE finally did pick somewhere else on its own).
        private const int TmceStarvationThreshold = 8;
        private static readonly Dictionary<ushort, int> TmcePuntStreak = new Dictionary<ushort, int>();

        private static void ResetPuntStreak(ushort buildingId)
        {
            TmcePuntStreak.Remove(buildingId);
        }

        private static bool TmceIsStarvingBuilding(ushort buildingId)
        {
            // Only ever counts up while the building keeps being punted; ResetPuntStreak drops the
            // entry entirely on any successful assignment, so this dictionary stays proportional
            // to buildings currently stuck rather than every building that was ever capped.
            // Deliberately not growing it for a building whose fire has since gone out - callers
            // only reach here while that building is an active, capped dispatch target.
            int streak;
            TmcePuntStreak.TryGetValue(buildingId, out streak);
            streak++;
            TmcePuntStreak[buildingId] = streak;
            return streak > TmceStarvationThreshold;
        }

        private static void Apply(string ownerTypeName, bool isCopter, ushort vehicleID, ref Vehicle data, ref ushort targetBuilding)
        {
            // "我想把全部功能拆開" (2026-08-15): the cap and the idle-seek behaviour used to
            // share one "Emergency vehicles" switch; they're independent now
            // (FireResponseCapEnabled / FireIdleSeekEnabled). If the cap is off, TryAssign is
            // never consulted at all - not "consulted but always allowed", genuinely skipped, so
            // FireResponseTracker's per-building counts stay untouched, matching the "off = never
            // written" contract.
            if (!ModSettings.FireResponseCapEnabled.value && !ModSettings.FireIdleSeekEnabled.value)
            {
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] FireResponseCapPatch is executing.");
            }

            bool tmceOwnsDispatch = CompanionModCompat.IsTmceFireDispatchActive(isCopter);

            if (targetBuilding == 0)
            {
                if (ModSettings.FireResponseCapEnabled.value)
                {
                    FireResponseTracker.TryAssign(isCopter, vehicleID, 0);
                }

                if (!ModSettings.FireIdleSeekEnabled.value)
                {
                    return;
                }

                if (tmceOwnsDispatch)
                {
                    // Let TMCE's own FireTruckAI/FireCopterAI dispatch pick the next target -
                    // don't also search and risk overwriting a choice it makes on the very same
                    // call.
                    return;
                }

                ushort nearby = FireResponseTracker.TryFindAlternateBurningBuilding(isCopter, 0, data.GetLastFramePosition());
                if (nearby != 0 && FireResponseTracker.TryAssign(isCopter, vehicleID, nearby))
                {
                    Log.Verbose(
                        "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " was going idle - " +
                        "retargeted to nearby still-burning building " + nearby + " instead.");
                    targetBuilding = nearby;
                }

                return;
            }

            // BUG FOUND VIA AUDIT (2026-08-15, prompted by a player report of stuck fire trucks):
            // this call ran unconditionally regardless of FireResponseCapEnabled - if a player
            // turned the cap off specifically (while leaving idle-seek on), TryAssign kept
            // rejecting dispatches past MaxRespondersPerBuilding anyway, contradicting "off =
            // never written". Doesn't affect the default (both features on) configuration, but is
            // a real toggle-not-actually-off bug regardless.
            if (!ModSettings.FireResponseCapEnabled.value)
            {
                return;
            }

            if (FireResponseTracker.TryAssign(isCopter, vehicleID, targetBuilding))
            {
                ResetPuntStreak(targetBuilding);
                return;
            }

            {
                ushort original = targetBuilding;

                if (tmceOwnsDispatch && !TmceIsStarvingBuilding(original))
                {
                    // Still enforce the cap (this building has enough responders already), but
                    // leave picking the replacement target to TMCE's own next dispatch pass
                    // instead of searching ourselves.
                    Log.Verbose(
                        "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " redirected away " +
                        "from building " + original + " - already at " +
                        FireResponseTracker.MaxRespondersPerBuilding + " responders. Leaving target " +
                        "selection to Transfer Manager CE instead of picking one ourselves.");
                    targetBuilding = 0;
                    return;
                }

                if (tmceOwnsDispatch && Log.VerboseEnabled)
                {
                    Log.Verbose(
                        "[AIImprove] " + ownerTypeName + ": building " + original + " punted to Transfer " +
                        "Manager CE more than " + TmceStarvationThreshold + " times in a row with no " +
                        "successful assignment landing there - Transfer Manager CE isn't moving on from " +
                        "it on its own, picking an alternate building ourselves instead.");
                }

                ushort alternate = FireResponseTracker.TryFindAlternateBurningBuilding(isCopter, original, data.GetLastFramePosition());

                if (alternate != 0 && FireResponseTracker.TryAssign(isCopter, vehicleID, alternate))
                {
                    // The vehicle landed somewhere else, not `original` - original's own streak
                    // is left alone (still unresolved) but the alternate building just proved it
                    // isn't starved, so make sure it doesn't inherit a stale streak from a past
                    // fire at the same building ID.
                    ResetPuntStreak(alternate);

                    Log.Verbose(
                        "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " redirected from " +
                        "building " + original + " (at " + FireResponseTracker.MaxRespondersPerBuilding +
                        " responders) to still-burning building " + alternate + ".");
                    targetBuilding = alternate;
                }
                else
                {
                    Log.Verbose(
                        "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " redirected away " +
                        "from building " + original + " - already at " +
                        FireResponseTracker.MaxRespondersPerBuilding + " responders, no alternate fire found.");
                    targetBuilding = 0;
                }
            }
        }

        internal static class Truck
        {
            public static void Prefix(ushort vehicleID, ref Vehicle data, ref ushort targetBuilding) =>
                Apply(nameof(FireTruckAI), false, vehicleID, ref data, ref targetBuilding);
        }

        internal static class Copter
        {
            public static void Prefix(ushort vehicleID, ref Vehicle data, ref ushort targetBuilding) =>
                Apply(nameof(FireCopterAI), true, vehicleID, ref data, ref targetBuilding);
        }
    }
}
