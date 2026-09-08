namespace AIImprove
{
    // One place that knows every per-vehicle tracker this mod keeps, so releasing a vehicle's
    // state is a single call rather than a list that each new call site has to remember to keep
    // in sync. Added 2026-09-05 while fixing a player-reported bug that existed precisely because
    // that list lived in only one place and a second place needed it.
    //
    // WHY THIS EXISTS (player report, Fey Warrior, 2026-09-05): "planes appear, taxi to a stand,
    // disappear, then repeat. Disabling stand selection in this mod seems to have stopped it."
    //
    // Root cause, confirmed by decompiling Vehicle.Unspawn: when AircraftGateAssignmentPatch
    // refuses a landing at a saturated airport, its Prefix returns false, so vanilla's
    // StartPathFind never runs and __result stays false. The caller
    // (PassengerPlaneAI.SimulationStep) responds with `data.Unspawn(vehicleID)` - and Unspawn
    // only releases *trailing* vehicles, removes the lead vehicle from the grid, and clears
    // Flags.Spawned. It never calls ReleaseVehicle on the vehicle itself.
    //
    // AircraftReleasePatch hangs off VehicleAI.ReleaseVehicle, so it never fired for those
    // planes, and AirTrafficControlManager's occupancy for that airport was never decremented.
    // Occupancy could then only grow: every refusal left a permanent +1, which made saturation
    // more likely, which refused more planes, each adding another +1. The airport ends up
    // permanently closed and the player sees planes appearing, taxiing and vanishing on a loop.
    //
    // THE GENERAL RULE this came from (see 12 - 開發準則, 準則 6): checking that cleanup is
    // "hooked up" is not enough. Enumerate every way an entity can disappear. The audit on the
    // same day verified every tracker was wired into VehicleAI.ReleaseVehicle and passed it -
    // and still missed this, because Unspawn is a second, entirely separate disappearance path.
    //
    // Enumeration of the paths where THIS MOD makes a vehicle disappear, as of 2026-09-05:
    //
    //   AircraftGateAssignmentPatch, saturated airport  -> Prefix false -> Unspawn. Leaks.
    //   HelicopterWeatherHaltPatch, thunderstorm         -> __result false -> Unspawn. Leaks.
    //   TrainSpawnThrottlePatch, low ridership/saturated -> refuses the offer before any vehicle
    //                                                       exists. Nothing to leak.
    //
    // Both leaking paths now call ReleaseAll before returning.
    internal static class VehicleStateCleanup
    {
        // Every tracker keyed by vehicle ID. Each one's own release is a no-op for IDs it never
        // recorded, so calling this for any vehicle of any type is safe and cheap.
        //
        // Anything added here must be safe to call more than once for the same vehicle: a refused
        // vehicle gets this call at the refusal, and again later if the game does eventually
        // release it properly.
        public static void ReleaseAll(ushort vehicleID)
        {
            AirTrafficControlManager.ReleaseVehicle(vehicleID);
            HoldingPatternManager.EndHolding(vehicleID);
            FireResponseTracker.ReleaseVehicle(vehicleID);
            StuckRerouteTracker.Clear(vehicleID);
            RerouteEffectDiagnostics.ReleaseVehicle(vehicleID);
            EmergencyDispatchTracker.ReleaseVehicle(vehicleID);
            ShipQueueDetector.ReleaseVehicle(vehicleID);
            SanitationIdleSeekTracker.ReleaseVehicle(vehicleID);
        }
    }
}
