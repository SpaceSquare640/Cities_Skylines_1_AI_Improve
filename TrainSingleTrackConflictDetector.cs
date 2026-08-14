using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "單線軌道防撞" (2026-08-14, requested alongside a generalized take on the Workshop mod
    // SingleTrainTrackAI). That mod's real value isn't preventing literal collisions - TrainAI
    // already has its own CheckOverlap braking for that (confirmed via dnSpy) - it's preventing
    // two trains from ever committing to opposite ends of the same single shared-direction track
    // at once and meeting nose-to-nose in the middle with neither able to back out.
    //
    // A real fix needs the train to actually STOP AND WAIT before entering a contested segment.
    // Every accessible hook for that turned out unsafe on inspection: PassengerTrainAI's own
    // ArriveAtTarget/StartPathFind call sites all release (despawn) the vehicle when StartPathFind
    // returns false - the same trap AircraftGateAssignmentPatch hit with aircraft (see
    // HoldingPatternManager's "KNOWN UNUSED" notes), except trains don't even have that circling
    // fallback built yet. Doing this properly means manipulating live position/speed fields with
    // real risk of visibly broken trains, so building it blind isn't worth it.
    //
    // Step 1 (this file): detect-and-log only, zero vehicle-behavior changes, zero risk. Answers
    // two open questions before any further investment: (1) does this map even have any lane
    // configured for both-directions train travel on a single shared lane (NetInfo.Direction.Both)
    // - vanilla may not expose this at all without custom track assets, in which case the whole
    // feature has no target and isn't worth building further; (2) if it does exist, how often do
    // two trains actually converge on the same one at once in practice. A real hold/wait mechanism
    // is a follow-up decision once this data comes back from a real play session's log.
    internal static class TrainSingleTrackConflictDetector
    {
        // How far ahead (in path positions) to look for a shared single-track segment - mirrors
        // SegmentCongestionQuery's lookahead approach.
        private const int LookaheadPositions = 6;

        private static readonly Dictionary<ushort, ushort> ApproachingVehicle = new Dictionary<ushort, ushort>();

        private static bool loggedFirstCall;
        private static bool loggedAnySharedSegment;

        public static bool IsSharedSingleTrack(ushort segmentId)
        {
            if (segmentId == 0)
            {
                return false;
            }

            NetInfo info = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info;
            if (info == null || info.m_lanes == null)
            {
                return false;
            }

            for (int i = 0; i < info.m_lanes.Length; i++)
            {
                NetInfo.Lane lane = info.m_lanes[i];
                if ((lane.m_vehicleType & VehicleInfo.VehicleType.Train) != 0 &&
                    lane.m_direction == NetInfo.Direction.Both)
                {
                    return true;
                }
            }

            return false;
        }

        public static void CheckAhead(string ownerTypeName, ushort vehicleID, ref Vehicle vehicleData)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;

                if (CompanionModCompat.IsSingleTrainTrackAiLoaded())
                {
                    Debug.Log(
                        "[AIImprove] SingleTrainTrackAI detected - it already handles single-track " +
                        "reservation directly (redirects TrainAI.UpdatePathTargetPositions), so " +
                        "TrainSingleTrackConflictDetector is staying passive/silent to avoid " +
                        "confusing duplicate logging. Not touching its behavior.");
                }
                else
                {
                    Debug.Log("[AIImprove] TrainSingleTrackConflictDetector is executing (detect-and-log only, no behavior change yet).");
                }
            }

            if (CompanionModCompat.IsSingleTrainTrackAiLoaded())
            {
                return;
            }

            if (vehicleData.m_path == 0U)
            {
                return;
            }

            uint unitId = vehicleData.m_path;
            int index = vehicleData.m_pathPositionIndex >> 1;

            for (int i = 0; i < LookaheadPositions; i++)
            {
                PathUnit.Position position;
                bool invalid;
                if (!PathUnit.GetNextPosition(ref unitId, ref index, out position, out invalid) || invalid)
                {
                    return;
                }

                if (position.m_segment == 0 || !IsSharedSingleTrack(position.m_segment))
                {
                    continue;
                }

                if (!loggedAnySharedSegment)
                {
                    loggedAnySharedSegment = true;
                    Debug.Log(
                        "[AIImprove] Found this map's first shared (both-direction, single-lane) " +
                        "train segment: " + position.m_segment + ".");
                }

                ushort otherVehicle;
                if (ApproachingVehicle.TryGetValue(position.m_segment, out otherVehicle) && otherVehicle != vehicleID)
                {
                    Debug.Log(
                        "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " and vehicle " +
                        otherVehicle + " are both approaching shared single-track segment " +
                        position.m_segment + " - this is the head-on-deadlock scenario a real " +
                        "hold/wait mechanism would need to prevent.");
                }

                ApproachingVehicle[position.m_segment] = vehicleID;
                return;
            }
        }

        internal static class Train
        {
            public static void Postfix(ushort __0, ref Vehicle __1) => CheckAhead(nameof(TrainAI), __0, ref __1);
        }
    }
}
