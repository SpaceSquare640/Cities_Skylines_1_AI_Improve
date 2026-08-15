using System.Reflection;
using ColossalFramework;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace AIImprove
{
    // "你參考一下這兩個模組的 UI 設計...做得類似那兩個的 UI 設計" (2026-08-15, "完全還原" chosen
    // explicitly over the two lighter options): rebuilds the Content Manager settings page to match
    // the tabbed, card-header, pill-switch style ACME / Advanced Stop Selection use, instead of the
    // flat AddGroup-per-category list this page used before. Built directly on the page's real
    // UIComponent tree (obtained via the concrete UIHelper.self, which UIHelperBase's own interface
    // doesn't expose - see GetRoot below) rather than UIHelperBase's convenience methods, since
    // UIHelperBase has no concept of tabs at all. Every ColossalFramework.UI member used here (
    // UIHelper.self, UITabstrip/UITabContainer wiring, UITextureAtlas construction) was confirmed
    // via dnSpy against the installed game's Assembly-CSharp.dll/ColossalManaged.dll before writing
    // this, since none of it can be exercised by a plain `dotnet build` until tested in-game - if
    // any of it renders wrong, the fallback is reverting to the previous AddGroup-based page, not a
    // hard crash (every risky step here is additive UI construction, nothing game-state-affecting).
    internal static class SettingsPageUI
    {
        private const string RepoUrl = "https://github.com/SpaceSquare640/Cities_Skylines_1_AI_Improve";
        private const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3782858610";

        private static readonly Color32 AccentColor = new Color32(58, 121, 187, 255);
        private static readonly Color32 AccentHoverColor = new Color32(78, 141, 207, 255);
        private static readonly Color32 AccentPressedColor = new Color32(45, 98, 154, 255);
        private static readonly Color32 HeaderColor = new Color32(30, 40, 56, 255);
        private static readonly Color32 TabColor = new Color32(48, 52, 60, 255);
        private static readonly Color32 TabHoverColor = new Color32(64, 68, 78, 255);
        private static readonly Color32 PillOnColor = new Color32(76, 175, 80, 255);
        private static readonly Color32 PillOffColor = new Color32(95, 95, 100, 255);

        public static void Build(UIHelperBase helper)
        {
            UIComponent root = GetRoot(helper);
            if (root == null)
            {
                // Fall back to the plain flat list rather than showing nothing - see GetRoot's
                // notes on why this could return null (a future game update changing UIHelper's
                // internals).
                BuildFlatFallback(helper);
                return;
            }

            BuildContent(root, helper);
        }

        // "還是看不到切換語言的按鈕" (2026-08-15): the language-cycle button (see BuildHeader)
        // changes ModSettings.LanguageOverride and then needs every already-built label on this
        // page to switch text immediately - simplest correct way to do that without hand-tracking
        // every UILabel/UIButton this page creates is to tear the page down and build it again from
        // scratch, the same construction path Build() already uses.
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
            BuildHeader(root, helper);

            UITabstrip tabstrip = root.AddUIComponent<UITabstrip>();
            tabstrip.width = root.width;
            tabstrip.height = 34f;

            UITabContainer tabContainer = root.AddUIComponent<UITabContainer>();
            tabContainer.width = root.width;
            tabContainer.height = 420f;
            tabstrip.tabPages = tabContainer;

            StyleTab(tabstrip.AddTab(Localization.Get("tab.toggles")));
            StyleTab(tabstrip.AddTab(Localization.Get("tab.tuning")));
            StyleTab(tabstrip.AddTab(Localization.Get("tab.about")));
            tabstrip.selectedIndex = 0;

            if (tabContainer.components.Count >= 3 &&
                tabContainer.components[0] is UIPanel togglesPage &&
                tabContainer.components[1] is UIPanel tuningPage &&
                tabContainer.components[2] is UIPanel aboutPage)
            {
                // BUG FOUND VIA SCREENSHOT (2026-08-15): UITabContainer.AddTabPage(string) never
                // sets isVisible=false on the pages it creates (confirmed via dnSpy - only the
                // lower-level AddTabPage(string, GameObject, ...) overload does that), and
                // UITabstrip.selectedIndex's setter no-ops when the value already equals its
                // default (0), so the "hide every page except the selected one" logic never ran -
                // both tab pages rendered stacked on top of each other. Hiding every page but the
                // initially-selected one here sidesteps both quirks directly instead of fighting
                // the setter's default-value guard.
                tuningPage.isVisible = false;
                aboutPage.isVisible = false;

                BuildTogglesPage(togglesPage);
                BuildTuningPage(tuningPage);
                BuildAboutPage(aboutPage);
            }
            else
            {
                Debug.Log("[AIImprove] SettingsPageUI: tab pages missing after AddTab, falling back to flat list.");
                BuildFlatFallback(helper);
            }
        }

        // UIHelperBase (the interface OnSettingsUI receives) doesn't expose the underlying
        // UIComponent - only the concrete UIHelper class does, via its `self` property (confirmed
        // via dnSpy: `public object self => this.m_Root;`). Content Manager always hands OnSettingsUI
        // a real UIHelper instance in practice, but this stays defensive in case a future game
        // update swaps the concrete type.
        private static UIComponent GetRoot(UIHelperBase helper) => (helper as UIHelper)?.self as UIComponent;

        private static void BuildHeader(UIComponent root, UIHelperBase helper)
        {
            UIPanel header = root.AddUIComponent<UIPanel>();
            header.width = root.width;
            header.height = 100f;
            header.atlas = SolidColorSprite.Atlas;
            header.backgroundSprite = SolidColorSprite.SpriteName;
            header.color = HeaderColor;

            UILabel title = header.AddUIComponent<UILabel>();
            title.text = "AI_Improve";
            title.textScale = 1.4f;
            title.textColor = Color.white;
            title.relativePosition = new Vector3(16f, 14f);

            UILabel version = header.AddUIComponent<UILabel>();
            version.text = Localization.Get("about.version", GetVersionString());
            version.textScale = 0.75f;
            version.textColor = new Color32(190, 195, 205, 255);
            version.relativePosition = new Vector3(16f, 46f);

            UILabel status = header.AddUIComponent<UILabel>();
            status.text = Localization.Get("header.status");
            status.textScale = 0.75f;
            status.textColor = new Color32(120, 220, 140, 255);
            status.relativePosition = new Vector3(16f, 62f);

            UIButton languageButton = header.AddUIComponent<UIButton>();
            languageButton.width = 130f;
            languageButton.height = 34f;
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
            changelog.height = 34f;
            changelog.textScale = 0.8f;
            StyleAccentButton(changelog);
            changelog.relativePosition = new Vector3(header.width - changelog.width - 16f, 14f + languageButton.height + 6f);
            changelog.eventClick += (component, param) => Application.OpenURL(RepoUrl + "/commits/main");
        }

        // "還是看不到切換語言的按鈕" (2026-08-15): language names are shown in their OWN language
        // (not run through Localization.Get) so a player can find their target language even when
        // the current UI text is unreadable to them - the whole point of the button. Parallel
        // arrays instead of a tuple array - this project's target framework has no
        // System.ValueTuple reference.
        private static readonly string[] LanguageCodes = { "auto", "en", "zh-tw", "zh-cn" };
        private static readonly string[] LanguageLabels = { "Auto", "English", "繁體中文", "简体中文" };

        private static void CycleLanguage()
        {
            int index = System.Array.IndexOf(LanguageCodes, ModSettings.LanguageOverride.value);
            int nextIndex = (Mathf.Max(index, 0) + 1) % LanguageCodes.Length;
            ModSettings.LanguageOverride.value = LanguageCodes[nextIndex];
        }

        private static void RefreshLanguageButtonText(UIButton button)
        {
            int index = System.Array.IndexOf(LanguageCodes, ModSettings.LanguageOverride.value);
            button.text = LanguageLabels[Mathf.Max(index, 0)];
        }

        private static void StyleTab(UIButton tab)
        {
            tab.width = 150f;
            tab.height = 34f;
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

        private static void BuildTogglesPage(UIPanel page)
        {
            page.autoLayout = true;
            page.autoLayoutDirection = LayoutDirection.Vertical;
            page.autoLayoutPadding = new RectOffset(0, 0, 0, 6);
            page.padding = new RectOffset(10, 10, 10, 10);

            AddToggleRow(page, "category.emergency", ModSettings.EmergencyVehiclesEnabled);
            AddToggleRow(page, "category.metro", ModSettings.TrainsAndMetroEnabled);
            AddToggleRow(page, "category.intercityTrain", ModSettings.IntercityTrainEnabled);
            AddToggleRow(page, "category.aircraft", ModSettings.AircraftEnabled);
            AddToggleRow(page, "category.buses", ModSettings.BusesAndHelicoptersEnabled);
            AddToggleRow(page, "category.intercityBus", ModSettings.IntercityBusEnabled);
            AddToggleRow(page, "category.traffic", ModSettings.OrdinaryTrafficEnabled);
            AddToggleRow(page, "category.citizens", ModSettings.CitizensEnabled);
            AddToggleRow(page, "category.racecars", ModSettings.RaceCarsEnabled);
        }

        // "現在的設定就只有開啟和關閉" (2026-08-15): the three tunables players have actually
        // asked about/complained about in this project's history (see ModSettings.cs's notes),
        // exposed as sliders on their own tab instead of the fixed constants they used to be.
        private static void BuildTuningPage(UIPanel page)
        {
            page.autoLayout = true;
            page.autoLayoutDirection = LayoutDirection.Vertical;
            page.autoLayoutPadding = new RectOffset(0, 0, 0, 10);
            page.padding = new RectOffset(10, 10, 14, 10);

            AddSliderRow(
                page, Localization.Get("tune.raceCarSpeed"), 40f, 160f, 5f,
                () => ModSettings.RaceCarMaxSpeed.value,
                v => ModSettings.RaceCarMaxSpeed.value = v,
                "{0:0}");

            AddSliderRow(
                page, Localization.Get("tune.fireResponders"), 5f, 50f, 1f,
                () => ModSettings.FireMaxRespondersPerBuilding.value,
                v => ModSettings.FireMaxRespondersPerBuilding.value = Mathf.RoundToInt(v),
                "{0:0}");

            AddSliderRow(
                page, Localization.Get("tune.lowRidership"), 0f, 200f, 5f,
                () => ModSettings.IntercityLowRidershipThreshold.value,
                v => ModSettings.IntercityLowRidershipThreshold.value = Mathf.RoundToInt(v),
                "{0:0}");
        }

        private static void AddSliderRow(
            UIComponent parent, string label, float min, float max, float step,
            System.Func<float> getValue, System.Action<float> setValue, string valueFormat)
        {
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width = parent.width - 20f;
            row.height = 52f;

            UILabel titleLabel = row.AddUIComponent<UILabel>();
            titleLabel.text = label;
            titleLabel.textScale = 0.9f;
            titleLabel.relativePosition = new Vector3(4f, 0f);

            UILabel valueLabel = row.AddUIComponent<UILabel>();
            valueLabel.textScale = 0.85f;
            valueLabel.textColor = new Color32(190, 195, 205, 255);
            valueLabel.text = string.Format(valueFormat, getValue());
            valueLabel.relativePosition = new Vector3(row.width - valueLabel.width - 4f, 0f);

            UISlider slider = row.AddUIComponent<UISlider>();
            slider.width = row.width - 8f;
            slider.height = 18f;
            slider.relativePosition = new Vector3(4f, 26f);
            slider.minValue = min;
            slider.maxValue = max;
            slider.stepSize = step;
            slider.atlas = SolidColorSprite.Atlas;
            slider.backgroundSprite = SolidColorSprite.SpriteName;
            slider.color = new Color32(60, 64, 72, 255);

            UIPanel thumb = slider.AddUIComponent<UIPanel>();
            thumb.atlas = SolidColorSprite.Atlas;
            thumb.backgroundSprite = SolidColorSprite.SpriteName;
            thumb.color = AccentColor;
            thumb.width = 14f;
            thumb.height = 18f;
            slider.thumbObject = thumb;

            slider.value = getValue();

            slider.eventValueChanged += (component, val) =>
            {
                setValue(val);
                valueLabel.text = string.Format(valueFormat, val);
            };
        }

        // Row shows just the category name plus a pill switch - matching Advanced Stop Selection's
        // "通知消息" toggle rows in the reference screenshot, which don't show inline descriptions
        // either. The description text still exists (Localization's ".desc" keys) - it's on the
        // row's tooltip instead of always-on wrapped text, so hovering still explains what the
        // toggle affects without risking wrapped text overflowing a fixed-height row we can't
        // live-test the wrapping of.
        private static void AddToggleRow(UIComponent parent, string categoryKey, SavedBool setting)
        {
            UIPanel row = parent.AddUIComponent<UIPanel>();
            row.width = parent.width - 20f;
            row.height = 34f;
            row.tooltip = Localization.Get(categoryKey + ".desc");

            UILabel label = row.AddUIComponent<UILabel>();
            label.text = Localization.Get(categoryKey + ".title");
            label.textScale = 0.9f;
            label.relativePosition = new Vector3(4f, 8f);

            AddPillToggle(row, row.width - 54f, 6f, setting);
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

        private static void BuildAboutPage(UIPanel page)
        {
            page.autoLayout = true;
            page.autoLayoutDirection = LayoutDirection.Vertical;
            page.autoLayoutPadding = new RectOffset(0, 0, 0, 10);
            page.padding = new RectOffset(10, 10, 14, 10);

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
            button.height = 34f;
            button.textScale = 0.85f;
            StyleAccentButton(button);
            button.eventClick += (component, param) => Application.OpenURL(url);
        }

        private static string GetVersionString()
        {
            AssemblyName name = Assembly.GetExecutingAssembly().GetName();
            return name.Version.Major + "." + name.Version.Minor + "." + name.Version.Build;
        }

        // Same page this project shipped before the "完全還原" redesign - kept as a safety net for
        // the (unlikely) case GetRoot/tab construction doesn't work in-game, so a failed redesign
        // degrades to a known-working page instead of an empty one.
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
