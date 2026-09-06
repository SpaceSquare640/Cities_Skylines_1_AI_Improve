using ColossalFramework;
using System.Collections.Generic;
using UnityEngine;

namespace AIImprove
{
    // "我想把以前出現的問題重新開始研究" (2026-09-04): instrumentation for the long-standing
    // "91% of reroute attempts fail" observation (2026-08-12: 2431 attempts, 224 accepted, over
    // 17 minutes - see Cities_Skylines_1_AI_Improve_Document/01).
    //
    // That note recorded two conclusions which decompiling TrainAI.StartPathFind(6-arg) has now
    // shown to be wrong, and this file exists to replace the guesswork with measurement:
    //
    //   1. "even a failed attempt is a full pathfinding computation". It is not. The method only
    //      returns true after CreatePath succeeds, and the A* itself runs asynchronously on the
    //      PathFind threads *afterwards*. A false return means one of FindPathPosition(startPos),
    //      FindPathPosition(endPos) or CreatePath failed - so a failure costs at most two spatial
    //      grid searches and never queues a search at all. The expensive case is the 9% that
    //      succeed: those release the old path, set WaitingPath (the vehicle stalls until the
    //      repath lands) and queue a real A*.
    //
    //   2. "the root cause is track topology - many segments simply have no alternative route".
    //      Topology decides whether the A* finds a better route, but the A* never ran. Whatever
    //      is failing happens earlier, during position resolution or path allocation.
    //
    // Rather than guess again, this records the discriminating context at the moment of failure.
    // The leading hypothesis is that startPos is the problem: FlexibleReroutePatch passes
    // vehicleData.m_targetPos3, which is a *lookahead* target rather than where the vehicle
    // actually is, while FindPathPosition snaps within 32m and is given an underground/surface
    // flag derived from Vehicle.Flags.Underground|Transition. A long train, or one near a tunnel
    // portal, can easily have a m_targetPos3 that fails to snap - a mechanism that has nothing to
    // do with topology. Hence the two things measured per failure: how far m_targetPos3 is from
    // the vehicle's real position, and whether it was underground/in transition.
    //
    // Everything here is Verbose-only and callers must guard with `if (Log.VerboseEnabled)` -
    // with verbose off (the default) not even the counters are touched, so ordinary play pays
    // nothing. See Log.cs for why that convention exists.
    internal static class RerouteFailureDiagnostics
    {
        // Called by TrackerReset when a save is unloaded. Building, vehicle and node IDs are
        // recycled from fixed pools, so anything left here from the previous city would be read
        // back as if it described the new one. Registered centrally rather than relied on being
        // remembered per class - see 12 - 開發準則, 準則 3.
        public static void ResetForNewLevel()
        {
            ByOwnerType.Clear();
        }

        // One line per this many failures, per vehicle AI type. Large enough that a long session
        // produces a readable handful of lines rather than a wall of them.
        private const int ReportEveryFailures = 50;

        // FindPathPosition's own snap radius in TrainAI/AircraftAI/ShipAI.StartPathFind. A
        // m_targetPos3 further than this from the vehicle cannot resolve to the vehicle's own
        // lane, and is the specific thing this measurement is here to confirm or rule out.
        private const float SnapRadius = 32f;

        // Above this fraction of the PathUnit pool in use, CreatePath is plausibly the failing
        // step rather than either FindPathPosition. Not a threshold anything acts on - only a
        // label attached to the sample.
        private const float PoolPressureFraction = 0.95f;

        private sealed class Counters
        {
            public int Attempts;
            public int Accepted;
            public int Failed;
            public int FailedUnderPoolPressure;
            public int FailedWhileUndergroundOrTransition;
            public int FailedWithTargetPosBeyondSnapRadius;
            public float FailedTargetPosDistanceSum;
            public float FailedTargetPosDistanceMax;
        }

        private static readonly Dictionary<string, Counters> ByOwnerType =
            new Dictionary<string, Counters>();

        public static void Record(string ownerTypeName, ref Vehicle vehicleData, bool accepted)
        {
            Counters counters;
            if (!ByOwnerType.TryGetValue(ownerTypeName, out counters))
            {
                counters = new Counters();
                ByOwnerType[ownerTypeName] = counters;
            }

            counters.Attempts++;

            if (accepted)
            {
                counters.Accepted++;
                return;
            }

            counters.Failed++;

            // m_targetPos3 is a Vector4 (w carries height//lane offset elsewhere in the game) -
            // only the positional xyz is meaningful for this comparison.
            Vector3 lookaheadTarget = vehicleData.m_targetPos3;
            float distance = Vector3.Distance(lookaheadTarget, vehicleData.GetLastFramePosition());
            counters.FailedTargetPosDistanceSum += distance;
            if (distance > counters.FailedTargetPosDistanceMax)
            {
                counters.FailedTargetPosDistanceMax = distance;
            }

            if (distance > SnapRadius)
            {
                counters.FailedWithTargetPosBeyondSnapRadius++;
            }

            if ((vehicleData.m_flags & (Vehicle.Flags.Underground | Vehicle.Flags.Transition)) != 0)
            {
                counters.FailedWhileUndergroundOrTransition++;
            }

            PathManager pathManager = Singleton<PathManager>.instance;
            int capacity = pathManager.m_pathUnits.m_buffer.Length;
            if (capacity > 0 && pathManager.m_pathUnitCount >= capacity * PoolPressureFraction)
            {
                counters.FailedUnderPoolPressure++;
            }

            if (counters.Failed % ReportEveryFailures == 0)
            {
                Report(ownerTypeName, counters);
            }
        }

        private static void Report(string ownerTypeName, Counters counters)
        {
            float acceptedPercent = counters.Attempts > 0
                ? counters.Accepted * 100f / counters.Attempts
                : 0f;
            float averageDistance = counters.Failed > 0
                ? counters.FailedTargetPosDistanceSum / counters.Failed
                : 0f;

            Log.Verbose(
                "[AIImprove] Reroute stats (" + ownerTypeName + "): " +
                counters.Attempts + " attempts, " + counters.Accepted + " accepted (" +
                acceptedPercent.ToString("F0") + "%), " + counters.Failed + " failed. Of the failures: " +
                counters.FailedWithTargetPosBeyondSnapRadius + " had m_targetPos3 beyond the " +
                SnapRadius.ToString("F0") + "m snap radius (avg " + averageDistance.ToString("F0") +
                "m, max " + counters.FailedTargetPosDistanceMax.ToString("F0") + "m), " +
                counters.FailedWhileUndergroundOrTransition + " were underground/in transition, " +
                counters.FailedUnderPoolPressure + " happened with the PathUnit pool over " +
                (PoolPressureFraction * 100f).ToString("F0") + "% full.");
        }
    }
}
