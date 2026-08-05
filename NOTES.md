# Status and open questions

## Live testing log (real Steam install)

Real progress: the mod now loads cleanly in the actual game (Unity 2020.3.24f1, Mono
net35 runtime module) -- `SuperhotArchipelago loading.` / `LevelCatalog loaded 34 levels`
both printed with no errors, once `Archipelago.MultiClient.Net.dll` was actually in
`UserLibs/` (a missing-file goof the first time, not a code bug).

One real bug found by that run: `MelonPreferences_Category.CreateEntry(...)` doesn't
write anything to disk by itself (confirmed by decompiling `MelonLoader.dll`) -- closing
the game after a clean first run left no `UserData/SuperhotArchipelago.cfg` at all.
Fixed by calling `category.SaveToFile()` explicitly in `Core/Config.cs` right after
creating the entries. Rebuilt and redeployed to both the copy and the real install.

Config file location was also wrong in docs/log messages: MelonLoader shares one
`UserData/MelonPreferences.cfg` across all mods (a `[SuperhotArchipelago]` section within
it), not a per-mod file -- fixed in `TESTING.md`, `Core/Config.cs`, `Core/Mod.cs`.

**A real connection to a locally-hosted server worked end to end.** Confirmed from actual
server console output: check-sending works (`Kick (Intro) Complete` and
`Hacker Room (2) Complete` both correctly triggered `Level Access: ...` items being sent
back). Two things found from that same real playtest:

1. **Hub showed everything unlocked from the start.** Root cause isn't the mod --
   `HubUnlockPatch` only ever *adds* unlocks on top of the native logic, never removes
   any, and the test was run on a save that had already progressed through/finished
   SUPERHOT normally before the mod was installed. Native logic alone already unlocks
   everything for a save like that. Confirmed by reading `SaveManager.cs`
   (`LoadGameAndControlsAsync` -> `Storage.GetContainerAsync("super.hot", ...)`, a fixed
   filename, `StorageResultType.NotFound` -> treated as a brand new game) -- renaming
   (not deleting) the real save file to anything other than exactly `super.hot` gives a
   clean slate without losing the original, since the loader only recognizes that exact
   name.
2. **No "goal achieved" announcement when the final level completed**, even though its
   location check correctly sent. Real gap, now fixed: sending a location check for the
   final level is not the same as telling the server the player won -- confirmed by
   decompiling `Archipelago.MultiClient.Net.dll`, goal completion is a separate
   `StatusUpdatePacket`/`ArchipelagoClientState.ClientGoal` signal
   (`Session.SetGoalAchieved()`) that nothing in the mod was ever sending. Added to
   `Core/LocationManager.cs`: when the completed level is the last one in
   `levels.json`'s order, the mod now also calls `SetGoalAchieved()`. This also happens
   to be the first real confirmation that `levels.json`'s last entry
   ("Hacker Room (2)" / `LevelTest#77 HackerRoom`) genuinely is where the game's real
   ending plays out -- worth remembering as real playtest evidence, not just a guess,
   next time the duplicate-scene-name caveats get revisited.

## Architecture correction: HubUnlockPatch alone doesn't gate a first playthrough

Real discovery from testing on a genuinely fresh save: the rich per-level hub browser
(`piOsMenu`'s "storylevels" menu node -- individually lockable icons, what
`HubUnlockPatch.cs` targets via `LockUnfinishedLevels()`) is **not** what a first-time
player sees. A fresh save's hub instead shows a single `superhot.exe` icon (the "last"
menu node, `piOsMenu.cs` ~413-433) whose click handler always launches
`LevelSetup.GetNewLevelInfo()` -- "whatever comes next after `highestfinishedLevel`" --
completely independent of `LockUnfinishedLevels()`/`HubUnlockPatch`. So the original
design only ever gated the *post-completion* "replay any level" browser, not a normal
first playthrough, which is what most players (and this whole project) actually care
about.

Fixed with a new patch, `Patches/LevelGatePatch.cs`, on `SHGUI.LaunchLevelAppTunnels(LevelInfo, bool, bool)`
-- confirmed by reading `piOsMenu.PrepareLevelCommanderButtonForLevel()` that *every*
level button's click handler, whether it's the single `superhot.exe` icon or one of the
many icons in the post-completion browser, funnels through this exact same call. One
patch now gates both cases. Blocks the launch (Harmony Prefix returns false) unless the
level is the first one or has been unlocked via a received AP item, and surfaces a
message via `TextManager.AddUptitleToQueue`.

`HubUnlockPatch` is still worth keeping -- it makes the post-completion rich browser's
lock icons visually match reality -- but `LevelGatePatch` is what actually enforces
anything during normal play. Confirmed in-game that the block message displays correctly
-- see the next section for why "legitimate unlocks let the click through" did NOT work
as-is, and the fix for that.

## Fixed: `superhot.exe` button could never reach a non-sequential unlocked level

Real playtest evidence this was a genuine deadlock, not theoretical: finishing "Kick"
(order 1) correctly granted "Level Access: Train" per the log
(`Unlocked 'Train' (scene 'TrainFight') from a received item.`), but clicking the single
`superhot.exe` hub button repeatedly only ever tried to launch "Dark Alley" (order 2, the
strictly-next level) and `LevelGatePatch` correctly kept blocking it --
`Blocked launch of 'Dark Alley' (scene 'CezaryDarkAlley_P') -- not yet unlocked.` printed
~15 times as the user kept clicking. There was no way to ever reach "Train" through the
single-button flow.

Root cause, confirmed by decompiling `LevelSetup.cs:886`: `GetNewLevelInfo()` (what that
button always calls) is hardcoded --

```csharp
public static LevelInfo GetNewLevelInfo()
{
    string text = (string)SaveManager.Instance.GetValue("highestfinishedLevel");
    if (text == null) { return Levels[1]; }
    LevelInfo levelInfo = GetLevelInfoByUniqueSHName(text);
    if (levelInfo.ID == -1) { levelInfo = GetLevelInfoBySceneFileName(text); }
    for (int i = 0; i < Levels.Count; i++)
    {
        LevelInfo levelInfo2 = Levels[i];
        if (levelInfo2.ID > levelInfo.ID) { return levelInfo2; }
    }
    return Levels[1];
}
```

-- to return strictly the next level after `highestfinishedLevel` in original campaign
order, with zero awareness of what Archipelago has actually granted. `LevelGatePatch`
blocking the wrong (locked) level was working exactly as designed; the real bug was that
nothing was ever offering the *right* (unlocked) level in the first place.

Fixed with two pieces:

- `Core/CompletionState.cs`: a local, in-memory record of which scenes this session has
  already completed (separate from `UnlockState`, which tracks what's been granted). Only
  needed so the fix below can tell "already played, don't re-offer" apart from "unlocked
  but not yet played." Wired into `Core/LocationManager.cs`'s `CheckLocation` --
  `CompletionState.MarkCompleted(sceneName)` runs right after a check is confirmed to
  match a catalog entry.
- `Patches/NextLevelPatch.cs`: a Harmony Prefix on `LevelSetup.GetNewLevelInfo()` that
  ignores the native sequential search and instead scans `LevelCatalog.Levels` in catalog
  order for the first entry that's reachable (`order == 1` or `UnlockState.IsUnlocked`)
  and not yet completed (`!CompletionState.IsCompleted`), resolves it to a real
  `LevelInfo` via `GetLevelInfoBySceneFileName`, and returns it via `__result` (skipping
  the original with `return false`). Falls back to the native method (`return true`) if
  no such level is found, so a fully-completed catalog or an empty one can't hand back
  null and crash the hub.

Built and redeployed (`SuperhotArchipelago.dll`/`.pdb`, plus `data/levels.json`) to both
the real Steam install and the workspace copy.

## Reverted: smart-jump patch, in favor of the native LEVELS folder browser

The `GetNewLevelInfo()` smart-jump fix above got built and deployed, but real screenshots
from the next playtest showed something better was already there and working: SUPERHOT's
own `LEVELS` folder (`piOsMenu.cs`'s `"storylevels"` case) is reachable from the hub once
a level or two is done, lists every campaign level individually
(`AppendLevelData(sHGUIcommanderview, LevelSetup.Levels, ...)`), and -- confirmed by the
screenshots -- already shows correct per-level lock state
(`CRACKED!` for unlocked, `-LEVEL->` for locked) for levels far outside strict sequence,
because that case calls `LockUnfinishedLevels()` even on an unfinished save
(`if (!storyFinished || challenge) { LockUnfinishedLevels(); }`, `piOsMenu.cs` ~476-481)
-- which is exactly what `HubUnlockPatch.cs` was already patching. In other words,
per-level out-of-order picking via a real menu was already functioning correctly; the
earlier note claiming `HubUnlockPatch` "only affects the post-completion browser" was
wrong, or at least not the whole picture -- it works pre-completion too, once the player
navigates to `LEVELS` instead of clicking the single `superhot.exe` shortcut.

Given that, the user asked for exactly this -- a real, always-picks-are-visible level
select -- and explicitly did not want the single button auto-skipping to a computed
"next" level. So `Patches/NextLevelPatch.cs` and `Core/CompletionState.cs` (which existed
only to support it) were deleted, and `Core/LocationManager.cs`'s
`CompletionState.MarkCompleted()` call was reverted. `LevelSetup.GetNewLevelInfo()` is
native again: `superhot.exe` only ever offers the strictly-next sequential level, and
`LevelGatePatch` still blocks it if that level isn't unlocked -- which is now the
intended nudge to go check the `LEVELS` folder instead of a dead end. `HubUnlockPatch`
and `LevelGatePatch` are unchanged and remain the two patches actually doing the work:
one drives the `LEVELS` browser's lock icons, the other enforces the lock when a level
launch is attempted from anywhere.

Rebuilt (0 errors) and redeployed to both installs.

## Four real bugs from the next playtest, fixed together

Server chat log evidence made the first one obvious: `Cyberspace (1)` was granted as an
item (`Hospital Complete -> Level Access: Cyberspace (1)`) but never became launchable
anywhere. Investigating that led to a bigger foundational fix that also explains long-
standing oddities with the other duplicate-scene entries.

**Bug 1 -- duplicate scene names silently broke matching.** Root cause: the mod matched
levels at runtime by Unity scene name (`LevelInfo.SceneFileName`), but four of our 34
catalog entries reuse a scene for a different story beat (`TheyAreYourTools_C_2` x3,
`"LevelTest#77 HackerRoom"` x2, `"piCyberSpace#1_E"` x2 -- see levels.json's
`_caveats`). Every dictionary keyed by scene name (`SceneNameToLevel`) can only hold one
entry per duplicate, so the *other* occurrence(s) silently pointed nowhere -- exactly
matching "Cyberspace (1) didn't seem to link to any of the levels" (it lost the
last-one-wins race to "Cyberspace (2)"'s dictionary entry).

Fixed by switching every runtime lookup from scene name to `LevelInfo.ID` -- confirmed by
decompiling `LevelSetup.LoadStoryLevels()`/`AddLevelInfo()`: `ID` is assigned as a
straight index into the Story/Level XML in document order (`AddLevelInfo(list[i], i)`),
genuinely unique per level instance, duplicates included, and it's already sitting right
on every `LevelInfo` object with no lookup needed. Added `LevelEntry.LevelId` (=
`Order - 1`, since our own `order` field was built from the same document order) and
`LevelCatalog.LevelIdToLevel`. Updated `UnlockState` (now tracks `HashSet<int>` level
ids), `ItemManager.ApplyItem`, `LocationManager.CheckLocation` (now takes an int id),
`LevelCompletePatch` (reports `finished.ID`), `LevelGatePatch` (matches `level.ID`
directly -- no lookup needed at all, it already has the real `LevelInfo`), and
`HubUnlockPatch` (resolves each hub button's real `LevelInfo` via
`LevelSetup.GetLevelInfoByUniqueSHName(button.LevelToBeLoadedName)`, then matches on
`.ID`). `SceneNameToLevel` is kept only for reference/logging, no longer used for gating.

**Bug 3 -- clicking through a level's ending fade skipped straight to the next level,
unlocked or not.** Confirmed by decompiling `LevelSetup.cs`: the end-of-level fade's
click-through calls `LevelSetup.LoadNextLevel()` -> `LoadNextLevelInternal()` ->
`GetNextLevelInfo()` (a *different*, even more naive method than the hub's
`GetNewLevelInfo()` -- just `Levels[GetLevelIndexByID(CurrentLevelInfo.ID) + 1]`, the
literal next entry in document order) -> `LoadLevel(...)`, entirely bypassing
`SHGUI.LaunchLevelAppTunnels()`, the only method `LevelGatePatch` guards. That let a
player walk from level 3 into level 4 without ever holding its access item, as long as
they never went back through the hub. Fixed with a new patch,
`Patches/DirectLevelSkipPatch.cs`: a Prefix on `LevelSetup.LoadNextLevel(bool)` that
computes what `GetNextLevelInfo()` would return, applies the same unlock check
`LevelGatePatch` uses, and if it's not allowed, skips the original method (so none of its
side effects run -- no kill-count reset, no time-scale pause, no scene load) and sends
the player back to the hub via `SHGUI.current.LaunchLevelAppTunnels("SHMenu", false)`
(the same string-overload call the native "return to menu" paths use, confirmed a
separate overload from the one `LevelGatePatch` patches, so no recursive-patch risk)
instead of leaving them stuck on the fade screen.

**Bug 4 -- locked level names were unreadable, not just gray.** SUPERHOT natively
scrambles a locked level's displayed name into noise (`piOsMenu.LockUnfinishedLevels()`'s
locking loop calls `StringScrambler.GetScrambledString` on the button's `prefix`), on top
of separately graying it out via `SetLocked(true)` (confirmed by decompiling
`SHGUIcommanderbutton.SetLocked()`: it just flips a color code, `'z'` for locked vs `'w'`
normal -- completely independent of the scrambling). The user explicitly wants names
always legible, gray being the only "locked" signal. Fixed with two new pieces:
`Core/ButtonTextCache.cs` (a small cache of each level's clean name, keyed by
`LevelId`) and `Patches/LevelButtonCapturePatch.cs` (a Postfix on the private
`piOsMenu.PrepareLevelCommanderButtonForLevel()` that snapshots a button's clean name the
moment it's built, before any locking pass can scramble it -- guarded to skip the
`customName != ""` case so the single "superhot.exe" button's special label never
pollutes a real level's cached name). `HubUnlockPatch` now restores each tracked button's
`ButtonText` from that cache every time it runs (rebuilding it as `<clean name>│<status>`
with the status suffix chosen from our own unlock decision -- `"MENU_LEVEL8CHARS".T()` or
`"MENU_LOCKED8CHARS".T()`, the same two localization keys the native code itself uses in
each branch), then calls `SetLocked()` itself as the single source of truth for color,
superseding whatever the native pass already decided rather than only ever loosening.

**Bug 2 -- check/item names didn't match the in-game numbering.** Updated
`data/levels.json` (both the `apworld/superhot/` source and the `mod/SuperhotArchipelago/`
copy, which must stay identical) so every level's `name` carries a zero-padded position
prefix -- `"01 - Kick"`, `"02 - Dark Alley"`, ... `"34 - Hacker Room (2)"` -- matching how
the in-game hub lists levels in order. Note this is our own 1-34 catalog order, not the
native `.lvl` numbering scheme visible in the hub browser itself (that one skips numbers
for excluded intermission content and uses localized strings we don't have reliably
extracted yet -- flagged in the updated `_caveats` for a future pass if a real playthrough
transcript surfaces the exact native names).

Since this changes AP location/item **names** (the keys the Python world builds
`location_table`/`item_table` from), it requires a new `.apworld` build -- there's no
Archipelago Launcher GUI available in this environment, so instead of using "Build
APWorlds," the previously-built `dist/superhot.apworld` (which already has the correct
`version`/`compatible_version` manifest fields stamped by that tool) was unzipped, just
its `data/levels.json` was swapped for the updated one, and it was rezipped with the same
member paths -- verified the result is a valid zip, `data/levels.json` parses with all 34
new names, and `pkgutil.get_data` can read it back out via a real zipimport. Rebuilding
the world's `.apworld` means **the currently-running AP server session needs a fresh
Generate + Host from this new file** for the renamed locations/items to take effect --
existing in-progress connections were generated from the old datapackage. This does not
affect the SUPERHOT save file itself, only the Archipelago session/seed.

Mod rebuilt (0 errors) and redeployed (`SuperhotArchipelago.dll`/`.pdb` plus the updated
`data/levels.json`) to both installs.

## Root cause found: `LevelId = Order - 1` was wrong all along

The very next playtest (after the four fixes above) turned up what looked like four more
bugs -- garbled names persisting on later levels, having to start via `superhot.exe`
again, the wrong level lighting up when an item was received ("unlocked Cage Fight but
level 6 lit up"), and check names not matching what was actually played ("02 - Dark
Alley" reported complete after finishing the *first* level). All four turned out to be
the same one bug.

`LevelEntry.LevelId` (added in the ID-based-matching fix above, to work around duplicate
scene names) was computed as `Order - 1`, on the assumption that this file's 1-34 order
exactly matches the real game's `LevelInfo.ID` sequence. That assumption was never
actually verified against the real data -- and it's wrong. Confirmed by extracting the
*real* `GameData` Story/Level XML directly (it's stored as plain readable text inside
`SH_Data/resources.assets`, no decompiler needed for this part -- found via `strings`/grep
on the raw `.assets` file, then parsed as XML): the real document has **49** `<Level>`
elements, not 34. `LevelSetup.LoadStoryLevels()` assigns `LevelInfo.ID` as a straight,
unfiltered index over all 49 (confirmed by decompiling `LevelSetup.cs`:
`AddLevelInfo(list[i], i)`), including `SHMenu` and 15 `_SEGWAYSTUB` dialogue-interlude
entries that this file deliberately excludes from its own 34-entry catalog. Excluding
those before numbering meant `order - 1` drifted further and further from the real `ID`
as more excluded entries piled up earlier in the sequence -- e.g. "Kick" is really game id
1 (matches order 1 fine, nothing excluded before it yet), but "Cage Fight" is really game
id 9, not `order 8 - 1 = 7` ("Jump"'s real id) -- which is exactly the mismatch that made
Cage Fight's unlock visually light up Jump instead.

Fixed properly this time: `LevelEntry.LevelId` is no longer computed, it's read directly
from a new `"gameId"` field added to every entry in `data/levels.json`, sourced from the
real 49-element extraction so it can never silently drift out of sync with the formula
again. Also used this same extraction to fix the check names for real (previous "numbered
prefix" attempt was our own synthetic 1-34 counting, not the actual game's numbering) --
each `<Level>`'s `name` attribute is a loc key like
`GameData.Story.Level.name.01KICK_LVL`, and the embedded number/code turned out to *be*
the real in-game display text for most entries (cross-checked against real hub
screenshots: `03CORRID`, `05SUBWAY`, `10DESPER`, etc. all matched exactly). Two of the
"dog" bonus entries' loc-key numbers (`08DOG1`, `12DOG2`) turned out to be stale --
real screenshots confirmed the actual displayed numbers are `99`/`98` instead, so those
two are hand-corrected in `levels.json` with a caveat explaining why; the third ("Dog3")
has no screenshot confirmation yet and uses an inferred `97`, flagged as unverified.

Two things intentionally NOT fully resolved yet, both flagged in `levels.json`'s
`_caveats` for follow-up:
- Both "Cyberspace" entries are flagged `intermission="true"` in the source data, same as
  the excluded `_SEGWAYSTUB` entries -- and `piOsMenu.AppendLevelData()` explicitly skips
  `IsIntermission` levels when building the hub's button list. That means even with
  correct id matching, Cyberspace may never get a clickable hub icon at all, which likely
  explains the *original* "Cyberspace (1) didn't link to anything" report just as much as
  the id bug did. Needs a real playthrough to see whether it's reachable some other way
  (e.g. auto-chained from an adjacent level) before deciding whether it should stay a
  trackable AP location.
- The "had to start via `superhot.exe` instead of the level selector" report from the
  same playtest wasn't independently reproduced or root-caused this pass --
  `Patches/MenuVisibilityPatch.cs` (which forces every hub folder visible from boot) is
  unchanged and should already cover this; flagging it in case it recurs after
  regenerating, rather than assuming it's fixed.

Since this changes both `gameId` (the mod's matching key) and `name` (the AP world's
location/item names) at once, it requires **both** a mod rebuild *and* a fresh
`.apworld`/regenerate -- same repack process as the earlier rename (unzip the existing
`dist/superhot.apworld`, swap in the corrected `data/levels.json`, rezip with identical
member paths), verified valid (zip integrity, JSON parses, all 34 names still unique --
required since AP's `location_table`/`item_table` use them as dict keys). Mod rebuilt (0
errors) and redeployed to both installs; `dist/superhot.apworld` rebuilt.

## Two more bypass paths found after the LEVELS folder became reachable

With the id/gameId fix above live, the next playtest surfaced two more real gaps in
gating -- both from the same root cause: SUPERHOT has more than one way to move from one
level to the next, and each new patch so far only closed one of them.

**Still possible to walk into a locked level.** Confirmed by decompiling
`LevelFlowControl.cs`: level-to-level "no hub visit" transitions
(`LoadNextLevel()`, `LoadNextLevelWithTunnelsWithoutScramble()`,
`LoadNextLevelInstant()`, and friends) call `SHGUI.LaunchLevelViaApp(LevelInfo, float)` --
a *third* method, different from both `SHGUI.LaunchLevelAppTunnels` (guarded by
`LevelGatePatch`) and the static `LevelSetup.LoadNextLevel(bool)` click-through (guarded
by `DirectLevelSkipPatch`). Neither existing patch touched it, so a player could still
walk straight into a locked level through this path. Fixed with a new patch,
`Patches/ViaAppGatePatch.cs`, guarding that method too. Pulled the actual "is this level
allowed" check out of all three patches into one shared helper,
`Core/LevelAccessGuard.cs`, specifically so a fourth bypass (if one ever turns up) only
needs a new Harmony attribute pointed at the same helper, not a fourth copy of the same
logic to keep in sync by hand.

**"32 - Longway" never sent a check.** Real, distinct bug, also rooted in
`LevelFlowControl`: not every level ends through the normal "kill all -> ending fade ->
click to continue" sequence that calls `LevelSetup.UnlockNextLevel()` (what
`LevelCompletePatch.cs` listens to). Some levels instead end with a smooth scripted
transition straight into the next level via the same `LoadNextLevel*` methods above,
without ever calling `UnlockNextLevel()` -- for those, the only completion signal the mod
had simply never fired. Fixed with `Patches/AutoTransitionCheckPatch.cs`: Prefix patches
on all five `LevelFlowControl.LoadNextLevel*` methods that send the completion check
directly, reading `LevelSetup.CurrentLevelInfo` at the moment the transition starts (same
invariant `LevelCompletePatch` already relies on -- confirmed none of these methods
reassign `CurrentLevelInfo` themselves). Four of the five share a private
`loadedNextLevel` field that guards their real work against repeated per-frame calls;
the Prefix reads it before the original method flips it, so the check only sends once per
real transition, not once per frame. Sending twice (if a level somehow triggers both this
and the normal `UnlockNextLevel()` path) is harmless -- `CompleteLocationChecks` is
idempotent on the AP server.

Both fixes are C#-only -- no `levels.json` changes this round, so no apworld
rebuild/regenerate needed, just the mod rebuild. 0 errors, redeployed to both installs.

## Blocking too late left a real soft-lock on "14 - Serv"

The fix above (gating `SHGUI.LaunchLevelViaApp`) closed the *access* hole, but the very
next playtest found a UX problem it introduced: blocking that call happens ~0.1-0.4s
*after* the transition already started, because `LevelFlowControl`'s
`LoadNextLevel()`/`LoadNextLevelWithTunnels()`/etc. kick off their camera-glitch/static
effects (`CameraEffectsManager.Instance["HotswitchRealtime"].Play(...)`, the
`AppHotswitch` overlay fill) synchronously, then defer the actual level launch via
`DelayedInvokeMarshal.Instance.Enqueue(...)` -- confirmed by decompiling
`LevelFlowControl.cs`. Blocking only the deferred part left the player stuck in that
half-played static effect with nothing loading underneath it. Apparently escapable via
the Escape key for most levels (not something this mod does or controls) -- but not for
`"14 - Serv"` (`hallOfFame`) specifically, which soft-locked outright. Given the existing
`_caveats` note that `hallOfFame` might not be a real combat level at all, this is
plausibly a level with different-than-usual input/menu handling, but the real fix doesn't
depend on knowing why -- it's to never let the static start in the first place.

Fixed in `Patches/AutoTransitionCheckPatch.cs`: the same five `LevelFlowControl` methods
now also gate, via a Prefix that computes `LevelSetup.GetNextLevelInfo()` and blocks the
*entire* method (skip before any camera/audio effect ever fires) if it's not allowed,
redirecting to the hub immediately instead of leaving anything mid-transition. Since
skipping the method means the native `loadedNextLevel` guard field never gets set by the
original code, the Prefix sets it itself when blocking, so the behavior-tree node doesn't
keep re-entering and re-redirecting every frame. The completion-check-sending half of
this patch (added for "32 - Longway" not registering) is unconditional and unaffected --
the level that just finished genuinely was finished regardless of whether the next one is
reachable.

The deeper gates (`LevelGatePatch`, `ViaAppGatePatch`, `DirectLevelSkipPatch`) are
unchanged and still needed -- hub button clicks don't go through `LevelFlowControl` at
all, so this new, earlier gate only covers the auto-transition path, not every path.

C#-only again, no `levels.json` changes, no apworld rebuild needed. 0 errors, redeployed
to both installs.

## A third auto-advance path, plus a reactive safety net for whatever's left

The very next playtest found "28 - Station" and "30 - Gate" getting stuck the same way
"14 - Serv" had been, even with the `LevelFlowControl` gate from the previous round in
place. Root cause, confirmed by decompiling `LevelFlowControl.cs` further: most levels
end with the classic "SUPERHOT" title-card sequence
(`SuperHotSuperHotEnding()`/`SuperHotSuperHotEndingClickThrough()`), and once the skip
button (LMB) is pressed there, these methods play audio, start a camera effect, and set
`PlayerActions.CURRENT.state = PlayerState.FadeOut` -- all synchronously, all *before*
deferring the actual `SHGUI.current.LaunchLevelViaApp(...)` call (already gated by
`ViaAppGatePatch`) by 0.4s. Same "blocked too late" mistake as the `LevelFlowControl`
transition methods, just a third independent code path nobody had checked yet.

This time, fixed via the user's own suggested approach: instead of trying to unwind
whatever visual/state effects the native code already started (fragile, lots of
interacting systems -- `TimeControl`, `PlayerController`, `Crosshair2D`,
`CameraEffectsManager`), `Patches/TitleCardGatePatch.cs` neutralizes the *input* itself
before the native method ever reads it. `LevelFlowControl` keeps its own private
`inputData` field (type `InputSystem.SHInputGUI.InputData`, confirmed via decompile,
holding the skip-button state); a Prefix on both ending methods checks whether the
would-be-next level should be blocked, and if so, forces `inputData.skipButton` to
`unpressed` before the original method runs. The native "if skip button pressed, advance"
branch then simply doesn't trigger that frame -- nothing starts, nothing needs undoing,
the title card just waits as if nothing was clicked. Everything else in these methods
(the actual `WinLevel()` call, achievement flags, the title-card display) is untouched.

Separately, "22 - Hacker" turned up something that isn't really a bug: decompiling its
specific ending variant (`LevelTest77_HackerRoomFlowControlVariantEnding`) shows it's a
real, deliberate SUPERHOT plot twist -- completing it calls
`SaveManager.Instance.SetValue("storyFinished", true)` and detours into a credits scene
(`_SEGWAYSTUBCredits`) via `LaunchLevelViaApp`, which our gate correctly leaves alone
since that scene isn't one of our tracked levels. What happens on the restart the game
then expects -- reported as force-loading "25 - Fall" regardless of unlock state --
wasn't fully traceable through static analysis alone in the time available; it likely
happens very early at boot, possibly through a path none of the existing gates watch.

Rather than keep chasing individual call sites indefinitely, added a last-resort net in
`Core/Mod.cs`'s `OnSceneWasLoaded` (already logging every scene load): if the scene that
just loaded resolves to one of our tracked, still-locked levels, kick straight back to
the hub. This won't prevent a brief flash of the wrong level the way the launch-time
gates do, but guarantees nothing locked stays actually playable even through a path we
don't yet fully understand -- and if it turns out to *not* catch the Hacker Room/Fall
case, that's itself useful information (means the force-load happens somewhere even
`CurrentLevelInfo`/scene-load hooks can't see, worth flagging back for further digging).

Also corrected two names from real playthrough evidence, overriding earlier guesses:
"32 - Passage" (order 32, guessed by title-casing the raw loc-key code `32PASSAGE`) is
actually "32 - Core"; "97 - Dog3" (order 19, previously flagged as an unconfirmed
inference) is actually "99 - Dog3" -- meaning Dog1 and Dog3 both legitimately use "99",
same kind of native number reuse already confirmed for "32". `levels.json` updated in
both copies, `_caveats` updated to reflect these are now confirmed, not guessed.

Since names changed again, this round needs both a mod rebuild and a fresh
`.apworld`/regenerate -- same repack process as before, reverified (zip integrity, JSON
parses, all 34 names still unique). Mod rebuilt (0 errors) and redeployed to both
installs; `dist/superhot.apworld` rebuilt.

## Removing the forced-quit fake ending entirely

The reactive safety net above was a stopgap; the user asked for the actual source removed
-- the hub menu scrambling every option except `quit.exe` after "22 - Hacker", pressuring
the player to close the game. This is real, deliberate SUPERHOT design (not a bug): its
ending is a narrative fake-out, and the native hub treats `storyFinished=true` as "the
game is over," locking down the menu to push the player toward quitting and relaunching
to see the story continue. Legitimate in vanilla; actively hostile mid-AP-run.

Confirmed via decompile that `SaveManager.SetValue("storyFinished", true)` has exactly
two call sites in the entire assembly: Hacker Room's fake ending, and a separate
"unlock everything" cheat/exploit tool (`APPUnlockEverything`) -- neither is something a
normal AP playthrough should trigger. Neither is needed for our own tracking either: goal
completion is already fully independent of this flag, driven by
`LocationManager`/`Session.SetGoalAchieved()` when the real final level (order 34,
"Hackerg") is completed.

Fixed with `Patches/StoryFinishedSuppressPatch.cs`: a Prefix on `SaveManager.SetValue`
that suppresses every write of `storyFinished=true`, full stop. Bonus effect: the
`"storylevels"` menu case only calls `LockUnfinishedLevels()` when `storyFinished` is
false (confirmed via decompile) -- which `HubUnlockPatch` depends on to ever run -- so
keeping this flag false also keeps that path working exactly as it already needed to,
rather than being a separate case to reconcile. The `OnSceneWasLoaded` safety net from
the previous round is left in place as a backstop for anything else that might force a
locked scene to load, unrelated to this specific flag.

Not yet confirmed in-game that this actually stops the menu-scrambling effect -- the
mechanism connecting `storyFinished` to that specific visual lockdown wasn't traced all
the way through the menu code, just the one concrete piece of state we know the fake
ending sets. If suppressing it doesn't fully resolve the scrambling, that's useful
information that the effect is driven by something else `storyFinished`-adjacent, worth
another pass.

C#-only, no `levels.json` changes, no apworld rebuild needed. 0 errors, redeployed to
both installs.

## Confirmed scope of the storyFinished suppression, plus one more name fix

User asked directly whether `StoryFinishedSuppressPatch` touches "22 - Hacker" only, or
also the real finale at order 34. Re-verified by exhaustively grepping the decompiled
assembly for every `SetValue("storyFinished", true)` call site -- there are exactly two
in the entire game: Hacker Room's fake ending (order 22) and the separate "unlock
everything" cheat tool. The real finale's own ending code never calls this at all (no
third site exists), so the patch has zero effect on it either way -- there's nothing
there for it to suppress.

While confirming this, also fixed order 34's name: this project's own earlier save-file
inspection (from way back, debugging the "everything unlocked" issue) had already turned
up the real value -- `"highestfinishedLevel": "34FREE.lvl"` -- which should have been
cross-referenced when the raw loc-key extraction (`HACKERG_LVL`, no number) was used to
name this entry "Hackerg (Finale)" a few rounds ago. Corrected to "34 - Free" now, same
localized-name-overrides-raw-key pattern already established for Dog1/Dog2/Dog3.

Name changed again, so another apworld rebuild/regenerate is needed. Repacked and
reverified (zip integrity, JSON parses, all 34 names unique); mod rebuilt (0 errors) and
redeployed to both installs.

**Deliberately not fixed:** the "superhot.exe" hub entry is still visible alongside
`LEVELS` now that `MenuVisibilityPatch` shows every folder from boot -- the user flagged
this as a nice-to-have, not required. Investigated removing it: it's built through the
same `PrepareLevelCommanderButtonForLevel()` our button-text cache already patches, but
actually *hiding* a list entry risks `SHGUIcommanderview`'s index-based up/down
navigation landing on an invisible/dead slot -- not confirmed safe without real
in-game testing. Left alone for now rather than risk a UI bug for a cosmetic request.

## LEVELS folder wasn't actually reachable pre-completion either -- real fix this time

The revert above assumed the native `LEVELS` folder browser was already reachable
pre-completion (based on screenshots from an earlier session) and just needed correct
per-level locking, which `HubUnlockPatch` already provided. A genuinely fresh save's real
log disproved that assumption: after finishing "Kick" and receiving "Desperados" (a
non-sequential item) from AP, the hub still only offered the single `superhot.exe` icon --
`LEVELS` wasn't there to browse into at all. The earlier screenshots showing `LEVELS` /
`ENDLESS` / `CHALLENGES` / `recruit.exe` / `credits.exe` all visible together must have
been from an old, previously-finished save that leaked through (the same rotating
3-save-slot + Steam Cloud contamination documented above), not evidence the mod's design
was working.

Root cause, confirmed by decompiling `piOsMenu.cs`: the entire hub menu tree
(`FolderStructure` XML, walked by `CreateViewFromNode`) filters every folder/app node
through `ShouldBeShown(XElement node, List<int> allowedTags)` -- a node only shows if its
`tag` attribute (when present) is in `allowedTags`, which on a fresh save is just `{0, 1}`
plus whatever tags native progression has actually earned via `SaveManager.Instance.GetTags()`.
`LEVELS` needs a tag that isn't earned yet this early -- unrelated to which story items
AP has granted, so no amount of correct per-level locking inside that folder mattered if
the folder itself was invisible.

Per the user's direction (start the game as if it were "finished" for menu-access
purposes, then rely on the mod's own patches to actually gate levels): added
`Patches/MenuVisibilityPatch.cs`, a Harmony Prefix on `ShouldBeShown` that always returns
`true`, skipping the tag check entirely. This makes the full hub -- `LEVELS`, `ENDLESS`,
`CHALLENGES`, and everything else in `FolderStructure` -- visible from the very first
boot, matching what a finished save's menu looks like. Deliberately does **not** touch
the `storyFinished` save flag or call `SetValue` at all -- only bypasses this one
visibility check -- specifically so that `piOsMenu.LockUnfinishedLevels()` keeps running
normally when `LEVELS` is opened (it's skipped only when `storyFinished` is true, which
stays false here), meaning `HubUnlockPatch`'s AP-driven unlock pass still runs on top of
it exactly as before, and `LevelGatePatch` still blocks any level whose scene isn't
actually unlocked regardless of what its icon shows.

Accepted tradeoff, matching what the user asked for: non-AP content (`ENDLESS`,
`CHALLENGES`, `recruit.exe`, `credits.exe`, etc.) becomes reachable earlier than vanilla
progression would allow. None of it is tracked by Archipelago (see "Design decisions
still open" above), so it doesn't affect check/item logic -- it can only let the player
poke around bonus content early, not skip AP-gated story progress.

Rebuilt (0 errors) and redeployed to both installs.

## Root cause of "everything unlocked" (resolved)

Not a Steam Cloud issue after all, despite looking exactly like one. Confirmed by
decompiling `FileSystemStorage.cs`: SUPERHOT keeps **3 rotating save slots**
(`super.hot`, `super_2.hot`, `super_3.hot` -- registered via
`SetValidationFunctionForContainer("super.hot", validator, 3)` in `SaveManager.cs:242`).
Reads always pick whichever of the three has the newest timestamp
(`BackupContainerData.GetContainerPathForRead`), writes always target whichever has the
*oldest* timestamp (`GetContainerPathForWrite`) -- a round-robin backup scheme, not a
single fixed save file. Renaming only `super.hot` away does nothing: the read falls
straight through to `super_2.hot`/`super_3.hot`, which still had the old completed-game
data, and a subsequent write regenerates `super.hot` from that same old state. All three
files have to be out of the way at once for a genuinely fresh save. Confirmed fixed by
moving all three (plus the player's earlier manual `super_backup.hot`) out of
`AppData/LocalLow/SUPERHOT_Team/SUPERHOT/` entirely.

## Packaging pass (found by testing the actual .apworld zip, not just the loose folder)

Generating from the loose `apworld/superhot/` folder was clean the whole time, which hid
a real bug: `Items.py` loaded `data/levels.json` via `Path(__file__).parent / ...`, which
breaks once the world is packaged as a real `.apworld` (a zip, imported via `zipimport` --
there's no real directory for `Path` to resolve). Only surfaced by actually building with
Archipelago's "Build APWorlds" launcher component and generating from the result -- fixed
by switching to `pkgutil.get_data()`, which works the same whether the world is a loose
folder or a zip. `dist/superhot.apworld` is built the correct way (via that launcher
component, which also stamps the required `version`/`compatible_version` manifest fields
that a hand-rolled zip won't have) and has been generated from successfully in a from-a-
zip test.

`SUPERHOT/Mods/SuperhotArchipelago.dll` and `SUPERHOT/UserLibs/Archipelago.MultiClient.Net.dll`
are the actual built mod, already placed in your game folder. One thing to watch for:
both MelonLoader itself and our mod's dependency chain ship a copy of `Newtonsoft.Json.dll`
(MelonLoader's own, plus one pulled in transitively by the `Archipelago.MultiClient.Net`
NuGet package) -- if the mod throws a type-load error mentioning `Newtonsoft.Json`, that's
almost certainly why; see `TESTING.md`.

## What's now real (as of the game-file handoff)

With the actual SUPERHOT install + MelonLoader in hand, this stopped being a guessing
exercise. Concretely verified, not just written:

- **Mono confirmed.** `SH_Data/Managed/Assembly-CSharp.dll` exists, no IL2CPP involved.
  `SH_Data/app.info` also gave the exact `MelonGame` identifiers (`SUPERHOT_Team`,
  `SUPERHOT`), now set correctly in `mod/SuperhotArchipelago/Core/Mod.cs`.
- **`Assembly-CSharp.dll` decompiled** (with `ilspycmd`) to real, readable C# source, and
  used to find the actual hooks the mod needs -- see the class-by-class citations in the
  mod source comments (`LevelSetup.cs`, `LevelFlowControl.cs`, `LevelEnderTrigger.cs`,
  `piOsMenu.cs`, `SHGUIcommanderbutton.cs`, `LevelInfo.cs`, `SaveManager.cs`,
  `MainDebug.cs`). Key findings, all cited with file/line in the code:
  - Level completion funnels through one place regardless of ending style:
    `LevelSetup.UnlockNextLevel()`. Patched in
    `mod/SuperhotArchipelago/Patches/LevelCompletePatch.cs`.
  - The hub's lock/unlock UI only natively supports "everything up to your highest
    sequentially-finished level" or "everything," with no support for unlocking
    individual out-of-order levels -- which is exactly what an AP-shuffled item pool
    needs. Worked around with a separate unlock-tracking layer
    (`mod/SuperhotArchipelago/Core/UnlockState.cs`) plus a Postfix patch on
    `piOsMenu.LockUnfinishedLevels()` (`Patches/HubUnlockPatch.cs`) that loosens locks
    for anything AP has granted, on top of (not instead of) the native logic.
  - The real level list (scene names, in campaign order) was extracted from the
    `GameData` XML `TextAsset` embedded in `SH_Data/resources.assets`, replacing the old
    walkthrough-based guess. See `apworld/superhot/data/levels.json` and its `_caveats`
    -- a handful of scene names appear more than once in that extraction and still need
    confirming against an actual playthrough (the mod already logs every scene load for
    exactly this purpose, see `Mod.OnSceneWasLoaded`).
- **The mod project actually compiles.** Built end-to-end in a sandbox with a real .NET
  SDK, against the real `Assembly-CSharp.dll`, `MelonLoader.dll`, `0Harmony.dll`, and the
  real `Archipelago.MultiClient.Net` NuGet package -- `dotnet build` succeeds and
  produces `SuperhotArchipelago.dll`. This isn't a claim, it's been run.
- **The apworld still generates.** Re-verified against a real Archipelago checkout after
  every change described above (level list swap, id-range cleanup) -- still produces a
  valid seed with a coherent playthrough.

## What's still actually unverified

- **In-game behavior.** Nothing above has been run *inside SUPERHOT itself* yet -- no
  MelonLoader console output, no confirmation the patches actually fire when expected,
  no confirmation `LockUnfinishedLevels()` even runs often enough for unlocks to show up
  promptly (see the TODO in `ItemManager.ApplyItem`). Compiling clean means the C# is
  type-correct against the real game, not that the mod works.
- **The duplicate scene names** in `levels.json` (`TheyAreYourTools_C_2` x3,
  `"LevelTest#77 HackerRoom"` x2, `"piCyberSpace#1_E"` x2) and whether `hallOfFame` is
  really a playable level. Needs an actual playthrough with the mod's scene-load logging
  on.
- **Whether unlocking a level via `UnlockState`/`HubUnlockPatch` is enough on its own**,
  or whether the game also gates something else (e.g. an intro cutscene flag, an
  `interapp` field on `LevelInfo`) that would make a level unreachable even with its hub
  icon unlocked. Only a real playtest will show this.

## Round 5: intermission-skip root cause, Station softlock hardening, Cyberspace removal

Three real playtest reports this round, all connected:

1. **"05 - Subway" let its SUPERHOT title card be clicked through even when the real next
   level ("06 - Jump") was locked**, and played its whole ~20s scripted "unauthorized
   access" disconnect sequence before (correctly) failing to actually load anything.
2. **"28 - Station" softlocked outright** in the same kind of situation, despite the
   click-suppression fix from round 4 (`TitleCardGatePatch.cs`).
3. **"33 - Cyberspace (2)" is "a nothing check"** -- user confirmed by inspecting
   `levels.json` that both "Cyberspace" entries are the only bad ones, and that there are
   really only 32 playable levels, not 34.

Root cause of (1) and (2), confirmed by decompiling `LevelSetup.GetNextLevelInfo()`:
outside of `GameplayModifiers` challenge mode it does **zero intermission-skipping** --
it's just `Levels[GetLevelIndexByID(CurrentLevelInfo.ID) + 1]`, the literal next raw entry
in the full 49-element list, no filtering. Both Subway (raw gameId 5) and Station (raw
gameId 34) are immediately followed by an untracked raw entry (gameId 6 and 35
respectively -- neither is in `levels.json`, presumably a `_SEGWAYSTUB`-style narrative
interlude). Every gate (`TitleCardGatePatch`, `ViaAppGatePatch`,
`AutoTransitionCheckPatch`, `DirectLevelSkipPatch`) calls `LevelAccessGuard.ShouldBlock`
with that raw "next" `LevelInfo` -- and since it's not in our catalog, `ShouldBlock`
always said "not ours, let it through," without ever looking far enough ahead to see the
*real* next tracked level past it was still locked. For Subway this meant the whole
disconnect cutscene played for nothing before the level-load itself got blocked further
downstream; for Station -- whose ending uses the self-contained
`LevelFlowControl.SuperHotSuperHotEnding()` path that starts camera/fade-state effects
*before* deferring the actual scene load -- it meant the click-suppression gate never
fired at all, the visual transition started for real, and the only remaining backstop
(`ViaAppGatePatch`) just silently swallowed the blocked call without resetting any of
that state: a genuine soft-lock.

Fixed both parts:
- `Core/LevelAccessGuard.cs`: added `ResolveToTrackedLevel()`, which walks forward through
  `LevelSetup.Levels` from whatever raw `LevelInfo` it's given, skipping any entry not
  present in our catalog, until it finds the next one we actually track (or runs off the
  list, in which case there's nothing to gate). `ShouldBlock` now checks unlock status
  against that resolved level instead of the raw one. This is the actual fix for both (1)
  and (2) -- every existing gate benefits automatically since they all funnel through this
  one method.
- `Patches/ViaAppGatePatch.cs`: hardened as defense in depth -- it now redirects to the
  hub (`SHGUI.current.LaunchLevelAppTunnels("SHMenu", false)`) on block, matching the
  pattern `DirectLevelSkipPatch.cs` and `AutoTransitionCheckPatch.cs` already used. A
  block at this specific layer should be rare now that (2) is fixed at the root, but it's
  the last line of defense for the title-card path and was the one gate that didn't
  self-recover -- if anything else ever slips past the earlier gates, this no longer
  leaves the player stuck mid-transition.

Fixed (3) by actually removing both "Cyberspace" entries from `data/levels.json` (real
gameId 29 and 45) rather than just flagging them -- confirmed dead weight: both were
already noted as `intermission="true"` in the source XML, same as the `_SEGWAYSTUB`
entries this file already excludes, and `piOsMenu.AppendLevelData()` explicitly skips
`IsIntermission` levels, so they could never get a hub button regardless of unlock state.
Removing them and renumbering `order` 1-32 for everything else lines up exactly with the
user's "only 32 real levels" observation. This *does* change every subsequent level's
Archipelago location/item id (`BASE_ID + order`), so a seed generated from the old
`levels.json` is no longer id-compatible with this one -- not a concern yet since nothing
has actually shipped, but worth remembering if that changes. Updated the stale "26 story
levels" comments in `apworld/superhot/__init__.py` to 32 while in there.

Rebuilt the mod (`dotnet build -c Release -p:SuperhotGameDir=...`, 0 errors), repacked
`dist/superhot.apworld` with the new 32-level `levels.json` and updated `__init__.py`
(verified: 32 unique names, sequential 1-32 order, zip integrity, JSON validity), and
redeployed both to the real install and the copy in this project folder. Since the level
count and every id past level 22 changed, this needs a full regenerate + rehost, same as
every other `levels.json` change.

## Apworld unit tests (`apworld/superhot/test/`)

Added a `test/` package using Archipelago's own unit testing framework (see
`docs/tests.md` in the main Archipelago repo, and `test/bases.py` there for
`WorldTestBase`'s exact API). This tests the Python fill/logic layer only -- reachability,
item gating, seed generation -- it does not and cannot touch anything in `mod/`; only real
in-game playtesting exercises the C# side.

- `test/bases.py`: `SuperhotTestBase(WorldTestBase)`, `game = "SUPERHOT"`. Gives every
  subclass three tests for free (run automatically against this world's default options):
  `test_all_state_can_reach_everything`, `test_empty_state_can_reach_something`,
  `test_fill`.
- `test/test_level_access.py`: `TestLevelAccess`, four tests specific to this world --
  `test_first_level_needs_no_item` (level 1 has no rule and is always reachable),
  `test_each_level_needs_only_its_own_item` (walks `LEVELS[1:-1]`, asserting each
  location's `assertAccessDependency` on exactly its own item -- excludes level 1, no
  rule, and the final level, checked separately since it also gates Victory),
  `test_final_level_and_victory_need_final_item`, and `test_level_count_matches_catalog`
  (32 levels, unique names, sequential order -- a direct regression check for exactly the
  kind of edit the Cyberspace removal above was).

These tests directly encode the assumption `Rules.py`'s docstring states in prose (each
location needs only its own item, not a chain of every earlier one, because the region
graph is flat and there's no other way to reach a later item) -- something a future level
list edit (reorder, add, remove) could silently violate without any error elsewhere in
`Locations.py`/`Regions.py`/`Rules.py`.

**Verified, not just written:** cloned a real `ArchipelagoMW/Archipelago` checkout,
copied `apworld/superhot/` into its `worlds/` folder, and actually ran the suite --
`pytest worlds/superhot/test/` reports 10 passed, 30 subtests passed. Also deliberately
broke `data/levels.json` (duplicated a level name) in that throwaway copy to confirm
`test_level_count_matches_catalog` fails loudly as expected, then reverted. One real bug
caught in the process: the first draft of `test_each_level_needs_only_its_own_item`
looped over every level including the final one, and failed for it -- `assertAccessDependency`
does a full scan of every location in the multiworld, and the final level's item also
gates the separate "Victory" location, which wasn't in that iteration's expected list.
Not a bug in the world itself, just the test needing to exclude the level Victory already
covers separately -- exactly the kind of thing worth catching before it looks like a false
alarm on some future edit.

**Running these yourself needs a real Archipelago checkout** -- this repo intentionally
doesn't vendor one (`test/bases.py` imports `Generate`, `BaseClasses`, `worlds.AutoWorld`
etc. from Archipelago core, which don't exist in a loose apworld folder or inside a
`.apworld` zip). Also needs Python 3.11+ (Archipelago's `main` branch uses `typing.Self`;
this project's default `python3` here is 3.10, which fails an early import). To run:

```
git clone https://github.com/ArchipelagoMW/Archipelago.git
cd Archipelago
python3.12 -m venv venv && source venv/bin/activate
pip install pytest pytest-subtests setuptools
cp -r /path/to/superhot-project/apworld/superhot worlds/superhot
python ModuleUpdate.py -y   # installs every other world's own dependencies too -- slow, one-time
pytest worlds/superhot/test/ -v
```

Not wired into CI here since there's no CI on this project yet -- just a local safety net
to run before/after level-list edits like the Cyberspace removal above.

## Round 6: cosmetic-only -- hide superhot.exe, three-state level buttons

Two purely cosmetic requests, no gating logic changed.

**Hid the "superhot.exe" hub shortcut.** Previously deferred (round 3 above) over risk of
navigation bugs from removing an existing entry after it was already in a list. Avoided
that risk entirely this time: confirmed via decompile that "superhot.exe" appears exactly
once in the whole assembly, as the literal `customName` passed to
`PrepareLevelCommanderButtonForLevel` in `piOsMenu.CreateViewFromNode()`'s "last" case --
and since that method doesn't translate `customName`, the resulting button's `ButtonText`
reliably starts with that exact 12-character string. Rather than let the button get added
and then try to remove it, `Patches/SuperhotExeButtonPatch.cs` Prefixes
`SHGUIcommanderview.AddButtonView` and just declines to add this one button in the first
place -- `AddButtonView` is a plain list-append-and-position call, so skipping it entirely
leaves no gap and re-indexes nothing else.

**Three-state level button visuals**, replacing round 3's two-state (legible always, gray
vs white for locked vs unlocked) with what was actually asked for: garbled text if not
unlocked, grey legible text if unlocked but not completed, white legible text if unlocked
and completed. `SHGUIcommanderbutton.SetLocked(bool)` only ever supports two colors
(confirmed via decompile: `'z'` gray or `'w'` white, nothing else) -- so `Patches/HubUnlockPatch.cs`
now uses text legibility to carry unlock state (garbled via the same
`StringScrambler.GetScrambledString(text, 0.9f, "▀▄█▌▐░▒▓■▪01 ")` call native
`LockUnfinishedLevels()` uses for its own scrambling, applied ourselves so it's correct
even when native's own idea of "locked" disagrees with ours) and repurposes
`SetLocked`'s two colors to carry *completion* state instead, for any button already
decided to be unlocked.

Needed a way to know "has this level's check actually been sent," which didn't exist
before (only "unlocked," i.e. item received -- see `Core/UnlockState.cs`). Added
`LocationManager.IsLevelCompleted(int levelId)`, which reads
`Session.Locations.AllLocationsChecked` (confirmed via decompiling
`Archipelago.MultiClient.Net.dll`'s `LocationCheckHelper`) rather than tracking a second
local set -- that collection is populated from the server's own `ConnectedPacket` on every
connect/reconnect and updated instantly on every check, so it's authoritative, survives a
full game restart with no extra bookkeeping on our side, and can't drift from what the
server actually has recorded.

Rebuilt the mod (0 errors) and redeployed to both installs. No `levels.json` change this
round, so no apworld repack/regenerate needed -- existing seeds/hosts are unaffected.

**Real bug found immediately by a playtest screenshot: level 1 ("Kick") couldn't be
played at all.** superhot.exe hiding worked, and locked levels showed correctly garbled --
but the grey/white split above used `SetLocked(!completed)` for unlocked levels, and
`SetLocked(bool)` turned out to do more than recolor: confirmed via decompile that
`SHGUIcommanderbutton`'s own activation code checks `IsLocked` *before* ever invoking
`OnActivate` -- if locked, it just plays a "wrong" sound and returns, the level-launch
delegate never runs. Since a level can only become "completed" by having already been
played once, `SetLocked(!completed)` made every never-yet-played level permanently
unclickable, including the very first one (which is always supposed to be free). Fixed by
decoupling color from lock state for the unlocked branch: `button.IsLocked = false`
directly (stays genuinely clickable) plus `button.color = 'z'/'w'` and
`button.SetColorRecursive('z'/'w')` -- the same two calls `SetLocked` makes internally for
its color half (confirmed via decompile), just without the `IsLocked` assignment bundled
with it. Locked levels are unaffected -- they're still supposed to both look and actually
be locked, so they keep using real `SetLocked(true)`. Rebuilt and redeployed again.

## Round 7: secret console checks

New feature, not cosmetic: report an Archipelago location check the first time the player
finds an in-level secret console.

**Researched the mechanic from scratch via decompile + real data extraction.** Each
secret is a `TerminalActivator` component (`public int SecretNumber`, `private bool
secretFound`) reached through `ActivatorPickup.Pickup()`'s `SendMessage("OnActivate")`.
`OnActivate()` already has its own "first find" guard built in -- if `secretFound` is
already true it just plays an error sound and bails, otherwise it persists
`SceneFileName + SecretNumber + "unlocked"` to the save, launches the secret's content
app, and sets `secretFound = true`. Pulled `secrets="N"` straight out of the same
`<Story>` XML in `resources.assets` levels.json's own data came from, for every one of
the 49 raw `<Level>` entries -- confirmed every level has either 0 or 1 secrets, never
more: 27 of our 32 tracked levels have exactly one, the other 5 (Dog1/Dog2/Dog3/Hacker/
Free -- notably, exactly the levels that also share a duplicate scene name with another
tracked level) have none. That last part means the native save key's potential collision
across duplicate-scene levels never actually triggers in practice, though the mod's own
join still uses `LevelInfo.ID` regardless, same as everything else, so it wouldn't matter
either way.

**Mod side:** `Patches/SecretFoundPatch.cs` patches `TerminalActivator.OnActivate`,
capturing `secretFound`'s value before and after the call via Harmony's Prefix/Postfix
`__state` handoff, and only treats a genuine `false -> true` transition as a first find
(not a revisit, not a no-op call). Reports through a new
`LocationManager.CheckSecretLocation(levelId)`, using a new
`LevelCatalog.SecretLocationIdOffset` (`20000`) so a secret's location id
(`BaseId + SecretLocationIdOffset + order`) can never collide with the level's own
complete-location id (`BaseId + order`) or any item id. `LevelEntry` gained a `HasSecret`
bool, loaded from `levels.json`'s new `hasSecret` field.

**Apworld side:** `Locations.py` gained `secret_location_name()` and
`SECRET_LOCATION_OFFSET = 20000` (must stay in sync by hand with the C# constant above),
adding one location per level with `hasSecret`. `Rules.py` gates each secret location on
the exact same access item as its level's own completion location -- in vanilla, a secret
is something found *during* a normal playthrough of the level, not something requiring
having already finished it, so "can play this level" is the right rule, not "has
completed this level." Level 1's secret (it has one) follows the same no-rule exception
as level 1's own location. No new items -- secrets are locations only, filled from the
existing pool, per the user's own framing of the request.

**Verified against the real Archipelago checkout**, not just written: updated
`test/test_level_access.py` to account for the new secret locations (the existing
`test_each_level_needs_only_its_own_item` needed the secret location added to each
level's expected-unreachable-without-its-item list, same kind of full-scan interaction
as the Victory-location fix from the unit-testing round), and added
`test_secret_count_matches_catalog` (27 with, 5 without, matching `location_table`
exactly). All 11 tests / 30 subtests pass, including `test_fill`.

**Correction, from immediately after this section:** the claim above ("`test_fill` ...
confirms Archipelago's default filler correctly pads the pool... with no special handling
needed on our side") was wrong on both halves -- see the next section. `test_fill` had
silently not run at all, and there is no automatic padding in core Archipelago regardless.

Rebuilt the mod (0 errors), repacked `dist/superhot.apworld` with the updated
`levels.json`/`Locations.py`/`Rules.py` (verified: zip integrity, 32 levels, 27 with a
secret, `secret_location_name` present), redeployed both to both installs. Location/item
ids for every existing level are unchanged (only new location ids were added, nothing
renumbered), but this is still a real apworld content change -- needs a regenerate +
rehost for the new secret locations/checks to actually exist in the session.

## Round 8: filler was never actually padded, and neither was the test that should've caught it

Prompted by a direct question -- "how do you handle filler checks?" -- that didn't have a
real answer on file, so it got checked by hand rather than described from memory. Found
two real, connected bugs.

**`create_items()` never produced enough items for the location count, and nothing in
core Archipelago fixes that automatically.** Confirmed by manually running
`Fill.distribute_items_restrictive()` outside the test harness against a fresh multiworld:
59 real locations (32 level-complete + 27 secret, after last round's secrets feature added
a second location for most levels), only 32 items, immediate `FillError: Unable to fill
all locations`. A first read of `Fill.py` suggests there might be automatic padding
(`Main.py` does call `create_filler()` in one place) -- but that call is scoped to a
specific, unrelated feature (replacing items removed by the `start_inventory_from_pool`
option) and provably doesn't change the pool's total size (`assert len(multiworld.itempool)
== len(new_itempool)` right next to it). Every world is expected to pad its own itempool
in `create_items()` to match its own location count; this one simply never did, because
the location count changed (secrets) without anyone revisiting this loop to match.

**And the unit tests that exist specifically to catch this had been silently not running
at all.** `test_fill`, `test_all_state_can_reach_everything`, and
`test_empty_state_can_reach_something` all start with `if not (self.run_default_tests and
self.constructed): return`. `WorldTestBase.run_default_tests` (see `test/bases.py` in the
main Archipelago repo) is a property that's `False` for any subclass that doesn't set a
non-empty `options` dict or override `setUp`/`world_setup` -- by design, since
`test/general/` already runs every registered world once with default options as part of
core Archipelago's own test suite, and re-running the same default-option checks per-world
would waste CPU. Since neither `SuperhotTestBase` nor `TestLevelAccess` ever set custom
options, that property was `False` for both -- meaning these three tests were reporting
`PASSED` while their bodies never actually ran. Reproduced this directly: running
`test_fill` in isolation with `-v -s` showed a clean pass in about the same time as an
empty test, no distribute_items_restrictive output at all -- compared to the real,
successful run after the fix below, which produced actual Fill-step log lines. This is
exactly the mechanism that let the filler bug ship past the "all tests pass" report last
round: the tests whose entire job was to catch "can this multiworld actually be filled"
were never executing.

Fixed both:
- `apworld/superhot/__init__.py`: `create_items()` now pads the pool with
  `create_filler()` calls until it matches `len(location_table)`; added
  `get_filler_item_name()` returning a duplicate `Level Access: X` name (later replaced
  with a dedicated filler item -- see the next round below, this was superseded almost
  immediately once the actual receive-log experience was considered).
- `apworld/superhot/test/bases.py`: `SuperhotTestBase` now sets `run_default_tests = True`
  explicitly, with a docstring explaining why it's required, not optional, here --
  otherwise this exact class of bug (a test that looks like it ran and didn't) can hide
  again silently.

**Re-verified for real this time**, and confirmed the difference: `pytest
worlds/superhot/test/ -v` now shows `test_fill`/`test_all_state_can_reach_everything`/
`test_empty_state_can_reach_something` actually executing (158 subtests total, versus 30
before -- the jump itself is evidence they'd been no-ops). Also re-ran the manual
`distribute_items_restrictive()` reproduction: itempool now 59 items, all 59 real
locations filled, 0 unfilled, no error. Repacked `dist/superhot.apworld` with the updated
`__init__.py`, redeployed. Same as last round -- no existing ids changed, but this is a
real logic change (a seed generated from the previous apworld would have failed outright
at generation time), so it needs a regenerate + rehost.

## Round 9: dedicated "White Space" filler item

Real, explicit user request, raised immediately after the round above: don't use
duplicate `Level Access: X` items as filler. A player receiving several copies of the
same level's access item in their receive log would reasonably read those as real level
unlocks, not padding -- confusing given every other item in the pool genuinely does
something.

Added a real, distinct, on-brand filler item instead. `Items.py`: `WHITE_SPACE_ITEM_NAME
= "White Space"` (SUPERHOT's own aesthetic -- every level is a stark white void the
player fights through), `ItemClassification.filler`, id
`BASE_ID + ITEM_ID_OFFSET + 100` (`WHITE_SPACE_ITEM_ID_OFFSET = 100`, well clear of real
level orders 1-32 so it can't collide even if more levels are added later). `__init__.py`:
`get_filler_item_name()` now returns this fixed name instead of a random `Level Access:`
one; `create_items()`'s padding loop is unchanged, since it already just calls
`create_filler()` -> `get_filler_item_name()` under the hood.

Mod side needed a matching change too, or every "White Space" received would have logged
as `"Received unknown item id ... -- no matching level in LevelCatalog"` -- accurate but
alarming, since it'd fire on every single filler item despite nothing being wrong.
`LevelCatalog.cs` gained `WhiteSpaceItemId` (mirrors the Python id by hand, same pattern
as `SecretLocationIdOffset`); `ItemManager.ApplyItem` now recognizes it explicitly with
its own friendly log line and a clean no-op, before falling through to the real
"unknown item" warning path.

Added `test/test_filler.py`: confirms the itempool size matches the real location count
exactly (the direct regression test for the round-8 bug), that `White Space` is a real,
placeable, filler-classified item whose name doesn't start with `"Level Access:"`, and --
given round 8's whole lesson was a test silently not running -- that
`run_default_tests` is actually `True` on the shared test base, so this file's own
`test_fill`/etc. can't quietly no-op again either. All 20 tests / 286 subtests pass.

Rebuilt the mod (0 errors), repacked `dist/superhot.apworld` with the updated
`Items.py`/`__init__.py`, redeployed both to both installs. Item ids for every real level
are unchanged; only the filler item's identity changed, but that's still real apworld
content -- needs a regenerate + rehost, same as every round since the secrets feature.

## Round 10: block-message truncation, and removing the final level's own check

Two changes from the same round of real-playtest feedback.

**1. Truncated block message.** A screenshot showed the "LOCKED" message getting cut off
on both edges of the uptitle display when clicking a still-locked level. No code-level
truncation/wrapping logic exists for `TextManager.Uptitle` -- it's a fixed-width Unity UI
`Text` element. Real evidence rather than a guess: vanilla's own uptitle strings (e.g.
`"hack into a terminal to skip level"`, 35 chars) are far shorter than the original message
here (`"LOCKED -- 'X' needs an Archipelago item before you can play it."`, up to 74 chars
for the longest level name, `"28 - Station"`). Shortened to
`"LOCKED: 'X' needs an AP item"` (max 39 chars) in `LevelAccessGuard.cs` -- the single spot
in the codebase that constructs this string, confirmed via grep.

**2. No real check behind beating the game.** Real, explicit user request: "I don't want a
check hidden behind clearing 34 - Free, since that's ending the game and all checks should
be released anyways." Finishing the final level ends the run; a real, regular, fillable
item sitting behind "beat the entire game" is bad multiworld design if another player's own
progression happens to depend on it -- they'd be stuck waiting on this player's full
campaign clear. The dedicated Victory event location (`Regions.py`, `address=None`,
already existed to signal completion) is the correct mechanism instead.

Removed `"34 - Free Complete"` from `location_table` entirely (`Locations.py`:
`location_table` now builds from `LEVELS[:-1]`, not `LEVELS`; the now-dead
`FINAL_LOCATION_NAME` constant removed). `Rules.py`'s main loop now skips fetching/setting
a rule on the final level's own location (it doesn't exist anymore) while still setting
its secret rule if it had one (it doesn't) and the Victory rule, which still gates on the
final level's access item same as before. Location count dropped 59 -> 58, and since
filler padding (`create_items()`) is computed as `len(location_table) - len(LEVELS)`, the
filler count dropped in lockstep (27 -> 26) with no separate fix needed.

Mod side needed a matching change: `LocationManager.CheckLocation` no longer calls
`CompleteLocationChecks` for the final level (there's no location id for the server to
recognize anymore), but still calls `SetGoalAchieved()` exactly as before -- those were
always two independent signals (see the "goal achieved" fix in the live-testing log
above), so decoupling them here is not a behavior change to goal reporting. Also had to
revisit `IsLevelCompleted` (used by `HubUnlockPatch.cs` for the grey/white cosmetic
distinction): the final level no longer has a location to read a "completed" state from,
and there's no other server-authoritative signal for "unlocked but not yet played" vs.
"unlocked and played" for it specifically. Rather than invent local state that would drift
from the server-is-truth design used everywhere else in this class, the final level is
just treated as completed as soon as it's unlocked -- the same no-distinction exception
level 1 already gets for locked/unlocked.

Updated `test/test_level_access.py`: `test_final_level_and_victory_need_final_item` (which
asserted a real location existed for the final level) split into
`test_final_level_has_no_completion_location` (asserts it's genuinely absent from
`location_table`) and `test_victory_needs_final_item` (the actual remaining access-rule
check, now just against `"Victory"`). All 21 tests / 282 subtests pass against a real
Archipelago core checkout.

Rebuilt the mod (0 errors), repacked `dist/superhot.apworld` with the updated
`Locations.py`/`Rules.py`/`__init__.py`, redeployed both the mod DLL (to both installs)
and the apworld. Real content change on both sides -- needs a regenerate + rehost.

## Pre-release cleanup, before uploading (Nexus/GitHub)

User's plan: mod on Nexus Mods, apworld on GitHub. Before that, wrote `ARCHITECTURE.md`
(a file-by-file reference for both halves, separate from `NOTES.md`'s dated bug log), then
did a real pass over what a public upload would actually expose:

- `SuperhotArchipelago.csproj` hardcoded this machine's real Windows path
  (`C:\Users\Ultro\...`) as `SuperhotGameDir`'s default -- a real username leak, confirmed
  by grepping the repo, not a hypothetical. Fixed: default is now empty, with a friendly
  `<Error>` MSBuild target if it's never set, and three documented ways to set it
  (`-p:SuperhotGameDir=...`, an environment variable, or a local gitignored
  `.csproj.user` file). Verified all three paths actually work, and that the no-override
  case fails with the intended message, by building it both ways for real.
- No `.gitignore` existed -- `mod/SuperhotArchipelago/bin/`, `obj/`, and
  `apworld/superhot/__pycache__/` were all sitting there ready to be committed, including
  `obj/*.nuget.*` files that embed local machine paths. Added one.
- `README.md`/`TESTING.md` were stale relative to `NOTES.md`'s actual history: both still
  said "34 levels/items" (should be 32 levels / 58 real locations+items, post-Cyberspace-
  removal and post-final-level-location-removal), and README's Status section still
  claimed "nothing has run inside SUPERHOT yet," which stopped being true many rounds ago.
  Rewrote both to match reality, including an explicit "still beta, solo-tested" caveat and
  a note that `BASE_ID` is an unreserved placeholder.

## Round 11: in-game Archipelago connection overlay

Real, explicit user request, made right before uploading: an in-game way to set up the
connection, so players don't have to find and hand-edit `UserData/MelonPreferences.cfg`
themselves. Asked the user to choose between two approaches given the real cost/risk
difference: a simple toggleable overlay (fast, low-risk, looks like a generic mod menu) or
a native-styled in-hub app icon (thematically consistent with `superhot.exe`/`White
Space`, but needs new decompile research into `piOsMenu`'s app-content system this project
hasn't touched). User picked the overlay.

Confirmed rather than assumed before writing any code: decompiled `MelonLoader.dll` and
found `MelonBase.OnGUI()` is a real virtual method, auto-subscribed to `MelonEvents.OnGUI`
in `RegisterCallbacks()` -- overriding it in `Mod.cs` is genuinely all that's needed, same
mechanism every other MelonLoader mod with a settings overlay already relies on. Also
confirmed the toggle hotkey is safe -- and not just by inference: `SH_Data/Managed` has no
Unity Input System package DLL (so the legacy Input Manager, `UnityEngine.Input`, actually
living in `UnityEngine.InputLegacyModule.dll` not `CoreModule`, is what's active), and
decompiling `InputSystem.SHInputGUI` directly found the game's *own* menu-navigation code
already calling `Input.GetKeyDown(KeyCode.Return)` / `KeyCode.Escape` / `Input.GetKey` for
arrow keys -- about as direct a confirmation as possible that `UnityEngine.Input` genuinely
works in this build, not just that the class exists. The game's own `SHInput` separately
wraps a third-party asset (`InControl`, confirmed via `using InControl;` in the decompiled
`InputSystem.SHInput`) for its gameplay-facing input, but that's a separate layer on top,
not a replacement for Unity's own `Input` class. No existing `KeyCode.F2` binding found in
any of the input classes checked. Also had to add two new project references
(`UnityEngine.IMGUIModule`, `UnityEngine.InputLegacyModule`) that weren't needed before --
`GUILayout`/`GUI` and `Input`/`KeyCode` turned out to live in different modules than the
ones already referenced, confirmed the same way.

New `Core/ConnectionUI.cs`: a toggleable (`F2`) IMGUI window with Server/Slot/Password
fields and a Connect button, plus a live status line (idle/connected/specific error).
Real design detail, not just wiring text fields to existing config: the fields are
buffered in local strings and only committed to `Config` when Connect is actually pressed.
Binding them straight to `Config.Server.Value` etc. per keystroke would have fired the
existing `OnEntryValueChanged`-driven auto-reconnect (wired up for the original
hand-edit-the-file workflow) on every character typed. Committing all three at once still
needed its own fix: setting three `MelonPreferences_Entry.Value`s in a row would've fired
that same auto-reconnect three separate times for one button press, each a real blocking
network call -- added a `_suppressAutoReconnect` flag in `Mod.cs`, set for the duration of
the three writes, with a single explicit `TryConnect()` call after, so one press is one
connect attempt.

`ArchipelagoConnection.Connect()` also got two real fixes needed for this to be safe as
something the player can now trigger with a mistyped address rather than only ever running
once at startup with a config that (mostly) worked: it can throw outright rather than just
return an unsuccessful result (found by reasoning about what a malformed server string
would do, then confirmed the call needed a try/catch since nothing downstream was ever
tested against a bad address before this), and it now sets a `LastError` string the UI
reads instead of that failure being console-only.

`Config.cs` gained a `Save()` method (the category is now kept as a field instead of a
`Load()`-local variable) so the overlay's Connect button can persist what it just set the
same way `Load()` already does on first run.

Build succeeded with 0 errors on the first real attempt after fixing an unrelated `--` in
an XML comment (MSBuild rejects `--` inside `<!-- -->`, hit twice while writing these
notes into the `.csproj` itself, not the mod code) -- a clean compile against the new
`GUILayout`/`GUI`/`Input`/`KeyCode` calls is real confirmation those signatures match what
was found via decompile, not just an assumption. Rebuilt and redeployed to both installs.

**Not yet confirmed:** nothing about the actual rendered overlay (layout, whether F2
conflicts with anything the game already binds to that key, whether the window is
readable against gameplay) has been playtested -- unlike almost everything else in this
project, this went in right before upload without a live playtest pass first, because it
needs to ship before that pass can happen. Flagged as the first thing to check in the next
real playtest.

## Round 12: replaced the F2 hotkey with a real hub button

Real, explicit user follow-up, immediately after Round 11 landed and before any upload or
playtest of it: "instead of its own keybinds its just in a mod settings selector on the
main screen like levels." Asked the user where exactly -- a top-level hub icon (next to
"LEVELS"/"ENDLESS"/etc., reusing the same injection point already used to hide
`superhot.exe`) versus an entry inside the "LEVELS" folder alongside the 32 level buttons
themselves (closer to "like levels" literally, but the level list is built from
`data/levels.json` in a loop, so a non-level entry there would've needed new research into
whether a level button even supports a non-level click action). User picked the top-level
icon.

New decompile research (the actual risk this path was flagged for back in Round 11, now
paid down): decompiled `SHGUIcommanderbutton` fully for the first time in this project
(earlier rounds only touched its `IsLocked`/`SetLocked`/`color` surface via other patches'
needs) and found `OnActivate` is a real, public `Action<SHGUIcommanderbutton>` delegate
field, set via either the constructor's third parameter or `SetOnActivate()`, invoked
plainly (`OnActivate(this)`) after an `IsLocked` check -- genuinely generic, not secretly
tied to launching a level the way it looked from every existing patch only ever seeing it
used that way. Also decompiled `piOsMenu.CreateViewFromNode()` fully for the first time:
it's the one method that builds every hub screen (root and every subfolder alike), creates
a brand new `SHGUIcommanderview` on every single call (stored in a private `createdView`
field) and marks it `isRoot` based on whether it was called with `e == null` -- exactly
the signal needed to add a button to the top-level screen only, once per hub visit, with
no de-dup guard needed (each visit gets a genuinely fresh view).

New `Patches/ConnectionButtonPatch.cs`: a `Postfix` on `CreateViewFromNode` that, only when
`___createdView.isRoot` is true, constructs a real `SHGUIcommanderbutton` labeled
`ARCHIPELAGO` with live `ONLINE`/`OFFLINE` status (same `name│status` convention every
other hub button uses) and an `OnActivate` that calls `ConnectionUI.Toggle()` directly --
no `LevelInfo`, no interaction with `LevelAccessGuard` or any of the four level-launch
gates, since this button never goes anywhere near `SHGUI.LaunchLevelAppTunnels`/
`LaunchLevelViaApp`/etc. at all.

`Core/ConnectionUI.cs`: removed the `F2`/`Input.GetKeyDown` toggle entirely, replaced with
`Open()`/`Toggle()` methods called directly from the new button. `Mod.cs`: removed the
`OnUpdate` hotkey-polling call; `OnGUI` (still drawing the same overlay content) is
unchanged. `SuperhotArchipelago.csproj`: removed the now-unused
`UnityEngine.InputLegacyModule` reference, since nothing calls `UnityEngine.Input`
anymore -- `UnityEngine.IMGUIModule` (for the overlay itself) is still needed and stays.

Build succeeded with 0 errors (again after fixing a `--` in a new `.csproj` comment --
same MSBuild XML-comment rule as Round 11, now a known trap when writing these comments).
Rebuilt and redeployed to both installs.

**Still not yet confirmed:** same caveat as Round 11, now shifted rather than resolved --
the button's real on-screen appearance/position among the other hub icons, and the overlay
window itself, remain unplaytested. This is now the second feature in the project to ship
without a live playtest first, for the same reason (ready-before-upload timing) -- flagged
alongside Round 11's item as the first two things to check in the next real playtest.

## Round 13: overlay opened but was unclickable (first real playtest of Rounds 11/12)

Real bug report from the first actual playtest of the hub button: "I see the window open
but I can't press it." The window rendered fine; its Connect button and text fields never
responded to mouse clicks at all.

Root cause, confirmed by decompiling `SHGUI.cs` (the main hub GUI controller, distinct
from `piOsMenu`) rather than guessed: its `Update()` method sets `Cursor.visible = false`
unconditionally, every single frame -- it computes its own virtual cursor position each
frame from raw mouse delta (`cursorX`/`cursorY`, clamped to a 64x24 grid, clearly the
game's own in-fiction terminal cursor) and hides the real OS cursor right after, with no
check for any menu/UI state. That runs whether or not `ConnectionUI` is open, and re-hides
the cursor every frame regardless of what set it visible a moment earlier -- so a naive
"set `Cursor.visible = true` once when the window opens" fix would have lost that fight
every single frame and was never going to work, which is why the fix needed a real
Postfix on the same method rather than a one-time flag flip somewhere in `ConnectionUI`.

New `Patches/ConnectionCursorPatch.cs`: `[HarmonyPatch(typeof(SHGUI), "Update")]`,
`Postfix` that forces `Cursor.visible = true` only while `ConnectionUI.Visible` is true --
runs immediately after the native code that just hid it, winning the per-frame fight with
zero effect on cursor behavior the rest of the time. Deliberately left `Cursor.lockState`
alone (stays `CursorLockMode.Confined`, set once by `piOsMenu.ForceStart` on entering the
hub) -- Confined still allows normal absolute cursor movement within the game window,
which is all `GUILayout`'s mouse-driven controls actually need; visibility was the only
real problem.

Build succeeded with 0 errors. Rebuilt and redeployed to both installs.

**Still not yet confirmed:** whether this actually fixes it in-game (the user's playtest
that found the bug was interrupted by this fix landing, not yet re-tried), and the two
Round 11/12 items (overlay layout, button placement) that were never confirmed to begin
with. Also worth watching for on the next playtest, not yet investigated: since
`SHInputGUI` polls arrow keys/Return every frame independently of our overlay (confirmed
in Round 12's research), typing in a text field (which needs arrow keys to move the text
cursor) might simultaneously move the hub's own button selection in the background. Not
reported yet, and not fixed here -- noted so it's not a surprise if it shows up.

## Round 14: the connection screen is now a real native app, not a Unity window

Real, explicit user follow-up, immediately after Round 13's cursor fix landed: "could you
make actually in the game? Like how the settings are? instead of a prompt window?" --
asking for the connection screen itself to be built from the game's own UI, not a Unity
IMGUI window drawn on top of it.

New decompile research paid this off directly, and also fully obsoletes Round 13's whole
bug class rather than just patching around it. Found `APPSettings : SHGUIcommanderview` --
the real native settings screen (`Controls`/`Graphics`/`Physics` etc.) is built from
exactly the same `SHGUIcommanderview`/`SHGUIcommanderbutton` framework already used for the
hub and for Round 12's own button, not a separate rendering system. It uses
`SHGUISettingsButton`, a cycle/toggle-style widget -- fine for enum/bool settings, but no
help for genuinely free-form text like a server address. The real find was `AppSHConsole`
(the game's actual developer console, confirmed via decompile): it accumulates typed
characters every frame via `Input.inputString`, manually strips backspace (`'\b'`) and
submits on carriage return (`'\r'`), and draws a blinking caret with
`Mathf.Sin(Time.realtimeSinceStartup * 10f)` -- a complete, real, working precedent for
free-text keyboard entry in this exact game, confirmed rather than assumed to be possible.
Also found `SHGUIappbase` (the base class for simple bordered pop-up app screens): it
already draws a frame, a title, an "Esc" hint, and handles Escape-to-close for free.

New `Core/ArchipelagoConnectApp.cs`, a real `SHGUIappbase` subclass with three `SHGUItext`
field pairs (SERVER/SLOT/PASSWORD, label + value), reusing `AppSHConsole`'s exact
input-accumulation pattern per field instead of one. Tab cycles which field is focused
(`SHGUItext.color`, inherited from `SHGUIview`, is a plain settable field -- used to make
only the focused label white, others grey, the same convention as everywhere else).
Enter advances to the next field, or connects if already on PASSWORD. Password is
displayed masked (`*` characters) while the real typed value stays in memory for the
actual connect call. A `STATUS:` line shows live connected/error state, same source
(`Mod.Connection`) as everything else. Had to override `ReactToInputKeyboard` to swallow
"enter" -- `SHGUIappbase`'s own version treats Enter as "close the app," which would have
fought this screen's own use of Enter to advance fields; Escape is still forwarded to the
base class so it closes the same way every other app screen does.

Launched directly via `SHGUI.current.AddViewOnTop(new ArchipelagoConnectApp())` from
`Patches/ConnectionButtonPatch.cs`'s existing button -- confirmed via decompile this is
the same general mechanism `SHGUI.LaunchAppByName` uses internally, no name-registration
system required to use it directly. `Mod.ApplyConnectionSettingsAndConnect` and
`Config.Save()`/`ArchipelagoConnection.LastError` (both added in Round 11) needed no
changes at all -- only the rendering/input layer changed, the actual connect logic was
already solid.

Removed entirely, now that nothing needs a mouse or a Unity window: `Core/ConnectionUI.cs`
(the IMGUI overlay) and `Patches/ConnectionCursorPatch.cs` (Round 13's cursor-visibility
fix) -- both existed only to make a floating Unity window usable, and a native, keyboard-
driven, view-stack-pushed screen doesn't have that problem by construction. Also removed
the now-unused `UnityEngine.IMGUIModule` project reference; `UnityEngine.InputLegacyModule`
(for `Input`/`KeyCode`) is still needed and stays, now for real keyboard entry instead of
a hotkey.

As a side effect, this likely also resolves Round 13's other open worry (typing might leak
into the hub's background input handling) -- pushing a view via `AddViewOnTop` is how every
other app screen in the game already isolates its own input, not something particular to
IMGUI. Not separately confirmed, since it was never confirmed to be a problem in the first
place, but worth noting as a reason it's less likely to be one now.

Build succeeded with 0 errors (again after an XML `--` comment typo, now a very familiar
mistake in this project). Rebuilt and redeployed to both installs.

**Not yet confirmed, honestly:** this is the least-tested piece of code in the whole
project so far -- a brand new screen built from primitives (`SHGUItext`/`SHGUIappbase`)
never used directly by this mod before Round 14, with no live playtest of its layout,
whether the coordinate offsets chosen actually fit inside the app frame cleanly, whether
Tab/Enter feel right, or whether password masking renders as expected. Flagged as the
top thing to check on the next playtest, ahead of everything else still open from Rounds
11-13.

## Round 15: Escape didn't close the new native screen

Real bug report from the first actual playtest of Round 14's native connect screen:
"Looks/works mostly great but esc key doesn't work, cant go back to main menu." Everything
else -- layout, typing, Tab, Connect -- worked; only Escape didn't.

Root cause, found by re-examining `AppSHConsole` more closely rather than re-guessing:
`ArchipelagoConnectApp`'s `ReactToInputKeyboard` override forwarded `SHGUIinput.esc` to
`base.ReactToInputKeyboard` (`SHGUIappbase`'s version, which calls `SHGUI.current.PopView()`
on esc) -- the "correct-looking" way to close an app screen, and the way Round 14's own
docstring assumed every app screen relies on. But `AppSHConsole` itself, despite inheriting
that exact same machinery, does **not** rely on it for Escape -- it reads
`Input.GetKeyDown(KeyCode.Escape)` directly every frame in its own `Update()` and closes
itself that way (`Kill()`, not even `PopView()`). That detail was sitting right there in
Round 14's own research notes and got read past the first time -- the enum-dispatch path
apparently isn't reliable enough (for this class of view, or in general) for the one real
native precedent in this game to actually trust it for something as important as "let the
player leave."

Fix: added the same direct check `AppSHConsole` uses to `ArchipelagoConnectApp.Update()` --
`Input.GetKeyDown(KeyCode.Escape)` checked first thing every frame, closing via
`SHGUI.current.PopView()` before any of the field/typing logic runs that frame. Left the
`ReactToInputKeyboard` override in place as a harmless backup (still swallows "enter" for
the same reason as before), but it's no longer the thing actually doing the work.

Build succeeded with 0 errors. Rebuilt and redeployed to both installs.

**Not yet confirmed:** whether this actually fixes it in-game (fixed in response to the
report, not yet re-tried), and everything else Round 14 already flagged as unconfirmed
(layout, Tab/Enter feel, password masking) still stands.

## Round 16: status text ran off-screen, and stale "CRACKED!" secret badges

Two real bugs from the same playtest report, sent together with a screenshot of the
connect screen showing `STATUS: ERROR -- Login failed -- check server address, slot n`
cut off at the app frame's right edge: "1: Status goes into outside of screen. 2: Secrets
will say 'Cracked!' despite starting new game. I believe its leftover from the previous
run and just not reset."

**Bug 1 -- status line overflow.** `ArchipelagoConnectApp.RefreshDisplay()` assigned
`_statusField.text` directly with no line-wrapping, so a long real error message (exactly
the login-failure case in the screenshot) ran straight past the frame border instead of
wrapping. Fix: `SHGUItext.BreakTextForLineLength(int)` (confirmed via decompile -- it just
inserts `'\n'` into `.text` wherever a line would exceed the given length, the same
primitive the game's own multi-line text already uses) called on `_statusField` right
after setting its text, with a `StatusLineWidth` of 50 -- comfortably under the frame's
real width, not measured exactly against it. Also widened the vertical gap after the
status line (`y += 5` instead of `y += 2`) so the instruction line below it doesn't
overlap a wrapped multi-line status.

**Bug 2 -- stale "CRACKED!" badge.** The user's own diagnosis was right. Confirmed via
decompile: `LevelInfo.SecretsFound()` reads directly from native save data
(`SaveManager.Instance.GetValue(SceneFileName + secretIndex + "unlocked", false)`) with
zero awareness of Archipelago at all. Two places in `piOsMenu` use it purely for display:
`PrepareLevelCommanderButtonForLevel` adds the scrolling "CRACKED!" badge
(`AddScrollingNotification("MENU_CRACKED8CHARS".T()...)`) when `SecretsFound() ==
Secrets`, and `PrepareLevelDescription` bakes the localized `MENU_SECRETCRACKED` /
`MENU_SECRETNOTCRACKED` string into the button's description text the same way. A save
file with leftover `"...unlocked"` flags from an earlier, unrelated playthrough shows
"CRACKED!" regardless of what the *current* Archipelago run has actually checked -- the
exact same class of bug `LocationManager.IsLevelCompleted` already solves for main level
completion (trust the live AP session, not native save state), just for the secret
location instead of the main one.

Fix: added `LocationManager.IsSecretCompleted(int levelId)` (mirrors `IsLevelCompleted`,
just reads `AllLocationsChecked` for the secret's own location id --
`BaseId + SecretLocationIdOffset + entry.Order` -- instead of the level's). Deliberately
did **not** patch `LevelInfo.SecretsFound()`/`HasAllSecretsFound()` themselves -- those
also gate real native Steam-achievement logic (`TerminalActivator.CheckAllSecretsAchievement`),
and overriding them globally would risk achievements no longer matching what the player
actually did natively. Instead, `Patches/HubUnlockPatch.cs`'s existing per-button Postfix
loop (already resolving each hub button to a tracked `LevelEntry` on every native
`LockUnfinishedLevels()` refresh) now also, for any level with `HasSecret`: calls
`button.AddScrollingNotification(...)` or `button.RemoveScrollingNotification()`
(confirmed public, and `AddScrollingNotification` already calls `RemoveScrollingNotification`
internally first, so this can't stack duplicate badges) based on
`IsSecretCompleted(levelInfo.ID)` instead of trusting whatever native left behind; and
does a plain substring swap of the description text using the exact same localized
strings native code embeds (`"MENU_SECRETCRACKED".T()` / `"MENU_SECRETNOTCRACKED".T()` --
confirmed public via `LocalizationAccessHelperExtensions.T`, calls
`LocalizationManager.Instance.GetLocalized`), rather than reimplementing
`PrepareLevelDescription`'s own random-scrambled-filler text construction from scratch.
Runs unconditionally for every tracked level with a secret (not just the unlocked
branch), since a locked level's button can carry a stale badge too, and every hub refresh
re-corrects it, so it can't drift out of sync again even across saves.

Both fixes built together (0 errors) and redeployed to both installs.

**Not yet confirmed:** neither fix has been re-tested in-game yet (both were written and
built directly in response to this report, not re-played). The description-text swap in
particular is a string-surgery approach -- if the localized string ever contains
characters that also legitimately occur elsewhere in that level's filler text this could
theoretically mis-replace, though the localized sentences involved are specific enough
that this is unlikely in practice.

## Round 17: ARCHIPELAGO hub button sometimes stuck on OFFLINE

Real bug report: "the ONLINE next to Archipelago menu item in the main menu sometimes
says OFFLINE while being online."

Root cause, confirmed via decompile: `piOsMenu.CreateViewFromNode` -- the method
`Patches/ConnectionButtonPatch.cs` hooks to add the button and compute its ONLINE/OFFLINE
text -- is only called when the hub's root view is rebuilt from scratch. Grepped the
entire decompiled assembly for every caller of it: the only other callers are itself (a
recursive call for subfolders like "LEVELS", and a challenge-mode variant). Popping a
view pushed on top (`SHGUI.current.PopView()`, e.g. pressing Esc to close
`ArchipelagoConnectApp`) just reveals the same already-built root view again -- it does
not rebuild it. So the sequence that reproduces this every time: stand on the hub
(already showing OFFLINE, e.g. mod hadn't connected yet), open ARCHIPELAGO, connect
successfully, press Esc -- the button you're looking at is the exact same object built
before you connected, and nothing ever told it to recompute its text. It stays wrong
until the player leaves all the way back to the Main Menu and re-enters, which is the
only thing that rebuilds the root view.

Also checked whether `HubUnlockPatch.cs` (already patched onto
`piOsMenu.LockUnfinishedLevels`, and already used for Round 16's live secret-badge fix)
could refresh this too -- it can't: `LockUnfinishedLevels` only fires inside the
"LEVELS" subfolder's own view build, not the root's, so the ARCHIPELAGO button (which
only ever lives on the root view) isn't even present in that pass.

Fix: `ConnectionButtonPatch` now keeps a live reference to whichever button instance is
currently on screen (`Button`, overwritten every time a fresh one is built) and exposes
`RefreshLabel()`, which re-derives ONLINE/OFFLINE from `Mod.Connection.IsConnected` and
reapplies it via `button.ButtonText = ...; button.RefreshText();` -- the same two calls
the button's creation already used. `Mod.OnUpdate()` now calls `RefreshLabel()` every
frame alongside the existing `Items.ProcessQueue()`, so the label can't drift out of sync
again regardless of how the player navigates. Confirmed via decompile this is safe even
when the button's view has since been popped/killed: `SHGUIview.Kill()` (the whole
hierarchy, `SHGUIcommanderbutton` included) is a plain C# object marked as fading out, not
a Unity object that throws on further access post-destroy -- so no extra teardown
tracking was needed to null the reference out; a briefly-orphaned update between leaving
the hub and a fresh button being built has no visible effect and gets overwritten anyway.

Build succeeded with 0 errors. Rebuilt and redeployed to both installs.

**Not yet confirmed:** not yet re-tested in-game (fixed directly from the report, same as
Round 16). The per-frame refresh is a plain string rebuild + one method call when nothing
changed since last frame -- expected to be negligible, but not benchmarked.

## Pre-publish audit

Real request: "I think I'm ready to publish this mvp check for any problems." Full pass
over both halves before the first public upload (Nexus for the mod, GitHub for the
apworld), fixing everything found rather than just listing it:

- **Critical:** the project root's `SUPERHOT/` folder -- a full local copy of the actual
  commercial game (`SH.exe`, `SH_Data/` assets, ~4.1GB), used only as `SuperhotGameDir`
  for local builds/testing -- was not excluded from git. Had this been committed and
  pushed, it would have published copyrighted game files publicly. Added `/SUPERHOT/` to
  `.gitignore`.
- `TESTING.md` promised a ready-to-use `dist/superhot_michael.yaml` sample player file
  that didn't actually exist -- anyone following the guide fresh would have hit a missing
  file at step 3. Written for real (standard Archipelago YAML format, no custom options
  since `Options.py` doesn't define any yet).
- Stale comments/docs still pointing at `Core/ConnectionUI.cs` and an "F2 window" (both
  removed rounds ago, replaced by `ArchipelagoConnectApp.cs`/the hub button) in
  `Config.cs`, `ArchipelagoConnection.cs`, `Mod.cs`, and `TESTING.md` -- fixed.
  `apworld/superhot/__init__.py`'s docstring still called the world "a v0 scaffold, not a
  finished, tested world," undercutting everything actually verified since -- updated.
- Added `LICENSE` (MIT, the standard choice for both the Archipelago apworld and
  MelonLoader mod ecosystems).
- **Actually verified the apworld against the real Archipelago core**, not just this
  project's own standalone unit tests: downloaded a portable Python 3.11 build (this
  sandbox only had 3.10, and current Archipelago's `worlds/AutoWorld.py` genuinely
  requires 3.11+ typing features -- confirmed by trying to shim it and hitting a stdlib
  `TypeVar` incompatibility too deep to patch around), cloned `ArchipelagoMW/Archipelago`
  fresh from GitHub, installed the world into it, and:
  - Ran the real unit test suite for real (previously only syntax-checked, since no
    Archipelago core was available in-session before): all 21 tests / 282 subtests pass.
  - Ran `Generate.py` against the new sample YAML with the world installed both as loose
    files and as a real `.apworld` zip -- both produce a valid output archive.
  - Built the `.apworld` through the actual `Build APWorlds` code path
    (`worlds/LauncherComponents.py`'s `_build_apworlds`, invoked directly rather than
    through the GUI Launcher) instead of trusting the existing `dist/superhot.apworld`.
    Comparing the two: the manifest (`version`/`compatible_version`/etc.) matched exactly,
    but the old file was missing the `test/` folder entirely -- a real gap from how it was
    last updated (per this log's own earlier note: unzipped, `data/levels.json` swapped,
    rezipped by hand, rather than rebuilt from scratch), not something the loose-file
    testing above would have caught. Replaced `dist/superhot.apworld` with the freshly
    built one.
- Clean rebuild of the mod from scratch (`rm -rf bin obj`, full `dotnet build`) still
  succeeds with 0 errors.

## Round 18: BASE_ID's "unreserved placeholder" caveat was based on a wrong assumption

Real correction, sourced from an apworld-dev Discord conversation the user read and
brought back rather than something found independently: item/location ids in Archipelago
do **not** need to be globally unique across every game in a multiworld. That used to be
true in older Archipelago versions (per the Discord thread: "Ids used to be global, so
you had to offset"), but isn't anymore -- "a lot of worlds have no reason to undo that
effort so it's still there," which is exactly the situation this project was already in.

Confirmed directly against Archipelago's own current docs rather than taking the Discord
conversation at face value: `docs/world api.md`'s Locations section states "The ID needs
to be unique across all locations within the game. Locations and items can share IDs, and
locations can share IDs with other games' locations" -- and the Items section says the
same for items. So the scope was always meant to be per-game, not global, and the
server resolves a check by (game, id), never by id alone.

This directly contradicts what Round 10 wrote into the README (the note that `BASE_ID` is
an "unreserved placeholder" worth caution around if paired with other unofficial worlds)
and what I told the user in this same conversation before checking. That framing was
wrong, not just imprecise -- there was never a collision risk to begin with, official or
unofficial worlds. Fixed everywhere that claim appeared:

- `README.md`'s "Known limitations" bullet about `BASE_ID` -- removed, since it no longer
  describes a real limitation.
- `apworld/superhot/Items.py`'s comment above `BASE_ID` -- rewritten to explain it's an
  arbitrary internal starting point, not something requiring maintainer coordination.
- `ARCHITECTURE.md`'s "The id scheme" section -- "globally unique" corrected to "unique
  within this world's own tables."

No code changes -- `BASE_ID = 3891000` and all its offsets are unaffected either way, this
was purely a documentation/understanding correction. `NOTES.md`'s own Round 10 entry
(the one that originally added the now-corrected claim) is left as-is above, since it
accurately records what was actually done at the time -- this entry is the correction,
not a rewrite of history.

## Round 19: first real 2-player multiworld confirmed

Real milestone, reported directly: "I did testing with a friend in a multiworld and it
worked perfectly." This is the first confirmed run with a second real player actually
connected to the same room -- previously an explicitly flagged gap (every earlier round's
"still beta" caveat called out solo-only testing). No bugs reported from this session, so
nothing to fix here -- just closing out a real, previously-open unknown. Updated the
"still beta" caveat in `README.md` and `TESTING.md` to reflect this instead of continuing
to claim it was untested with a second player, since that stopped being true.

Still open: more than 2 players, and setups other than Windows/this exact game+MelonLoader
version, remain untested.

## Design decisions still open

- Should challenge-mode / endless-mode unlocks be locations in v1, or a stretch goal?
- Now that the hub's real constraints are known (sequential-or-everything, no native
  partial unlock), is per-level out-of-order unlocking via `UnlockState` actually the
  experience we want, or would a simpler "everything unlocks at once when you've
  received N of 34 items" model (using the native `unlockEverything` flag) be more
  robust for a v1, with true per-level unlocking as a v2 refinement?
- Multiplayer/co-op is not a thing in SUPERHOT, so this is single-player-per-slot only,
  same as most AP worlds.

## Architecture reference

Based on researching `ArchipelagoULTRAKILL` (closest existing analog -- also a Unity FPS
game with an AP integration): the in-game mod embeds the official C# client library
`Archipelago.MultiClient.Net` directly (it opens its own WebSocket to the AP server, no
separate Python process needed). Harmony patches on the game's own pickup/completion code
detect events and call `Session.Locations.CompleteLocationChecks(...)`. Items received over
the network arrive via a `Session.Items.ItemReceived` event, which -- because it fires off
Unity's main thread -- gets queued and applied later during a MonoBehaviour `Update()` tick.
That pattern held up once real SUPERHOT internals were available; `mod/SuperhotArchipelago/`
is built on it.
