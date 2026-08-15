using ColossalFramework;

namespace AIImprove
{
    // "我想把全部功能拆開，然後每個功能中的調整設定及數據可以拆開以及詳細調整" (2026-08-15).
    //
    // Before this, nine category toggles each bundled several unrelated behaviours - turning off
    // "Aircraft" also killed gate assignment, mid-route rerouting and the thunderstorm refusal
    // together, with no way to keep one and drop another. Every feature now has its own switch,
    // and the constants each one used to hardcode are exposed next to it.
    //
    // MIGRATION: the old category toggles are still declared below, but only as the *default* for
    // the per-feature switches that replaced them. SavedBool takes its default at construction and
    // only uses it when the key is absent from the settings file, so a player who had turned
    // "Aircraft" off keeps all three aircraft features off on first run with this version, instead
    // of silently getting them back. They are no longer shown in the UI and nothing reads them at
    // runtime. Declaration order matters here - C# runs static field initialisers top to bottom,
    // so the categories must stay above the features that reference them.
    //
    // Persistence is ColossalFramework's Saved* family, the same mechanism vanilla options and
    // most other mods use - a flat key/value file under the game's local app data folder.
    // autoUpdate=true means every `.value` read re-syncs from the file, so changes take effect
    // immediately with no restart.
    //
    // Patches stay Harmony-patched for the whole session and check their own switch at the top of
    // their entry point (early-return, same shape as the DlcDetector/CompanionModCompat checks).
    // The check must come before ANY state mutation, dictionary write or log, and bool-returning
    // Prefixes must `return true`, so that "off" is indistinguishable from the feature never
    // having been written.
    //
    // KNOWN GAP: transpiler-based patches (EmergencyIgnoreCostsPatch,
    // VanillaEmergencyCongestionPatch, TMPE's EmergencyVehiclePriorityPatch path) rewrite IL once
    // at patch time and have no per-call entry point to gate, so no switch here affects them.
    public static class ModSettings
    {
        private const string FileName = "AIImprove";

        static ModSettings()
        {
            GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });
        }

        // ---------------------------------------------------------------------------------
        // Legacy category toggles - migration defaults only. Not shown in the UI, not read at
        // runtime. See the class comment.
        // ---------------------------------------------------------------------------------
        private static readonly SavedBool LegacyEmergency = new SavedBool("EmergencyVehiclesEnabled", FileName, true, true);
        private static readonly SavedBool LegacyTrainsAndMetro = new SavedBool("TrainsAndMetroEnabled", FileName, true, true);
        private static readonly SavedBool LegacyIntercityTrain = new SavedBool("IntercityTrainEnabled", FileName, true, true);
        private static readonly SavedBool LegacyAircraft = new SavedBool("AircraftEnabled", FileName, true, true);
        private static readonly SavedBool LegacyBusesAndHelicopters = new SavedBool("BusesAndHelicoptersEnabled", FileName, true, true);
        private static readonly SavedBool LegacyIntercityBus = new SavedBool("IntercityBusEnabled", FileName, true, true);
        private static readonly SavedBool LegacyOrdinaryTraffic = new SavedBool("OrdinaryTrafficEnabled", FileName, true, true);
        private static readonly SavedBool LegacyCitizens = new SavedBool("CitizensEnabled", FileName, true, true);
        private static readonly SavedBool LegacyRaceCars = new SavedBool("RaceCarsEnabled", FileName, true, true);

        // ---------------------------------------------------------------------------------
        // Emergency services
        // ---------------------------------------------------------------------------------

        /// Caps how many fire trucks/helicopters respond to one burning building.
        public static readonly SavedBool FireResponseCapEnabled =
            new SavedBool("FireResponseCapEnabled", FileName, LegacyEmergency.value, true);

        public static readonly SavedInt FireMaxRespondersPerBuilding =
            new SavedInt("FireMaxRespondersPerBuilding", FileName, 20, true);

        /// Minutes a building must burn continuously before the cap is lifted for it entirely.
        public static readonly SavedInt FireUncapAfterMinutes =
            new SavedInt("FireUncapAfterMinutes", FileName, 15, true);

        /// Idle/returning fire vehicles look for a nearby still-burning building first.
        public static readonly SavedBool FireIdleSeekEnabled =
            new SavedBool("FireIdleSeekEnabled", FileName, LegacyEmergency.value, true);

        /// Emergency helicopters stay grounded during a thunderstorm.
        public static readonly SavedBool HelicopterWeatherHaltEnabled =
            new SavedBool("HelicopterWeatherHaltEnabled", FileName, LegacyEmergency.value, true);

        // ---------------------------------------------------------------------------------
        // Metro
        // ---------------------------------------------------------------------------------

        public static readonly SavedBool MetroPlatformAssignmentEnabled =
            new SavedBool("MetroPlatformAssignmentEnabled", FileName, LegacyTrainsAndMetro.value, true);

        public static readonly SavedBool MetroRerouteEnabled =
            new SavedBool("MetroRerouteEnabled", FileName, LegacyTrainsAndMetro.value, true);

        /// Ahead-congestion density (vanilla 0-100 scale) at or above which a metro train reroutes.
        public static readonly SavedInt MetroRerouteDensityThreshold =
            new SavedInt("MetroRerouteDensityThreshold", FileName, 80, true);

        // ---------------------------------------------------------------------------------
        // Intercity trains
        // ---------------------------------------------------------------------------------

        public static readonly SavedBool IntercityTrainPlatformAssignmentEnabled =
            new SavedBool("IntercityTrainPlatformAssignmentEnabled", FileName, LegacyIntercityTrain.value, true);

        /// Per-platform occupancy at which a station counts as saturated.
        public static readonly SavedInt TrainStationSaturationThreshold =
            new SavedInt("TrainStationSaturationThreshold", FileName, 25, true);

        /// How many candidate platform segments to probe around a station.
        public static readonly SavedInt TrainPlatformCandidateCount =
            new SavedInt("TrainPlatformCandidateCount", FileName, 24, true);

        public static readonly SavedBool IntercityTrainRerouteEnabled =
            new SavedBool("IntercityTrainRerouteEnabled", FileName, LegacyIntercityTrain.value, true);

        public static readonly SavedInt IntercityTrainRerouteDensityThreshold =
            new SavedInt("IntercityTrainRerouteDensityThreshold", FileName, 80, true);

        /// Throttles inbound intercity train spawns when the destination is saturated or city-wide
        /// ridership is low.
        public static readonly SavedBool IntercityTrainSpawnThrottleEnabled =
            new SavedBool("IntercityTrainSpawnThrottleEnabled", FileName, LegacyIntercityTrain.value, true);

        public static readonly SavedInt IntercityLowRidershipThreshold =
            new SavedInt("IntercityLowRidershipThreshold", FileName, 50, true);

        /// Percent chance of skipping a spawn while ridership is below the threshold.
        public static readonly SavedInt IntercityLowRidershipSkipPercent =
            new SavedInt("IntercityLowRidershipSkipPercent", FileName, 50, true);

        /// Detect-and-log only; never changes train behaviour.
        public static readonly SavedBool SingleTrackConflictDetectorEnabled =
            new SavedBool("SingleTrackConflictDetectorEnabled", FileName, LegacyTrainsAndMetro.value, true);

        // ---------------------------------------------------------------------------------
        // Aircraft
        // ---------------------------------------------------------------------------------

        public static readonly SavedBool AircraftGateAssignmentEnabled =
            new SavedBool("AircraftGateAssignmentEnabled", FileName, LegacyAircraft.value, true);

        /// How many aircraft one gate segment is considered able to hold.
        public static readonly SavedInt AircraftPerGateCapacity =
            new SavedInt("AircraftPerGateCapacity", FileName, 6, true);

        public static readonly SavedInt AircraftGateCandidateCount =
            new SavedInt("AircraftGateCandidateCount", FileName, 26, true);

        public static readonly SavedBool AircraftRerouteEnabled =
            new SavedBool("AircraftRerouteEnabled", FileName, LegacyAircraft.value, true);

        public static readonly SavedInt AircraftRerouteDensityThreshold =
            new SavedInt("AircraftRerouteDensityThreshold", FileName, 80, true);

        /// Airports refuse landings and departures for the duration of a thunderstorm.
        public static readonly SavedBool AircraftThunderstormRefusalEnabled =
            new SavedBool("AircraftThunderstormRefusalEnabled", FileName, LegacyAircraft.value, true);

        // ---------------------------------------------------------------------------------
        // Passenger helicopters
        // ---------------------------------------------------------------------------------

        public static readonly SavedBool PassengerHelicopterGateAssignmentEnabled =
            new SavedBool("PassengerHelicopterGateAssignmentEnabled", FileName, LegacyBusesAndHelicopters.value, true);

        public static readonly SavedBool PassengerHelicopterRerouteEnabled =
            new SavedBool("PassengerHelicopterRerouteEnabled", FileName, LegacyBusesAndHelicopters.value, true);

        public static readonly SavedBool PassengerHelicopterCapacityEnabled =
            new SavedBool("PassengerHelicopterCapacityEnabled", FileName, LegacyBusesAndHelicopters.value, true);

        /// Passenger helicopter capacity multiplier, in percent (200 = double).
        public static readonly SavedInt PassengerHelicopterCapacityPercent =
            new SavedInt("PassengerHelicopterCapacityPercent", FileName, 200, true);

        // ---------------------------------------------------------------------------------
        // Buses
        // ---------------------------------------------------------------------------------

        public static readonly SavedBool LocalBusRerouteEnabled =
            new SavedBool("LocalBusRerouteEnabled", FileName, LegacyBusesAndHelicopters.value, true);

        public static readonly SavedInt LocalBusRerouteDensityThreshold =
            new SavedInt("LocalBusRerouteDensityThreshold", FileName, 80, true);

        public static readonly SavedBool IntercityBusRerouteEnabled =
            new SavedBool("IntercityBusRerouteEnabled", FileName, LegacyIntercityBus.value, true);

        /// Intercity buses reroute more readily than local ones - they have further to go.
        public static readonly SavedInt IntercityBusRerouteDensityThreshold =
            new SavedInt("IntercityBusRerouteDensityThreshold", FileName, 60, true);

        // ---------------------------------------------------------------------------------
        // Ordinary city traffic
        // ---------------------------------------------------------------------------------

        public static readonly SavedBool OrdinaryTrafficRerouteEnabled =
            new SavedBool("OrdinaryTrafficRerouteEnabled", FileName, LegacyOrdinaryTraffic.value, true);

        public static readonly SavedInt OrdinaryTrafficRerouteDensityThreshold =
            new SavedInt("OrdinaryTrafficRerouteDensityThreshold", FileName, 80, true);

        // ---------------------------------------------------------------------------------
        // Citizens
        // ---------------------------------------------------------------------------------

        /// Citizens are less likely to drive toward an already-congested destination.
        public static readonly SavedBool CitizenCarProbabilityEnabled =
            new SavedBool("CitizenCarProbabilityEnabled", FileName, LegacyCitizens.value, true);

        public static readonly SavedInt CitizenCarDensityThreshold =
            new SavedInt("CitizenCarDensityThreshold", FileName, 70, true);

        /// Largest share of the drive-probability that congestion may remove, in percent.
        public static readonly SavedInt CitizenCarMaxReductionPercent =
            new SavedInt("CitizenCarMaxReductionPercent", FileName, 60, true);

        public static readonly SavedBool CitizenTaxiProbabilityEnabled =
            new SavedBool("CitizenTaxiProbabilityEnabled", FileName, LegacyCitizens.value, true);

        /// Taxi probability multiplier, in percent (150 = 1.5x).
        public static readonly SavedInt CitizenTaxiMultiplierPercent =
            new SavedInt("CitizenTaxiMultiplierPercent", FileName, 150, true);

        public static readonly SavedInt CitizenTaxiFlatBonus =
            new SavedInt("CitizenTaxiFlatBonus", FileName, 2, true);

        // ---------------------------------------------------------------------------------
        // Races
        // ---------------------------------------------------------------------------------

        public static readonly SavedBool RaceCarSpeedEnabled =
            new SavedBool("RaceCarSpeedEnabled", FileName, LegacyRaceCars.value, true);

        public static readonly SavedFloat RaceCarMaxSpeed =
            new SavedFloat("RaceCarMaxSpeed", FileName, 80f, true);

        public static readonly SavedBool RaceBuildingAttractivenessEnabled =
            new SavedBool("RaceBuildingAttractivenessEnabled", FileName, LegacyRaceCars.value, true);

        /// Racetrack attractiveness multiplier, in percent (200 = double).
        public static readonly SavedInt RaceBuildingAttractivenessPercent =
            new SavedInt("RaceBuildingAttractivenessPercent", FileName, 200, true);

        // ---------------------------------------------------------------------------------
        // Shared / advanced
        // ---------------------------------------------------------------------------------

        /// Seconds a vehicle must wait after rerouting before it may reroute again. Shared by
        /// every reroute feature - the point is to stop one vehicle thrashing, which is not
        /// vehicle-type specific.
        public static readonly SavedInt RerouteCooldownSeconds =
            new SavedInt("RerouteCooldownSeconds", FileName, 40, true);

        /// Per-vehicle congestion checks run once every N simulation frames rather than every
        /// frame. Higher = cheaper but slower to react. See SimulationStagger.
        public static readonly SavedInt RerouteCheckIntervalFrames =
            new SavedInt("RerouteCheckIntervalFrames", FileName, 32, true);

        // ---------------------------------------------------------------------------------
        // General
        // ---------------------------------------------------------------------------------

        /// "auto" follows the game's language; otherwise a Localization language code.
        public static readonly SavedString LanguageOverride =
            new SavedString("LanguageOverride", FileName, "auto", true);

        /// Per-vehicle/per-event diagnostic logging. Off by default - see Log.cs for the
        /// measurement that motivated it.
        public static readonly SavedBool VerboseLogging =
            new SavedBool("VerboseLogging", FileName, false, true);
    }
}
