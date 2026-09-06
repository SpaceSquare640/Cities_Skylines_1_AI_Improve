using System.Collections.Generic;
using System.Text;
using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "運河兩岸各排了數十艘船首尾相接、幾乎不動" (2026-09-05, from a player's in-game screenshot).
    // Phase 1: observation only - zero behavior change, no Vehicle/Building field is written.
    // See Cities_Skylines_1_AI_Improve_Document for the investigation notes.
    //
    // Already ruled out as our fault: ShipDockAssignmentPatch.Prefix returns immediately for any
    // ship with m_transportLine != 0, so a ship running a passenger ferry line never enters our
    // code at all. The vanilla mechanism that plausibly produces this shape:
    //   - PassengerShipAI.UpdateBuildingTargetPositions points every ship at the SAME dock
    //     midpoint, so they all converge on one coordinate instead of spreading out.
    //   - ShipAI.CheckOtherVehicle only brakes; it never steers around the ship ahead.
    //   - ShipAI.ReserveSpace does nothing in open water, so nothing arbitrates who goes first.
    //   - dnSpy (ShipAI.SimulationStep, 3-arg overload): despawn requires m_blockCounter == 255,
    //     but the 6-arg overload resets m_blockCounter to 0 whenever the ship moves at all, and a
    //     crawling ship keeps nudging forward - so the counter may never reach 255 and the queue
    //     never clears itself.
    //
    // This detector exists to turn those inferences into evidence before any fix is designed. Two
    // questions it must answer:
    //   (1) Are the queued ships actually on passenger lines? "They look like ferries" is so far
    //       only a read of the screenshot. If cargo ships are mixed in, ShipDockAssignmentPatch
    //       IS involved and the fix changes completely - hence m_transportLine and the concrete
    //       runtime AI type name are both logged, rather than assuming from the class hierarchy.
    //   (2) Does m_blockCounter really plateau below 255? The max seen per dock is reported.
    internal static class ShipQueueDetector
    {
        // Called by TrackerReset when a save is unloaded. Building, vehicle and node IDs are
        // recycled from fixed pools, so anything left here from the previous city would be read
        // back as if it described the new one. Registered centrally rather than relied on being
        // remembered per class - see 12 - 開發準則, 準則 3.
        public static void ResetForNewLevel()
        {
            StuckShips.Clear();
            LastReportedLength.Clear();
            GroupCount.Clear();
            GroupTransportLineCount.Clear();
            GroupMaxBlockCounter.Clear();
            GroupOldestFrame.Clear();
            GroupAiTypes.Clear();
            RemovalScratch.Clear();
        }

        // UNCALIBRATED starting value. Vanilla only despawns at 255; 32 is simply "blocked long
        // enough that this is not ordinary traffic braking". Expect to tune it once real logs come
        // back - it is deliberately a const rather than a setting, because adding a ModSettings
        // entry drags in settings-page layout plus nine language translations for what is a
        // temporary diagnostic.
        private const byte BlockCounterThreshold = 32;

        // A ship that is genuinely stationary. Squared, to avoid the sqrt in magnitude:
        // 0.01 = 0.1 units/frame.
        private const float StoppedSpeedSqr = 0.01f;

        // How often the aggregate is recomputed and (maybe) printed. Deliberately coarse: this
        // project has already been burned once by a per-event diagnostic producing 2540 of 2636
        // log lines (see Log.cs), and a queue of dozens of ships is exactly that shape of hazard.
        private const uint ReportIntervalFrames = 512U;

        private struct StuckShip
        {
            public uint FirstStuckFrame;
            public ushort TargetBuilding;
            public byte MaxBlockCounter;
            public bool OnTransportLine;
            public string AiTypeName;
        }

        // Per-vehicle state. Vehicle IDs are recycled from a fixed pool, so this MUST be cleared on
        // despawn or a new ship inherits a dead one's queue-start timestamp and reports an absurd
        // duration - the exact bug found by audit in StuckRerouteTracker/EmergencyDispatchTracker.
        // Cleanup is wired into AircraftReleasePatch.Postfix (patched on VehicleAI.ReleaseVehicle,
        // so it covers every vehicle type including ships).
        private static readonly Dictionary<ushort, StuckShip> StuckShips = new Dictionary<ushort, StuckShip>();

        // Last queue length printed per dock, so a steady-state queue is not re-printed every
        // report window - only actual changes produce a line.
        private static readonly Dictionary<ushort, int> LastReportedLength = new Dictionary<ushort, int>();

        private static uint lastReportFrame;
        private static bool loggedFirstCall;

        // Scratch buffers, reused across reports rather than reallocated per window.
        private static readonly Dictionary<ushort, int> GroupCount = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, int> GroupTransportLineCount = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, byte> GroupMaxBlockCounter = new Dictionary<ushort, byte>();
        private static readonly Dictionary<ushort, uint> GroupOldestFrame = new Dictionary<ushort, uint>();
        private static readonly Dictionary<ushort, string> GroupAiTypes = new Dictionary<ushort, string>();
        private static readonly List<ushort> RemovalScratch = new List<ushort>();

        // Called from AircraftReleasePatch.Postfix. No-op for vehicle IDs never tracked here.
        public static void ReleaseVehicle(ushort vehicleId)
        {
            StuckShips.Remove(vehicleId);
        }

        // The parameter MUST be named `data`, not `data`: Harmony matches injected
        // parameters to the original method's by name, and vanilla declares
        // ShipAI.SimulationStep(ushort vehicleID, ref Vehicle data, Vector3 physicsLodRefPos).
        // Naming it anything else fails the patch outright at registration - confirmed live
        // (2026-09-05): 'Parameter "data" not found in method virtual System.Void
        // ShipAI::SimulationStep(...)'. The rest of this project already uses `ref Vehicle data`
        // for exactly this reason (see FlexibleReroutePatch's own Postfixes).
        public static void Postfix(ushort vehicleID, ref Vehicle data)
        {
            // Verbose off means this feature costs nothing at all - not even a dictionary lookup.
            // Guarded at the call site per Log.cs's convention.
            if (!Log.VerboseEnabled)
            {
                // Toggled off mid-session: drop whatever was accumulated so a later re-enable
                // starts from a clean state instead of reporting durations spanning the gap.
                if (StuckShips.Count != 0)
                {
                    StuckShips.Clear();
                    LastReportedLength.Clear();
                }

                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                // Health check: "the patch applied" has previously been mistaken for "the logic
                // ran" in this project. This line proves the Postfix is actually executing.
                Log.Info("[AIImprove] ShipQueueDetector is executing.");
            }

            if (!SimulationStagger.ShouldRunThisFrame(vehicleID))
            {
                return;
            }

            uint frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;

            bool blocked = data.m_blockCounter >= BlockCounterThreshold &&
                           data.GetLastFrameVelocity().sqrMagnitude < StoppedSpeedSqr;

            if (!blocked)
            {
                StuckShips.Remove(vehicleID);
            }
            else
            {
                StuckShip entry;
                if (!StuckShips.TryGetValue(vehicleID, out entry))
                {
                    entry = new StuckShip
                    {
                        FirstStuckFrame = frame,
                        MaxBlockCounter = 0,
                    };

                    // Read the concrete runtime AI type rather than inferring from the patched
                    // class - the whole point of question (1) above. Vanilla itself distinguishes
                    // "on a line" by the m_transportLine field, not by AI class, so both are
                    // recorded independently.
                    VehicleInfo info = data.Info;
                    entry.AiTypeName = info != null && info.m_vehicleAI != null
                        ? info.m_vehicleAI.GetType().Name
                        : "unknown";
                }

                entry.TargetBuilding = data.m_targetBuilding;
                entry.OnTransportLine = data.m_transportLine != 0;
                if (data.m_blockCounter > entry.MaxBlockCounter)
                {
                    entry.MaxBlockCounter = data.m_blockCounter;
                }

                StuckShips[vehicleID] = entry;
            }

            // One ship per report window drives the aggregate; every other ship this window falls
            // straight through. Unsigned subtraction so the wrap of m_currentFrameIndex is benign.
            if (frame - lastReportFrame < ReportIntervalFrames)
            {
                return;
            }

            lastReportFrame = frame;
            ReportQueues(frame);
        }

        private static void ReportQueues(uint frame)
        {
            GroupCount.Clear();
            GroupTransportLineCount.Clear();
            GroupMaxBlockCounter.Clear();
            GroupOldestFrame.Clear();
            GroupAiTypes.Clear();

            foreach (KeyValuePair<ushort, StuckShip> pair in StuckShips)
            {
                StuckShip s = pair.Value;
                ushort dock = s.TargetBuilding;

                int count;
                GroupCount.TryGetValue(dock, out count);
                GroupCount[dock] = count + 1;

                if (s.OnTransportLine)
                {
                    int lineCount;
                    GroupTransportLineCount.TryGetValue(dock, out lineCount);
                    GroupTransportLineCount[dock] = lineCount + 1;
                }

                byte maxBlock;
                GroupMaxBlockCounter.TryGetValue(dock, out maxBlock);
                if (s.MaxBlockCounter > maxBlock)
                {
                    GroupMaxBlockCounter[dock] = s.MaxBlockCounter;
                }
                else if (!GroupMaxBlockCounter.ContainsKey(dock))
                {
                    GroupMaxBlockCounter[dock] = s.MaxBlockCounter;
                }

                uint oldest;
                if (!GroupOldestFrame.TryGetValue(dock, out oldest) || s.FirstStuckFrame < oldest)
                {
                    GroupOldestFrame[dock] = s.FirstStuckFrame;
                }

                string types;
                if (!GroupAiTypes.TryGetValue(dock, out types))
                {
                    GroupAiTypes[dock] = s.AiTypeName;
                }
                else if (types.IndexOf(s.AiTypeName) < 0)
                {
                    // Distinct type names only. A mixed list here is the answer to question (1):
                    // cargo AI appearing alongside passenger AI means our dock assignment is in
                    // scope after all.
                    GroupAiTypes[dock] = types + "+" + s.AiTypeName;
                }
            }

            // Drop docks whose queue has fully drained, and emit a one-time "cleared" line so the
            // log shows the queue ending rather than just going silent.
            RemovalScratch.Clear();
            foreach (KeyValuePair<ushort, int> pair in LastReportedLength)
            {
                if (!GroupCount.ContainsKey(pair.Key))
                {
                    RemovalScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < RemovalScratch.Count; i++)
            {
                ushort dock = RemovalScratch[i];
                LastReportedLength.Remove(dock);
                Log.Verbose("[AIImprove] Ship queue at dock " + dock + " cleared.");
            }

            StringBuilder sb = null;

            foreach (KeyValuePair<ushort, int> pair in GroupCount)
            {
                ushort dock = pair.Key;
                int length = pair.Value;

                int previous;
                if (LastReportedLength.TryGetValue(dock, out previous) && previous == length)
                {
                    // Steady state - already reported at this length, stay quiet.
                    continue;
                }

                LastReportedLength[dock] = length;

                int onLine;
                GroupTransportLineCount.TryGetValue(dock, out onLine);
                byte maxBlock;
                GroupMaxBlockCounter.TryGetValue(dock, out maxBlock);
                uint oldestFrame;
                GroupOldestFrame.TryGetValue(dock, out oldestFrame);
                string aiTypes;
                GroupAiTypes.TryGetValue(dock, out aiTypes);

                if (sb == null)
                {
                    sb = new StringBuilder(160);
                }
                else
                {
                    sb.Length = 0;
                }

                sb.Append("[AIImprove] Ship queue at dock ").Append(dock)
                  .Append(": ").Append(length).Append(" stopped ship(s), ")
                  .Append(onLine).Append(" on a transport line, ")
                  .Append(length - onLine).Append(" not; AI type(s) ").Append(aiTypes ?? "unknown")
                  .Append("; longest stuck ").Append(frame - oldestFrame).Append(" frames")
                  .Append("; max m_blockCounter ").Append(maxBlock).Append(" (despawn needs 255).");

                Log.Verbose(sb.ToString());
            }
        }
    }
}
