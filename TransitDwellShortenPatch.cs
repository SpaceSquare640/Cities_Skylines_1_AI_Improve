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
    // UNBUNCHING (added 2026-09-09). The other half of what an "express bus" mod does: when
    // vehicles on a line catch each other up, the leader takes every passenger and the follower
    // runs empty right behind it - which is why buses arrive in pairs with a long gap either side.
    //
    // The usual answer is to let the follower SKIP stops. This project has already learned what
    // that costs: the skipped stop's passengers are stranded, and the recursion needed to advance
    // a stop leaves vehicles holding competing path requests. So the same goal is reached from the
    // opposite direction - instead of sending the follower ahead, HOLD it back. The gap opens and
    // nobody is passed by.
    //
    //   nobody waiting                       -> leave early   (shortens the dwell)
    //   caught up with the vehicle in front  -> leave later    (extends the dwell)
    //
    // Both are the same one-line intervention on the same counter, in the same patch, and neither
    // can strand a passenger or issue a path request.
    //
    // WHO HOLDS: a vehicle holds only if another vehicle of the same line is stopped at the same
    // stop AND has been there at least as long. Without that second half both vehicles would hold
    // for each other and neither would leave; with it, whoever arrived first always leaves first.
    //
    // TERMINATION IS GUARANTEED, not hoped for: every hold is counted per vehicle and stops at
    // MaxHoldSteps regardless of conditions. A rule that can extend a dwell needs a bound that
    // does not depend on the situation resolving itself - a stuck bus is precisely the failure
    // this whole feature is being rewritten to avoid.
    internal static class TransitDwellShortenPatch
    {
        // Vanilla's own requirement, from BusAI.CanLeave.
        private const byte VanillaDwell = 12;

        // Still dwell this long before concluding nobody is coming. Deliberately not zero: the
        // point of a dwell is to catch people walking up, and the old version's error was
        // deciding at the first instant.
        private const byte MinimumDwell = 6;

        // Hard ceiling on how long unbunching may extend a stop, in simulation steps - roughly
        // three times vanilla's own dwell.
        private const int MaxHoldSteps = 36;

        private static bool loggedFirstCall;
        private static int shortened;
        private static int keptWaiting;
        private static int held;

        // How many steps each vehicle has been held at its current stop. Bounds the hold, and is
        // dropped as soon as the vehicle is allowed to leave.
        private static readonly System.Collections.Generic.Dictionary<ushort, int> HoldSteps =
            new System.Collections.Generic.Dictionary<ushort, int>();

        public static void ResetForNewLevel()
        {
            shortened = 0;
            keptWaiting = 0;
            held = 0;
            HoldSteps.Clear();
        }

        public static void ReleaseVehicle(ushort vehicleID)
        {
            HoldSteps.Remove(vehicleID);
        }


        // Cached because FindType walks every loaded assembly and this is asked per stop, per
        // vehicle. The answer cannot change during a session.
        private static bool? expressBusServicesPresent;

        private static bool StandDownForExpressBusServices()
        {
            if (expressBusServicesPresent == null)
            {
                expressBusServicesPresent = CompanionModCompat.IsExpressBusServicesLoaded();

                if (expressBusServicesPresent.Value)
                {
                    Debug.Log(
                        "[AIImprove] Express Bus Services is installed, so this mod's transit " +
                        "dwell features are standing down for the session - both decide when a " +
                        "vehicle leaves a stop, and two mods writing the same wait counter with " +
                        "different intentions is how transit broke here once before. Its toggles " +
                        "will have no effect until Express Bus Services is removed.");
                }
            }

            return expressBusServicesPresent.Value;
        }

        // COVERAGE (2026-09-09, checked with dnSpy rather than assumed - the four transit AIs do
        // NOT all work the same way):
        //
        //   BusAI          waitCounter >= 12, then base.CanLeave and CanLeaveStop.
        //   TrolleybusAI   character-for-character the same as BusAI.
        //   PassengerTrainAI (and MetroTrainAI, which inherits it unchanged) the same, EXCEPT
        //                  that a carriage with a leading vehicle bypasses the timer entirely -
        //                  only the lead car's counter means anything. Hence the guard below.
        //   TramAI         DOES NOT OVERRIDE CanLeave AT ALL. It falls through to
        //                  VehicleAI.CanLeave, which has no timer - only a check that nobody is
        //                  mid-boarding. Trams already leave as soon as boarding finishes, so
        //                  there is no dwell to shorten and this mechanism does not apply to them.
        //                  Holding a tram would mean making a base method return false with no
        //                  vanilla timer to lean on, which is a different and riskier design;
        //                  trams are left alone rather than covered badly.
        //
        // Prefix on <AI>.CanLeave(ushort, ref Vehicle) - a single ref struct parameter, the shape
        // this project has repeatedly confirmed is safe under Mono's JIT. Always returns true:
        // the original method still decides.
        public static bool Prefix(ushort vehicleID, ref Vehicle vehicleData)
        {
            // A carriage behind a locomotive has no say in when the train leaves - vanilla's own
            // CanLeave short-circuits the timer for it. Touching its counter would be writing to
            // a number nothing reads.
            if (vehicleData.m_leadingVehicle != 0)
            {
                return true;
            }

            if (StandDownForExpressBusServices() ||
                (!ModSettings.TransitDwellShortenEnabled.value &&
                 !ModSettings.TransitUnbunchEnabled.value) ||
                vehicleData.m_transportLine == 0 ||
                vehicleData.m_targetBuilding == 0)
            {
                HoldSteps.Remove(vehicleID);
                return true;
            }

            // Evaluated once per stop visit, at exactly one wait-counter value, rather than every
            // frame of the dwell: CalculatePassengerCount walks a patch of the citizen grid, and
            // running it eight times per stop to reach the same conclusion would be waste.
            if (ModSettings.TransitUnbunchEnabled.value &&
                HoldForVehicleInFront(vehicleID, ref vehicleData))
            {
                return true;
            }

            if (!ModSettings.TransitDwellShortenEnabled.value ||
                vehicleData.m_waitCounter != MinimumDwell)
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


        // True when this vehicle should stay a little longer to open a gap. The hold is done by
        // pushing the wait counter back down, which the original CanLeave then reads as "not yet".
        private static bool HoldForVehicleInFront(ushort vehicleID, ref Vehicle vehicleData)
        {
            if (vehicleData.m_waitCounter < VanillaDwell)
            {
                // Not ready to leave anyway - nothing to hold back.
                return false;
            }

            int alreadyHeld;
            HoldSteps.TryGetValue(vehicleID, out alreadyHeld);
            if (alreadyHeld >= MaxHoldSteps)
            {
                // The bound, applied whether or not the bunching has cleared. See the note on
                // guaranteed termination above.
                HoldSteps.Remove(vehicleID);
                return false;
            }

            if (!IsBehindAnotherVehicleHere(vehicleID, ref vehicleData))
            {
                HoldSteps.Remove(vehicleID);
                return false;
            }

            HoldSteps[vehicleID] = alreadyHeld + 1;
            vehicleData.m_waitCounter = VanillaDwell - 1;
            held++;
            Report();
            return true;
        }

        // Another vehicle of the same line, stopped at the same stop, that arrived no later than
        // we did. The "no later" half is what stops two vehicles holding for each other forever.
        private static bool IsBehindAnotherVehicleHere(ushort vehicleID, ref Vehicle vehicleData)
        {
            VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
            ushort other = Singleton<TransportManager>.instance
                .m_lines.m_buffer[vehicleData.m_transportLine].m_vehicles;

            int guard = 0;
            while (other != 0 && ++guard < 16384)
            {
                if (other != vehicleID)
                {
                    Vehicle otherData = vehicleManager.m_vehicles.m_buffer[other];
                    if (otherData.m_targetBuilding == vehicleData.m_targetBuilding &&
                        (otherData.m_flags & Vehicle.Flags.Stopped) != 0 &&
                        otherData.m_waitCounter >= vehicleData.m_waitCounter)
                    {
                        return true;
                    }
                }

                other = vehicleManager.m_vehicles.m_buffer[other].m_nextLineVehicle;
            }

            return false;
        }

        private static void Report()
        {
            int total = shortened + keptWaiting + held;
            if (total % 100 != 0 || !Log.VerboseEnabled)
            {
                return;
            }

            Log.Info(
                "[AIImprove] Transit dwell: " + total + " decision(s) - " + shortened +
                " released early with nobody waiting, " + keptWaiting +
                " dwelt in full with passengers still to board, " + held +
                " held back to open a gap behind the vehicle in front.");
        }
    }
}
