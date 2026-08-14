using System.Collections.Generic;
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
    // lasts.
    //
    // BUG FOUND VIA SCREENSHOT (2026-08-14): the original version of this patch assumed vanilla's
    // own per-frame recompute would restore Active once the storm ended, same as it does for
    // zoned/production buildings. Decompiling HelicopterDepotAI and TransportStationAI showed
    // that assumption was wrong - Building.Flags.Active is only ever WRITTEN in
    // PlayerBuildingAI.ProduceGoods (based on production rate), and depots/transport stations
    // never call that method (they have no "goods production" concept), so nothing in vanilla
    // ever turns it back on once cleared. A user screenshot confirmed the real symptom: a fire
    // helicopter depot stuck permanently on "Not Operating" long after the storm had passed.
    // Fixed by explicitly tracking which buildings this patch closed and restoring Active itself
    // the moment the storm ends, instead of relying on a vanilla behavior that doesn't exist for
    // these building types.
    internal static class ThunderstormFacilityShutdownPatch
    {
        private static readonly HashSet<ushort> ClosedByUs = new HashSet<ushort>();

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
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] ThunderstormFacilityShutdownPatch is executing.");
            }

            if (!WeatherDisasterDetector.IsThunderstormActive())
            {
                // Storm's over (or never started) - reopen anything we personally closed. Must
                // run even if this building's Active flag currently reads as already-set/clear
                // for some other reason, so ClosedByUs never leaks a stale entry.
                if (ClosedByUs.Remove(buildingId))
                {
                    data.m_flags |= Building.Flags.Active;
                    Debug.Log(
                        "[AIImprove] " + ownerTypeName + " building " + buildingId +
                        " reopened - thunderstorm has ended.");
                }

                return;
            }

            if ((data.m_flags & Building.Flags.Active) == Building.Flags.None)
            {
                // Already showing Not Operating (or never was Active this frame) - nothing to do.
                return;
            }

            data.m_flags &= ~Building.Flags.Active;
            ClosedByUs.Add(buildingId);
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
