"""Confirms Rules.py's stated design actually holds for the current level list.

Rules.py's docstring makes a specific claim: each level's completion location only needs
to state a rule for its own access item, not a chain of every earlier level's item too,
because the region graph is flat (see Regions.py) and there's no other way to reach a
later level's item than being able to play up through it. That's true for a linear game
with one access item per level, but it's exactly the kind of implicit assumption a level
list edit -- reordering, adding, or removing an entry (see the Cyberspace removal in
NOTES.md) -- could silently break without anything in Locations.py/Regions.py/Rules.py
raising an error. These tests exist to catch that.
"""
from __future__ import annotations

from ..Items import LEVELS, level_item_name
from ..Locations import level_location_name, location_table, secret_location_name
from .bases import SuperhotTestBase


class TestLevelAccess(SuperhotTestBase):
    def test_first_level_needs_no_item(self) -> None:
        """Level 1 has no access rule at all (see Items.py's _level_classification) and
        should always be reachable with nothing collected -- including its secret, which
        follows the same no-rule exception (see Rules.py)."""
        first_level = LEVELS[0]
        first_location = level_location_name(first_level)
        self.assertTrue(
            self.can_reach_location(first_location),
            f"{first_location} should be reachable with no items collected",
        )
        if first_level["hasSecret"]:
            first_secret = secret_location_name(first_level)
            self.assertTrue(
                self.can_reach_location(first_secret),
                f"{first_secret} should be reachable with no items collected",
            )

    def test_each_level_needs_only_its_own_item(self) -> None:
        """Walks the full level list (skipping level 1, which has no rule, and the final
        level, which has no completion location at all -- see
        test_final_level_has_no_completion_location/test_victory_needs_final_item) and
        asserts each location -- and its secret location, if it has one, since Rules.py
        deliberately gates both on the same item -- depends on exactly its own access
        item, not the one before or after it."""
        for level in LEVELS[1:-1]:
            with self.subTest(level=level["name"]):
                locations = [level_location_name(level)]
                if level["hasSecret"]:
                    locations.append(secret_location_name(level))
                self.assertAccessDependency(
                    locations,
                    [[level_item_name(level)]],
                )

    def test_final_level_has_no_completion_location(self) -> None:
        """Real, explicit user request: the final level ("34 - Free") must not generate
        its own real, checkable location -- finishing it ends the run, and a real
        fillable item behind "beat the entire game" is bad multiworld design (see
        Locations.py's module docstring). Only the dedicated Victory event location
        should gate on its access item."""
        final_level = LEVELS[-1]
        self.assertFalse(final_level["hasSecret"], "test assumes the final level has no secret to also check")
        self.assertNotIn(
            level_location_name(final_level),
            location_table,
            "the final level must not have its own real completion location",
        )

    def test_victory_needs_final_item(self) -> None:
        """The Victory event location gates on the final level's access requirement (see
        Regions.py/Rules.py) -- beating the game requires being able to play the last
        level, not just holding every other item."""
        final_level = LEVELS[-1]
        self.assertAccessDependency(
            ["Victory"],
            [[level_item_name(final_level)]],
        )

    def test_level_count_matches_catalog(self) -> None:
        """Sanity check on the level list itself: 32 real story levels, no duplicate
        names, sequential order starting at 1. Both Cyberspace entries were removed after
        a playtest confirmed they never get a real hub button (see NOTES.md) -- this would
        fail loudly if a future edit reintroduced a gap or a dead entry like that."""
        self.assertEqual(len(LEVELS), 32, "expected exactly 32 real playable levels")
        names = [level["name"] for level in LEVELS]
        self.assertEqual(len(names), len(set(names)), "level names must be unique")
        orders = [level["order"] for level in LEVELS]
        self.assertEqual(orders, list(range(1, len(LEVELS) + 1)), "order must be sequential starting at 1")

    def test_secret_count_matches_catalog(self) -> None:
        """Sanity check on hasSecret: extracted directly from the real game's data (see
        data/levels.json's _source note) -- 27 of the 32 levels have exactly one secret
        each, the other 5 (Dog1/Dog2/Dog3/Hacker/Free) have none. Every level with
        hasSecret should have a real secret location in location_table, and every one
        without should not."""
        from ..Locations import location_table

        with_secret = [level for level in LEVELS if level["hasSecret"]]
        without_secret = [level for level in LEVELS if not level["hasSecret"]]
        self.assertEqual(len(with_secret), 27, "expected exactly 27 levels with a secret")
        self.assertEqual(len(without_secret), 5, "expected exactly 5 levels without a secret")

        for level in with_secret:
            self.assertIn(secret_location_name(level), location_table)
        for level in without_secret:
            self.assertNotIn(secret_location_name(level), location_table)
