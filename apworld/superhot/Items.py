"""Item definitions for the SUPERHOT Archipelago world: one "Level Access" item per story
level, a Victory event item, and a single filler item ("White Space") padding the pool
out to match the real location count.
"""
from __future__ import annotations

import json
import pkgutil
from typing import NamedTuple, Optional

from BaseClasses import Item, ItemClassification

# Arbitrary base -- item/location ids only need to be unique within this world's own
# tables, not globally, but offsetting from a large base is common convention anyway.
BASE_ID = 3891000

# Items and locations are separate id namespaces; this offset just avoids confusion (e.g.
# in logs/spoilers) between an item and a location that happen to share a numeric code.
ITEM_ID_OFFSET = 10000

# pkgutil.get_data (not pathlib) is required here since a packaged .apworld is a zip
# imported via zipimport, with no real filesystem directory to open a path against.
_raw_levels_json = pkgutil.get_data(__name__, "data/levels.json")
assert _raw_levels_json is not None, "data/levels.json missing from the superhot world package"
LEVELS = json.loads(_raw_levels_json)["levels"]

# ExcludeSlowLevels' fixed set of slow/disruptive levels, matched by levels.json's "name".
SLOW_LEVEL_NAMES = {"99 - Dog1", "98 - Dog2", "99 - Dog3", "32 - Longway", "22 - Hacker"}


def is_excluded(level: dict, options) -> bool:
    """Whether ExcludeSlowLevels removes this level from the location/item pools.
    `options` is duck-typed to avoid Items.py (the lowest-level module) depending on
    Options.py.
    """
    return bool(options.exclude_slow_levels.value) and level["name"] in SLOW_LEVEL_NAMES


class ItemData(NamedTuple):
    code: Optional[int]
    classification: ItemClassification


def level_item_name(level: dict) -> str:
    return f"Level Access: {level['name']}"


# Level 1 has no access rule (always reachable), so it gets no item at all -- its pool
# slot is filled by an extra White Space filler instead (see __init__.py's create_items).
item_table: dict[str, ItemData] = {
    level_item_name(level): ItemData(
        BASE_ID + ITEM_ID_OFFSET + level["order"], ItemClassification.progression
    )
    for level in LEVELS
    if level["order"] != 1
}

# Event item signalling the game has been beaten (final level completed).
item_table["Victory"] = ItemData(None, ItemClassification.progression)

# The pool's one filler item, named for SUPERHOT's own white-void aesthetic rather than
# "Level Access: X" so it can't be mistaken for a real unlock. Id offset (100) stays well
# clear of real level orders (1-32). MUST be kept in sync by hand with
# mod/SuperhotArchipelago/Core/LevelCatalog.cs's WhiteSpaceItemId.
WHITE_SPACE_ITEM_NAME = "White Space"
WHITE_SPACE_ITEM_ID_OFFSET = 100
item_table[WHITE_SPACE_ITEM_NAME] = ItemData(
    BASE_ID + ITEM_ID_OFFSET + WHITE_SPACE_ITEM_ID_OFFSET, ItemClassification.filler
)

item_name_to_id: dict[str, int] = {
    name: data.code for name, data in item_table.items() if data.code is not None
}

item_name_groups = {
    "Levels": {name for name in item_table if name.startswith("Level Access:")},
}


class SuperhotItem(Item):
    game = "SUPERHOT"
