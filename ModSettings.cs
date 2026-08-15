using ColossalFramework;

namespace AIImprove
{
    // "開始搭建 UI 框架" (2026-08-15) - the in-game options panel discussed and deferred in
    // 07 - 開發路線圖與里程碑.md, plus the per-category toggles a Steam Workshop commenter
    // (jazzybee_s) asked for. Categories mirror the ones the README already groups features
    // into, not a new taxonomy invented for the panel.
    //
    // Persistence: ColossalFramework.SavedBool, the same mechanism vanilla's own options and
    // most other mods (e.g. Optimised Outside Connections, seen via CargoHoldFix.dll) use - a
    // flat key/value file under the game's local app data folder, registered once via
    // GameSettings.AddSettingsFile. autoUpdate=true means every .value read re-syncs from the
    // backing file, so a change made from Content Manager takes effect immediately without a
    // restart - no caching needed on this side.
    //
    // This only toggles behavior, not registration: every patch stays Harmony-patched for the
    // whole session (see Patcher.cs) and checks the relevant SavedBool itself at the top of its
    // own entry point, same "early return" shape already used for DlcDetector/CompanionModCompat
    // checks throughout this project. Unpatching/re-patching at runtime was considered and
    // rejected - it would need to happen from the options UI callback, on a background thread
    // boundary this project has no established pattern for yet, for no real benefit over a
    // cheap boolean check.
    //
    // KNOWN GAP: transpiler-based patches (EmergencyIgnoreCostsPatch, VanillaEmergencyCongestionPatch,
    // TMPE's EmergencyVehiclePriorityPatch path) rewrite IL once at patch time and have no
    // per-call entry point to gate - they are not wired to EmergencyVehiclesEnabled. Toggling
    // those off would need actual unpatch/re-patch, deferred for now (see EmergencyVehicles
    // group's own note below).
    public static class ModSettings
    {
        private const string FileName = "AIImprove";

        static ModSettings()
        {
            GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });
        }

        public static readonly SavedBool EmergencyVehiclesEnabled =
            new SavedBool("EmergencyVehiclesEnabled", FileName, true, true);

        // Metro only - see IntercityTrainEnabled below for the split-out regional/intercity
        // train toggle. Also the fallback for any other TrainAI (e.g. cargo trains) that isn't
        // clearly one or the other.
        public static readonly SavedBool TrainsAndMetroEnabled =
            new SavedBool("TrainsAndMetroEnabled", FileName, true, true);

        // "把城際火車及城際巴士的設定分開獨立放置" (2026-08-15): split out from
        // TrainsAndMetroEnabled. Per this project's own established terminology (see
        // TrainPassengerCapacityPatch.cs's notes), vanilla only has one non-metro passenger
        // train type - "PassengerTrainAI minus MetroTrainAI" IS "intercity/regional train" here,
        // not a separate third category.
        public static readonly SavedBool IntercityTrainEnabled =
            new SavedBool("IntercityTrainEnabled", FileName, true, true);

        // Split out from BusesAndHelicoptersEnabled, same reasoning - BusAI where
        // TransportStationAI.IsIntercity(m_info.m_class) is true.
        public static readonly SavedBool IntercityBusEnabled =
            new SavedBool("IntercityBusEnabled", FileName, true, true);

        public static readonly SavedBool AircraftEnabled =
            new SavedBool("AircraftEnabled", FileName, true, true);

        public static readonly SavedBool BusesAndHelicoptersEnabled =
            new SavedBool("BusesAndHelicoptersEnabled", FileName, true, true);

        public static readonly SavedBool OrdinaryTrafficEnabled =
            new SavedBool("OrdinaryTrafficEnabled", FileName, true, true);

        public static readonly SavedBool CitizensEnabled =
            new SavedBool("CitizensEnabled", FileName, true, true);

        public static readonly SavedBool RaceCarsEnabled =
            new SavedBool("RaceCarsEnabled", FileName, true, true);

        // "還是看不到切換語言的按鈕" (2026-08-15): Localization.cs picks a language from the
        // game's own LocaleManager automatically, with no way to override it - this stores an
        // explicit player choice instead. "auto" means "keep following the game's language" (the
        // previous, only behavior); any other value is one of Localization's language codes
        // (e.g. "en", "zh-tw", "zh-cn") and wins over the game's own setting.
        public static readonly SavedString LanguageOverride =
            new SavedString("LanguageOverride", FileName, "auto", true);
    }
}
