using System.Collections.Generic;

namespace AIImprove
{
    // "城際巴士...班次過多／過少" (2026-09-06). Intercity TRAINS were throttleable because their
    // spawn has an unmistakable signature: OutsideConnectionAI.StartTransfer with
    // TransferReason.DummyTrain. Intercity buses have no such reason - the game assembly defines
    // only DummyCar, DummyPlane, DummyShip and DummyTrain (checked directly against
    // Assembly-CSharp, 2026-09-06), so an intercity bus arriving from outside the city must be
    // coming through one of those, almost certainly DummyCar, which is also the reason for every
    // ordinary private car and truck entering the city.
    //
    // That distinction decides the whole design: a throttle keyed on DummyCar alone would refuse
    // ordinary outside traffic as well, which is emphatically not what was asked for. So this
    // does not guess. It records which (reason, destination building AI) pairs actually flow
    // through StartTransfer, one line per distinct pair, and one play session then says exactly
    // how the player's intercity buses are spawned - a bus station showing up as the destination
    // of a DummyCar offer is the confirmation needed to scope a throttle safely.
    //
    // This also closes open question A4: the existing "is executing" health check for
    // TrainSpawnThrottlePatch sits AFTER the DummyTrain filter, so "no intercity trains were
    // spawned this session" and "the patch never ran" looked identical in the log. This line runs
    // before any filter, so the patch's own reachability is no longer in question either.
    //
    // Info, not Verbose, and naturally self-limiting - there are only a handful of distinct pairs
    // in a city, so this is a few lines per session, not a stream. See 12 - 開發準則, 準則 10.
    //
    // Remove once the intercity bus spawn path is known and scoped.
    internal static class OutsideConnectionSpawnDiagnostics
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        public static void ResetForNewLevel()
        {
            Reported.Clear();
        }

        public static void Record(TransferManager.TransferReason material, ushort destinationBuilding)
        {
            // RELEASE GATE (2026-09-09): investigation diagnostics run at Info so they cannot be
            // missing from the log that matters (12 - 開發準則, 準則 10.3) - but that rule was
            // written for OUR test sessions, where we control the setting. Shipping it to players
            // meant roughly 16,000 lines a session of numbers only we can act on. The periodic
            // reports are behind verbose from here on; the one-off "is executing" and inventory
            // lines stay at Info, because those are what makes a player's bug report usable.
            if (!Log.VerboseEnabled)
            {
                return;
            }

            string destinationAi = "none";
            if (destinationBuilding != 0)
            {
                BuildingInfo info = ColossalFramework.Singleton<BuildingManager>.instance
                    .m_buildings.m_buffer[destinationBuilding].Info;
                if (info != null && info.m_buildingAI != null)
                {
                    destinationAi = info.m_buildingAI.GetType().Name;
                }
            }

            string key = material + "->" + destinationAi;
            if (!Reported.Add(key))
            {
                return;
            }

            Log.Info(
                "[AIImprove] Outside connection spawn observed: reason " + material +
                ", destination building AI " + destinationAi + ".");
        }
    }
}
