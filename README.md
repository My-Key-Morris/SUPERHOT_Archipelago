# SUPERHOT Archipelago Integration

Goal: make SUPERHOT (2016, original — not MIND CONTROL DELETE) playable as a game slot in
[Archipelago](https://archipelago.gg), the multi-game randomizer.

This has two halves:

1. **`apworld/superhot/`** — the Python world definition that plugs into Archipelago's
   generator. Defines the item pool, the location pool, and the logic connecting them.
2. **`mod/SuperhotArchipelago/`** — a [MelonLoader](https://melonwiki.xyz) mod that runs
   inside the SUPERHOT process. It detects in-game events (level completions, in-level
   secrets found) and reports them to the Archipelago server as "checks," and applies
   items you receive from other players (level unlocks) back into the game.

See `ARCHITECTURE.md` for a full file-by-file breakdown of both halves, `TESTING.md` for
how to run this yourself, and `NOTES.md` for the dated log of every real bug found (mostly
via actual playtesting against the real game) and how it was fixed.

## Design (revised after seeing the real game code, and after real playtesting)

SUPERHOT's campaign is a linear chain of story levels navigated through an in-fiction
"computer" hub. There's no branching, and (unlike MIND CONTROL DELETE) no persistent
inventory between levels, so there isn't a natural "item" pool the way most AP worlds have
one. `apworld/superhot/data/levels.json` lists 32 real levels extracted directly from the
game's own data (two more, both dead "Cyberspace" intermission entries with no real level
behind them, were removed after a playtest confirmed neither could ever get a hub button —
see `NOTES.md`).

Current design:
- **Locations (58):** one `<level> Complete` for 31 of the 32 levels, plus one
  `<level> Secret` for the 27 levels that have an in-level secret console. The 32nd level
  (the game's real ending) deliberately has no completion location of its own — finishing
  it ends the run, and a real, fillable check behind "beat the entire game" would be bad
  multiworld design if another player's own progression depended on it. A dedicated
  `Victory` event location (no real network id) signals completion instead.
- **Items (58 real + 1 event):** one `Level Access` item per level (32 — the first
  level's is "useful"-classified flavor rather than progression, since its location has no
  access rule), padded with 26 `White Space` filler items to match the real location
  count, plus the logic-only `Victory` event item.
- Secret in-hub computer terminals are real locations, not just an idea — see above.

It's a fully linear chain, so it's a simple but legitimate AP world (similar in spirit to
other linear/no-branching games in the ecosystem) — every level receives exactly one
shuffled unlock item, so other players' items can land in your world and vice versa, but
there's no logic complexity beyond "you need item N to reach location N." The game's own
hub UI doesn't natively support unlocking levels out of order, which the mod works around
with its own unlock-tracking layer (see `ARCHITECTURE.md`).

## Status

- **`apworld/superhot/`** generates successfully — verified against a real clone of
  `ArchipelagoMW/Archipelago` (`Generate.py` produces a valid `.zip` with a coherent
  playthrough), re-checked after every subsequent change to the level list and id scheme.
  A real unit test suite (`apworld/superhot/test/`) runs against the same checkout.
- **`mod/SuperhotArchipelago/`** compiles against the actual `Assembly-CSharp.dll`,
  `MelonLoader.dll`, `0Harmony.dll`, and has been run for real, repeatedly, against a
  locally-hosted Archipelago server: connecting, sending checks, and receiving items all
  work end to end. Real playtesting (not just a clean compile) found and fixed a long list
  of real bugs along the way — level-skip softlocks on several different level-transition
  paths, checks silently not sending for levels that end via a scripted transition instead
  of the normal ending, and a hub-button click-blocking regression, among others — all
  documented with root cause in `NOTES.md`.
- **Connecting is done in-game**, not by hand-editing a config file: an `ARCHIPELAGO`
  icon on the hub's main screen (alongside `LEVELS`/`ENDLESS`) opens a real, native-styled
  screen for entering your server/slot/password, with live connection status shown right
  on the icon. Editing `MelonPreferences.cfg` directly still works too, for
  scripting/automation — see `TESTING.md`.
- **Still beta.** Testing so far has been solo, on one Windows machine, one game install.
  There's no confirmed run yet with a second real player connected to the same multiworld
  room. If something breaks for you, see `TESTING.md`'s troubleshooting section for what
  to send along.
- `apworld/superhot/Items.py`'s `BASE_ID` is an unreserved placeholder, not a range
  assigned by the Archipelago maintainers — fine for running this as a standalone/custom
  world, but worth knowing if you're also running other unofficial, unlisted worlds in the
  same multiworld seed.
