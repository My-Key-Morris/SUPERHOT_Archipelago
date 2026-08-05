using System.Collections.Generic;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Caches each story level's clean, unscrambled hub button text (as originally built
    /// by piOsMenu.PrepareLevelCommanderButtonForLevel(), before the native
    /// LockUnfinishedLevels() pass mangles it -- see Patches/LevelButtonCapturePatch.cs
    /// for where this gets filled in, and Patches/HubUnlockPatch.cs for where it gets
    /// used to restore legibility.
    ///
    /// Exists because of a real, explicit user request: SUPERHOT's native design
    /// scrambles a locked level's displayed name into unreadable noise (on top of just
    /// graying it out), and the user wants every level name to stay fully legible, with
    /// only a gray color to indicate "not unlocked yet" -- no mystery text. Restoring
    /// from this cache is what makes that possible without fighting the native
    /// scrambling pass line by line.
    /// </summary>
    public static class ButtonTextCache
    {
        // Stores just the name portion (everything before the '│' status separator) --
        // the status text itself gets rebuilt fresh from our own unlock decision each
        // time, not replayed from this snapshot. See HubUnlockPatch.cs.
        private static readonly Dictionary<int, string> _cleanNameByLevelId = new();

        public static void Remember(int levelId, string cleanName)
        {
            _cleanNameByLevelId[levelId] = cleanName;
        }

        public static bool TryGet(int levelId, out string cleanName)
        {
            return _cleanNameByLevelId.TryGetValue(levelId, out cleanName!);
        }
    }
}
