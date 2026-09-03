"""SUPERHOT world for Archipelago.

See ../../README.md at the project root for design status and open questions --
playtested end-to-end against a real SUPERHOT install, but still solo-tested on one
machine/one game version, so treat it as beta.
"""
from __future__ import annotations

from BaseClasses import ItemClassification, Tutorial
from worlds.AutoWorld import WebWorld, World

from .Items import (
    BASE_ID,
    LEVELS,
    WHITE_SPACE_ITEM_NAME,
    SuperhotItem,
    is_excluded,
    item_name_groups,
    item_name_to_id,
    item_table,
    level_item_name,
)
from .Locations import location_name_to_id
from .Options import SuperhotOptions
from .Regions import VICTORY_LOCATION_NAME, create_regions
from .Rules import set_rules


class SuperhotWeb(WebWorld):
    theme = "partyTime"
    tutorials = [
        Tutorial(
            "Multiworld Setup Guide",
            "A guide to setting up SUPERHOT for Archipelago multiworld games.",
            "English",
            "setup_en.md",
            "setup/en",
            ["My-Key-Morris"],
        )
    ]


class SuperhotWorld(World):
    """SUPERHOT is a first-person shooter where time only moves when you do. This
    integration randomizes access to the game's 32 story levels."""

    game = "SUPERHOT"
    web = SuperhotWeb()
    options_dataclass = SuperhotOptions
    options: SuperhotOptions

    item_name_to_id = item_name_to_id
    location_name_to_id = location_name_to_id
    item_name_groups = item_name_groups

    def create_regions(self) -> None:
        create_regions(self)

    def create_item(self, name: str) -> SuperhotItem:
        data = item_table[name]
        classification = data.classification
        code = data.code
        return SuperhotItem(name, classification, code, self.player)

    def create_items(self) -> None:
        # No item for level 1 (always reachable, so an access item would be a no-op) or
        # any ExcludeSlowLevels-excluded level (its location doesn't exist this generation).
        # real_item_count tracks how many were actually created so the filler math below
        # self-balances regardless of exclusions.
        real_item_count = 0
        for level in LEVELS:
            if level["order"] == 1:
                continue
            if is_excluded(level, self.options):
                continue
            self.multiworld.itempool.append(self.create_item(level_item_name(level)))
            real_item_count += 1

        # Archipelago doesn't auto-pad a world's itempool to match its location count --
        # each world must fill the remainder itself via create_filler(), or generation
        # fails with "Unable to fill all locations". real_location_count mirrors
        # Regions.py's create_regions loop (not the static location_table, which ignores
        # this player's own exclusions).
        real_location_count = sum(
            (0 if level is LEVELS[-1] else 1) + (1 if level["hasSecret"] else 0)
            for level in LEVELS
            if not is_excluded(level, self.options)
        )
        filler_needed = real_location_count - real_item_count
        for _ in range(filler_needed):
            self.multiworld.itempool.append(self.create_filler())

    def get_filler_item_name(self) -> str:
        # Dedicated filler item instead of the base class default (random.choice over all
        # items, which could hand out a real level item as "filler" and logs a warning).
        return WHITE_SPACE_ITEM_NAME

    def set_rules(self) -> None:
        set_rules(self)

        # Victory is a locked event item on Regions.py's dedicated event location --
        # the final level no longer has a real checkable location of its own.
        victory_location = self.multiworld.get_location(VICTORY_LOCATION_NAME, self.player)
        victory_location.place_locked_item(
            SuperhotItem("Victory", ItemClassification.progression, None, self.player)
        )

    def fill_slot_data(self) -> dict:
        # The mod reads both of these at connect time: levels_required_for_free for its
        # "34 - Free" real-time gate, excluded_level_orders (ExcludeSlowLevels) so it knows
        # which levels have no real location/item this generation.
        return {
            "levels_required_for_free": self.options.levels_required_for_free.value,
            "excluded_level_orders": sorted(
                level["order"] for level in LEVELS if is_excluded(level, self.options)
            ),
        }
