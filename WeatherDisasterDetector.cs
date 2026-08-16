using ColossalFramework;

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
        public static bool IsThunderstormActive()
        {
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
                // The correct test is the one vanilla itself uses (confirmed via dnSpy against
                // DisasterManager, which gates its own experience-milestone logic on precisely
                // this): a disaster is in progress only while Active or Clearing is set. Emerging
                // is deliberately excluded - the storm hasn't actually hit yet - and Finished is
                // the explicit terminal state that this check used to keep treating as live.
                if ((flags & (DisasterData.Flags.Active | DisasterData.Flags.Clearing)) == DisasterData.Flags.None)
                {
                    continue;
                }

                DisasterInfo info = disasters.m_buffer[i].Info;
                if (info != null && info.m_disasterAI is ThunderStormAI)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
