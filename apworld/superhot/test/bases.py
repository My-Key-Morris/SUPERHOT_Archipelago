"""Shared base class for SUPERHOT's Archipelago unit tests.

See docs/tests.md in the main Archipelago repo for what WorldTestBase gives every subclass
for free: test_all_state_can_reach_everything, test_empty_state_can_reach_something, and
test_fill. Everything in test_*.py in this package builds on top of that with
SUPERHOT-specific checks.

run_default_tests = True is NOT optional here, and was a real bug the first time this file
was written: WorldTestBase.run_default_tests (see its own definition in test/bases.py) is
a property that's False by default for any subclass that doesn't set a non-empty `options`
dict or override setUp/world_setup -- its docstring says this is "not possible or
identical to the base test that's always being run already" (test/general/ runs every
registered world once with default options as part of Archipelago's own core test suite).
Since this project's classes never set custom options, that property silently evaluated to
False, and test_fill/test_all_state_can_reach_everything/test_empty_state_can_reach_something
all start with "if not (self.run_default_tests and self.constructed): return" -- so they
were reporting PASSED while actually never running their bodies at all. That's exactly how
a real, later-discovered create_items() bug (see NOTES.md's "secrets" round -- the item
pool stopped matching the location count once secret locations were added, which
test_fill's own assertions exist specifically to catch) went undetected until someone
asked how filler was being handled and it got checked by hand. Forcing this to True here
means every subclass actually runs these checks for real, at the cost of a small amount of
redundant CPU time against what test/general/ already covers -- worth it for a project this
size, where "let someone read the test file and go verify it inspecting Fill.py directly"
should not be the only way that gets caught.
"""
from __future__ import annotations

from test.bases import WorldTestBase


class SuperhotTestBase(WorldTestBase):
    game = "SUPERHOT"
    run_default_tests = True
