using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AIImprove
{
    // Soft-dependency detection for Workshop mods this project was asked to consider
    // "integrating" (2026-08-14) - see 07 - 開發路線圖與里程碑.md for the investigation. Their
    // real mechanisms turned out to be mature, complex, and already solve their respective
    // problems: SingleTrainTrackAI's ReservationManager fully redirects
    // TrainAI.UpdatePathTargetPositions to implement single-track reservation (confirmed via
    // decompiling the actual mod), and Reversible Tram AI Harmony-patches the same method for
    // trams. Reimplementing either ourselves would be redundant at best - SingleTrainTrackAI's
    // own Workshop page explicitly says it's incompatible with any other mod that touches
    // UpdatePathTargetPositions. Optional/soft dependency here means: detect them at runtime and
    // defer/stay quiet, not duplicate their work. Same reflection-only, no-compile-time-reference
    // approach as TmpeCompat.cs - builds and runs fine whether or not either is installed.
    internal static class CompanionModCompat
    {
        private const string SingleTrainTrackAiTypeName = "SingleTrackAI.SingleTrainTrackAI";
        private const string ReversibleTramAiTypeName = "ReversibleTramAI.Mod";

        // BUG FOUND VIA SCREENSHOT (2026-08-14): a user with Advanced Vehicle Options installed
        // had a train showing 31968 passenger capacity. AVO lets players set an explicit custom
        // capacity per vehicle asset; our own capacity-boost patches (TrainPassengerCapacityPatch,
        // PassengerHelicopterCapacityPatch) unconditionally multiply
        // whatever m_passengerCapacity they find by a fixed factor, with no awareness that the
        // "original" value they captured might already be an intentional custom number from AVO
        // rather than the vanilla default - the two stack multiplicatively (e.g. AVO's own 15984
        // -> our x2 -> 31968). The fix isn't a bug in our multiplier itself (it's a clean, stable
        // 2x every time - confirmed via log), it's that we shouldn't be doubling a number the
        // player already explicitly chose. When AVO is present, defer to it entirely instead of
        // stacking on top - same "detect and stay passive" philosophy as SingleTrainTrackAI/
        // Reversible Tram AI above.
        private const string AdvancedVehicleOptionsTypeName = "AdvancedVehicleOptionsUID.AdvancedVehicleOptionsLoader";

        // "改為可支援 Real Time 模組" (2026-09-04). This one deliberately changes no behavior.
        //
        // CORRECTED 2026-09-09 - the original claim here was "the two mods do not overlap at all",
        // and that was too broad. It was reached by decompiling RealTime's ResidentAIPatch and
        // finding no reference to GetVehicleInfo or the probability methods - a true observation
        // about ONE class, written up as a conclusion about the whole assembly. The shared-patch
        // report added the same week found the rest of it on its first run:
        //
        //   FireTruckAI.SetTarget  and  FireCopterAI.SetTarget  are patched by Real Time.
        //
        // Those are the fire dispatch path, which this mod's fire response cap also works on. So
        // there IS an overlap, and the earlier text denied it.
        //
        // What the overlap actually does, decompiled rather than assumed (RealTime's
        // VehicleAIPatch): both are void Prefixes that never touch the target, never return
        // false, and only call RealTime's own RemoveBuildingFire bookkeeping when a truck is
        // released or the fire is already out. The original method always runs and the dispatch
        // outcome is unchanged. The conclusion "supported, nothing to defer to" therefore still
        // holds - but it now rests on knowing what the shared patch does, rather than on a belief
        // that there was no shared patch.
        //
        // The scheduling half of the original note is unchanged and still accurate: Real Time
        // decides *when* a citizen leaves and reaches vanilla HumanAI.StartMoving through its own
        // delegate, so GetVehicleInfo still runs underneath it.
        //
        // Adding a compatibility workaround here would therefore be inventing a problem. What is
        // genuinely useful is recording, in the log, whether the player has it - bug reports
        // arrive as an output_log.txt and until now nothing in it said which companion mods were
        // present. Supported means detected, logged and documented; it is not a requirement.
        // "我希望我們的功能是可以讓使用者直接取代 ExpressBusServices" (2026-09-09). Until this
        // mod's transit work is a genuine replacement, the two must not both be steering the same
        // buses. Express Bus Services decides when a vehicle departs and which stops it serves;
        // AI_Improve's dwell shortening and unbunching decide when a vehicle departs. Running both
        // means two mods writing the same wait counter with different intentions - which is how
        // 2026-08-14 happened, except with two authors instead of one.
        //
        // So this is a hard stand-down rather than an advisory: when Express Bus Services is
        // present, AI_Improve's transit dwell features do nothing at all, regardless of their
        // toggles, and say so once in the log. The player keeps whichever mod they chose, and the
        // one that arrived first at this job keeps it.
        private const string ExpressBusServicesTypeName = "ExpressBusServices.ExpressBusServices";

        public static bool IsExpressBusServicesLoaded() => FindType(ExpressBusServicesTypeName) != null;


        // TM:PE ALSO PATCHES ResidentAI.GetVehicleInfo (found 2026-09-09 by the shared-patch
        // report; previously unknown, and not mentioned anywhere in this project's compatibility
        // notes). That is the exact method CitizenTransportModePatch replaces.
        //
        // TM:PE's is a Prefix returning bool - its TouristAI sibling, which is readable and
        // written the same way, replaces vanilla's vehicle choice outright. Two Prefixes that
        // both return false do not compose: whichever Harmony runs first wins and the other never
        // executes. So with TM:PE installed, "custom citizen transport weights" and TM:PE's own
        // vehicle choice are in a race, and one of them silently loses.
        //
        // Not defended against in code, deliberately. The feature is off by default and now sits
        // in the Experimental section, so nobody has it on by accident; and picking a winner here
        // would mean this mod deciding which of two mods the player actually wanted. What was
        // missing was that nobody knew - the shared-patch line in the log now says so every
        // session, and this note says so to whoever reads the code.

        private const string RealTimeTypeName = "RealTime.Core.RealTimeMod";

        public static bool IsRealTimeLoaded() => FindType(RealTimeTypeName) != null;

        // One Info line at startup listing which companion mods were detected. Info rather than
        // Verbose because it fires exactly once per session and is the first thing worth knowing
        // when reading somebody else's log - see Log.cs for why everything per-vehicle is not.
        public static void LogDetectedCompanions()
        {
            string detected = string.Join(", ", new[]
            {
                IsRealTimeLoaded() ? "Real Time" : null,
                IsSingleTrainTrackAiLoaded() ? "SingleTrainTrackAI" : null,
                IsReversibleTramAiLoaded() ? "Reversible Tram AI" : null,
                IsAdvancedVehicleOptionsLoaded() ? "Advanced Vehicle Options" : null,
                IsExpressBusServicesLoaded() ? "Express Bus Services (transit dwell features stand down)" : null,
                FindType(TmceModSettingsTypeName) != null ? "Transfer Manager CE" : null,
            }.Where(name => name != null).ToArray());

            Log.Info("[AIImprove] Companion mods detected: " +
                     (detected.Length == 0 ? "none" : detected));
        }

        public static bool IsSingleTrainTrackAiLoaded() => FindType(SingleTrainTrackAiTypeName) != null;

        public static bool IsReversibleTramAiLoaded() => FindType(ReversibleTramAiTypeName) != null;

        public static bool IsAdvancedVehicleOptionsLoaded() => FindType(AdvancedVehicleOptionsTypeName) != null;

        // BUG FOUND VIA USER REPORT (2026-08-14): "消防車派遣邏輯本身有問題" (idle trucks never
        // dispatched, wrong/not-nearest truck sent, bad multi-fire allocation). Root cause:
        // Transfer Manager CE's own FireVehicleAIPatch Prefixes the exact same
        // FireTruckAI.SetTarget method FireResponseCapPatch does, and both independently try to
        // pick/overwrite `targetBuilding` on the same call - whichever prefix runs second wins,
        // and our own cap-tracking has no idea what TMCE just assigned. Confirmed via decompiling
        // TransferManagerCE.dll: TransferManagerCore.Settings.ModSettings.GetSettings().FireTruckAI
        // / .FireCopterAI (both user-toggleable, default true) gate its own fire dispatch. When
        // these read true, FireResponseCapPatch now defers target *selection* to TMCE entirely -
        // it already does real nearest-fire matching (see FireVehicleAIPatch.FindBuildingWithFire,
        // 160m search) far better than our own simple "closest known-burning building" pool - and
        // only keeps its own value-add (10-per-building cap, 15-minute uncap) instead of also
        // trying to pick a target and fighting over it.
        private const string TmceModSettingsTypeName = "TransferManagerCore.Settings.ModSettings";

        public static bool IsTmceFireDispatchActive(bool isCopter)
        {
            Type settingsType = FindType(TmceModSettingsTypeName);
            if (settingsType == null)
            {
                return false;
            }

            try
            {
                MethodInfo getSettings = settingsType.GetMethod("GetSettings", BindingFlags.Public | BindingFlags.Static);
                object settings = getSettings?.Invoke(null, null);
                if (settings == null)
                {
                    return false;
                }

                PropertyInfo property = settingsType.GetProperty(isCopter ? "FireCopterAI" : "FireTruckAI");
                return property != null && (bool)property.GetValue(settings, null);
            }
            catch
            {
                // Reflection into another mod's internals is inherently fragile across its
                // updates - fail safe (assume not active, keep our own redirect logic) rather
                // than throw.
                return false;
            }
        }

        // PERF (2026-09-05): this used to walk AppDomain.CurrentDomain.GetAssemblies() on every
        // single call, with no caching at all - and GetAssemblies() allocates a fresh array each
        // time, over 100 entries deep for a heavily modded player. Two of the three callers sit on
        // hot paths: FireResponseCapPatch runs it per FireTruckAI/FireCopterAI.SetTarget, and
        // TrainSingleTrackConflictDetector ran it TWICE per train per tick - ahead of the
        // SimulationStagger check that exists precisely to make that path cheap. Same shape as the
        // 2026-08-15 audit finding where LoggedFirstCall's string hashing sat in front of the same
        // stagger: the optimization was there, the expensive work was just placed before it.
        //
        // DlcDetector already caches its own answer this way; this brings companion detection in
        // line with it.
        //
        // KNOWN LIMIT: results are cached for the session, negatives included. Cities: Skylines
        // can load assemblies at runtime (Content Manager enabling a mod mid-session), so a
        // companion mod enabled after this first ran will not be picked up until the next restart.
        // Accepted deliberately: every caller only asks about mods that must be present from
        // startup to matter, and the alternative is paying a full assembly scan forever.
        // Locked because the writers are on different threads: LogDetectedCompanions runs from
        // Patcher.PatchAll during load, while IsSingleTrainTrackAiLoaded / IsTmceFireDispatchActive
        // / IsAdvancedVehicleOptionsLoaded run from patches on the simulation thread. Load does
        // finish before simulation starts, so an actual overlap is unlikely - but a concurrently
        // written Dictionary can loop forever inside a resize rather than throwing, and the
        // symptom of that is a frozen game with nothing in the log. The lock is taken a handful of
        // times per session (once per distinct type name, then never again), so it costs nothing.
        // AirTrafficControlManager and EmergencyDispatchTracker guard their own state the same way.
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        private static Type FindType(string typeName)
        {
            lock (CacheLock)
            {
                Type cached;
                if (TypeCache.TryGetValue(typeName, out cached))
                {
                    return cached;
                }

                Type found = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType(typeName, throwOnError: false);
                    if (type != null)
                    {
                        found = type;
                        break;
                    }
                }

                TypeCache[typeName] = found;
                return found;
            }
        }
    }
}
