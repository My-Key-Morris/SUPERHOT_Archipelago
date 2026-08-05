# Testing this yourself

Everything mechanical is already done for you. Here's what's already in place, then what
you need to do.

## Already done

- **`dist/superhot.apworld`** — the real, properly-packaged apworld file (built with
  Archipelago's own "Build APWorlds" tool, not a hand-rolled zip — an earlier hand-rolled
  attempt actually broke on data loading once zipped, a real bug that's now fixed in
  `apworld/superhot/Items.py`; see `NOTES.md`).
- **`dist/superhot_michael.yaml`** — a sample player file so you don't have to write one.
- **`SUPERHOT/Mods/SuperhotArchipelago.dll`** and **`SUPERHOT/UserLibs/Archipelago.MultiClient.Net.dll`**
  — the mod, already built and already dropped into your game folder in the right spots.
  Built and verified against your actual game files, not just written.

## What you need to do

### 1. Get the official Archipelago app (if you don't have it)

Download the latest Windows release from
[github.com/ArchipelagoMW/Archipelago/releases](https://github.com/ArchipelagoMW/Archipelago/releases)
and install it. This is the real, official multiworld generator/server/client — needed to
actually create and host a game, which isn't something the SUPERHOT mod does on its own.

### 2. Install the apworld

Open the Archipelago Launcher, click **"Install APWorld"**, and pick
`dist/superhot.apworld` from this folder. This copies it into Archipelago's own
`custom_worlds` folder — SUPERHOT should now show up as a supported game in the Launcher.

### 3. Generate a test game

Copy `dist/superhot_michael.yaml` into Archipelago's `Players` folder (inside wherever you
installed Archipelago), then in the Launcher click **"Generate"**. It should complete
without errors and drop an output `.zip` in Archipelago's `output` folder. If this step
fails, stop here and send me the error — it means something about the apworld itself is
broken on your machine specifically (different Archipelago version, etc.), separate from
anything below.

### 4. Host it locally

In the Launcher, click **"Host"** and pick the `.zip` Generate just made. This starts a
local server, by default at `localhost:38281` — you'll see a console window confirm it's
running and waiting for connections.

### 5. Launch SUPERHOT

Run `SUPERHOT.exe` as usual. MelonLoader should print console output on top of the game
(a separate console window) — look for lines starting with `[SuperhotArchipelago]`,
specifically:
- `SuperhotArchipelago loading.`
- `LevelCatalog loaded 32 levels from '...'.`
- Either a connect attempt, or a message saying Server/Slot aren't configured yet.

If you don't see MelonLoader output at all, MelonLoader itself isn't hooking the game —
that's a MelonLoader install problem, not this mod, and worth confirming separately
before going further.

### 6. Configure the connection

**In-game (the easy way):** on the hub's main screen (where you'd also see "LEVELS",
"ENDLESS", etc.), select the **ARCHIPELAGO** icon and press Enter — its status shows
right on the icon (`ONLINE`/`OFFLINE`). This opens a real in-game screen (styled like the
game's own settings/console screens, not a floating window) with SERVER/SLOT/PASSWORD
fields:
- Type into the highlighted field directly.
- **Tab** moves to the next field.
- **Enter** moves to the next field too, or connects if you're on PASSWORD (the last one).
- A `STATUS:` line shows `NOT CONNECTED`, `CONNECTED AS '...'`, or a specific error if
  login fails, right there on screen — no need to watch the console for this part.
- **Esc** closes the screen. Your settings are saved automatically, so you won't need to
  fill them in again next launch.

No mouse needed anywhere in this screen — it's fully keyboard-driven, matching how the
rest of the hub already works.

**Editing the file directly (still works, e.g. for scripting/automation):** the mod reads
server/slot/password from `SUPERHOT/UserData/MelonPreferences.cfg` — **not** a per-mod
file, MelonLoader shares one preferences file across every installed mod, under a
`[SuperhotArchipelago]` section. Close the game, open that file, and edit the section to
look like:
```
[SuperhotArchipelago]
Server = "localhost:38281"
Slot = "Michael"
Password = ""
```
Relaunch, or if the game's still running, just save the file — the mod reconnects
automatically when you edit it, no restart needed.

Either way, you should see `Connecting to 'localhost:38281' as 'Michael'...` in the
MelonLoader console, followed by either `Connected to Archipelago as 'Michael'.` or an
error (also shown live in the connect screen's `STATUS:` line, and reflected in the
ARCHIPELAGO button's `ONLINE`/`OFFLINE` status on the hub itself).

### 7. Play the first level and watch for a check

Once connected, play through the very first level (the intro/"Kick" level) to completion.
Watch the MelonLoader console for a line like `Sent check for '01 - Kick' (level id ...,
location id ...)`. If it appears, also check the AP server console/web client — it should
show that location as checked, and grant an item back. A received item logs a line like
`Unlocked '...' (level id ..., scene '...') from a received item.` — and should also show
up in the hub: the corresponding level's button turns from garbled/grey (locked) to a
legible grey (unlocked, not yet played), turning white once you've completed it. This part
has been playtested for real, repeatedly (see `NOTES.md`) — it's not a guess.

## If something breaks

Send me whatever the MelonLoader console printed (especially any red/error lines) and
which step you got to — that tells me whether it's a Harmony patch not firing, a wrong
field name I got wrong despite the decompile, or something more basic like a missing
dependency. This project has been tested solo, on one Windows machine, one game install —
if you're on a different setup (different game version, different MelonLoader version, a
second real player in the same room), you're in genuinely new territory, and that's
exactly the kind of report that's most useful. `NOTES.md` has the full history of what's
already been found and fixed, and its "Design decisions still open" section lists what's
still known-incomplete (e.g. challenge/endless mode isn't tracked by Archipelago at all).
