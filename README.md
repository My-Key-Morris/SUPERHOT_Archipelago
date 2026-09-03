# SUPERHOT Archipelago

The original SUPERHOT (2016 — not MIND CONTROL DELETE) as a playable game slot in
[Archipelago](https://archipelago.gg), the multi-game randomizer.

## Required Software

- SUPERHOT: [Steam Store](https://store.steampowered.com/app/322500)
- Archipelago: [Releases Page](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
- This apworld: [GitHub Releases Page](https://github.com/My-Key-Morris/SUPERHOT_Archipelago/releases/latest)
- The mod: [Nexus Mods](https://www.nexusmods.com/superhot/mods/7)
- [MelonLoader](https://melonwiki.xyz) — the mod loader the in-game half runs on

## What does randomization do to this game?

SUPERHOT's campaign is a linear chain of story levels navigated through an in-fiction
"computer" hub — no branching, no persistent inventory between levels. Each level is
shuffled behind its own `Level Access` item, so levels unlock out of order as items come
in from other players, instead of the game's native strictly-sequential unlock. In-level
secret consoles are also real, separately-checkable locations.

The goal is beating the final level, same as vanilla.

<details>
<summary><h3 style="display: inline">What gets randomized</h3></summary>

- **58 locations by default:** one `<level> Complete` for 31 of the 32 real levels, plus
  one `<level> Secret` for the 27 levels that have an in-level secret console. The 32nd
  level (the game's real ending) has no completion location of its own.
- **58 real items + 1 event by default:** one `Level Access` item for every level except
  the first (its location has no access rule, so it's always open — an item for it would
  do nothing), padded with `White Space` filler items to match the location count, plus
  the logic-only `Victory` event.
- **Optionally excludable levels:** the `exclude_slow_levels` YAML option (off by default)
  removes `99 - Dog1`, `98 - Dog2`, `99 - Dog3`, and `32 - Longway` (including Longway's
  secret) from both pools entirely — they're known for slower, more repetitive gameplay
  than the rest of the campaign. An excluded level stays freely playable in-game, always
  unlocked, exactly like `01 - Kick`; it just isn't part of the multiworld item/location
  pool. See `dist/superhot_example.yaml` for where to set it.

</details>

<details>
<summary><h3 style="display: inline">What other changes are made to the game</h3></summary>

- **Everything Archipelago-related lives in one hub folder**: an `ARCHIPELAGO` icon on the
  hub's main screen (alongside `LEVELS`/`ENDLESS`) opens a folder with three entries.
- **Connecting is done in-game**, not by hand-editing a config file: the `CONNECT` entry
  opens a real, native-styled screen for entering your server/slot/password, with live
  connection status shown right on the icon.
- **Archipelago mode can be turned off** without uninstalling the mod: the `AP MODE` entry
  flips between `ON` and `OFF` in one click. Off plays SUPERHOT exactly like vanilla — no
  level gating, no hub overlay — and drops any active connection; back on reconnects and
  picks up right where you left off.
- **In-game notifications**: a short popup appears while playing whenever you receive an
  item or send a check — nothing pops up for the batch of past items a fresh connection
  replays, only for genuinely new activity. The `AP LOG` entry opens a scrollable history
  of everything received and sent this run, for anything you missed or want to look back
  on.
- The hub's native lock logic only supports two states (sequential-up-to-highest-finished,
  or everything unlocked) — a layer on top tracks Archipelago's own out-of-order unlocks
  without replacing that native logic.
- Level buttons and their secret-console badges reflect what Archipelago has actually
  checked this run, not leftover native save data from a previous playthrough.
- **`34 - Free`** (the real ending) has a second lock on top of the normal one: even
  once its own access item is received, it stays closed until enough of the other 31
  levels are actually completed, not just unlocked — so a lucky early item can't end
  the run right away. Its hub row shows live progress (e.g. `12/25`) in place of the
  usual status text the whole time it's still short — before its own access item is
  received as well as after — and the same count shows up as a readable line in the
  right-side preview panel when you scroll to it, the same way a cracked/not-cracked
  secret shows up for other levels. How many levels are required is a YAML option,
  `levels_required_for_free` (0–31, default 25, 0 disables it) — see
  `dist/superhot_example.yaml` for where to set it.

</details>

## Setting it up

<details open>
<summary><h3 style="display: inline">Install steps</h3></summary>

1. Install the official [Archipelago app](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
   (the real multiworld generator/server/client — this is what actually creates and hosts
   a game).
2. Open the Archipelago Launcher, click **Install APWorld**, and pick `superhot.apworld`
   from [this project's GitHub releases](https://github.com/My-Key-Morris/SUPERHOT_Archipelago/releases/latest).
   SUPERHOT should now show up as a supported game.
3. Install [MelonLoader](https://melonwiki.xyz) into your SUPERHOT install (same folder as
   `SUPERHOT.exe`). Launch and close SUPERHOT once to finalize the MelonLoader install.
4. Grab the mod from [Nexus Mods](https://www.nexusmods.com/superhot/mods/7) and drop the `Mods` and `UserLibs`
   folders it contains into your SUPERHOT folder, alongside `SUPERHOT.exe`.
5. Generate and host a game as usual through the Archipelago Launcher (**Generate**, then
   **Host** on the resulting `.zip`) — or use an already-hosted room if you're joining
   someone else's.
6. Launch SUPERHOT. MelonLoader prints console output on top of the game — look for lines
   starting with `[SuperhotArchipelago]` confirming it loaded.
7. On the hub's main screen (alongside `LEVELS`/`ENDLESS`), open the `ARCHIPELAGO` folder
   and select `CONNECT`, then enter your server/slot/password — `Tab`/`Enter` move between
   fields, `Enter` on the last field connects, `Esc` closes the screen. Settings are saved
   automatically, so you won't need to re-enter them next launch.
8. Play the first level. Watch the MelonLoader console for a line confirming a check was
   sent — that's the sign everything's actually wired up correctly.

**If something breaks:** open an issue with whatever the MelonLoader console printed
(especially red/error lines) and which step you got to.

</details>

## Bug Reports & Feature Requests

Found a bug, or something feel off? Please open an
[issue](https://github.com/My-Key-Morris/SUPERHOT_Archipelago/issues) with whatever the
MelonLoader console printed (especially red/error lines) and which step you got to —
that's the fastest way to track down whether it's a patch not firing, a version mismatch,
or something more basic.

## Contributing

### Tools

- [MelonLoader](https://melonwiki.xyz) & [Harmony](https://github.com/pardeike/Harmony) —
  the mod's runtime and patching framework
- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) —
  official C# client library
- [ilspycmd](https://github.com/icsharpcode/ILSpy) — decompiling the real game assembly

### Credits

- Miikurb — apworld & mod
