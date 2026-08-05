namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Shared "is this level actually allowed right now" check, used by every patch that
    /// can result in a level being loaded. Pulled out into one place after a real
    /// playtest found a third bypass path (SHGUI.LaunchLevelViaApp) beyond the two
    /// already gated (SHGUI.LaunchLevelAppTunnels, LevelSetup.LoadNextLevel) -- rather
    /// than copy-pasting this logic a third time, and risk a fourth path someday drifting
    /// out of sync with the other three, every gate now calls this one method.
    ///
    /// Real bug found by a playtest: "05 - Subway" let its SUPERHOT title card be clicked
    /// through freely (and played its whole ~20s scripted "unauthorized access" disconnect
    /// sequence) even when the real next level, "06 - Jump", was still locked; "28 -
    /// Station" softlocked outright in the same situation. Root cause, confirmed by
    /// decompiling LevelSetup.GetNextLevelInfo(): outside of GameplayModifiers challenge
    /// mode it does zero intermission-skipping -- it's just
    /// Levels[GetLevelIndexByID(CurrentLevelInfo.ID) + 1], the literal next raw entry in
    /// the full 49-element list. Both Subway (gameId 5) and Station (gameId 34) are
    /// immediately followed by an untracked raw entry (gameId 6 and 35 respectively --
    /// neither appears in levels.json) that's presumably a "_SEGWAYSTUB"-style narrative
    /// interlude, not a real playable level. Every call site here was checking THAT
    /// untracked entry for lock status; since it's not in our catalog, ShouldBlock always
    /// said "let it through" without ever looking far enough ahead to see that the real
    /// next tracked level past it was still locked. Fixed by resolving forward through any
    /// untracked entries to the next level we actually track before checking unlock state.
    /// </summary>
    public static class LevelAccessGuard
    {
        /// <summary>
        /// Returns true if the level should be BLOCKED, and sets blockMessage to what to
        /// show the player. Returns false (with an empty message) if the level should be
        /// allowed through -- either because it's genuinely unlocked, or because walking
        /// forward from it never reaches one of our tracked story levels at all (credits,
        /// SHMenu, secret/bonus content we don't understand shouldn't be blocked by us).
        /// </summary>
        public static bool ShouldBlock(LevelInfo level, out string blockMessage)
        {
            blockMessage = "";

            if (level == null || string.IsNullOrEmpty(level.SceneFileName))
            {
                return false;
            }

            LevelInfo tracked = ResolveToTrackedLevel(level);
            if (tracked == null)
            {
                return false;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(tracked.ID, out LevelEntry? entry))
            {
                return false;
            }

            if (entry.Order == 1 || UnlockState.IsUnlocked(tracked.ID))
            {
                return false;
            }

            // Real bug found by a playtest screenshot: the original, longer message here
            // ("LOCKED -- 'X' needs an Archipelago item before you can play it.", up to 74
            // characters for the longest level name) got visibly truncated on BOTH edges
            // by the uptitle display -- it's a fixed-width Unity UI Text element (see
            // TextManager.Uptitle), not something that wraps or shrinks to fit. Vanilla's
            // own uptitle strings (e.g. "hack into a terminal to skip level", 35 chars)
            // are much shorter, which is the actual evidence this needed shortening
            // rather than a guess -- kept this well under that with real margin (39 chars
            // for the longest level name) rather than cutting it as close as possible.
            blockMessage = $"LOCKED: '{entry.DisplayName}' needs an AP item";
            return true;
        }

        /// <summary>
        /// Walks forward through LevelSetup.Levels (the raw, unfiltered 49-entry list)
        /// starting at the given level, skipping any entries not present in our catalog
        /// (segway stubs, SHMenu, etc.), until it finds the next one we actually track --
        /// or runs off the end / hits a bounded step limit, in which case there's nothing
        /// left for us to gate.
        /// </summary>
        private static LevelInfo ResolveToTrackedLevel(LevelInfo level)
        {
            LevelInfo current = level;
            int steps = 0;

            while (current != null && !string.IsNullOrEmpty(current.SceneFileName)
                && !LevelCatalog.LevelIdToLevel.ContainsKey(current.ID) && steps < 64)
            {
                int nextIndex = LevelSetup.GetLevelIndexByID(current.ID) + 1;
                current = (nextIndex < LevelSetup.Levels.Count) ? LevelSetup.Levels[nextIndex] : null;
                steps++;
            }

            return current;
        }
    }
}
