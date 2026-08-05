# Architecture

How the SUPERHOT Archipelago integration is put together: the Python **apworld** (the
world definition Archipelago's generator/server use) and the C# **mod** (what actually
runs inside the game). See `README.md` for the project pitch, `TESTING.md` for how to run
it yourself, and `NOTES.md` for the dated log of every bug found and fixed along the way —
this document is the "how it's structured" reference, not a changelog.

## The big picture

```
 apworld/superhot/  --(generation time)-->  Archipelago server  <--(runtime)-->  mod/SuperhotArchipelago/
 (Python, runs in          produces a          (hosts the room,      WebSocket      (C#, runs inside the
  the AP generator)        multiworld seed      tracks checks,        connection     SUPERHOT.exe process,
                            .zip once)           forwards items)                     via MelonLoader)
```

The apworld and the mod never talk to each other directly — they only agree on a shared
numbering scheme (see "The id scheme" below) and both talk independently to the
Archipelago server. The apworld runs once, offline, when a multiworld game is generated.
The mod runs continuously while SUPERHOT is open, reporting checks and applying received
items over a live connection.

## The apworld (`apworld/superhot/`)

This is a standard Archipelago "world" package — the same shape as any other game
supported by the generator. Archipelago imports it (either as a loose folder or, once
packaged, as a `.apworld` zip) and calls a fixed set of methods/reads a fixed set of
module-level tables to build a multiworld.

| File | Role |
|---|---|
| `__init__.py` | The `SuperhotWorld` class — the actual entry point Archipelago's `AutoWorld` machinery loads. Wires `create_regions`, `create_item`, `create_items`, `get_filler_item_name`, and `set_rules` to the functions defined in the other files below. |
| `Items.py` | Defines every item: one `Level Access: <name>` progression item per level, the `Victory` event item, and the `White Space` filler item. Builds `item_table` / `item_name_to_id`. |
| `Locations.py` | Defines every real, checkable location: one `<level> Complete` per level (except the last — see "The final level is special" below) plus one `<level> Secret` for levels that have an in-level secret console. Builds `location_table` / `location_name_to_id`. |
| `Regions.py` | The region graph. Deliberately flat — a single `"Menu"` region holding every location, since SUPERHOT's hub has no explorable space to model (see its own docstring). Also creates the dedicated `Victory` event location. |
| `Rules.py` | Access logic: location `<level> Complete` requires having received `Level Access: <level>`. Because the game is fully linear and each level has exactly one gating item, no location needs a *chain* of every earlier item — just its own. Level 1 has no rule (always reachable). Sets `multiworld.completion_condition` to require the `Victory` item. |
| `Options.py` | Player-facing options (`SuperhotOptions`). Currently empty — a stub kept so `__init__.py` has something real to import. |
| `data/levels.json` | The actual level list this whole package is built from: `order`, `gameId` (the real game's internal level index), `id` (Unity scene name), `name` (display name), `hasSecret`. Extracted directly from the real game's data, not hand-guessed — see the file's own `_source`/`_caveats` fields for how and what's still uncertain. **Shared 1:1 with the mod's copy at `mod/SuperhotArchipelago/data/levels.json`.** |
| `archipelago.json` | Package metadata Archipelago's Launcher reads (game name, world version, minimum AP version). |
| `docs/setup_en.md` | Player-facing setup guide, surfaced in Archipelago's web docs / Launcher tutorial list. |
| `test/` | Real unit tests against Archipelago's own test harness — see "Testing the apworld" below. |

### The id scheme

Every location and item needs a globally unique integer id. This world reserves a block
starting at `BASE_ID = 3891000` (a placeholder — a real upstream submission would need a
maintainer-assigned range) and slices it up by adding fixed offsets:

- **Level-complete locations:** `BASE_ID + order` (e.g. level 5 → `3891005`).
- **Secret locations:** `BASE_ID + 20000 + order` (`SECRET_LOCATION_OFFSET`, kept well
  clear of the location range above).
- **Level-access items:** `BASE_ID + 10000 + order` (`ITEM_ID_OFFSET`).
- **White Space (filler item):** `BASE_ID + 10000 + 100` (`WHITE_SPACE_ITEM_ID_OFFSET`).
- **Victory:** no real id (`code = None`) — Archipelago requires event items to live on
  event locations (`address = None`), since there's nothing to send over the network when
  they're "checked."

The mod's `Core/LevelCatalog.cs` reproduces these exact same constants by hand
(`BaseId`, `ItemIdOffset`, `SecretLocationIdOffset`, `WhiteSpaceItemId`) so it can compute
the same codes independently, without a shared library between the Python and C# sides.
**There is no automated check tying the two copies together** — if they ever drift, checks
and items will silently map to the wrong level. This is the single most important thing to
keep in sync when editing either side.

### The final level is special

`"34 - Free"` (the last entry in `data/levels.json`) does **not** have its own
`Complete` location — a deliberate design choice, not an oversight. Finishing it ends the
run, and a real, regular, fillable item sitting behind "beat the entire game" would be bad
multiworld design (another player's own progression could end up depending on this
player's full campaign clear). Instead, the dedicated `Victory` event location gates on
the final level's access item and signals completion on its own. `Rules.py`'s main loop
explicitly skips fetching a location for the final level; `location_table` is built from
`LEVELS[:-1]`, not `LEVELS`. See `Locations.py`'s module docstring and `NOTES.md`'s
"Round 10" for the full reasoning.

### Filler padding

`create_items()` must produce exactly one item per real location. Because secret
locations added a second location per level, `len(location_table)` is now bigger than
`len(LEVELS)` — and, contrary to what a first read of Archipelago's `Fill.py` suggests,
**nothing in core Archipelago pads a short item pool automatically**;
`distribute_items_restrictive()` just raises `FillError` if items run short. `__init__.py`
explicitly pads the remainder with `create_filler()` calls
(`filler_needed = len(location_table) - len(LEVELS)`), and `get_filler_item_name()`
always returns `"White Space"` rather than the base class's default (a random item name,
which could return a real progression item as "filler"). See `NOTES.md`'s "Round 8"/"Round
9" for the bug this fixes and why a distinct filler item exists at all.

### Testing the apworld

`test/bases.py` defines `SuperhotTestBase`, shared by every test file, with
`run_default_tests = True` set explicitly — required, not optional (see the file's own
docstring: this property defaults to `False` for a class like this one, which silently
skipped `test_fill` and friends the first time this was written, masking the filler bug
above). `test/test_level_access.py` and `test/test_filler.py` build SUPERHOT-specific
checks on top: level 1 needs no item, each level needs exactly its own access item (not
a neighbor's), the level/secret counts match `data/levels.json`, the final level has no
completion location, `Victory` needs the final level's item, the itempool size matches the
real location count, and `White Space` is a real, correctly-classified, distinctly-named
filler item.

These tests only run against a real Archipelago core checkout (they import from
`test.bases`, `worlds.generic.Rules`, `BaseClasses`, etc. — all part of the main
Archipelago repository, not this project). To run them: clone
`ArchipelagoMW/Archipelago`, copy `apworld/superhot` into that checkout's `worlds/`
folder as `worlds/superhot`, then `pytest worlds/superhot/test/ -v` from the checkout
root (Python 3.12+ required).

### Packaging

Archipelago's Launcher ("Build APWorlds") or a manual zip both produce the same shape: a
`superhot/` folder at the zip root containing the nine files listed in the table above
(no `test/` — tests aren't shipped) as a `.apworld` file. `Items.py` reads
`data/levels.json` via `pkgutil.get_data()` rather than plain `pathlib` file I/O
specifically because a packaged `.apworld` is a zip loaded via `zipimport` — there's no
real filesystem directory for `Path(__file__).parent` to point at once it's zipped.
`dist/superhot.apworld` in this project is the current built copy.

## The mod (`mod/SuperhotArchipelago/`)

A [MelonLoader](https://melonwiki.xyz) mod (net472, Harmony patching) that loads into the
SUPERHOT process itself. It has two jobs: detect in-game events and report them to the
Archipelago server as checks, and apply items received from other players back into the
game. It embeds the official `Archipelago.MultiClient.Net` C# client directly — it opens
its own WebSocket to the AP server, no separate helper process needed.

### Core/ — state and infrastructure

| File | Role |
|---|---|
| `Mod.cs` | Entry point (`MelonMod` subclass). Loads config, loads the level catalog, opens the connection, wires reconnect-on-settings-change, and drains the item queue every frame (`OnUpdate`). Also a last-resort safety net on `OnSceneWasLoaded`: if a scene that just loaded resolves to a tracked-but-locked level (through some path none of the launch-time gates caught), kicks back to the hub immediately. |
| `Config.cs` | Reads/writes `Server` / `Slot` / `Password` via MelonLoader's shared preferences file (`UserData/MelonPreferences.cfg`, `[SuperhotArchipelago]` section). `Save()` lets `ArchipelagoConnectApp.cs` persist values changed through the in-game connect screen the same way `Load()` does for the very first run — creating/setting entries alone doesn't write to disk by itself. |
| `ArchipelagoConnection.cs` | Thin wrapper around `Archipelago.MultiClient.Net`'s `ArchipelagoSession`. Owns the WebSocket connection only — never touches game/Unity state itself. Exposes `LastError` so `ArchipelagoConnectApp.cs` can show a specific failure reason in-game instead of only in the console; `Connect()` catches exceptions from `TryConnectAndLogin` (confirmed by testing against a bad address that it can throw, not just return an unsuccessful result) so a bad in-game input can't take the mod down. |
| `ArchipelagoConnectApp.cs` | A real, native app screen (subclasses the game's own `SHGUIappbase`, the same base every simple bordered pop-up app screen uses) for setting Server/Slot/Password and connecting, so players don't have to find and hand-edit `MelonPreferences.cfg`. Opened via `Patches/ConnectionButtonPatch.cs`'s hub button. Free-text entry (`Input.inputString` accumulation, manual backspace/enter handling, a blinking caret) mirrors the exact pattern the game's own native dev console (`AppSHConsole`) already uses — confirmed via decompile rather than invented, since nothing in the game's UI framework has an off-the-shelf text box widget. Replaced an earlier Unity IMGUI overlay (`ConnectionUI.cs`, removed) that looked like a generic mod menu rather than something built into the game. |
| `LevelCatalog.cs` | Loads `data/levels.json` (shipped next to the built DLL) into `LevelEntry` records, and reproduces the apworld's id scheme (see "The id scheme" above) so the mod can compute the same location/item codes independently. Keyed by `LevelInfo.ID` (`LevelIdToLevel`), **not** scene name — several levels reuse the same Unity scene for different story beats, which would make scene-name lookups silently ambiguous. |
| `LevelAccessGuard.cs` | The one shared "is this level actually allowed right now" check, used by every patch that can result in a level loading. Resolves forward through any untracked raw entries (interstitial/segue scenes not in `data/levels.json`) to the next level actually in the catalog before checking unlock state — the fix for the Subway/Station softlock bugs (see `NOTES.md`). |
| `UnlockState.cs` | A local `HashSet<int>` of unlocked level ids. Exists because the game's native hub-unlock logic only supports two states (sequential-up-to-highest-finished, or everything) — no native support for unlocking individual out-of-order levels, which is exactly what a shuffled AP item pool needs. Layered on top of, not instead of, native logic. |
| `ButtonTextCache.cs` | Caches each level's clean, unscrambled hub button text before the native lock pass mangles it, so it can be restored later for levels the mod considers unlocked (even if native logic would otherwise still scramble them). |
| `ItemManager.cs` | Handles items received from the server. `ItemReceived` fires off Unity's main thread, so items are queued and drained on `Mod.OnUpdate()` instead of applied immediately. Recognizes `White Space` (filler, no-op) and `Victory` (id `0`/`None`, logic-only) as expected non-level items before falling through to "unknown item" for anything else. |
| `LocationManager.cs` | Sends location checks (`CheckLocation`, `CheckSecretLocation`) and reports goal completion (`SetGoalAchieved()`, a signal independent from any location check). Also exposes `IsLevelCompleted()` and `IsSecretCompleted()`, both read live from `Session.Locations.AllLocationsChecked` rather than a second local set, used for the hub's grey/white completion coloring and for correcting the native "CRACKED!" secret badge/description text (which otherwise reads stale native save data with no knowledge of the current AP run — see `HubUnlockPatch.cs`). |

### Patches/ — Harmony hooks into the real game

Every file here is a `[HarmonyPatch]` targeting a specific method in the decompiled game
assembly, confirmed against the real `Assembly-CSharp.dll` rather than guessed. Grouped by
what they're for:

**Reporting completion** (game → Archipelago):
- `LevelCompletePatch.cs` — patches `LevelSetup.UnlockNextLevel()`, the one method every
  normal level-ending path funnels through.
- `AutoTransitionCheckPatch.cs` — some levels instead end via a smooth "no hub visit"
  transition (`LevelFlowControl.LoadNextLevel()` and four siblings) that never calls
  `UnlockNextLevel()` at all; this patches those directly so those levels still send a
  check. Sending is idempotent, so overlap with `LevelCompletePatch` is harmless.
- `SecretFoundPatch.cs` — patches `TerminalActivator.OnActivate()`; watches the private
  `secretFound` field's false→true transition to detect a genuine first find (not a
  revisit), then reports the level's secret location.

**Gating access** (Archipelago → game, blocking locked levels): SUPERHOT turned out to
have several independent ways a level can actually start loading, discovered one real
playtest bug at a time — each needed its own gate:
- `LevelGatePatch.cs` — `SHGUI.LaunchLevelAppTunnels()`, used by the hub's per-level
  buttons and the (now-hidden) `superhot.exe` shortcut.
- `ViaAppGatePatch.cs` — `SHGUI.LaunchLevelViaApp()`, used by most smooth level-to-level
  auto-transitions.
- `DirectLevelSkipPatch.cs` — `LevelSetup.LoadNextLevel()`, reachable by clicking through
  the end-of-level fade quickly enough to bypass both methods above.
- `TitleCardGatePatch.cs` — the classic "SUPERHOT" title-card click-through
  (`LevelFlowControl.SuperHotSuperHotEnding()` / `...ClickThrough()`); rather than block
  the launch call itself (which fires 0.1–0.4s later, after camera/audio effects already
  started — too late, causing a softlock), this suppresses the *input* that would trigger
  the advance, via Harmony's private-field access to the method's own input state.
- `AutoTransitionCheckPatch.cs` does double duty here too — the same four/five methods it
  patches for completion-reporting also need pre-emptive blocking *before* their
  transition effects start, for the same "too late" reason as the title-card gate.

All of these gates call the same `LevelAccessGuard.ShouldBlock()` and, when blocking, queue
a short "LOCKED" message via `TextManager.AddUptitleToQueue` and redirect to the hub
(`SHGUI.current.LaunchLevelAppTunnels("SHMenu", false)`) rather than leaving the player
stuck mid-transition.

**Cosmetics and hub behavior**:
- `LevelButtonCapturePatch.cs` — snapshots each level's clean button text the moment
  `piOsMenu.PrepareLevelCommanderButtonForLevel()` builds it, before native locking can
  scramble it.
- `HubUnlockPatch.cs` — the real three-state visual logic, as a `Postfix` on
  `piOsMenu.LockUnfinishedLevels()`: garbled text + grey + actually locked if not
  unlocked; clean text + grey + clickable if unlocked but not yet completed; clean text +
  white + clickable if unlocked and completed. Layers Archipelago's own unlocks
  (`UnlockState`) on top of native sequential unlocking rather than replacing it. The same
  per-button loop also corrects the native "CRACKED!" secret badge/description text for
  any level with a secret, using `LocationManager.IsSecretCompleted()` instead of trusting
  `LevelInfo.SecretsFound()`'s native save-data read (which can show "CRACKED!" from a
  leftover flag on a save that predates the current AP run entirely).
- `MenuVisibilityPatch.cs` — forces every hub menu node visible from the very first boot
  (native `ShouldBeShown()` normally hides most of the hub, including "LEVELS" itself,
  behind tags earned through native progression that an AP run wouldn't have yet).
- `SuperhotExeButtonPatch.cs` — declines to add the `"superhot.exe"` hub shortcut at all
  (a cosmetic removal requested to avoid a redundant, ungated entry point).
- `ConnectionButtonPatch.cs` — adds a real `ARCHIPELAGO` button to the hub's top-level
  screen (a `Postfix` on `piOsMenu.CreateViewFromNode()`, gated on the freshly-created
  view's `isRoot` flag so it's only added once per hub visit, not in every subfolder),
  showing live `ONLINE`/`OFFLINE` status and pushing `Core/ArchipelagoConnectApp.cs` via
  `SHGUI.current.AddViewOnTop` (the same general mechanism `SHGUI.LaunchAppByName` uses
  internally for any other app screen) on click. Uses `SHGUIcommanderbutton`'s own generic
  `Action<SHGUIcommanderbutton> OnActivate` delegate directly — confirmed via decompile
  this isn't hardcoded to level-launching the way it first looked, so this button needs no
  `LevelInfo` and never touches any of the level-access gates above. The ONLINE/OFFLINE
  text isn't just set once at creation: the button's own `CreateViewFromNode` Postfix only
  fires when the hub's root view is rebuilt from scratch (confirmed via decompile —
  popping back out of `ArchipelagoConnectApp` reveals the same already-built root view,
  it doesn't rebuild it), so a connect made while already standing on the hub would leave
  the label stale until a full trip back to the Main Menu and back. `RefreshLabel()`
  re-derives it live from `Mod.Connection.IsConnected` and is called both at creation and
  every frame from `Mod.OnUpdate()`, via a static `Button` reference kept pointed at
  whichever instance is currently on screen.
- `StoryFinishedSuppressPatch.cs` — suppresses every write of the native
  `storyFinished = true` save flag, which normally comes from a narrative fake-ending
  ("22 - Hacker") or an unlock-everything cheat, neither of which should force-quit an
  in-progress AP run. Goal completion is entirely independent (`LocationManager`'s
  `SetGoalAchieved()`), so nothing depends on this flag for correctness.

### Build and deploy

`SuperhotArchipelago.csproj` targets `net472` (confirmed a Mono build, not IL2CPP, since
`SH_Data/Managed/Assembly-CSharp.dll` exists). References `MelonLoader.dll`, `0Harmony.dll`,
`Assembly-CSharp.dll`, and a few `UnityEngine` modules directly from a real game install
path (`SuperhotGameDir` MSBuild property, overridable via `-p:SuperhotGameDir=...`), plus
the `Archipelago.MultiClient.Net` NuGet package. `data/levels.json` is copied to the
output directory automatically (`CopyToOutputDirectory`).

Build with `dotnet build -c Release`. The output DLL/PDB
(`bin/Release/net472/SuperhotArchipelago.dll`/`.pdb`) plus `data/levels.json` get copied
into the real game's `Mods/` folder to actually load; `Archipelago.MultiClient.Net.dll`
needs to be present in `UserLibs/` alongside it (see `TESTING.md`).

## Keeping the two halves in sync

Nothing automated ties the apworld and the mod together — they're independent codebases
that happen to agree on a numbering scheme and a level list by convention. Two things
must be hand-updated together whenever either changes:

1. **`data/levels.json`** exists in two copies (`apworld/superhot/data/levels.json` and
   `mod/SuperhotArchipelago/data/levels.json`) and must be kept byte-identical.
2. **The id constants** (`BASE_ID`/`ITEM_ID_OFFSET` in `Items.py`,
   `SECRET_LOCATION_OFFSET` in `Locations.py`, `WHITE_SPACE_ITEM_ID_OFFSET` in `Items.py`)
   must match their C# counterparts (`BaseId`/`ItemIdOffset`/`SecretLocationIdOffset`/
   `WhiteSpaceItemId` in `LevelCatalog.cs`) exactly. If they ever drift, checks and items
   will silently map to the wrong level — there's no runtime check that would catch this.

Any change to `data/levels.json` (adding/removing/reordering levels) changes every
subsequent level's id, so it also invalidates any seed already generated from an older
version — not id-compatible across a level-list edit.
