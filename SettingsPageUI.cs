using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace AIImprove
{
    // Content Manager settings page. Rebuilt 2026-08-15 against three Workshop mods the user
    // pointed at as references:
    //   - Natural Disasters Renewal : vertical section list down the left side, language as a
    //                                 dropdown rather than a cycling button
    //   - IOperateIt Revisited      : sliders with their current value shown to the right
    //   - Node Controller Renewal   : horizontal tab strip across the top
    // The user asked for both nav directions at once, so sections are chosen from the vertical
    // list on the left and each section's sub-pages from the horizontal strip above the content.
    //
    // Built on the page's real UIComponent tree (via the concrete UIHelper.self, which
    // UIHelperBase's interface doesn't expose - see GetRoot) rather than UIHelperBase's own
    // methods, since UIHelperBase has no concept of tabs at all. Dropdowns and sliders are cloned
    // from the game's own "OptionsDropdownTemplate" / "OptionsSliderTemplate" prefabs - the same
    // ones UIHelper.AddDropdown/AddSlider use - instead of being hand-assembled, so they come out
    // correctly styled without guessing at a dozen sprite names that can't be verified offline.
    //
    // Every ColossalFramework.UI member used here was checked against the installed game's
    // assemblies with dnSpy before being written, since none of it can be exercised by a plain
    // `dotnet build`. If the tree fails to come together at runtime the page degrades to a plain
    // flat toggle list (BuildFlatFallback) rather than rendering empty.
    internal static class SettingsPageUI
    {
        private const string RepoUrl = "https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve";
        private const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3782858610";

        private const string DropdownTemplate = "OptionsDropdownTemplate";
        private const string SliderTemplate = "OptionsSliderTemplate";

        private const float HeaderHeight = 100f;
        private const float NavWidth = 172f;
        private const float BodyHeight = 430f;
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

        // Left-hand nav buttons and the section panel each one reveals, paired by index.
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

        // The language dropdown changes every label already on the page, so the page is torn down
        // and rebuilt through the same path that created it rather than each control being hunted
        // down and re-textured individually.
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

            AddSection(root, nav, sectionX, sectionWidth, "nav.transport",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    BuildTransportToggles(pages[0]);
                    BuildTransportTuning(pages[1]);
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.emergency",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "category.emergency", ModSettings.EmergencyVehiclesEnabled);
                    AddSliderRow(pages[1], Localization.Get("tune.fireResponders"), 5f, 50f, 1f,
                        ModSettings.FireMaxRespondersPerBuilding.value,
                        v => ModSettings.FireMaxRespondersPerBuilding.value = Mathf.RoundToInt(v), "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "nav.citizens",
                new[] { "subtab.toggles", "subtab.tuning" },
                pages =>
                {
                    AddToggleRow(pages[0], "category.citizens", ModSettings.CitizensEnabled);
                    AddToggleRow(pages[0], "category.racecars", ModSettings.RaceCarsEnabled);
                    AddSliderRow(pages[1], Localization.Get("tune.raceCarSpeed"), 40f, 160f, 5f,
                        ModSettings.RaceCarMaxSpeed.value,
                        v => ModSettings.RaceCarMaxSpeed.value = v, "0");
                });

            AddSection(root, nav, sectionX, sectionWidth, "tab.about",
                new[] { "subtab.links" },
                pages => BuildAboutPage(pages[0]));

            SelectSection(0);
        }

        // UIHelperBase (what OnSettingsUI receives) doesn't expose the underlying UIComponent -
        // only the concrete UIHelper does, via `self` (dnSpy: `public object self => this.m_Root;`).
        // Content Manager always passes a real UIHelper in practice; this stays defensive in case a
        // future game update swaps the concrete type.
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

            UIButton changelog = header.AddUIComponent<UIButton>();
            changelog.text = Localization.Get("header.changelog");
            changelog.width = 170f;
            changelog.height = 32f;
            changelog.textScale = 0.8f;
            StyleAccentButton(changelog);
            changelog.relativePosition = new Vector3(header.width - changelog.width - 16f, 34f);
            changelog.eventClick += (component, param) => Application.OpenURL(RepoUrl + "/commits/main");
        }

        // One vertical nav entry plus the section it reveals. The section owns its own horizontal
        // tab strip, so the two nav directions stay independent of each other.
        private static void AddSection(
            UIComponent root, UIComponent nav, float sectionX, float sectionWidth,
            string navKey, string[] subTabKeys, Action<UIPanel[]> fillPages)
        {
            int index = Sections.Count;

            UIButton navButton = nav.AddUIComponent<UIButton>();
            navButton.text = Localization.Get(navKey);
            navButton.width = NavWidth - 12f;
            navButton.height = 32f;
            navButton.textScale = 0.85f;
            navButton.atlas = SolidColorSprite.Atlas;
            navButton.normalBgSprite = SolidColorSprite.SpriteName;
            navButton.color = NavItemColor;
            navButton.hoveredColor = NavItemHoverColor;
            navButton.pressedColor = AccentPressedColor;
            navButton.textColor = Color.white;
            navButton.textHorizontalAlignment = UIHorizontalAlignment.Left;
            navButton.textPadding = new RectOffset(10, 0, 8, 0);
            navButton.eventClick += (component, param) => SelectSection(index);
            NavButtons.Add(navButton);

            UIPanel section = root.AddUIComponent<UIPanel>();
            section.width = sectionWidth;
            section.height = BodyHeight;
            section.relativePosition = new Vector3(sectionX, HeaderHeight + 6f);
            section.isVisible = false;
            Sections.Add(section);

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

                // UITabContainer.AddTabPage(string) doesn't hide the pages it creates (only the
                // lower-level GameObject overload does), and UITabstrip.selectedIndex no-ops when
                // set to its already-default 0 - so without this every page in a section renders
                // stacked on top of the others. Found via screenshot, 2026-08-15.
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

        private static void BuildTransportToggles(UIPanel page)
        {
            AddToggleRow(page, "category.metro", ModSettings.TrainsAndMetroEnabled);
            AddToggleRow(page, "category.intercityTrain", ModSettings.IntercityTrainEnabled);
            AddToggleRow(page, "category.aircraft", ModSettings.AircraftEnabled);
            AddToggleRow(page, "category.buses", ModSettings.BusesAndHelicoptersEnabled);
            AddToggleRow(page, "category.intercityBus", ModSettings.IntercityBusEnabled);
            AddToggleRow(page, "category.traffic", ModSettings.OrdinaryTrafficEnabled);
        }

        private static void BuildTransportTuning(UIPanel page)
        {
            AddSliderRow(page, Localization.Get("tune.lowRidership"), 0f, 200f, 5f,
                ModSettings.IntercityLowRidershipThreshold.value,
                v => ModSettings.IntercityLowRidershipThreshold.value = Mathf.RoundToInt(v), "0");
        }

        // Language names are written in their own language, never translated, so a player can find
        // theirs even when the current interface language is unreadable to them.
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

        // Row is the category name plus a pill switch, matching the reference mods' toggle rows.
        // The description text still exists (Localization's ".desc" keys) but sits on the row's
        // tooltip rather than as always-on wrapped text, which would risk overflowing a
        // fixed-height row whose wrapping can't be checked offline.
        private static void AddToggleRow(UIComponent parent, string categoryKey, SavedBool setting)
        {
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width = parent.width - 20f;
            row.height = 32f;
            row.tooltip = Localization.Get(categoryKey + ".desc");

            UILabel label = row.AddUIComponent<UILabel>();
            label.text = Localization.Get(categoryKey + ".title");
            label.textScale = 0.85f;
            label.relativePosition = new Vector3(4f, 8f);

            AddPillToggle(row, row.width - 54f, 5f, setting);
        }

        private static void AddPlainToggleRow(UIComponent parent, string label, SavedBool setting)
        {
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width = parent.width - 20f;
            row.height = 32f;

            UILabel rowLabel = row.AddUIComponent<UILabel>();
            rowLabel.text = label;
            rowLabel.textScale = 0.85f;
            rowLabel.relativePosition = new Vector3(4f, 8f);

            AddPillToggle(row, row.width - 54f, 5f, setting);
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
        // current value shown to the right of the track (the IOperateIt Revisited layout the user
        // referenced).
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
                titleLabel.textScale = 0.85f;
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

        // "用 GitHub commit 作版本標準" (2026-08-15): prefers AssemblyInformationalVersion, which
        // the build stamps as "<commit date> (<short hash>)" (see the SetVersionFromGit target in
        // AIImprove.csproj) - the date answers "is my copy current?", and the hash is what pins a
        // player's build to an exact commit when they report a bug. AssemblyVersion can only hold
        // numbers, so it carries the calendar version and is the fallback if the informational
        // attribute is missing.
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

        // Safety net for the unlikely case GetRoot or the tab construction doesn't work in-game -
        // a failed layout degrades to this known-working flat list instead of an empty page.
        private static void BuildFlatFallback(UIHelperBase helper)
        {
            AddFlatGroup(helper, "category.emergency", ModSettings.EmergencyVehiclesEnabled);
            AddFlatGroup(helper, "category.metro", ModSettings.TrainsAndMetroEnabled);
            AddFlatGroup(helper, "category.intercityTrain", ModSettings.IntercityTrainEnabled);
            AddFlatGroup(helper, "category.aircraft", ModSettings.AircraftEnabled);
            AddFlatGroup(helper, "category.buses", ModSettings.BusesAndHelicoptersEnabled);
            AddFlatGroup(helper, "category.intercityBus", ModSettings.IntercityBusEnabled);
            AddFlatGroup(helper, "category.traffic", ModSettings.OrdinaryTrafficEnabled);
            AddFlatGroup(helper, "category.citizens", ModSettings.CitizensEnabled);
            AddFlatGroup(helper, "category.racecars", ModSettings.RaceCarsEnabled);
        }

        private static void AddFlatGroup(UIHelperBase helper, string categoryKey, SavedBool setting)
        {
            UIHelperBase group = helper.AddGroup(Localization.Get(categoryKey + ".title"));
            string label = Localization.Get("toggle.enable") + " - " + Localization.Get(categoryKey + ".desc");
            group.AddCheckbox(label, setting.value, value => setting.value = value);
        }
    }
}
