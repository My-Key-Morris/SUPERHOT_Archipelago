using System.Collections.Generic;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Real, explicit user request: "a popup while the user is playing to show recent
    /// checks pertaining to them. I also [want] a log section in the hub to see older
    /// notifications" -- scoped to items received and the player's own checks sent (not
    /// other players' checks, which SUPERHOT has no in-fiction way to attribute to a
    /// location the player would recognize). This class is the single shared sink both
    /// halves of that request go through: Core/ItemManager.cs and Core/LocationManager.cs
    /// call Add() when something notification-worthy happens, Core/ArchipelagoLogApp.cs's
    /// screen reads Entries to render the hub log.
    ///
    /// Popup delivery reuses TextManager.AddUptitleToQueue(LocalizableText) -- the same
    /// native queued top-of-screen text mechanism this mod already uses for LOCKED block
    /// messages (see e.g. Core/LevelAccessGuard.cs's callers), rather than building a
    /// second, separate popup system.
    ///
    /// Real, explicit user request: the log should reflect this slot's full history on
    /// the Archipelago server, not just whatever happened to occur during the current
    /// process/connection -- toggling AP MODE off and back on (or restarting the game)
    /// should never look like it erased anything, only refreshed once reconnected. The
    /// log itself is still a plain in-memory list, not persisted to disk -- but
    /// ArchipelagoConnection.Connect() calls Clear() below right before every successful
    /// connect (including a reconnect), and LocationManager.cs/ItemManager.cs each
    /// rebuild their half from the server's own authoritative state on every
    /// Connected event (Session.Locations.AllLocationsChecked and the replayed
    /// ItemReceived history respectively) -- so the log always ends up back at the same
    /// complete, correct picture a moment after connecting, not a partial one.
    /// </summary>
    public static class NotificationLog
    {
        // Generous defensive cap, not a real, expected limit -- a maximally-played run
        // has at most 58 item-received entries and 58 check-sent entries (see
        // LevelCatalog), so this is never expected to actually trigger.
        private const int MaxEntries = 200;

        private static readonly List<string> _entries = new();

        // Real bug found by live testing: the log was showing every "Received" entry
        // from a reconnect's history resync as one solid block, followed much later by
        // every "Sent" entry as a second solid block -- instead of the two naturally
        // interleaved, matching how the player actually experienced them (check a
        // location, get its item, check the next one...). Root cause: ItemManager.cs's
        // resync (Session.Items.AllItemsReceived) is a synchronous, in-memory snapshot
        // that finishes within the same frame, while LocationManager.cs's resync
        // (Session.Locations.ScoutLocationsAsync) is a real network round-trip that
        // only finishes several frames later -- so one manager's whole batch always
        // lands in the log well before the other's, no matter how the underlying
        // events were actually ordered in-game. Each entry now carries an explicit
        // orderKey (see Add() below) -- both managers use Archipelago's own location
        // id for historical/catch-up entries (ItemManager via ItemInfo.LocationId, the
        // location that granted the item; LocationManager via the location id it
        // scouted), which is the same numbering scheme for both, so a location's
        // "Sent" and "Received" entries now sort next to each other regardless of
        // which async batch actually appends first. Live (non-catch-up) entries keep
        // using long.MaxValue -- always sorts after every historical entry, and among
        // themselves in true real-time call order, exactly like before this fix.
        private static readonly List<long> _entryOrderKeys = new();

        // Real bug reports from live testing: a popup queued at the exact moment a
        // secret console's "content app" overlay opens never became visible at all,
        // and one queued right at level completion was visible only very briefly.
        // Root cause for both, confirmed via decompile: TextManager's native
        // uptitleSHGUIQueue gives every entry a flat ~1 real-time second
        // (Update()'s uptitleSHGUITimer, reset to 1f each time an item is popped)
        // before moving on, with no guarantee of an undisturbed turn -- a secret's
        // content app freezes gameplay via TimeControl.forcedTimeScale = 0f
        // (TerminalActivator.OnActivate) for as long as the player leaves it open,
        // during which our message's single second can tick away unseen; a level
        // completion's scene transition to the hub is similarly disruptive. Popups
        // are queued here instead of calling TextManager.AddUptitleToQueue directly,
        // and only actually dispatched by FlushPendingPopups() (called every frame
        // from Mod.OnUpdate()) once TimeControl.forcedTimeScale is back to its
        // normal not-frozen state -- the same real-time signal
        // TerminalActivator.Update() itself uses to notice its own content app has
        // closed.
        private static readonly List<string> _pendingPopups = new();

        // Each dispatched popup is queued this many times in a row in the native
        // queue, since a single entry's ~1-second display (see above) isn't a
        // configurable value without patching TextManager's own timer field --
        // repeating the same text is a low-risk way to get a longer, more readable
        // display out of the existing public API instead.
        private const int PopupRepeatCount = 3;

        // Real, explicit user report: a burst of many real checks/receives in quick
        // succession (e.g. speed-running several already-unlocked levels back to
        // back) queued so many popups -- each repeated PopupRepeatCount times, at
        // ~1 real second per native queue slot -- that later ones were still working
        // through the backlog well after the actions that triggered them, looking
        // like they'd gotten mismatched with whatever the player was doing by then.
        // Capping how many distinct pending popups are kept means live popups can
        // never fall meaningfully behind real time -- a dropped one is still fully
        // preserved in the log (Add() records logText unconditionally, this only
        // ever trims _pendingPopups), so nothing is actually lost, just not popped
        // up individually during a big burst.
        private const int MaxPendingPopups = 4;

        /// <summary>Full log text, oldest first -- what Core/ArchipelagoLogApp.cs renders.</summary>
        public static IReadOnlyList<string> Entries => _entries;

        /// <summary>
        /// Records one log line, and -- if popupText is non-null -- also queues it as an
        /// in-game popup. Callers pass null for popupText for anything that shouldn't
        /// interrupt play (history being replayed/resynced on connect; see
        /// ItemManager.cs/LocationManager.cs), and real display text otherwise.
        ///
        /// Defaults orderKey to long.MaxValue -- plain chronological append, correct for
        /// any genuinely live event, which is every caller except the two historical
        /// resync loops (see the 3-arg overload below and _entryOrderKeys' own comment).
        /// </summary>
        public static void Add(string logText, string? popupText) => Add(logText, popupText, long.MaxValue);

        /// <summary>
        /// Same as Add(string, string?) above, but inserts the entry in ascending
        /// orderKey position among other entries rather than always appending -- see
        /// _entryOrderKeys' own comment for why this exists. Historical/catch-up callers
        /// pass a real Archipelago location id here; everything else should keep using
        /// the 2-arg overload (which passes long.MaxValue, i.e. "always append last").
        /// </summary>
        public static void Add(string logText, string? popupText, long orderKey)
        {
            int insertIndex = _entries.Count;
            while (insertIndex > 0 && _entryOrderKeys[insertIndex - 1] > orderKey)
            {
                insertIndex--;
            }
            _entries.Insert(insertIndex, logText);
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
        /// Dispatches any popups queued by Add() above to the native uptitle system,
        /// but only once it's safe to -- see _pendingPopups' own comment for the full
        /// reasoning. Called every frame from Mod.OnUpdate(); cheap no-op on every
        /// frame with nothing pending.
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
        /// Called by ArchipelagoConnection.Connect() right before every successful
        /// connect (including a reconnect). The log is about to be fully rebuilt from
        /// the server's own authoritative history (see class doc) -- clearing first
        /// means a reconnect can never leave stale or duplicated lines behind, only a
        /// clean, correct picture a moment later.
        /// </summary>
        public static void Clear()
        {
            _entries.Clear();
            _entryOrderKeys.Clear();
            _pendingPopups.Clear();
        }
    }
}
