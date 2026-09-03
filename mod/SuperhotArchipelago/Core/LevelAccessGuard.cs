namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Shared "is this level actually allowed right now" check, used by every patch that
    /// can result in a level being loaded, so bypass paths can't drift out of sync. Resolves
    /// forward through untracked raw entries to the next tracked level, blocking
    /// unconditionally (instead of auto-playing through them) if any had to be skipped.
    /// </summary>
    public static class LevelAccessGuard
    {
        /// <summary>
        /// Returns true and sets blockMessage if the level should be blocked; returns false
        /// if it's genuinely unlocked or resolves to no tracked level at all (credits, hub, etc).
        /// </summary>
        public static bool ShouldBlock(LevelInfo level, out string blockMessage)
        {
            blockMessage = "";

            // Archipelago mode can be turned off entirely to play vanilla; since every gate
            // funnels through this method, checking it here once makes all of them a no-op.
            if (!Mod.IsEnabled)
            {
                return false;
            }

            if (level == null || string.IsNullOrEmpty(level.SceneFileName))
            {
                return false;
            }

            LevelInfo? tracked = ResolveToTrackedLevel(level);
            if (tracked == null)
            {
                return false;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(tracked.ID, out LevelEntry? entry))
            {
                return false;
            }

            // If the raw level asked about isn't itself already the next tracked level (e.g.
            // "22 - Hacker" -> several untracked narrative scenes -> "25 - Fall"), block
            // unconditionally and send the player to the hub rather than letting the native
            // flow auto-play through the untracked scenes in between.
            bool isDirectlyNextLevel = LevelCatalog.LevelIdToLevel.ContainsKey(level.ID);
            if (!isDirectlyNextLevel)
            {
                blockMessage = "Return to hub to continue";
                return true;
            }

            if (entry.Order == 1)
            {
                return false;
            }

            // A level excluded via ExcludeSlowLevels has no access item or location in this
            // generation, so it can never become "unlocked" the normal way -- always let it
            // through instead, same as level 1's special case above.
            if (Mod.Connection != null && Mod.Connection.IsLevelExcluded(entry.Order))
            {
                return false;
            }

            if (!UnlockState.IsUnlocked(tracked.ID))
            {
                // No longer needs to be kept artificially short -- that was working around
                // the old uptitle's fixed-width truncation (and, briefly, SHGUI's own
                // shader-based text renderer). PopupOverlay's Canvas-based box wraps/sizes to
                // fit real text, so this can just say what it means.
                blockMessage = $"LOCKED: '{entry.DisplayName}' needs an Archipelago item to unlock";
                return true;
            }

            // "34 - Free" (the real ending) gets a second gate on top of the item-based one:
            // even with its access item, it stays locked until enough other levels are
            // completed, so a lucky early item can't end the run prematurely. No other level
            // has this check.
            if (entry.Order == LevelCatalog.Levels.Count)
            {
                // The raw YAML threshold doesn't account for ExcludeSlowLevels and can ask for
                // more completions than are possible; GetLevelsRequiredForFree() clamps it.
                int required = Mod.Locations?.GetLevelsRequiredForFree() ?? 0;
                int completed = Mod.Locations?.CountOtherLevelsCompleted() ?? 0;

                if (completed < required)
                {
                    blockMessage = $"LOCKED: {completed}/{required} levels needed";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Walks forward through LevelSetup.Levels (the raw, unfiltered list), skipping
        /// entries not in our catalog, until it finds the next tracked level or gives up.
        /// </summary>
        private static LevelInfo? ResolveToTrackedLevel(LevelInfo level)
        {
            LevelInfo? current = level;
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
