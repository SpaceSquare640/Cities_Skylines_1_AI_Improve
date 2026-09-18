using ColossalFramework;
using System.Collections.Generic;

namespace AIImprove
{
    // Detects an active thunderstorm disaster, for the "shut down helicopter services and close
    // airports during a thunderstorm" feature (per user request, 2026-08-13). Pure read against
    // DisasterManager.m_disasters (a public FastList<DisasterData>) - no Harmony patching
    // involved, same "just read the data" approach as SegmentCongestionQuery. Works whether or
    // not the Natural Disasters DLC content is unlocked - DisasterManager and ThunderStormAI are
    // both base-game types; the DLC only unlocks being able to trigger the disaster in-game, not
    // the types themselves.
    internal static class WeatherDisasterDetector
    {
        // Called by TrackerReset when a save is unloaded. Building, vehicle and node IDs are
        // recycled from fixed pools, so anything left here from the previous city would be read
        // back as if it described the new one. Registered centrally rather than relied on being
        // remembered per class - see 12 - 開發準則, 準則 3.
        public static void ResetForNewLevel()
        {
            LastReportedFlags.Clear();

            // Cleared here on purpose. The override below is the one piece of dev-only plumbing
            // that DOES ship, so it must not be able to survive into a city it was not set for.
            // Note the interaction this creates for whoever is using the dev panel: the
            // Ctrl+Shift+R hotkey calls TrackerReset.ResetAll(), which lands here, so forcing a
            // tracker reset also switches the storm override back off. That is the correct
            // behaviour - it is exactly the "stale flag from a previous city" case - but it will
            // look like the override turned itself off if you do not know why.
            forceThunderstormActive = false;
        }

        // DEV-ONLY OVERRIDE, AND THE ONLY DEV PLUMBING IN THE SHIPPED DLL.
        //
        // Written by nothing except dev/DevTriggerPanel.cs, which is not compiled into the
        // released build (see AIImprove.csproj, DevTools). In a player's copy this field is
        // therefore false at every moment of its existence: there is no settings UI for it, no
        // SavedBool backing it, no Harmony patch that writes it and no code path in the shipped
        // assembly that assigns anything but the `false` on the line above.
        //
        // It is here rather than inside the dev file because IsThunderstormActive must consult it,
        // and that method ships. Ten lines of permanently-false field is the price of keeping the
        // other ninety lines out of the DLL entirely.
        private static bool forceThunderstormActive;

        internal static bool ForceThunderstormActive
        {
            get { return forceThunderstormActive; }
            set { forceThunderstormActive = value; }
        }

        public static bool IsThunderstormActive()
        {
            // Ahead of the DLC gate deliberately: the point of the override is to exercise the
            // three consumers on a machine that may not own Natural Disasters, and the DLC gate
            // below would otherwise return false before the override was ever read.
            if (forceThunderstormActive)
            {
                return true;
            }

            // Skip the scan entirely without Natural Disasters (2026-08-14, per user request to
            // DLC-gate the remaining features). This is the one gate that buys something real:
            // every caller sits on a hot path - HelicopterWeatherHaltPatch runs on each emergency
            // helicopter dispatch and AircraftGateAssignmentPatch on each aircraft pathfind - and
            // without the DLC that whole list walk can only ever return false, forever.
            if (!DlcDetector.IsNaturalDisastersOwned())
            {
                return false;
            }

            FastList<DisasterData> disasters = Singleton<DisasterManager>.instance.m_disasters;
            if (disasters == null)
            {
                return false;
            }

            for (int i = 0; i < disasters.m_size; i++)
            {
                DisasterData.Flags flags = disasters.m_buffer[i].m_flags;
                if ((flags & (DisasterData.Flags.Created | DisasterData.Flags.Deleted)) != DisasterData.Flags.Created)
                {
                    continue;
                }

                // BUG FOUND VIA LOG ANALYSIS (2026-08-16), root cause of the player report
                // "建築物火災，但沒有派遣任何消防車輛或直升機":
                //
                // `Created && !Deleted` is NOT "this disaster is happening right now" - it only
                // means the record still occupies a slot. A thunderstorm that fully ran its course
                // keeps Created set (and stays undeleted) until the game eventually recycles the
                // slot, so this loop kept reporting an active storm long after the weather had
                // cleared. Measured in a real 62-minute session: emergency helicopters were
                // refused dispatch in 55 of those minutes continuously, and airports refused
                // 13,003 landings - a real thunderstorm lasts minutes, not an hour. Because
                // HelicopterWeatherHaltPatch grounds FireCopterAI along with the other emergency
                // copters, this is exactly why burning buildings got no response at all.
                //
                // SECOND FIX (2026-09-05): the above was right that Created is not "happening",
                // but wrong to accept Clearing alongside Active - Clearing means the storm is
                // already OVER. Decompiling DisasterAI settles it; the state machine is
                //
                //     Emerging -> Active -> Clearing -> Finished
                //
                // and it is DeactivateDisaster that sets Clearing, in the same breath as raising
                // OnDisasterDeactivated - which is precisely the "Disaster Deactivated ... Type:
                // ThunderStorm" event other disaster mods log when the weather stops. Clearing is
                // the aftermath phase (collapsed buildings, cleanup still running) and it lasts a
                // long time.
                //
                // Confirmed against a live session before this fix: the detector matched slot 55
                // with flags 9553 - Created, Clearing, SelfTrigger, Significant, Follow,
                // UnDetected - with Active NOT set, and airports plus emergency helicopters
                // stayed held for 5,929 refusals across 16 continuous minutes to the end of the
                // log. Same symptom as the original bug, one state later.
                //
                // Vanilla's own CanAffectAt does include Clearing, which is what the 2026-08-16
                // note was reaching for - but that method answers "can this place still take
                // damage" (fires still spread during cleanup), not "is the weather bad right
                // now". Grounding aircraft is the second question, so Active alone is the test.
                // Emerging stays excluded as before: the storm has not hit yet.
                if ((flags & DisasterData.Flags.Active) == DisasterData.Flags.None)
                {
                    continue;
                }

                DisasterInfo info = disasters.m_buffer[i].Info;
                if (info != null && info.m_disasterAI is ThunderStormAI)
                {
                    if (Log.VerboseEnabled)
                    {
                        ReportMatch((ushort)i, flags);
                    }

                    return true;
                }
            }

            return false;
        }

        // "可以查實機 game log" (2026-09-05): the 2026-08-16 fix above is not holding, and this
        // exists to find out which flag is actually still set rather than guessing a third time.
        //
        // Evidence from a live 38-minute session: Natural Disasters Renewal logs each disaster's
        // lifecycle, and the last thunderstorm was deactivated at 24.9 minutes
        // ("Disaster Deactivated: ... Type: ThunderStorm ... FinishOnDeactivate:True"). Airports
        // kept refusing landings and emergency helicopters kept being grounded from 0.9 seconds
        // after that, continuously, until the log ended 13.7 minutes later - 2,387 of the
        // session's 4,165 refusals, 57%, with no active storm anywhere. Same shape as the original
        // bug, and with FireCopterAI grounded alongside the others it is once again a live cause
        // of burning buildings getting no response.
        //
        // What is NOT yet known is why: whether Active is never cleared, whether Clearing latches,
        // or whether this matches some record the deactivation log does not correspond to at all.
        // Natural Disasters Renewal manages disaster lifecycle itself, so its "Deactivated" event
        // need not coincide with vanilla's flag transitions - which the 2026-08-16 analysis, done
        // against vanilla DisasterManager alone, had no way to account for.
        //
        // So: log the disaster's index and its full flag bits the first time a given record
        // matches, and again whenever that record's flags change. One line per state transition,
        // Verbose-only. The flag value is printed numerically as well as by name because a
        // combination this code does not expect would otherwise be invisible.
        private static readonly Dictionary<ushort, DisasterData.Flags> LastReportedFlags =
            new Dictionary<ushort, DisasterData.Flags>();

        private static void ReportMatch(ushort index, DisasterData.Flags flags)
        {
            DisasterData.Flags previous;
            if (LastReportedFlags.TryGetValue(index, out previous) && previous == flags)
            {
                return;
            }

            LastReportedFlags[index] = flags;

            Log.Verbose(
                "[AIImprove] Thunderstorm detector matched disaster slot " + index +
                " with flags " + (int)flags + " (" + flags + "). Airports and emergency " +
                "helicopters are being held for as long as this keeps matching.");
        }
    }
}
