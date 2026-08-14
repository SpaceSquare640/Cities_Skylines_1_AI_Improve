using UnityEngine;

namespace AIImprove
{
    // "調整及改進雷暴雨災難期內的全部空中交通運及服務" (2026-08-14): during an active thunderstorm,
    // directly flip the facility to "Not Operating" (Building.Flags.Active cleared) instead of
    // only refusing individual dispatches/path-finds at the vehicle level - the same flag the
    // base game itself uses to show a building as "Not Operating" in the UI (see
    // PlayerBuildingAI.GetLocalizedStatus / ProduceGoods, both keyed off this exact flag).
    // Complements, doesn't replace, the existing per-vehicle refusals in
    // AircraftGateAssignmentPatch and HelicopterWeatherHaltPatch - those still catch already-
    // airborne retargets, and doubled protection is harmless.
    //
    // Scope: airports, heliports (both are TransportStationAI, differentiated by the transport
    // line's vehicle type - there's no separate "AirportAI"/"HeliportAI" class in this game), and
    // the shared emergency-helicopter depot building (HelicopterDepotAI - one class used for all
    // three of police/fire/ambulance helicopters, differentiated only by prefab service at
    // runtime, per HelicopterWeatherHaltPatch's own instance-type check). "交通運飛艇" (passenger
    // blimp) is excluded, same reasoning as TransitStationSkipPatch.cs - BlimpAI has no transport-
    // line/stop system, so there's no passenger airship facility to close.
    //
    // Postfixes each building AI's own SimulationStep(ushort, ref Building, ref Building.Frame) -
    // called every simulation frame per building - so forcing the flag off here runs after
    // vanilla's own Active recomputation and reliably wins every frame for as long as the storm
    // lasts. No explicit "reopen" step is needed: once the storm ends this Postfix simply stops
    // touching the flag, and vanilla's own recompute (already running unconditionally every frame
    // regardless of this patch) restores it on its own very next cycle.
    internal static class ThunderstormFacilityShutdownPatch
    {
        private static bool loggedFirstCall;

        private static bool IsHeliport(TransportStationAI ai)
        {
            TransportInfo primary = ai.GetTransportLineInfo();
            if (primary != null && primary.m_vehicleType == VehicleInfo.VehicleType.Helicopter)
            {
                return true;
            }

            TransportInfo secondary = ai.GetSecondaryTransportLineInfo();
            return secondary != null && secondary.m_vehicleType == VehicleInfo.VehicleType.Helicopter;
        }

        private static bool IsAirport(TransportStationAI ai)
        {
            TransportInfo primary = ai.GetTransportLineInfo();
            return primary != null
                && primary.m_class.m_service == ItemClass.Service.PublicTransport
                && primary.m_class.m_subService == ItemClass.SubService.PublicTransportPlane;
        }

        private static void Shutdown(string ownerTypeName, ushort buildingId, ref Building data)
        {
            if (!WeatherDisasterDetector.IsThunderstormActive())
            {
                return;
            }

            if ((data.m_flags & Building.Flags.Active) == Building.Flags.None)
            {
                // Already showing Not Operating (or never was Active this frame) - nothing to do.
                return;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] ThunderstormFacilityShutdownPatch is executing.");
            }

            data.m_flags &= ~Building.Flags.Active;
            Debug.Log(
                "[AIImprove] " + ownerTypeName + " building " + buildingId +
                " set to Not Operating for thunderstorm.");
        }

        internal static class Transport
        {
            public static void Postfix(TransportStationAI __instance, ushort __0, ref Building __1)
            {
                if (IsAirport(__instance))
                {
                    Shutdown("Airport", __0, ref __1);
                }
                else if (IsHeliport(__instance))
                {
                    Shutdown("Heliport", __0, ref __1);
                }
            }
        }

        internal static class HelicopterDepot
        {
            public static void Postfix(ushort __0, ref Building __1)
            {
                Shutdown("HelicopterDepot", __0, ref __1);
            }
        }
    }
}
