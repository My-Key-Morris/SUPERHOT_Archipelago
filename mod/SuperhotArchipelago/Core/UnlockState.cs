using System.Collections.Generic;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Tracks which levels this player has unlocked via received Archipelago items.
    /// SUPERHOT's native hub-unlock logic only supports "sequentially unlocked" or "all
    /// unlocked," with no support for out-of-order unlocks, so this set is kept separately
    /// and layered on top via ../Patches/HubUnlockPatch.cs. Tracked by LevelEntry.LevelId,
    /// not scene name, since several levels reuse the same Unity scene.
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

        /// <summary>
        /// Called from ArchipelagoConnection.Connect() (alongside NotificationLog.Clear()) so
        /// each connect starts clean. Without this, this static set persisted across
        /// connections within the same game session, so unlocks from a previous room bled
        /// into a newly-opened one.
        /// </summary>
        public static void Clear()
        {
            _unlockedLevelIds.Clear();
        }
    }
}
