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
    // DISABLED 2026-09-09 - THIS FEATURE WAS ACTIVELY HARMFUL, and the reason is a sentence in
    // this very file that was never checked.
    //
    // The claim was: "m_transferSize is recomputed from scratch at the vehicle's first real stop
    // rather than decremented arithmetically from this seed, so an inaccurate seed cannot
    // accumulate." It was written in August, carried forward when the feature was rewritten on
    // 2026-09-06, and it is FALSE. Decompiling BusAI.LoadPassengers shows it begins with
    //
    //     int num6 = (int)data.m_transferSize;
    //
    // and counts boarding citizens up from there before writing the total back. The seed is
    // carried forward, not replaced - and boarding stops when that running total reaches
    // m_passengerCapacity.
    //
    // So every phantom passenger this feature invented permanently occupied a seat that a real
    // citizen needed, for the whole life of the vehicle. Player screenshots (2026-09-09) show
    // exactly that: intercity buses pulling away at 31/60, 40/60 and 63/150 while several hundred
    // citizens stand at the stop they just left.
    //
    // It cannot be fixed by tuning the number down. A seed of any size steals that many seats,
    // and the phantoms never alight because they were never citizens. A correct version would
    // have to create real citizen units, which is a different feature entirely.
    //
    // THE LESSON, which is the same one this project keeps paying for: the sentence that made
    // this safe was a claim about the game, written without reading the game, and then trusted
    // twice - once when the feature shipped and once when it was rebuilt. dnSpy was available on
    // this machine the whole time.
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
