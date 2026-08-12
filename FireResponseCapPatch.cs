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
    // Only one `ref Vehicle` parameter on this method - safe Prefix shape, same as everything
    // else in this project.
    internal static class FireResponseCapPatch
    {
        private static bool loggedFirstCall;

        private static void Apply(string ownerTypeName, bool isCopter, ushort vehicleID, ref ushort targetBuilding)
        {
            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log("[AIImprove] FireResponseCapPatch is executing.");
            }

            if (targetBuilding == 0)
            {
                FireResponseTracker.TryAssign(isCopter, vehicleID, 0);
                return;
            }

            if (!FireResponseTracker.TryAssign(isCopter, vehicleID, targetBuilding))
            {
                ushort original = targetBuilding;
                ushort alternate = FireResponseTracker.TryFindAlternateBurningBuilding(isCopter, original);

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
            public static void Prefix(ushort vehicleID, ref ushort targetBuilding) =>
                Apply(nameof(FireTruckAI), false, vehicleID, ref targetBuilding);
        }

        internal static class Copter
        {
            public static void Prefix(ushort vehicleID, ref ushort targetBuilding) =>
                Apply(nameof(FireCopterAI), true, vehicleID, ref targetBuilding);
        }
    }
}
