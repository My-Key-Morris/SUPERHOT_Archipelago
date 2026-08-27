"""Location definitions for the SUPERHOT Archipelago world.

v0: one location per story level, checked on level completion, plus one more for levels
that have an in-level secret console (see data/levels.json's "hasSecret" and NOTES.md's
"secrets" section for how that was extracted and confirmed against the real game). See
Items.py for the matching base_id and NOTES.md for why the level list itself is still a
placeholder.

The final level (LEVELS[-1], "34 - Free") is the one exception: it has no completion
location of its own. Real, explicit user request -- finishing the final level ends the
run, and a real, regular fillable item sitting behind "beat the entire game" is bad
multiworld design if another player's own progression happens to depend on it (they'd be
stuck waiting on this player's full campaign clear). The dedicated Victory event location
(see Regions.py) already exists specifically to signal completion without holding a real
item -- see its own docstring for why that has to be a separate, address=None location
rather than reusing a real one. So the final level still requires its own access item to reach and play (see Rules.py),
and still reports goal completion when finished (the mod side needed a matching change --
see NOTES.md and mod/SuperhotArchipelago/Core/LocationManager.cs), it just doesn't have a
"34 - Free Complete" location generating a real check anymore.
"""
from __future__ import annotations

import json
from pathlib import Path
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


# Locations and items are already separate id namespaces (see Items.py's ITEM_ID_OFFSET
# comment for why distinct ranges are used anyway to avoid confusion) -- this is a further
# offset within the location namespace itself, so a level-complete location and that same
# level's secret location can never collide even though both are BASE_ID + a per-level
# offset. Must be kept in sync by hand with
# mod/SuperhotArchipelago/Core/LevelCatalog.cs's SecretLocationIdOffset.
SECRET_LOCATION_OFFSET = 20000

# Real, explicit user request: Options.py's ExcludeSlowLevels toggle removes some levels'
# locations from a given player's own world (see Regions.py's create_regions) -- but this
# table itself deliberately stays the full, unfiltered universe of every location this
# game could ever have, regardless of any one player's options. That's the documented
# Archipelago world API convention for class-level name/id tables (location_name_to_id
# below is built straight from this), not something exclusion should ever shrink.
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
