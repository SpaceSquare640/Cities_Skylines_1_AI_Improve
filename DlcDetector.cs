using ColossalFramework;

namespace AIImprove
{
    // DLC ownership checks, for features that only make sense when the content they act on
    // actually exists (2026-08-14, per user request: "如果偵測到玩家沒有的士的 DLC 內容我們這個
    // 的士的 AI 部份就不會啟用").
    //
    // Note this is the opposite direction from the helicopter features elsewhere in this project,
    // which deliberately work whether or not the player owns the DLC that introduced them - those
    // act on vehicles the game can still create, while a taxi probability boost is meaningless
    // with no taxi vehicle to hand out.
    //
    // SteamHelper.IsDLCOwned is a plain static call on a global (namespace-less) game type, so no
    // reflection is needed - unlike the third-party mod detection in CompanionModCompat.
    internal static class DlcDetector
    {
        private static bool? afterDarkOwned;

        // Taxis (the vehicle, the taxi depot and the taxi stand) came with After Dark. Cached
        // because DLC ownership cannot change while the game is running, and this is consulted
        // from a per-trip-decision hot path.
        public static bool IsAfterDarkOwned()
        {
            if (!afterDarkOwned.HasValue)
            {
                afterDarkOwned = SteamHelper.IsDLCOwned(SteamHelper.DLC.AfterDarkDLC);
            }

            return afterDarkOwned.Value;
        }
    }
}
