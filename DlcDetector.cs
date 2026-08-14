using ColossalFramework;

namespace AIImprove
{
    // DLC ownership checks, for features that only make sense when the content they act on
    // actually exists (2026-08-14, per user request: "如果偵測到玩家沒有的士的 DLC 內容我們這個
    // 的士的 AI 部份就不會啟用").
    //
    // Note this is the opposite direction from the helicopter features elsewhere in this project,
    // which deliberately work whether or not the player owns the DLC that introduced them - those
    // act on vehicles the game can still create, while a taxi probability boost is meaningless
    // with no taxi vehicle to hand out.
    //
    // SteamHelper.IsDLCOwned is a plain static call on a global (namespace-less) game type, so no
    // reflection is needed - unlike the third-party mod detection in CompanionModCompat.
    // Deliberately only covers DLCs whose mapping is certain. A wrong "not owned" verdict would
    // silently disable a feature the player can actually use, which is worse than leaving a patch
    // registered - and a Harmony patch on a method the game never calls (because the content it
    // belongs to isn't installed) costs nothing at runtime anyway. So gating is applied where it
    // either saves real per-call work or makes the log honest, not everywhere for its own sake.
    // See Patcher.PatchAll and WeatherDisasterDetector for the two places it's used.
    //
    // Note Skyve patches SteamHelper.IsDLCOwned to implement its own DLC-disabling feature, so
    // these calls correctly follow whatever the player configured there, not just raw ownership.
    internal static class DlcDetector
    {
        private static bool? afterDarkOwned;
        private static bool? naturalDisastersOwned;
        private static bool? racesAndParadesOwned;

        // Taxis (the vehicle, the taxi depot and the taxi stand) came with After Dark. Cached
        // because DLC ownership cannot change while the game is running, and this is consulted
        // from a per-trip-decision hot path.
        public static bool IsAfterDarkOwned() =>
            Cached(ref afterDarkOwned, SteamHelper.DLC.AfterDarkDLC);

        // Thunderstorms - and every other disaster - are Natural Disasters content. Without it
        // DisasterManager never holds a ThunderStormAI entry, so every thunderstorm check can
        // only ever answer "no".
        public static bool IsNaturalDisastersOwned() =>
            Cached(ref naturalDisastersOwned, SteamHelper.DLC.NaturalDisastersDLC);

        // The race complex, its race cars and the race events are all Races and Parades content.
        public static bool IsRacesAndParadesOwned() =>
            Cached(ref racesAndParadesOwned, SteamHelper.DLC.RacesAndParadesDLC);

        private static bool Cached(ref bool? slot, SteamHelper.DLC dlc)
        {
            if (!slot.HasValue)
            {
                slot = SteamHelper.IsDLCOwned(dlc);
            }

            return slot.Value;
        }
    }
}
