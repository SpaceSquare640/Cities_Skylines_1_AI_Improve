using ColossalFramework;

namespace AIImprove
{
    // Reads real-time congestion density for the road/track/taxiway segments a vehicle is about
    // to travel through, by walking its existing path via PathUnit's own public traversal API
    // (PathUnit.GetNextPosition). Pure data reads against already-public fields
    // (NetSegment.m_trafficDensity) - no Harmony patching of any pathfinding method, so this has
    // none of the Mono JIT "Invalid IL code" limitations that blocked directly patching the cost
    // functions themselves (CalculateAdvancedAiCostFactors / ProcessItemCosts - see
    // Cities_Skylines_1_AI_Improve_Document/03). This is what actually lets
    // FlexibleReroutePatch use real congestion instead of the "vehicle speed near zero" proxy it
    // started with.
    internal static class SegmentCongestionQuery
    {
        private const int LookaheadPositions = 6;

        // Average NetSegment.m_trafficDensity (vanilla 0-100 scale) across the next few path
        // positions ahead of the vehicle's current spot, or -1 if there's no path to read /
        // nothing resolved (treat -1 as "don't reroute, no data").
        public static float GetAverageAheadDensity(ref Vehicle vehicleData)
        {
            if (vehicleData.m_path == 0U)
            {
                return -1f;
            }

            uint unitId = vehicleData.m_path;
            // m_pathPositionIndex is a packed (index * 2) value throughout this codebase's
            // vehicle/path code - halving it recovers the actual PathUnit.Position index.
            int index = vehicleData.m_pathPositionIndex >> 1;

            NetSegment[] segments = Singleton<NetManager>.instance.m_segments.m_buffer;

            int sampled = 0;
            int densitySum = 0;

            for (int i = 0; i < LookaheadPositions; i++)
            {
                PathUnit.Position position;
                bool invalid;
                if (!PathUnit.GetNextPosition(ref unitId, ref index, out position, out invalid) || invalid)
                {
                    break;
                }

                if (position.m_segment == 0)
                {
                    continue;
                }

                densitySum += segments[position.m_segment].m_trafficDensity;
                sampled++;
            }

            return sampled > 0 ? (float)densitySum / sampled : -1f;
        }
    }
}
