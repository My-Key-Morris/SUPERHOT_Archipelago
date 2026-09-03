# SUPERHOT Archipelago

The original SUPERHOT (2016 — not MIND CONTROL DELETE) as a playable game slot in
[Archipelago](https://archipelago.gg), the multi-game randomizer.

## Required Software

- SUPERHOT: [Steam Store](https://store.steampowered.com/app/322500)
- Archipelago: [Releases Page](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
- This apworld: [GitHub Releases Page](https://github.com/My-Key-Morris/SUPERHOT_Archipelago/releases/latest)
- The mod: [Thunderstore](https://thunderstore.io/c/superhot/p/Archihot/SuperhotArchipelago/)
- [MelonLoader](https://melonwiki.xyz) — the mod loader the in-game half runs on

## What does randomization do to this game?

SUPERHOT's campaign is a linear chain of story levels played through an in-fiction
"computer" hub, with no branching and no inventory carried between levels. Each level is
shuffled behind its own `Level Access` item, so levels unlock out of order as items arrive
from other players instead of the game's normal strict sequence. In-level secret consoles
are separately-checkable locations too.

The goal is beating the final level, same as vanilla.

<details>
<summary><h3 style="display: inline">What gets randomized</h3></summary>

- **58 locations:** one `<level> Complete` for 31 of the 32 real levels, plus one
  `<level> Secret` for the 27 levels with an in-level secret console. The 32nd level (the
  game's real ending) has no completion location of its own.
- **58 items + 1 event:** one `Level Access` item per level except the first (already
  open by default, so an item for it would do nothing), padded with `White Space` filler
  to match the location count, plus the logic-only `Victory` event.
- **Optional exclusions:** the `exclude_slow_levels` YAML option (off by default) drops
  `99 - Dog1`, `98 - Dog2`, `99 - Dog3`, and `32 - Longway` (including its secret) from
  both pools — they're known for slower, more repetitive gameplay than the rest of the
  campaign. Excluded levels stay freely playable and always unlocked, same as
  `01 - Kick`; they just aren't part of the multiworld pool. See
  `dist/superhot_example.yaml` for where to set it.

</details>

<details>
<summary><h3 style="display: inline">What other changes are made to the game</h3></summary>

- **One hub folder for everything Archipelago-related**: an `ARCHIPELAGO` icon on the
  hub's main screen (alongside `LEVELS`/`ENDLESS`) opens a folder with three entries.
- **Connect in-game** instead of hand-editing a config file: `CONNECT` opens a native-
  styled screen for your server/slot/password, with live connection status on the icon.
- **Toggle Archipelago mode** without uninstalling: `AP MODE` flips `ON`/`OFF` in one
  click. Off plays vanilla SUPERHOT — no gating, no hub overlay — and drops any active
  connection; back on reconnects and picks up where you left off.
- **In-game notifications**: a short popup appears whenever you receive an item or send a
  check, skipping the batch of past items a fresh connection replays. `AP LOG` opens a
  scrollable history of everything received and sent this run.
- The hub's native lock logic only supports two states (sequential-up-to-highest-finished,
  or everything unlocked); a layer on top tracks Archipelago's out-of-order unlocks
  without replacing that native logic.
- Level buttons and secret-console badges reflect what Archipelago has actually checked
  this run, not leftover native save data from a previous playthrough.
- **`34 - Free`** (the real ending) has a second lock: even once its access item is
  received, it stays closed until enough of the other 31 levels are actually completed —
  so a lucky early item can't end the run right away. Its hub row shows live progress
  (e.g. `12/25`) the whole time it's short, both on the button and in the right-side
  preview panel. How many levels are required is a YAML option,
  `levels_required_for_free` (0–31, default 25, 0 disables it) — see
  `dist/superhot_example.yaml` for where to set it.

</details>

## Setting it up

<details open>
<summary><h3 style="display: inline">Install steps</h3></summary>

1. Install the official [Archipelago app](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
   — the real multiworld generator/server/client, and what actually creates and hosts a
   game.
2. In the Archipelago Launcher, click **Install APWorld** and pick `superhot.apworld`
   from [this project's GitHub releases](https://github.com/My-Key-Morris/SUPERHOT_Archipelago/releases/latest).
   SUPERHOT now shows up as a supported game.
3. Install the mod from [Thunderstore](https://thunderstore.io/c/superhot/p/Archihot/SuperhotArchipelago/).
   It declares [MelonLoader](https://melonwiki.xyz) as a required dependency, so a
   Thunderstore-aware mod manager (r2modman, Gale, the Thunderstore Mod Manager) installs
   MelonLoader alongside it automatically and drops the `Mods`/`UserLibs` contents
   straight into your SUPERHOT install. Installing by hand instead? Grab MelonLoader
   yourself first, install it into your SUPERHOT folder (same folder as `SUPERHOT.exe`),
   launch and close SUPERHOT once to finalize it, then unzip the mod's package and copy
   its `Mods` and `UserLibs` folders in the same way.
4. Generate and host a game as usual through the Archipelago Launcher (**Generate**, then
   **Host** on the resulting `.zip`) — or join an already-hosted room.
5. Launch SUPERHOT. MelonLoader prints console output on top of the game — look for
   lines starting with `[SuperhotArchipelago]` confirming it loaded.
6. On the hub's main screen, open the `ARCHIPELAGO` folder and select `CONNECT`, then
   enter your server/slot/password. `Tab`/`Enter` move between fields, `Enter` on the
   last field connects, `Esc` closes the screen. Settings save automatically.
7. Play the first level. A line in the MelonLoader console confirming a check was sent is
   the sign everything's wired up correctly.

</details>

## Bug Reports & Feature Requests

Found a bug, or something feel off? Open an
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
