using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;

namespace AIImprove
{
    // "我們的模組需要依賴 UnifiedUI 才能在遊戲的地圖中打開UI" (2026-08-15): the in-game
    // toggle button (IngameUI.cs) is registered into the player's existing UnifiedUI (UUI) toolbar
    // instead of us drawing our own floating button. Reflection-only, no compile-time reference to
    // UnifiedUILib.dll - same soft-dependency pattern as CompanionModCompat.cs - so this project
    // still builds and the rest of the mod still works if UUI isn't installed; only the in-game
    // panel becomes unreachable in that case (Content Manager's own settings page still works
    // regardless).
    //
    // Signature confirmed via dnSpy against the player's actual UnifiedUILib.dll:
    // UnifiedUI.API.UUIAPI.Register(string name, string groupName, string tooltip,
    // string spritefile, Action<bool> onToggle, Action<ToolBase> onToolChanged,
    // SavedInputKey activationKey, Dictionary<SavedInputKey, Func<bool>> activeKeys) - the overload
    // that takes a plain toggle callback instead of requiring a ToolBase map-editing tool, since we
    // have no such tool, just a settings panel to show/hide.
    internal static class UnifiedUiIntegration
    {
        private const string UuiApiTypeName = "UnifiedUI.API.UUIAPI";

        public static bool TryRegister(string name, string groupName, string tooltip, Action<bool> onToggle)
        {
            try
            {
                Type uuiApiType = FindType(UuiApiTypeName);
                if (uuiApiType == null)
                {
                    return false;
                }

                Type[] paramTypes =
                {
                    typeof(string), typeof(string), typeof(string), typeof(string),
                    typeof(Action<bool>), typeof(Action<ToolBase>),
                    typeof(SavedInputKey), typeof(Dictionary<SavedInputKey, Func<bool>>)
                };

                MethodInfo register = uuiApiType.GetMethod(
                    "Register", BindingFlags.Public | BindingFlags.Static, null, paramTypes, null);
                if (register == null)
                {
                    return false;
                }

                register.Invoke(null, new object[] { name, groupName, tooltip, null, onToggle, null, null, null });
                return true;
            }
            catch
            {
                // Reflection into another mod's internals is inherently fragile across its
                // updates - fail safe (report "not registered") rather than throw.
                return false;
            }
        }

        // BUG FOUND VIA SCREENSHOT (2026-08-15): the player has several mods that each bundle
        // their own copy of UnifiedUILib.dll (different versions: 2.2.12.32690, 2.2.12.31246,
        // 2.1.13.30989, ...) - the log confirmed our button ended up registered against a stale
        // duplicate's MainPanel.Instance (the log line for that duplicate's UnifiedUILib being
        // (re)initialized appears in the same instant as our own registration), producing a
        // disconnected placeholder box instead of a slot in the real, currently-displayed toolbar.
        // Picking the highest-versioned loaded copy instead of just the first match found fixes
        // this - it's the same "latest wins" arbitration the game's own log shows other components
        // doing ("using latest UnifiedUILib version: ...") for the exact same multi-copy situation.
        private static Type FindType(string typeName)
        {
            Type best = null;
            Version bestVersion = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, throwOnError: false);
                if (type == null)
                {
                    continue;
                }

                Version version = assembly.GetName().Version;
                if (best == null || version > bestVersion)
                {
                    best = type;
                    bestVersion = version;
                }
            }

            return best;
        }
    }
}
