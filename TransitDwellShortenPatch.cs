using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace AIImprove
{
    // Replaces TransitStationSkipPatch, which was disabled on 2026-08-14 after a player bug
    // report: 3,676 skip events across 473 vehicles, buses flying past six consecutive stops,
    // vehicles stuck, and transit ridership collapsing. That file is kept for its analysis; this
    // is the rework it called for.
    //
    // WHY THE OLD ONE FAILED, and why this is a different shape rather than a fixed version of
    // the same shape:
    //
    //   1. It measured the wrong thing. "Nobody boarded" was read as "nobody wants this stop",
    //      but BusAI.LoadPassengers only boards citizens already flagged WaitingTransport within
    //      32m AT THE INSTANT OF ARRIVAL. Vanilla then lets the vehicle dwell so people still
    //      walking up can reach it. Judging demand at the instant of arrival therefore reports
    //      "empty" for a stop whose riders simply had not arrived yet - and skipping it stranded
    //      exactly those people, which made the next stop look empty too. Self-reinforcing, and
    //      it matches the reported ridership collapse precisely.
    //
    //   2. It advanced to the next stop by re-entering ArriveAtTarget through reflection. Each
    //      skip fired another StartPathFind in the same tick, so a chain of skips left one
    //      vehicle with several competing path requests and the Stopped flag still set. That is
    //      the stuck vehicles.
    //
    // WHAT THIS DOES INSTEAD: it never skips a stop. The vehicle always stops, always opens its
    // doors, always boards whoever is there. The only thing changed is how long it then sits
    // there with nobody coming - vanilla dwells a fixed 12 simulation steps regardless.
    //
    // Neither failure mode can occur by construction: nobody is stranded, because the stop is
    // still served; and no path request is issued, because leaving is still vanilla's own
    // decision.
    //
    // HOW: BusAI.CanLeave requires m_waitCounter >= 12 AND base.CanLeave (nobody mid-boarding)
    // AND TransportLine.CanLeaveStop. A Prefix here sets the wait counter to 12 when the stop
    // has no waiting demand, then lets the original method run and evaluate everything else
    // itself. This project does not reimplement those checks - it satisfies the timer and leaves
    // the safety conditions to the game. If the other conditions are not met the vehicle stays,
    // exactly as it would have.
    //
    // The demand signal is TransportLine.CalculatePassengerCount(stop), which scans the citizen
    // grid around the stop for WaitingTransport citizens whose journey continues toward the next
    // stop on this line. Decompiled before use (2026-09-09): it calls
    // HumanAI.TransportArriveAtSource, which reads flags, path positions and distances and writes
    // nothing - so calling it as a query has no side effects on citizens. That check was the
    // whole reason to look: the old version's mistake was measuring without knowing what the
    // measurement meant.
    internal static class TransitDwellShortenPatch
    {
        // Vanilla's own requirement, from BusAI.CanLeave.
        private const byte VanillaDwell = 12;

        // Still dwell this long before concluding nobody is coming. Deliberately not zero: the
        // point of a dwell is to catch people walking up, and the old version's error was
        // deciding at the first instant.
        private const byte MinimumDwell = 6;

        private static bool loggedFirstCall;
        private static int shortened;
        private static int keptWaiting;

        public static void ResetForNewLevel()
        {
            shortened = 0;
            keptWaiting = 0;
        }

        // Prefix on BusAI.CanLeave(ushort, ref Vehicle) - a single ref struct parameter, the
        // shape this project has repeatedly confirmed is safe under Mono's JIT. Always returns
        // true: the original method still decides.
        public static bool Prefix(ushort vehicleID, ref Vehicle vehicleData)
        {
            if (!ModSettings.TransitDwellShortenEnabled.value ||
                vehicleData.m_transportLine == 0 ||
                vehicleData.m_targetBuilding == 0)
            {
                return true;
            }

            // Evaluated once per stop visit, at exactly one wait-counter value, rather than every
            // frame of the dwell: CalculatePassengerCount walks a patch of the citizen grid, and
            // running it eight times per stop to reach the same conclusion would be waste.
            if (vehicleData.m_waitCounter != MinimumDwell)
            {
                return true;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] TransitDwellShortenPatch is executing.");
            }

            TransportManager transportManager = Singleton<TransportManager>.instance;
            int waiting = transportManager.m_lines.m_buffer[vehicleData.m_transportLine]
                .CalculatePassengerCount(vehicleData.m_targetBuilding);

            if (waiting > 0)
            {
                keptWaiting++;
                Report();
                return true;
            }

            // Satisfies the timer only. base.CanLeave (nobody still boarding or alighting) and
            // TransportLine.CanLeaveStop are evaluated by the original method immediately after
            // this returns, and either can still keep the vehicle here.
            vehicleData.m_waitCounter = VanillaDwell;
            shortened++;
            Report();
            return true;
        }

        private static void Report()
        {
            int total = shortened + keptWaiting;
            if (total % 100 != 0 || !Log.VerboseEnabled)
            {
                return;
            }

            Log.Info(
                "[AIImprove] Transit dwell: " + total + " stop visit(s) reached the " +
                MinimumDwell + "-step mark - " + shortened + " had nobody waiting and were " +
                "released early, " + keptWaiting + " had passengers still to board and dwelt in " +
                "full.");
        }
    }
}
