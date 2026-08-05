using System.Collections.Generic;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Tracks which levels this player has unlocked via received Archipelago items.
    ///
    /// This exists because of a real constraint found in the decompiled game: SUPERHOT's
    /// native hub-unlock logic (piOsMenu.LockUnfinishedLevels(), confirmed at
    /// piOsMenu.cs:1525) only supports two states -- "everything up to your highest
    /// sequentially-finished level is unlocked" (driven by the save value
    /// "highestfinishedLevel"), or "everything is unlocked" (the "unlockEverything" save
    /// flag / MainDebug.UnlockAllLevels). There's no native support for unlocking
    /// individual out-of-order levels, which is exactly what an Archipelago-shuffled
    /// item pool needs (you might receive level 20's access item before level 3's).
    ///
    /// So instead of touching the game's own save data, we keep our own set here and
    /// patch the hub's lock pass (see ../Patches/HubUnlockPatch.cs) to unlock icons for
    /// any level in this set, layered on top of (not instead of) the native logic --
    /// i.e. the game's own sequential unlock still works as a floor, this only adds
    /// extra unlocks on top of it.
    ///
    /// Tracked by LevelEntry.LevelId (== the real LevelInfo.ID), not scene name -- a
    /// real bug found by testing showed several levels reuse the same Unity scene for
    /// different story beats (see LevelCatalog.LevelEntry's comment), so scene name
    /// can't reliably identify which level was actually unlocked/completed.
    /// </summary>
    public static class UnlockState
    {
        private static readonly HashSet<int> _unlockedLevelIds = new();

        public static void Unlock(int levelId)
        {
            _unlockedLevelIds.Add(levelId);
        }

        public static bool IsUnlocked(int levelId)
        {
            return _unlockedLevelIds.Contains(levelId);
        }
    }
}
