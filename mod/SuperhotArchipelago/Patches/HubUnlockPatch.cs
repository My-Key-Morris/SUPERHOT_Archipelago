using System.Reflection;
using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Layers Archipelago-granted unlocks on top of the game's native sequential hub lock
    /// logic as a Postfix on piOsMenu.LockUnfinishedLevels(), resolving each button to its
    /// LevelInfo via LevelSetup.GetLevelInfoByUniqueSHName rather than scene name (multiple
    /// levels share scenes). Buttons can be in one of five states: locked (garbled name,
    /// gray, unclickable), unlocked-not-completed (clean name, gray, clickable), completed
    /// (clean name, white, clickable), excluded via ExcludeSlowLevels (garbled name, blue,
    /// "INACTIVE" suffix, unclickable in the hub only -- normal in-level progression into
    /// it still works via Core/LevelAccessGuard.cs), and the single "34 - Free" button
    /// while gated behind a second completed-level-count threshold (shows a live "X/Y"
    /// progress suffix and description-panel line, unconditionally, even after the gate
    /// opens).
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), nameof(piOsMenu.LockUnfinishedLevels))]
    public static class HubUnlockPatch
    {
        // SHGUIcommanderbutton.Update() has a private once-only fallback that forces
        // color to gray the first frame it sees IsLocked = true with recursiveColorSet
        // still false. This only matters when we pair IsLocked = true with a non-gray
        // color (the excluded-level blue), so recursiveColorSet is set via reflection
        // to pre-empt that one-frame flash back to gray.
        private static readonly FieldInfo RecursiveColorSetField =
            AccessTools.Field(typeof(SHGUIcommanderbutton), "recursiveColorSet");

        public static void Postfix()
        {
            // Archipelago mode can be disabled entirely to play vanilla; skipping this pass
            // then leaves the native lock/scramble result untouched. Note that
            // LockUnfinishedLevels() itself can also go uncalled by native code entirely
            // (see Mod.cs's OnSceneWasLoaded) -- if this ever looks broken, check that first.
            if (!SuperhotArchipelago.Core.Mod.IsEnabled)
            {
                return;
            }

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

                // Never returns null -- falls back to a sentinel LevelInfo with default
                // fields (ID 0, SceneFileName "") on a lookup miss, so we guard on
                // SceneFileName instead of null to avoid misreading a miss as level id 0
                // ("Kick", always treated as unlocked).
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

                // Excluded levels (ExcludeSlowLevels) have no access item to ever receive,
                // so they're always treated as unlocked rather than permanently locked.
                // Computed here since the secret-badge block below needs it too.
                bool isExcluded = SuperhotArchipelago.Core.Mod.Connection != null
                    && SuperhotArchipelago.Core.Mod.Connection.IsLevelExcluded(entry.Order);

                // Native code paints the "CRACKED!" badge/description from stale save
                // flags with no knowledge of Archipelago's own tracked state, so this
                // overrides it every pass based on the real tracked secret status.
                // AddScrollingNotification already calls RemoveScrollingNotification
                // internally, so this can't stack duplicate badges.
                if (entry.HasSecret)
                {
                    // An excluded level's secret state isn't meaningfully trackable
                    // (IsSecretCompleted() always reports true for one), so it gets its
                    // own "INACTIVE" badge/description instead of a claim we can't verify.
                    if (isExcluded)
                    {
                        button.AddScrollingNotification("INACTIVE".PadRight(8), scrolling: false);

                        string notCrackedInactive = "MENU_SECRETNOTCRACKED".T();
                        string crackedInactive = "MENU_SECRETCRACKED".T();
                        if (button.data.Contains(crackedInactive))
                        {
                            button.data = button.data.Replace(crackedInactive, "INACTIVE");
                        }
                        else if (button.data.Contains(notCrackedInactive))
                        {
                            button.data = button.data.Replace(notCrackedInactive, "INACTIVE");
                        }
                    }
                    else
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
                }

                // "34 - Free" has a second gate on top of the normal item-unlock: it stays
                // locked until enough of the other levels are completed. This mirrors the
                // real gate in Core/LevelAccessGuard.cs (this is only the display half) and
                // is computed unconditionally, not just once unlocked, so the progress
                // shows for the whole run rather than only appearing once the item arrives.
                bool isFreeLevel = entry.Order == SuperhotArchipelago.Core.LevelCatalog.Levels.Count;
                int freeRequired = 0;
                int freeCompleted = 0;
                bool freeStillGated = false;
                if (isFreeLevel)
                {
                    // Read via GetLevelsRequiredForFree() rather than
                    // ArchipelagoConnection.LevelsRequiredForFree directly so the
                    // displayed "X/Y" stays in agreement with what LevelAccessGuard.cs
                    // actually enforces.
                    freeRequired = SuperhotArchipelago.Core.Mod.Locations?.GetLevelsRequiredForFree() ?? 0;
                    freeCompleted = SuperhotArchipelago.Core.Mod.Locations?.CountOtherLevelsCompleted() ?? 0;
                    freeStillGated = freeCompleted < freeRequired;

                    // Pushed into the description/preview panel (button.data), same as
                    // native's own secret cracked/not-cracked line. Rebuilt from
                    // LevelButtonCapturePatch's cached original every pass so repeated
                    // refreshes can't double-insert the line, and shown unconditionally
                    // (not just while gated) so it doesn't vanish the moment the
                    // requirement is met.
                    if (SuperhotArchipelago.Core.ButtonTextCache.TryGetData(levelInfo.ID, out string cleanData))
                    {
                        button.data = $"\n\n{freeCompleted}/{freeRequired} LEVELS COMPLETED\n\n" + cleanData;
                    }
                }

                // Padded to 8 chars to match the fixed-width status field every other
                // button uses, keeping the '│' column aligned across the hub list.
                string? freeStatusSuffix = isFreeLevel ? $"{freeCompleted}/{freeRequired}".PadRight(8) : null;

                bool unlocked = entry.Order == 1
                    || isExcluded
                    || SuperhotArchipelago.Core.UnlockState.IsUnlocked(levelInfo.ID);

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
                    button.ButtonText = scrambled + "│" + (freeStatusSuffix ?? "MENU_LOCKED8CHARS".T());
                    button.RefreshText();
                    button.SetLocked(true);
                    continue;
                }

                bool completed = SuperhotArchipelago.Core.Mod.Locations?.IsLevelCompleted(levelInfo.ID) ?? false;

                if (isFreeLevel && freeStillGated)
                {
                    button.ButtonText = cleanName + "│" + freeStatusSuffix;
                    button.RefreshText();
                    button.IsLocked = true;
                    button.color = 'z';
                    button.SetColorRecursive('z');
                    continue;
                }

                // Excluded levels fall into this "unlocked" branch (not the genuinely
                // locked one) but still get garbled text plus a distinct blue color and
                // "INACTIVE" suffix instead of gray, so they read as their own state
                // rather than looking identical to a genuinely locked level.
                const char ExcludedColor = 'b';
                const string ExcludedStatusSuffix = "INACTIVE";

                string displayName = isExcluded
                    ? StringScrambler.GetScrambledString(cleanName.ToUpper(), 0.9f, "▀▄█▌▐░▒▓■▪01 ")
                    : cleanName;

                // "34 - Free" keeps its "X/Y" suffix even after falling through to this
                // normal-unlocked branch, rather than resetting to plain "LEVEL" text.
                string statusSuffix = isFreeLevel
                    ? freeStatusSuffix!
                    : (isExcluded ? ExcludedStatusSuffix : "MENU_LEVEL8CHARS".T());
                button.ButtonText = displayName + "│" + statusSuffix;
                button.RefreshText();

                // NOT SetLocked(!completed) -- also sets IsLocked and would block clicks
                // on never-yet-played levels; color is set directly instead so only color
                // tracks completion. isExcluded overrides `completed` for color entirely
                // since IsLevelCompleted() always reports excluded levels as completed.
                // IsLocked = isExcluded only blocks the hub shortcut; normal in-level
                // progression into an excluded level is unaffected by
                // Core/LevelAccessGuard.cs's separate launch-time gate.
                button.IsLocked = isExcluded;
                char targetColor = isExcluded ? ExcludedColor : (completed ? 'w' : 'z');
                button.color = targetColor;
                button.SetColorRecursive(targetColor);

                // Pre-empts native's one-frame flash back to gray -- see
                // RecursiveColorSetField's own comment for the root cause.
                if (isExcluded)
                {
                    RecursiveColorSetField.SetValue(button, true);
                }
            }
        }
    }
}
