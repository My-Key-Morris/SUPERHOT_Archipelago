"""Region graph for the SUPERHOT Archipelago world.

v0 keeps this deliberately flat: SUPERHOT's campaign has no explorable space to model as
distinct regions (it's a hub screen launching one linear level after another), so every
location lives in a single "Menu" region and progression is gated entirely by Rules.py
checking for the right "Level Access" items, not by region connections.

This will be worth revisiting if we ever add locations that live logically apart from the
main campaign (e.g. challenge mode, endless mode).

Also creates a single event location, VICTORY_LOCATION_NAME, with no real id (address=
None). Archipelago requires event items (code=None, used for the "Victory" item) to live
on event locations (address=None) -- they can't be placed on a real, checkable location,
since there'd be nothing meaningful to send over the network when that location is
checked. So "beating the game" is modeled as its own logic-only location layered on top
of the real ones, rather than reusing the final level's real location.
"""
from __future__ import annotations

from BaseClasses import Region

from .Locations import location_table, SuperhotLocation

VICTORY_LOCATION_NAME = "Victory"


def create_regions(world) -> None:
    menu = Region("Menu", world.player, world.multiworld)
    world.multiworld.regions.append(menu)

    for name, data in location_table.items():
        menu.locations.append(SuperhotLocation(world.player, name, data.code, menu))

    victory_location = SuperhotLocation(world.player, VICTORY_LOCATION_NAME, None, menu)
    menu.locations.append(victory_location)

    # Single-region layout: nothing else to connect.
