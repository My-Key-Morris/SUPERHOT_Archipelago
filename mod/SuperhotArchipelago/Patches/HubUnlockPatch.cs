using System.Reflection;
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
    /// A later, separate follow-up request added a wrinkle to the "Unlocked" states
    /// above specifically for ExcludeSlowLevels-excluded levels: "boring levels should
    /// be garbled if excluded" -- cosmetic only at first, the level stayed exactly as
    /// clickable as this class already always treated it, only its button text was
    /// scrambled instead of clean. A follow-up to that follow-up: garbled text plus
    /// this class's usual gray/white made an excluded level indistinguishable from a
    /// genuinely locked one at a glance -- fixed with a distinct blue color and a
    /// literal "INACTIVE" status suffix (instead of "LOCKED"/"LEVEL"), so it's still
    /// visually grouped with the "not a real check" row of buttons without being
    /// mistaken for something that just needs an item. A third follow-up made
    /// "INACTIVE" actually mean it: excluded levels' hub buttons are now genuinely
    /// unclickable (`IsLocked = true`), not just styled to look that way -- real
    /// launch access into an excluded level (via normal in-level progression) is
    /// unaffected, since `Core/LevelAccessGuard.cs`'s own separate launch-time gate
    /// still always lets one through by design; only the hub shortcut is blocked.
    ///
    /// A related real bug report caught the secret badge/description text (see below)
    /// not accounting for excluded levels at all: `LocationManager.IsSecretCompleted()`
    /// deliberately returns `true` unconditionally for an excluded level (no real
    /// secret location to track), which this class used to trust at face value,
    /// always painting "CRACKED!" for e.g. `"32 - Longway"` (excluded by default, and
    /// one of the few excluded levels with a real secret) regardless of whether it was
    /// ever actually found. Fixed by giving excluded levels their own "INACTIVE"
    /// badge/description instead of trusting a claim this mod has no way to verify for
    /// them.
    ///
    /// A fourth state exists for exactly one button, "34 - Free": still short of the
    /// other-levels-completed threshold (Core/LevelAccessGuard.cs's second gate on it).
    /// Unlike the other three states, this one is checked independently of
    /// unlocked/not-unlocked and applies on both sides of it -- confirmed by a real user
    /// report that only showing it after the access item was already received made it
    /// look broken, since most of a run happens before that item ever arrives. While
    /// still gated: the status suffix reads a live "done/needed" count instead of
    /// "LOCKED"/"LEVEL" (garbled name + progress suffix if the item isn't held yet,
    /// clean name + progress suffix and genuinely locked -- IsLocked = true, unlike the
    /// general unlocked/not-yet-completed case -- once it is), and the same count is
    /// pushed into the button's right-panel description text (SHGUIcommanderbutton.data),
    /// so it shows up as a legible line in the hub's preview panel when this row is
    /// highlighted -- the same treatment the "secret cracked/not cracked" line gets for
    /// levels that have a secret, confirmed via decompile.
    ///
    /// Real bug found by a direct user report, fixed after the above was already live:
    /// both the "X/Y" status suffix and the description-panel count line used to only
    /// show while still gated, so they silently vanished the instant the requirement
    /// was actually met -- looking like the feature had broken, not succeeded. Both are
    /// now unconditional on isFreeLevel instead of freeStillGated, so "34 - Free" keeps
    /// showing its "X/Y" count for the rest of the run even once satisfied; only
    /// IsLocked/color (the actual gate, still driven by freeStillGated) change once it
    /// opens.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), nameof(piOsMenu.LockUnfinishedLevels))]
    public static class HubUnlockPatch
    {
        // Real bug found by a direct user report: the excluded-level blue color
        // (below) showed correctly for exactly one frame, then flipped to gray.
        // Root cause, confirmed via decompile of SHGUIcommanderbutton.Update():
        // native code has its own once-only fallback --
        // `if (IsLocked && !recursiveColorSet) { recursiveColorSet = true;
        // SetColorRecursive('z'); }` -- that runs every single frame until it fires
        // exactly once per button instance (buttons are recreated, and this private
        // field resets to false, every time the hub view is rebuilt). Every other
        // place in this class that sets IsLocked = true also happens to already use
        // gray ('z') as the color (SetLocked(true) sets both internally; the "34 -
        // Free" gated branch below sets both to 'z' itself), so this fallback firing
        // was always a harmless no-op coincidence -- until "Round 39" made excluded
        // levels the first case to pair IsLocked = true with a different color
        // (blue), which is what actually surfaced this. recursiveColorSet is
        // private, so it's set directly via reflection wherever this class assigns
        // IsLocked = true with a non-gray color, to tell native code "already
        // handled" and pre-empt the one-frame-later gray override.
        private static readonly FieldInfo RecursiveColorSetField =
            AccessTools.Field(typeof(SHGUIcommanderbutton), "recursiveColorSet");

        public static void Postfix()
        {
            // Real, explicit user request: Archipelago mode can be turned off entirely to
            // play vanilla (see Core/Mod.cs's IsEnabled/Patches/ArchipelagoModeTogglePatch.cs).
            // Skipping this whole pass while off leaves the native lock/scramble result
            // (which just ran, right before this Postfix) completely untouched -- real
            // save-progress-based locking and colors, no AP overlay text at all.
            //
            // Real bug found by a live playtest: this Postfix can go silently uncalled
            // entirely, for reasons that have nothing to do with the checks below -- see
            // Mod.cs's OnSceneWasLoaded for the actual root cause (a stale "storyFinished"
            // save flag) and fix (resetting it). Worth knowing about if this ever looks
            // broken again: nothing in this method running is a much likelier explanation
            // than a logic bug inside it, since piOsMenu.LockUnfinishedLevels() -- the
            // method this Postfix is attached to -- native code sometimes doesn't call at
            // all.
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

                // Real, explicit user request (ExcludeSlowLevels): an excluded level has no
                // access item to ever receive (see apworld/superhot/__init__.py's
                // create_items), so it can never earn "unlocked" through
                // UnlockState.IsUnlocked -- always shown/treated as unlocked instead, same
                // as level 1's own special case, so it never displays as permanently
                // locked in the hub for a level the player can freely walk into anyway
                // (see Core/LevelAccessGuard.cs). Computed here, before the secret-badge
                // block below, since that block needs it too now.
                bool isExcluded = SuperhotArchipelago.Core.Mod.Connection != null
                    && SuperhotArchipelago.Core.Mod.Connection.IsLevelExcluded(entry.Order);

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
                    // Real bug found by a direct user report: "32 - Longway" (excluded by
                    // default, and one of the few excluded levels with a real secret)
                    // always showed "CRACKED!" once excluded, regardless of whether its
                    // secret had ever actually been found. Root cause:
                    // LocationManager.IsSecretCompleted() deliberately returns true
                    // unconditionally for an excluded level (same "always show completed,
                    // never grey" reasoning as IsLevelCompleted -- there's no real secret
                    // location to track for one at all), which this block used to trust
                    // at face value. An excluded level's secret state genuinely isn't
                    // known, so it gets its own literal "INACTIVE" badge/description here
                    // instead of a real (and, for this level, always wrong) cracked/not
                    // cracked claim -- the same word the main button/status suffix below
                    // already use for "not a real tracked check."
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

                // Real, explicit user request: "34 - Free" gets a second gate on top of
                // the normal item-unlock below -- even once its own access item is
                // received, it stays locked until enough of the other 31 levels are
                // actually completed. This has to stay in sync with the real gate in
                // Core/LevelAccessGuard.cs, which is what actually blocks entry -- this
                // is only the display half. Computed here, before the unlocked check
                // below, and NOT conditioned on unlocked -- a real user report showed
                // this only appearing once the item was already in hand looked like it
                // wasn't working at all, since most of a run happens before that. Runs
                // unconditionally instead, same reasoning as the secret-crack block
                // above: the progress is worth showing the whole time, so the one row
                // in the whole locked list that isn't a plain "-LOCKED-"/noise is a
                // (deliberately subtle) hint of which garbled row is actually Free.
                bool isFreeLevel = entry.Order == SuperhotArchipelago.Core.LevelCatalog.Levels.Count;
                int freeRequired = 0;
                int freeCompleted = 0;
                bool freeStillGated = false;
                if (isFreeLevel)
                {
                    // Real bug found by a direct user question: see
                    // LocationManager.GetLevelsRequiredForFree()'s own doc -- reading it
                    // here instead of ArchipelagoConnection.LevelsRequiredForFree
                    // directly keeps this displayed "X/Y" in agreement with what
                    // LevelAccessGuard.cs actually enforces.
                    freeRequired = SuperhotArchipelago.Core.Mod.Locations?.GetLevelsRequiredForFree() ?? 0;
                    freeCompleted = SuperhotArchipelago.Core.Mod.Locations?.CountOtherLevelsCompleted() ?? 0;
                    freeStillGated = freeCompleted < freeRequired;

                    // Also push it into the right-side description/preview panel, the
                    // same way piOsMenu.PrepareLevelDescription() shows "secret
                    // cracked"/"not cracked" for levels that have one -- confirmed via
                    // decompile that SHGUIcommanderbutton.data is exactly what gets
                    // pushed into that panel (listLink.rightPanel.text) the moment this
                    // row is highlighted. Rebuilt from LevelButtonCapturePatch's cached,
                    // pre-scramble original every pass -- never mutated in place -- so
                    // repeated hub refreshes can't double-insert the line.
                    //
                    // Real bug found by a direct user report: this used to only show
                    // while freeStillGated was true, so the count line (and the "X/Y"
                    // status suffix below) both silently disappeared the moment the
                    // requirement was actually met -- looking like the feature had
                    // broken rather than succeeded. Fixed by making both unconditional
                    // on isFreeLevel instead of freeStillGated: the requirement is worth
                    // showing for the rest of the run regardless of whether it's already
                    // satisfied, same "worth showing the whole time" reasoning this
                    // block's own comment already used to justify computing it before
                    // the item is even held.
                    if (SuperhotArchipelago.Core.ButtonTextCache.TryGetData(levelInfo.ID, out string cleanData))
                    {
                        button.data = $"\n\n{freeCompleted}/{freeRequired} LEVELS COMPLETED\n\n" + cleanData;
                    }
                }

                // Padded to 8 chars to match the fixed-width status field every other
                // status string here uses (MENU_LOCKED8CHARS/MENU_LEVEL8CHARS are both
                // baked to that width) -- keeps the '│' column aligned with every other
                // hub button instead of just this one shifting. Always set (not just
                // while freeStillGated) for the same reason as button.data above -- see
                // that comment.
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

                // Real, explicit user follow-up request: "boring levels should be
                // garbled if excluded." An excluded level is always "unlocked" for
                // text/color-branch purposes (see the `unlocked` computation above --
                // it still falls into this branch, not the genuinely-locked one), but
                // since "Round 39" its hub button is deliberately made unclickable
                // below (`button.IsLocked = isExcluded`), unlike every other level that
                // reaches this branch. The button text itself gets scrambled, the same
                // GetScrambledString call the genuinely-locked branch above uses.
                //
                // Real bug found by the user's own follow-up report: garbled text +
                // gray color (the very first fix for this) made an excluded level look
                // pixel-identical to a genuinely locked one -- no way to tell "this is
                // permanently playable, just not tracked" apart from "this needs an
                // item you haven't received yet." Fixed with two more distinguishing
                // touches, both purely cosmetic, same as the rest of this block:
                // ExcludedColor (blue, see below) instead of gray, and a literal
                // "INACTIVE" status suffix (exactly 8 characters -- no padding needed
                // to match the fixed-width column every other suffix here uses)
                // instead of the normal "LEVEL" text.
                const char ExcludedColor = 'b';
                const string ExcludedStatusSuffix = "INACTIVE";

                string displayName = isExcluded
                    ? StringScrambler.GetScrambledString(cleanName.ToUpper(), 0.9f, "▀▄█▌▐░▒▓■▪01 ")
                    : cleanName;

                // "34 - Free" keeps showing its "X/Y" status suffix even once the gate
                // above is satisfied and it's fallen through to this normal-unlocked
                // branch -- see the real bug fixed above where this used to reset to
                // the plain "LEVEL" text the instant the requirement was met.
                string statusSuffix = isFreeLevel
                    ? freeStatusSuffix!
                    : (isExcluded ? ExcludedStatusSuffix : "MENU_LEVEL8CHARS".T());
                button.ButtonText = displayName + "│" + statusSuffix;
                button.RefreshText();

                // NOT SetLocked(!completed) -- see the class doc for the real bug that
                // was. Genuinely unlocked, non-excluded levels must stay clickable
                // regardless of completion; only color should track completion.
                //
                // isExcluded overrides `completed` for color entirely -- LocationManager
                // .IsLevelCompleted() unconditionally reports an excluded level as
                // completed (Round 30), which would otherwise read as plain white/"done".
                //
                // Real, explicit user request: excluded levels used to stay genuinely
                // clickable (deliberately, so a player could still freely walk into one
                // that just isn't tracked by Archipelago) -- since revised to also block
                // the click itself, matching "INACTIVE" actually meaning inactive rather
                // than just looking that way. This only blocks the hub button; natural
                // in-level progression into an excluded level (finishing the level right
                // before it) is untouched, since Core/LevelAccessGuard.cs -- the real,
                // separate launch-time gate -- still always lets an excluded level
                // through by design.
                button.IsLocked = isExcluded;
                char targetColor = isExcluded ? ExcludedColor : (completed ? 'w' : 'z');
                button.color = targetColor;
                button.SetColorRecursive(targetColor);

                // Real bug found by a direct user report: this blue color showed for
                // exactly one frame then flipped to gray. See this class's own
                // RecursiveColorSetField doc for the full root cause -- short version,
                // native SHGUIcommanderbutton.Update() forces gray back on, once, the
                // first frame it sees IsLocked = true on a button whose private
                // recursiveColorSet flag is still false. Pre-empt it here.
                if (isExcluded)
                {
                    RecursiveColorSetField.SetValue(button, true);
                }
            }
        }
    }
}
