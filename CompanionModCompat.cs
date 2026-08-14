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

        public static bool IsSingleTrainTrackAiLoaded() => FindType(SingleTrainTrackAiTypeName) != null;

        public static bool IsReversibleTramAiLoaded() => FindType(ReversibleTramAiTypeName) != null;

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
