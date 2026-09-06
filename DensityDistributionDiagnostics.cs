using System.Collections.Generic;

namespace AIImprove
{
    // OPEN QUESTION A3 (2026-09-05): trains and aircraft reach the reroute check but essentially
    // never pass it. The density threshold was raised from 60 to 80 on 2026-08-12 on reasoning
    // that has since been disproven, and the obvious suspicion is that rail/air ahead-density
    // simply never gets near 80 - but we have never measured it. Every existing log line about
    // density is written AFTER the threshold test, so the samples that fail it (i.e. all of them)
    // produce no record at all: the one number we need is the one number we throw away.
    //
    // This records every sample regardless of outcome, bucketed, per vehicle-AI type. One session
    // then answers "what does the distribution actually look like, and is 80 reachable at all"
    // instead of only re-confirming that nothing passed. See 12 - 開發準則, 準則 10.2 - a
    // diagnostic that only reports failure forces a second 20-minute session to ask where.
    //
    // Info, not Verbose, and self-throttled: a diagnostic that requires someone to have remembered
    // a setting is a diagnostic that will be missing from the log you actually need (準則 10.3).
    //
    // Remove once A3 is closed and the thresholds are set from data.
    internal static class DensityDistributionDiagnostics
    {
        private const int BucketWidth = 10;
        private const int BucketCount = 11;   // 0-9, 10-19, ... 90-99, 100+
        private const int ReportEvery = 500;

        private sealed class Histogram
        {
            public readonly int[] Buckets = new int[BucketCount];
            public int Samples;
            public float Max;
        }

        private static readonly Dictionary<string, Histogram> ByOwnerType =
            new Dictionary<string, Histogram>();

        public static void ResetForNewLevel()
        {
            ByOwnerType.Clear();
        }

        public static void Record(string ownerTypeName, float density, float threshold)
        {
            // Negative means the query could not answer (no path, no ahead segment). Counting it
            // as bucket 0 would understate real congestion, so it is simply not a sample.
            if (density < 0f)
            {
                return;
            }

            Histogram histogram;
            if (!ByOwnerType.TryGetValue(ownerTypeName, out histogram))
            {
                histogram = new Histogram();
                ByOwnerType[ownerTypeName] = histogram;
            }

            int bucket = (int)(density / BucketWidth);
            if (bucket < 0)
            {
                bucket = 0;
            }
            else if (bucket >= BucketCount)
            {
                bucket = BucketCount - 1;
            }

            histogram.Buckets[bucket]++;
            histogram.Samples++;
            if (density > histogram.Max)
            {
                histogram.Max = density;
            }

            if (histogram.Samples % ReportEvery != 0)
            {
                return;
            }

            System.Text.StringBuilder line = new System.Text.StringBuilder();
            line.Append("[AIImprove] Density distribution (").Append(ownerTypeName).Append("): ")
                .Append(histogram.Samples).Append(" sample(s), threshold ")
                .Append(threshold.ToString("F0")).Append(", max seen ")
                .Append(histogram.Max.ToString("F1")).Append(" -");

            for (int i = 0; i < BucketCount; i++)
            {
                if (histogram.Buckets[i] == 0)
                {
                    continue;
                }

                line.Append(' ')
                    .Append(i * BucketWidth)
                    .Append(i == BucketCount - 1 ? "+" : "-" + ((i * BucketWidth) + BucketWidth - 1))
                    .Append(':')
                    .Append(histogram.Buckets[i]);
            }

            Log.Info(line.ToString());
        }
    }
}
