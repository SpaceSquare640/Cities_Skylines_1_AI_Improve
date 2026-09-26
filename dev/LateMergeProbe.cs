using ColossalFramework;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AIImprove.Dev
{
    // DEVELOPMENT ONLY. NOT COMPILED INTO THE RELEASED DLL. See dev/DevTriggerPanel.cs for how the
    // exclusion works and why it is airtight.
    //
    // THIS DELIBERATELY CORRUPTS VEHICLE PATHS. That is the entire point, and it is why it lives
    // here and can never ship. It is an experiment, not a feature.
    //
    // WHAT QUESTION IT ANSWERS. Late merge - a vehicle using the faster lane along an approach and
    // merging into its turn lane near the junction, which is what real drivers do and what neither
    // vanilla nor TMPE's Dynamic Lane Selection will do - requires a path to say "this segment, in
    // lane 1 up to the halfway point, then lane 0". A PathUnit.Position is (segment, lane, offset),
    // so expressing that means INSERTING an extra position on the same segment, not rewriting one.
    //
    // TMPE's UpdatePathTargetPositionsPatch proves that rewriting an existing position's lane works
    // (`nextPosition.m_lane = (byte)bestLaneIndex; ... SetPosition(...)`). It proves nothing about
    // inserting, because it never inserts. Three things are therefore unknown, and none of them can
    // be answered by reading code - they are questions about how the game's own logic reacts:
    //
    //   1. Do multiple vehicles ever share a PathUnit? m_referenceCount and AddPathReference exist.
    //      If they share, editing one vehicle's path edits another's. TMPE's lane rewrite has the
    //      same exposure and has been fine for years, which is evidence but not proof.
    //   2. Does the game handle two consecutive positions on the SAME segment in different lanes?
    //      This is the shape late merge needs and nothing in the codebase demonstrates it.
    //   3. Does m_lastPathOffset stay coherent when a position is inserted ahead of the vehicle?
    //
    // SO THIS PROBE DOES NOT TRY TO BE USEFUL. It inserts one such position into one vehicle's path
    // and then just watches, reporting what the game did. A result of "the vehicle froze" is a
    // successful run - it answers question 2 with a no.
    //
    // SAFETY, because this edits live simulation state:
    //   - One vehicle under test at a time, never two.
    //   - Hard cap on experiments per session (MaxExperiments), so a bad interaction cannot
    //     compound across a whole city.
    //   - Only when the unit has a free position slot - no reallocating or re-chaining PathUnits,
    //     which keeps the most dangerous operation out of the experiment entirely.
    //   - Records the original position and restores it if the vehicle looks wedged.
    public sealed class LateMergeProbe
    {
        private const int MaxExperiments = 20;

        // How long to watch one vehicle before writing the verdict, in simulation frames.
        private const uint ObservationFrames = 512U;

        // A PathUnit holds twelve positions (m_position00 .. m_position11). Insertion shifts the
        // tail along by one, so there has to be a free slot; when there is not, the vehicle is
        // skipped rather than reallocating the unit.
        private const int PositionsPerUnit = 12;

        private struct Experiment
        {
            public ushort VehicleId;
            public uint PathUnitId;
            public int InsertedIndex;
            public ushort Segment;
            public byte OriginalLane;
            public byte ProbeLane;
            public uint StartFrame;
            public Vector3 StartPosition;
            public byte OriginalPositionCount;
        }

        private static Experiment current;
        private static bool active;
        private static int experimentsRun;
        private static bool patched;

        // Its own Harmony id, separate from the mod's "spacesquare.aiimprove". Keeping them apart
        // means this experiment can never be mistaken for a shipped patch in another mod's conflict
        // report, and it shows up under its own name in Harmony's patch listing.
        private const string ProbeHarmonyId = "spacesquare.aiimprove.devprobe";

        internal static void Install()
        {
            if (patched)
            {
                return;
            }

            Harmony harmony = new Harmony(ProbeHarmonyId);

            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(CarAI),
                    "SimulationStep",
                    new[] { typeof(ushort), typeof(Vehicle).MakeByRefType(), typeof(Vector3) });

                if (original == null)
                {
                    Debug.LogWarning("[AIImprove] DEV probe: CarAI.SimulationStep not found, probe disabled.");
                    return;
                }

                MethodInfo postfix = typeof(LateMergeProbe).GetMethod(
                    nameof(Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                patched = true;

                Debug.Log(
                    "[AIImprove] *** DEV PROBE ARMED *** LateMergeProbe will insert a same-segment " +
                    "different-lane path position into up to " + MaxExperiments + " vehicles and " +
                    "report what the game does. This build edits live vehicle paths on purpose.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AIImprove] DEV probe install failed: " + ex);
            }
        }

        public static void Postfix(ushort vehicleID, ref Vehicle data)
        {
            if (active)
            {
                if (vehicleID == current.VehicleId)
                {
                    Watch(ref data);
                }

                return;
            }

            if (experimentsRun >= MaxExperiments)
            {
                return;
            }

            TryStart(vehicleID, ref data);
        }

        private static void TryStart(ushort vehicleID, ref Vehicle data)
        {
            if (data.m_path == 0U || (data.m_flags & Vehicle.Flags.WaitingPath) != 0)
            {
                return;
            }

            PathManager pathManager = Singleton<PathManager>.instance;
            uint unitId = data.m_path;
            int index = data.m_pathPositionIndex >> 1;

            byte count = pathManager.m_pathUnits.m_buffer[unitId].m_positionCount;
            if (count >= PositionsPerUnit || index < 0 || index >= count)
            {
                return;
            }

            PathUnit.Position position;
            if (!pathManager.m_pathUnits.m_buffer[unitId].GetPosition(index, out position) ||
                position.m_segment == 0)
            {
                return;
            }

            // Needs a same-direction sibling lane to merge out into - which is precisely the lane
            // that is useless to this vehicle under today's rules, and the whole point of late
            // merge.
            byte probeLane;
            if (!TryFindSiblingLane(position.m_segment, position.m_lane, out probeLane))
            {
                return;
            }

            // Insert AFTER the vehicle's current position: shift the tail right by one, then write
            // the probe position into the freed slot. The vehicle's own m_pathPositionIndex is
            // untouched because everything moves behind it, not in front of it.
            for (int i = count - 1; i > index; i--)
            {
                PathUnit.Position moved;
                pathManager.m_pathUnits.m_buffer[unitId].GetPosition(i, out moved);
                pathManager.m_pathUnits.m_buffer[unitId].SetPosition(i + 1, moved);
            }

            PathUnit.Position probe = position;
            probe.m_lane = probeLane;
            probe.m_offset = (byte)(position.m_offset / 2);
            pathManager.m_pathUnits.m_buffer[unitId].SetPosition(index + 1, probe);
            pathManager.m_pathUnits.m_buffer[unitId].m_positionCount = (byte)(count + 1);

            current = new Experiment
            {
                VehicleId = vehicleID,
                PathUnitId = unitId,
                InsertedIndex = index + 1,
                Segment = position.m_segment,
                OriginalLane = position.m_lane,
                ProbeLane = probeLane,
                StartFrame = Singleton<SimulationManager>.instance.m_currentFrameIndex,
                StartPosition = data.GetLastFramePosition(),
                OriginalPositionCount = count,
            };

            active = true;
            experimentsRun++;

            Debug.Log(
                "[AIImprove] DEV probe #" + experimentsRun + " START vehicle " + vehicleID +
                ": segment " + position.m_segment + ", inserted (lane " + probeLane + ", offset " +
                probe.m_offset + ") at index " + (index + 1) + " ahead of the original (lane " +
                position.m_lane + ", offset " + position.m_offset + "). positionCount " + count +
                " -> " + (count + 1) + ".");
        }

        private static bool TryFindSiblingLane(ushort segmentId, byte laneIndex, out byte sibling)
        {
            sibling = 0;
            NetInfo info = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info;
            if (info == null || info.m_lanes == null || laneIndex >= info.m_lanes.Length)
            {
                return false;
            }

            NetInfo.Lane own = info.m_lanes[laneIndex];
            if (own == null || (own.m_laneType & NetInfo.LaneType.Vehicle) == NetInfo.LaneType.None)
            {
                return false;
            }

            for (int i = 0; i < info.m_lanes.Length && i < 255; i++)
            {
                if (i == laneIndex)
                {
                    continue;
                }

                NetInfo.Lane other = info.m_lanes[i];
                if (other != null &&
                    (other.m_laneType & NetInfo.LaneType.Vehicle) != NetInfo.LaneType.None &&
                    other.m_finalDirection == own.m_finalDirection)
                {
                    sibling = (byte)i;
                    return true;
                }
            }

            return false;
        }

        private static void Watch(ref Vehicle data)
        {
            uint frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            if (frame - current.StartFrame < ObservationFrames)
            {
                return;
            }

            PathManager pathManager = Singleton<PathManager>.instance;
            StringBuilder sb = new StringBuilder();
            sb.Append("[AIImprove] DEV probe #").Append(experimentsRun).Append(" RESULT vehicle ")
              .Append(current.VehicleId).Append(" after ").Append(ObservationFrames).Append(" frames:");

            float moved = Vector3.Distance(data.GetLastFramePosition(), current.StartPosition);
            sb.Append("\n  moved ").Append(moved.ToString("F1")).Append("m")
              .Append(moved < 1f ? "  <-- WEDGED? question 2 may be answered NO" : "");

            sb.Append("\n  path now ").Append(data.m_path == 0U ? "RELEASED (repathed or despawned)"
                : "unit " + data.m_path + (data.m_path == current.PathUnitId ? " (same)" : " (DIFFERENT - the game repathed)"));

            sb.Append("\n  flags ").Append(data.m_flags & (Vehicle.Flags.WaitingPath | Vehicle.Flags.Stopped));

            if (data.m_path == current.PathUnitId)
            {
                byte nowCount = pathManager.m_pathUnits.m_buffer[current.PathUnitId].m_positionCount;
                sb.Append("\n  positionCount ").Append(current.OriginalPositionCount).Append(" -> ")
                  .Append(nowCount).Append(nowCount == current.OriginalPositionCount
                      ? "  <-- the game UNDID our insert" : "  (our insert survived)");

                PathUnit.Position at;
                if (pathManager.m_pathUnits.m_buffer[current.PathUnitId].GetPosition(current.InsertedIndex, out at))
                {
                    sb.Append("\n  inserted slot now: segment ").Append(at.m_segment)
                      .Append(", lane ").Append(at.m_lane)
                      .Append(at.m_lane == current.ProbeLane ? " (ours)" : " (rewritten - TMPE or vanilla changed it)");
                }

                sb.Append("\n  vehicle pathPositionIndex ").Append(data.m_pathPositionIndex >> 1)
                  .Append(" (inserted at ").Append(current.InsertedIndex).Append(")");
            }

            sb.Append("\n  ORIGINAL was segment ").Append(current.Segment)
              .Append(" lane ").Append(current.OriginalLane)
              .Append("; probe used sibling lane ").Append(current.ProbeLane).Append('.');

            Debug.Log(sb.ToString());

            active = false;
        }
    }
}
