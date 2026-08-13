using ColossalFramework;
using UnityEngine;

namespace AIImprove
{
    // "賽車的 AI 我想他的駕駛速度為無上限" (2026-08-13): removes each racer's individual top-speed
    // cap. dnSpy showed RaceCarAI.CalculateTargetSpeed computes a curve/turning-based cornering
    // speed (num, from the car's RacerData.m_turning) and then clamps it with
    // Mathf.Min(num, racerData.m_maxSpeed) - that clamp is the actual "top speed" stat being
    // removed here. The cornering math itself is left untouched, so cars still slow realistically
    // for turns; only the flat ceiling on straights (and anywhere the corner math would otherwise
    // allow more) is lifted.
    //
    // RacerData is a class (reference type), not a struct - eventData.m_raceEventData.m_racerData
    // returns a live reference to the same object CalculateTargetSpeed itself reads moments later,
    // so writing m_maxSpeed here (a Prefix, before the original runs) is enough; no need to
    // duplicate the cornering formula or otherwise touch the original method's logic.
    internal static class RaceCarSpeedPatch
    {
        // Not float.MaxValue - kept merely "unreasonably high for this game" rather than the
        // literal float ceiling, to avoid any risk of overflow/NaN in downstream squaring
        // (CalculateMaxSpeed's braking-distance math uses targetSpeed^2) or oscillation math.
        private const float UnlimitedMaxSpeed = 100000f;

        private static bool loggedFirstCall;

        public static void Prefix(ushort vehicleID, ref Vehicle data)
        {
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

            racerData.m_maxSpeed = UnlimitedMaxSpeed;
        }
    }
}
