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
