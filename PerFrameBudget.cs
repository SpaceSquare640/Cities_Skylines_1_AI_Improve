using ColossalFramework;

namespace AIImprove
{
    // Generic "at most N of this expensive operation per simulation frame" gate. Extracted from
    // RerouteRateLimiter (2026-08-13) so the same fix could be reapplied to
    // CitizenCarProbabilityPatch without duplicating the frame-boundary-detection logic -
    // CitizenCongestionQuery.GetNearbyRoadDensity is its own real PathManager.FindPathPosition
    // call, unthrottled, running once per citizen trip decision; a busy city can easily start many
    // trips in the same frame, which reproduced the exact same kind of burst-driven stutter this
    // class was originally built to fix for vehicle reroutes.
    internal sealed class PerFrameBudget
    {
        private readonly int maxPerFrame;
        private uint lastFrameIndex;
        private int usedThisFrame;

        public PerFrameBudget(int maxPerFrame)
        {
            this.maxPerFrame = maxPerFrame;
        }

        public bool TryConsume()
        {
            uint currentFrame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            if (currentFrame != lastFrameIndex)
            {
                lastFrameIndex = currentFrame;
                usedThisFrame = 0;
            }

            if (usedThisFrame >= maxPerFrame)
            {
                return false;
            }

            usedThisFrame++;
            return true;
        }
    }
}
