"""Location definitions for the SUPERHOT Archipelago world: one location per story level
completion, plus one more for levels with an in-level secret console.

The final level ("34 - Free") has no completion location of its own -- a real check
behind "beat the entire game" is bad multiworld design if another player's progression
depends on it. It still needs its own access item and still reports goal completion (see
Regions.py's Victory event location and mod/SuperhotArchipelago/Core/LocationManager.cs).
"""
from __future__ import annotations

import json
from typing import NamedTuple

from BaseClasses import Location

from .Items import BASE_ID, LEVELS


class LocationData(NamedTuple):
    code: int
    region: str


def level_location_name(level: dict) -> str:
    return f"{level['name']} Complete"


def secret_location_name(level: dict) -> str:
    return f"{level['name']} Secret"


# Keeps a level-complete location and that level's secret location from colliding. Must
# be kept in sync by hand with mod/SuperhotArchipelago/Core/LevelCatalog.cs's SecretLocationIdOffset.
SECRET_LOCATION_OFFSET = 20000

# Stays the full, unfiltered universe of every possible location regardless of any one
# player's ExcludeSlowLevels choices -- Regions.py's create_regions filters per-player.
location_table: dict[str, LocationData] = {
    level_location_name(level): LocationData(BASE_ID + level["order"], "Menu")
    for level in LEVELS[:-1]  # excludes the final level -- see module docstring
}
location_table.update({
    secret_location_name(level): LocationData(BASE_ID + SECRET_LOCATION_OFFSET + level["order"], "Menu")
    for level in LEVELS
    if level["hasSecret"]
})

location_name_to_id: dict[str, int] = {
    name: data.code for name, data in location_table.items()
}


class SuperhotLocation(Location):
    game = "SUPERHOT"
