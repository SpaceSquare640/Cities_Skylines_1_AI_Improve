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

        // BUG FOUND VIA AUDIT (2026-08-16, prompted by a player report that no setting persists
        // across restarts): this used to live in an explicit `static ModSettings() { ... }` body.
        // C# always runs a class's static *field* initializers before the body of an explicit
        // static constructor, regardless of source order - so every SavedBool field below whose
        // default reads another field's `.value` (the nine Legacy* migration fields do, for all 21
        // feature toggles) was doing that read *before* this registration ever ran. Confirmed via
        // output_log.txt: exactly nine "GameSettings: 'AIImprove' is not found or cannot be
        // loaded" warnings at startup, one per Legacy* field. ColossalFramework's SavedValue.Sync()
        // sets m_Synced = true even when the lookup fails, so a field poisoned this way never
        // retries for the rest of the session - it silently stops reading from and writing to disk
        // permanently. Moving registration into a field initializer of its own, declared first,
        // fixes it for real: field initializers (unlike a static constructor body) run in strict
        // declaration order, so this one now genuinely executes before any other field below it.
        private static readonly bool SettingsFileRegistered = RegisterSettingsFile();

        private static bool RegisterSettingsFile()
        {
            GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });
            return true;
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
        /// Lets ambulances, fire trucks and police cars reroute around congestion mid-journey,
        /// the same way every other road vehicle already does.
        ///
        /// WHY THIS EXISTS (2026-09-06, player screenshots): FlexibleReroutePatch.Car.Postfix
        /// opened by returning immediately for AmbulanceAI/FireTruckAI/PoliceCarAI, on the stated
        /// grounds that emergency vehicles are "handled separately". What handles them separately
        /// is EmergencyIgnoreCostsPatch - and decompiling PathFind showed that only skips
        /// NetLane.m_ticketCost, the toll on toll roads. So the vehicles with the strongest claim
        /// to routing around a jam were the only ones excluded from the feature that does it.
        /// Screenshots of dozens of ambulances queued nose-to-tail are what prompted looking.
        ///
        /// Default OFF per 12 - 開發準則 準則 1: this changes emergency vehicle behavior, and new
        /// behavior ships off until it has been verified in a real city. Flip the default once
        /// there is evidence it helps.
        public static readonly SavedBool EmergencyRerouteEnabled =
            new SavedBool("EmergencyRerouteEnabled", FileName, false, true);

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
            new SavedInt("MetroRerouteDensityThreshold", FileName, 50, true);

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
            new SavedInt("IntercityTrainRerouteDensityThreshold", FileName, 50, true);

        /// Throttles inbound intercity train spawns when the destination is saturated or city-wide
        /// ridership is low.
        public static readonly SavedBool IntercityTrainSpawnThrottleEnabled =
            new SavedBool("IntercityTrainSpawnThrottleEnabled", FileName, false, true);

        public static readonly SavedInt IntercityLowRidershipThreshold =
            new SavedInt("IntercityLowRidershipThreshold", FileName, 50, true);

        /// Percent chance of skipping a spawn while ridership is below the threshold.
        public static readonly SavedInt IntercityLowRidershipSkipPercent =
            new SavedInt("IntercityLowRidershipSkipPercent", FileName, 0, true);

        /// Detect-and-log only; never changes train behaviour.
        public static readonly SavedBool SingleTrackConflictDetectorEnabled =
            new SavedBool("SingleTrackConflictDetectorEnabled", FileName, false, true);

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

        // EXPERIMENTAL as of 2026-09-07, default off. 5000 measured samples of aircraft
        // ahead-density never exceeded 10.8 against a threshold of 50 - air density is near zero
        // by construction, so this cannot fire and no threshold value changes that. Kept visible
        // under Experimental rather than deleted, because it needs a different signal (landing
        // queue length, airport occupancy) and that is a redesign, not a tuning change.
        public static readonly SavedBool AircraftRerouteEnabled =
            new SavedBool("AircraftRerouteEnabled", FileName, false, true);

        public static readonly SavedInt AircraftRerouteDensityThreshold =
            new SavedInt("AircraftRerouteDensityThreshold", FileName, 50, true);

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
            new SavedInt("LocalBusRerouteDensityThreshold", FileName, 50, true);

        public static readonly SavedBool IntercityBusRerouteEnabled =
            new SavedBool("IntercityBusRerouteEnabled", FileName, LegacyIntercityBus.value, true);

        /// Intercity buses reroute more readily than local ones - they have further to go.
        // "設定頁不可以讓玩家自行胡亂調整參數" (2026-09-07). The numeric tunables are hidden
        // behind this, default off, and their ranges are clamped to bands that cannot break the
        // feature they belong to. The toggles that turn features on and off stay visible.
        // CALIBRATED 2026-09-07 from an 83-minute session, not chosen by feel. Percentiles of
        // the measured ahead-density distribution, per vehicle AI:
        //
        //   CargoTruckAI (69500 samples)   top 15% begins at 50
        //   TaxiAI (28000)                 top 15% begins at 50
        //   MaintenanceTruckAI (14000)     top 15% begins at 50
        //   BankVanAI (3500)               top 15% begins at 50
        //   TrainAI (2500)                 top 15-25% begins at 50
        //   GarbageTruckAI (8000)          top 20% begins at 50
        //   AmbulanceAI (46000)            top 10% begins at 50
        //   PoliceCarAI (28000)            top 15% begins at 60
        //
        // 50 lands on the top 10-20% of real congestion for nearly every type at once, which is
        // what "the road ahead is actually congested" should mean. The previous 80 sat at roughly
        // the top 5% and 60 at the top 10-13%; an earlier attempt at 40 was picked from a
        // 3-minute sample that never exceeded 66.7 and was too low.
        //
        // AircraftAI is the exception and no threshold can fix it: 5000 samples, maximum ever
        // observed 10.8. Air "density" is near zero by construction, so aircraft rerouting needs
        // a different signal entirely rather than a different number.
        // Replacement for the transit station skipping disabled on 2026-08-14. Experimental and
        // off by default per 準則 13 - it has never run in a real session.
        public static readonly SavedBool TransitDwellShortenEnabled =
            new SavedBool("TransitDwellShortenEnabled", FileName, false, true);

        // The other half: hold a vehicle that has caught up with the one in front, so the gap
        // reopens. Experimental and off by default.
        public static readonly SavedBool TransitUnbunchEnabled =
            new SavedBool("TransitUnbunchEnabled", FileName, false, true);

        public static readonly SavedBool ShowAdvancedTuning =
            new SavedBool("ShowAdvancedTuning", FileName, false, true);

        // Bumped whenever a stored numeric value has to be reclaimed from players' config files.
        //
        // WHY THIS EXISTS: three separate bugs in two days could not be fixed by changing a
        // default, because SavedInt/SavedBool read AIImprove.cgs and anyone who had opened the
        // settings panel already had the old number written there - the helicopter capacity
        // multiplier, the intercity train ridership skip, and the reroute density thresholds that
        // turned out to sit above the highest density the game ever produces. A default is not a
        // fix for anyone who has already played.
        public const int CurrentSchemaVersion = 4;

        public static readonly SavedInt SchemaVersion =
            new SavedInt("SchemaVersion", FileName, 0, true);

        public static readonly SavedInt IntercityBusRerouteDensityThreshold =
            new SavedInt("IntercityBusRerouteDensityThreshold", FileName, 50, true);

        // Re-enabled 2026-09-06 at user request. Default OFF on purpose: this feature was turned
        // off by an explicit user decision on 2026-08-14, and a mod update must never switch a
        // deliberately-disabled feature back on behind the player's back.
        //
        // This is 已載客量 (how full a bus arrives), not 總載客量 (its seat capacity) - see
        // IntercityBusPreloadPatch.cs for why the distinction matters and what went wrong when
        // the two were conflated.
        public static readonly SavedBool IntercityBusPreloadEnabled =
            new SavedBool("IntercityBusPreloadEnabled", FileName, false, true);

        // Upper bound as a percentage of the vehicle's real seat capacity.
        public static readonly SavedInt IntercityBusPreloadPercent =
            new SavedInt("IntercityBusPreloadPercent", FileName, 75, true);

        // ---------------------------------------------------------------------------------
        // Ordinary city traffic
        // ---------------------------------------------------------------------------------

        public static readonly SavedBool OrdinaryTrafficRerouteEnabled =
            new SavedBool("OrdinaryTrafficRerouteEnabled", FileName, LegacyOrdinaryTraffic.value, true);

        public static readonly SavedInt OrdinaryTrafficRerouteDensityThreshold =
            new SavedInt("OrdinaryTrafficRerouteDensityThreshold", FileName, 50, true);

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
        // Citizen transport mode ("更好的市民 AI", 2026-08-15)
        // ---------------------------------------------------------------------------------
        //
        // Four relative weights (not required to sum to 100 - normalized at roll time) that
        // decide, per citizen trip, which of Walk / Drive / Taxi / Transit that citizen ends up
        // using. Originally requested as 7 separate sliders (walk, drive, taxi, bus, metro,
        // train, other transit) - collapsed to one combined "Transit" weight because vanilla has
        // no point where a citizen picks a specific public transport type; the pathfinder mixes
        // whatever lines are available lane-by-lane. See CitizenTransportModePatch.cs for exactly
        // what each of the 4 categories forces vs. merely allows.
        //
        // Default OFF: unlike the other Citizens toggles (which only nudge vanilla's own
        // probabilities), this one fully replaces ResidentAI's vehicle-choice decision while
        // active, including bypassing CitizenCarProbabilityEnabled/CitizenTaxiProbabilityEnabled
        // above. Defaulting it on would silently overwrite existing saves' citizen behavior with
        // an arbitrary distribution, which the "off = vanilla" contract elsewhere in this mod
        // does not allow.
        public static readonly SavedBool CitizenTransportModeEnabled =
            new SavedBool("CitizenTransportModeEnabled", FileName, false, true);

        public static readonly SavedInt CitizenWalkWeight =
            new SavedInt("CitizenWalkWeight", FileName, 25, true);

        public static readonly SavedInt CitizenDriveWeight =
            new SavedInt("CitizenDriveWeight", FileName, 25, true);

        public static readonly SavedInt CitizenTaxiWeight =
            new SavedInt("CitizenTaxiWeight", FileName, 25, true);

        public static readonly SavedInt CitizenTransitWeight =
            new SavedInt("CitizenTransitWeight", FileName, 25, true);

        // ---------------------------------------------------------------------------------
        // Cargo / ships ("繼續深入研究 TransferManager 供需配對" led to finding this gap,
        // 2026-08-16 - see Cities_Skylines_1_AI_Improve_Document/10)
        // ---------------------------------------------------------------------------------

        /// Occupancy-aware dock assignment for ShipAI (cargo ships and any passenger ferries) -
        /// the one vehicle base class that never got the TrainPlatformAssignmentPatch/
        /// AircraftGateAssignmentPatch treatment. New feature, no legacy category to inherit
        /// from, defaults on since it reuses the exact same fail-open search-or-leave-vanilla-
        /// alone logic already proven safe for trains and aircraft.
        public static readonly SavedBool ShipDockAssignmentEnabled =
            new SavedBool("ShipDockAssignmentEnabled", FileName, true, true);

        public static readonly SavedInt ShipDockCandidateCount =
            new SavedInt("ShipDockCandidateCount", FileName, 24, true);

        public static readonly SavedInt ShipDockSaturationThreshold =
            new SavedInt("ShipDockSaturationThreshold", FileName, 25, true);

        // ---------------------------------------------------------------------------------
        // Sanitation ("垃圾車／殯儀車調度", 2026-08-17 - see
        // Cities_Skylines_1_AI_Improve_Document/10 for the TransferManager research this builds
        // on without touching MatchOffers itself)
        // ---------------------------------------------------------------------------------

        /// New feature, no legacy category to inherit from - defaults on since it reuses the same
        /// fail-open search-or-leave-vanilla-alone logic already proven safe for fire idle-seek.
        public static readonly SavedBool GarbageIdleSeekEnabled =
            new SavedBool("GarbageIdleSeekEnabled", FileName, true, true);

        public static readonly SavedBool HearseIdleSeekEnabled =
            new SavedBool("HearseIdleSeekEnabled", FileName, true, true);

        // ---------------------------------------------------------------------------------
        // Races
        // ---------------------------------------------------------------------------------

        // REMOVED (2026-08-15): RaceCarSpeedEnabled / RaceCarMaxSpeed - forcing a flat top-speed
        // ceiling caused racers to lose control per user report ("修改賽車車手速度會導致車輛失控").
        // Reverted to fully vanilla racer speed; see RaceBuildingAttractivenessPatch.cs for the
        // one race feature that remains.

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

        // ---------------------------------------------------------------------------------
        // Reset ("全新設計 content manager 中的 UI", 2026-08-17)
        // ---------------------------------------------------------------------------------

        /// Restores every player-facing setting to the value it ships with. Added alongside the
        /// settings page redesign because there was previously no way back from a bad tuning
        /// session other than editing each of the ~48 controls by hand.
        ///
        /// The feature toggles below reset to `true` rather than to their Legacy* migration
        /// default: those legacy fields exist only to carry a pre-split save's choice forward on
        /// first run, and all nine of them default to true anyway. "Reset to defaults" means the
        /// values a fresh install would have, not a replay of that one-time migration.
        ///
        /// LanguageOverride is deliberately NOT reset - it is a display preference, not a
        /// behaviour tuning value, and silently flipping a player's language back to "auto" while
        /// they are reading this page would be hostile.

        // Reclaims every numeric tunable from the player's config file when the schema version is
        // behind. Feature on/off toggles are deliberately NOT touched: those record decisions the
        // player made about what the mod should do, while the numbers below were interim values
        // this project picked without data and then found to be wrong - the reroute density
        // thresholds sat above the highest density the game was ever observed to produce, so the
        // feature could not fire for anyone who had ever opened the settings panel.
        //
        // "設定頁不可以讓玩家自行胡亂調整參數" (2026-09-07): the numbers are the project's to get
        // right, not the player's to guess at. They remain adjustable behind the advanced toggle,
        // but a corrected value now reaches everyone instead of only new installs.
        public static void ApplySchemaMigrations()
        {
            if (SchemaVersion.value >= CurrentSchemaVersion)
            {
                return;
            }

            int previous = SchemaVersion.value;

            FireMaxRespondersPerBuilding.value = 20;
            FireUncapAfterMinutes.value = 15;
            MetroRerouteDensityThreshold.value = 50;
            TrainStationSaturationThreshold.value = 25;
            TrainPlatformCandidateCount.value = 24;
            IntercityTrainRerouteDensityThreshold.value = 50;
            IntercityLowRidershipThreshold.value = 50;
            IntercityLowRidershipSkipPercent.value = 0;
            AircraftPerGateCapacity.value = 6;
            AircraftGateCandidateCount.value = 26;
            AircraftRerouteDensityThreshold.value = 50;
            PassengerHelicopterCapacityPercent.value = 200;
            LocalBusRerouteDensityThreshold.value = 50;
            IntercityBusRerouteDensityThreshold.value = 50;
            IntercityBusPreloadPercent.value = 75;
            OrdinaryTrafficRerouteDensityThreshold.value = 50;
            CitizenCarDensityThreshold.value = 70;
            CitizenCarMaxReductionPercent.value = 60;
            CitizenTaxiMultiplierPercent.value = 150;
            CitizenTaxiFlatBonus.value = 2;
            CitizenWalkWeight.value = 25;
            CitizenDriveWeight.value = 25;
            CitizenTaxiWeight.value = 25;
            CitizenTransitWeight.value = 25;
            ShipDockCandidateCount.value = 24;
            ShipDockSaturationThreshold.value = 25;
            RaceBuildingAttractivenessPercent.value = 200;
            RerouteCooldownSeconds.value = 40;
            RerouteCheckIntervalFrames.value = 32;

            // Schema 2: aircraft rerouting moved to Experimental and switched off. This is the
            // one place a feature TOGGLE is written by a migration, and it is defensible only
            // because the feature demonstrably cannot do anything - leaving it on would not
            // preserve a player's choice, it would preserve the appearance of one.
            if (previous < 2)
            {
                AircraftRerouteEnabled.value = false;
            }

            // Schema 3: the intercity train spawn throttle is switched off for existing configs
            // too, on the same reasoning as aircraft rerouting in schema 2 - it is not merely
            // unverified, it is measured dead. Across three maps and five and a half hours its
            // Prefix never saw a single DummyTrain offer, because intercity trains are not
            // spawned through the mechanism it attaches to. Leaving it on preserves the
            // appearance of a choice, not a choice.
            //
            // The single-track conflict detector moved to Experimental in the same pass but is
            // deliberately NOT forced off: it is uncertain rather than dead - it no-ops only when
            // SingleTrainTrackAI is installed, and may well work for players without it. New
            // installs get it off; anyone who already had it on keeps it.
            if (previous < 3)
            {
                IntercityTrainSpawnThrottleEnabled.value = false;
            }

            // Schema 4: intercity bus arrival occupancy is switched off for existing configs.
            // It does not merely fail to help - every phantom passenger it seeded permanently
            // occupied a seat a real citizen needed, because BusAI.LoadPassengers counts up from
            // the existing m_transferSize rather than recomputing it. See
            // IntercityBusPreloadPatch.cs.
            if (previous < 4)
            {
                IntercityBusPreloadEnabled.value = false;
            }

            SchemaVersion.value = CurrentSchemaVersion;

            UnityEngine.Debug.Log(
                "[AIImprove] Settings schema " + previous + " -> " + CurrentSchemaVersion +
                ": every numeric tunable has been reset to this version's calibrated value. " +
                "Feature on/off switches were left as they were.");
        }

        public static void ResetAllToDefaults()
        {
            FireResponseCapEnabled.value = true;
            FireMaxRespondersPerBuilding.value = 20;
            FireUncapAfterMinutes.value = 15;
            FireIdleSeekEnabled.value = true;
            // Added 2026-09-06. Defaults to false, unlike its neighbours - see the field's own
            // note. Wired in here at the same time as the field itself, because the two sanitation
            // toggles added on 2026-08-20 were not, and "Reset to defaults" silently skipped them
            // until 2026-08-23.
            EmergencyRerouteEnabled.value = false;
            HelicopterWeatherHaltEnabled.value = true;

            GarbageIdleSeekEnabled.value = true;
            HearseIdleSeekEnabled.value = true;

            MetroPlatformAssignmentEnabled.value = true;
            MetroRerouteEnabled.value = true;
            MetroRerouteDensityThreshold.value = 50;

            IntercityTrainPlatformAssignmentEnabled.value = true;
            TrainStationSaturationThreshold.value = 25;
            TrainPlatformCandidateCount.value = 24;
            IntercityTrainRerouteEnabled.value = true;
            IntercityTrainRerouteDensityThreshold.value = 50;
            IntercityTrainSpawnThrottleEnabled.value = false;
            IntercityLowRidershipThreshold.value = 50;
            IntercityLowRidershipSkipPercent.value = 0;
            SingleTrackConflictDetectorEnabled.value = false;

            AircraftGateAssignmentEnabled.value = true;
            AircraftPerGateCapacity.value = 6;
            AircraftGateCandidateCount.value = 26;
            AircraftRerouteEnabled.value = false;
            AircraftRerouteDensityThreshold.value = 50;
            AircraftThunderstormRefusalEnabled.value = true;

            PassengerHelicopterGateAssignmentEnabled.value = true;
            PassengerHelicopterRerouteEnabled.value = true;
            // Total-capacity changes are off by standing user instruction (2026-09-06); the
            // patch is unregistered, so these two only still exist to be reset cleanly.
            PassengerHelicopterCapacityEnabled.value = false;
            PassengerHelicopterCapacityPercent.value = 100;

            LocalBusRerouteEnabled.value = true;
            LocalBusRerouteDensityThreshold.value = 50;
            IntercityBusRerouteEnabled.value = true;
            IntercityBusRerouteDensityThreshold.value = 50;
            IntercityBusPreloadEnabled.value = false;
            TransitDwellShortenEnabled.value = false;
            TransitUnbunchEnabled.value = false;
            IntercityBusPreloadPercent.value = 75;

            OrdinaryTrafficRerouteEnabled.value = true;
            OrdinaryTrafficRerouteDensityThreshold.value = 50;

            ShipDockAssignmentEnabled.value = true;
            ShipDockCandidateCount.value = 24;
            ShipDockSaturationThreshold.value = 25;

            CitizenCarProbabilityEnabled.value = true;
            CitizenCarDensityThreshold.value = 70;
            CitizenCarMaxReductionPercent.value = 60;
            CitizenTaxiProbabilityEnabled.value = true;
            CitizenTaxiMultiplierPercent.value = 150;
            CitizenTaxiFlatBonus.value = 2;

            CitizenTransportModeEnabled.value = false;
            CitizenWalkWeight.value = 25;
            CitizenDriveWeight.value = 25;
            CitizenTaxiWeight.value = 25;
            CitizenTransitWeight.value = 25;

            RaceBuildingAttractivenessEnabled.value = true;
            RaceBuildingAttractivenessPercent.value = 200;

            RerouteCooldownSeconds.value = 40;
            RerouteCheckIntervalFrames.value = 32;

            VerboseLogging.value = false;
        }
    }
}
