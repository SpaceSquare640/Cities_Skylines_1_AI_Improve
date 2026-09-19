using ColossalFramework;
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

        // HEARTBEAT (added 2026-09-19, diagnostic table corrected the same day).
        //
        // On 2026-09-19 the log showed no DEV: lines at all, and nothing in it could distinguish
        // "the user never pressed the keys" from "the keys were pressed and something ate them".
        // It turned out to be the former, but only because the user could be asked - a log from a
        // week ago cannot answer that question.
        //
        // HOW TO READ IT. The first row is the one the original version of this table got wrong:
        //
        //   no heartbeat line at all   -> OnUpdate is not being called. The counters cannot report
        //                                 this, because printing them requires OnUpdate to run;
        //                                 silence IS the signal, and it is the loudest one here.
        //   window updates > 0,
        //     window Ctrl+Shift = 0    -> we run, but the combo never arrived in that window -
        //                                 another mod in the pack is almost certainly consuming it
        //   window Ctrl+Shift > 0,
        //     window R/T = 0           -> the combo arrives but the letter does not; try other keys
        //   all non-zero, no DEV: line -> the edge latch or the action itself is broken, look there
        //
        // WINDOW vs TOTAL, and why both. Counters reset after every heartbeat, so "window" means
        // the last HeartbeatIntervalSeconds only. The first version reported cumulative totals
        // alone, which quietly could not answer the question the message asks: once you press
        // Ctrl+Shift even once, a cumulative count stays non-zero forever, so a press at minute 40
        // of a long session is indistinguishable from one at minute 2. The window figure puts the
        // spike in the interval you actually pressed. Totals are kept alongside because "did this
        // ever arrive, at all" is still worth one glance.
        //
        // Counted on the raw keys without the modifier, deliberately: a mod that swallows
        // Ctrl+Shift+R may still leave plain R visible, and that difference is the diagnosis.
        private const float HeartbeatIntervalSeconds = 120f;

        private float sinceHeartbeat;

        // Reset after each heartbeat.
        private int windowUpdates;
        private int windowModifier;
        private int windowR;
        private int windowT;

        // Never reset.
        private int totalUpdates;
        private int totalModifier;
        private int totalR;
        private int totalT;

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

            // CORRECTED 2026-09-19. This used to say the latch was needed "because OnUpdate is
            // called from the simulation thread on some frames" - the same wrong premise that
            // produced two retracted findings on 2026-09-18, left behind in this file while the
            // comment 30 lines below already stated the verified model. Two contradicting claims
            // in one file is worse than either alone, because whichever one you read first looks
            // authoritative. OnUpdate runs on the MAIN thread, once per frame.
            //
            // The hand-rolled latch stays, for a reason that survives the correction: ICities does
            // not contractually pin OnUpdate to exactly one call per Unity input frame, and
            // GetKeyDown is only true on the frame the key goes down. Latching on our own bool
            // makes one physical press do one thing whatever the call cadence turns out to be.
            // Cheap insurance for a dev tool; not a claim about threads.
            bool r = Input.GetKey(KeyCode.R);
            bool t = Input.GetKey(KeyCode.T);

            HandleEdge(modifier && r, ref resetHeld, ForceTrackerReset);
            HandleEdge(modifier && t, ref stormHeld, ToggleThunderstormOverride);

            windowUpdates++;
            totalUpdates++;

            if (modifier)
            {
                windowModifier++;
                totalModifier++;
            }

            if (r)
            {
                windowR++;
                totalR++;
            }

            if (t)
            {
                windowT++;
                totalT++;
            }

            // Real time, not simulation time: simulationTimeDelta is 0 while the game is paused,
            // which would freeze the heartbeat exactly when someone is most likely to be reaching
            // for a hotkey. Whether OnUpdate is called AT ALL while paused is expected but not
            // verified - and pleasingly, this feature answers that itself: pause for two minutes
            // and see whether a heartbeat still appears. Write the answer down when you know it
            // rather than leaving this comment to be inherited as fact (see 2026-09-18).
            sinceHeartbeat += realTimeDelta;
            if (sinceHeartbeat < HeartbeatIntervalSeconds)
            {
                return;
            }

            sinceHeartbeat = 0f;
            Debug.Log(
                "[AIImprove] DEV heartbeat - this window: " + windowUpdates + " updates, Ctrl+Shift on " +
                windowModifier + " frame(s), R on " + windowR + ", T on " + windowT +
                " | totals: " + totalUpdates + " / " + totalModifier + " / " + totalR + " / " + totalT +
                ". Pressed a hotkey and saw no DEV: line? The WINDOW figures for the interval you " +
                "pressed in say where it was lost. No heartbeat line at all means OnUpdate is not running.");

            windowUpdates = 0;
            windowModifier = 0;
            windowR = 0;
            windowT = 0;
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
            //
            // MARSHALLED, NOT CALLED DIRECTLY (fixed 2026-09-18 after review). This is a genuine
            // cross-thread hazard, and worth being precise about, because a sibling claim made the
            // same day turned out to be wrong.
            //
            // IThreadingExtension.OnUpdate runs on the MAIN thread - that is why ICities offers
            // OnBeforeSimulationTick/OnAfterSimulationTick separately, and why
            // SimulationManager.AddAction exists at all. The simulation thread keeps stepping
            // vehicle AI while OnUpdate runs. ResetAll() Clear()s 23 collections that the
            // simulation thread reads and writes from our patches, so calling it straight from
            // here meant a fire truck's SetTarget prefix could be mid-lookup in a dictionary this
            // thread was clearing. The tool built to verify a fix would have been corrupting the
            // state it was verifying.
            //
            // Note what is NOT the reason: there is only one simulation thread, so vehicle AI
            // never races itself. Main thread against simulation thread is the real axis, and it
            // is the only one. See the threading note in FireResponseCapPatch.cs.
            //
            // AddAction queues the delegate onto the simulation thread, which runs it between
            // simulation steps - the same kind of quiet moment the real call sites get.
            Debug.Log("[AIImprove] DEV: queueing TrackerReset.ResetAll() onto the simulation thread.");
            Singleton<SimulationManager>.instance.AddAction(() =>
            {
                TrackerReset.ResetAll();
                Debug.Log("[AIImprove] DEV: TrackerReset.ResetAll() returned without throwing.");
            });
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
