using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "賽車的 AI 我想他的駕駛速度為無上限" (2026-08-13), REVISED (2026-08-14) to "賽車AI固定速度為
    // 120以內", REVISED AGAIN (2026-08-14) to 80 - felt too fast at 120, lowered per user request.
    // Originally removed each racer's individual top-speed cap entirely (set to a very large
    // value); now instead fixes it at a flat ceiling for every racer. dnSpy showed
    // RaceCarAI.CalculateTargetSpeed computes a curve/turning-based cornering speed (num, from the
    // car's RacerData.m_turning) and then clamps it with Mathf.Min(num, racerData.m_maxSpeed) -
    // that clamp is what's being overridden here. The cornering math itself is left untouched, so
    // cars still slow realistically for turns; only the flat top-speed ceiling changes.
    //
    // RacerData is a class (reference type), not a struct - eventData.m_raceEventData.m_racerData
    // returns a live reference to the same object CalculateTargetSpeed itself reads moments later,
    // so writing m_maxSpeed here (a Prefix, before the original runs) is enough; no need to
    // duplicate the cornering formula or otherwise touch the original method's logic.
    internal static class RaceCarSpeedPatch
    {
        // "現在的設定就只有開啟和關閉" (2026-08-15): now reads ModSettings.RaceCarMaxSpeed
        // instead of a fixed constant - see that file for the default (matches the old 80f
        // exactly, so nothing changes until a player moves the slider).
        private static bool loggedFirstCall;

        public static void Prefix(ushort vehicleID, ref Vehicle data)
        {
            if (!ModSettings.RaceCarSpeedEnabled.value)
            {
                return;
            }

            if (data.m_sourceBuilding == 0)
            {
                return;
            }

            Building building = Singleton<BuildingManager>.instance.m_buildings.m_buffer[data.m_sourceBuilding];
            EventData eventData = Singleton<EventManager>.instance.m_events.m_buffer[building.m_eventIndex];
            RaceEventData raceEventData = eventData.m_raceEventData;
            if (raceEventData.m_racerData == null || data.m_racerIndex >= raceEventData.m_racerData.Length)
            {
                return;
            }

            RacerData racerData = raceEventData.m_racerData[data.m_racerIndex];
            if (racerData == null)
            {
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] RaceCarSpeedPatch is executing.");
            }

            racerData.m_maxSpeed = ModSettings.RaceCarMaxSpeed.value;
        }
    }
}
