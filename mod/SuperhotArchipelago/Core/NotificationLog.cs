using System.Collections.Generic;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// One colored run of text within a single notification log line, since a line needs its item
    /// name, player name, and connective words each in their own color. See NotificationColors.cs.
    /// </summary>
    public readonly struct LogSegment
    {
        public readonly string Text;
        public readonly char Color;

        public LogSegment(string text, char color)
        {
            Text = text;
            Color = color;
        }
    }

    /// <summary>
    /// Shared sink for the notification popup/log feature: ItemManager.cs and LocationManager.cs call
    /// Add() when something notification-worthy happens (items received and the player's own checks
    /// sent, not other players' checks); ArchipelagoLogApp.cs renders Entries as the hub log. The list
    /// is in-memory only, but Connect() clears it and both managers rebuild it from server state on
    /// every connect, so it always reflects this slot's full history rather than just this session's.
    /// </summary>
    public static class NotificationLog
    {
        // Defensive cap only -- a maximally-played run has at most ~116 entries, so this shouldn't trigger.
        private const int MaxEntries = 200;

        // Each entry is an array of colored segments (see LogSegment) rather than a plain string, so
        // ArchipelagoLogApp.cs can render item/player names in their own colors.
        private static readonly List<LogSegment[]> _entries = new();

        // ItemManager's resync is synchronous while LocationManager's is an async network round-trip, so
        // without an explicit sort key, historical "Sent"/"Received" entries land in two separate blocks
        // instead of interleaved by location. Each entry carries an orderKey (a shared Archipelago
        // location id for historical entries, long.MaxValue for live ones) so they sort correctly.
        private static readonly List<long> _entryOrderKeys = new();

        // TextManager's native uptitle queue gives each entry only ~1 real-time second, which can tick by
        // unseen during a secret's content-app freeze or a level-completion scene transition. Popups are
        // queued here instead and only dispatched by FlushPendingPopups() once forcedTimeScale is unfrozen.
        private static readonly List<string> _pendingPopups = new();

        // Repeats each popup this many times since the ~1-second display isn't otherwise configurable
        // without patching TextManager's timer field.
        private const int PopupRepeatCount = 3;

        // Caps pending popups so a burst of checks/receives can't queue so many that later popups fall
        // far behind real time -- a dropped popup is still preserved in the log, just not shown.
        private const int MaxPendingPopups = 4;

        /// <summary>Full log entries, oldest first -- what Core/ArchipelagoLogApp.cs renders.</summary>
        public static IReadOnlyList<LogSegment[]> Entries => _entries;

        /// <summary>
        /// Records one log line, and queues it as a popup if popupText is non-null (null for
        /// history/resync entries that shouldn't interrupt play). Defaults orderKey to long.MaxValue --
        /// correct for any live event; see the 3-arg overload for historical entries.
        /// </summary>
        public static void Add(LogSegment[] segments, string? popupText) => Add(segments, popupText, long.MaxValue);

        /// <summary>
        /// Convenience overload for uncolored lines (error/fallback messages) -- wraps the text as one
        /// default-colored segment.
        /// </summary>
        public static void Add(string plainText, string? popupText) =>
            Add(new[] { new LogSegment(plainText, NotificationColors.Default) }, popupText, long.MaxValue);

        /// <summary>
        /// Same as the 2-arg Add(), but inserts at ascending orderKey position instead of always
        /// appending (see _entryOrderKeys). Historical/catch-up callers pass a real location id here.
        /// </summary>
        public static void Add(LogSegment[] segments, string? popupText, long orderKey)
        {
            int insertIndex = _entries.Count;
            while (insertIndex > 0 && _entryOrderKeys[insertIndex - 1] > orderKey)
            {
                insertIndex--;
            }
            _entries.Insert(insertIndex, segments);
            _entryOrderKeys.Insert(insertIndex, orderKey);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
                _entryOrderKeys.RemoveAt(0);
            }

            if (popupText != null)
            {
                _pendingPopups.Add(popupText);
                while (_pendingPopups.Count > MaxPendingPopups)
                {
                    _pendingPopups.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Dispatches popups queued by Add() to the native uptitle system once it's safe to (see
        /// _pendingPopups). Called every frame from Mod.OnUpdate(); a no-op when nothing is pending.
        /// </summary>
        public static void FlushPendingPopups()
        {
            if (_pendingPopups.Count == 0 || TimeControl.forcedTimeScale == 0f)
            {
                return;
            }

            foreach (string popupText in _pendingPopups)
            {
                for (int i = 0; i < PopupRepeatCount; i++)
                {
                    TextManager.AddUptitleToQueue(new LocalizableText(popupText));
                }
            }
            _pendingPopups.Clear();
        }

        /// <summary>
        /// Called by ArchipelagoConnection.Connect() right before every connect, so a reconnect never
        /// leaves stale or duplicated lines behind once the log is rebuilt.
        /// </summary>
        public static void Clear()
        {
            _entries.Clear();
            _entryOrderKeys.Clear();
            _pendingPopups.Clear();
        }
    }
}
