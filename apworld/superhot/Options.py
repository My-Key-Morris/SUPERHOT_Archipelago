"""Player-facing options for the SUPERHOT Archipelago world."""
from __future__ import annotations

from dataclasses import dataclass

from Options import PerGameCommonOptions, Range, Toggle


class LevelsRequiredForFree(Range):
    """How many of the other 31 story levels have to actually be completed in-game
    before "34 - Free" (the real ending) can be entered -- even after its own Level
    Access item has already been received.

    This isn't an item/logic requirement Archipelago's generator enforces (receiving
    the access item still makes the location/goal reachable in logic, same as any
    other level); it's a real-time gate the mod itself enforces during play, so a
    player who happens to receive "Level Access: 34 - Free" early can't immediately
    end their run and stop sending checks to everyone else. Set to 0 to disable it
    and let the access item alone be enough, same as every other level.
    """
    display_name = "Levels Required For Free"
    range_start = 0
    range_end = 31
    default = 25


class ExcludeSlowLevels(Toggle):
    """Real, explicit user request: excludes "99 - Dog1", "98 - Dog2", "99 - Dog3", and
    "32 - Longway" from the location and item pools entirely -- known for slow, repetitive
    gameplay relative to the rest of the campaign, and requested as a fixed set (this is
    one on/off switch, not a per-level pick list).

    With this on, none of the four get their own checkable location (main or secret) or
    their own Level Access item -- their pool slots are simply padded out with more filler
    instead (see __init__.py's create_items). The mod's own gating treats them exactly
    like "01 - Kick": always unlocked, no item required, and their hub badges/completion
    display always shows them as done -- see mod/SuperhotArchipelago/Core/LocationManager.cs
    and Core/LevelAccessGuard.cs. They stay real, playable levels in the hub, just entirely
    outside the randomizer's own tracking.
    """
    display_name = "Exclude Slow Levels"


@dataclass
class SuperhotOptions(PerGameCommonOptions):
    levels_required_for_free: LevelsRequiredForFree
    exclude_slow_levels: ExcludeSlowLevels
