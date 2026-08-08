"""SUPERHOT world for Archipelago.

See ../../README.md and ../../NOTES.md at the project root for design status and open
questions -- playtested end-to-end against a real SUPERHOT install (see NOTES.md's
testing log), but still solo-tested on one machine/one game version, so treat it as beta.
"""
from __future__ import annotations

from BaseClasses import ItemClassification, Tutorial
from worlds.AutoWorld import WebWorld, World

from .Items import (
    BASE_ID,
    LEVELS,
    WHITE_SPACE_ITEM_NAME,
    SuperhotItem,
    item_name_groups,
    item_name_to_id,
    item_table,
    level_item_name,
)
from .Locations import location_name_to_id, location_table
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
            ["Michael"],
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
        # One item per level, except level 1 ("01 - Kick") -- its location has no access
        # rule (always reachable), so a "Level Access: 01 - Kick" item would be a no-op
        # if received. Real, explicit user request: rather than still create one anyway
        # (the old behavior -- see git history/NOTES.md), level 1 is skipped here and its
        # location's pool slot is covered by the filler padding below instead, one White
        # Space item larger than before. The separate Victory event location created in
        # Regions.py holds the locked Victory item and isn't part of this regular
        # pool/fill count.
        for level in LEVELS:
            if level["order"] == 1:
                continue
            self.multiworld.itempool.append(self.create_item(level_item_name(level)))

        # Real bug found by a direct question about how this world handles filler,
        # confirmed by actually running Fill.distribute_items_restrictive() outside the
        # test harness: len(location_table) (32 level-complete + 27 secret locations,
        # after the "secrets" feature added a second location per level) has always been
        # bigger than len(LEVELS) -- and unlike what a first read of Fill.py suggests,
        # nothing in core Archipelago automatically pads a world's itempool up to match
        # its location count. distribute_items_restrictive() just raises FillError
        # ("Unable to fill all locations") if items run short; the one create_filler()
        # call in Main.py is for a specific, unrelated feature (replacing items removed
        # by start_inventory_from_pool) and doesn't change the pool's total size. Every
        # world is expected to pad its own pool in create_items() -- this was silently
        # missing here since the location count changed but this loop was never revisited
        # to match. Fixed by explicitly filling the remainder with create_filler() calls
        # (see get_filler_item_name below for what that actually returns). The "- 1" here
        # accounts for level 1's skipped item above -- one more filler needed than levels
        # actually get their own item now.
        filler_needed = len(location_table) - (len(LEVELS) - 1)
        for _ in range(filler_needed):
            self.multiworld.itempool.append(self.create_filler())

    def get_filler_item_name(self) -> str:
        # Real, explicit user request: don't reuse "Level Access: X" as filler -- a
        # player receiving several of those in their item log would reasonably read them
        # as real level unlocks, not padding. WHITE_SPACE_ITEM_NAME (see Items.py) is a
        # single, distinct, on-brand filler item instead. Overriding this (rather than
        # relying on the World base class's own default, which does
        # random.choice(item_name_to_id) -- i.e. could return a real level item too, plus
        # logs a "generating a filler item without custom filler pool" warning every
        # time) makes this explicit and warning-free.
        return WHITE_SPACE_ITEM_NAME

    def set_rules(self) -> None:
        set_rules(self)

        # Victory is an event item locked on the dedicated event location created in
        # Regions.py (address=None) -- it can't live on a real, checkable location, since
        # the final level doesn't have one anymore (see Locations.py docstring for why).
        victory_location = self.multiworld.get_location(VICTORY_LOCATION_NAME, self.player)
        victory_location.place_locked_item(
            SuperhotItem("Victory", ItemClassification.progression, None, self.player)
        )

    def fill_slot_data(self) -> dict:
        # The only real per-player setting this world has so far. Not something logic
        # itself needs to know about (see Options.py's LevelsRequiredForFree docstring
        # for why) -- it's read purely by the mod, at connect time, to enforce its own
        # real-time gate on "34 - Free" (mod/SuperhotArchipelago/Core/LevelAccessGuard.cs).
        # Riding along in slot data, rather than a second place to configure it, is what
        # keeps the YAML the single source of truth for this number.
        return {
            "levels_required_for_free": self.options.levels_required_for_free.value,
        }
