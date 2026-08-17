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
    // REBUILT (2026-08-17, "全新設計 content manager 中的 UI" + a Canva mockup the user approved).
    // The previous version split every section into a "Toggles" tab and a "Tuning" tab, which
    // meant a feature's switch and the numbers belonging to that same feature could never be on
    // screen together. Four changes came out of that redesign:
    //
    //  1. CARDS, NOT TABS. One card per feature, holding its toggle and its own sliders. The
    //     per-section tab strip is gone entirely (UITabstrip/UITabContainer no longer used, which
    //     also retires the AddTabPage visibility bug worked around in the old build).
    //  2. SEARCH. 48 controls across 12 sections was unnavigable. Features are now declared as
    //     data (Section/Feature/Tunable below) instead of being hand-built per section, so a
    //     single filter can match across every section at once.
    //  3. DESCRIPTIONS ARE VISIBLE. They used to live only in a tooltip, which players had no
    //     reason to know existed. Each card now renders its description under the title.
    //  4. RESET. See ModSettings.ResetAllToDefaults.
    //
    // Section bodies are UIScrollablePanel: cards are much taller than the old one-line rows, so
    // a busy section (Local Transport has five features) no longer fits in BodyHeight.
    //
    // Built on the page's real UIComponent tree (via the concrete UIHelper.self, which
    // UIHelperBase's interface doesn't expose). BuildContent is wrapped in a try/catch that falls
    // back to a plain checkbox list - this version leans on more hand-assembled components
    // (UITextField, UIScrollablePanel) than the old one, and a settings page that throws halfway
    // through would otherwise leave the player with a half-drawn page and no way to change
    // anything.
    internal static class SettingsPageUI
    {
        private const string RepoUrl = "https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve";
        private const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3782858610";

        private const string DropdownTemplate = "OptionsDropdownTemplate";
        private const string SliderTemplate = "OptionsSliderTemplate";

        private const float HeaderHeight = 112f;
        private const float NavWidth = 172f;
        private const float BodyHeight = 430f;
        private const float CardPadding = 14f;
        private const float RowHeight = 30f;

        private static readonly Color32 AccentColor = new Color32(58, 121, 187, 255);
        private static readonly Color32 AccentHoverColor = new Color32(78, 141, 207, 255);
        private static readonly Color32 AccentPressedColor = new Color32(45, 98, 154, 255);
        private static readonly Color32 HeaderColor = new Color32(35, 42, 54, 255);
        private static readonly Color32 NavColor = new Color32(30, 36, 46, 255);
        private static readonly Color32 NavItemColor = new Color32(36, 43, 55, 255);
        private static readonly Color32 NavItemHoverColor = new Color32(68, 74, 85, 255);
        private static readonly Color32 CardColor = new Color32(37, 44, 56, 255);
        private static readonly Color32 FieldColor = new Color32(22, 26, 33, 255);
        private static readonly Color32 PillOnColor = new Color32(76, 175, 80, 255);
        private static readonly Color32 PillOffColor = new Color32(95, 95, 100, 255);
        private static readonly Color32 MutedTextColor = new Color32(155, 163, 180, 255);
        private static readonly Color32 LabelTextColor = new Color32(195, 202, 216, 255);

        private static readonly List<UIButton> NavButtons = new List<UIButton>();
        private static readonly List<UIPanel> Sections = new List<UIPanel>();

        // Current search text. Kept across a RebuildInPlace so changing language (which rebuilds
        // the whole tree) doesn't silently drop what the player typed.
        private static string searchText = string.Empty;

        // Remembered so controls built deep inside a card (the citizen transport presets) can ask
        // for a rebuild without every builder having to thread root/helper down to them.
        private static UIComponent currentRoot;
        private static UIHelperBase currentHelper;

        // ------------------------------------------------------------------------------------
        // Declarative model
        // ------------------------------------------------------------------------------------
        // Everything the page renders is described here rather than imperatively built per
        // section. That is what makes cross-section search possible at all: filtering is just a
        // predicate over this list, instead of trying to reach into an already-constructed tree.

        private sealed class Tunable
        {
            public string LabelKey;
            public string DescKey;
            public float Min;
            public float Max;
            public float Step;
            public Func<float> Get;
            public Action<float> Set;
            public string Suffix = string.Empty;
            /// Optional extra text appended to the localized label, for the two cases where one
            /// section has the same tunable twice (local bus vs ordinary traffic density).
            public string LabelQualifierKey;
        }

        private sealed class Feature
        {
            /// Localization key stem: Key is the title, Key + ".desc" is the description.
            public string Key;
            /// null for a card that is only tunables (the Advanced section).
            public SavedBool Toggle;
            public readonly List<Tunable> Tunables = new List<Tunable>();
            /// Optional extra content drawn at the bottom of the card, after the sliders.
            /// Signature: (card, x, y, width) => vertical space consumed. Used for the citizen
            /// transport presets, which are buttons rather than a toggle or a slider.
            public Func<UIPanel, float, float, float, float> ExtraBuilder;
        }

        private sealed class Section
        {
            public string NavKey;
            public readonly List<Feature> Features = new List<Feature>();
            /// Non-null for the hand-built pages (General, About) that aren't feature cards.
            public Action<UIPanel, UIComponent, UIHelperBase> CustomBuilder;
        }

        private static Feature Toggle(string key, SavedBool setting)
        {
            return new Feature { Key = key, Toggle = setting };
        }

        private static Feature With(this Feature feature, string labelKey, float min, float max, float step,
            Func<float> get, Action<float> set, string suffix = "", string labelQualifierKey = null)
        {
            feature.Tunables.Add(new Tunable
            {
                LabelKey = labelKey,
                DescKey = labelKey + ".desc",
                Min = min,
                Max = max,
                Step = step,
                Get = get,
                Set = set,
                Suffix = suffix,
                LabelQualifierKey = labelQualifierKey,
            });
            return feature;
        }

        private static Feature WithExtra(this Feature feature, Func<UIPanel, float, float, float, float> builder)
        {
            feature.ExtraBuilder = builder;
            return feature;
        }

        private static List<Section> BuildModel()
        {
            var model = new List<Section>();

            // REORGANISED (2026-08-17, "我覺得要重新分類選單上的選項，就例如市民與賽車及市民交通AI").
            // The old section list still had the shape of the pre-split category toggles, and two
            // problems came out of that. "Citizens & Races" bundled citizen behaviour together
            // with a Races-DLC building bonus that has nothing to do with it, while "Citizen
            // Transport AI" - also citizen behaviour - sat in a separate section further down with
            // unrelated sections in between, so one topic was split across two places. Metro and
            // intercity trains were likewise apart despite both being rail, and "Local Transport"
            // mixed buses, passenger helicopters and private cars into one page.
            //
            // Sections are now ordered the way a player thinks about their city - who moves
            // (citizens), what responds to trouble (emergency), then one section per transport
            // mode - with races standing on their own as the DLC extra they are.
            //
            // Several sections still contain a slider labelled just "Reroute density threshold".
            // That is fine now in a way it wasn't under the old shared tuning tab: each one sits
            // inside its own feature card, so the card title supplies the context that previously
            // needed a qualifier appended to the label.

            model.Add(new Section { NavKey = "nav.general", CustomBuilder = BuildGeneralPage });

            model.Add(new Section
            {
                NavKey = "nav.citizens",
                Features =
                {
                    Toggle("feature.citizenCar", ModSettings.CitizenCarProbabilityEnabled)
                        .With("tune.citizenCarDensity", 20f, 100f, 5f,
                            () => ModSettings.CitizenCarDensityThreshold.value,
                            v => ModSettings.CitizenCarDensityThreshold.value = Mathf.RoundToInt(v))
                        .With("tune.citizenCarReduction", 0f, 100f, 5f,
                            () => ModSettings.CitizenCarMaxReductionPercent.value,
                            v => ModSettings.CitizenCarMaxReductionPercent.value = Mathf.RoundToInt(v), "%"),
                    Toggle("feature.citizenTaxi", ModSettings.CitizenTaxiProbabilityEnabled)
                        .With("tune.taxiMultiplier", 100f, 400f, 10f,
                            () => ModSettings.CitizenTaxiMultiplierPercent.value,
                            v => ModSettings.CitizenTaxiMultiplierPercent.value = Mathf.RoundToInt(v), "%")
                        .With("tune.taxiFlatBonus", 0f, 20f, 1f,
                            () => ModSettings.CitizenTaxiFlatBonus.value,
                            v => ModSettings.CitizenTaxiFlatBonus.value = Mathf.RoundToInt(v)),
                    Toggle("feature.citizenTransportMode", ModSettings.CitizenTransportModeEnabled)
                        .With("tune.citizenWalkWeight", 0f, 100f, 1f,
                            () => ModSettings.CitizenWalkWeight.value,
                            v => ModSettings.CitizenWalkWeight.value = Mathf.RoundToInt(v))
                        .With("tune.citizenDriveWeight", 0f, 100f, 1f,
                            () => ModSettings.CitizenDriveWeight.value,
                            v => ModSettings.CitizenDriveWeight.value = Mathf.RoundToInt(v))
                        .With("tune.citizenTaxiWeight", 0f, 100f, 1f,
                            () => ModSettings.CitizenTaxiWeight.value,
                            v => ModSettings.CitizenTaxiWeight.value = Mathf.RoundToInt(v))
                        .With("tune.citizenTransitWeight", 0f, 100f, 1f,
                            () => ModSettings.CitizenTransitWeight.value,
                            v => ModSettings.CitizenTransitWeight.value = Mathf.RoundToInt(v))
                        .WithExtra(AddCitizenTransportPresets),
                },
            });

            model.Add(new Section
            {
                NavKey = "nav.emergency",
                Features =
                {
                    Toggle("feature.fireResponseCap", ModSettings.FireResponseCapEnabled)
                        .With("tune.fireResponders", 5f, 50f, 1f,
                            () => ModSettings.FireMaxRespondersPerBuilding.value,
                            v => ModSettings.FireMaxRespondersPerBuilding.value = Mathf.RoundToInt(v))
                        .With("tune.fireUncapMinutes", 5f, 60f, 1f,
                            () => ModSettings.FireUncapAfterMinutes.value,
                            v => ModSettings.FireUncapAfterMinutes.value = Mathf.RoundToInt(v)),
                    Toggle("feature.fireIdleSeek", ModSettings.FireIdleSeekEnabled),
                    Toggle("feature.helicopterWeatherHalt", ModSettings.HelicopterWeatherHaltEnabled),
                },
            });

            model.Add(new Section
            {
                NavKey = "nav.road",
                Features =
                {
                    Toggle("feature.trafficReroute", ModSettings.OrdinaryTrafficRerouteEnabled)
                        .With("tune.rerouteDensity", 20f, 100f, 5f,
                            () => ModSettings.OrdinaryTrafficRerouteDensityThreshold.value,
                            v => ModSettings.OrdinaryTrafficRerouteDensityThreshold.value = Mathf.RoundToInt(v)),
                    Toggle("feature.localBusReroute", ModSettings.LocalBusRerouteEnabled)
                        .With("tune.rerouteDensity", 20f, 100f, 5f,
                            () => ModSettings.LocalBusRerouteDensityThreshold.value,
                            v => ModSettings.LocalBusRerouteDensityThreshold.value = Mathf.RoundToInt(v)),
                    Toggle("feature.intercityBusReroute", ModSettings.IntercityBusRerouteEnabled)
                        .With("tune.rerouteDensity", 20f, 100f, 5f,
                            () => ModSettings.IntercityBusRerouteDensityThreshold.value,
                            v => ModSettings.IntercityBusRerouteDensityThreshold.value = Mathf.RoundToInt(v)),
                },
            });

            model.Add(new Section
            {
                NavKey = "nav.rail",
                Features =
                {
                    Toggle("feature.metroPlatform", ModSettings.MetroPlatformAssignmentEnabled),
                    Toggle("feature.metroReroute", ModSettings.MetroRerouteEnabled)
                        .With("tune.rerouteDensity", 20f, 100f, 5f,
                            () => ModSettings.MetroRerouteDensityThreshold.value,
                            v => ModSettings.MetroRerouteDensityThreshold.value = Mathf.RoundToInt(v)),
                    Toggle("feature.trainPlatform", ModSettings.IntercityTrainPlatformAssignmentEnabled)
                        .With("tune.stationSaturation", 5f, 60f, 1f,
                            () => ModSettings.TrainStationSaturationThreshold.value,
                            v => ModSettings.TrainStationSaturationThreshold.value = Mathf.RoundToInt(v))
                        .With("tune.platformCandidates", 8f, 40f, 1f,
                            () => ModSettings.TrainPlatformCandidateCount.value,
                            v => ModSettings.TrainPlatformCandidateCount.value = Mathf.RoundToInt(v)),
                    Toggle("feature.trainReroute", ModSettings.IntercityTrainRerouteEnabled)
                        .With("tune.rerouteDensity", 20f, 100f, 5f,
                            () => ModSettings.IntercityTrainRerouteDensityThreshold.value,
                            v => ModSettings.IntercityTrainRerouteDensityThreshold.value = Mathf.RoundToInt(v)),
                    Toggle("feature.trainSpawnThrottle", ModSettings.IntercityTrainSpawnThrottleEnabled)
                        .With("tune.lowRidership", 0f, 200f, 5f,
                            () => ModSettings.IntercityLowRidershipThreshold.value,
                            v => ModSettings.IntercityLowRidershipThreshold.value = Mathf.RoundToInt(v))
                        .With("tune.lowRidershipSkipChance", 0f, 100f, 5f,
                            () => ModSettings.IntercityLowRidershipSkipPercent.value,
                            v => ModSettings.IntercityLowRidershipSkipPercent.value = Mathf.RoundToInt(v), "%"),
                    Toggle("feature.singleTrackDetector", ModSettings.SingleTrackConflictDetectorEnabled),
                },
            });

            model.Add(new Section
            {
                NavKey = "nav.aviation",
                Features =
                {
                    Toggle("feature.aircraftGate", ModSettings.AircraftGateAssignmentEnabled)
                        .With("tune.gateCandidates", 8f, 50f, 1f,
                            () => ModSettings.AircraftGateCandidateCount.value,
                            v => ModSettings.AircraftGateCandidateCount.value = Mathf.RoundToInt(v))
                        .With("tune.perGateCapacity", 1f, 20f, 1f,
                            () => ModSettings.AircraftPerGateCapacity.value,
                            v => ModSettings.AircraftPerGateCapacity.value = Mathf.RoundToInt(v)),
                    Toggle("feature.aircraftReroute", ModSettings.AircraftRerouteEnabled)
                        .With("tune.rerouteDensity", 20f, 100f, 5f,
                            () => ModSettings.AircraftRerouteDensityThreshold.value,
                            v => ModSettings.AircraftRerouteDensityThreshold.value = Mathf.RoundToInt(v)),
                    Toggle("feature.aircraftThunderstorm", ModSettings.AircraftThunderstormRefusalEnabled),
                    Toggle("feature.helicopterGate", ModSettings.PassengerHelicopterGateAssignmentEnabled),
                    Toggle("feature.helicopterReroute", ModSettings.PassengerHelicopterRerouteEnabled),
                    Toggle("feature.helicopterCapacity", ModSettings.PassengerHelicopterCapacityEnabled)
                        .With("tune.helicopterCapacity", 100f, 400f, 10f,
                            () => ModSettings.PassengerHelicopterCapacityPercent.value,
                            v => ModSettings.PassengerHelicopterCapacityPercent.value = Mathf.RoundToInt(v), "%"),
                },
            });

            model.Add(new Section
            {
                NavKey = "nav.shipping",
                Features =
                {
                    Toggle("feature.shipDock", ModSettings.ShipDockAssignmentEnabled)
                        .With("tune.gateCandidates", 8f, 50f, 1f,
                            () => ModSettings.ShipDockCandidateCount.value,
                            v => ModSettings.ShipDockCandidateCount.value = Mathf.RoundToInt(v))
                        .With("tune.stationSaturation", 5f, 60f, 1f,
                            () => ModSettings.ShipDockSaturationThreshold.value,
                            v => ModSettings.ShipDockSaturationThreshold.value = Mathf.RoundToInt(v)),
                },
            });

            model.Add(new Section
            {
                NavKey = "nav.races",
                Features =
                {
                    Toggle("feature.raceAttractiveness", ModSettings.RaceBuildingAttractivenessEnabled)
                        .With("tune.raceAttractiveness", 100f, 400f, 10f,
                            () => ModSettings.RaceBuildingAttractivenessPercent.value,
                            v => ModSettings.RaceBuildingAttractivenessPercent.value = Mathf.RoundToInt(v), "%"),
                },
            });

            model.Add(new Section
            {
                NavKey = "nav.advanced",
                Features =
                {
                    new Feature { Key = "feature.rerouteTiming" }
                        .With("tune.rerouteCooldown", 5f, 120f, 5f,
                            () => ModSettings.RerouteCooldownSeconds.value,
                            v => ModSettings.RerouteCooldownSeconds.value = Mathf.RoundToInt(v), "s")
                        .With("tune.checkInterval", 1f, 128f, 1f,
                            () => ModSettings.RerouteCheckIntervalFrames.value,
                            v => ModSettings.RerouteCheckIntervalFrames.value = Mathf.RoundToInt(v)),
                },
            });

            model.Add(new Section { NavKey = "tab.about", CustomBuilder = BuildAboutPage });

            return model;
        }

        // ------------------------------------------------------------------------------------
        // Entry point
        // ------------------------------------------------------------------------------------

        public static void Build(UIHelperBase helper)
        {
            UIComponent root = GetRoot(helper);
            if (root == null)
            {
                BuildFlatFallback(helper);
                return;
            }

            try
            {
                BuildContent(root, helper);
            }
            catch (Exception ex)
            {
                // A half-built page would leave the player unable to change anything at all, so
                // wipe whatever got as far as being created and fall back to the plain list.
                Debug.LogWarning(
                    "[AIImprove] SettingsPageUI failed to build the full page, falling back to a " +
                    "plain checkbox list. Reason: " + ex);

                for (int i = root.components.Count - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(root.components[i].gameObject);
                }

                BuildFlatFallback(helper);
            }
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

            currentRoot = root;
            currentHelper = helper;

            List<Section> model = BuildModel();

            // BUG FOUND VIA SCREENSHOT (2026-08-17): the page came out as header, then the nav
            // list, then the section content stacked underneath at full width - the left-nav /
            // content-beside-it layout was gone entirely, and the top of the header was scrolled
            // out of view.
            //
            // Root cause: the container Content Manager hands us has autoLayout switched on, so it
            // positions children itself and every relativePosition assigned below is discarded.
            // The stacked result (header + nav + a full-height section) is also taller than the
            // options viewport, which is why the title, search box and reset button had scrolled
            // off the top rather than being missing.
            //
            // Everything in this page is positioned absolutely, so auto-layout has to be off. Both
            // concrete types that can turn up here own their own autoLayout property (they don't
            // share a base class that declares it), hence the two casts.
            UIPanel rootPanel = root as UIPanel;
            if (rootPanel != null)
            {
                rootPanel.autoLayout = false;
            }

            UIScrollablePanel rootScrollable = root as UIScrollablePanel;
            if (rootScrollable != null)
            {
                rootScrollable.autoLayout = false;
            }

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

            bool searching = !string.IsNullOrEmpty(searchText);

            if (searching)
            {
                // Search results replace the whole body: matches are drawn as one flat list
                // across every section, with the owning section named on each card.
                nav.isVisible = false;
                BuildSearchResults(root, model, 0f, root.width);
                return;
            }

            for (int i = 0; i < model.Count; i++)
            {
                AddSection(root, nav, sectionX, sectionWidth, model[i], helper);
            }

            SelectSection(0);
        }

        private static UIComponent GetRoot(UIHelperBase helper) => (helper as UIHelper)?.self as UIComponent;

        // ------------------------------------------------------------------------------------
        // Header
        // ------------------------------------------------------------------------------------

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
            title.textScale = 1.35f;
            title.textColor = Color.white;
            title.relativePosition = new Vector3(16f, 12f);

            UILabel version = header.AddUIComponent<UILabel>();
            version.text = Localization.Get("about.version", GetVersionString());
            version.textScale = 0.7f;
            version.textColor = MutedTextColor;
            version.relativePosition = new Vector3(16f, 44f);

            UILabel status = header.AddUIComponent<UILabel>();
            status.text = Localization.Get("header.status");
            status.textScale = 0.7f;
            status.textColor = new Color32(120, 220, 140, 255);
            status.relativePosition = new Vector3(16f, 62f);

            // Right-hand controls, laid out right to left.
            UIButton reset = header.AddUIComponent<UIButton>();
            reset.text = Localization.Get("header.reset");
            reset.width = 150f;
            reset.height = 28f;
            reset.textScale = 0.72f;
            reset.tooltip = Localization.Get("header.reset.desc");
            StyleAccentButton(reset);
            reset.relativePosition = new Vector3(header.width - reset.width - 16f, 12f);
            reset.eventClick += (component, param) =>
            {
                ConfirmPanel.ShowModal("AI_Improve", Localization.Get("header.reset.confirm"), (comp, ret) =>
                {
                    if (ret != 1)
                    {
                        return;
                    }

                    ModSettings.ResetAllToDefaults();
                    RebuildInPlace(root, helper);
                });
            };

            UIButton changelog = header.AddUIComponent<UIButton>();
            changelog.text = Localization.Get("header.changelog");
            changelog.width = 150f;
            changelog.height = 28f;
            changelog.textScale = 0.72f;
            StyleAccentButton(changelog);
            changelog.relativePosition = new Vector3(header.width - changelog.width - 16f, 46f);
            changelog.eventClick += (component, param) => Application.OpenURL(RepoUrl + "/commits/main");

            AddSearchField(header, root, helper);
        }

        // Hand-assembled rather than cloned from a template: the game ships no options-page text
        // field prefab (UITemplateManager has OptionsDropdownTemplate/OptionsSliderTemplate but no
        // text-field equivalent), so the sprites come from SolidColorSprite like the pill toggles.
        //
        // Search applies on submit (Enter) and on focus loss rather than per keystroke, because
        // every change rebuilds the entire page - doing that on each character typed would destroy
        // the field mid-input and drop the player's caret.
        private static void AddSearchField(UIPanel header, UIComponent root, UIHelperBase helper)
        {
            float fieldWidth = Mathf.Min(300f, header.width - 400f);
            if (fieldWidth < 140f)
            {
                // Not enough header width on this resolution to place a search box without
                // colliding with the buttons - skip it rather than overlap them.
                return;
            }

            UILabel caption = header.AddUIComponent<UILabel>();
            caption.text = Localization.Get("header.search");
            caption.textScale = 0.7f;
            caption.textColor = MutedTextColor;
            caption.relativePosition = new Vector3(header.width - 182f - fieldWidth, 14f);

            UITextField field = header.AddUIComponent<UITextField>();
            field.atlas = SolidColorSprite.Atlas;
            field.normalBgSprite = SolidColorSprite.SpriteName;
            field.color = FieldColor;
            field.width = fieldWidth;
            field.height = 30f;
            field.padding = new RectOffset(8, 8, 7, 0);
            field.textScale = 0.8f;
            field.textColor = Color.white;
            field.cursorWidth = 2;
            field.cursorBlinkTime = 0.45f;
            field.selectOnFocus = true;
            field.selectionSprite = SolidColorSprite.SpriteName;
            field.selectionBackgroundColor = AccentColor;
            field.horizontalAlignment = UIHorizontalAlignment.Left;
            field.verticalAlignment = UIVerticalAlignment.Middle;
            field.builtinKeyNavigation = true;
            field.submitOnFocusLost = true;
            field.text = searchText;
            field.relativePosition = new Vector3(header.width - 182f - fieldWidth, 34f);

            field.eventTextSubmitted += (component, value) =>
            {
                string trimmed = (value ?? string.Empty).Trim();
                if (trimmed == searchText)
                {
                    return;
                }

                searchText = trimmed;
                RebuildInPlace(root, helper);
            };
        }

        // ------------------------------------------------------------------------------------
        // Sections
        // ------------------------------------------------------------------------------------

        private static void AddSection(
            UIComponent root, UIComponent nav, float sectionX, float sectionWidth,
            Section spec, UIHelperBase helper)
        {
            int index = Sections.Count;

            UIButton navButton = nav.AddUIComponent<UIButton>();
            navButton.text = Localization.Get(spec.NavKey);
            navButton.width = NavWidth - 12f;
            navButton.height = 30f;
            navButton.textScale = 0.78f;
            navButton.atlas = SolidColorSprite.Atlas;
            navButton.normalBgSprite = SolidColorSprite.SpriteName;
            navButton.color = NavItemColor;
            navButton.hoveredColor = NavItemHoverColor;
            navButton.pressedColor = AccentPressedColor;
            // BUG FOUND VIA SCREENSHOT (2026-08-17): the clicked nav entry rendered as a blank
            // white box. UIButton keeps focus after a click and falls back to its own default
            // focusedColor, which overrode the accent colour SelectSection assigns - and with
            // white-on-white the label vanished too. SelectSection drives the selected look, so
            // focus must not re-tint the button at all.
            navButton.focusedColor = NavItemColor;
            navButton.textColor = LabelTextColor;
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

            UIScrollablePanel body = CreateScrollBody(section, sectionWidth, BodyHeight);

            if (spec.CustomBuilder != null)
            {
                UIPanel page = body.AddUIComponent<UIPanel>();
                page.width = body.width - 20f;
                page.autoLayout = true;
                page.autoLayoutDirection = LayoutDirection.Vertical;
                page.autoLayoutPadding = new RectOffset(0, 0, 0, 6);
                page.autoFitChildrenVertically = true;
                spec.CustomBuilder(page, root, helper);
                return;
            }

            for (int i = 0; i < spec.Features.Count; i++)
            {
                AddFeatureCard(body, spec.Features[i], null);
            }
        }

        private static UIScrollablePanel CreateScrollBody(UIComponent parent, float width, float height)
        {
            UIScrollablePanel body = parent.AddUIComponent<UIScrollablePanel>();
            body.width = width;
            body.height = height;
            body.relativePosition = Vector3.zero;
            body.autoLayout = true;
            body.autoLayoutDirection = LayoutDirection.Vertical;
            body.autoLayoutPadding = new RectOffset(0, 0, 0, 8);
            body.clipChildren = true;
            body.scrollWheelDirection = UIOrientation.Vertical;
            body.scrollWheelAmount = 24;
            body.builtinKeyNavigation = true;
            return body;
        }

        private static void SelectSection(int index)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                bool selected = i == index;
                Sections[i].isVisible = selected;

                // focusedColor is set alongside color on purpose: the entry the player just
                // clicked is also the focused one, so leaving focusedColor at the non-selected
                // colour would immediately undo the accent highlight (see AddSection's note).
                NavButtons[i].color = selected ? AccentColor : NavItemColor;
                NavButtons[i].focusedColor = selected ? AccentColor : NavItemColor;
                NavButtons[i].hoveredColor = selected ? AccentHoverColor : NavItemHoverColor;
                NavButtons[i].textColor = selected ? Color.white : LabelTextColor;
            }
        }

        // ------------------------------------------------------------------------------------
        // Search
        // ------------------------------------------------------------------------------------

        private static void BuildSearchResults(UIComponent root, List<Section> model, float x, float width)
        {
            UIPanel section = root.AddUIComponent<UIPanel>();
            section.width = width;
            section.height = BodyHeight;
            section.relativePosition = new Vector3(x, HeaderHeight + 6f);
            Sections.Add(section);

            UIScrollablePanel body = CreateScrollBody(section, width, BodyHeight);

            string needle = searchText.ToLowerInvariant();
            int matches = 0;

            for (int s = 0; s < model.Count; s++)
            {
                Section spec = model[s];
                for (int f = 0; f < spec.Features.Count; f++)
                {
                    Feature feature = spec.Features[f];
                    if (!FeatureMatches(feature, needle))
                    {
                        continue;
                    }

                    AddFeatureCard(body, feature, Localization.Get(spec.NavKey));
                    matches++;
                }
            }

            if (matches == 0)
            {
                UILabel empty = body.AddUIComponent<UILabel>();
                empty.text = Localization.Get("search.noResults", searchText);
                empty.textScale = 0.85f;
                empty.textColor = MutedTextColor;
                empty.padding = new RectOffset(14, 14, 20, 0);
            }
        }

        private static bool FeatureMatches(Feature feature, string needle)
        {
            if (Contains(Localization.Get(feature.Key), needle) ||
                Contains(Localization.Get(feature.Key + ".desc"), needle))
            {
                return true;
            }

            for (int i = 0; i < feature.Tunables.Count; i++)
            {
                Tunable tunable = feature.Tunables[i];
                if (Contains(Localization.Get(tunable.LabelKey), needle) ||
                    Contains(Localization.Get(tunable.DescKey), needle))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.ToLowerInvariant().Contains(needle);
        }

        // ------------------------------------------------------------------------------------
        // Feature card
        // ------------------------------------------------------------------------------------

        // One card = one feature: its title, its always-visible description, its pill toggle, and
        // every slider that belongs to it. `sectionLabel`, when non-null, is drawn as a small
        // caption so a search result still says which section it came from.
        private static void AddFeatureCard(UIComponent parent, Feature feature, string sectionLabel)
        {
            UIPanel card = parent.AddUIComponent<UIPanel>();
            card.width = parent.width - 24f;
            card.atlas = SolidColorSprite.Atlas;
            card.backgroundSprite = SolidColorSprite.SpriteName;
            card.color = CardColor;

            float y = CardPadding;
            float innerWidth = card.width - (CardPadding * 2f);
            float titleRightEdge = feature.Toggle != null ? innerWidth - 60f : innerWidth;

            if (!string.IsNullOrEmpty(sectionLabel))
            {
                UILabel caption = card.AddUIComponent<UILabel>();
                caption.text = sectionLabel.ToUpperInvariant();
                caption.textScale = 0.62f;
                caption.textColor = new Color32(120, 150, 200, 255);
                caption.relativePosition = new Vector3(CardPadding, y);
                y += 16f;
            }

            UILabel title = card.AddUIComponent<UILabel>();
            title.text = Localization.Get(feature.Key);
            title.textScale = 0.95f;
            title.textColor = Color.white;
            title.autoSize = false;
            title.autoHeight = true;
            title.wordWrap = true;
            title.width = titleRightEdge;
            title.relativePosition = new Vector3(CardPadding, y);

            if (feature.Toggle != null)
            {
                AddPillToggle(card, card.width - CardPadding - 44f, y - 2f, feature.Toggle);
            }

            y += Mathf.Max(title.height, 20f) + 4f;

            string description = Localization.Get(feature.Key + ".desc");
            if (!string.IsNullOrEmpty(description) && description != feature.Key + ".desc")
            {
                // Also on the card as a whole, so hovering anywhere in it (not just the exact
                // description line) surfaces the text - same reasoning as AddTunableRow's note.
                card.tooltip = description;

                UILabel desc = card.AddUIComponent<UILabel>();
                desc.text = description;
                desc.textScale = 0.75f;
                desc.textColor = MutedTextColor;
                desc.autoSize = false;
                desc.autoHeight = true;
                desc.wordWrap = true;
                desc.width = innerWidth;
                desc.relativePosition = new Vector3(CardPadding, y);
                y += Mathf.Max(desc.height, 16f) + 6f;
            }

            for (int i = 0; i < feature.Tunables.Count; i++)
            {
                y += AddTunableRow(card, feature.Tunables[i], CardPadding, y, innerWidth);
            }

            if (feature.ExtraBuilder != null)
            {
                y += feature.ExtraBuilder(card, CardPadding, y, innerWidth);
            }

            card.height = y + CardPadding - 4f;
        }

        // Returns the vertical space consumed, so the caller can stack rows without needing the
        // template's own (auto-layout driven) height to be correct first.
        //
        // REVISED (2026-08-17, player report "滑鼠放在滑桿或按鈕上並沒有顯示註解"): the explanation
        // for each value used to exist only as a tooltip on the small caption label. Hovering the
        // obvious target - the slider track itself - therefore showed nothing at all. Rather than
        // chase that with more tooltips, the description is now drawn as visible text between the
        // label and the track, which is exactly what this redesign already decided to do for
        // feature descriptions and for the same reason: a tooltip nobody knows to hover for is not
        // documentation. The tooltip is still attached as well, on the whole row, so the text
        // being clipped on a narrow page doesn't lose the information.
        private static float AddTunableRow(UIPanel card, Tunable tunable, float x, float y, float width)
        {
            string label = Localization.Get(tunable.LabelKey);
            if (!string.IsNullOrEmpty(tunable.LabelQualifierKey))
            {
                label += " (" + Localization.Get(tunable.LabelQualifierKey) + ")";
            }

            string description = Localization.Get(tunable.DescKey);
            bool hasDescription = !string.IsNullOrEmpty(description) && description != tunable.DescKey;

            UILabel caption = card.AddUIComponent<UILabel>();
            caption.text = label;
            caption.textScale = 0.75f;
            caption.textColor = LabelTextColor;
            caption.relativePosition = new Vector3(x, y);
            if (hasDescription)
            {
                caption.tooltip = description;
            }

            UILabel valueLabel = card.AddUIComponent<UILabel>();
            valueLabel.textScale = 0.78f;
            valueLabel.textColor = Color.white;
            valueLabel.autoSize = false;
            valueLabel.width = 70f;
            valueLabel.height = 18f;
            valueLabel.textAlignment = UIHorizontalAlignment.Right;
            valueLabel.text = FormatValue(tunable, tunable.Get());
            valueLabel.relativePosition = new Vector3(x + width - 70f, y);

            float cursor = y + 18f;

            if (hasDescription)
            {
                UILabel desc = card.AddUIComponent<UILabel>();
                desc.text = description;
                desc.textScale = 0.68f;
                desc.textColor = MutedTextColor;
                desc.autoSize = false;
                desc.autoHeight = true;
                desc.wordWrap = true;
                desc.width = width;
                desc.relativePosition = new Vector3(x, cursor);
                cursor += Mathf.Max(desc.height, 13f) + 3f;
            }

            // Cloned from the game's own options slider so the track/thumb match vanilla exactly -
            // hand-building a UISlider needs several sprite names this project can't verify.
            UIPanel row = card.AttachUIComponent(UITemplateManager.GetAsGameObject(SliderTemplate)) as UIPanel;
            if (row == null)
            {
                return (cursor - y) + 6f;
            }

            row.autoLayout = false;
            row.width = width;
            row.height = 18f;
            row.relativePosition = new Vector3(x, cursor);
            if (hasDescription)
            {
                row.tooltip = description;
            }

            UILabel templateLabel = row.Find<UILabel>("Label");
            if (templateLabel != null)
            {
                // The template ships with its own caption; ours already sits above the track with
                // the value readout aligned to it, so hide the built-in one rather than have two.
                templateLabel.isVisible = false;
            }

            UISlider slider = row.Find<UISlider>("Slider");
            if (slider == null)
            {
                return (cursor - y) + 24f;
            }

            slider.minValue = tunable.Min;
            slider.maxValue = tunable.Max;
            slider.stepSize = tunable.Step;
            slider.value = tunable.Get();
            slider.width = width;
            slider.relativePosition = Vector3.zero;
            if (hasDescription)
            {
                slider.tooltip = description;
            }

            slider.eventValueChanged += (component, val) =>
            {
                tunable.Set(val);
                valueLabel.text = FormatValue(tunable, val);
            };

            return (cursor - y) + 26f;
        }

        private static string FormatValue(Tunable tunable, float value)
        {
            return Mathf.RoundToInt(value).ToString() + tunable.Suffix;
        }

        private struct CitizenTransportPreset
        {
            public readonly string LabelKey;
            public readonly int Walk;
            public readonly int Drive;
            public readonly int Taxi;
            public readonly int Transit;

            public CitizenTransportPreset(string labelKey, int walk, int drive, int taxi, int transit)
            {
                LabelKey = labelKey;
                Walk = walk;
                Drive = drive;
                Taxi = taxi;
                Transit = transit;
            }
        }

        // "直接套用模板［我想總共會有 4 個模板］" (2026-08-15). Percentages sum to 100 for
        // readability, though CitizenTransportModePatch normalizes whatever four values it finds.
        private static readonly CitizenTransportPreset[] CitizenTransportPresets =
        {
            new CitizenTransportPreset("preset.balanced", 30, 25, 5, 40),
            new CitizenTransportPreset("preset.transitOriented", 25, 10, 5, 60),
            new CitizenTransportPreset("preset.carDependent", 15, 60, 10, 15),
            new CitizenTransportPreset("preset.walkable", 55, 5, 5, 35),
        };

        private static float AddCitizenTransportPresets(UIPanel card, float x, float y, float width)
        {
            UILabel caption = card.AddUIComponent<UILabel>();
            caption.text = Localization.Get("preset.label");
            caption.textScale = 0.75f;
            caption.textColor = LabelTextColor;
            caption.relativePosition = new Vector3(x, y);

            float buttonY = y + 20f;
            float gap = 6f;
            float buttonWidth = (width - (gap * (CitizenTransportPresets.Length - 1))) / CitizenTransportPresets.Length;

            for (int i = 0; i < CitizenTransportPresets.Length; i++)
            {
                CitizenTransportPreset preset = CitizenTransportPresets[i];

                UIButton button = card.AddUIComponent<UIButton>();
                button.text = Localization.Get(preset.LabelKey);
                button.width = buttonWidth;
                button.height = 26f;
                button.textScale = 0.68f;
                StyleAccentButton(button);
                button.relativePosition = new Vector3(x + (buttonWidth + gap) * i, buttonY);
                button.eventClick += (component, param) =>
                {
                    ModSettings.CitizenWalkWeight.value = preset.Walk;
                    ModSettings.CitizenDriveWeight.value = preset.Drive;
                    ModSettings.CitizenTaxiWeight.value = preset.Taxi;
                    ModSettings.CitizenTransitWeight.value = preset.Transit;

                    // The four sliders above show the old positions until they are rebuilt.
                    if (currentRoot != null && currentHelper != null)
                    {
                        RebuildInPlace(currentRoot, currentHelper);
                    }
                };
            }

            return 52f;
        }

        // ------------------------------------------------------------------------------------
        // Shared controls
        // ------------------------------------------------------------------------------------

        private static void AddPillToggle(UIComponent parent, float x, float y, SavedBool setting)
        {
            const float width = 40f;
            const float height = 20f;

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

        private static void AddPlainToggleRow(UIComponent parent, string label, SavedBool setting, string tooltip = null)
        {
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width = parent.width - 20f;
            row.height = RowHeight;
            if (!string.IsNullOrEmpty(tooltip))
            {
                row.tooltip = tooltip;
            }

            UILabel rowLabel = row.AddUIComponent<UILabel>();
            rowLabel.text = label;
            rowLabel.textScale = 0.85f;
            rowLabel.relativePosition = new Vector3(4f, 7f);

            AddPillToggle(row, row.width - 50f, 4f, setting);
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

        // ------------------------------------------------------------------------------------
        // Hand-built pages
        // ------------------------------------------------------------------------------------

        private static void BuildGeneralPage(UIPanel page, UIComponent root, UIHelperBase helper)
        {
            AddLanguageDropdown(page, root, helper);
            AddPlainToggleRow(page, Localization.Get("tune.verboseLogging"), ModSettings.VerboseLogging,
                Localization.Get("tune.verboseLogging.desc"));
        }

        private static readonly string[] LanguageCodes = { "auto", "en", "zh-tw", "zh-cn" };
        private static readonly string[] LanguageLabels = { "Auto", "English", "繁體中文", "简体中文" };

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

        private static void BuildAboutPage(UIPanel page, UIComponent root, UIHelperBase helper)
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
            button.height = 30f;
            button.textScale = 0.82f;
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

        // Safety net for the unlikely case GetRoot returns null or the real page throws while
        // building. Deliberately a degraded view, not a second full UI to maintain.
        private static void BuildFlatFallback(UIHelperBase helper)
        {
            AddFlatGroup(helper, "緊急車輛 (Emergency)", ModSettings.FireResponseCapEnabled, ModSettings.FireIdleSeekEnabled, ModSettings.HelicopterWeatherHaltEnabled);
            AddFlatGroup(helper, "地鐵 (Metro)", ModSettings.MetroPlatformAssignmentEnabled, ModSettings.MetroRerouteEnabled);
            AddFlatGroup(helper, "城際火車 (Intercity trains)", ModSettings.IntercityTrainPlatformAssignmentEnabled, ModSettings.IntercityTrainRerouteEnabled, ModSettings.IntercityTrainSpawnThrottleEnabled);
            AddFlatGroup(helper, "飛機與機場 (Aircraft)", ModSettings.AircraftGateAssignmentEnabled, ModSettings.AircraftRerouteEnabled, ModSettings.AircraftThunderstormRefusalEnabled);
            AddFlatGroup(helper, "市內巴士與客運直升機 (Local transport)", ModSettings.LocalBusRerouteEnabled, ModSettings.PassengerHelicopterRerouteEnabled);
            AddFlatGroup(helper, "城際巴士 (Intercity buses)", ModSettings.IntercityBusRerouteEnabled);
            AddFlatGroup(helper, "貨運與船運 (Cargo & ships)", ModSettings.ShipDockAssignmentEnabled);
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
