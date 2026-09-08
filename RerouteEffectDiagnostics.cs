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
    // ACCURACY CAVEAT, stated rather than hidden: the vehicle moves between the two samples, so
    // the "before" fingerprint starts a little further back than the "after" one. A route that is
    // genuinely unchanged can therefore fingerprint as changed if the vehicle crossed a segment
    // boundary in between. That biases the result TOWARDS "different" - which is the safe
    // direction here, because the finding we are testing for is "almost everything comes back
    // identical". A high identical rate cannot be an artefact of this; a high different rate
    // needs a closer look before being believed.
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
            public uint Fingerprint;
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
                Fingerprint = Fingerprint(ref vehicleData),
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
            Record(ownerTypeName, Fingerprint(ref vehicleData) == before.Fingerprint ? 1 : 0);
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

        // FNV-1a over the segment ids still ahead of the vehicle. Order matters: a route that
        // visits the same segments in a different order is a different route.
        private static uint Fingerprint(ref Vehicle vehicleData)
        {
            uint unitId = vehicleData.m_path;
            int index = vehicleData.m_pathPositionIndex >> 1;
            uint hash = 2166136261U;

            for (int i = 0; i < LookaheadPositions; i++)
            {
                PathUnit.Position position;
                bool invalid;
                if (!PathUnit.GetNextPosition(ref unitId, ref index, out position, out invalid) || invalid)
                {
                    break;
                }

                hash = (hash ^ position.m_segment) * 16777619U;
            }

            return hash;
        }
    }
}
