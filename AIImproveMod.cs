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
        // split requested 2026-08-15, rebuilt the same day into a tabbed/card-header/pill-switch
        // page matching ACME/Advanced Stop Selection's style per explicit user request. See
        // SettingsPageUI.cs for the actual construction (kept out of this file since it's a
        // sizable chunk of raw ColossalFramework.UI work, not a couple of AddGroup calls anymore).
        public void OnSettingsUI(UIHelperBase helper) => SettingsPageUI.Build(helper);
    }
}
