"""Player-facing options for the SUPERHOT Archipelago world.

Empty for now -- v0 has no meaningful variation to expose (no death link equivalent, no
difficulty toggle implemented yet). Kept as a stub so __init__.py has something real to
import and so adding options later doesn't require restructuring.
"""
from __future__ import annotations

from dataclasses import dataclass

from Options import PerGameCommonOptions


@dataclass
class SuperhotOptions(PerGameCommonOptions):
    pass
