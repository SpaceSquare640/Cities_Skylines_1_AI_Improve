using ColossalFramework;
using ICities;
using UnityEngine;

namespace AIImprove
{
    // Entry point the game discovers via reflection. Must not reference HarmonyLib directly here,
    // only through CitiesHarmony.API, otherwise the mod fails to load when CitiesHarmony isn't installed.
    public class AIImproveMod : IUserMod
    {
        public string Name => "AI_Improve";

        public string Description => "Improves traffic, citizen and service vehicle AI decision quality. By SpaceSquare.";

        public void OnEnabled()
        {
            Debug.Log("[AIImprove] OnEnabled called - mod loaded successfully.");
            CitiesHarmony.API.HarmonyHelper.DoOnHarmonyReady(Patcher.PatchAll);
        }

        public void OnDisabled()
        {
            Debug.Log("[AIImprove] OnDisabled called.");
            if (CitiesHarmony.API.HarmonyHelper.IsHarmonyInstalled)
            {
                Patcher.UnpatchAll();
            }
        }

        // Content Manager's per-mod options page. Vanilla UIHelperBase has no concept of separate
        // "pages"/tabs - AddGroup is the closest equivalent (a titled, visually boxed section
        // within the single scrollable panel), so "獨立設定頁" (2026-08-15, per user request) is
        // implemented as one AddGroup per feature instead of one shared group with a flat
        // checkbox list. Each group's own checkbox is still the same category-level on/off switch
        // from ModSettings.cs - see that file for the persistence mechanism and KNOWN GAP notes.
        public void OnSettingsUI(UIHelperBase helper)
        {
            AddFeatureGroup(helper, "緊急車輛 (Emergency vehicles)",
                "出勤上限、閒置車輛找附近火場。部分派遣邏輯用 IL 轉譯器實作，此開關對那部分無效",
                ModSettings.EmergencyVehiclesEnabled);

            AddFeatureGroup(helper, "地鐵 (Metro)",
                "月台分配、中途改道",
                ModSettings.TrainsAndMetroEnabled);

            AddFeatureGroup(helper, "城際火車 (Intercity trains)",
                "月台分配、中途改道、入城流量節流",
                ModSettings.IntercityTrainEnabled);

            AddFeatureGroup(helper, "飛機與機場 (Aircraft & airports)",
                "登機門分配、中途改道、雷暴雨拒絕起降",
                ModSettings.AircraftEnabled);

            AddFeatureGroup(helper, "市內巴士與客運直升機 (Local buses & passenger helicopters)",
                "中途改道、登機點分配、載客量",
                ModSettings.BusesAndHelicoptersEnabled);

            AddFeatureGroup(helper, "城際巴士 (Intercity buses)",
                "中途改道閾值調整",
                ModSettings.IntercityBusEnabled);

            AddFeatureGroup(helper, "一般市內交通 (Ordinary city traffic)",
                "私家車/計程車/貨車動態改道",
                ModSettings.OrdinaryTrafficEnabled);

            AddFeatureGroup(helper, "市民行為 (Citizens)",
                "依壅塞調整開車/計程車機率",
                ModSettings.CitizensEnabled);

            AddFeatureGroup(helper, "賽車 (Race cars)",
                "速度上限、賽車場吸引力",
                ModSettings.RaceCarsEnabled);
        }

        private static void AddFeatureGroup(UIHelperBase helper, string title, string description, SavedBool setting)
        {
            UIHelperBase group = helper.AddGroup(title);
            group.AddCheckbox("啟用此功能 (Enable) - " + description, setting.value, value => setting.value = value);
        }
    }
}
