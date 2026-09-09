using ColossalFramework.Math;
using UnityEngine;

namespace AIImprove
{
    // "已載客量" - how full an intercity bus already is when it arrives from outside the city.
    // NOT its seat capacity.
    //
    // CORRECTION (2026-09-06, user): "載客量有分為 總載客量 / 已載客量... 我之前一直想改的是
    // 已載客量". This patch previously did both, and worse, derived the one that was wanted from
    // the one that was not: it doubled BusAI.m_passengerCapacity and then seeded m_transferSize
    // as a fraction of the DOUBLED value. So asking for "buses arrive fuller" silently also made
    // every intercity bus a double-size vehicle, and the boarded figure the player saw was a
    // percentage of a number this mod had invented.
    //
    // Two separate consequences of getting this wrong, both now gone:
    //   - m_passengerCapacity lives on the shared VehicleInfo AI instance, so writing it damaged
    //     the prefab for the rest of the session (that was Medium #5, and it needed a whole
    //     restore mechanism to be safe). Not writing it at all is the better fix: there is
    //     nothing to restore.
    //   - it also silently overrode whatever capacity the player had set in Advanced Vehicle
    //     Options, which is why this patch used to bow out entirely when AVO was installed.
    //
    // Now the only thing touched is m_transferSize on the vehicle being created, expressed as a
    // percentage of that vehicle's REAL capacity - whether that capacity is vanilla's or one the
    // player set in Advanced Vehicle Options. Composing with AVO instead of fighting it means the
    // AVO check is no longer needed either.
    //
    // DISABLED 2026-09-09, pending evidence - and the first explanation given for disabling it
    // was WRONG. Both halves are recorded here because the second mistake was made an hour after
    // warning about the first.
    //
    // WHAT HAPPENED: a player reported intercity buses pulling away at 31/60, 40/60 and 63/150
    // while several hundred citizens stood at the stop. This feature was enabled in that session.
    //
    // THE FIRST (WRONG) EXPLANATION: that BusAI.LoadPassengers counts up from the existing
    // m_transferSize, so every phantom passenger seeded here permanently occupied a real seat.
    // The first half is true - LoadPassengers does begin with `int num6 = (int)data.m_transferSize;`
    // - but the conclusion does not follow, because reading only that method left out where the
    // value comes from. BusAI.ArriveAtTarget calls UnloadPassengers BEFORE LoadPassengers, and
    // unloading ends in BusAI.TransportArriveAtTarget with
    //
    //     data.m_transferSize = (ushort)num;
    //
    // where num is counted from scratch by walking the vehicle's citizen units. The seed is
    // therefore wiped at the vehicle's first stop. It cannot accumulate, and it cannot steal a
    // seat beyond the first leg.
    //
    // So the August claim this file used to carry - "recomputed from scratch at the vehicle's
    // first real stop" - was substantially RIGHT, and today's correction of it was the actual
    // error. Two methods were read where three were needed.
    //
    // WHERE THAT LEAVES THE FEATURE: with no demonstrated mechanism linking it to the player's
    // screenshots. It stays off, but as an unverified suspicion rather than a proven cause - it
    // was enabled when the problem appeared, it has never been validated in a real session, and
    // it belongs to the Experimental set where "we have never watched this work" is reason enough
    // to leave it off. If it is ever revisited, the open question is what a phantom m_transferSize
    // does during the first leg, before any stop clears it.
    internal static class IntercityBusPreloadPatch
    {
        // The player's slider is the upper bound. The actual figure is randomized between half of
        // it and it, seeded on the vehicle ID, so a line of arriving buses is not uniformly full -
        // same variety vanilla produces, just centred where the player asked.
        private static int MaxFillPercent => ModSettings.IntercityBusPreloadPercent.value;

        private static bool loggedFirstCall;

        public static void Prefix(BusAI __instance, ushort vehicleID, ref Vehicle data)
        {
            // Hard off. Not gated on the setting: a player who turned it on already has true
            // saved in AIImprove.cgs, and this actively damages transit while it runs.
            return;

#pragma warning disable 162
            if (!TransportStationAI.IsIntercity(__instance.m_info.m_class) ||
                !ModSettings.IntercityBusPreloadEnabled.value)
            {
                return;
            }

            int capacity = __instance.m_passengerCapacity;
            if (capacity <= 0)
            {
                return;
            }

            int maxBoarded = Mathf.Clamp(
                Mathf.RoundToInt(capacity * (MaxFillPercent / 100f)), 0, capacity);
            int minBoarded = maxBoarded >> 1;

            data.m_transferSize = (ushort)new Randomizer(vehicleID).Int32(minBoarded, maxBoarded);

            if (!loggedFirstCall)
            {
                loggedFirstCall = true;
                Debug.Log(
                    "[AIImprove] IntercityBusPreloadPatch is executing (capacity " + capacity +
                    " untouched, boarded on arrival " + data.m_transferSize + " of a permitted " +
                    minBoarded + "-" + maxBoarded + ").");
            }
        }
    }
}
