"""Access rules for the SUPERHOT Archipelago world: level N's completion (and secret)
location requires having received its own "Level Access" item; level 1 has no item and no
rule. The final level has no completion location of its own (only the Victory event
location gates on its access item) so the main loop skips it. levels_required_for_free is
deliberately not an access rule -- it's a real-time gate the mod enforces during play (see
LevelAccessGuard.cs), not something the generator's reachability logic needs to know
about. Excluded levels (ExcludeSlowLevels) are skipped the same way level 1 is, since they
have no location to set a rule on.
"""
from __future__ import annotations

from worlds.generic.Rules import set_rule

from .Items import LEVELS, is_excluded, level_item_name
from .Locations import level_location_name, secret_location_name
from .Regions import VICTORY_LOCATION_NAME


def set_rules(world) -> None:
    multiworld = world.multiworld
    player = world.player

    for level in LEVELS:
        if level["order"] == 1:
            continue
        if is_excluded(level, world.options):
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
