using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace AIImprove
{
    // "加入更好的市民AI" (2026-08-15): a Prefix on ResidentAI.GetVehicleInfo that skips vanilla's
    // own layered car/bike/taxi/electric-car dice rolls entirely and replaces them with a single
    // weighted pick among 4 user-controlled categories: Walk, Drive, Taxi, Transit.
    //
    // dnSpy showed vanilla's GetVehicleInfo does several *independent* Bernoulli rolls (car? bike?
    // then, if car, electric?; if not car, taxi?) which can't be mapped cleanly onto "4 relative
    // weights that must sum to one outcome". So when this feature is on, none of those vanilla
    // sub-rolls run for a citizen this call - one weighted draw picks exactly one of the 4
    // categories, seeded the same way vanilla seeds its own roll (new Randomizer(citizenData.m_citizen))
    // so results stay stable frame-to-frame for the same citizen/trip.
    //
    // What each category actually does:
    // - Drive: requests a plain low-tier residential car. Vanilla's eco/electric-car sub-service
    //   nuance is intentionally dropped here - not exposed as one of the user's 4 requested
    //   categories, and replicating it would mean reaching into ResidentAI's private
    //   GetElectricCarProbability via reflection for no requested benefit.
    // - Taxi: requests a taxi service vehicle, same as vanilla's own taxi outcome.
    // - Walk: sets CitizenInstance.Flags.CannotUseTransport before the pathfind request that
    //   follows this call. That flag already exists in vanilla (used for e.g. evacuation-adjacent
    //   cases) specifically to strip NetInfo.LaneType.PublicTransport from the path search - see
    //   CitizenAI.StartPathFind (dnSpy) - so this is a genuine forced walk-only trip, not a guess.
    // - Transit: returns no vehicle and leaves the flag untouched - the same "no vehicle, transport
    //   allowed" state vanilla itself uses, so the pathfinder is free to route through any bus/
    //   metro/train/other line that makes the trip cheaper than pure walking. This can't be forced
    //   any harder than "allowed" - vanilla has no "must use transit" concept, and first/last-mile
    //   walking to a stop is unavoidable regardless.
    //
    // Bicycles are not one of the 4 requested categories and are not produced by this patch while
    // it's active - a citizen who would have cycled under vanilla now rolls among the 4 categories
    // instead. Cyclist-heavy cities should leave this feature off.
    //
    // Turning this on bypasses CitizenCarProbabilityPatch/CitizenTaxiProbabilityPatch for any
    // citizen this runs for, since GetCarProbability/GetTaxiProbability (which those patches
    // target) are only ever called from inside the vanilla method body this Prefix skips.
    internal static class CitizenTransportModePatch
    {
        private static bool loggedFirstCall;

        public static bool Prefix(
            ushort instanceID, ref CitizenInstance citizenData, bool forceProbability,
            out VehicleInfo trailer, ref VehicleInfo __result)
        {
            trailer = null;

            if (!ModSettings.CitizenTransportModeEnabled.value)
            {
                return true;
            }

            // Forced-probability trips (e.g. a scripted move-in) and borrowed cars are vanilla's
            // own special cases, not ordinary "citizen picks a mode" trips - leave them untouched.
            if (citizenData.m_citizen == 0U || forceProbability ||
                (citizenData.m_flags & CitizenInstance.Flags.BorrowCar) != CitizenInstance.Flags.None)
            {
                return true;
            }

            int walk = Mathf.Max(0, ModSettings.CitizenWalkWeight.value);
            int drive = Mathf.Max(0, ModSettings.CitizenDriveWeight.value);
            int taxi = Mathf.Max(0, ModSettings.CitizenTaxiWeight.value);
            int transit = Mathf.Max(0, ModSettings.CitizenTransitWeight.value);
            int total = walk + drive + taxi + transit;
            if (total <= 0)
            {
                // All 4 weights at 0 - degenerate config, fall back to vanilla rather than divide
                // by zero or produce an undefined outcome.
                return true;
            }

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] CitizenTransportModePatch is executing.");
            }

            Randomizer randomizer = new Randomizer(citizenData.m_citizen);
            int roll = randomizer.Int32((uint)total);

            if (roll < walk)
            {
                citizenData.m_flags |= CitizenInstance.Flags.CannotUseTransport;
                __result = null;
                return false;
            }

            roll -= walk;
            if (roll < drive)
            {
                VehicleInfo carInfo = Singleton<VehicleManager>.instance.GetRandomVehicleInfo(
                    ref randomizer, ItemClass.Service.Residential, ItemClass.SubService.ResidentialLow, ItemClass.Level.Level1);
                if (carInfo != null)
                {
                    __result = carInfo;
                    return false;
                }

                // No residential car prefab available (very unusual) - fall through to vanilla
                // rather than force a walk/transit outcome the roll didn't actually pick.
                return true;
            }

            roll -= drive;
            if (roll < taxi)
            {
                VehicleInfo taxiInfo = Singleton<VehicleManager>.instance.GetRandomVehicleInfo(
                    ref randomizer, ItemClass.Service.PublicTransport, ItemClass.SubService.PublicTransportTaxi, ItemClass.Level.Level1);
                if (taxiInfo != null)
                {
                    __result = taxiInfo;
                    return false;
                }

                return true;
            }

            // Transit: no vehicle, CannotUseTransport left unset - pathfinder may freely route
            // through public transport.
            __result = null;
            return false;
        }
    }
}
