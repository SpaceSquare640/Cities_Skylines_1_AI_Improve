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

        // Content Manager's per-mod options page. Each checkbox is a category-level on/off
        // switch, not a numeric tunable - see ModSettings.cs for the persistence mechanism and
        // KNOWN GAP notes. Group order matches the README's feature list.
        public void OnSettingsUI(UIHelperBase helper)
        {
            UIHelperBase group = helper.AddGroup("AI_Improve - 功能分類開關 (Feature toggles)");

            AddToggle(group, "緊急車輛 (Emergency vehicles) - 出勤上限、閒置車輛找附近火場。部分派遣邏輯用 IL 轉譯器實作，此開關對那部分無效", ModSettings.EmergencyVehiclesEnabled);
            AddToggle(group, "地鐵 (Metro) - 月台分配、中途改道", ModSettings.TrainsAndMetroEnabled);
            AddToggle(group, "城際火車 (Intercity trains) - 月台分配、中途改道、入城流量節流", ModSettings.IntercityTrainEnabled);
            AddToggle(group, "飛機與機場 (Aircraft & airports) - 登機門分配、中途改道、雷暴雨拒絕起降", ModSettings.AircraftEnabled);
            AddToggle(group, "市內巴士與客運直升機 (Local buses & passenger helicopters) - 中途改道、登機點分配、載客量", ModSettings.BusesAndHelicoptersEnabled);
            AddToggle(group, "城際巴士 (Intercity buses) - 中途改道閾值調整", ModSettings.IntercityBusEnabled);
            AddToggle(group, "一般市內交通 (Ordinary city traffic) - 私家車/計程車/貨車動態改道", ModSettings.OrdinaryTrafficEnabled);
            AddToggle(group, "市民行為 (Citizens) - 依壅塞調整開車/計程車機率", ModSettings.CitizensEnabled);
            AddToggle(group, "賽車 (Race cars) - 速度上限、賽車場吸引力", ModSettings.RaceCarsEnabled);
        }

        private static void AddToggle(UIHelperBase group, string label, SavedBool setting)
        {
            group.AddCheckbox(label, setting.value, value => setting.value = value);
        }
    }
}
