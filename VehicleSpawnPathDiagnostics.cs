using System.Collections.Generic;
using ColossalFramework;

namespace AIImprove
{
    // Where do intercity trains and buses actually come from?
    //
    // OPEN QUESTION (2026-09-07): TrainSpawnThrottlePatch hangs off
    // OutsideConnectionAI.StartTransfer and refuses DummyTrain offers. A 4.7-hour session logged
    // every distinct (reason, destination) pair reaching that method and NOT ONE was DummyTrain -
    // nor DummyCar, DummyShip or DummyPlane. Only citizen and goods transfers came through.
    //
    // That is long enough to stop treating it as bad luck: intercity vehicles are not spawned by
    // the mechanism this project assumed. The throttle has been attached to a path the game does
    // not use for this, which is why "intercity train spawn rate is near zero" could never be
    // explained by it - and why turning the throttle's rules off changed nothing.
    //
    // TransportStationAI.CreateIncomingVehicle / CreateOutgoingVehicle exist in the assembly and
    // are the obvious candidates. Rather than assume a second time, this records what actually
    // calls them: one line per distinct (station AI, transport type, direction). One session then
    // says where a throttle would have to live to work at all.
    //
    // See 12 - 開發準則, 準則 4 (measure before reasoning) and the three occasions this week where
    // a conclusion drawn from too short a sample had to be withdrawn.
    //
    // Remove once the spawn path is known and the throttle has been moved to it.
    internal static class VehicleSpawnPathDiagnostics
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        // "生成率可見是低到接近於零" is a claim about RATE, and recording only that a path was
        // seen cannot answer it - one train an hour and one a minute look identical. Counting per
        // path costs a dictionary increment and turns the same session into a measurement: the
        // log lines carry timestamps, so count over elapsed time gives the rate directly.
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

        private const int ReportEvery = 10;

        // Which buildings the refusals come from. A handful of stations means the cause is local
        // to them; an even spread across every station means it is something city-wide.
        private static readonly Dictionary<ushort, int> FailuresByBuilding = new Dictionary<ushort, int>();

        public static void ResetForNewLevel()
        {
            Reported.Clear();
            Counts.Clear();
            FailuresByBuilding.Clear();
        }

        // __result is the method's own return value. If a future game version makes these void,
        // Harmony refuses the patch and Patcher logs the failure rather than silently degrading.
        public static void RecordIncoming(ushort buildingID, bool __result) =>
            Record(buildingID, __result ? "incoming-ok" : "incoming-FAILED");

        public static void RecordOutgoing(ushort buildingID, bool __result) =>
            Record(buildingID, __result ? "outgoing-ok" : "outgoing-FAILED");

        private static void Record(ushort buildingID, string direction)
        {
            // RELEASE GATE (2026-09-09): investigation diagnostics run at Info so they cannot be
            // missing from the log that matters (12 - 開發準則, 準則 10.3) - but that rule was
            // written for OUR test sessions, where we control the setting. Shipping it to players
            // meant roughly 16,000 lines a session of numbers only we can act on. The periodic
            // reports are behind verbose from here on; the one-off "is executing" and inventory
            // lines stay at Info, because those are what makes a player's bug report usable.
            if (!Log.VerboseEnabled)
            {
                return;
            }

            string ai = "unknown";
            string subService = "unknown";
            string intercity = "unknown";

            if (buildingID != 0)
            {
                BuildingInfo info = Singleton<BuildingManager>.instance.m_buildings.m_buffer[buildingID].Info;
                if (info != null)
                {
                    if (info.m_buildingAI != null)
                    {
                        ai = info.m_buildingAI.GetType().Name;
                    }

                    if (info.m_class != null)
                    {
                        subService = info.m_class.m_subService.ToString();
                        intercity = TransportStationAI.IsIntercity(info.m_class) ? "intercity" : "local";
                    }
                }
            }

            string key = ai + "/" + subService + "/" + intercity + "/" + direction;

            if (direction.EndsWith("FAILED"))
            {
                int failures;
                FailuresByBuilding.TryGetValue(buildingID, out failures);
                FailuresByBuilding[buildingID] = failures + 1;
            }

            int count;
            Counts.TryGetValue(key, out count);
            count++;
            Counts[key] = count;

            if (Reported.Add(key))
            {
                Log.Info("[AIImprove] Vehicle spawn path observed: " + key + ".");
                return;
            }

            if (count % ReportEvery != 0)
            {
                return;
            }

            // WHY THE EXTRA DETAIL (2026-09-08): the ok/FAILED split answered the first question
            // and immediately raised the next one. Intercity trains succeed 5.7% of the time
            // (100 ok against 1660 failed) while every other mode sits between 29% and 70%. The
            // game is not failing to try - it tries constantly and is refused. Two candidate
            // reasons, and these two numbers tell them apart:
            //
            //   a global limit - the vehicle buffer being full would refuse everyone, so the
            //   usage figure says whether that is even plausible;
            //
            //   something local to particular stations - if the failures all come from one or two
            //   buildings, the cause is those stations (no track connection, no reachable
            //   platform), not a city-wide shortage. Hence the per-building tally.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[AIImprove] Vehicle spawn path ").Append(key).Append(": ").Append(count)
              .Append(" so far. Vehicle buffer ")
              .Append(Singleton<VehicleManager>.instance.m_vehicles.ItemCount()).Append('/')
              .Append(Singleton<VehicleManager>.instance.m_vehicles.m_size).Append('.');

            if (key.EndsWith("FAILED"))
            {
                sb.Append(" Failures by building:");
                foreach (KeyValuePair<ushort, int> pair in FailuresByBuilding)
                {
                    sb.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
                }
            }

            Log.Info(sb.ToString());
        }
    }
}
