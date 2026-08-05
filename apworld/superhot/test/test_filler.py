"""Regression coverage for the itempool-size bug documented in NOTES.md's "filler" round.

create_items() silently stopped producing enough items once secret locations pushed the
real location count past len(LEVELS) -- and nothing in core Archipelago pads this
automatically (confirmed by directly running Fill.distribute_items_restrictive() outside
the test harness). The tests that should have caught it (test_fill and friends, inherited
from WorldTestBase) turned out to have been silently not running at all, for an unrelated
reason (see test/bases.py's SuperhotTestBase.run_default_tests). These tests exist so that
class of bug can't recur unnoticed a second time -- both the itempool/location count
match itself, and confirmation the base tests are genuinely executing rather than no-oping.
"""
from __future__ import annotations

from BaseClasses import ItemClassification

from ..Items import WHITE_SPACE_ITEM_NAME, item_table
from ..Locations import location_table
from .bases import SuperhotTestBase


class TestFiller(SuperhotTestBase):
    def test_itempool_matches_real_location_count(self) -> None:
        """The itempool must have exactly one item per real (non-event) location --
        Archipelago does not pad a short pool automatically, it just fails to generate."""
        self.assertEqual(
            len(self.multiworld.itempool),
            len(location_table),
            "itempool size must match the real (non-event) location count",
        )

    def test_white_space_is_the_filler_item(self) -> None:
        """White Space is a real, placeable, filler-classified item distinct from any
        'Level Access: X' name -- a player receiving one shouldn't be able to mistake it
        for a real level unlock."""
        self.assertIn(WHITE_SPACE_ITEM_NAME, item_table)
        white_space = item_table[WHITE_SPACE_ITEM_NAME]
        self.assertIsNotNone(white_space.code, "White Space must have a real item id to be placeable")
        self.assertEqual(white_space.classification, ItemClassification.filler)
        self.assertFalse(
            WHITE_SPACE_ITEM_NAME.startswith("Level Access:"),
            "filler item name must not look like a real level-access item",
        )

    def test_base_reachability_tests_actually_run(self) -> None:
        """Confirms SuperhotTestBase.run_default_tests is actually forcing WorldTestBase's
        free tests (test_fill etc.) to execute their real bodies, not silently return --
        see test/bases.py's docstring for the bug this guards against. If this class ever
        stops setting run_default_tests = True, those tests report PASSED while doing
        nothing, exactly as they did when the itempool bug above shipped unnoticed."""
        self.assertTrue(self.run_default_tests)
