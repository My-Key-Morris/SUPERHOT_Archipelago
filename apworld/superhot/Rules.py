"""Access rules for the SUPERHOT Archipelago world.

v0 logic: level N's completion location requires having received the "Level Access" item
for level N (and, transitively, for every level before it, since the in-game hub only lets
you launch levels in order). We only need to state the direct requirement per location --
Archipelago's fill algorithm doesn't require earlier levels to also be listed as long as
every level's access item is itself only reachable in order, which in a fully linear game
with a single access item per level is automatically true (there's no other way to reach
level N's location than being able to play up through level N).

Level 1 is the one exception: it's reachable from the start of the game, has no access
item (see Items.py), and so gets no rule here -- it's always in logic.

Secret locations (see Locations.py) use the exact same rule as their level's own
completion location -- in vanilla, a secret console is something you find *during* a
normal playthrough of the level, not something that requires having already finished it,
so gating on "can play this level" rather than "has completed this level" matches how the
game actually works. Level 1's secret (it has one -- see data/levels.json) follows the
same no-rule exception as level 1's own location, for the same reason.

The final level (LEVELS[-1], "34 - Free") has no completion location of its own anymore
(see Locations.py's module docstring) -- only the Victory event location gates on its
access item, so the main loop below skips it entirely rather than trying to fetch a
location that no longer exists. It also has no secret (see data/levels.json), so there's
nothing else to skip it for.
"""
from __future__ import annotations

from worlds.generic.Rules import set_rule

from .Items import LEVELS, level_item_name
from .Locations import level_location_name, secret_location_name
from .Regions import VICTORY_LOCATION_NAME


def set_rules(world) -> None:
    multiworld = world.multiworld
    player = world.player

    for level in LEVELS:
        if level["order"] == 1:
            continue

        item_name = level_item_name(level)
        rule = lambda state, item_name=item_name: state.has(item_name, player)

        if level is not LEVELS[-1]:
            location = multiworld.get_location(level_location_name(level), player)
            set_rule(location, rule)

        if level["hasSecret"]:
            secret_location = multiworld.get_location(secret_location_name(level), player)
            set_rule(secret_location, rule)

    # The Victory event location shares the final level's access requirement --
    # completing it in-game requires being able to reach/play that level.
    final_item_name = level_item_name(LEVELS[-1])
    victory_location = multiworld.get_location(VICTORY_LOCATION_NAME, player)
    set_rule(victory_location, lambda state: state.has(final_item_name, player))

    multiworld.completion_condition[player] = lambda state: state.has("Victory", player)
