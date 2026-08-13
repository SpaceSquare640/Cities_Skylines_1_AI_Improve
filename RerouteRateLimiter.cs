namespace AIImprove
{
    // Caps how many vehicles can actually invoke a real StartPathFind reroute within the same
    // simulation frame, across ALL of FlexibleReroutePatch (trains, aircraft, and - since
    // 2026-08-13 - every ordinary CarAI vehicle in the city, including cargo trucks).
    //
    // Root cause of a reported periodic stutter (2026-08-13): StuckRerouteTracker's per-vehicle
    // cooldown has no reason to desynchronize vehicles from each other - when congestion builds up
    // in an area, many vehicles cross the density threshold and come off cooldown around the same
    // moment, so live-test logs showed bursts of 10-34 reroute requests landing inside the exact
    // same logged second. Each one triggers a real PathManager.CreatePath computation (via
    // reflection) - genuinely expensive pathfinding, not a cheap flag check - so a burst that size
    // in a single simulation frame is a real, visible hitch, not a red herring.
    //
    // This doesn't reduce how many vehicles eventually reroute, just how many can do so in any one
    // frame - the rest simply try again on their very next SimulationStep tick (their per-vehicle
    // cooldown in StuckRerouteTracker is deliberately NOT consumed when this budget denies them -
    // see FlexibleReroutePatch's call site - so nothing is wasted, it's just spread over a few more
    // frames instead of landing all at once).
    internal static class RerouteRateLimiter
    {
        private static readonly PerFrameBudget Budget = new PerFrameBudget(3);

        public static bool TryConsumeBudget()
        {
            return Budget.TryConsume();
        }
    }
}
