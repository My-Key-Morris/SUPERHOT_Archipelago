"""Player-facing options for the SUPERHOT Archipelago world."""
from __future__ import annotations

from dataclasses import dataclass

from Options import PerGameCommonOptions, Range, Toggle


# A real-time gate the mod enforces during play (not an Archipelago logic requirement),
# so receiving "Level Access: 34 - Free" early can't immediately end the run for everyone.
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


# Fixed set of slow/repetitive or disruptive levels; the mod treats them like "01 - Kick"
# -- always unlocked, no item required (see LocationManager.cs/LevelAccessGuard.cs).
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
