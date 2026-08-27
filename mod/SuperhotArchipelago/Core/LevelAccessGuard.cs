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
    ///
    /// Second real, explicit user request, reported after the above fix shipped: once the
    /// resolved target IS unlocked, the forward-resolution above let the native flow play
    /// through every untracked entry in between uninterrupted -- fine for a short segway
    /// stub, but "22 - Hacker" -> "25 - Fall" runs through several of its own narrative
    /// detour scenes this way, none of which the player has any way to skip. Whether the
    /// resolved target is locked or not no longer matters for this: ShouldBlock now blocks
    /// unconditionally whenever the raw level it's asked about isn't itself already the
    /// next tracked level (see the `isDirectlyNextLevel` check below), sending the player
    /// to the hub instead of auto-playing anything in between. A genuinely direct
    /// transition -- no untracked entry between the current level and the next tracked one
    /// -- still passes straight through to the real unlock check below, same as always.
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

            // Real, explicit user request: Archipelago mode can be turned off entirely
            // (Mod.IsEnabled, see Patches/ArchipelagoModeTogglePatch.cs) to play vanilla
            // SUPERHOT without uninstalling the mod. Since every gate that can block a
            // level launch funnels through this one method, checking it here once is
            // enough to make every one of them a no-op while disabled -- nothing further
            // down this method runs, so nothing is ever blocked, exactly like the mod
            // wasn't installed at all for gating purposes.
            if (!Mod.IsEnabled)
            {
                return false;
            }

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

            // Real, explicit user request, real reported case: "22 - Hacker" ends into a
            // narrative detour of several untracked entries (its own credits/interlude
            // scenes, none in our catalog) before the native flow would eventually land on
            // "25 - Fall" -- and since ResolveToTrackedLevel above walks straight through
            // those and only checks Fall's own unlock state, an unlocked Fall let the whole
            // detour auto-play uninterrupted, cutscenes and all, with no way for the player
            // to skip straight to the hub instead. Whether the eventual destination is
            // locked or not is irrelevant here -- the player just shouldn't be walked
            // through scenes outside our catalog automatically at all. So: if the raw level
            // this call was actually asked about isn't ITSELF already the next tracked
            // level (i.e. ResolveToTrackedLevel had to skip past at least one untracked
            // entry to get here), block unconditionally and send the player to the hub,
            // before any of those scenes get a chance to start. Only a genuinely direct
            // transition -- the very next raw entry already IS the next tracked level, no
            // detour in between -- skips this and falls through to the normal per-level
            // checks below.
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

            if (!UnlockState.IsUnlocked(tracked.ID))
            {
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

            // Real, explicit user request: "34 - Free" (the game's real ending) gets a
            // second, independent gate on top of the normal item-based one above -- even
            // once its own access item is received, it stays locked until enough of the
            // other 31 levels have actually been completed (not just unlocked). Stops a
            // lucky early access item from ending the run before the player's engaged
            // with most of the campaign. Threshold is a per-player apworld option
            // (Options.py's levels_required_for_free) carried over slot data -- see
            // Core/ArchipelagoConnection.cs's LevelsRequiredForFree. No other level has
            // this second check.
            if (entry.Order == LevelCatalog.Levels.Count)
            {
                int required = Mod.Connection?.LevelsRequiredForFree ?? 0;
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
