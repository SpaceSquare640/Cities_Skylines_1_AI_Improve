using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AIImprove
{
    // Soft-dependency detection for TM:PE (Traffic Manager: President Edition).
    // No compile-time reference to TMPE's assemblies exists anywhere in this project -
    // everything TMPE-specific is located and invoked purely via reflection at runtime,
    // so the mod builds and runs fine whether or not TMPE is installed.
    internal static class TmpeCompat
    {
        private const string CustomPathFindTypeName = "TrafficManager.Custom.PathFinding.CustomPathFind";
        private const string CostFactorsMethodName = "CalculateAdvancedAiCostFactors";

        public static bool IsTmpeLoaded()
        {
            return FindCustomPathFindType() != null;
        }

        public static Type FindCustomPathFindType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(CustomPathFindTypeName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        public static MethodInfo FindCostFactorsMethod(Type customPathFindType)
        {
            if (customPathFindType == null)
            {
                return null;
            }

            // Only expected to exist when TMPE was built with ADVANCEDAI && ROUTING defined
            // (true for all published releases as of writing - "Advanced Vehicle AI" is a
            // real, user-toggleable TMPE option). Returns null harmlessly if absent so the
            // caller can skip patching instead of crashing.
            return customPathFindType
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == CostFactorsMethodName);
        }
    }
}
