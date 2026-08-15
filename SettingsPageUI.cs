using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace AIImprove
{
    // Content Manager settings page.
    //
    // "我想把全部功能拆開，然後每個功能中的調整設定及數據可以拆開以及詳細調整" (2026-08-15):
    // rebuilt again from the 9-category version to expose every one of the ~19 individual
    // features and ~25 previously-hardcoded values ModSettings.cs now defines, instead of one
    // toggle covering several unrelated behaviours. Ten sections instead of five, each with its
    // own Toggles/Tuning tabs (or a single page where there's little to show).
    //
    // Layout pattern (left-hand vertical section list + per-section horizontal tabs, dropdowns
    // and sliders cloned from the game's own OptionsDropdownTemplate/OptionsSliderTemplate) is
    // unchanged from the previous version - see AddSection/AddSliderRow for the mechanics and
    // their notes on why templates are used instead of hand-assembling UIDropDown/UISlider.
    //
    // Built on the page's real UIComponent tree (via the concrete UIHelper.self, which
    // UIHelperBase's interface doesn't expose) rather than UIHelperBase's own methods, since
    // UIHelperBase has no concept of tabs. If the tree fails to come together at runtime the page
    // degrades to a plain flat toggle list (BuildFlatFallback) rather than rendering empty.
    internal static class SettingsPageUI
    {
        private const string RepoUrl = "https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve";
        private const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3782858610";

        private const string DropdownTemplate = "OptionsDropdownTemplate";
        private const string SliderTemplate = "OptionsSliderTemplate";

        private const float HeaderHeight = 100f;
        private const float NavWidth = 172f;
        private const float BodyHeight = 440f;
        private const float SubTabHeight = 32f;

        private static readonly Color32 AccentColor = new Color32(58, 121, 187, 255);
        private static readonly Color32 AccentHoverColor = new Color32(78, 141, 207, 255);
        private static readonly Color32 AccentPressedColor = new Color32(45, 98, 154, 255);
        private static readonly Color32 HeaderColor = new Color32(30, 40, 56, 255);
        private static readonly Color32 NavColor = new Color32(38, 42, 50, 255);
        private static readonly Color32 NavItemColor = new Color32(52, 57, 66, 255);
        private static readonly Color32 NavItemHoverColor = new Color32(68, 74, 85, 255);
        private static readonly Color32 TabColor = new Color32(48, 52, 60, 255);
        private static readonly Color32 TabHoverColor = new Color32(64, 68, 78, 255);
        private static readonly Color32 PillOnColor = new Color32(76, 175, 80, 255);
        private static readonly Color32 PillOffColor = new Color32(95, 95, 100, 255);
        private static readonly Color32 MutedTextColor = new Color32(190, 195, 205, 255);

        private static readonly List<UIButton> NavButtons = new List<UIButton>();
        private static readonly List<UIPanel> Sections = new List<UIPanel>();

        public static void Build(UIHelperBase helper)
        {
            UIComponent root = GetRoot(helper);
            if (root == null)
            {
                BuildFlatFallback(helper);
                return;
            }

            BuildContent(root, helper);
        }

        private static void RebuildInPlace(UIComponent root, UIHelperBase helper)
        {
            for (int i = root.components.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(root.components[i].gameObject);
            }

            BuildContent(root, helper);
        }

        private static void BuildContent(UIComponent root, UIHelperBase helper)
        {
            NavButtons.Clear();
            Sections.Clear();

            BuildHeader(root, helper);

            UIPanel nav = root.AddUIComponent<UIPanel>();
            nav.width = NavWidth;
            nav.height = BodyHeight;
            nav.atlas = SolidColorSprite.Atlas;
            nav.backgroundSprite = SolidColorSprite.SpriteName;
            nav.color = NavColor;
            nav.relativePosition = new Vector3(0f, HeaderHeight + 6f);
            nav.autoLayout = true;
            nav.autoLayoutDirection = LayoutDirection.Vertical;
            nav.autoLayoutPadding = new RectOffset(0, 0, 0, 2);
            nav.padding = new RectOffset(6, 6, 8, 6);

            float sectionX = NavWidth + 8f;
            float sectionWidth = root.width - sectionX;

            AddSection(root, nav, sectionX, sectionWidth, "nav.general",
                new[] { "subtab.settings" },
                pages => BuildGeneralPage(pages[0], root, helper));

            AddSection(root, nav, sectionX, sectionWidth, "nav.emergency",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "feature.fireResponseCap", ModSettings.FireResponseCapEnabled);
                    AddToggleRow(pages[0], "feature.fireIdleSeek", ModSettings.FireIdleSeekEnabled);
                    AddToggleRow(pages[0], "feature.helicopterWeatherHalt", ModSettings.HelicopterWeatherHaltEnabled);

                    AddSliderRow(pages[1], Localization.Get("tune.fireResponders"), 5f, 50f, 1f,
                        ModSettings.FireMaxRespondersPerBuilding.value,
                        v => ModSettings.FireMaxRespondersPerBuilding.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.fireUncapMinutes"), 5f, 60f, 1f,
                        ModSettings.FireUncapAfterMinutes.value,
                        v => ModSettings.FireUncapAfterMinutes.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.metro",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "feature.metroPlatform", ModSettings.MetroPlatformAssignmentEnabled);
                    AddToggleRow(pages[0], "feature.metroReroute", ModSettings.MetroRerouteEnabled);

                    AddSliderRow(pages[1], Localization.Get("tune.rerouteDensity"), 20f, 100f, 5f,
                        ModSettings.MetroRerouteDensityThreshold.value,
                        v => ModSettings.MetroRerouteDensityThreshold.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.intercityTrain",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "feature.trainPlatform", ModSettings.IntercityTrainPlatformAssignmentEnabled);
                    AddToggleRow(pages[0], "feature.trainReroute", ModSettings.IntercityTrainRerouteEnabled);
                    AddToggleRow(pages[0], "feature.trainSpawnThrottle", ModSettings.IntercityTrainSpawnThrottleEnabled);
                    AddToggleRow(pages[0], "feature.singleTrackDetector", ModSettings.SingleTrackConflictDetectorEnabled);

                    AddSliderRow(pages[1], Localization.Get("tune.stationSaturation"), 5f, 60f, 1f,
                        ModSettings.TrainStationSaturationThreshold.value,
                        v => ModSettings.TrainStationSaturationThreshold.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.platformCandidates"), 8f, 40f, 1f,
                        ModSettings.TrainPlatformCandidateCount.value,
                        v => ModSettings.TrainPlatformCandidateCount.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.rerouteDensity"), 20f, 100f, 5f,
                        ModSettings.IntercityTrainRerouteDensityThreshold.value,
                        v => ModSettings.IntercityTrainRerouteDensityThreshold.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.lowRidership"), 0f, 200f, 5f,
                        ModSettings.IntercityLowRidershipThreshold.value,
                        v => ModSettings.IntercityLowRidershipThreshold.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.lowRidershipSkipChance"), 0f, 100f, 5f,
                        ModSettings.IntercityLowRidershipSkipPercent.value,
                        v => ModSettings.IntercityLowRidershipSkipPercent.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.aircraft",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "feature.aircraftGate", ModSettings.AircraftGateAssignmentEnabled);
                    AddToggleRow(pages[0], "feature.aircraftReroute", ModSettings.AircraftRerouteEnabled);
                    AddToggleRow(pages[0], "feature.aircraftThunderstorm", ModSettings.AircraftThunderstormRefusalEnabled);

                    AddSliderRow(pages[1], Localization.Get("tune.gateCandidates"), 8f, 50f, 1f,
                        ModSettings.AircraftGateCandidateCount.value,
                        v => ModSettings.AircraftGateCandidateCount.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.perGateCapacity"), 1f, 20f, 1f,
                        ModSettings.AircraftPerGateCapacity.value,
                        v => ModSettings.AircraftPerGateCapacity.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.rerouteDensity"), 20f, 100f, 5f,
                        ModSettings.AircraftRerouteDensityThreshold.value,
                        v => ModSettings.AircraftRerouteDensityThreshold.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.localTransport",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "feature.localBusReroute", ModSettings.LocalBusRerouteEnabled);
                    AddToggleRow(pages[0], "feature.trafficReroute", ModSettings.OrdinaryTrafficRerouteEnabled);
                    AddToggleRow(pages[0], "feature.helicopterGate", ModSettings.PassengerHelicopterGateAssignmentEnabled);
                    AddToggleRow(pages[0], "feature.helicopterReroute", ModSettings.PassengerHelicopterRerouteEnabled);
                    AddToggleRow(pages[0], "feature.helicopterCapacity", ModSettings.PassengerHelicopterCapacityEnabled);

                    AddSliderRow(pages[1], Localization.Get("tune.rerouteDensity") + " (" + Localization.Get("feature.localBusReroute") + ")",
                        20f, 100f, 5f, ModSettings.LocalBusRerouteDensityThreshold.value,
                        v => ModSettings.LocalBusRerouteDensityThreshold.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.rerouteDensity") + " (" + Localization.Get("feature.trafficReroute") + ")",
                        20f, 100f, 5f, ModSettings.OrdinaryTrafficRerouteDensityThreshold.value,
                        v => ModSettings.OrdinaryTrafficRerouteDensityThreshold.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.helicopterCapacity"), 100f, 400f, 10f,
                        ModSettings.PassengerHelicopterCapacityPercent.value,
                        v => ModSettings.PassengerHelicopterCapacityPercent.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.intercityBus",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "feature.intercityBusReroute", ModSettings.IntercityBusRerouteEnabled);

                    AddSliderRow(pages[1], Localization.Get("tune.rerouteDensity"), 20f, 100f, 5f,
                        ModSettings.IntercityBusRerouteDensityThreshold.value,
                        v => ModSettings.IntercityBusRerouteDensityThreshold.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.citizensRaces",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "feature.citizenCar", ModSettings.CitizenCarProbabilityEnabled);
                    AddToggleRow(pages[0], "feature.citizenTaxi", ModSettings.CitizenTaxiProbabilityEnabled);
                    AddToggleRow(pages[0], "feature.raceAttractiveness", ModSettings.RaceBuildingAttractivenessEnabled);

                    AddSliderRow(pages[1], Localization.Get("tune.citizenCarDensity"), 20f, 100f, 5f,
                        ModSettings.CitizenCarDensityThreshold.value,
                        v => ModSettings.CitizenCarDensityThreshold.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.citizenCarReduction"), 0f, 100f, 5f,
                        ModSettings.CitizenCarMaxReductionPercent.value,
                        v => ModSettings.CitizenCarMaxReductionPercent.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.taxiMultiplier"), 100f, 400f, 10f,
                        ModSettings.CitizenTaxiMultiplierPercent.value,
                        v => ModSettings.CitizenTaxiMultiplierPercent.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.taxiFlatBonus"), 0f, 20f, 1f,
                        ModSettings.CitizenTaxiFlatBonus.value,
                        v => ModSettings.CitizenTaxiFlatBonus.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.raceAttractiveness"), 100f, 400f, 10f,
                        ModSettings.RaceBuildingAttractivenessPercent.value,
                        v => ModSettings.RaceBuildingAttractivenessPercent.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.citizenAI",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "feature.citizenTransportMode", ModSettings.CitizenTransportModeEnabled);

                    AddSliderRow(pages[1], Localization.Get("tune.citizenWalkWeight"), 0f, 100f, 1f,
                        ModSettings.CitizenWalkWeight.value,
                        v => ModSettings.CitizenWalkWeight.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.citizenDriveWeight"), 0f, 100f, 1f,
                        ModSettings.CitizenDriveWeight.value,
                        v => ModSettings.CitizenDriveWeight.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.citizenTaxiWeight"), 0f, 100f, 1f,
                        ModSettings.CitizenTaxiWeight.value,
                        v => ModSettings.CitizenTaxiWeight.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[1], Localization.Get("tune.citizenTransitWeight"), 0f, 100f, 1f,
                        ModSettings.CitizenTransitWeight.value,
                        v => ModSettings.CitizenTransitWeight.value = Mathf.RoundToInt(v), "0");

                    AddCitizenTransportPresets(pages[1], root, helper);
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.advanced",
                new[] { "subtab.tuning" },
                pages =>
                {
                    AddSliderRow(pages[0], Localization.Get("tune.rerouteCooldown"), 5f, 120f, 5f,
                        ModSettings.RerouteCooldownSeconds.value,
                        v => ModSettings.RerouteCooldownSeconds.value = Mathf.RoundToInt(v), "0");
                    AddSliderRow(pages[0], Localization.Get("tune.checkInterval"), 1f, 128f, 1f,
                        ModSettings.RerouteCheckIntervalFrames.value,
                        v => ModSettings.RerouteCheckIntervalFrames.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "tab.about",
                new[] { "subtab.links" },
                pages => BuildAboutPage(pages[0]));

            SelectSection(0);
        }

        private static UIComponent GetRoot(UIHelperBase helper) => (helper as UIHelper)?.self as UIComponent;

        private static void BuildHeader(UIComponent root, UIHelperBase helper)
        {
            UIPanel header = root.AddUIComponent<UIPanel>();
            header.width = root.width;
            header.height = HeaderHeight;
            header.atlas = SolidColorSprite.Atlas;
            header.backgroundSprite = SolidColorSprite.SpriteName;
            header.color = HeaderColor;
            header.relativePosition = Vector3.zero;

            UILabel title = header.AddUIComponent<UILabel>();
            title.text = "AI_Improve";
            title.textScale = 1.4f;
            title.textColor = Color.white;
            title.relativePosition = new Vector3(16f, 14f);

            UILabel version = header.AddUIComponent<UILabel>();
            version.text = Localization.Get("about.version", GetVersionString());
            version.textScale = 0.75f;
            version.textColor = MutedTextColor;
            version.relativePosition = new Vector3(16f, 46f);

            UILabel status = header.AddUIComponent<UILabel>();
            status.text = Localization.Get("header.status");
            status.textScale = 0.75f;
            status.textColor = new Color32(120, 220, 140, 255);
            status.relativePosition = new Vector3(16f, 62f);

            UIButton languageButton = header.AddUIComponent<UIButton>();
            languageButton.width = 130f;
            languageButton.height = 32f;
            languageButton.textScale = 0.8f;
            StyleAccentButton(languageButton);
            RefreshLanguageButtonText(languageButton);
            languageButton.relativePosition = new Vector3(header.width - languageButton.width - 16f, 14f);
            languageButton.eventClick += (component, param) =>
            {
                CycleLanguage();
                RebuildInPlace(root, helper);
            };

            UIButton changelog = header.AddUIComponent<UIButton>();
            changelog.text = Localization.Get("header.changelog");
            changelog.width = 170f;
            changelog.height = 32f;
            changelog.textScale = 0.8f;
            StyleAccentButton(changelog);
            changelog.relativePosition = new Vector3(header.width - changelog.width - 16f, 14f + languageButton.height + 6f);
            changelog.eventClick += (component, param) => Application.OpenURL(RepoUrl + "/commits/main");
        }

        private static readonly string[] LanguageCodes = { "auto", "en", "zh-tw", "zh-cn" };
        private static readonly string[] LanguageLabels = { "Auto", "English", "繁體中文", "简体中文" };

        private static void CycleLanguage()
        {
            int index = Array.IndexOf(LanguageCodes, ModSettings.LanguageOverride.value);
            int nextIndex = (Mathf.Max(index, 0) + 1) % LanguageCodes.Length;
            ModSettings.LanguageOverride.value = LanguageCodes[nextIndex];
        }

        private static void RefreshLanguageButtonText(UIButton button)
        {
            int index = Array.IndexOf(LanguageCodes, ModSettings.LanguageOverride.value);
            button.text = LanguageLabels[Mathf.Max(index, 0)];
        }

        private static void AddSection(
            UIComponent root, UIComponent nav, float sectionX, float sectionWidth,
            string navKey, string[] subTabKeys, Action<UIPanel[]> fillPages)
        {
            int index = Sections.Count;

            UIButton navButton = nav.AddUIComponent<UIButton>();
            navButton.text = Localization.Get(navKey);
            navButton.width = NavWidth - 12f;
            navButton.height = 30f;
            navButton.textScale = 0.8f;
            navButton.atlas = SolidColorSprite.Atlas;
            navButton.normalBgSprite = SolidColorSprite.SpriteName;
            navButton.color = NavItemColor;
            navButton.hoveredColor = NavItemHoverColor;
            navButton.pressedColor = AccentPressedColor;
            navButton.textColor = Color.white;
            navButton.textHorizontalAlignment = UIHorizontalAlignment.Left;
            navButton.textPadding = new RectOffset(10, 0, 7, 0);
            navButton.eventClick += (component, param) => SelectSection(index);
            NavButtons.Add(navButton);

            UIPanel section = root.AddUIComponent<UIPanel>();
            section.width = sectionWidth;
            section.height = BodyHeight;
            section.relativePosition = new Vector3(sectionX, HeaderHeight + 6f);
            section.isVisible = false;
            Sections.Add(section);

            if (subTabKeys.Length == 1)
            {
                // Single-page section (General/Advanced/About) - no point in a one-item tab strip.
                UIPanel onlyPage = section.AddUIComponent<UIPanel>();
                onlyPage.width = sectionWidth;
                onlyPage.height = BodyHeight;
                onlyPage.relativePosition = Vector3.zero;
                onlyPage.autoLayout = true;
                onlyPage.autoLayoutDirection = LayoutDirection.Vertical;
                onlyPage.autoLayoutPadding = new RectOffset(0, 0, 0, 6);
                onlyPage.padding = new RectOffset(10, 10, 12, 10);

                fillPages(new[] { onlyPage });
                return;
            }

            UITabstrip strip = section.AddUIComponent<UITabstrip>();
            strip.width = sectionWidth;
            strip.height = SubTabHeight;
            strip.relativePosition = Vector3.zero;

            UITabContainer container = section.AddUIComponent<UITabContainer>();
            container.width = sectionWidth;
            container.height = BodyHeight - SubTabHeight - 6f;
            container.relativePosition = new Vector3(0f, SubTabHeight + 6f);
            strip.tabPages = container;

            foreach (string key in subTabKeys)
            {
                StyleTab(strip.AddTab(Localization.Get(key)));
            }

            UIPanel[] pages = new UIPanel[subTabKeys.Length];
            for (int i = 0; i < subTabKeys.Length; i++)
            {
                if (i >= container.components.Count || !(container.components[i] is UIPanel page))
                {
                    Debug.Log("[AIImprove] SettingsPageUI: sub-tab page " + i + " missing for " + navKey + ".");
                    return;
                }

                // BUG FOUND VIA SCREENSHOT (2026-08-15): UITabContainer.AddTabPage(string) doesn't
                // hide the pages it creates (only the lower-level GameObject overload does), and
                // UITabstrip.selectedIndex no-ops when set to its already-default 0 - so without
                // this every page in a section renders stacked on top of the others.
                page.isVisible = i == 0;
                page.autoLayout = true;
                page.autoLayoutDirection = LayoutDirection.Vertical;
                page.autoLayoutPadding = new RectOffset(0, 0, 0, 6);
                page.padding = new RectOffset(10, 10, 12, 10);
                pages[i] = page;
            }

            strip.selectedIndex = 0;
            fillPages(pages);
        }

        private static void SelectSection(int index)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                Sections[i].isVisible = i == index;
                NavButtons[i].color = i == index ? AccentColor : NavItemColor;
                NavButtons[i].hoveredColor = i == index ? AccentHoverColor : NavItemHoverColor;
            }
        }

        private static void StyleTab(UIButton tab)
        {
            tab.width = 140f;
            tab.height = SubTabHeight;
            tab.atlas = SolidColorSprite.Atlas;
            tab.normalBgSprite = SolidColorSprite.SpriteName;
            tab.textColor = Color.white;
            tab.textScale = 0.85f;
            tab.color = TabColor;
            tab.hoveredColor = TabHoverColor;
            tab.focusedColor = AccentColor;
            tab.pressedColor = AccentPressedColor;
        }

        private static void StyleAccentButton(UIButton button)
        {
            button.atlas = SolidColorSprite.Atlas;
            button.normalBgSprite = SolidColorSprite.SpriteName;
            button.color = AccentColor;
            button.hoveredColor = AccentHoverColor;
            button.pressedColor = AccentPressedColor;
            button.textColor = Color.white;
        }

        private static void BuildGeneralPage(UIPanel page, UIComponent root, UIHelperBase helper)
        {
            AddLanguageDropdown(page, root, helper);
            AddPlainToggleRow(page, Localization.Get("tune.verboseLogging"), ModSettings.VerboseLogging);
        }

        private static void AddLanguageDropdown(UIPanel page, UIComponent root, UIHelperBase helper)
        {
            UIPanel row = page.AttachUIComponent(UITemplateManager.GetAsGameObject(DropdownTemplate)) as UIPanel;
            if (row == null)
            {
                return;
            }

            row.width = page.width - 20f;

            UILabel label = row.Find<UILabel>("Label");
            if (label != null)
            {
                label.text = Localization.Get("lang.label");
            }

            UIDropDown dropdown = row.Find<UIDropDown>("Dropdown");
            if (dropdown == null)
            {
                return;
            }

            dropdown.items = LanguageLabels;
            int current = Array.IndexOf(LanguageCodes, ModSettings.LanguageOverride.value);
            dropdown.selectedIndex = Mathf.Max(current, 0);

            dropdown.eventSelectedIndexChanged += (component, index) =>
            {
                if (index < 0 || index >= LanguageCodes.Length ||
                    ModSettings.LanguageOverride.value == LanguageCodes[index])
                {
                    return;
                }

                ModSettings.LanguageOverride.value = LanguageCodes[index];
                RebuildInPlace(root, helper);
            };
        }

        // Row shows just the feature name plus a pill switch. The description text still exists
        // (Localization's ".desc" keys) but sits on the row's tooltip rather than always-on
        // wrapped text, which would risk overflowing a fixed-height row.
        private static void AddToggleRow(UIComponent parent, string featureKey, SavedBool setting)
        {
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width = parent.width - 20f;
            row.height = 30f;
            row.tooltip = Localization.Get(featureKey + ".desc");

            UILabel label = row.AddUIComponent<UILabel>();
            label.text = Localization.Get(featureKey);
            label.textScale = 0.85f;
            label.relativePosition = new Vector3(4f, 7f);

            AddPillToggle(row, row.width - 54f, 4f, setting);
        }

        private static void AddPlainToggleRow(UIComponent parent, string label, SavedBool setting)
        {
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width = parent.width - 20f;
            row.height = 30f;

            UILabel rowLabel = row.AddUIComponent<UILabel>();
            rowLabel.text = label;
            rowLabel.textScale = 0.85f;
            rowLabel.relativePosition = new Vector3(4f, 7f);

            AddPillToggle(row, row.width - 54f, 4f, setting);
        }

        private static void AddPillToggle(UIComponent parent, float x, float y, SavedBool setting)
        {
            const float width = 44f;
            const float height = 22f;

            UIPanel background = parent.AddUIComponent<UIPanel>();
            background.atlas = SolidColorSprite.Atlas;
            background.backgroundSprite = SolidColorSprite.SpriteName;
            background.width = width;
            background.height = height;
            background.relativePosition = new Vector3(x, y);
            background.isInteractive = true;

            UIPanel knob = background.AddUIComponent<UIPanel>();
            knob.atlas = SolidColorSprite.Atlas;
            knob.backgroundSprite = SolidColorSprite.SpriteName;
            knob.color = Color.white;
            knob.width = height - 4f;
            knob.height = height - 4f;
            knob.isInteractive = false;

            void Refresh()
            {
                bool isOn = setting.value;
                background.color = isOn ? PillOnColor : PillOffColor;
                knob.relativePosition = new Vector3(isOn ? width - height + 2f : 2f, 2f);
            }

            Refresh();

            background.eventClick += (component, param) =>
            {
                setting.value = !setting.value;
                Refresh();
            };
        }

        // Cloned from the game's own options slider prefab so it matches vanilla styling, with the
        // current value shown to the right of the track.
        private static void AddSliderRow(
            UIComponent parent, string label, float min, float max, float step,
            float initialValue, Action<float> setValue, string valueFormat)
        {
            UIPanel row = parent.AttachUIComponent(UITemplateManager.GetAsGameObject(SliderTemplate)) as UIPanel;
            if (row == null)
            {
                return;
            }

            row.width = parent.width - 20f;

            UILabel titleLabel = row.Find<UILabel>("Label");
            if (titleLabel != null)
            {
                titleLabel.text = label;
                titleLabel.textScale = 0.8f;
            }

            UISlider slider = row.Find<UISlider>("Slider");
            if (slider == null)
            {
                return;
            }

            slider.minValue = min;
            slider.maxValue = max;
            slider.stepSize = step;
            slider.value = initialValue;
            slider.width = row.width - 90f;

            UILabel valueLabel = row.AddUIComponent<UILabel>();
            valueLabel.textScale = 0.85f;
            valueLabel.textColor = MutedTextColor;
            valueLabel.autoSize = false;
            valueLabel.width = 70f;
            valueLabel.height = 20f;
            valueLabel.textAlignment = UIHorizontalAlignment.Right;
            valueLabel.text = initialValue.ToString(valueFormat);
            valueLabel.relativePosition = new Vector3(row.width - 78f, slider.relativePosition.y);

            slider.eventValueChanged += (component, val) =>
            {
                setValue(val);
                valueLabel.text = val.ToString(valueFormat);
            };
        }

        private struct CitizenTransportPreset
        {
            public string LabelKey;
            public int Walk;
            public int Drive;
            public int Taxi;
            public int Transit;

            public CitizenTransportPreset(string labelKey, int walk, int drive, int taxi, int transit)
            {
                LabelKey = labelKey;
                Walk = walk;
                Drive = drive;
                Taxi = taxi;
                Transit = transit;
            }
        }

        // Four one-click presets for the Walk/Drive/Taxi/Transit weights - "直接套用模板［模板中的
        // 百分比我想你幫我分配］［我想總共會有 4 個模板］" (2026-08-15). Percentages are mine to pick;
        // each sums to 100 for readability, though the patch itself normalizes any 4 values.
        private static readonly CitizenTransportPreset[] CitizenTransportPresets =
        {
            new CitizenTransportPreset("preset.balanced", 30, 25, 5, 40),
            new CitizenTransportPreset("preset.transitOriented", 25, 10, 5, 60),
            new CitizenTransportPreset("preset.carDependent", 15, 60, 10, 15),
            new CitizenTransportPreset("preset.walkable", 55, 5, 5, 35),
        };

        private static void AddCitizenTransportPresets(UIComponent parent, UIComponent root, UIHelperBase helper)
        {
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width = parent.width - 20f;
            row.height = 32f;
            row.autoLayout = true;
            row.autoLayoutDirection = LayoutDirection.Horizontal;
            row.autoLayoutPadding = new RectOffset(0, 8, 0, 0);

            foreach (CitizenTransportPreset preset in CitizenTransportPresets)
            {
                UIButton button = row.AddUIComponent<UIButton>();
                button.text = Localization.Get(preset.LabelKey);
                button.width = (row.width - 24f) / 4f;
                button.height = 30f;
                button.textScale = 0.7f;
                StyleAccentButton(button);
                button.eventClick += (component, param) =>
                {
                    ModSettings.CitizenWalkWeight.value = preset.Walk;
                    ModSettings.CitizenDriveWeight.value = preset.Drive;
                    ModSettings.CitizenTaxiWeight.value = preset.Taxi;
                    ModSettings.CitizenTransitWeight.value = preset.Transit;
                    RebuildInPlace(root, helper);
                };
            }
        }

        private static void BuildAboutPage(UIPanel page)
        {
            AddLinkButton(page, Localization.Get("about.github"), RepoUrl);
            AddLinkButton(page, Localization.Get("about.wiki"), RepoUrl + "/wiki");
            AddLinkButton(page, Localization.Get("about.workshop"), WorkshopUrl);
            AddLinkButton(page, Localization.Get("about.reportIssue"), RepoUrl + "/issues/new/choose");
        }

        private static void AddLinkButton(UIComponent parent, string label, string url)
        {
            UIButton button = parent.AddUIComponent<UIButton>();
            button.text = label;
            button.width = parent.width - 20f;
            button.height = 32f;
            button.textScale = 0.85f;
            StyleAccentButton(button);
            button.eventClick += (component, param) => Application.OpenURL(url);
        }

        private static string GetVersionString()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            object[] attributes = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            if (attributes.Length > 0)
            {
                string informational = ((AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion;
                if (!string.IsNullOrEmpty(informational))
                {
                    return informational;
                }
            }

            Version version = assembly.GetName().Version;
            return version.Major + "." + version.Minor + "." + version.Build;
        }

        // Safety net for the unlikely case GetRoot or the tab construction doesn't work in-game.
        // Flat list of the nine broad categories (not all ~19 features - this is a degraded
        // fallback, not a second full UI to maintain), each toggle driving every feature that used
        // to share that legacy category so the fallback still does something sensible.
        private static void BuildFlatFallback(UIHelperBase helper)
        {
            AddFlatGroup(helper, "緊急車輛 (Emergency)", ModSettings.FireResponseCapEnabled, ModSettings.FireIdleSeekEnabled, ModSettings.HelicopterWeatherHaltEnabled);
            AddFlatGroup(helper, "地鐵 (Metro)", ModSettings.MetroPlatformAssignmentEnabled, ModSettings.MetroRerouteEnabled);
            AddFlatGroup(helper, "城際火車 (Intercity trains)", ModSettings.IntercityTrainPlatformAssignmentEnabled, ModSettings.IntercityTrainRerouteEnabled, ModSettings.IntercityTrainSpawnThrottleEnabled);
            AddFlatGroup(helper, "飛機與機場 (Aircraft)", ModSettings.AircraftGateAssignmentEnabled, ModSettings.AircraftRerouteEnabled, ModSettings.AircraftThunderstormRefusalEnabled);
            AddFlatGroup(helper, "市內巴士與客運直升機 (Local transport)", ModSettings.LocalBusRerouteEnabled, ModSettings.PassengerHelicopterRerouteEnabled);
            AddFlatGroup(helper, "城際巴士 (Intercity buses)", ModSettings.IntercityBusRerouteEnabled);
            AddFlatGroup(helper, "一般市內交通 (Ordinary traffic)", ModSettings.OrdinaryTrafficRerouteEnabled);
            AddFlatGroup(helper, "市民行為 (Citizens)", ModSettings.CitizenCarProbabilityEnabled, ModSettings.CitizenTaxiProbabilityEnabled, ModSettings.CitizenTransportModeEnabled);
            AddFlatGroup(helper, "賽車 (Race cars)", ModSettings.RaceBuildingAttractivenessEnabled);
        }

        private static void AddFlatGroup(UIHelperBase helper, string title, params SavedBool[] settings)
        {
            UIHelperBase group = helper.AddGroup(title);
            for (int i = 0; i < settings.Length; i++)
            {
                SavedBool setting = settings[i];
                group.AddCheckbox(title + " #" + (i + 1), setting.value, value => setting.value = value);
            }
        }
    }
}
