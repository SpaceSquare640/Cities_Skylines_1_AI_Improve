using UnityEngine;

namespace AIImprove
{
    // "全面進行效能優化" (2026-08-15): measured against a real player log, 2540 of this mod's
    // 2636 total log lines (96%) came from a single per-event diagnostic - the per-aircraft gate
    // assignment line - with per-reroute lines the next biggest contributor. Each one builds a
    // formatted string and hands it to Unity's Debug.Log, which writes through to disk
    // synchronously; at that volume it is a real cost in a large city, paid entirely for
    // diagnostics nobody reads during normal play.
    //
    // Split into two levels rather than deleting the lines outright, because those per-event logs
    // are exactly what this project has repeatedly used to diagnose real player-reported bugs
    // (the metro-stuck m_targetBuilding bug, the stop-skipping ridership collapse, the TMCE fire
    // dispatch conflict were all found this way):
    //   Info    - one-off/rare events (patch applied, feature first executed, a skip decision).
    //             Always written; volume is negligible.
    //   Verbose - per-vehicle/per-event spam. Off by default, flipped on from the settings page
    //             when a log is actually being collected for a bug report.
    internal static class Log
    {
        public static void Info(string message) => Debug.Log(message);

        public static void Warning(string message) => Debug.LogWarning(message);

        public static bool VerboseEnabled => ModSettings.VerboseLogging.value;

        // Callers must guard the call site with `if (Log.VerboseEnabled)` when building the
        // message costs anything - passing an already-concatenated string here would still pay
        // the concatenation even when verbose logging is off, which is the entire cost being
        // avoided.
        public static void Verbose(string message)
        {
            if (ModSettings.VerboseLogging.value)
            {
                Debug.Log(message);
            }
        }
    }
}
