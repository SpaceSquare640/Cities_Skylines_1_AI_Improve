using ICities;
using UnityEngine;

namespace AIImprove.Dev
{
    // DEVELOPMENT ONLY. NOT COMPILED INTO THE RELEASED DLL.
    //
    // Excluded by default in AIImprove.csproj: the SDK's default `**/*.cs` glob has `dev/**/*.cs`
    // removed from it, and a second ItemGroup adds it back only when DevTools=true. So
    // `dotnet build` (what we always run, including the build whose output goes to the Workshop)
    // does not contain a single byte of this file, and `dotnet build -p:DevTools=true` prints a
    // build warning saying so.
    //
    // WHY THIS EXISTS (12 - 開發準則, 準則 3). Two fixes went unverified for weeks - the
    // thunderstorm refusal since 2026-09-05 (5 sessions missed) and TrackerReset since 2026-09-06
    // (4 missed). Neither happens on its own: one needs a disaster, the other needs loading a
    // second save mid-session. The response so far was to write "remember to test this" on a list,
    // which is a disciplinary fix, and it has now failed five times running. This turns both into
    // a keypress.
    //
    // WHY IT HOOKS IN THROUGH IThreadingExtension AND NOT OUR OWN CODE. The game scans the mod
    // assembly for IThreadingExtension implementations and instantiates them itself, so nothing in
    // the main build refers to this class - not AIImproveMod, not IngameUI, not the settings page.
    // That is what makes the exclusion above airtight rather than merely conventional: with the
    // file out of the compile, the rest of the mod still builds unchanged because it never named
    // this type in the first place. Wiring it into SettingsPageUI instead would have put a
    // reference to dev-only code in a file that always ships.
    //
    // No localization keys here on purpose - this is a developer tool, and adding dev-only keys to
    // Localization.cs would put strings players can never see into the shipped table, where
    // tools/check_localization.py would then have to be taught to ignore them.
    public sealed class DevTriggerExtension : ThreadingExtensionBase
    {
        private bool resetHeld;
        private bool stormHeld;

        public override void OnCreated(IThreading threading)
        {
            base.OnCreated(threading);

            // Deliberately Debug.Log and not Log.Verbose: this must be visible in a log even when
            // verbose logging is off, so that a log from a build that accidentally carries this
            // file is identifiable after the fact rather than only at build time.
            Debug.Log(
                "[AIImprove] *** DEVELOPMENT BUILD *** Dev trigger panel is active. This build was " +
                "produced with -p:DevTools=true and must not be uploaded to the Workshop. " +
                "Ctrl+Shift+R = force TrackerReset.ResetAll(). Ctrl+Shift+T = toggle the " +
                "thunderstorm override.");
        }

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            bool modifier = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

            // Edge-triggered by hand rather than with GetKeyDown, because OnUpdate is called from
            // the simulation thread on some frames and Unity's per-frame input state can be read
            // more than once for a single physical press. Latching on our own bool makes one press
            // do one thing regardless.
            HandleEdge(modifier && Input.GetKey(KeyCode.R), ref resetHeld, ForceTrackerReset);
            HandleEdge(modifier && Input.GetKey(KeyCode.T), ref stormHeld, ToggleThunderstormOverride);
        }

        private static void HandleEdge(bool pressed, ref bool held, System.Action action)
        {
            if (pressed && !held)
            {
                action();
            }

            held = pressed;
        }

        private static void ForceTrackerReset()
        {
            // WHAT THIS PROVES, AND WHAT IT DOES NOT. It proves all 23 ResetForNewLevel() calls in
            // TrackerReset.ResetAll() run without throwing - which is the part that has never once
            // been exercised. It does NOT prove that ResetAll is actually *reached* when a city is
            // unloaded; that is a question about the registration site in AIImproveMod, and
            // answering it still needs a real second save load. Half the verification, not all of
            // it - recorded that way in 14 - 現況總表 too, so this does not get filed as "done".
            Debug.Log("[AIImprove] DEV: forcing TrackerReset.ResetAll() by hotkey.");
            TrackerReset.ResetAll();
            Debug.Log("[AIImprove] DEV: TrackerReset.ResetAll() returned without throwing.");
        }

        private static void ToggleThunderstormOverride()
        {
            bool next = !WeatherDisasterDetector.ForceThunderstormActive;
            WeatherDisasterDetector.ForceThunderstormActive = next;

            // Same honesty as above: flipping this exercises the three consumers - aircraft gate
            // refusal, helicopter grounding, facility shutdown. It does NOT exercise
            // WeatherDisasterDetector's own read of DisasterManager, which is short-circuited by
            // the very flag being set. If the bug is in the detector, this hides it.
            Debug.Log(
                "[AIImprove] DEV: thunderstorm override is now " + (next ? "ON" : "OFF") +
                ". Consumers (aircraft refusal / helicopter halt / facility shutdown) will behave " +
                "as if a storm is active. The DisasterManager scan itself is bypassed and remains " +
                "unverified.");
        }
    }
}
