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

        // Content Manager's per-mod options page - the "detailed settings" half of the TM:PE-style
        // split requested 2026-08-15: reached via ESC -> Options -> Content Manager, shows every
        // toggle WITH its full description, and nothing else (no action buttons - those live only
        // in the simple in-game panel, see IngameUI.cs). Vanilla UIHelperBase has no concept of
        // separate "pages"/tabs - AddGroup is the closest equivalent (a titled, visually boxed
        // section within the single scrollable panel), so "獨立設定頁" (2026-08-15, earlier
        // request) is implemented as one AddGroup per feature instead of one shared group with a
        // flat checkbox list. Each group's own checkbox is still the same category-level on/off
        // switch from ModSettings.cs - see that file for the persistence mechanism and KNOWN GAP
        // notes.
        public void OnSettingsUI(UIHelperBase helper)
        {
            AddFeatureGroup(helper, "category.emergency", ModSettings.EmergencyVehiclesEnabled);
            AddFeatureGroup(helper, "category.metro", ModSettings.TrainsAndMetroEnabled);
            AddFeatureGroup(helper, "category.intercityTrain", ModSettings.IntercityTrainEnabled);
            AddFeatureGroup(helper, "category.aircraft", ModSettings.AircraftEnabled);
            AddFeatureGroup(helper, "category.buses", ModSettings.BusesAndHelicoptersEnabled);
            AddFeatureGroup(helper, "category.intercityBus", ModSettings.IntercityBusEnabled);
            AddFeatureGroup(helper, "category.traffic", ModSettings.OrdinaryTrafficEnabled);
            AddFeatureGroup(helper, "category.citizens", ModSettings.CitizensEnabled);
            AddFeatureGroup(helper, "category.racecars", ModSettings.RaceCarsEnabled);
        }

        private static void AddFeatureGroup(UIHelperBase helper, string categoryKey, SavedBool setting)
        {
            UIHelperBase group = helper.AddGroup(Localization.Get(categoryKey + ".title"));
            string label = Localization.Get("toggle.enable") + " - " + Localization.Get(categoryKey + ".desc");
            group.AddCheckbox(label, setting.value, value => setting.value = value);
        }
    }
}
