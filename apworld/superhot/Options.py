"""Player-facing options for the SUPERHOT Archipelago world."""
from __future__ import annotations

from dataclasses import dataclass

from Options import PerGameCommonOptions, Range, Toggle


# Real, explicit user request: a real-time gate the mod itself enforces during play,
# not an Archipelago logic requirement (receiving the access item still makes the
# location/goal reachable in logic, same as any other level) -- stops a player who
# happens to receive "Level Access: 34 - Free" early from immediately ending the run
# and cutting everyone else off from checks.
class LevelsRequiredForFree(Range):
    """How many of the other 31 story levels must actually be completed in-game
    before "34 - Free" (the real ending) can be entered, even after its own Level
    Access item has been received. Set to 0 to disable this and let the access item
    alone be enough, same as every other level.
    """
    display_name = "Levels Required For Free"
    range_start = 0
    range_end = 31
    default = 25


# Real, explicit user request: slow/repetitive gameplay (the three Dog levels,
# Longway) or otherwise disruptive to a normal run ("22 - Hacker" forces its own
# native story-ending detour regardless of AP progress -- see Mod.cs's
# OnSceneWasLoaded/StoryFinishedSuppressPatch.cs) relative to the rest of the
# campaign -- a fixed set, one on/off switch rather than a per-level pick list. With
# this on, none of the five get a checkable location (main or secret) or a Level
# Access item -- their pool slots are padded out with more filler instead (see
# __init__.py's create_items). The mod's own gating treats them exactly like
# "01 - Kick": always unlocked, no item required, badges/completion always show done
# -- see mod/SuperhotArchipelago/Core/LocationManager.cs and Core/LevelAccessGuard.cs.
class ExcludeSlowLevels(Toggle):
    """Removes "99 - Dog1", "98 - Dog2", "99 - Dog3", "32 - Longway", and
    "22 - Hacker" from the location and item pools entirely. These levels stay in
    the game, always unlocked and playable -- they're just no longer part of the
    randomizer.
    """
    display_name = "Exclude Slow Levels"


@dataclass
class SuperhotOptions(PerGameCommonOptions):
    levels_required_for_free: LevelsRequiredForFree
    exclude_slow_levels: ExcludeSlowLevels
