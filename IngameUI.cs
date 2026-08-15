using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace AIImprove
{
    // "地圖中也可以啟用，而不是每次需要調整設定都要返回主菜單" (2026-08-15), then
    // "我們的模組需要依賴 UnifiedUI 才能在遊戲的地圖中打開UI" (2026-08-15, follow-up): the
    // in-game panel is opened from a button registered into the player's UnifiedUI (UUI) toolbar
    // (see UnifiedUiIntegration.cs) instead of a floating button of our own, and instead of routing
    // through Content Manager's OnSettingsUI (AIImproveMod.cs) - that page only exists inside
    // Content Manager's own context, which is what forced the round-trip out of the city the player
    // was complaining about. This mirrors the exact same toggles and scan buttons; both read/write
    // the same ModSettings.SavedBool fields (autoUpdate=true), so flipping one from either UI is
    // reflected immediately in the other. UnifiedUI is a real dependency for reaching this panel
    // in-game per the player's explicit request - without it installed, Content Manager's settings
    // page is still the only way in.
    //
    // Panel built directly on ColossalFramework.UI (not UIHelperBase, which only works inside a
    // UIHelperBase-provided page) - every member used here was confirmed via dnSpy against the
    // installed game's Assembly-CSharp.dll/ColossalManaged.dll before writing this, since none of
    // it can be exercised by a plain `dotnet build` until tested in-game.
    //
    // Uses a plain UIButton (checked state shown via a "[On]/[Off] " text prefix) instead of
    // UICheckBox for the toggles - UICheckBox needs a manually-wired checked-state sprite object
    // with no safe built-in default, and a button reads just as clearly with far less that can go
    // visually wrong without being able to live-test it first.
    internal static class IngameUI
    {
        private static UIPanel panel;
        private static bool registeredWithUui;

        public static void OnLevelLoaded(SimulationManager.UpdateMode mode)
        {
            if (mode != SimulationManager.UpdateMode.NewGameFromMap &&
                mode != SimulationManager.UpdateMode.NewGameFromScenario &&
                mode != SimulationManager.UpdateMode.LoadGame &&
                mode != SimulationManager.UpdateMode.LoadScenario)
            {
                // Not an actual city (e.g. asset/map editor) - nothing here is relevant there.
                return;
            }

            registeredWithUui = UnifiedUiIntegration.TryRegister(
                "AI_Improve", "AI_Improve", Localization.Get("uui.tooltip"), OnUuiToggle);

            if (registeredWithUui)
            {
                Debug.Log("[AIImprove] IngameUI: registered a toggle button with UnifiedUI.");
            }
            else
            {
                Debug.Log(
                    "[AIImprove] IngameUI: UnifiedUI not detected - in-game panel button not " +
                    "available this session. Install UnifiedUI to reach it without going through " +
                    "Content Manager.");
            }
        }

        public static void OnLevelUnloading()
        {
            if (panel != null)
            {
                UnityEngine.Object.Destroy(panel.gameObject);
                panel = null;
            }

            registeredWithUui = false;
        }

        private static void OnUuiToggle(bool isOn)
        {
            if (panel == null)
            {
                CreatePanel();
            }

            panel.isVisible = isOn;
        }

        private static void CreatePanel()
        {
            UIView view = UIView.GetAView();
            panel = view.AddUIComponent(typeof(UIPanel)) as UIPanel;
            panel.name = "AIImprovePanel";
            panel.backgroundSprite = "MenuPanel2";
            panel.width = 440f;
            panel.isVisible = false;
            panel.canFocus = true;
            panel.isInteractive = true;
            panel.absolutePosition = new Vector3(56f, 60f);

            UIDragHandle dragHandle = panel.AddUIComponent<UIDragHandle>();
            dragHandle.width = panel.width;
            dragHandle.height = 40f;
            dragHandle.relativePosition = Vector3.zero;
            dragHandle.target = panel;

            UILabel title = panel.AddUIComponent<UILabel>();
            title.text = Localization.Get("panel.title");
            title.relativePosition = new Vector3(10f, 12f);

            UIButton closeButton = panel.AddUIComponent<UIButton>();
            closeButton.text = "X";
            closeButton.width = 30f;
            closeButton.height = 30f;
            closeButton.relativePosition = new Vector3(panel.width - 40f, 5f);
            closeButton.normalBgSprite = "buttonclose";
            closeButton.hoveredBgSprite = "buttonclosehover";
            closeButton.pressedBgSprite = "buttonclosepressed";
            closeButton.eventClick += (component, param) => panel.isVisible = false;

            UIPanel content = panel.AddUIComponent<UIPanel>();
            content.relativePosition = new Vector3(10f, 45f);
            content.width = panel.width - 20f;
            content.autoLayout = true;
            content.autoLayoutDirection = LayoutDirection.Vertical;
            content.autoLayoutPadding = new RectOffset(0, 0, 0, 4);
            content.autoFitChildrenVertically = true;

            AddToggle(content, "category.emergency.title", ModSettings.EmergencyVehiclesEnabled);
            AddToggle(content, "category.metro.title", ModSettings.TrainsAndMetroEnabled);
            AddToggle(content, "category.intercityTrain.title", ModSettings.IntercityTrainEnabled);
            AddScanButton(
                content, Localization.Get("button.scanTrain"), EmptyVehicleAuditor.ScanIntercityTrains,
                Localization.Get("category.intercityTrain.short"));
            AddToggle(content, "category.aircraft.title", ModSettings.AircraftEnabled);
            AddToggle(content, "category.buses.title", ModSettings.BusesAndHelicoptersEnabled);
            AddToggle(content, "category.intercityBus.title", ModSettings.IntercityBusEnabled);
            AddScanButton(
                content, Localization.Get("button.scanBus"), EmptyVehicleAuditor.ScanIntercityBuses,
                Localization.Get("category.intercityBus.short"));
            AddToggle(content, "category.traffic.title", ModSettings.OrdinaryTrafficEnabled);
            AddToggle(content, "category.citizens.title", ModSettings.CitizensEnabled);
            AddToggle(content, "category.racecars.title", ModSettings.RaceCarsEnabled);

            panel.height = content.relativePosition.y + content.height + 15f;
        }

        private static void AddToggle(UIComponent content, string titleKey, ColossalFramework.SavedBool setting)
        {
            string label = Localization.Get(titleKey);

            UIButton button = content.AddUIComponent<UIButton>();
            button.width = content.width;
            button.height = 26f;
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.textScale = 0.8f;
            button.textHorizontalAlignment = UIHorizontalAlignment.Left;
            button.textPadding = new RectOffset(8, 0, 5, 0);

            RefreshToggleText(button, label, setting.value);

            button.eventClick += (component, param) =>
            {
                setting.value = !setting.value;
                RefreshToggleText(button, label, setting.value);
            };
        }

        private static void RefreshToggleText(UIButton button, string label, bool isEnabled)
        {
            button.text = Localization.Get(isEnabled ? "toggle.on" : "toggle.off") + label;
        }

        private static void AddScanButton(
            UIComponent content, string label, Func<EmptyVehicleAuditor.ScanResult> scan, string categoryLabel)
        {
            UIButton button = content.AddUIComponent<UIButton>();
            button.text = label;
            button.width = content.width;
            button.height = 28f;
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.textScale = 0.75f;

            button.eventClick += (component, param) =>
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

                ConfirmPanel.ShowModal("AI_Improve", message, (comp, ret) =>
                {
                    if (ret == 1)
                    {
                        EmptyVehicleAuditor.DeleteVehicles(matchedIds);
                    }
                });
            };
        }
    }
}
