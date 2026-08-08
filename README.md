# SUPERHOT Archipelago

The original SUPERHOT (2016 — not MIND CONTROL DELETE) as a playable game slot in
[Archipelago](https://archipelago.gg), the multi-game randomizer.

## Required Software

- SUPERHOT: [Steam Store](https://store.steampowered.com/app/322500)
- Archipelago: [Releases Page](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
- This apworld: [Releases Page](<your-repo-url>/releases/latest) *(update once the repo is up)*
- [MelonLoader](https://melonwiki.xyz) — the mod loader the in-game half runs on

## What does randomization do to this game?

SUPERHOT's campaign is a linear chain of story levels navigated through an in-fiction
"computer" hub — no branching, no persistent inventory between levels. Each level is
shuffled behind its own `Level Access` item, so levels unlock out of order as items come
in from other players, instead of the game's native strictly-sequential unlock. In-level
secret consoles are also real, separately-checkable locations.

The goal is beating the final level, same as vanilla — no alternate goals yet.

<details>
<summary><h3 style="display: inline">What gets randomized</h3></summary>

- **58 locations:** one `<level> Complete` for 31 of the 32 real levels, plus one
  `<level> Secret` for the 27 levels that have an in-level secret console. The 32nd level
  (the game's real ending) has no completion location of its own.
- **58 real items + 1 event:** one `Level Access` item per level (the first level's is
  flavor rather than progression, since its location has no access rule), padded with
  `White Space` filler items to match the location count, plus the logic-only `Victory`
  event.

</details>

<details>
<summary><h3 style="display: inline">What other changes are made to the game</h3></summary>

- **Connecting is done in-game**, not by hand-editing a config file: an `ARCHIPELAGO`
  icon on the hub's main screen (alongside `LEVELS`/`ENDLESS`) opens a real, native-styled
  screen for entering your server/slot/password, with live connection status shown right
  on the icon.
- The hub's native lock logic only supports two states (sequential-up-to-highest-finished,
  or everything unlocked) — a layer on top tracks Archipelago's own out-of-order unlocks
  without replacing that native logic.
- Level buttons and their secret-console badges reflect what Archipelago has actually
  checked this run, not leftover native save data from a previous playthrough.
- **`34 - Free`** (the real ending) has a second lock on top of the normal one: even
  once its own access item is received, it stays closed until enough of the other 31
  levels are actually completed, not just unlocked — so a lucky early item can't end
  the run right away. Its hub button shows live progress (e.g. `12/25`) instead of the
  usual status text while it's still short. How many levels are required is a YAML
  option, `levels_required_for_free` (0–31, default 25, 0 disables it) — see
  `dist/superhot_michael.yaml` for where to set it.

</details>

## Setting it up

<details open>
<summary><h3 style="display: inline">Install steps</h3></summary>

1. Install the official [Archipelago app](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
   (the real multiworld generator/server/client — this is what actually creates and hosts
   a game).
2. Open the Archipelago Launcher, click **Install APWorld**, and pick `superhot.apworld`
   from this project's releases. SUPERHOT should now show up as a supported game.
3. Install [MelonLoader](https://melonwiki.xyz) into your SUPERHOT install (same folder as
   `SUPERHOT.exe`). Launch and close SUPERHOT once to finalize the MelonLoader install.
4. Grab the latest mod release from this project and drop the `Mods` and `UserLibs`
   folders it contains into your SUPERHOT folder, alongside `SUPERHOT.exe`.
5. Generate and host a game as usual through the Archipelago Launcher (**Generate**, then
   **Host** on the resulting `.zip`) — or use an already-hosted room if you're joining
   someone else's.
6. Launch SUPERHOT. MelonLoader prints console output on top of the game — look for lines
   starting with `[SuperhotArchipelago]` confirming it loaded.
7. On the hub's main screen (alongside `LEVELS`/`ENDLESS`), select the `ARCHIPELAGO` icon
   and enter your server/slot/password — `Tab`/`Enter` move between fields, `Enter` on the
   last field connects, `Esc` closes the screen. Settings are saved automatically, so you
   won't need to re-enter them next launch.
8. Play the first level. Watch the MelonLoader console for a line confirming a check was
   sent — that's the sign everything's actually wired up correctly.

**If something breaks:** open an issue with whatever the MelonLoader console printed
(especially red/error lines) and which step you got to.

</details>

## Status & known limitations

<details>
<summary><h3 style="display: inline">Status</h3></summary>

- The apworld generates successfully against a real Archipelago checkout, with a real
  unit test suite (`apworld/superhot/test/`) that runs against it.
- The mod has been run for real, repeatedly, against a locally-hosted server: connecting,
  sending checks, and receiving items all work end to end. Real playtesting found and
  fixed a long list of real bugs along the way, from level-skip softlocks to hub display
  issues — all since fixed.
- **Still beta**, but confirmed working with a second real player: a real 2-player
  multiworld room (both players actually connected, sending checks, and receiving items
  from each other) has been played end to end with no issues. Most testing has still been
  on one Windows machine/one game install, so different setups (different game version,
  different MelonLoader version, more than 2 players) are still less-traveled territory.

</details>

<details>
<summary><h3 style="display: inline">Known limitations</h3></summary>

- Challenge mode and Endless mode aren't tracked by Archipelago at all yet.
- No alternate goals besides the vanilla ending.

</details>

## Bug Reports & Feature Requests

Found a bug, or something feel off? Please open an
[issue](<your-repo-url>/issues) *(update once the repo is up)* with whatever the
MelonLoader console printed (especially red/error lines) and which step you got to —
that's the fastest way to track down whether it's a patch not firing, a version mismatch,
or something more basic.

## Contributing

Pull requests welcome. `apworld/superhot/` is a standard Archipelago world (Python);
`mod/SuperhotArchipelago/` is a MelonLoader/Harmony mod (C#, `net472`) that builds against
a real local SUPERHOT install.

### Tools

- [MelonLoader](https://melonwiki.xyz) & [Harmony](https://github.com/pardeike/Harmony) —
  the mod's runtime and patching framework
- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) —
  official C# client library
- [ilspycmd](https://github.com/icsharpcode/ILSpy) — decompiling the real game assembly to
  confirm every claim about SUPERHOT's internals against the actual shipped code, rather
  than guessing

### Credits

- Miikurb — apworld & mod
