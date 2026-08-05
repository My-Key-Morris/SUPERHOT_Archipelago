# SUPERHOT Archipelago

The original SUPERHOT (2016 — not MIND CONTROL DELETE) as a playable game slot in
[Archipelago](https://archipelago.gg), the multi-game randomizer.

⚠ *Solo-tested on Windows, one machine, one game install. Still beta — see [Status](#status--known-limitations) below.* ⚠

## Required Software

- SUPERHOT: [Steam Store](https://store.steampowered.com/app/322500)
- Archipelago: [Releases Page](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
- This apworld: [Releases Page](<your-repo-url>/releases/latest) *(update once the repo is up)*
- [MelonLoader](https://melonwiki.xyz) — the mod loader the in-game half runs on

**See [TESTING.md](TESTING.md) for the full first-time setup walkthrough.**

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

</details>

## Setting it up

<details>
<summary><h3 style="display: inline">Install steps</h3></summary>

1. Install [MelonLoader](https://melonwiki.xyz) into your SUPERHOT install.
2. Grab the latest mod release and drop the `Mods` and `UserLibs` folders it contains into
   your SUPERHOT folder (same level as `SUPERHOT.exe`).
3. Install `superhot.apworld` into Archipelago via the Launcher's **Install APWorld**
   button, generate/host a game as usual.
4. Launch SUPERHOT, select the `ARCHIPELAGO` icon on the hub's main screen, and enter your
   server/slot/password — `Tab`/`Enter` move between fields, `Enter` on the last field
   connects, `Esc` closes the screen. Settings are saved automatically.

Full details, including troubleshooting, are in [TESTING.md](TESTING.md).

</details>

## Status & known limitations

<details>
<summary><h3 style="display: inline">Status</h3></summary>

- The apworld generates successfully against a real Archipelago checkout, with a real
  unit test suite (`apworld/superhot/test/`) that runs against it.
- The mod has been run for real, repeatedly, against a locally-hosted server: connecting,
  sending checks, and receiving items all work end to end. Real playtesting found and
  fixed a long list of real bugs along the way — see [NOTES.md](NOTES.md) for the dated
  log of every one, with root cause.
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
[issue](<your-repo-url>/issues) *(update once the repo is up)* — see `NOTES.md`'s
troubleshooting note in `TESTING.md` for what's most useful to include.

## Contributing / how this is built

See [ARCHITECTURE.md](ARCHITECTURE.md) for a full file-by-file breakdown of both the
apworld and the mod, and [NOTES.md](NOTES.md) for the complete history of what's been
found and fixed.

### Tools

- [MelonLoader](https://melonwiki.xyz) & [Harmony](https://github.com/pardeike/Harmony) —
  the mod's runtime and patching framework
- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) —
  official C# client library
- [ilspycmd](https://github.com/icsharpcode/ILSpy) — decompiling the real game assembly to
  confirm every claim about SUPERHOT's internals against the actual shipped code, rather
  than guessing

### Credits

- Michael — apworld & mod
