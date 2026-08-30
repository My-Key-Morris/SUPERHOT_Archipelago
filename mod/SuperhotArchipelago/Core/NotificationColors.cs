using Archipelago.MultiClient.Net.Enums;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Real, explicit user request: "See if we can color check received similar to
    /// [the official Archipelago] text client" -- that client colors player names and
    /// items differently within one line, an item's color driven by its real
    /// Archipelago classification (progression/useful/trap/filler), not by what game it
    /// belongs to. Confirmed via decompile that SUPERHOT's own native text renderer
    /// (SHGUI's sColors table) only defines a handful of colors -- red, green, blue,
    /// white, black, a grayscale ramp, and transparent -- nothing like the text client's
    /// own plum/slateblue/salmon/cyan palette, so this is the closest approximation
    /// actually available in-game, not a literal port of that client's exact colors.
    ///
    /// Real constraint hit by a follow-up request ("there is also a 'useful' item flag,
    /// make normal gray and useful a different color"): the engine only has three real
    /// saturated hues at all -- red, green, blue -- everything else in `sColors` is a
    /// black-to-white grayscale ramp. With trap=red and progression=green already taken,
    /// there was exactly one hue left (blue) for a fourth distinct item color, and it was
    /// already spent on player names. Rather than let two item classifications collide on
    /// the same color, player names moved to a light gray (`'D'`, brighter than the
    /// filler gray below) and blue was freed up for "useful" -- so all three real hues
    /// are now reserved entirely for item classification (the thing this request is
    /// actually about), and player names/filler items both stay in the grayscale family
    /// but far enough apart in brightness to still read as clearly different at a glance:
    ///
    /// - Progression items (`ItemFlags.Advancement` set -- every real "Level Access: X"
    ///   item, plus "Victory"): green.
    /// - Useful items (`ItemFlags.NeverExclude` set, with no `Advancement`/`Trap`):
    ///   blue. This world doesn't define any itself, but a received/scouted item can
    ///   belong to any game in the multiworld, and some of those do.
    /// - Trap items (`ItemFlags.Trap` set): red. Same reasoning as useful -- none of
    ///   this world's own items, but real for other games' items passing through here.
    /// - Normal/filler items (`ItemFlags.None` -- this world's own filler item, "White
    ///   Space", included): gray -- reads as "least important", matching how the
    ///   client's own filler/junk color reads relative to progression.
    /// - Player names: a lighter gray, distinct from the filler gray above.
    /// - Connective words (like "Sent"/"to"/"from") and location names: left at the
    ///   default white every other line in this mod already uses.
    ///
    /// Real bug found by an earlier follow-up report: an earlier version of this keyed
    /// item color off `itemId == LevelCatalog.WhiteSpaceItemId` -- correct only by
    /// accident for this world's own two item kinds (progression / filler), and wrong in
    /// general, since a scouted or received item in a multiworld can belong to a
    /// completely different game with its own trap/useful items that id check would
    /// never recognize. Reading the real `ItemFlags` Archipelago itself already reports
    /// on every `ItemInfo`/`ScoutedItemInfo` (confirmed via the
    /// Archipelago.MultiClient.Net.dll source docs) is correct for any item from any
    /// game, not just this world's own two kinds.
    ///
    /// Shared by Core/LocationManager.cs (check-sent notifications) and
    /// Core/ItemManager.cs (item-received notifications) so both build the exact same
    /// palette rather than each picking its own.
    /// </summary>
    public static class NotificationColors
    {
        // Light gray (SHGUI's sColors[68] == 0.806 brightness) -- deliberately far from
        // FillerItem's mid gray (0.53) below so the two are still easy to tell apart at
        // a glance, even though both are technically "grayscale, not a real hue."
        public const char Player = 'D';
        public const char ProgressionItem = 'g';
        public const char UsefulItem = 'b';
        public const char TrapItem = 'r';
        public const char FillerItem = 'z';
        public const char Default = 'w';

        /// <summary>
        /// Trap checked first: a trap item that also happens to be flagged
        /// NeverExclude/Advancement (unusual, but the enum is a [Flags] bitmask, so
        /// nothing stops a world from doing it) should still read as a trap -- the
        /// property a player most needs a heads-up about. Advancement checked next for
        /// the normal progression case, then NeverExclude for "useful." Anything left
        /// (just `ItemFlags.None`) falls to the same gray this world's own filler item
        /// already used.
        /// </summary>
        public static char ForItemFlags(ItemFlags flags)
        {
            if (flags.HasFlag(ItemFlags.Trap))
            {
                return TrapItem;
            }

            if (flags.HasFlag(ItemFlags.Advancement))
            {
                return ProgressionItem;
            }

            if (flags.HasFlag(ItemFlags.NeverExclude))
            {
                return UsefulItem;
            }

            return FillerItem;
        }
    }
}
