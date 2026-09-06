using System.Collections.Generic;
using ColossalFramework;

namespace AIImprove
{
    // Player report (2026-09-07): "現在的城際巴士及城際火車生成率可見是低到接近於零".
    //
    // The half of that report about buses cannot be explained by anything this mod does - there
    // is no bus spawn throttle here, only a train one. And the previous session's log carries a
    // stronger signal than the spawn counters do: FlexibleReroutePatch logs "is executing" once
    // per vehicle-AI label the first time it sees one, and across the whole session it logged
    // BusAI(Local) and never BusAI(Intercity). Not "few intercity buses" - not one existed.
    //
    // Before writing any more code aimed at spawn rates, establish whether the city has the
    // infrastructure for those spawns at all. An intercity bus or train only appears if there is
    // a station of that kind AND an outside connection of the matching transport type; if either
    // is missing, a spawn rate of zero is correct behaviour and every fix aimed at throttling is
    // aimed at the wrong thing.
    //
    // Runs once per level load, on the simulation thread, and prints a handful of lines. See
    // 12 - 開發準則, 準則 4: measure before reasoning, and 準則 10: one load should answer as many
    // questions as it can.
    internal static class IntercityServiceInventory
    {
        public static void OnLevelLoaded(SimulationManager.UpdateMode mode)
        {
            Singleton<SimulationManager>.instance.AddAction(Report);
        }

        private static void Report()
        {
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            Dictionary<string, int> tally = new Dictionary<string, int>();

            for (int i = 0; i < buildings.Length; i++)
            {
                if ((buildings[i].m_flags & Building.Flags.Created) == Building.Flags.None)
                {
                    continue;
                }

                BuildingInfo info = buildings[i].Info;
                if (info == null || info.m_buildingAI == null)
                {
                    continue;
                }

                bool isStation = info.m_buildingAI is TransportStationAI;
                bool isOutsideConnection = info.m_buildingAI is OutsideConnectionAI;
                if (!isStation && !isOutsideConnection)
                {
                    continue;
                }

                string intercity = info.m_class != null && TransportStationAI.IsIntercity(info.m_class)
                    ? "intercity"
                    : "local";
                string subService = info.m_class != null ? info.m_class.m_subService.ToString() : "none";

                string key = info.m_buildingAI.GetType().Name + " / " + subService + " / " + intercity;

                int count;
                tally.TryGetValue(key, out count);
                tally[key] = count + 1;
            }

            if (tally.Count == 0)
            {
                Log.Info(
                    "[AIImprove] Intercity service inventory: no transport stations and no outside " +
                    "connections found in this city.");
                return;
            }

            foreach (KeyValuePair<string, int> entry in tally)
            {
                Log.Info("[AIImprove] Intercity service inventory: " + entry.Value + " x " + entry.Key);
            }
        }
    }
}
