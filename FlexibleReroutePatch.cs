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

            ushort targetBuilding = (vehicleData.m_flags & Vehicle.Flags.GoingBack) != 0
                ? vehicleData.m_sourceBuilding
                : vehicleData.m_targetBuilding;

            if (targetBuilding == 0)
            {
                return;
            }

            float aheadDensity = SegmentCongestionQuery.GetAverageAheadDensity(ref vehicleData);
            if (aheadDensity < 0f || !StuckRerouteTracker.ShouldReroute(vehicleID, aheadDensity))
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
            if (aheadDensity < 0f || !StuckRerouteTracker.ShouldReroute(vehicleID, aheadDensity))
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
        // BusAI doesn't override that exact overload, only the ref-Frame one) and filtered to
        // BusAI instances, further filtered to intercity buses only - per the explicit request
        // scope ("城際巴士"), not ordinary in-city bus routes, which already work fine and have a
        // much higher vehicle count where added reroute-search overhead would matter more.
        internal static class Bus
        {
            private static readonly MethodInfo StartPathFindMethod = FindSelfStartPathFind(typeof(BusAI));

            public static void Postfix(ushort vehicleID, CarAI __instance, ref Vehicle data)
            {
                var busAi = __instance as BusAI;
                if (busAi == null || !TransportStationAI.IsIntercity(busAi.m_info.m_class))
                {
                    return;
                }

                TryRerouteViaSelf(nameof(BusAI), StartPathFindMethod, busAi, vehicleID, ref data);
            }
        }
    }
}
