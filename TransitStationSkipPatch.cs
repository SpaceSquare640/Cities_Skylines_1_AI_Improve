using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AIImprove
{
    // "全局型調整全部大眾運輸交通 AI" (2026-08-14): applies to ordinary (non-intercity) bus,
    // passenger helicopter, and metro. Intercity bus and intercity/regional train are explicitly
    // excluded per request - those already have their own separate tuning (IntercityBusCapacityPatch,
    // TrainPlatformAssignmentPatch/TrainPassengerCapacityPatch/TrainSpawnThrottlePatch) and stay
    // untouched here. "客運飛艇" was also requested, but the only airship-like AI in this game
    // (BlimpAI) is a decorative advertising blimp with no transport line/stop system at all - no
    // passenger-carrying airship exists to hook, so it's skipped (flagged in chat, not silently
    // dropped).
    //
    // Implements two rules on arrival at a stop:
    //   1. "已經有3輛車" - if the stop already has >= 3 OTHER vehicles of this feature currently
    //      assigned to it, fly past it without stopping.
    //   2. "車站沒有乘客" - if nobody boarded there this leg, fly past it too.
    //
    // All three AI classes (BusAI, PassengerHelicopterAI, PassengerTrainAI/MetroTrainAI) have a
    // near-identical private `bool ArriveAtTarget(ushort vehicleID, ref Vehicle data)` that reads
    // data.m_targetBuilding, decides the next stop via TransportLine.GetNextStop, unloads/loads
    // passengers, and reassigns data.m_targetBuilding to the new stop. Rather than reimplementing
    // that ~40-line method to change stop selection, this Postfixes it and - if the stop it just
    // committed to should be skipped - immediately re-invokes the same (Harmony-patched)
    // ArriveAtTarget again via reflection, exactly as if the vehicle had instantly arrived there
    // and left. That recursive call goes through this same Prefix/Postfix pair, so it can chain
    // through several dead/congested stops in one tick until it finds one worth actually stopping
    // at, bounded by MaxSkipDepth. Same reflection-invoke-a-patched-private-method technique as
    // FlexibleReroutePatch's "self StartPathFind" trick elsewhere in this project.
    //
    // "No passengers boarded" is approximated as "data.m_transferSize (current onboard count) did
    // not increase across the whole ArriveAtTarget call" - the call also unloads arriving
    // passengers first, so a stop with heavy alighting and zero boarding could in theory net
    // negative and still read as "no passengers" correctly, but a stop with simultaneous heavy
    // unload AND boarding that happens to net to exactly zero would be misread as empty. Accepted
    // as a v1 approximation; a precise measurement would need a Transpiler between the internal
    // Unload/Load calls, not worth the complexity here.
    internal static class TransitStationSkipPatch
    {
        // "3 vehicles" from the request, read as 3 OTHER vehicles already there before this one.
        private const int MinOtherVehiclesToSkip = 3;

        // Guards against a pathological chain (e.g. an entire line with no waiting citizens)
        // turning into unbounded recursion in a single tick.
        private const int MaxSkipDepth = 8;

        // BUG FOUND VIA LOG (2026-08-14): a plain depth counter isn't enough on a short/shuttle
        // line - live logs showed a 2-stop metro line bouncing 23316 -> 23317 -> 23316 -> 23317
        // forever (every real arrival re-triggered another full 8-hop burst, session-long, zero
        // real service), because GetNextStop cycles right back to a stop already skipped this
        // same burst when the line is short enough. Tracking which stops were already visited in
        // the current burst and stopping (accepting the current stop for real) the moment a
        // repeat is seen fixes this at the root, rather than just capping how far the symptom can
        // run before giving up mid-flight.
        private static readonly Dictionary<ushort, HashSet<ushort>> VisitedThisBurst =
            new Dictionary<ushort, HashSet<ushort>>();

        private static bool loggedFirstCall;

        internal struct ArriveState
        {
            public ushort OldTarget;
            public ushort TransferBefore;
        }

        private static void BeforeArrive(ref Vehicle data, out ArriveState state)
        {
            state.OldTarget = data.m_targetBuilding;
            state.TransferBefore = data.m_transferSize;
        }

        private static void AfterArrive(
            string ownerTypeName,
            bool isNodeSpace,
            MethodInfo arriveAtTarget,
            object aiInstance,
            ushort vehicleID,
            ref Vehicle data,
            ArriveState state)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] TransitStationSkipPatch is executing.");
            }

            if (isNodeSpace)
            {
                TransitStopOccupancyTracker.LeaveNode(state.OldTarget);
            }
            else
            {
                TransitStopOccupancyTracker.LeaveBuilding(state.OldTarget);
            }

            ushort newTarget = data.m_targetBuilding;
            if (newTarget == 0 || newTarget == state.OldTarget)
            {
                // Released (end of trip) or no next stop was assigned this call - nothing to
                // evaluate.
                VisitedThisBurst.Remove(vehicleID);
                return;
            }

            if (isNodeSpace)
            {
                TransitStopOccupancyTracker.EnterNode(newTarget);
            }
            else
            {
                TransitStopOccupancyTracker.EnterBuilding(newTarget);
            }

            HashSet<ushort> visited;
            if (!VisitedThisBurst.TryGetValue(vehicleID, out visited))
            {
                visited = new HashSet<ushort>();
                visited.Add(state.OldTarget);
                VisitedThisBurst[vehicleID] = visited;
            }

            if (visited.Contains(newTarget) || visited.Count >= MaxSkipDepth)
            {
                // Either a short/looping line brought us back to a stop we already flew past
                // this burst, or we've hit the sanity cap - either way, stop skipping and let
                // the vehicle actually dwell here for real instead of chaining forever.
                VisitedThisBurst.Remove(vehicleID);
                return;
            }

            int occupancyIncludingSelf = isNodeSpace
                ? TransitStopOccupancyTracker.GetNodeOccupancy(newTarget)
                : TransitStopOccupancyTracker.GetBuildingOccupancy(newTarget);
            bool congested = (occupancyIncludingSelf - 1) >= MinOtherVehiclesToSkip;
            bool noPassengers = data.m_transferSize <= state.TransferBefore;

            if (!congested && !noPassengers)
            {
                VisitedThisBurst.Remove(vehicleID);
                return;
            }

            visited.Add(newTarget);

            Debug.Log(
                "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " flying past stop " +
                newTarget + " (" +
                (congested
                    ? (occupancyIncludingSelf - 1) + " other vehicles already there"
                    : "no passengers boarded") +
                ") - skipping to the next stop.");

            object[] args = { vehicleID, data };
            arriveAtTarget.Invoke(aiInstance, args);
            data = (Vehicle)args[1];
        }

        internal static class Bus
        {
            private static MethodInfo arriveAtTarget;

            public static void Prefix(ref Vehicle __1, out ArriveState __state)
            {
                BeforeArrive(ref __1, out __state);
            }

            public static void Postfix(BusAI __instance, ushort __0, ref Vehicle __1, ArriveState __state)
            {
                if (TransportStationAI.IsIntercity(__instance.m_info.m_class))
                {
                    return;
                }

                if (arriveAtTarget == null)
                {
                    arriveAtTarget = AccessTools.Method(
                        typeof(BusAI), "ArriveAtTarget", new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });
                }

                AfterArrive(nameof(BusAI), false, arriveAtTarget, __instance, __0, ref __1, __state);
            }
        }

        internal static class PassengerHelicopter
        {
            private static MethodInfo arriveAtTarget;

            public static void Prefix(ref Vehicle __1, out ArriveState __state)
            {
                BeforeArrive(ref __1, out __state);
            }

            public static void Postfix(PassengerHelicopterAI __instance, ushort __0, ref Vehicle __1, ArriveState __state)
            {
                if (arriveAtTarget == null)
                {
                    arriveAtTarget = AccessTools.Method(
                        typeof(PassengerHelicopterAI), "ArriveAtTarget",
                        new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });
                }

                AfterArrive(nameof(PassengerHelicopterAI), true, arriveAtTarget, __instance, __0, ref __1, __state);
            }
        }

        // PassengerTrainAI.ArriveAtTarget is private (not virtual), so this Harmony patch on the
        // declaring type also fires for MetroTrainAI (which inherits it unchanged) as well as
        // ordinary regional/intercity trains (which also use PassengerTrainAI directly, per this
        // project's established terminology - see TrainPassengerCapacityPatch.cs). Since only
        // metro is in scope this time, filter by runtime type here instead of needing a second
        // patch target.
        internal static class Metro
        {
            private static MethodInfo arriveAtTarget;

            public static void Prefix(PassengerTrainAI __instance, ref Vehicle __1, out ArriveState __state)
            {
                if (!(__instance is MetroTrainAI))
                {
                    __state = default(ArriveState);
                    return;
                }

                BeforeArrive(ref __1, out __state);
            }

            public static void Postfix(PassengerTrainAI __instance, ushort __0, ref Vehicle __1, ArriveState __state)
            {
                if (!(__instance is MetroTrainAI))
                {
                    return;
                }

                if (arriveAtTarget == null)
                {
                    arriveAtTarget = AccessTools.Method(
                        typeof(PassengerTrainAI), "ArriveAtTarget",
                        new[] { typeof(ushort), typeof(Vehicle).MakeByRefType() });
                }

                AfterArrive(nameof(MetroTrainAI), true, arriveAtTarget, __instance, __0, ref __1, __state);
            }
        }
    }
}
