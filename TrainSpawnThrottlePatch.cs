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
        private const uint LowRidershipThreshold = 50;
        private const float LowRidershipSkipChance = 0.5f;

        private static bool loggedFirstCall;
        private static bool loggedRidership;

        // Prefix on OutsideConnectionAI.StartTransfer(ushort, ref Building, TransferReason,
        // TransferOffer) - single `ref Building` param, safe shape. Only intervenes for
        // DummyTrain (incoming intercity train spawns); every other transfer reason (goods,
        // tourists, planes, ships, ...) passes through untouched.
        public static bool Prefix(ushort buildingID, TransferManager.TransferReason material, TransferManager.TransferOffer offer)
        {
            if (material != TransferManager.TransferReason.DummyTrain)
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

            if (ridership < LowRidershipThreshold &&
                Singleton<SimulationManager>.instance.m_randomizer.Int32(100U) < (uint)(LowRidershipSkipChance * 100f))
            {
                Debug.Log(
                    "[AIImprove] Skipped spawning an incoming intercity train - city-wide train " +
                    "ridership (" + ridership + ") is low, fewer trains are needed to serve real " +
                    "demand now that each one already carries a realistic pre-loaded passenger count.");
                return false;
            }

            ushort destinationStation = offer.Building;
            if (destinationStation == 0 || !TrainPlatformAssignmentPatch.IsStationLikelySaturated(destinationStation))
            {
                return true;
            }

            Debug.Log(
                "[AIImprove] Skipped spawning an incoming intercity train toward building " +
                destinationStation + " - station was saturated as of its last observed platform " +
                "search. Offer left unfulfilled this tick for TransferManager to retry.");
            return false;
        }
    }
}
