using System.Collections.Generic;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Caches each story level's clean, unscrambled hub button text before the native
    /// LockUnfinishedLevels() pass mangles it (see Patches/LevelButtonCapturePatch.cs for
    /// where it's filled in, Patches/HubUnlockPatch.cs for where it's restored), so a locked
    /// level's name stays legible with only its color indicating lock state.
    /// </summary>
    public static class ButtonTextCache
    {
        // Stores just the name portion (before the '│' status separator) -- status text is
        // rebuilt fresh from our own unlock decision each time. See HubUnlockPatch.cs.
        private static readonly Dictionary<int, string> _cleanNameByLevelId = new();

        // Same idea for SHGUIcommanderbutton.data (the hub's right-side preview panel text):
        // needed so HubUnlockPatch.cs's Free-level progress display can rebuild it from a
        // known-clean original each pass instead of double-inserting or baking in a stale line.
        private static readonly Dictionary<int, string> _cleanDataByLevelId = new();

        public static void Remember(int levelId, string cleanName)
        {
            _cleanNameByLevelId[levelId] = cleanName;
        }

        public static bool TryGet(int levelId, out string cleanName)
        {
            return _cleanNameByLevelId.TryGetValue(levelId, out cleanName!);
        }

        public static void RememberData(int levelId, string cleanData)
        {
            _cleanDataByLevelId[levelId] = cleanData;
        }

        public static bool TryGetData(int levelId, out string cleanData)
        {
            return _cleanDataByLevelId.TryGetValue(levelId, out cleanData!);
        }
    }
}
