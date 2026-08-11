using System;
using System.Reflection;
using UnityEngine;

namespace AIImprove
{
    // TMPE-only path: reduces the congestion cost penalty TMPE's Advanced Vehicle AI
    // applies to a road segment when the path being calculated belongs to an emergency
    // vehicle (ambulance / fire truck / police responding to a call).
    //
    // Only ever applied when TmpeCompat.IsTmpeLoaded() is true (see Patcher.cs). Vanilla
    // (no-TMPE) behaviour is a separate, not-yet-implemented Transpiler patch on
    // PathFind.ProcessItemCosts - see Cities_Skylines_1_AI_Improve_Document/03.
    internal static class EmergencyVehiclePriorityPatch
    {
        // ExtVehicleType.Emergency = 1 << 5, per TLM/TMPE.API/Traffic/Enums/ExtVehicleType.cs.
        // Compared as a raw int so this file never needs a compile-time reference to TMPE.
        private const int EmergencyFlagValue = 1 << 5;

        // How much of the congestion penalty to keep for emergency vehicles.
        // 0 = ignore congestion entirely, 1 = no change from TMPE's calculated cost.
        private const float EmergencyCongestionRetention = 0.25f;

        private static FieldInfo vehicleTypeFieldCache;
        private static Type cachedQueueItemType;

        public static void Postfix(object ___queueItem_, ref float segmentSelectionCost)
        {
            if (___queueItem_ == null || !IsEmergencyQueueItem(___queueItem_))
            {
                return;
            }

            // segmentSelectionCost is a multiplier where 1f means "no extra cost".
            // Pull it back toward 1f instead of zeroing it out, so emergency vehicles
            // still mildly prefer clearer roads rather than driving blind into gridlock.
            segmentSelectionCost = 1f + (segmentSelectionCost - 1f) * EmergencyCongestionRetention;
        }

        private static bool IsEmergencyQueueItem(object queueItem)
        {
            FieldInfo field = GetVehicleTypeField(queueItem.GetType());
            if (field == null)
            {
                return false;
            }

            object vehicleTypeValue = field.GetValue(queueItem);
            if (vehicleTypeValue == null)
            {
                return false;
            }

            int intValue = Convert.ToInt32(vehicleTypeValue);
            return (intValue & EmergencyFlagValue) != 0;
        }

        private static FieldInfo GetVehicleTypeField(Type queueItemType)
        {
            if (queueItemType != cachedQueueItemType)
            {
                cachedQueueItemType = queueItemType;
                vehicleTypeFieldCache = queueItemType.GetField(
                    "vehicleType",
                    BindingFlags.Public | BindingFlags.Instance);

                if (vehicleTypeFieldCache == null)
                {
                    Debug.LogWarning(
                        "[AIImprove] TMPE PathUnitQueueItem.vehicleType field not found - " +
                        "emergency vehicle priority patch will have no effect. TMPE may have " +
                        "changed its internal layout since this was written.");
                }
            }

            return vehicleTypeFieldCache;
        }
    }
}
