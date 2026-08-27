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
        #
        # Real, explicit user request (ExcludeSlowLevels, see Options.py): a level this
        # option excludes gets no item either, for the same reason level 1 doesn't -- its
        # location doesn't exist for this player (see Regions.py), so an access item for
        # it would be as much of a no-op as level 1's own always-would-be. real_item_count
        # below tracks exactly how many real items this loop actually appended (not
        # len(LEVELS) - 1, which would be wrong once any are excluded), so the filler math
        # after it self-balances regardless of how many levels this player's own options
        # excluded.
        real_item_count = 0
        for level in LEVELS:
            if level["order"] == 1:
                continue
            if is_excluded(level, self.options):
                continue
            self.multiworld.itempool.append(self.create_item(level_item_name(level)))
            real_item_count += 1

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
        # (see get_filler_item_name below for what that actually returns).
        #
        # real_location_count mirrors Regions.py's own create_regions loop exactly (same
        # "skip the final level's own completion, skip anything is_excluded() flags, add
        # one more for hasSecret" shape) rather than reading the static len(location_table)
        # -- that table intentionally stays the full, unfiltered universe regardless of
        # this player's own options (see Regions.py's own docstring), so it can't be used
        # here once exclusion makes the real, per-player count sometimes smaller.
        real_location_count = sum(
            (0 if level is LEVELS[-1] else 1) + (1 if level["hasSecret"] else 0)
            for level in LEVELS
            if not is_excluded(level, self.options)
        )
        filler_needed = real_location_count - real_item_count
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
        # levels_required_for_free: not something logic itself needs to know about (see
        # Options.py's LevelsRequiredForFree docstring for why) -- it's read purely by the
        # mod, at connect time, to enforce its own real-time gate on "34 - Free"
        # (mod/SuperhotArchipelago/Core/LevelAccessGuard.cs). Riding along in slot data,
        # rather than a second place to configure it, is what keeps the YAML the single
        # source of truth for this number.
        #
        # excluded_level_orders: real, explicit user request (ExcludeSlowLevels, see
        # Options.py) -- the mod needs to know which levels this player's own options
        # excluded so it can treat them as always-unlocked/always-complete and never try
        # to send a check for a location that doesn't exist in this generation
        # (mod/SuperhotArchipelago/Core/LocationManager.cs/LevelAccessGuard.cs). Sent as a
        # plain list of "order" values (LevelEntry.Order on the mod side) rather than a
        # single "exclude_slow_levels" bool plus a hardcoded name list on both sides --
        # this way only Items.py's SLOW_LEVEL_NAMES ever needs to change if the exact set
        # of "slow" levels is ever revisited later; the mod never needs its own copy of
        # that list to go out of sync with.
        return {
            "levels_required_for_free": self.options.levels_required_for_free.value,
            "excluded_level_orders": sorted(
                level["order"] for level in LEVELS if is_excluded(level, self.options)
            ),
        }
