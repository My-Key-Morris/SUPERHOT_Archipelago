using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Layers Archipelago-granted unlocks on top of the game's native sequential hub
    /// lock logic. See Core/UnlockState.cs for why this is a separate layer rather than
    /// replacing the native logic outright.
    ///
    /// piOsMenu.LockUnfinishedLevels() (confirmed instance method, piOsMenu.cs:1525)
    /// rebuilds the hub's lock state every time it runs: it pulls
    /// (SHGUI.current.GetInteractableView() as SHGUIcommanderview).buttons (confirmed,
    /// piOsMenu.cs:1555 -- each button is a SHGUIcommanderbutton), then locks/unlocks
    /// each one based on save progress via SetLocked(bool) (confirmed
    /// SHGUIcommanderbutton.cs:154). Running our own pass as a Postfix means we always
    /// run after the native logic has finished, and we decide the end result ourselves
    /// for every tracked button rather than only ever loosening what native decided.
    ///
    /// Join key: NOT SceneFileName/LevelToBeLoaded -- a real playtest showed several
    /// levels reuse the same Unity scene for different story beats, so scene name can't
    /// reliably identify which level a button represents (this is why "Cyberspace (1)"
    /// never linked to anything before this fix). Instead we resolve each button back to
    /// its real LevelInfo via LevelSetup.GetLevelInfoByUniqueSHName(button.LevelToBeLoadedName)
    /// (LevelToBeLoadedName is set from LevelInfo.UniqueSHName, confirmed unique per
    /// level instance -- see Core/LevelCatalog.cs's LevelEntry comment) and match on its
    /// .ID.
    ///
    /// Also controls button text/color here -- a real, explicit user request, revised
    /// twice now:
    ///
    /// Round 1 wanted names to always stay legible, with only color indicating lock
    /// state -- so this Postfix used to unconditionally restore each button's clean
    /// cached text (see Patches/LevelButtonCapturePatch.cs) and call SetLocked(!unlocked).
    ///
    /// Round 2 asked for a three-way distinction instead: GARBLED if not unlocked, grey
    /// legible text if unlocked but not yet completed (the check hasn't actually been
    /// sent), and white legible text once it has.
    ///
    /// - Locked: garble the cached clean name ourselves via the same
    ///   StringScrambler.GetScrambledString(text, 0.9f, "▀▄█▌▐░▒▓■▪01 ") call native
    ///   LockUnfinishedLevels() uses on its own scrambling pass (confirmed via decompile,
    ///   same method/args) -- doing it ourselves rather than trusting whatever the native
    ///   pass already left behind, since native's own sequential-unlock idea of "locked"
    ///   can disagree with ours (an AP-received item can unlock a level natively still
    ///   considers out of reach, and vice versa before any item arrives). SetLocked(true)
    ///   for both the gray color and the actual click-block -- genuinely locked levels
    ///   should refuse to launch.
    /// - Unlocked, not completed: clean cached text, gray -- but NOT via SetLocked(true).
    ///   A real bug found by testing: SetLocked(bool) doesn't just recolor, it also sets
    ///   IsLocked, which SHGUIcommanderbutton's own activation code checks BEFORE ever
    ///   invoking OnActivate (confirmed via decompile -- if IsLocked, it plays a "wrong"
    ///   sound and returns immediately, the level-launch delegate never runs). Since a
    ///   level can only ever become "completed" by playing it once, calling
    ///   SetLocked(!completed) here made every never-yet-played level -- including the
    ///   very first one -- permanently unclickable. Fixed by setting color directly
    ///   instead: button.IsLocked = false (so it stays genuinely clickable), then
    ///   button.color = 'z' and button.SetColorRecursive('z') -- the same two calls
    ///   SetLocked makes internally for its color half, confirmed via decompile, just
    ///   without the IsLocked assignment that came bundled with it.
    /// - Unlocked, completed: same as above but with 'w' instead of 'z'. Completion comes
    ///   from LocationManager.IsLevelCompleted(), which reads the live Archipelago session
    ///   rather than anything tracked locally -- see its own comment for why.
    ///
    /// A fourth state exists for exactly one button, "34 - Free": item-unlocked but still
    /// short of the other-levels-completed threshold (Core/LevelAccessGuard.cs's second
    /// gate on it). Shown grey like "unlocked, not completed" above, but with a live
    /// "done/needed" count in place of the usual "LEVEL" status, and genuinely locked
    /// (IsLocked = true) rather than just colored -- unlike the general unlocked/not-yet-
    /// completed case, a never-yet-played level, this one really can't be entered yet.
    /// The same count is also pushed into the button's right-panel description text
    /// (SHGUIcommanderbutton.data), so it shows up as a legible line in the hub's preview
    /// panel when this row is highlighted -- the same treatment the "secret cracked/not
    /// cracked" line gets for levels that have a secret, confirmed via decompile.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), nameof(piOsMenu.LockUnfinishedLevels))]
    public static class HubUnlockPatch
    {
        public static void Postfix()
        {
            if (SHGUI.current == null)
            {
                return;
            }

            if (SHGUI.current.GetInteractableView() is not SHGUIcommanderview commanderView)
            {
                return;
            }

            if (commanderView.buttons == null)
            {
                return;
            }

            foreach (SHGUIcommanderbutton button in commanderView.buttons)
            {
                if (button == null || button.LevelToBeLoaded == "")
                {
                    continue;
                }

                // Never actually returns null -- falls back to a sentinel LevelInfo
                // with default field values (ID 0, SceneFileName "") if it can't find a
                // match, confirmed by decompiling LevelSetup.cs. Guard on SceneFileName
                // rather than null so a lookup miss can't get misread as level id 0
                // ("Kick", which is always treated as unlocked).
                LevelInfo levelInfo = LevelSetup.GetLevelInfoByUniqueSHName(button.LevelToBeLoadedName);
                if (string.IsNullOrEmpty(levelInfo.SceneFileName))
                {
                    continue;
                }

                if (!SuperhotArchipelago.Core.LevelCatalog.LevelIdToLevel.TryGetValue(
                        levelInfo.ID, out SuperhotArchipelago.Core.LevelEntry? entry))
                {
                    // Not one of our tracked story levels -- leave the native
                    // lock/scramble result alone.
                    continue;
                }

                // Real bug found by playtesting, reported verbatim: "Secrets will say
                // 'Cracked!' despite starting new game... leftover from the previous run
                // and just not reset." Confirmed via decompile: PrepareLevelCommanderButtonForLevel
                // adds the "CRACKED!" scrolling badge (AddScrollingNotification) purely
                // from element.SecretsFound(), and PrepareLevelDescription bakes the same
                // native check into this button's description text (button.data) as the
                // localized MENU_SECRETCRACKED/MENU_SECRETNOTCRACKED strings -- both read
                // stale native save flags with zero knowledge of Archipelago's own tracked
                // state. Runs unconditionally (not just in the unlocked branch below) since
                // a locked level's button can carry a stale badge too, and this loop
                // already runs every time the native pass does (right after it, same
                // Postfix), so it stays correct across every hub refresh, not just once.
                //
                // AddScrollingNotification/RemoveScrollingNotification are both public and
                // AddScrollingNotification already calls RemoveScrollingNotification
                // internally first (confirmed via decompile, SHGUIcommanderbutton.cs),
                // so this can't stack duplicate badges by calling it repeatedly.
                //
                // The description text fix is a plain substring swap rather than
                // reimplementing PrepareLevelDescription's own random-scrambled-filler
                // text construction: "KEY".T() (confirmed public, LocalizationAccessHelperExtensions.T,
                // calls LocalizationManager.Instance.GetLocalized) resolves to the exact
                // same literal string native code embeds, so replacing one for the other
                // inside the already-built button.data is safe and idempotent.
                if (entry.HasSecret)
                {
                    bool secretCompleted = SuperhotArchipelago.Core.Mod.Locations?.IsSecretCompleted(levelInfo.ID) ?? false;

                    if (secretCompleted)
                    {
                        button.AddScrollingNotification("MENU_CRACKED8CHARS".T().PadRight(8), scrolling: false);
                    }
                    else
                    {
                        button.RemoveScrollingNotification();
                    }

                    string notCrackedText = "MENU_SECRETNOTCRACKED".T();
                    string crackedText = "MENU_SECRETCRACKED".T();
                    if (secretCompleted && button.data.Contains(notCrackedText))
                    {
                        button.data = button.data.Replace(notCrackedText, crackedText);
                    }
                    else if (!secretCompleted && button.data.Contains(crackedText))
                    {
                        button.data = button.data.Replace(crackedText, notCrackedText);
                    }
                }

                bool unlocked = entry.Order == 1 || SuperhotArchipelago.Core.UnlockState.IsUnlocked(levelInfo.ID);

                if (!SuperhotArchipelago.Core.ButtonTextCache.TryGet(levelInfo.ID, out string cleanName))
                {
                    // No cached clean name to work with (shouldn't normally happen --
                    // LevelButtonCapturePatch snapshots every tracked level's button on
                    // creation) -- fall back to just fixing color, leave text alone.
                    button.SetLocked(!unlocked);
                    continue;
                }

                if (!unlocked)
                {
                    string scrambled = StringScrambler.GetScrambledString(
                        cleanName.ToUpper(), 0.9f, "▀▄█▌▐░▒▓■▪01 ");
                    button.ButtonText = scrambled + "│" + "MENU_LOCKED8CHARS".T();
                    button.RefreshText();
                    button.SetLocked(true);
                    continue;
                }

                bool completed = SuperhotArchipelago.Core.Mod.Locations?.IsLevelCompleted(levelInfo.ID) ?? false;

                // Real, explicit user request: "34 - Free" gets a second gate on top of
                // the normal item-unlock above -- even with its access item in hand, it
                // stays locked (and shows live "done/needed" progress instead of the
                // generic "LEVEL" status) until enough of the other 31 levels are
                // actually completed. This has to stay in sync with the real gate in
                // Core/LevelAccessGuard.cs, which is what actually blocks entry -- this
                // is only the display half.
                if (entry.Order == SuperhotArchipelago.Core.LevelCatalog.Levels.Count)
                {
                    int required = SuperhotArchipelago.Core.Mod.Connection?.LevelsRequiredForFree ?? 0;
                    int otherCompleted = SuperhotArchipelago.Core.Mod.Locations?.CountOtherLevelsCompleted() ?? 0;
                    bool stillGated = otherCompleted < required;

                    // Real, explicit user request: also show this in the right-side
                    // description/preview panel, the same way piOsMenu.PrepareLevelDescription()
                    // shows "secret cracked"/"not cracked" for levels that have one --
                    // confirmed via decompile that SHGUIcommanderbutton.data is exactly
                    // what gets pushed into that panel (listLink.rightPanel.text) the
                    // moment this row is highlighted. Rebuilt from
                    // LevelButtonCapturePatch's cached, pre-scramble original every pass
                    // -- never mutated in place -- so repeated hub refreshes can't
                    // double-insert the line, and it cleanly reverts to the original
                    // (mostly noise, same as every other level) once the gate opens.
                    if (SuperhotArchipelago.Core.ButtonTextCache.TryGetData(levelInfo.ID, out string cleanData))
                    {
                        button.data = stillGated
                            ? $"\n\n{otherCompleted}/{required} LEVELS COMPLETED\n\n" + cleanData
                            : cleanData;
                    }

                    if (stillGated)
                    {
                        // Padded to 8 chars to match the fixed-width status field every
                        // other status string here uses (MENU_LOCKED8CHARS/MENU_LEVEL8CHARS
                        // are both baked to that width) -- keeps the '│' column aligned
                        // with every other hub button instead of just this one shifting.
                        button.ButtonText = cleanName + "│" + $"{otherCompleted}/{required}".PadRight(8);
                        button.RefreshText();
                        button.IsLocked = true;
                        button.color = 'z';
                        button.SetColorRecursive('z');
                        continue;
                    }
                }

                button.ButtonText = cleanName + "│" + "MENU_LEVEL8CHARS".T();
                button.RefreshText();

                // NOT SetLocked(!completed) -- see the class doc for the real bug that
                // was. Genuinely unlocked levels must stay clickable regardless of
                // completion; only color should track completion.
                button.IsLocked = false;
                char targetColor = completed ? 'w' : 'z';
                button.color = targetColor;
                button.SetColorRecursive(targetColor);
            }
        }
    }
}
