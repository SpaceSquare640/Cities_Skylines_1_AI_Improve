using ColossalFramework;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace AIImprove
{
    // Bounded v1 of "flexible mid-route rerouting" for trains/metro (both are TrainAI - metro is
    // just a VehicleInfo.VehicleType.Metro flavor of the same class) and aircraft taxiing - see
    // Cities_Skylines_1_AI_Improve_Document/01, this was the project's original core
    // differentiation thesis ("持續動態調整" vs every existing mod's "一次性最佳化"), deferred
    // from project start for complexity.
    //
    // Mechanism: Postfixes <VehicleAI>.SimulationStep(ushort, ref Vehicle, Vector3) - called
    // every tick, single `ref Vehicle` parameter, same safe shape used throughout this project.
    // When StuckRerouteTracker judges a vehicle has been essentially stationary for too long
    // (and isn't intentionally stopped - see the Flags check below), re-invokes the vehicle's own
    // 6-arg StartPathFind from its CURRENT position (vehicleData.m_targetPos3, which the game
    // itself already uses as "current position" for path recalculation - see VehicleAI's own
    // 2-arg StartPathFind) toward its existing destination, letting the pathfinder pick a
    // different route around whatever is blocking it.
    //
    // StartPathFind is protected, so it can't be called by name from this assembly - reflection
    // via a cached MethodInfo. Only invoked in the rare "actually stuck" branch (gated by
    // StuckRerouteTracker's cooldown), so reflection's per-call overhead is a non-issue - this
    // patch's hot path (the speed check on every tick) never touches reflection at all.
    internal static class FlexibleReroutePatch
    {
        private static readonly System.Collections.Generic.HashSet<string> LoggedFirstCall =
            new System.Collections.Generic.HashSet<string>();

        private static void TryReroute(
            string ownerTypeName,
            MethodInfo startPathFind,
            object aiInstance,
            ushort vehicleID,
            ref Vehicle vehicleData)
        {
            if (startPathFind == null)
            {
                return;
            }

            if (LoggedFirstCall.Add(ownerTypeName))
            {
                Debug.Log("[AIImprove] FlexibleReroutePatch (" + ownerTypeName + ") is executing.");
            }

            // Only vehicles actually in transit toward a real destination. Stations/gates
            // intentionally stop vehicles (Vehicle.Flags.Stopped) - that is not "stuck", and
            // WaitingPath means a path request is already in flight.
            if ((vehicleData.m_flags & (Vehicle.Flags.Stopped | Vehicle.Flags.WaitingPath)) != 0)
            {
                StuckRerouteTracker.Clear(vehicleID);
                return;
            }

            // REVISED (2026-08-13): used to pick source vs target based on Vehicle.Flags.GoingBack,
            // mirroring a pattern borrowed from the emergency-vehicle dispatch code. dnSpy showed
            // that's not how the base VehicleAI.StartPathFind(ushort, ref Vehicle) - which TrainAI
            // doesn't override, and which many vehicle AIs including AircraftAI's own equivalent
            // built-in logic ultimately mirror - actually decides where to go: it unconditionally
            // trusts vehicleData.m_targetBuilding, GoingBack or not. That flag toggling was never
            // the right signal for "where is this vehicle currently headed."
            ushort targetBuilding = vehicleData.m_targetBuilding;

            if (targetBuilding == 0)
            {
                return;
            }

            float aheadDensity = SegmentCongestionQuery.GetAverageAheadDensity(ref vehicleData);
            if (aheadDensity < 0f || !RerouteRateLimiter.TryConsumeBudget() || !StuckRerouteTracker.ShouldReroute(vehicleID, aheadDensity))
            {
                return;
            }

            Vector3 endPos = Singleton<BuildingManager>.instance.m_buildings.m_buffer[targetBuilding].m_position;
            Vector3 startPos = vehicleData.m_targetPos3;

            object[] args = { vehicleID, vehicleData, startPos, endPos, true, true };
            bool success = (bool)startPathFind.Invoke(aiInstance, args);
            vehicleData = (Vehicle)args[1]; // reflection writes ref/out results back into the array

            Debug.Log(
                "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " ahead segment density " +
                aheadDensity.ToString("F0") + " too high, requested reroute from current position: " +
                (success ? "accepted" : "failed"));
        }

        private static MethodInfo FindStartPathFind(Type vehicleAiType)
        {
            MethodInfo method = AccessTools.Method(
                vehicleAiType,
                "StartPathFind",
                new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool) });

            if (method == null)
            {
                Debug.LogWarning(
                    "[AIImprove] " + vehicleAiType.Name + ".StartPathFind(6-arg) not found - game " +
                    "version may have changed. Flexible reroute disabled for " + vehicleAiType.Name + ".");
            }

            return method;
        }

        // BusAI.m_targetBuilding does NOT consistently mean "a Building ID" the way it does for
        // TrainAI/AircraftAI - dnSpy showed BusAI's own StartPathFind(ushort, ref Vehicle) picks
        // between three different interpretations of that field (a real Building when GoingBack
        // or DummyTraffic, but a NetManager node ID for a normal transport-line-following bus,
        // which is what a real intercity bus is). Re-deriving that branching ourselves here would
        // risk resolving the wrong position and rerouting the bus somewhere nonsensical. Instead,
        // this reflectively calls BusAI's own 2-arg StartPathFind(ushort, ref Vehicle) - the exact
        // same convenience method vanilla itself uses to restart pathfinding toward "wherever this
        // vehicle is currently supposed to be going" - so vanilla's own branching handles target
        // resolution and we only ever have to decide *whether* to trigger a reroute, not *where to*.
        private static MethodInfo FindSelfStartPathFind(Type vehicleAiType)
        {
            MethodInfo method = AccessTools.Method(
                vehicleAiType,
                "StartPathFind",
                new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });

            if (method == null)
            {
                Debug.LogWarning(
                    "[AIImprove] " + vehicleAiType.Name + ".StartPathFind(ushort, ref Vehicle) not " +
                    "found - game version may have changed. Flexible reroute disabled for " +
                    vehicleAiType.Name + ".");
            }

            return method;
        }

        private static void TryRerouteViaSelf(
            string ownerTypeName,
            MethodInfo selfStartPathFind,
            object aiInstance,
            ushort vehicleID,
            ref Vehicle vehicleData)
        {
            if (selfStartPathFind == null)
            {
                return;
            }

            if (LoggedFirstCall.Add(ownerTypeName))
            {
                Debug.Log("[AIImprove] FlexibleReroutePatch (" + ownerTypeName + ") is executing.");
            }

            if ((vehicleData.m_flags & (Vehicle.Flags.Stopped | Vehicle.Flags.WaitingPath)) != 0)
            {
                StuckRerouteTracker.Clear(vehicleID);
                return;
            }

            if (vehicleData.m_targetBuilding == 0 && vehicleData.m_sourceBuilding == 0)
            {
                return;
            }

            float aheadDensity = SegmentCongestionQuery.GetAverageAheadDensity(ref vehicleData);
            if (aheadDensity < 0f || !RerouteRateLimiter.TryConsumeBudget() || !StuckRerouteTracker.ShouldReroute(vehicleID, aheadDensity))
            {
                return;
            }

            object[] args = { vehicleID, vehicleData };
            bool success = (bool)selfStartPathFind.Invoke(aiInstance, args);
            vehicleData = (Vehicle)args[1];

            Debug.Log(
                "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " ahead segment density " +
                aheadDensity.ToString("F0") + " too high, requested reroute (via self StartPathFind): " +
                (success ? "accepted" : "failed"));
        }

        internal static class Train
        {
            private static readonly MethodInfo StartPathFindMethod = FindStartPathFind(typeof(TrainAI));

            public static void Postfix(ushort vehicleID, TrainAI __instance, ref Vehicle data)
            {
                TryReroute(nameof(TrainAI), StartPathFindMethod, __instance, vehicleID, ref data);
            }
        }

        internal static class Aircraft
        {
            private static readonly MethodInfo StartPathFindMethod = FindStartPathFind(typeof(AircraftAI));

            public static void Postfix(ushort vehicleID, AircraftAI __instance, ref Vehicle data)
            {
                if (HoldingPatternPatch.TryUpdateHolding(StartPathFindMethod, __instance, vehicleID, ref data))
                {
                    return;
                }

                TryReroute(nameof(AircraftAI), StartPathFindMethod, __instance, vehicleID, ref data);
            }
        }

        // Patched on CarAI (the declaring type of SimulationStep(ushort, ref Vehicle, Vector3) -
        // several subtypes, e.g. BusAI, only override the ref-Frame overload) and covers every
        // ordinary road vehicle: private cars, taxis, cargo trucks, service vehicles, and both
        // in-city and intercity buses. Started out intercity-bus-only (2026-08-12) then widened to
        // "all ordinary city traffic" per explicit request (2026-08-13) - "跟巴士/火車一樣的邏輯"
        // applied to normal CarAI traffic, only excluding emergency vehicles (Ambulance/FireTruck/
        // PoliceCar), which already have their own ignore-costs dispatch handling and are out of
        // scope here.
        //
        // Different concrete CarAI subtypes override StartPathFind(ushort, ref Vehicle)
        // differently (e.g. BusAI's own override branches on GoingBack/DummyTraffic/transport-line
        // in ways a plain CarAI never would - see FindSelfStartPathFind's notes) - so the method to
        // invoke must be resolved per the vehicle's *actual* runtime type, not just typeof(CarAI).
        // Results are cached per type since AccessTools.Method itself isn't especially cheap and
        // this covers every car in the city.
        internal static class Car
        {
            private static readonly System.Collections.Generic.Dictionary<Type, MethodInfo> StartPathFindCache =
                new System.Collections.Generic.Dictionary<Type, MethodInfo>();

            private static MethodInfo GetSelfStartPathFind(Type vehicleAiType)
            {
                MethodInfo method;
                if (!StartPathFindCache.TryGetValue(vehicleAiType, out method))
                {
                    method = FindSelfStartPathFind(vehicleAiType);
                    StartPathFindCache[vehicleAiType] = method; // cache null too - avoid re-resolving every call
                }

                return method;
            }

            public static void Postfix(ushort vehicleID, CarAI __instance, ref Vehicle data)
            {
                if (__instance is AmbulanceAI || __instance is FireTruckAI || __instance is PoliceCarAI)
                {
                    return;
                }

                Type actualType = __instance.GetType();
                MethodInfo startPathFind = GetSelfStartPathFind(actualType);
                TryRerouteViaSelf(actualType.Name, startPathFind, __instance, vehicleID, ref data);
            }
        }
    }
}
