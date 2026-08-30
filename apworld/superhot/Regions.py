"""Region graph for the SUPERHOT Archipelago world. Deliberately flat: everything lives in
one "Menu" region since SUPERHOT's campaign is linear, with progression gated by Rules.py
checking "Level Access" items rather than region connections. Also creates a single event
location (VICTORY_LOCATION_NAME, address=None) for the Victory item, since event items must
live on event locations. ExcludeSlowLevels-excluded levels get no SuperhotLocation created
here (location_table itself stays complete, per Archipelago's world-API expectations).
"""
from __future__ import annotations

from BaseClasses import Region

from .Items import LEVELS, is_excluded
from .Locations import level_location_name, location_table, secret_location_name, SuperhotLocation

VICTORY_LOCATION_NAME = "Victory"


def create_regions(world) -> None:
    menu = Region("Menu", world.player, world.multiworld)
    world.multiworld.regions.append(menu)

    for level in LEVELS:
        if is_excluded(level, world.options):
            continue

        if level is not LEVELS[-1]:  # the final level has no completion location at all
            name = level_location_name(level)
            data = location_table[name]
            menu.locations.append(SuperhotLocation(world.player, name, data.code, menu))

        if level["hasSecret"]:
            name = secret_location_name(level)
            data = location_table[name]
            menu.locations.append(SuperhotLocation(world.player, name, data.code, menu))

    victory_location = SuperhotLocation(world.player, VICTORY_LOCATION_NAME, None, menu)
    menu.locations.append(victory_location)

    # Single-region layout: nothing else to connect.
