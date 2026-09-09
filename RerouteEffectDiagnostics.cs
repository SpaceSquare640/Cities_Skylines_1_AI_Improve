using System.Collections.Generic;
using ColossalFramework;

namespace AIImprove
{
    // Does a reroute actually produce a DIFFERENT route?
    //
    // This is the question 14 - 現況總表 (A00) said had to be answered before anything else, and
    // it is the one this project keeps not answering. Every figure collected so far measures
    // whether the path REQUEST was accepted - 469 accepted in one session, 17,377 in another at
    // 99% - and that number says only that a request was queued. It says nothing about whether
    // the pathfinder came back with a different set of segments.
    //
    //   "我們一直在量測「請求成功率」，而不是「路徑是否真的變了」."
    //
    // That was written on 2026-09-06 and then the same mistake was made again on 2026-09-07, when
    // a 99% acceptance rate was reported as evidence the feature now works. It is evidence that
    // the threshold change made the feature FIRE. Whether firing changes anything is this file.
    //
    // HOW IT WORKS: at the moment a reroute is requested, fingerprint the segments still ahead of
    // the vehicle. The pathfinder answers asynchronously, so the comparison happens on a later
    // tick, once the vehicle is off WaitingPath and holds a different path unit. Fingerprinting
    // the same way at that point gives three outcomes worth telling apart:
    //
    //   different  - the reroute produced another route. The feature is doing its job.
    //   identical  - a new path unit holding the same segments. The pathfinder was asked and
    //                answered with the road we were already on, which is the failure mode A00
    //                describes and no existing metric can see.
    //   abandoned  - no new path arrived at all within the grace period.
    //
    // THE FIRST VERSION OF THIS MEASURED WRONG (fixed 2026-09-09, same day, before its numbers
    // were believed). It hashed the next 8 segments from the vehicle's current position in both
    // samples. But the vehicle MOVES while the pathfinder works, so the window slides: an
    // unchanged route sampled one position later hashes differently and was counted as "a
    // different route". The first session reported 59% different - and that figure cannot be
    // distinguished from "the vehicle advanced one segment", which is the ordinary case.
    //
    // A measurement whose bias points at the answer you were hoping for is worth less than no
    // measurement, because it will be believed. This project has already reported a request
    // acceptance rate as if it were an effect; doing it again with a subtly shifted window would
    // have been the same mistake wearing a better disguise.
    //
    // The comparison is now position-independent: keep the actual segment sequence, and treat the
    // new route as UNCHANGED when it is a continuation of the old one - that is, when the new
    // sequence appears inside the old sequence starting at any offset. Only a genuine divergence
    // counts as different.
    //
    // Verbose-gated: this is an investigation, not something a player needs (12 - 開發準則,
    // 準則 10.3 as amended on release day).
    internal static class RerouteEffectDiagnostics
    {
        private const int LookaheadPositions = 8;

        // How long to wait for the pathfinder before calling it abandoned. Path requests normally
        // complete in a handful of frames; this is deliberately generous.
        private const uint GraceFrames = 512U;

        private const int ReportEvery = 50;

        private struct Sample
        {
            public ushort[] Segments;
            public uint PathUnit;
            public uint Frame;
        }

        private static readonly Dictionary<ushort, Sample> Awaiting = new Dictionary<ushort, Sample>();

        private static readonly Dictionary<string, int[]> Results = new Dictionary<string, int[]>();

        public static void ResetForNewLevel()
        {
            Awaiting.Clear();
            Results.Clear();
        }

        public static void ReleaseVehicle(ushort vehicleID)
        {
            Awaiting.Remove(vehicleID);
        }

        // Called immediately before the reroute's StartPathFind call.
        public static void RecordBefore(ushort vehicleID, ref Vehicle vehicleData)
        {
            if (!Log.VerboseEnabled || vehicleData.m_path == 0U)
            {
                return;
            }

            Awaiting[vehicleID] = new Sample
            {
                Segments = ReadSegmentsAhead(ref vehicleData),
                PathUnit = vehicleData.m_path,
                Frame = Singleton<SimulationManager>.instance.m_currentFrameIndex,
            };
        }

        // Called on later ticks for the same vehicle, to see what the pathfinder came back with.
        public static void CheckAfter(string ownerTypeName, ushort vehicleID, ref Vehicle vehicleData)
        {
            if (!Log.VerboseEnabled)
            {
                return;
            }

            Sample before;
            if (!Awaiting.TryGetValue(vehicleID, out before))
            {
                return;
            }

            // Still being computed - not an answer yet.
            if ((vehicleData.m_flags & Vehicle.Flags.WaitingPath) != 0)
            {
                return;
            }

            uint now = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            bool expired = now - before.Frame > GraceFrames;

            if (vehicleData.m_path == 0U || vehicleData.m_path == before.PathUnit)
            {
                if (!expired)
                {
                    return;
                }

                Awaiting.Remove(vehicleID);
                Record(ownerTypeName, 2);
                return;
            }

            Awaiting.Remove(vehicleID);
            Record(ownerTypeName, IsContinuationOf(ReadSegmentsAhead(ref vehicleData), before.Segments) ? 1 : 0);
        }

        private static void Record(string ownerTypeName, int outcome)
        {
            int[] counts;
            if (!Results.TryGetValue(ownerTypeName, out counts))
            {
                counts = new int[3];
                Results[ownerTypeName] = counts;
            }

            counts[outcome]++;

            int total = counts[0] + counts[1] + counts[2];
            if (total % ReportEvery != 0)
            {
                return;
            }

            Log.Info(
                "[AIImprove] Reroute effect (" + ownerTypeName + "): " + total + " completed - " +
                counts[0] + " produced a different route, " + counts[1] +
                " came back with the same segments, " + counts[2] + " never produced a new path.");
        }

        // The segment ids still ahead of the vehicle, in order.
        private static ushort[] ReadSegmentsAhead(ref Vehicle vehicleData)
        {
            uint unitId = vehicleData.m_path;
            int index = vehicleData.m_pathPositionIndex >> 1;
            var segments = new List<ushort>(LookaheadPositions);

            for (int i = 0; i < LookaheadPositions; i++)
            {
                PathUnit.Position position;
                bool invalid;
                if (!PathUnit.GetNextPosition(ref unitId, ref index, out position, out invalid) || invalid)
                {
                    break;
                }

                if (position.m_segment != 0)
                {
                    segments.Add(position.m_segment);
                }
            }

            return segments.ToArray();
        }

        // True when the new route is the old one with some leading segments already travelled -
        // i.e. the pathfinder handed back the road the vehicle was already on. This is what makes
        // the comparison independent of how far the vehicle moved while the path was computed,
        // which the first version of this file got wrong.
        private static bool IsContinuationOf(ushort[] after, ushort[] before)
        {
            if (after.Length == 0 || before.Length == 0)
            {
                // Nothing to compare. Counted as changed rather than unchanged so an empty sample
                // can never manufacture evidence for the conclusion being tested.
                return false;
            }

            for (int offset = 0; offset < before.Length; offset++)
            {
                if (before[offset] != after[0])
                {
                    continue;
                }

                int overlap = System.Math.Min(before.Length - offset, after.Length);
                bool matches = true;
                for (int i = 1; i < overlap; i++)
                {
                    if (before[offset + i] != after[i])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
