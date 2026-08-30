"""Coverage for Options.py's ExcludeSlowLevels toggle.

Real, explicit user request: "I want a yaml setting to exclude levels with boring/slow
gameplay (dog 1-3 and longway)", later followed up with "boring levels add 22 - hacker" to
add a fifth. Two things need checking: with the option off, nothing about the world changes
from before this feature existed (every slow level still has its usual location(s)/item,
exactly as if the option didn't exist at all); with it on, all five are gone from both
pools entirely, and the item/location counts still balance.
"""
from __future__ import annotations

from ..Items import LEVELS, SLOW_LEVEL_NAMES, level_item_name
from ..Locations import level_location_name, secret_location_name
from .bases import SuperhotTestBase


def _slow_levels() -> list[dict]:
    slow = [level for level in LEVELS if level["name"] in SLOW_LEVEL_NAMES]
    assert len(slow) == 5, "expected exactly the five levels ExcludeSlowLevels documents"
    return slow


class TestExclusionOff(SuperhotTestBase):
    """Default options (exclude_slow_levels off) -- every slow level still has its usual
    location(s) and item, unaffected by this option existing at all."""

    def test_slow_levels_still_present_by_default(self) -> None:
        for level in _slow_levels():
            with self.subTest(level=level["name"]):
                # Raises KeyError if the location wasn't created for this player -- the
                # simplest direct "does this exist at all" check, distinct from
                # reachability (which secret/whether-it's-in-logic tests already cover).
                self.multiworld.get_location(level_location_name(level), self.player)
                if level["hasSecret"]:
                    self.multiworld.get_location(secret_location_name(level), self.player)

        item_names = {item.name for item in self.multiworld.itempool}
        for level in _slow_levels():
            self.assertIn(level_item_name(level), item_names)


class TestExclusionOn(SuperhotTestBase):
    """exclude_slow_levels on -- all five are gone from both pools, and the item/location
    counts still balance (Fill would otherwise raise FillError -- see test_filler.py's own
    docstring for why nothing pads this automatically)."""

    options = {"exclude_slow_levels": True}

    def test_slow_levels_have_no_location(self) -> None:
        for level in _slow_levels():
            with self.subTest(level=level["name"]):
                with self.assertRaises(KeyError):
                    self.multiworld.get_location(level_location_name(level), self.player)
                if level["hasSecret"]:
                    with self.assertRaises(KeyError):
                        self.multiworld.get_location(secret_location_name(level), self.player)

    def test_slow_levels_have_no_item(self) -> None:
        item_names = {item.name for item in self.multiworld.itempool}
        for level in _slow_levels():
            self.assertNotIn(level_item_name(level), item_names)

    def test_itempool_still_matches_real_location_count(self) -> None:
        """Same invariant test_filler.py checks for the default case -- must still hold
        once exclusion shrinks both pools, or generation would fail with FillError."""
        real_location_count = sum(
            1 for _ in self.multiworld.get_locations(self.player)
        ) - 1  # -1 for the Victory event location, which isn't part of the real pool
        self.assertEqual(len(self.multiworld.itempool), real_location_count)

    def test_other_levels_unaffected(self) -> None:
        """Excluding the five slow levels shouldn't touch any other level's own
        location/item -- a real, explicit implicit expectation worth asserting directly
        rather than just inferring it from the count-based tests above."""
        for level in LEVELS:
            if level["name"] in SLOW_LEVEL_NAMES or level is LEVELS[0]:
                continue
            with self.subTest(level=level["name"]):
                if level is not LEVELS[-1]:
                    self.multiworld.get_location(level_location_name(level), self.player)
                if level["hasSecret"]:
                    self.multiworld.get_location(secret_location_name(level), self.player)
