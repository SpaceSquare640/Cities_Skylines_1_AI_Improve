using UnityEngine;

namespace AIImprove
{
    // Prefixes FireTruckAI/FireCopterAI.SetTarget(ushort, ref Vehicle, ushort) - called every
    // time a truck/helicopter's target building is (re)assigned, including the initial
    // dispatch. If the target building already has FireResponseTracker.MaxRespondersPerBuilding
    // vehicles of this type responding (and hasn't passed the 15-minute uncapped threshold -
    // see FireResponseTracker), the vehicle is force-redirected to another building that is
    // still burning and still has room, per explicit user request ("被擋下的車輛應該強制改派去
    // 「其他」還在燒的建築物，而不是回站待命"). Only falls back to targetBuilding = 0 (idle) if
    // no such alternate building exists.
    //
    // REVISED (2026-08-14): also retargets genuinely IDLE vehicles - i.e. targetBuilding == 0,
    // vanilla's own dispatch logic found no further work for this vehicle - not just ones
    // blocked by the cap. This is the core idea behind the Steam Workshop mod "Smarter
    // Firefighters: Improved AI" (id 2346565561): a truck/helicopter that just finished a job
    // and is heading back to the station, or waiting idle, should check for nearby fires first
    // instead of always accepting idle and waiting for vanilla's own (distance-blind)
    // offer-matching to maybe send it clear across the map next time. This mod already had the
    // building-search infrastructure (TryFindAlternateBurningBuilding) from the cap-overflow
    // case above; the only change needed was calling it here too, now with the vehicle's own
    // position so it genuinely prioritizes *nearby* fires (see FireResponseTracker's notes) - the
    // same core behavior as Smarter Firefighters, but combined with this mod's own 10-responder
    // cap and 15-minute-uncap system, which that mod doesn't have at all.
    //
    // Only one `ref Vehicle` parameter on this method - safe Prefix shape, same as everything
    // else in this project.
    internal static class FireResponseCapPatch
    {
        private static bool loggedFirstCall;

        private static void Apply(string ownerTypeName, bool isCopter, ushort vehicleID, ref Vehicle data, ref ushort targetBuilding)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] FireResponseCapPatch is executing.");
            }

            if (targetBuilding == 0)
            {
                FireResponseTracker.TryAssign(isCopter, vehicleID, 0);

                ushort nearby = FireResponseTracker.TryFindAlternateBurningBuilding(isCopter, 0, data.GetLastFramePosition());
                if (nearby != 0 && FireResponseTracker.TryAssign(isCopter, vehicleID, nearby))
                {
                    Debug.Log(
                        "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " was going idle - " +
                        "retargeted to nearby still-burning building " + nearby + " instead.");
                    targetBuilding = nearby;
                }

                return;
            }

            if (!FireResponseTracker.TryAssign(isCopter, vehicleID, targetBuilding))
            {
                ushort original = targetBuilding;
                ushort alternate = FireResponseTracker.TryFindAlternateBurningBuilding(isCopter, original, data.GetLastFramePosition());

                if (alternate != 0 && FireResponseTracker.TryAssign(isCopter, vehicleID, alternate))
                {
                    Debug.Log(
                        "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " redirected from " +
                        "building " + original + " (at " + FireResponseTracker.MaxRespondersPerBuilding +
                        " responders) to still-burning building " + alternate + ".");
                    targetBuilding = alternate;
                }
                else
                {
                    Debug.Log(
                        "[AIImprove] " + ownerTypeName + " vehicle " + vehicleID + " redirected away " +
                        "from building " + original + " - already at " +
                        FireResponseTracker.MaxRespondersPerBuilding + " responders, no alternate fire found.");
                    targetBuilding = 0;
                }
            }
        }

        internal static class Truck
        {
            public static void Prefix(ushort vehicleID, ref Vehicle data, ref ushort targetBuilding) =>
                Apply(nameof(FireTruckAI), false, vehicleID, ref data, ref targetBuilding);
        }

        internal static class Copter
        {
            public static void Prefix(ushort vehicleID, ref Vehicle data, ref ushort targetBuilding) =>
                Apply(nameof(FireCopterAI), true, vehicleID, ref data, ref targetBuilding);
        }
    }
}
