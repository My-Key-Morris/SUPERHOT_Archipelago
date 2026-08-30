using System;
using System.Collections.Concurrent;
using Archipelago.MultiClient.Net.Enums;
using MelonLoader;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Handles items received from the Archipelago server. ItemReceived fires off Unity's main
    /// thread, so items are queued here and drained by Mod.OnUpdate() on the main thread.
    /// </summary>
    public class ItemManager
    {
        // Items arriving within this window of connecting are treated as history replay (log only, no
        // popup), not a genuinely new item. 5 seconds is generous margin for this game's small item pool.
        private static readonly TimeSpan CatchUpWindow = TimeSpan.FromSeconds(5);

        private readonly ArchipelagoConnection _connection;
        private readonly MelonLogger.Instance _log;

        private readonly struct QueuedItem
        {
            public readonly long ItemId;
            public readonly long LocationId;
            public readonly string SenderDisplayName;

            // ItemInfo.Player is whoever's location check produced this item, not necessarily the receiver --
            // compared against our own slot in Notify() to detect and merge self-sends into one line.
            public readonly int SenderSlot;
            public readonly bool IsCatchUp;

            // Drives notification color by item class (progression/normal/trap) -- see
            // NotificationColors.ForItemFlags.
            public readonly ItemFlags Flags;

            public QueuedItem(long itemId, long locationId, string senderDisplayName, int senderSlot, ItemFlags flags, bool isCatchUp)
            {
                ItemId = itemId;
                LocationId = locationId;
                SenderDisplayName = senderDisplayName;
                SenderSlot = senderSlot;
                Flags = flags;
                IsCatchUp = isCatchUp;
            }
        }

        private readonly ConcurrentQueue<QueuedItem> _itemQueue = new();

        public ItemManager(ArchipelagoConnection connection, MelonLogger.Instance log)
        {
            _connection = connection;
            _log = log;
            _connection.Connected += OnConnected;
        }

        private void OnConnected()
        {
            // Defense in depth on top of ArchipelagoConnection's per-subscriber try/catch: this subscription
            // is the only thing that repopulates UnlockState, so it must never be skipped by another
            // handler's failure.
            try
            {
                if (_connection.Session == null) return;

                // The server is about to replay this slot's entire item history through ItemReceived, rebuilding
                // this half of the log from scratch as catch-up entries (no popup spam -- see CatchUpWindow).
                _connection.Session.Items.ItemReceived += (helper) =>
                {
                    // Without a try/catch here, one item throwing (e.g. an unexpected null Player) could propagate
                    // into the client library's own event dispatch and silently stop it from processing further
                    // items for the rest of the connection -- wrapping each item isolates failures.
                    try
                    {
                        var item = helper.DequeueItem();
                        bool isCatchUp = DateTime.UtcNow - _connection.ConnectedAtUtc < CatchUpWindow;
                        string senderDisplayName = item.Player.Alias;
                        _itemQueue.Enqueue(new QueuedItem(item.ItemId, item.LocationId, senderDisplayName, item.Player.Slot, item.Flags, isCatchUp));
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"ItemReceived handler threw for one item -- it may not unlock its " +
                            $"level, but every other item is unaffected: {ex}");
                    }
                };

                // ItemReceived doesn't fire for history on reconnect (it's live-forward-only): must drain the
                // library's internal itemQueue directly via Any()/DequeueItem(), not just read AllItemsReceived,
                // or live receives afterward will wrongly dequeue stale historical items instead of new ones.
                while (_connection.Session.Items.Any())
                {
                    // Per-item try/catch so one bad historical item doesn't abort the loop and leave the rest
                    // stuck undequeued.
                    try
                    {
                        var item = _connection.Session.Items.DequeueItem();
                        _itemQueue.Enqueue(new QueuedItem(item.ItemId, item.LocationId, item.Player.Alias, item.Player.Slot, item.Flags, isCatchUp: true));
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Draining one historical item from the received-items queue threw -- it " +
                            $"may not unlock its level, but every other item is unaffected: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"ItemManager.OnConnected failed -- items received this connection " +
                    $"may not unlock levels correctly: {ex}");
            }
        }

        public void ProcessQueue()
        {
            while (_itemQueue.TryDequeue(out var queued))
            {
                ApplyItem(queued);
            }
        }

        private void ApplyItem(QueuedItem queued)
        {
            long itemId = queued.ItemId;

            // Computed once since every branch below that calls Notify() needs the same self-send check.
            bool isSelfSend = _connection.Session != null
                && queued.SenderSlot == _connection.Session.ConnectionInfo.Slot;

            if (itemId == 0)
            {
                // Victory has code=None on the Python side, surfaced as 0 here -- a pure logic/goal marker with
                // nothing to apply or notify.
                return;
            }

            if (itemId == LevelCatalog.WhiteSpaceItemId)
            {
                // Filler item (see LevelCatalog.WhiteSpaceItemId) -- gets its own log instead of falling through
                // to the "unknown item id" warning below.
                _log.Msg("Received 'White Space' -- filler, nothing to apply.");
                Notify("White Space", queued.SenderDisplayName, queued.IsCatchUp, queued.LocationId, isSelfSend, queued.Flags);
                return;
            }

            if (!LevelCatalog.ItemIdToLevel.TryGetValue(itemId, out LevelEntry? level))
            {
                _log.Warning($"Received unknown item id {itemId} -- no matching level in LevelCatalog.");
                return;
            }

            // See UnlockState.cs for why this doesn't touch native save data directly; tracked by LevelId,
            // not scene name (see LevelCatalog.LevelEntry for why scene name isn't safe).
            UnlockState.Unlock(level.LevelId);
            _log.Msg($"Unlocked '{level.DisplayName}' (level id {level.LevelId}, scene '{level.SceneName}') from a received item.");
            Notify(level.DisplayName, queued.SenderDisplayName, queued.IsCatchUp, queued.LocationId, isSelfSend, queued.Flags);

            // The hub only re-evaluates locks when piOsMenu.LockUnfinishedLevels() runs on its own triggers
            // (see HubUnlockPatch.cs) -- no forced refresh here.
        }

        /// <summary>
        /// Records (and, for live items, pops up) a "Received X from Y" notification. Returns immediately
        /// for a self-send since LocationManager.cs already produces the merged "X found their Y" line for
        /// the same event. Catch-up entries pass locationId as the sort key so they land next to
        /// LocationManager's matching resync entry; live entries plain-append via long.MaxValue.
        /// </summary>
        private void Notify(string itemDisplayName, string senderDisplayName, bool isCatchUp, long locationId, bool isSelfSend, ItemFlags flags)
        {
            if (isSelfSend)
            {
                return;
            }

            // Uses the real Flags (not a hardcoded item check) so this works for any item from any game.
            char itemColor = NotificationColors.ForItemFlags(flags);
            var segments = new[]
            {
                new LogSegment("Received ", NotificationColors.Default),
                new LogSegment(itemDisplayName, itemColor),
                new LogSegment(" from ", NotificationColors.Default),
                new LogSegment(senderDisplayName, NotificationColors.Player),
            };
            string text = $"Received {itemDisplayName} from {senderDisplayName}";

            if (isCatchUp)
            {
                NotificationLog.Add(segments, null, locationId);
            }
            else
            {
                NotificationLog.Add(segments, text);
            }
        }
    }
}
