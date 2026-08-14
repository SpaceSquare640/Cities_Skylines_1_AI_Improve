using System;
using System.Reflection;

namespace AIImprove
{
    // Soft-dependency detection for Workshop mods this project was asked to consider
    // "integrating" (2026-08-14) - see 07 - 開發路線圖與里程碑.md for the investigation. Their
    // real mechanisms turned out to be mature, complex, and already solve their respective
    // problems: SingleTrainTrackAI's ReservationManager fully redirects
    // TrainAI.UpdatePathTargetPositions to implement single-track reservation (confirmed via
    // decompiling the actual mod), and Reversible Tram AI Harmony-patches the same method for
    // trams. Reimplementing either ourselves would be redundant at best - SingleTrainTrackAI's
    // own Workshop page explicitly says it's incompatible with any other mod that touches
    // UpdatePathTargetPositions. Optional/soft dependency here means: detect them at runtime and
    // defer/stay quiet, not duplicate their work. Same reflection-only, no-compile-time-reference
    // approach as TmpeCompat.cs - builds and runs fine whether or not either is installed.
    internal static class CompanionModCompat
    {
        private const string SingleTrainTrackAiTypeName = "SingleTrackAI.SingleTrainTrackAI";
        private const string ReversibleTramAiTypeName = "ReversibleTramAI.Mod";

        // BUG FOUND VIA SCREENSHOT (2026-08-14): a user with Advanced Vehicle Options installed
        // had a train showing 31968 passenger capacity. AVO lets players set an explicit custom
        // capacity per vehicle asset; our own capacity-boost patches (TrainPassengerCapacityPatch,
        // IntercityBusCapacityPatch, PassengerHelicopterCapacityPatch) unconditionally multiply
        // whatever m_passengerCapacity they find by a fixed factor, with no awareness that the
        // "original" value they captured might already be an intentional custom number from AVO
        // rather than the vanilla default - the two stack multiplicatively (e.g. AVO's own 15984
        // -> our x2 -> 31968). The fix isn't a bug in our multiplier itself (it's a clean, stable
        // 2x every time - confirmed via log), it's that we shouldn't be doubling a number the
        // player already explicitly chose. When AVO is present, defer to it entirely instead of
        // stacking on top - same "detect and stay passive" philosophy as SingleTrainTrackAI/
        // Reversible Tram AI above.
        private const string AdvancedVehicleOptionsTypeName = "AdvancedVehicleOptionsUID.AdvancedVehicleOptionsLoader";

        public static bool IsSingleTrainTrackAiLoaded() => FindType(SingleTrainTrackAiTypeName) != null;

        public static bool IsReversibleTramAiLoaded() => FindType(ReversibleTramAiTypeName) != null;

        public static bool IsAdvancedVehicleOptionsLoaded() => FindType(AdvancedVehicleOptionsTypeName) != null;

        private static Type FindType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
