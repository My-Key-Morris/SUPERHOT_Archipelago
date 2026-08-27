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

Real, explicit user request: Options.py's ExcludeSlowLevels toggle removes a level's real
location(s) from THIS player's region entirely -- location_table/location_name_to_id (see
Locations.py) stay complete and unchanged regardless (Archipelago's world API expects those
class-level name/id tables to represent the full universe of possible locations across any
settings, not just this player's own choices -- other worlds' own logic never needs to know
which of ours actually got created), but create_regions only ever adds a SuperhotLocation
for a level here if is_excluded() says no. This is why the loop below iterates LEVELS
directly (the same pattern Rules.py's own loop already uses) rather than location_table --
LEVELS carries the exclusion-relevant "name" field.
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
