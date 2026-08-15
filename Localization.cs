using System.Collections.Generic;
using ColossalFramework.Globalization;

namespace AIImprove
{
    // "我想我們的介面，支援多國語言" (2026-08-15): both UI surfaces (AIImproveMod.OnSettingsUI's
    // Content Manager page and IngameUI's UnifiedUI panel) pull every label through here instead
    // of hardcoding text, so both stay in sync automatically. Picks a translation table based on
    // ColossalFramework.Globalization.LocaleManager.instance.language (the same locale code the
    // game's own UI uses, e.g. "en", "zh-tw", "zh-cn") and falls back to English for any language
    // or key this project hasn't translated yet - never throws, never shows a blank label.
    internal static class Localization
    {
        private const string DefaultLanguage = "en";

        private static readonly Dictionary<string, Dictionary<string, string>> Strings =
            new Dictionary<string, Dictionary<string, string>>
            {
                [DefaultLanguage] = new Dictionary<string, string>
                {
                    ["panel.title"] = "AI_Improve - Feature Toggles",
                    ["category.emergency.title"] = "Emergency vehicles",
                    ["category.emergency.desc"] =
                        "Response caps, idle vehicles seeking nearby fires. Some dispatch logic uses " +
                        "an IL transpiler and is unaffected by this toggle.",
                    ["category.metro.title"] = "Metro",
                    ["category.metro.desc"] = "Platform assignment, mid-route rerouting.",
                    ["category.intercityTrain.title"] = "Intercity trains",
                    ["category.intercityTrain.desc"] =
                        "Platform assignment, mid-route rerouting, inbound spawn throttling.",
                    ["category.aircraft.title"] = "Aircraft & airports",
                    ["category.aircraft.desc"] =
                        "Gate assignment, mid-route rerouting, thunderstorm landing refusal.",
                    ["category.buses.title"] = "Local buses & passenger helicopters",
                    ["category.buses.desc"] = "Mid-route rerouting, boarding point assignment, capacity.",
                    ["category.intercityBus.title"] = "Intercity buses",
                    ["category.intercityBus.desc"] = "Mid-route rerouting threshold adjustment.",
                    ["category.traffic.title"] = "Ordinary city traffic",
                    ["category.traffic.desc"] = "Dynamic rerouting for private cars/taxis/trucks.",
                    ["category.citizens.title"] = "Citizens",
                    ["category.citizens.desc"] = "Adjusts driving/taxi probability based on congestion.",
                    ["category.racecars.title"] = "Race cars",
                    ["category.racecars.desc"] = "Speed cap, racetrack attractiveness.",
                    ["toggle.enable"] = "Enable",
                    ["toggle.on"] = "[On] ",
                    ["toggle.off"] = "[Off] ",
                    ["button.scanTrain"] = "Scan for empty intercity trains",
                    ["button.scanBus"] = "Scan for empty intercity buses",
                    ["category.intercityTrain.short"] = "intercity trains",
                    ["category.intercityBus.short"] = "intercity buses",
                    ["scan.noneFound"] = "No empty {0} found.",
                    ["scan.confirm"] =
                        "Found {0} empty {1} chain(s) ({2} vehicle instance(s) total, including trailers).\n\n" +
                        "Delete these vehicles now?",
                    ["uui.tooltip"] = "AI_Improve Settings",
                    ["header.status"] = "Active",
                    ["header.changelog"] = "Changelog",
                    ["tab.toggles"] = "Toggles",
                    ["tab.tuning"] = "Tuning",
                    ["tab.about"] = "About",
                    ["about.version"] = "Version {0}",
                    ["about.github"] = "GitHub Source",
                    ["about.wiki"] = "Documentation Wiki",
                    ["about.workshop"] = "Steam Workshop Page",
                    ["about.reportIssue"] = "Report an Issue (GitHub)",
                    ["tune.raceCarSpeed"] = "Race car max speed",
                    ["tune.fireResponders"] = "Max fire responders per building",
                    ["tune.lowRidership"] = "Intercity train low-ridership skip threshold",
                    ["tune.verboseLogging"] = "Verbose logging (only for bug reports - slows the game)",
                },
                ["zh-tw"] = new Dictionary<string, string>
                {
                    ["panel.title"] = "AI_Improve - 功能分類開關",
                    ["category.emergency.title"] = "緊急車輛 (Emergency vehicles)",
                    ["category.emergency.desc"] = "出勤上限、閒置車輛找附近火場。部分派遣邏輯用 IL 轉譯器實作，此開關對那部分無效",
                    ["category.metro.title"] = "地鐵 (Metro)",
                    ["category.metro.desc"] = "月台分配、中途改道",
                    ["category.intercityTrain.title"] = "城際火車 (Intercity trains)",
                    ["category.intercityTrain.desc"] = "月台分配、中途改道、入城流量節流",
                    ["category.aircraft.title"] = "飛機與機場 (Aircraft & airports)",
                    ["category.aircraft.desc"] = "登機門分配、中途改道、雷暴雨拒絕起降",
                    ["category.buses.title"] = "市內巴士與客運直升機 (Local buses & passenger helicopters)",
                    ["category.buses.desc"] = "中途改道、登機點分配、載客量",
                    ["category.intercityBus.title"] = "城際巴士 (Intercity buses)",
                    ["category.intercityBus.desc"] = "中途改道閾值調整",
                    ["category.traffic.title"] = "一般市內交通 (Ordinary city traffic)",
                    ["category.traffic.desc"] = "私家車/計程車/貨車動態改道",
                    ["category.citizens.title"] = "市民行為 (Citizens)",
                    ["category.citizens.desc"] = "依壅塞調整開車/計程車機率",
                    ["category.racecars.title"] = "賽車 (Race cars)",
                    ["category.racecars.desc"] = "速度上限、賽車場吸引力",
                    ["toggle.enable"] = "啟用此功能 (Enable)",
                    ["toggle.on"] = "[開] ",
                    ["toggle.off"] = "[關] ",
                    ["button.scanTrain"] = "檢測沒有乘客的城際火車",
                    ["button.scanBus"] = "檢測沒有乘客的城際巴士",
                    ["category.intercityTrain.short"] = "城際火車 (intercity trains)",
                    ["category.intercityBus.short"] = "城際巴士 (intercity buses)",
                    ["scan.noneFound"] = "沒有偵測到沒有乘客的{0}。",
                    ["scan.confirm"] = "偵測到 {0} 輛沒有乘客的{1}（共 {2} 節車廂/車輛實例）。\n\n是否要直接刪除這些車輛？",
                    ["uui.tooltip"] = "AI_Improve 設定 (Settings)",
                    ["header.status"] = "運作中 (Active)",
                    ["header.changelog"] = "更新日誌 (Changelog)",
                    ["tab.toggles"] = "功能開關",
                    ["tab.tuning"] = "數值調整",
                    ["tab.about"] = "關於",
                    ["about.version"] = "版本 (Version) {0}",
                    ["about.github"] = "GitHub 原始碼",
                    ["about.wiki"] = "說明 Wiki",
                    ["about.workshop"] = "Steam Workshop 頁面",
                    ["about.reportIssue"] = "回報問題 (GitHub Issues)",
                    ["tune.raceCarSpeed"] = "賽車速度上限",
                    ["tune.fireResponders"] = "消防車/直升機每棟建築派遣上限",
                    ["tune.lowRidership"] = "城際火車低載客量跳過閾值",
                    ["tune.verboseLogging"] = "詳細記錄 (只在回報問題時開啟，會拖慢遊戲)",
                },
                ["zh-cn"] = new Dictionary<string, string>
                {
                    ["panel.title"] = "AI_Improve - 功能分类开关",
                    ["category.emergency.title"] = "紧急车辆 (Emergency vehicles)",
                    ["category.emergency.desc"] = "出勤上限、闲置车辆寻找附近火场。部分派遣逻辑用 IL 转译器实现，此开关对那部分无效",
                    ["category.metro.title"] = "地铁 (Metro)",
                    ["category.metro.desc"] = "月台分配、中途改道",
                    ["category.intercityTrain.title"] = "城际火车 (Intercity trains)",
                    ["category.intercityTrain.desc"] = "月台分配、中途改道、入城流量节流",
                    ["category.aircraft.title"] = "飞机与机场 (Aircraft & airports)",
                    ["category.aircraft.desc"] = "登机门分配、中途改道、雷暴雨拒绝起降",
                    ["category.buses.title"] = "市内巴士与客运直升机 (Local buses & passenger helicopters)",
                    ["category.buses.desc"] = "中途改道、登机点分配、载客量",
                    ["category.intercityBus.title"] = "城际巴士 (Intercity buses)",
                    ["category.intercityBus.desc"] = "中途改道阈值调整",
                    ["category.traffic.title"] = "一般市内交通 (Ordinary city traffic)",
                    ["category.traffic.desc"] = "私家车/出租车/货车动态改道",
                    ["category.citizens.title"] = "市民行为 (Citizens)",
                    ["category.citizens.desc"] = "依拥堵调整开车/出租车概率",
                    ["category.racecars.title"] = "赛车 (Race cars)",
                    ["category.racecars.desc"] = "速度上限、赛车场吸引力",
                    ["toggle.enable"] = "启用此功能 (Enable)",
                    ["toggle.on"] = "[开] ",
                    ["toggle.off"] = "[关] ",
                    ["button.scanTrain"] = "检测没有乘客的城际火车",
                    ["button.scanBus"] = "检测没有乘客的城际巴士",
                    ["category.intercityTrain.short"] = "城际火车 (intercity trains)",
                    ["category.intercityBus.short"] = "城际巴士 (intercity buses)",
                    ["scan.noneFound"] = "没有侦测到没有乘客的{0}。",
                    ["scan.confirm"] = "侦测到 {0} 辆没有乘客的{1}（共 {2} 节车厢/车辆实例）。\n\n是否要直接删除这些车辆？",
                    ["uui.tooltip"] = "AI_Improve 设定 (Settings)",
                    ["header.status"] = "运作中 (Active)",
                    ["header.changelog"] = "更新日志 (Changelog)",
                    ["tab.toggles"] = "功能开关",
                    ["tab.tuning"] = "数值调整",
                    ["tab.about"] = "关于",
                    ["about.version"] = "版本 (Version) {0}",
                    ["about.github"] = "GitHub 源代码",
                    ["about.wiki"] = "说明 Wiki",
                    ["about.workshop"] = "Steam Workshop 页面",
                    ["about.reportIssue"] = "报告问题 (GitHub Issues)",
                    ["tune.raceCarSpeed"] = "赛车速度上限",
                    ["tune.fireResponders"] = "消防车/直升机每栋建筑派遣上限",
                    ["tune.lowRidership"] = "城际火车低载客量跳过阈值",
                    ["tune.verboseLogging"] = "详细记录 (只在报告问题时开启，会拖慢游戏)",
                },
            };

        public static string Get(string key)
        {
            string language = ResolveLanguage();

            if (Strings.TryGetValue(language, out Dictionary<string, string> table) &&
                table.TryGetValue(key, out string value))
            {
                return value;
            }

            return Strings[DefaultLanguage].TryGetValue(key, out string fallback) ? fallback : key;
        }

        public static string Get(string key, params object[] args) => string.Format(Get(key), args);

        // Every language code Localization actually has a translation table for - drives both
        // ResolveLanguage's override check and the language-cycle button in SettingsPageUI.cs, so
        // the two can't drift out of sync with each other.
        public static readonly string[] SupportedLanguages = { "en", "zh-tw", "zh-cn" };

        private static string ResolveLanguage()
        {
            string overrideLanguage = ModSettings.LanguageOverride.value;
            if (!string.IsNullOrEmpty(overrideLanguage) && overrideLanguage != "auto")
            {
                return overrideLanguage;
            }

            if (!LocaleManager.exists)
            {
                return DefaultLanguage;
            }

            string language = LocaleManager.instance.language;
            return string.IsNullOrEmpty(language) ? DefaultLanguage : language.ToLowerInvariant();
        }
    }
}
