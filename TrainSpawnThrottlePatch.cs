using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "城市的城際火車吞吐量已經飽和，但是也照樣生成城際火車進入城市" (2026-08-13): incoming
    // intercity trains are spawned by OutsideConnectionAI.StartTransfer, matching a
    // TransferManager.TransferReason.DummyTrain offer/request pair - exactly the same mechanism
    // DummyPlane uses for aircraft (see AircraftGateAssignmentPatch's notes). Vanilla never checks
    // whether the destination station actually has room before spawning; TrainPlatformAssignmentPatch
    // only comes into play once a train already exists and starts pathfinding, by which point it's
    // already added to the pile.
    //
    // Unlike aircraft, trains don't get a visible holding pattern here - real track/signal blocking
    // already queues an over-dispatched train safely once it exists (see
    // TrainPlatformAssignmentPatch's notes on why no holding equivalent was built for them). The
    // actual complaint is upstream of that: the city keeps manufacturing MORE trains to add to a
    // queue that's already full. This patch addresses that directly by skipping the spawn itself
    // when the destination is known to be saturated, letting TransferManager simply try again on a
    // later tick (the same "offer went unfulfilled this round" path vanilla already takes whenever
    // StartConnectionTransferImpl picks no vehicleInfo for a reason it doesn't recognize - not a new
    // failure mode, an existing one).
    // REVISED (2026-08-14): "根據城市中的吞吐量動態調整...入城流量" - platform occupancy alone
    // only measures physical crowding, not whether the city actually needs another train. Now
    // also reads real city-wide train ridership (TransportThroughputTracker) and, when it's low,
    // probabilistically skips spawns even at stations whose platforms aren't yet flagged
    // saturated - each incoming train now already carries a large, realistically pre-loaded
    // passenger count (see TrainPassengerCapacityPatch), so fewer trains are genuinely needed to
    // serve the same real demand. Interim thresholds pending live-test calibration, same
    // philosophy as every other tunable in this project.
    internal static class TrainSpawnThrottlePatch
    {
        // "每個功能中的調整設定及數據可以拆開以及詳細調整" (2026-08-15): both the threshold and
        // the skip chance are sliders now, defaults unchanged (50, 50%).
        private static float LowRidershipSkipChance => ModSettings.IntercityLowRidershipSkipPercent.value / 100f;

        private static bool loggedFirstCall;
        private static bool loggedRidership;

        // Every DummyTrain offer this patch sees, and what it did with it. Info rather than
        // verbose, and one line per 25 offers: intercity train spawns are rare enough that a
        // whole session produces a handful of lines, and the player-visible symptom this exists
        // to explain is "almost none are spawning" - which is exactly the case where a
        // verbose-gated counter would be missing from the log that mattered.
        private static int offersSeen;
        private static int lowRidershipSkips;
        private static int saturationSkips;

        private static void ReportIfDue(uint ridership)
        {
            if (offersSeen % 25 != 0)
            {
                return;
            }

            Log.Info(
                "[AIImprove] Intercity train spawn throttle: " + offersSeen + " offer(s) seen, " +
                lowRidershipSkips + " refused for low ridership, " + saturationSkips +
                " refused for a saturated station, " +
                (offersSeen - saturationSkips) + " allowed (the low-ridership rule is currently " +
                "disabled in code, so its count is what it WOULD have refused). Current ridership " +
                "reading " + ridership + " against a threshold of " +
                ModSettings.IntercityLowRidershipThreshold.value + ".");
        }

        // Prefix on OutsideConnectionAI.StartTransfer(ushort, ref Building, TransferReason,
        // TransferOffer) - single `ref Building` param, safe shape. Only intervenes for
        // DummyTrain (incoming intercity train spawns); every other transfer reason (goods,
        // tourists, planes, ships, ...) passes through untouched.
        public static bool Prefix(ushort buildingID, TransferManager.TransferReason material, TransferManager.TransferOffer offer)
        {
            // DummyTrain only ever fires for trains arriving from an outside connection, which
            // per this project's own terminology are always "intercity trains" - metro never uses
            // this transfer reason - so this is gated on its own toggle, not any train/metro
            // reroute switch (2026-08-15, split per user request).
            // Before any filter, so this doubles as the reachability check the DummyTrain-gated
            // health log below could never be (open question A4), and as the measurement that
            // will tell us how intercity BUSES get spawned - see OutsideConnectionSpawnDiagnostics.
            OutsideConnectionSpawnDiagnostics.Record(material, offer.Building);

            if (material != TransferManager.TransferReason.DummyTrain || !ModSettings.IntercityTrainSpawnThrottleEnabled.value)
            {
                return true;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] TrainSpawnThrottlePatch is executing.");
            }

            uint ridership = TransportThroughputTracker.GetAverageRidership(TransportInfo.TransportType.Train);
            if (!loggedRidership)
            {
                loggedRidership = true;
                Debug.Log("[AIImprove] Current average train ridership reading: " + ridership + ".");
            }

            offersSeen++;

            // BUG REPORT (user, 2026-09-07): "現在的城際巴士及城際火車生成率可見是低到接近於零".
            //
            // The low-ridership rule is the same self-reinforcing shape as the airport occupancy
            // leak and the saturated-station deadlock, and this is the third instance:
            //
            //   low measured ridership -> refuse spawns -> fewer intercity trains -> fewer
            //   passengers riding them -> lower measured ridership -> refuse more
            //
            // The reading is city-wide train ridership. It cannot tell "nobody wants to ride" from
            // "there is nothing to ride", and this rule then acts on the second as if it were the
            // first. Same class of mistake as reading CemeteryAI.GetMaterialAmount as "needs
            // collection" - the number is real, it just answers a different question than the one
            // being asked of it (see 12 - 開發準則, 準則 2).
            //
            // The rule is switched off IN CODE, not by changing the default - a player who has
            // ever opened the panel has 50 saved in AIImprove.cgs, so a new default would not
            // reach them. Same lesson as the helicopter capacity patch on 2026-09-06.
            //
            // The counters below still report what it WOULD have refused, which is the
            // calibration data the threshold of 50 never had: it was an interim value picked in
            // 2026-08-14 without anyone ever having seen a real ridership reading. Turn this back
            // on only once a log shows the reading's actual range AND the feedback loop above is
            // broken - e.g. by requiring evidence that intercity trains exist before concluding
            // that nobody is riding them.
            const bool LowRidershipRuleEnabled = false;

            bool wouldSkipForLowRidership =
                ridership < (uint)ModSettings.IntercityLowRidershipThreshold.value &&
                Singleton<SimulationManager>.instance.m_randomizer.Int32(100U) < (uint)(LowRidershipSkipChance * 100f);

            if (wouldSkipForLowRidership)
            {
                lowRidershipSkips++;
            }

            if (LowRidershipRuleEnabled && wouldSkipForLowRidership)
            {
                ReportIfDue(ridership);
                // Verbose-gated for the same reason as the helicopter logs (2026-08-16 audit):
                // this fires per spawn attempt, not once.
                Log.Verbose(
                    "[AIImprove] Skipped spawning an incoming intercity train - city-wide train " +
                    "ridership (" + ridership + ") is low, fewer trains are needed to serve real " +
                    "demand now that each one already carries a realistic pre-loaded passenger count.");
                return false;
            }

            ushort destinationStation = offer.Building;
            if (destinationStation == 0 || !TrainPlatformAssignmentPatch.IsStationLikelySaturated(destinationStation))
            {
                ReportIfDue(ridership);
                return true;
            }

            saturationSkips++;
            ReportIfDue(ridership);

            Log.Verbose(
                "[AIImprove] Skipped spawning an incoming intercity train toward building " +
                destinationStation + " - station was saturated as of its last observed platform " +
                "search. Offer left unfulfilled this tick for TransferManager to retry.");
            return false;
        }
    }
}
