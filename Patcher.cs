using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AIImprove
{
    // Kept separate from AIImproveMod so HarmonyLib is never touched unless CitiesHarmony reported ready.
    public static class Patcher
    {
        private const string HarmonyId = "spacesquare.aiimprove";

        private static bool patched = false;

        public static void PatchAll()
        {
            if (patched)
            {
                return;
            }

            patched = true;

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            PatchEmergencyVehiclePriority(harmony);
            TryPatchEmergencyIgnoreCosts(harmony, typeof(AmbulanceAI), typeof(AmbulanceIgnoreCostsPatch));
            TryPatchEmergencyIgnoreCosts(harmony, typeof(FireTruckAI), typeof(FireTruckIgnoreCostsPatch));
            TryPatchEmergencyIgnoreCosts(harmony, typeof(PoliceCarAI), typeof(PoliceCarIgnoreCostsPatch));
            TryPatchArrivalTracking(harmony, typeof(AmbulanceAI), typeof(ArrivalTrackingPatch.Ambulance));
            TryPatchArrivalTracking(harmony, typeof(FireTruckAI), typeof(ArrivalTrackingPatch.FireTruck));
            TryPatchArrivalTracking(harmony, typeof(PoliceCarAI), typeof(ArrivalTrackingPatch.PoliceCar));
            TryPatchArrivalTracking(harmony, typeof(AmbulanceCopterAI), typeof(ArrivalTrackingPatch.AmbulanceCopter));
            TryPatchArrivalTracking(harmony, typeof(FireCopterAI), typeof(ArrivalTrackingPatch.FireCopter));
            TryPatchArrivalTracking(harmony, typeof(PoliceCopterAI), typeof(ArrivalTrackingPatch.PoliceCopter));
            TryPatchHelicopterDispatchTracking(harmony);
            TryPatchTrainPlatformAssignment(harmony);
            TryPatchAircraftGateAssignment(harmony);
            TryPatchShipDockAssignment(harmony);
            TryPatchFlexibleReroute(harmony, typeof(TrainAI), typeof(FlexibleReroutePatch.Train));
            TryPatchFlexibleReroute(harmony, typeof(AircraftAI), typeof(FlexibleReroutePatch.Aircraft));
            // CarAI is the declaring type of SimulationStep(ushort, ref Vehicle, Vector3) - some
            // subtypes (e.g. BusAI) only override the ref-Frame overload - so this patches CarAI
            // directly; FlexibleReroutePatch.Car.Postfix covers every ordinary road vehicle
            // (private cars, taxis, in-city and intercity buses) and excludes emergency vehicles,
            // which are handled separately.
            TryPatchFlexibleReroute(harmony, typeof(CarAI), typeof(FlexibleReroutePatch.Car));
            // REMOVED (2026-09-05): CargoTruckAI used to get its own registration here, on the
            // stated grounds that it overrides this overload and so the CarAI patch above "never
            // actually runs for cargo trucks". That reasoning was wrong, and the comment saying so
            // was actively misleading maintenance. Decompiling CargoTruckAI.SimulationStep shows
            // its override ends with `base.SimulationStep(vehicleID, ref data, physicsLodRefPos)`
            // in the non-Congestion branch - a direct call into the patched CarAI method - so the
            // CarAI Postfix has always fired for cargo trucks. The extra registration only made it
            // fire twice per truck per tick.
            //
            // Live logs agree: TaxiAI, PostVanAI, GarbageTruckAI, HearseAI, MaintenanceTruckAI and
            // BusAI all report "is executing" through the CarAI registration despite overriding
            // the same overload, because they all call base too.
            //
            // The corrected rule: "subclass overrides it, so the base patch will not run" depends
            // entirely on whether the override calls base. Check the override's body in dnSpy
            // before adding a registration for a subtype.
            TryPatchFireResponseCap(harmony, typeof(FireTruckAI), typeof(FireResponseCapPatch.Truck));
            TryPatchFireResponseCap(harmony, typeof(FireCopterAI), typeof(FireResponseCapPatch.Copter));
            TryPatchSanitationIdleSeek(harmony, typeof(GarbageTruckAI), typeof(SanitationIdleSeekPatch.Garbage));
            TryPatchSanitationIdleSeek(harmony, typeof(HearseAI), typeof(SanitationIdleSeekPatch.Hearse));
            TryPatchTrainSpawnThrottle(harmony);
            TryPatchCitizenCarProbability(harmony);
            TryPatchCitizenTaxiProbability(harmony);
            TryPatchCitizenTransportMode(harmony);
            TryPatchHelicopterWeatherHalt(harmony);
            // TryPatchTrainPassengerCapacity(harmony) - DISABLED (2026-08-14) per user request
            // ("取消對城際火車及城際巴士的乘客改動"). File kept in place, not deleted, in case
            // this gets revisited later - just not registered/active.

            // Race features are Races and Parades content - skip registering them outright
            // without it, so the log reflects what is actually active instead of listing patches
            // whose target methods can never run (2026-08-14, per user request to DLC-gate the
            // remaining features). Purely cosmetic/hygienic: an unused patch costs nothing at
            // runtime, unlike the thunderstorm scan gated in WeatherDisasterDetector.
            //
            // REMOVED (2026-08-15): the flat top-speed override (TryPatchRaceCarSpeed /
            // RaceCarSpeedPatch) per explicit user request - "修改賽車車手速度會導致車輛失控". Racer
            // speed is fully vanilla again; only the race-building attractiveness patch remains.
            if (DlcDetector.IsRacesAndParadesOwned())
            {
                TryPatchRaceBuildingAttractiveness(harmony);
            }
            else
            {
                Debug.Log(
                    "[AIImprove] Races and Parades not owned - skipping the race complex " +
                    "attractiveness patch, there is no race content to affect.");
            }
            TryPatchPassengerHelicopterGateAssignment(harmony);
            TryPatchFlexibleReroute(harmony, typeof(PassengerHelicopterAI), typeof(FlexibleReroutePatch.PassengerHelicopter));
            // TryPatchPassengerHelicopterCapacity(harmony) - DISABLED 2026-09-06 by standing
            // user instruction: "盡量不要修改任何載具的總載客量". Multiplying m_passengerCapacity
            // produced absurd figures in early development - a minibus at 5000 seats, a taxi at
            // 500 - because the multiplier lands on whatever value is already there, and that
            // value can already be someone else's (Advanced Vehicle Options set a train to 15984,
            // which our x2 turned into 31968 - see CompanionModCompat.cs). Deferring to AVO only
            // covered the case where we knew who the other party was.
            //
            // This was the last registered patch that wrote m_passengerCapacity, so as of now
            // this mod does not change any vehicle's total capacity at all. If fuller vehicles
            // are wanted, the supported way is the occupancy approach used by
            // IntercityBusPreloadPatch: set how many passengers are already aboard, as a
            // percentage of the vehicle's own real capacity, and leave the capacity alone.
            //
            // The setting now defaults off, but a player who previously turned it on still has
            // true saved in AIImprove.cgs - which is exactly why this is unregistered rather than
            // merely defaulted off, and why its settings entry is gone: a toggle that cannot do
            // anything is worse than no toggle.
            // RE-ENABLED 2026-09-06 at user request. It is registered, but its own setting
            // (IntercityBusPreloadEnabled) defaults to OFF, so nothing changes for an existing
            // player until they turn it on - the 2026-08-14 decision to disable it stands until
            // the player themselves reverses it. The reason it was unsafe to leave running is
            // gone rather than mitigated: it no longer writes m_passengerCapacity at all (see
            // IntercityBusPreloadPatch.cs).
            TryPatchIntercityBusPreload(harmony);
            // TryPatchTransitStationSkip(...) x3 - DISABLED (2026-08-14) after a player bug report
            // ("地鐵無法移動 / 有些巴士無法移動 / 直升機無法移動 / 部份電車無法移動 /
            // 公共交通工具客量大幅減少"), confirmed against their output_log: 3676 skip events
            // across 473 vehicles, with buses routinely flying past 6+ consecutive stops.
            //
            // Root cause (found via dnSpy): the "車站沒有乘客" rule measures the wrong thing.
            // BusAI.LoadPassengers only boards citizens already flagged WaitingTransport within
            // 32m *at the instant of arrival* - vanilla then lets the vehicle dwell at the stop
            // (Stopped + m_waitCounter, CanLeave needs m_waitCounter >= 12) so more riders can
            // reach it. Checking "did m_transferSize increase during ArriveAtTarget" therefore
            // reports "nobody boarded" for any stop whose riders simply hadn't walked up yet, so
            // the vehicle skips a stop it should have served - which strands those citizens and
            // makes the next stop look empty too, a self-reinforcing spiral that matches the
            // reported ridership collapse exactly. The stuck vehicles are the same mechanism's
            // other half: each skip re-enters ArriveAtTarget and fires another StartPathFind in
            // the same tick, so a chain of skips issues several competing path requests for one
            // vehicle while leaving Stopped set.
            //
            // Fixing this properly needs a genuinely different signal for "this stop has no
            // demand" (e.g. TransportLine.CalculatePassengerCount, which scans waiting citizens
            // around a stop without depending on boarding having already happened) plus a
            // non-recursive way to advance to the next stop. Files kept in place for that
            // rework; not registered until it exists.
            //
            // TryPatchThunderstormFacilityShutdown(...) x2 - DISABLED (2026-08-14) after the user
            // reported air facilities never recovering once a storm passed. Clearing
            // Building.Flags.Active turned out to have persistent side effects this mod doesn't
            // control: TransportStationAI.CreateConnectionLines stamps NetNode.Flags.Disabled onto
            // a station's connection nodes whenever Active is clear at that moment, and those node
            // flags are not recomputed per frame - they survive until the connection lines are
            // rebuilt, so a station could stay functionally dead even after Active came back. The
            // flag is also save-persisted, so any failure to restore it (save/load mid-storm, an
            // exception, unsubscribing the mod) bricks the building permanently in the player's
            // save. Not worth that risk: the vehicle-level thunderstorm refusals in
            // AircraftGateAssignmentPatch and HelicopterWeatherHaltPatch remain registered and
            // already delivered the requested behavior (confirmed in logs), just without the
            // "Not Operating" building status.
            TryPatchTrainSingleTrackConflictDetector(harmony);
            TryPatchShipQueueDetector(harmony);

            Debug.Log("[AIImprove] Harmony patches applied.");
            // Recorded once per session so a player's output_log.txt says which of the mods this
            // one is designed to coexist with were actually present - see CompanionModCompat.
            CompanionModCompat.LogDetectedCompanions();
        }

        // Boosts intercity bus capacity - see IntercityBusPreloadPatch.cs.
        private static bool TryPatchIntercityBusPreload(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(BusAI),
                    "CreateVehicle",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] BusAI.CreateVehicle not found - game version may have changed. " +
                        "Skipping intercity bus preload patch.");
                    return false;
                }

                MethodInfo prefix = typeof(IntercityBusPreloadPatch).GetMethod(
                    nameof(IntercityBusPreloadPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Intercity bus preload patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Intercity bus preload patch failed to apply, skipping it. Rest " +
                    "of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // "Fly past a congested/empty stop" - see TransitStationSkipPatch.cs. ArriveAtTarget is
        // private on every target type here, hence BindingFlags.NonPublic (needed for the
        // instance-method search since it's not public). Both Prefix (captures pre-call state)
        // and Postfix (does the actual skip decision) patch the same original method.
        private static bool TryPatchTransitStationSkip(Harmony harmony, Type vehicleAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    vehicleAiType,
                    "ArriveAtTarget",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + vehicleAiType.Name + ".ArriveAtTarget not found - game " +
                        "version may have changed. Skipping transit station skip patch for " +
                        vehicleAiType.Name + ".");
                    return false;
                }

                MethodInfo prefix = patchWrapperType.GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
                MethodInfo postfix = patchWrapperType.GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Transit station skip patch applied for " + vehicleAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Transit station skip patch failed to apply for " + vehicleAiType.Name +
                    ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Directly flips airports/heliports/emergency-helicopter-depots to "Not Operating" during
        // a thunderstorm - see ThunderstormFacilityShutdownPatch.cs.
        private static bool TryPatchThunderstormFacilityShutdown(Harmony harmony, Type buildingAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    buildingAiType,
                    "SimulationStep",
                    new[] { typeof(ushort), typeof(Building).MakeByRefType(), typeof(Building.Frame).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + buildingAiType.Name + ".SimulationStep(ushort, ref Building, " +
                        "ref Building.Frame) not found - game version may have changed. Skipping " +
                        "thunderstorm facility shutdown patch for " + buildingAiType.Name + ".");
                    return false;
                }

                MethodInfo postfix = patchWrapperType.GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Thunderstorm facility shutdown patch applied for " + buildingAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Thunderstorm facility shutdown patch failed to apply for " +
                    buildingAiType.Name + ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Detect-and-log-only single-shared-track conflict detector - see
        // TrainSingleTrackConflictDetector.cs. Adds its own independent Postfix onto the same
        // TrainAI.SimulationStep(ushort, ref Vehicle, Vector3) overload FlexibleReroutePatch
        // already Postfixes for trains - Harmony supports multiple independent registrations on
        // the same method, same pattern as HelicopterWeatherHaltPatch/HelicopterDispatchTrackingPatch.
        private static bool TryPatchTrainSingleTrackConflictDetector(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(TrainAI),
                    "SimulationStep",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] TrainAI.SimulationStep(ushort, ref Vehicle, Vector3) not found - " +
                        "game version may have changed. Skipping single-track conflict detector patch.");
                    return false;
                }

                MethodInfo postfix = typeof(TrainSingleTrackConflictDetector.Train).GetMethod(
                    "Postfix", BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Single-track conflict detector patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Single-track conflict detector patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Detect-and-log-only ship queue observer - see ShipQueueDetector.cs. Phase 1 of the
        // "canal full of motionless ships" investigation; changes no vehicle behavior.
        //
        // ShipAI is the correct declaring type: dnSpy confirms ShipAI declares
        // SimulationStep(ushort, ref Vehicle, Vector3) and PassengerShipAI overrides only the
        // 6-arg overload, so one patch here covers passenger ferries and cargo ships alike -
        // which is precisely what the detector needs, since telling those two apart at runtime is
        // the open question. The 3-arg overload also has a single ref-struct parameter, the shape
        // this project has repeatedly verified as safe under Mono's JIT.
        private static bool TryPatchShipQueueDetector(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(ShipAI),
                    "SimulationStep",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] ShipAI.SimulationStep(ushort, ref Vehicle, Vector3) not found - " +
                        "game version may have changed. Skipping ship queue detector patch.");
                    return false;
                }

                MethodInfo postfix = typeof(ShipQueueDetector).GetMethod(
                    nameof(ShipQueueDetector.Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Ship queue detector patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Ship queue detector patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Occupancy-aware landing-point assignment for passenger helicopters - see
        // PassengerHelicopterGateAssignmentPatch.cs. Release tracking is already covered by the
        // existing global VehicleAI.ReleaseVehicle patch (TryPatchAircraftGateRelease) -
        // PassengerHelicopterAI's own ReleaseVehicle override calls base.ReleaseVehicle, so that
        // patch fires for it too.
        private static bool TryPatchPassengerHelicopterGateAssignment(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(PassengerHelicopterAI),
                    "StartPathFind",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(bool) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] PassengerHelicopterAI.StartPathFind(7-arg) not found - game " +
                        "version may have changed. Skipping passenger helicopter gate assignment patch.");
                    return false;
                }

                MethodInfo prefix = typeof(PassengerHelicopterGateAssignmentPatch).GetMethod(
                    nameof(PassengerHelicopterGateAssignmentPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Passenger helicopter gate assignment patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Passenger helicopter gate assignment patch failed to apply, " +
                    "skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // TryPatchPassengerHelicopterCapacity removed 2026-09-06 along with the patch
        // body it registered - see PassengerHelicopterCapacityPatch.cs and 準則 11. Kept as
        // a note so nobody re-adds a registration for a patch that no longer exists.

        // TryPatchTrainPassengerCapacity removed 2026-09-06 - see
        // TrainPassengerCapacityPatch.cs and 準則 11.

        // Boosts the motorsport race complex's tourism attractiveness - see
        // RaceBuildingAttractivenessPatch.cs.
        private static bool TryPatchRaceBuildingAttractiveness(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(RaceBuildingAI),
                    "GetAttractivenessAccumulation",
                    new[] { typeof(ushort), typeof(Building).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] RaceBuildingAI.GetAttractivenessAccumulation not found - game " +
                        "version may have changed. Skipping race building attractiveness patch.");
                    return false;
                }

                MethodInfo postfix = typeof(RaceBuildingAttractivenessPatch).GetMethod(
                    nameof(RaceBuildingAttractivenessPatch.Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Race building attractiveness patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Race building attractiveness patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Grounds new emergency-helicopter dispatches during an active thunderstorm - see
        // WeatherDisasterDetector.cs / HelicopterWeatherHaltPatch.cs. Prefix on the same
        // HelicopterAI.StartPathFind(5-arg) method HelicopterDispatchTrackingPatch already
        // Postfixes - Harmony supports independent prefix/postfix registrations on the same
        // method from different call sites, so this is a separate Patch() call rather than
        // folding into the existing one.
        private static bool TryPatchHelicopterWeatherHalt(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(HelicopterAI),
                    "StartPathFind",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(float) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] HelicopterAI.StartPathFind(5-arg) not found - game version may " +
                        "have changed. Skipping helicopter weather halt patch.");
                    return false;
                }

                MethodInfo prefix = typeof(HelicopterWeatherHaltPatch).GetMethod(
                    nameof(HelicopterWeatherHaltPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Helicopter weather halt patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Helicopter weather halt patch failed to apply, skipping it. Rest " +
                    "of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Makes citizens' walk/drive/transit split respond to real-time road congestion - see
        // CitizenCarProbabilityPatch.cs. GetCarProbability is private, hence BindingFlags.NonPublic.
        private static bool TryPatchCitizenCarProbability(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(ResidentAI),
                    "GetCarProbability",
                    new[] { typeof(ushort), typeof(CitizenInstance).MakeByRefType(), typeof(Citizen.AgeGroup) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] ResidentAI.GetCarProbability not found - game version may have " +
                        "changed. Skipping citizen car probability patch.");
                    return false;
                }

                MethodInfo postfix = typeof(CitizenCarProbabilityPatch).GetMethod(
                    nameof(CitizenCarProbabilityPatch.Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Citizen car probability patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Citizen car probability patch failed to apply, skipping it. Rest " +
                    "of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Increases taxi usage - see CitizenTaxiProbabilityPatch.cs. GetTaxiProbability is
        // private, hence BindingFlags.NonPublic.
        private static bool TryPatchCitizenTaxiProbability(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(ResidentAI),
                    "GetTaxiProbability",
                    new[] { typeof(ushort), typeof(CitizenInstance).MakeByRefType(), typeof(Citizen.AgeGroup) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] ResidentAI.GetTaxiProbability not found - game version may " +
                        "have changed. Skipping citizen taxi probability patch.");
                    return false;
                }

                MethodInfo postfix = typeof(CitizenTaxiProbabilityPatch).GetMethod(
                    nameof(CitizenTaxiProbabilityPatch.Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Citizen taxi probability patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Citizen taxi probability patch failed to apply, skipping it. Rest " +
                    "of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Replaces vanilla's own layered car/bike/taxi/electric-car dice rolls with a single
        // weighted Walk/Drive/Taxi/Transit pick - see CitizenTransportModePatch.cs. GetVehicleInfo
        // is protected, hence BindingFlags.NonPublic; skips the original (Prefix returns false)
        // only when ModSettings.CitizenTransportModeEnabled is on.
        private static bool TryPatchCitizenTransportMode(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(ResidentAI),
                    "GetVehicleInfo",
                    new[] { typeof(ushort), typeof(CitizenInstance).MakeByRefType(), typeof(bool), typeof(VehicleInfo).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] ResidentAI.GetVehicleInfo not found - game version may have " +
                        "changed. Skipping citizen transport mode patch.");
                    return false;
                }

                MethodInfo prefix = typeof(CitizenTransportModePatch).GetMethod(
                    nameof(CitizenTransportModePatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Citizen transport mode patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Citizen transport mode patch failed to apply, skipping it. Rest " +
                    "of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Skips spawning a new incoming intercity train when its destination station is already
        // saturated - see TrainSpawnThrottlePatch.cs. Single `ref Building` param, safe shape.
        private static bool TryPatchTrainSpawnThrottle(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(OutsideConnectionAI),
                    "StartTransfer",
                    new[] { typeof(ushort), typeof(Building).MakeByRefType(), typeof(TransferManager.TransferReason), typeof(TransferManager.TransferOffer) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] OutsideConnectionAI.StartTransfer not found - game version may " +
                        "have changed. Skipping train spawn throttle patch.");
                    return false;
                }

                MethodInfo prefix = typeof(TrainSpawnThrottlePatch).GetMethod(
                    nameof(TrainSpawnThrottlePatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Train spawn throttle patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Train spawn throttle patch failed to apply, skipping it. Rest of " +
                    "the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Caps responders per burning building - see FireResponseTracker.cs / FireResponseCapPatch.cs.
        private static bool TryPatchFireResponseCap(Harmony harmony, Type vehicleAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    vehicleAiType,
                    "SetTarget",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(ushort) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + vehicleAiType.Name + ".SetTarget not found - game version " +
                        "may have changed. Skipping fire response cap patch for " + vehicleAiType.Name + ".");
                    return false;
                }

                MethodInfo prefix = patchWrapperType.GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Fire response cap patch applied for " + vehicleAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Fire response cap patch failed to apply for " + vehicleAiType.Name +
                    ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Idle garbage trucks / hearses seek nearby buildings that still need collection instead
        // of waiting on TransferManager's own priority/distance-weighted matching - see
        // SanitationIdleSeekPatch.cs / SanitationIdleSeekTracker.cs. Same SetTarget(ushort, ref
        // Vehicle, ushort) shape as TryPatchFireResponseCap.
        private static bool TryPatchSanitationIdleSeek(Harmony harmony, Type vehicleAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    vehicleAiType,
                    "SetTarget",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(ushort) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + vehicleAiType.Name + ".SetTarget not found - game version " +
                        "may have changed. Skipping sanitation idle-seek patch for " + vehicleAiType.Name + ".");
                    return false;
                }

                MethodInfo prefix = patchWrapperType.GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Sanitation idle-seek patch applied for " + vehicleAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Sanitation idle-seek patch failed to apply for " + vehicleAiType.Name +
                    ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Bounded "mid-route dynamic reroute" v1 - see FlexibleReroutePatch.cs /
        // StuckRerouteTracker.cs. Postfixes SimulationStep(ushort, ref Vehicle, Vector3), which
        // both TrainAI and AircraftAI override directly with that exact signature.
        private static bool TryPatchFlexibleReroute(Harmony harmony, Type vehicleAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    vehicleAiType,
                    "SimulationStep",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + vehicleAiType.Name + ".SimulationStep(ushort, ref Vehicle, " +
                        "Vector3) not found - game version may have changed. Skipping flexible " +
                        "reroute patch for " + vehicleAiType.Name + ".");
                    return false;
                }

                MethodInfo postfix = patchWrapperType.GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Flexible reroute patch applied for " + vehicleAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Flexible reroute patch failed to apply for " + vehicleAiType.Name +
                    ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Dispatch-start half of helicopter effect measurement - see
        // HelicopterDispatchTrackingPatch.cs for why this patches the shared HelicopterAI base
        // method (declaring-type requirement, same reasoning as the ReleaseVehicle fix below) and
        // filters by instance type instead of patching each copter type individually.
        private static bool TryPatchHelicopterDispatchTracking(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(HelicopterAI),
                    "StartPathFind",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(float) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] HelicopterAI.StartPathFind(5-arg) not found - game version may " +
                        "have changed. Skipping helicopter dispatch tracking patch.");
                    return false;
                }

                MethodInfo postfix = typeof(HelicopterDispatchTrackingPatch).GetMethod(
                    nameof(HelicopterDispatchTrackingPatch.Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Helicopter dispatch tracking patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Helicopter dispatch tracking patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Occupancy-aware ATC-style platform assignment for trains - upgraded from the earlier
        // blind-jitter PlatformGateJitterPatch after in-game testing. See
        // TrainPlatformAssignmentPatch.cs. Shares AirTrafficControlManager and the
        // VehicleAI.ReleaseVehicle release patch with the aircraft version below.
        private static bool TryPatchTrainPlatformAssignment(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(TrainAI),
                    "StartPathFind",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] TrainAI.StartPathFind(6-arg) not found - game version may have " +
                        "changed. Skipping train platform assignment patch.");
                    return false;
                }

                MethodInfo prefix = typeof(TrainPlatformAssignmentPatch).GetMethod(
                    nameof(TrainPlatformAssignmentPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Train platform assignment patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Train platform assignment patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Occupancy-aware dock assignment for ships (cargo ships and any passenger ferries) -
        // ShipAI never had this before (dnSpy-confirmed: no existing patch anywhere in this
        // project touches ShipAI). See ShipDockAssignmentPatch.cs.
        private static bool TryPatchShipDockAssignment(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(ShipAI),
                    "StartPathFind",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] ShipAI.StartPathFind(6-arg) not found - game version may have " +
                        "changed. Skipping ship dock assignment patch.");
                    return false;
                }

                MethodInfo prefix = typeof(ShipDockAssignmentPatch).GetMethod(
                    nameof(ShipDockAssignmentPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Ship dock assignment patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Ship dock assignment patch failed to apply, skipping it. Rest of " +
                    "the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Occupancy-aware ATC-style gate assignment for planes - see
        // AirTrafficControlManager.cs / AircraftGateAssignmentPatch.cs.
        // Split into two independent try/catch blocks (gate assignment, release tracking) so a
        // failure in one can never take down the other - see the ReleaseVehicle bug below,
        // caught by the try/catch exactly as designed rather than crashing anything.
        private static bool TryPatchAircraftGateAssignment(Harmony harmony)
        {
            bool gateAssigned = TryPatchAircraftGatePrefix(harmony);
            TryPatchAircraftGateRelease(harmony);
            return gateAssigned;
        }

        private static bool TryPatchAircraftGatePrefix(Harmony harmony)
        {
            try
            {
                MethodInfo startPathFind = AccessTools.Method(
                    typeof(AircraftAI),
                    "StartPathFind",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool) });

                if (startPathFind == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] AircraftAI.StartPathFind(6-arg) not found - game version may " +
                        "have changed. Skipping aircraft gate assignment patch.");
                    return false;
                }

                MethodInfo gatePrefix = typeof(AircraftGateAssignmentPatch).GetMethod(
                    nameof(AircraftGateAssignmentPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(startPathFind, prefix: new HarmonyMethod(gatePrefix));

                Debug.Log("[AIImprove] Aircraft gate assignment patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Aircraft gate assignment patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // KNOWN ISSUE (2026-08-12, fixed): AircraftAI doesn't override ReleaseVehicle, so
        // AccessTools.Method(typeof(AircraftAI), "ReleaseVehicle", ...) resolves to a MethodInfo
        // whose ReflectedType is AircraftAI but DeclaringType is VehicleAI - the bundled Harmony
        // version refuses to patch that ("Patch the declared method ... VehicleAI::ReleaseVehicle
        // instead"). Fix: patch VehicleAI.ReleaseVehicle directly (the actual declaring type).
        // This runs for every vehicle in the game, not just aircraft, but that's harmless -
        // AirTrafficControlManager.ReleaseVehicle no-ops for any vehicle ID it never assigned a
        // gate to, so cars/buses/trains/etc. passing through cost one dictionary lookup and
        // nothing else.
        private static bool TryPatchAircraftGateRelease(Harmony harmony)
        {
            try
            {
                MethodInfo releaseVehicle = AccessTools.Method(
                    typeof(VehicleAI), "ReleaseVehicle", new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

                if (releaseVehicle == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] VehicleAI.ReleaseVehicle not found - gate occupancy counts " +
                        "will leak (never freed) until the mod or game restarts. Non-fatal.");
                    return false;
                }

                MethodInfo releasePostfix = typeof(AircraftReleasePatch).GetMethod(
                    nameof(AircraftReleasePatch.Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(releaseVehicle, postfix: new HarmonyMethod(releasePostfix));

                Debug.Log("[AIImprove] Gate release tracking patch applied (VehicleAI.ReleaseVehicle, all vehicle types).");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Gate release tracking patch failed to apply, skipping it. Gate " +
                    "occupancy counts will leak. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Effect measurement for the ignore-costs patch above - see ArrivalTrackingPatch.cs.
        private static bool TryPatchArrivalTracking(Harmony harmony, Type vehicleAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    vehicleAiType,
                    "ArriveAtDestination",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + vehicleAiType.Name + ".ArriveAtDestination not found - game " +
                        "version may have changed. Skipping arrival tracking patch for " + vehicleAiType.Name + ".");
                    return false;
                }

                MethodInfo postfix = patchWrapperType.GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] Arrival tracking patch applied for " + vehicleAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Arrival tracking patch failed to apply for " + vehicleAiType.Name +
                    ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Soft dependency: prefer the TMPE-aware patch when TMPE is present and it actually
        // applies cleanly, otherwise fall back to the vanilla PathFind transpiler. Resilient
        // to either path failing for any reason - the other is still attempted.
        //
        // KNOWN DEAD as of 2026-08-12 (kept in place; see Cities_Skylines_1_AI_Improve_Document/03
        // "策略性轉向"): the TMPE branch fails on a Mono JIT limitation, and the vanilla branch
        // patches successfully but is never actually called when TMPE is active - TMPE's
        // CustomPathFind replaces PathFind entirely for real pathfinding. Superseded by
        // TryPatchAmbulanceIgnoreCosts below, which works whether or not TMPE is installed.
        private static void PatchEmergencyVehiclePriority(Harmony harmony)
        {
            if (TryPatchTmpeEmergencyVehiclePriority(harmony))
            {
                return;
            }

            TryPatchVanillaEmergencyCongestion(harmony);
        }

        // Vehicle-level, TMPE-independent emergency priority patch - see EmergencyIgnoreCostsPatch.cs.
        // Same shared transpiler body applied to AmbulanceAI, FireTruckAI and PoliceCarAI, each
        // via its own thin wrapper class (Harmony transpilers must be plain static methods).
        private static bool TryPatchEmergencyIgnoreCosts(Harmony harmony, Type vehicleAiType, Type patchWrapperType)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    vehicleAiType,
                    "StartPathFind",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(bool) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] " + vehicleAiType.Name + ".StartPathFind(7-arg) not found - game " +
                        "version may have changed. Skipping ignore-costs patch for " + vehicleAiType.Name + ".");
                    return false;
                }

                MethodInfo transpiler = patchWrapperType.GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));

                Debug.Log("[AIImprove] Ignore-costs patch applied for " + vehicleAiType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Ignore-costs patch failed to apply for " + vehicleAiType.Name +
                    ", skipping it. Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Reflection-only patch: no compile-time reference to TMPE exists anywhere in this
        // project, so this is skipped harmlessly (not an error) when TMPE isn't installed,
        // or if TMPE's internals have changed since this was written.
        //
        // KNOWN ISSUE (2026-08-12): currently fails at runtime with
        // "FormatException: ... cannot be patched. Reason: Invalid IL code".
        // Root cause: CalculateAdvancedAiCostFactors has 4 `ref` struct parameters
        // (BufferItem, NetSegment, NetLane, NetNode), and Harmony's dynamic-method wrapper
        // generation hits a known Mono JIT limitation with methods shaped like this -
        // see https://github.com/pardeike/Harmony/issues/105. This is a Mono/Harmony
        // limitation, not a logic bug in this patch. Wrapped in try/catch so it fails
        // loudly in the log without ever taking down PatchAll() or the game. Needs a
        // different insertion point (see Cities_Skylines_1_AI_Improve_Document/03).
        private static bool TryPatchTmpeEmergencyVehiclePriority(Harmony harmony)
        {
            try
            {
                Type customPathFindType = TmpeCompat.FindCustomPathFindType();
                if (customPathFindType == null)
                {
                    Debug.Log("[AIImprove] TMPE not detected, skipping TMPE emergency vehicle priority patch.");
                    return false;
                }

                MethodInfo original = TmpeCompat.FindCostFactorsMethod(customPathFindType);
                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] TMPE detected but CalculateAdvancedAiCostFactors not found - " +
                        "TMPE may not have Advanced Vehicle AI compiled in, or its internals changed. " +
                        "Skipping TMPE emergency vehicle priority patch.");
                    return false;
                }

                MethodInfo postfix = typeof(EmergencyVehiclePriorityPatch).GetMethod(
                    nameof(EmergencyVehiclePriorityPatch.Postfix),
                    BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                Debug.Log("[AIImprove] TMPE detected, TMPE emergency vehicle priority patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] TMPE emergency vehicle priority patch failed to apply, falling back " +
                    "to the vanilla PathFind transpiler. Reason: " + ex.Message);
                return false;
            }
        }

        // Vanilla fallback: patches PathFind.ProcessItemCosts directly. Located by name since
        // its first parameter (PathFind.BufferItem) is a private nested struct and can't be
        // named from this assembly - see VanillaEmergencyCongestionPatch.cs.
        private static bool TryPatchVanillaEmergencyCongestion(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(typeof(PathFind), "ProcessItemCosts");
                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] PathFind.ProcessItemCosts not found - game version may have " +
                        "changed. Skipping vanilla emergency congestion patch.");
                    return false;
                }

                MethodInfo transpiler = typeof(VanillaEmergencyCongestionPatch).GetMethod(
                    nameof(VanillaEmergencyCongestionPatch.Transpiler),
                    BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));

                Debug.Log("[AIImprove] Vanilla emergency congestion patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Vanilla emergency congestion patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        public static void UnpatchAll()
        {
            if (!patched)
            {
                return;
            }

            var harmony = new Harmony(HarmonyId);
            harmony.UnpatchAll(HarmonyId);

            patched = false;

            Debug.Log("[AIImprove] Harmony patches removed.");
        }
    }
}
