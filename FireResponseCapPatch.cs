using ColossalFramework;
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
        // Called by TrackerReset when a save is unloaded. Building, vehicle and node IDs are
        // recycled from fixed pools, so anything left here from the previous city would be read
        // back as if it described the new one. Registered centrally rather than relied on being
        // remembered per class - see 12 - 開發準則, 準則 3.
        public static void ResetForNewLevel()
        {
            lock (PuntStreakLock)
            {
                TmcePuntStreak.Clear();
            }
        }

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
        // ADDED 2026-09-18 AFTER REVIEW. SetTarget is patched as a Prefix on FireTruckAI and
        // FireCopterAI (Patcher.cs), and Cities: Skylines steps vehicle AI across several
        // simulation threads - so every line below can run on more than one thread at once. That
        // was survivable while this dictionary only saw TryGetValue / indexer-set / Remove, which
        // is why it went unlocked for a month. It stopped being survivable the moment
        // SweepExtinguishedPuntStreaks started ENUMERATING it: a foreach over a Dictionary that
        // another thread writes throws "Collection was modified", and in Mono a concurrent resize
        // can spin instead of throwing - a frozen game with an empty log, exactly what
        // FlexibleReroutePatch's CacheLock note describes.
        //
        // The lesson worth keeping: point access and enumeration are not the same risk. Adding a
        // single foreach converted a dormant assumption into a live race. Every access to
        // TmcePuntStreak and PuntStreakSweepScratch now goes through this lock; if you add
        // another, it goes through it too.
        private static readonly object PuntStreakLock = new object();

        private static readonly Dictionary<ushort, int> TmcePuntStreak = new Dictionary<ushort, int>();

        private static void ResetPuntStreak(ushort buildingId)
        {
            lock (PuntStreakLock)
            {
                TmcePuntStreak.Remove(buildingId);
            }
        }

        // The streak dictionary only ever had two ways out: a successful assignment at that
        // building, or ResetForNewLevel. Neither fires for the common ending - the fire simply
        // goes out while TMCE is still punting, and that building's entry sits there until the
        // save is unloaded. Bounded (one int per building ID, and IDs are a fixed pool) and
        // harmless to correctness, because a building ID reused by a new fire gets its stale
        // streak cleared by ResetPuntStreak on the first successful assignment. Still a leak, and
        // a stale streak can make a genuinely new fire look starved for one dispatch.
        //
        // Swept lazily rather than on a timer: the sweep costs one array read per tracked
        // building and only runs once the dictionary is larger than any plausible set of
        // simultaneously-burning buildings, so in a normal city it never runs at all.
        private const int PuntStreakSweepThreshold = 64;

        // ADDED 2026-09-18 AFTER REVIEW. The threshold above gates whether the sweep runs, not how
        // often. Without this second gate, a city with 65+ buildings burning at once - a mass-fire
        // event, which is exactly what a thunderstorm produces - put a full dictionary walk on
        // EVERY fire truck dispatch, each one finding nothing to remove because every tracked
        // building really was still on fire. Worst possible moment for per-dispatch work, and the
        // original comment claimed the opposite was happening.
        //
        // 512 frames matches ShipQueueDetector's report interval. Unsigned subtraction so the wrap
        // of m_currentFrameIndex is benign, same idiom as there.
        private const uint PuntStreakSweepIntervalFrames = 512U;

        private static uint lastPuntStreakSweepFrame;

        private static void SweepExtinguishedPuntStreaks()
        {
            uint frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            if (frame - lastPuntStreakSweepFrame < PuntStreakSweepIntervalFrames)
            {
                return;
            }

            int removed;
            int remaining;

            lock (PuntStreakLock)
            {
                // Re-read the frame counter inside the lock rather than trusting the check above:
                // two threads can both pass it before either has written, and doing the whole
                // sweep twice back-to-back is precisely the cost this gate exists to avoid.
                if (frame - lastPuntStreakSweepFrame < PuntStreakSweepIntervalFrames)
                {
                    return;
                }

                lastPuntStreakSweepFrame = frame;

                if (TmcePuntStreak.Count <= PuntStreakSweepThreshold)
                {
                    return;
                }

                Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
                PuntStreakSweepScratch.Clear();

                foreach (KeyValuePair<ushort, int> pair in TmcePuntStreak)
                {
                    if (buildings[pair.Key].m_fireIntensity == 0)
                    {
                        PuntStreakSweepScratch.Add(pair.Key);
                    }
                }

                for (int i = 0; i < PuntStreakSweepScratch.Count; i++)
                {
                    TmcePuntStreak.Remove(PuntStreakSweepScratch[i]);
                }

                removed = PuntStreakSweepScratch.Count;
                remaining = TmcePuntStreak.Count;
            }

            // Outside the lock on purpose - string concatenation while holding a lock that every
            // fire truck dispatch in the city contends for is the kind of thing that turns a
            // correctness fix into a stutter.
            if (removed > 0 && Log.VerboseEnabled)
            {
                Log.Verbose(
                    "[AIImprove] Dropped " + removed + " Transfer Manager CE punt streak entries " +
                    "for buildings that are no longer burning; " + remaining + " still tracked.");
            }
        }

        // Scratch - cleared at the top of every sweep, never read outside it. Guarded by
        // PuntStreakLock like the dictionary it serves: it is one shared static List, so two
        // threads sweeping at once would Clear() it out from under each other.
        private static readonly List<ushort> PuntStreakSweepScratch = new List<ushort>();

        private static bool TmceIsStarvingBuilding(ushort buildingId)
        {
            // Only ever counts up while the building keeps being punted; ResetPuntStreak drops the
            // entry entirely on any successful assignment, so this dictionary stays proportional
            // to buildings currently stuck rather than every building that was ever capped.
            // Deliberately not growing it for a building whose fire has since gone out - callers
            // only reach here while that building is an active, capped dispatch target.
            int streak;
            lock (PuntStreakLock)
            {
                TmcePuntStreak.TryGetValue(buildingId, out streak);
                streak++;
                TmcePuntStreak[buildingId] = streak;
            }

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
                    // PERF (2026-08-24): these four Log.Verbose calls in this file concatenated
                    // unconditionally on the fire-dispatch hot path - see Log.cs's own guidance,
                    // callers must guard message-building with VerboseEnabled or pay the
                    // concatenation cost on every dispatch decision even with Verbose logging off.
                    if (Log.VerboseEnabled)
                    {
                        Log.Verbose(
                            "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " was going idle - " +
                            "retargeted to nearby still-burning building " + nearby + " instead.");
                    }

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

            SweepExtinguishedPuntStreaks();

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
                    if (Log.VerboseEnabled)
                    {
                        Log.Verbose(
                            "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " redirected away " +
                            "from building " + original + " - already at " +
                            FireResponseTracker.MaxRespondersPerBuilding + " responders. Leaving target " +
                            "selection to Transfer Manager CE instead of picking one ourselves.");
                    }

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

                    if (Log.VerboseEnabled)
                    {
                        Log.Verbose(
                            "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " redirected from " +
                            "building " + original + " (at " + FireResponseTracker.MaxRespondersPerBuilding +
                            " responders) to still-burning building " + alternate + ".");
                    }

                    targetBuilding = alternate;
                }
                else
                {
                    if (Log.VerboseEnabled)
                    {
                        Log.Verbose(
                            "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " redirected away " +
                            "from building " + original + " - already at " +
                            FireResponseTracker.MaxRespondersPerBuilding + " responders, no alternate fire found.");
                    }

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
