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
            AddFeatureGroup(helper, "category.emergency", ModSettings.EmergencyVehiclesEnabled);
            AddFeatureGroup(helper, "category.metro", ModSettings.TrainsAndMetroEnabled);

            UIHelperBase intercityTrainGroup =
                AddFeatureGroup(helper, "category.intercityTrain", ModSettings.IntercityTrainEnabled);
            AddEmptyVehicleScanButton(
                intercityTrainGroup,
                Localization.Get("button.scanTrain"),
                EmptyVehicleAuditor.ScanIntercityTrains,
                Localization.Get("category.intercityTrain.short"));

            AddFeatureGroup(helper, "category.aircraft", ModSettings.AircraftEnabled);
            AddFeatureGroup(helper, "category.buses", ModSettings.BusesAndHelicoptersEnabled);

            UIHelperBase intercityBusGroup =
                AddFeatureGroup(helper, "category.intercityBus", ModSettings.IntercityBusEnabled);
            AddEmptyVehicleScanButton(
                intercityBusGroup,
                Localization.Get("button.scanBus"),
                EmptyVehicleAuditor.ScanIntercityBuses,
                Localization.Get("category.intercityBus.short"));

            AddFeatureGroup(helper, "category.traffic", ModSettings.OrdinaryTrafficEnabled);
            AddFeatureGroup(helper, "category.citizens", ModSettings.CitizensEnabled);
            AddFeatureGroup(helper, "category.racecars", ModSettings.RaceCarsEnabled);
        }

        private static UIHelperBase AddFeatureGroup(UIHelperBase helper, string categoryKey, SavedBool setting)
        {
            UIHelperBase group = helper.AddGroup(Localization.Get(categoryKey + ".title"));
            string label = Localization.Get("toggle.enable") + " - " + Localization.Get(categoryKey + ".desc");
            group.AddCheckbox(label, setting.value, value => setting.value = value);
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
                    ConfirmPanel.ShowModal("AI_Improve", Localization.Get("scan.noneFound", categoryLabel), null);
                    return;
                }

                List<ushort> matchedIds = result.LeadVehicleIds;
                string message = Localization.Get(
                    "scan.confirm", matchedIds.Count, categoryLabel, result.TotalVehicleCount);

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
