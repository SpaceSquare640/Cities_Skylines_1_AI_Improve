using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
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

            // "地圖中也可以啟用" (2026-08-15): the in-game toggle button/panel (IngameUI.cs) is
            // created per-city on level load and torn down on unload, since UIView itself gets
            // destroyed/recreated between scenes (main menu <-> city <-> another city).
            ColossalFramework.Singleton<LoadingManager>.instance.m_levelLoaded += IngameUI.OnLevelLoaded;
            ColossalFramework.Singleton<LoadingManager>.instance.m_levelUnloaded += IngameUI.OnLevelUnloading;
        }

        public void OnDisabled()
        {
            Debug.Log("[AIImprove] OnDisabled called.");
            if (CitiesHarmony.API.HarmonyHelper.IsHarmonyInstalled)
            {
                Patcher.UnpatchAll();
            }

            ColossalFramework.Singleton<LoadingManager>.instance.m_levelLoaded -= IngameUI.OnLevelLoaded;
            ColossalFramework.Singleton<LoadingManager>.instance.m_levelUnloaded -= IngameUI.OnLevelUnloading;
            IngameUI.OnLevelUnloading();
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

            UIHelperBase intercityTrainGroup = AddFeatureGroup(helper, "城際火車 (Intercity trains)",
                "月台分配、中途改道、入城流量節流",
                ModSettings.IntercityTrainEnabled);
            AddEmptyVehicleScanButton(
                intercityTrainGroup,
                "檢測沒有乘客的城際火車 (Scan for empty intercity trains)",
                EmptyVehicleAuditor.ScanIntercityTrains,
                "城際火車 (intercity trains)");

            AddFeatureGroup(helper, "飛機與機場 (Aircraft & airports)",
                "登機門分配、中途改道、雷暴雨拒絕起降",
                ModSettings.AircraftEnabled);

            AddFeatureGroup(helper, "市內巴士與客運直升機 (Local buses & passenger helicopters)",
                "中途改道、登機點分配、載客量",
                ModSettings.BusesAndHelicoptersEnabled);

            UIHelperBase intercityBusGroup = AddFeatureGroup(helper, "城際巴士 (Intercity buses)",
                "中途改道閾值調整",
                ModSettings.IntercityBusEnabled);
            AddEmptyVehicleScanButton(
                intercityBusGroup,
                "檢測沒有乘客的城際巴士 (Scan for empty intercity buses)",
                EmptyVehicleAuditor.ScanIntercityBuses,
                "城際巴士 (intercity buses)");

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

        private static UIHelperBase AddFeatureGroup(UIHelperBase helper, string title, string description, SavedBool setting)
        {
            UIHelperBase group = helper.AddGroup(title);
            group.AddCheckbox("啟用此功能 (Enable) - " + description, setting.value, value => setting.value = value);
            return group;
        }

        // "一鍵檢測沒有乘客的車輛及檢測後讓玩家選擇是否直接刪除" (2026-08-15): scan first
        // (read-only), then ask via the game's own confirm dialog before deleting anything -
        // deletion never happens without an explicit click. See EmptyVehicleAuditor.cs for the
        // scan/delete logic itself.
        private static void AddEmptyVehicleScanButton(
            UIHelperBase group, string buttonLabel, System.Func<EmptyVehicleAuditor.ScanResult> scan, string categoryLabel)
        {
            group.AddButton(buttonLabel, () =>
            {
                EmptyVehicleAuditor.ScanResult result = scan();

                if (result.LeadVehicleIds.Count == 0)
                {
                    ConfirmPanel.ShowModal("AI_Improve", "沒有偵測到沒有乘客的" + categoryLabel + "。", null);
                    return;
                }

                List<ushort> matchedIds = result.LeadVehicleIds;
                string message =
                    "偵測到 " + matchedIds.Count + " 輛沒有乘客的" + categoryLabel +
                    "（共 " + result.TotalVehicleCount + " 節車廂/車輛實例）。\n\n是否要直接刪除這些車輛？";

                ConfirmPanel.ShowModal("AI_Improve", message, (component, ret) =>
                {
                    if (ret != 1)
                    {
                        return;
                    }

                    EmptyVehicleAuditor.DeleteVehicles(matchedIds);
                });
            });
        }
    }
}
