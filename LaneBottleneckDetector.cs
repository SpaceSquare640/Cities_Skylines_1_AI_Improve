using ColossalFramework;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AIImprove
{
    // Finds junctions where the bottleneck is LANE CONNECTIVITY, not lane choice.
    //
    // THE OBSERVATION THIS EXISTS FOR (2026-09-26). A player reported a roundabout where traffic
    // queued solid on the approaches while an adjacent lane sat empty. The obvious reading is "the
    // AI is not using the free lane", and that is what we assumed at first. It was wrong twice
    // over:
    //
    //   1. Turning TMPE's Advanced Vehicle AI on with Dynamic Lane Selection at 100% changed
    //      nothing. TMPE's VehicleBehaviorManager.FindBestLane re-evaluates lanes every simulation
    //      step using live lane speeds, so if a smarter lane choice existed it would have been
    //      taken.
    //   2. Checking the lane arrows explained why: the empty lane led to a DIFFERENT EXIT. It was
    //      not an unused lane, it was a dead end for those vehicles.
    //
    // So no lane-selection AI can fix that junction - not TMPE's, and not one we could write. The
    // constraint is the road's own lane routing. What is missing is not smarter driving; it is that
    // nothing tells the player WHERE their lane connectivity is the limiting factor. TMPE ships a
    // lane connector tool to fix such spots, but the player has to already know which junction to
    // point it at.
    //
    // That gap is what this fills, and it is a genuinely empty one: vanilla has nothing, TMPE's
    // tools are manual, and Dynamic Lane Selection is by design unable to help here.
    //
    // PHASE 1: DETECT AND LOG ONLY. No behaviour is changed. Same staging ShipQueueDetector used -
    // measure first, decide what to do about it afterwards, and never ship a fix for a shape of
    // problem we have only reasoned about.
    internal static class LaneBottleneckDetector
    {
        // Matches ShipQueueDetector's reporting window.
        private const uint EvaluationIntervalFrames = 512U;

        // A lane must be seen this many times in one window to count as "queueing". These are
        // vehicle-SAMPLES, not distinct vehicles: a car sitting still is sampled on many frames, a
        // car passing through on few. That is the property we want - it measures dwell, which is
        // what a queue is - but it means the numbers are not a headcount, and the log says so.
        private const int BusyLaneMinSamples = 40;

        // ...and the comparison lane must be this quiet. Deliberately not zero: a lane with an
        // occasional car still counts as unused relative to one with forty times the dwell.
        private const int QuietLaneMaxSamples = 4;

        // The busy lane also has to actually be slow. Without this, a high-throughput lane that
        // simply carries more traffic would look like a queue.
        private const float BusyLaneMaxSpeedFraction = 0.3f;

        // WHY SUSTAIN AT ALL. Lane asymmetry is normal and constant - a batch of cars all wanting
        // the same turn produces it every few seconds. Only a condition that survives several
        // windows is structural. Three windows is about 25 seconds of simulation.
        private const int SustainedWindowsRequired = 3;

        // Report each junction once per this many windows after it first qualifies, so a genuinely
        // broken junction does not fill the log for the rest of the session.
        private const int ReportCooldownWindows = 40;

        private struct LaneSample
        {
            public int Samples;
            public float SpeedSum;
        }

        // Key packs segment and lane index together: (segment << 8) | laneIndex. Lane indices are
        // small (NetInfo.m_lanes is a handful of entries) and segment ids are ushort, so this fits
        // a uint with room to spare and avoids allocating a key object per lane per window - net35
        // has no ValueTuple, and a struct key would need an IEqualityComparer to avoid boxing.
        private static readonly Dictionary<uint, LaneSample> Samples = new Dictionary<uint, LaneSample>();

        private static readonly Dictionary<ushort, int> SustainedWindows = new Dictionary<ushort, int>();

        private static readonly Dictionary<ushort, int> ReportCooldown = new Dictionary<ushort, int>();

        // Scratch - cleared at the top of every evaluation, never read outside it. Safe as shared
        // statics because only the simulation thread reaches them (see the threading note in
        // FireResponseCapPatch.cs).
        private static readonly List<ushort> SegmentScratch = new List<ushort>();

        private static uint lastEvaluationFrame;
        private static bool loggedFirstCall;

        public static void ResetForNewLevel()
        {
            Samples.Clear();
            SustainedWindows.Clear();
            ReportCooldown.Clear();
            lastEvaluationFrame = 0U;
            loggedFirstCall = false;
        }

        // Called from FlexibleReroutePatch.Car.Postfix, immediately after the SimulationStagger
        // gate and BEFORE the reroute toggles and the per-vehicle cooldown check.
        //
        // The placement is deliberate and was the first thing to get right: a vehicle that has just
        // rerouted is on a 40-second cooldown and returns early from the reroute path. Sampling
        // after that gate would make queued vehicles - the exact ones this detector is about -
        // invisible for most of the time they spend queueing.
        //
        // KNOWN COUPLING: this piggybacks on the reroute patch's hot path rather than scanning the
        // map, which costs one dictionary write per already-sampled vehicle instead of an O(all
        // segments) sweep, and has the pleasant side effect of only tracking segments that vehicles
        // actually use. The price is that if TryPatchFlexibleReroute fails to apply for CarAI, this
        // detector silently gets no data. Worth knowing before concluding "no bottlenecks found".
        public static void Observe(ushort vehicleID, ref Vehicle vehicleData)
        {
            if (!ModSettings.LaneBottleneckDetectionEnabled.value)
            {
                return;
            }

            if (vehicleData.m_path == 0U)
            {
                return;
            }

            PathUnit.Position position;
            if (!Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicleData.m_path]
                    .GetPosition(vehicleData.m_pathPositionIndex >> 1, out position) ||
                position.m_segment == 0)
            {
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] LaneBottleneckDetector is executing.");
            }

            uint key = ((uint)position.m_segment << 8) | position.m_lane;

            LaneSample sample;
            Samples.TryGetValue(key, out sample);
            sample.Samples++;
            sample.SpeedSum += vehicleData.GetLastFrameVelocity().magnitude;
            Samples[key] = sample;

            uint frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            if (frame - lastEvaluationFrame < EvaluationIntervalFrames)
            {
                return;
            }

            lastEvaluationFrame = frame;
            Evaluate();
        }

        private static void Evaluate()
        {
            SegmentScratch.Clear();

            foreach (KeyValuePair<uint, LaneSample> pair in Samples)
            {
                ushort segmentId = (ushort)(pair.Key >> 8);
                if (!SegmentScratch.Contains(segmentId))
                {
                    SegmentScratch.Add(segmentId);
                }
            }

            for (int i = 0; i < SegmentScratch.Count; i++)
            {
                EvaluateSegment(SegmentScratch[i]);
            }

            // Tick every cooldown down by one window, dropping entries that have expired so the
            // dictionary stays proportional to junctions recently reported rather than every
            // junction ever reported. Keys are collected first because a Dictionary cannot be
            // modified while it is being enumerated, even on a single thread - same idiom as
            // FireResponseCapPatch's sweep.
            SegmentScratch.Clear();
            foreach (KeyValuePair<ushort, int> pair in ReportCooldown)
            {
                SegmentScratch.Add(pair.Key);
            }

            for (int i = 0; i < SegmentScratch.Count; i++)
            {
                ushort key = SegmentScratch[i];
                int remaining = ReportCooldown[key] - 1;
                if (remaining <= 0)
                {
                    ReportCooldown.Remove(key);
                }
                else
                {
                    ReportCooldown[key] = remaining;
                }
            }

            Samples.Clear();
        }

        private static void EvaluateSegment(ushort segmentId)
        {
            NetManager netManager = Singleton<NetManager>.instance;
            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            NetInfo info = segment.Info;
            if (info == null || info.m_lanes == null || info.m_lanes.Length < 2)
            {
                ClearSustained(segmentId);
                return;
            }

            // Busiest and quietest lane WITHIN THE SAME DIRECTION. Comparing a forward lane against
            // an oncoming one would flag every two-way road in the city.
            int busyIndex = -1;
            int quietIndex = -1;
            int busySamples = 0;
            int quietSamples = int.MaxValue;
            float busySpeedSum = 0f;
            NetInfo.Direction busyDirection = NetInfo.Direction.None;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int laneIndex = 0; laneIndex < info.m_lanes.Length && laneIndex < 255; laneIndex++)
                {
                    NetInfo.Lane laneInfo = info.m_lanes[laneIndex];
                    if (laneInfo == null || (laneInfo.m_laneType & NetInfo.LaneType.Vehicle) == NetInfo.LaneType.None)
                    {
                        continue;
                    }

                    if (pass == 1 && laneInfo.m_finalDirection != busyDirection)
                    {
                        continue;
                    }

                    LaneSample sample;
                    Samples.TryGetValue(((uint)segmentId << 8) | (uint)laneIndex, out sample);

                    if (pass == 0)
                    {
                        if (sample.Samples > busySamples)
                        {
                            busySamples = sample.Samples;
                            busyIndex = laneIndex;
                            busySpeedSum = sample.SpeedSum;
                            busyDirection = laneInfo.m_finalDirection;
                        }
                    }
                    else if (laneIndex != busyIndex && sample.Samples < quietSamples)
                    {
                        quietSamples = sample.Samples;
                        quietIndex = laneIndex;
                    }
                }

                if (pass == 0 && busyIndex < 0)
                {
                    ClearSustained(segmentId);
                    return;
                }
            }

            if (quietIndex < 0 || busySamples < BusyLaneMinSamples || quietSamples > QuietLaneMaxSamples)
            {
                ClearSustained(segmentId);
                return;
            }

            float speedLimit = info.m_lanes[busyIndex].m_speedLimit;
            float meanSpeed = busySamples > 0 ? busySpeedSum / busySamples : 0f;
            if (speedLimit <= 0f || meanSpeed > speedLimit * BusyLaneMaxSpeedFraction)
            {
                ClearSustained(segmentId);
                return;
            }

            // THE DECIDING TEST. Two lanes both being busy-and-quiet proves nothing on its own -
            // that is ordinary demand imbalance, and a lane-selection AI would fix it. What makes
            // this a CONNECTIVITY problem is that the quiet lane cannot serve the busy lane's
            // traffic at all, because their turn arrows have nothing in common.
            NetLane.Flags busyArrows = GetArrows(segmentId, busyIndex, ref segment, netManager);
            NetLane.Flags quietArrows = GetArrows(segmentId, quietIndex, ref segment, netManager);

            if (busyArrows == NetLane.Flags.None || quietArrows == NetLane.Flags.None ||
                (busyArrows & quietArrows) != NetLane.Flags.None)
            {
                ClearSustained(segmentId);
                return;
            }

            int windows;
            SustainedWindows.TryGetValue(segmentId, out windows);
            windows++;
            SustainedWindows[segmentId] = windows;

            if (windows < SustainedWindowsRequired || ReportCooldown.ContainsKey(segmentId))
            {
                return;
            }

            ReportCooldown[segmentId] = ReportCooldownWindows;
            Report(segmentId, ref segment, busyIndex, quietIndex, busySamples, quietSamples,
                meanSpeed, speedLimit, busyArrows, quietArrows);
        }

        private static void ClearSustained(ushort segmentId)
        {
            SustainedWindows.Remove(segmentId);
        }

        // NetInfo.m_lanes is indexed by position in the prefab; the lane IDs that carry the live
        // per-lane flags are a linked list off the segment. Walking it is the only mapping between
        // the two, and it is short (a handful of lanes).
        private static NetLane.Flags GetArrows(ushort segmentId, int laneIndex, ref NetSegment segment, NetManager netManager)
        {
            uint laneId = segment.m_lanes;
            for (int i = 0; i < laneIndex && laneId != 0U; i++)
            {
                laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
            }

            if (laneId == 0U)
            {
                return NetLane.Flags.None;
            }

            return (NetLane.Flags)netManager.m_lanes.m_buffer[laneId].m_flags &
                   NetLane.Flags.LeftForwardRight;
        }

        private static void Report(ushort segmentId, ref NetSegment segment, int busyIndex, int quietIndex,
            int busySamples, int quietSamples, float meanSpeed, float speedLimit,
            NetLane.Flags busyArrows, NetLane.Flags quietArrows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[AIImprove] Lane connectivity bottleneck at segment ").Append(segmentId)
              .Append(" (nodes ").Append(segment.m_startNode).Append('/').Append(segment.m_endNode)
              .Append("), sustained ").Append(SustainedWindowsRequired).Append(" windows:");
            sb.Append("\n  lane ").Append(busyIndex).Append(" arrows=").Append(busyArrows)
              .Append(" - ").Append(busySamples).Append(" vehicle-samples, mean speed ")
              .Append(Mathf.RoundToInt(meanSpeed / speedLimit * 100f)).Append("% of limit");
            sb.Append("\n  lane ").Append(quietIndex).Append(" arrows=").Append(quietArrows)
              .Append(" - ").Append(quietSamples).Append(" vehicle-samples");
            sb.Append("\n  The quiet lane's arrows share nothing with the queued lane's, so the ")
              .Append("queue cannot use it. This is a lane ROUTING limit, not a lane CHOICE one - ")
              .Append("no vehicle AI can resolve it. A lane connector or an extra lane serving ")
              .Append(busyArrows).Append(" at this node would.");
            sb.Append("\n  Read as: vehicle-samples measure dwell, not headcount - a stopped car is ")
              .Append("sampled far more often than a moving one. Arrows are the VANILLA lane ")
              .Append("arrows; if you have already used TMPE's lane connector here, check it ")
              .Append("against what the connector actually allows.");

            Log.Info(sb.ToString());
        }
    }
}
