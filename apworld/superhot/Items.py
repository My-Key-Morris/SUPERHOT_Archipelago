"""Item definitions for the SUPERHOT Archipelago world.

v0 design: one progressive "Level Access" item per story level (see data/levels.json),
plus a single Victory event item on the final level, plus a single filler item ("White
Space", see below) used to pad the pool out to match the real location count (see
__init__.py's create_items -- location count grew past item count once secret locations
were added, and Archipelago doesn't pad this automatically). There is no persistent
inventory in vanilla SUPERHOT, so there's no natural "junk" item pool to draw real content
from yet -- once mod-side research confirms what's moddable in-level (e.g. weapon crate
contents), this will likely grow into real, distinct filler items.
"""
from __future__ import annotations

import json
import pkgutil
from typing import NamedTuple, Optional

from BaseClasses import Item, ItemClassification

# NOTE: this base_id is a placeholder. A real submission to the Archipelago project needs
# a reserved, collision-free id range assigned by the maintainers.
BASE_ID = 3891000

# Items and locations are separate id namespaces in Archipelago -- a location and an item
# are allowed to numerically share a code without colliding -- but giving them distinct
# ranges avoids any confusion (both on the mod side, which needs to map codes back to
# levels, and for anyone reading logs/spoilers) about which "3891005" is being discussed.
# Locations.py uses BASE_ID directly; items use this offset range instead.
ITEM_ID_OFFSET = 10000

# pkgutil.get_data (not plain pathlib file I/O) is required here: when this world is
# distributed as a packaged .apworld, it's a zip file that gets imported via zipimport,
# and there's no real filesystem directory for Path(__file__).parent to point at --
# "NotADirectoryError: [Errno 20] Not a directory" trying to open a path through the zip.
# get_data() goes through the same import machinery Python used to load this module in
# the first place, so it works identically whether superhot/ is a loose folder or the
# inside of a zip. Confirmed by actually building this world with the Launcher's "Build
# APWorlds" component and generating from the resulting .apworld -- pathlib failed there,
# get_data() doesn't.
_raw_levels_json = pkgutil.get_data(__name__, "data/levels.json")
assert _raw_levels_json is not None, "data/levels.json missing from the superhot world package"
LEVELS = json.loads(_raw_levels_json)["levels"]


class ItemData(NamedTuple):
    code: Optional[int]
    classification: ItemClassification


def level_item_name(level: dict) -> str:
    return f"Level Access: {level['name']}"


def _level_classification(level: dict) -> ItemClassification:
    # Level 1 is always reachable (see Rules.py -- its location has no access rule), so
    # its item doesn't gate anything. It still needs to exist and occupy a slot in the
    # pool (one real item per real location, see __init__.py create_items), just not as
    # a progression item -- "useful" flavor fits better than plain filler since it's
    # still thematically a level-unlock, even a redundant one.
    if level["order"] == 1:
        return ItemClassification.useful
    return ItemClassification.progression


item_table: dict[str, ItemData] = {
    level_item_name(level): ItemData(
        BASE_ID + ITEM_ID_OFFSET + level["order"], _level_classification(level)
    )
    for level in LEVELS
}

# Event item signalling the game has been beaten (final level completed).
item_table["Victory"] = ItemData(None, ItemClassification.progression)

# The pool's filler item -- see the module docstring for why this exists and NOTES.md's
# "filler" round for the bug that made it necessary. Named for SUPERHOT's own aesthetic
# (every level is a stark white void the player fights through -- see the game's own
# loading/menu screens) rather than "Level Access: X", so a player receiving several of
# these doesn't mistake them for real level unlocks in their receive log. Id offset (100)
# is well clear of the real level orders (1-32, and not expected to grow anywhere near
# that high), so it can never collide with a real level's item id even if more levels are
# added later. MUST be kept in sync by hand with
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
