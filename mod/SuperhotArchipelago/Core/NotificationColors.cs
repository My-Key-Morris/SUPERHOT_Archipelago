using Archipelago.MultiClient.Net.Enums;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Colors notification text similar to the official Archipelago text client, using an
    /// item's real ItemFlags classification (progression/useful/trap/filler) rather than
    /// its game of origin, since a multiworld item can belong to any game. SHGUI's native
    /// sColors table only has three saturated hues (red/green/blue) plus grayscale, so
    /// player names use a light gray to leave all three hues free for item classification.
    /// Shared by LocationManager (check-sent) and ItemManager (item-received) so both use
    /// the same palette.
    /// </summary>
    public static class NotificationColors
    {
        // Light gray, deliberately far from FillerItem's mid gray below so the two read
        // as distinct at a glance despite both being grayscale.
        public const char Player = 'D';
        public const char ProgressionItem = 'g';
        public const char UsefulItem = 'b';
        public const char TrapItem = 'r';
        public const char FillerItem = 'z';
        public const char Default = 'w';

        /// <summary>
        /// Trap is checked first since ItemFlags is a bitmask and a trap could also carry
        /// other flags, but trap is the property a player most needs a heads-up about.
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
