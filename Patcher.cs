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
            TryPatchFlexibleReroute(harmony, typeof(TrainAI), typeof(FlexibleReroutePatch.Train));
            TryPatchFlexibleReroute(harmony, typeof(AircraftAI), typeof(FlexibleReroutePatch.Aircraft));
            // CarAI is the declaring type of SimulationStep(ushort, ref Vehicle, Vector3) - some
            // subtypes (e.g. BusAI) only override the ref-Frame overload - so this patches CarAI
            // directly; FlexibleReroutePatch.Car.Postfix covers every ordinary road vehicle
            // (private cars, taxis, in-city and intercity buses) and excludes emergency vehicles,
            // which are handled separately.
            TryPatchFlexibleReroute(harmony, typeof(CarAI), typeof(FlexibleReroutePatch.Car));
            // CargoTruckAI is a CarAI subtype but overrides this exact SimulationStep(ushort, ref
            // Vehicle, Vector3) overload with its own implementation - the CarAI patch above never
            // actually runs for cargo trucks (this is the "must patch the declaring/overriding
            // type" rule from earlier in this project, not a new lesson) - so it needs its own
            // registration, reusing the same Car wrapper (it already resolves each vehicle's own
            // StartPathFind by its actual runtime type).
            TryPatchFlexibleReroute(harmony, typeof(CargoTruckAI), typeof(FlexibleReroutePatch.Car));
            TryPatchFireResponseCap(harmony, typeof(FireTruckAI), typeof(FireResponseCapPatch.Truck));
            TryPatchFireResponseCap(harmony, typeof(FireCopterAI), typeof(FireResponseCapPatch.Copter));
            TryPatchTrainSpawnThrottle(harmony);
            TryPatchCitizenCarProbability(harmony);
            TryPatchCitizenTaxiProbability(harmony);
            TryPatchHelicopterWeatherHalt(harmony);
            TryPatchRaceCarSpeed(harmony);
            TryPatchTrainPassengerCapacity(harmony);
            TryPatchRaceBuildingAttractiveness(harmony);
            TryPatchPassengerHelicopterGateAssignment(harmony);
            TryPatchFlexibleReroute(harmony, typeof(PassengerHelicopterAI), typeof(FlexibleReroutePatch.PassengerHelicopter));
            TryPatchPassengerHelicopterCapacity(harmony);
            TryPatchIntercityBusCapacity(harmony);
            TryPatchTransitStationSkip(harmony, typeof(BusAI), typeof(TransitStationSkipPatch.Bus));
            TryPatchTransitStationSkip(harmony, typeof(PassengerHelicopterAI), typeof(TransitStationSkipPatch.PassengerHelicopter));
            TryPatchTransitStationSkip(harmony, typeof(PassengerTrainAI), typeof(TransitStationSkipPatch.Metro));
            TryPatchThunderstormFacilityShutdown(
                harmony, typeof(TransportStationAI), typeof(ThunderstormFacilityShutdownPatch.Transport));
            TryPatchThunderstormFacilityShutdown(
                harmony, typeof(HelicopterDepotAI), typeof(ThunderstormFacilityShutdownPatch.HelicopterDepot));
            TryPatchTrainSingleTrackConflictDetector(harmony);

            Debug.Log("[AIImprove] Harmony patches applied.");
        }

        // Boosts intercity bus capacity - see IntercityBusCapacityPatch.cs.
        private static bool TryPatchIntercityBusCapacity(Harmony harmony)
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
                        "Skipping intercity bus capacity patch.");
                    return false;
                }

                MethodInfo prefix = typeof(IntercityBusCapacityPatch).GetMethod(
                    nameof(IntercityBusCapacityPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Intercity bus capacity patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Intercity bus capacity patch failed to apply, skipping it. Rest " +
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

        // Boosts passenger helicopter capacity - see PassengerHelicopterCapacityPatch.cs.
        private static bool TryPatchPassengerHelicopterCapacity(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(PassengerHelicopterAI),
                    "CreateVehicle",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] PassengerHelicopterAI.CreateVehicle not found - game version " +
                        "may have changed. Skipping passenger helicopter capacity patch.");
                    return false;
                }

                MethodInfo prefix = typeof(PassengerHelicopterCapacityPatch).GetMethod(
                    nameof(PassengerHelicopterCapacityPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Passenger helicopter capacity patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Passenger helicopter capacity patch failed to apply, skipping it. " +
                    "Rest of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

        // Boosts intercity/regional train passenger capacity - see
        // TrainPassengerCapacityPatch.cs. CreateVehicle is public, but PassengerTrainAI is the
        // declaring/overriding type (MetroTrainAI has its own separate override), so this must
        // patch PassengerTrainAI specifically to naturally exclude metro.
        private static bool TryPatchTrainPassengerCapacity(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(PassengerTrainAI),
                    "CreateVehicle",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] PassengerTrainAI.CreateVehicle not found - game version may " +
                        "have changed. Skipping train passenger capacity patch.");
                    return false;
                }

                MethodInfo prefix = typeof(TrainPassengerCapacityPatch).GetMethod(
                    nameof(TrainPassengerCapacityPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Train passenger capacity patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Train passenger capacity patch failed to apply, skipping it. Rest " +
                    "of the mod is unaffected. Reason: " + ex.Message);
                return false;
            }
        }

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

        // Removes each racer's individual top-speed cap - see RaceCarSpeedPatch.cs.
        // CalculateTargetSpeed is protected, hence BindingFlags.NonPublic.
        private static bool TryPatchRaceCarSpeed(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(RaceCarAI),
                    "CalculateTargetSpeed",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(float), typeof(float) });

                if (original == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] RaceCarAI.CalculateTargetSpeed not found - game version may " +
                        "have changed. Skipping race car speed patch.");
                    return false;
                }

                MethodInfo prefix = typeof(RaceCarSpeedPatch).GetMethod(
                    nameof(RaceCarSpeedPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                Debug.Log("[AIImprove] Race car speed patch applied.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[AIImprove] Race car speed patch failed to apply, skipping it. Rest of the " +
                    "mod is unaffected. Reason: " + ex.Message);
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
