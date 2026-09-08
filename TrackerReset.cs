namespace AIImprove
{
    // The single list of everything this mod remembers about one particular city.
    //
    // BUG THIS EXISTS FOR (audit, 2026-09-06): every tracker below is keyed by a building,
    // vehicle or node ID, and Cities: Skylines recycles those IDs out of fixed-size pools that
    // are refilled from scratch when a save is loaded. Nothing cleared them between saves, so
    // loading a second city in the same session started with the first city's occupancy counts,
    // burning-building set, saturated-station flags and in-flight reroute states still in place -
    // all attached to IDs that now mean something completely different. The visible symptoms are
    // the same self-reinforcing kind as the airport occupancy leak: an airport that is "full"
    // with no planes, a station that refuses spawns forever, fire trucks capped against fires
    // that ended in another city.
    //
    // WHY IT IS A LIST AND NOT A PER-CLASS HABIT (12 - 開發準則, 準則 3): the previous approach
    // was "remember to clean up", and that is exactly what failed. Adding a new tracker now means
    // adding one line here; forgetting is visible in one place instead of invisible in twenty.
    internal static class TrackerReset
    {
        public static void ResetAll()
        {
            AircraftGateAssignmentPatch.ResetForNewLevel();
            AirTrafficControlManager.ResetForNewLevel();
            DensityDistributionDiagnostics.ResetForNewLevel();
            EmergencyDispatchTracker.ResetForNewLevel();
            EmergencyIgnoreCostsPatch.ResetForNewLevel();
            FireResponseCapPatch.ResetForNewLevel();
            FireResponseTracker.ResetForNewLevel();
            FlexibleReroutePatch.ResetForNewLevel();
            HoldingPatternManager.ResetForNewLevel();
            HoldingPatternPatch.ResetForNewLevel();
            OutsideConnectionSpawnDiagnostics.ResetForNewLevel();
            VehicleSpawnPathDiagnostics.ResetForNewLevel();
            PassengerHelicopterGateAssignmentPatch.ResetForNewLevel();
            RerouteEffectDiagnostics.ResetForNewLevel();
            RerouteFailureDiagnostics.ResetForNewLevel();
            SanitationIdleSeekTracker.ResetForNewLevel();
            ShipDockAssignmentPatch.ResetForNewLevel();
            ShipQueueDetector.ResetForNewLevel();
            StuckRerouteTracker.ResetForNewLevel();
            ThunderstormFacilityShutdownPatch.ResetForNewLevel();
            TrainPlatformAssignmentPatch.ResetForNewLevel();
            TrainSingleTrackConflictDetector.ResetForNewLevel();
            TransitStationSkipPatch.ResetForNewLevel();
            TransitStopOccupancyTracker.ResetForNewLevel();
            WeatherDisasterDetector.ResetForNewLevel();

            UnityEngine.Debug.Log("[AIImprove] Per-city tracker state cleared for level unload.");
        }
    }
}
