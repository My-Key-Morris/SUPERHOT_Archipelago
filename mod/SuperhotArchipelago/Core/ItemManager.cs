using System;
using System.Collections.Concurrent;
using Archipelago.MultiClient.Net.Enums;
using MelonLoader;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Handles items received from the Archipelago server. Session.Items.ItemReceived
    /// fires off Unity's main thread, so we can't safely touch game state directly from
    /// it -- instead we queue the item and let Mod.OnUpdate() drain the queue once per
    /// frame on the main thread.
    /// </summary>
    public class ItemManager
    {
        // Real, explicit user request (Notifications feature): any item that arrives
        // within this window of a successful connect is treated as part of the server's
        // replay of this slot's existing item history, not a genuinely new live arrival
        // -- "log only on replay, popups only for genuinely new items". See
        // ArchipelagoConnection.ConnectedAtUtc's own comment for why this is a real-time
        // grace period rather than a count comparison against Session.Items directly. 5
        // seconds is generous margin over how long even a slow replay of this game's
        // small (58-location) item pool could plausibly take -- no real gameplay item
        // could ever legitimately arrive this soon after just clicking "connect".
        private static readonly TimeSpan CatchUpWindow = TimeSpan.FromSeconds(5);

        private readonly ArchipelagoConnection _connection;
        private readonly MelonLogger.Instance _log;

        private readonly struct QueuedItem
        {
            public readonly long ItemId;
            public readonly long LocationId;
            public readonly string SenderDisplayName;

            // Real, explicit user follow-up request: "merge sends to self into one
            // line". ItemInfo.Player is "the player of the world the item is located
            // in" (confirmed via the Archipelago.MultiClient.Net.dll source docs) --
            // i.e. whoever's location check actually produced this item, not
            // necessarily who's receiving it. Comparing this slot against our own
            // (ArchipelagoConnection.Session.ConnectionInfo.Slot, read at Notify()
            // time rather than cached here since it can't change mid-connection) is
            // what lets Notify() recognize "the location I just checked happened to
            // contain my own item" and skip its own line entirely -- see that
            // method's own comment for why LocationManager.cs's matching check
            // already produces the one merged line this event needs.
            public readonly int SenderSlot;
            public readonly bool IsCatchUp;

            // Real, explicit user follow-up request: "coloring should be different
            // depending on item class (progression - green, normal - gray, trap -
            // red)". Archipelago reports this on every ItemInfo regardless of which
            // game the item belongs to -- see NotificationColors.ForItemFlags's own
            // doc for why this replaced an earlier, narrower id-based check.
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
            // Real bug found by live testing: this subscription used to happen
            // unguarded. ArchipelagoConnection.cs's Connected event now invokes each
            // subscriber with its own try/catch (see that class's own comment for the
            // full incident -- LocationManager.cs's OnConnected, subscribed first,
            // could throw and silently prevent this method from ever running at all,
            // which meant this class never subscribed to ItemReceived, which meant
            // UnlockState never got repopulated from the server's replay). This
            // try/catch is real defense in depth on top of that fix, not a
            // replacement for it -- this subscription succeeding is too important
            // (it's the only thing that repopulates UnlockState) to leave dependent on
            // nothing else in the invocation chain ever throwing.
            try
            {
                if (_connection.Session == null) return;

                // Real, explicit user request (Notifications feature): the log should
                // show this slot's full history, not just this session --
                // ArchipelagoConnection.Connect() already called NotificationLog.Clear()
                // right before this fired, and the server is about to replay this
                // slot's *entire* item history through ItemReceived below, which
                // naturally rebuilds this half of the log from scratch (each as a
                // catch-up entry, so no popup spam -- see CatchUpWindow below).
                _connection.Session.Items.ItemReceived += (helper) =>
                {
                    // Real bug found by live testing: a burst of ~12 checks sent in
                    // quick succession only produced 2 matching "Received" log entries
                    // -- most of what should have been received (and therefore most of
                    // UnlockState) silently never happened, with nothing in the
                    // MelonLoader console to explain why. Suspected root cause: this
                    // handler had no try/catch of its own -- if literally anything in
                    // it throws for one item (e.g. an edge case around Player being
                    // unexpectedly null for a given ItemInfo -- not confirmed, just the
                    // most plausible candidate), the exception propagates up into
                    // Archipelago.MultiClient.Net's own internal event dispatch code,
                    // which this mod doesn't control and can't assume is itself
                    // defensive about a subscriber throwing -- worst case, that could
                    // silently stop the library's own processing of further incoming
                    // items for the rest of the connection, exactly matching "only a
                    // few of many got through". Wrapping every single item's handling
                    // in its own try/catch, with a loud log line, means one bad item
                    // can now never take any others down with it, and if this really
                    // is the cause, the log will finally say so outright next time.
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

                // Real bug found by live testing, confirmed with temporary diagnostic
                // logging: this project had assumed since Round 25 that reconnecting
                // "replays this slot's full item history" through ItemReceived, with
                // UnlockState ending up rebuilt automatically -- untrue, and never
                // actually verified live against a room with real pre-existing items
                // until now. What the diagnostic logging showed: Session.Items.AllItemsReceived
                // already reported the correct historical count (10) the instant this
                // subscription completed, but ItemReceived never fired for a single one
                // of them -- it only fires for items that arrive *after* subscribing, a
                // live-forward-only notification, not a history replay mechanism. On a
                // room with real prior progress, that left UnlockState with only level
                // 1 (the one level that's unconditionally unlocked regardless of
                // UnlockState -- see HubUnlockPatch.cs) looking unlocked, no matter how
                // many items the server actually had on record, which is exactly the
                // "only Kick unlocked" symptom -- and explains why a brand new room
                // "worked": a fresh room has zero prior items, so there was nothing this
                // gap could lose.
                //
                // Real, SERIOUS bug found by live testing, this time confirmed by
                // decompiling Archipelago.MultiClient.Net.dll's own ReceivedItemsHelper
                // directly (not guessed): the fix above -- reading AllItemsReceived via
                // a plain foreach -- was itself incomplete, and is the actual root cause
                // of every "received notification doesn't match what I just did"/"looks
                // like it's grabbing an old one" report this whole project has seen,
                // including the user's own very early suspicion to that exact effect.
                // ReceivedItemsHelper keeps TWO parallel collections: `allItemsReceived`
                // (what AllItemsReceived exposes) and a separate `itemQueue`
                // (ConcurrentQueue<ItemInfo>) that DequeueItem() -- what the live
                // ItemReceived handler above calls -- actually reads from. Its
                // PerformResynchronization() (which runs the instant the server's
                // ReceivedItems packet arrives, confirmed to happen on every connect)
                // populates BOTH of those collections for every historical item, and
                // also invokes ItemReceived for each one -- but that happens on the
                // socket's own thread the moment the packet arrives, which is *before*
                // ArchipelagoConnection's Connected event (and therefore this
                // subscription) ever runs, so those particular invocations are always
                // missed. Reading AllItemsReceived directly (the previous fix) correctly
                // recovered the missed *notifications*, but never touched `itemQueue`
                // itself -- leaving every historical item still sitting there, un-dequeued,
                // in strict FIFO order. The result: the very next genuinely live
                // ItemReceived event (a real, new check/receive happening during actual
                // play) calls DequeueItem(), which -- since the queue's front is still
                // packed with old historical entries -- returns the OLDEST undequeued
                // historical item instead of the item that actually just arrived. Every
                // live receive after that keeps draining one more stale historical entry,
                // each one reported as if it just happened, completely disconnected from
                // whatever real check actually triggered the event -- exactly the
                // "Received X" that doesn't match what was just done, for as many live
                // events as it takes to work through the whole historical backlog.
                //
                // Fixed by properly draining the real queue here -- Any()/DequeueItem(),
                // the same calls the live handler itself uses -- instead of only reading
                // the separate AllItemsReceived snapshot. This leaves `itemQueue` empty
                // by the time this method returns, so every DequeueItem() call from a
                // genuinely live ItemReceived event afterward correctly returns the item
                // that actually just arrived.
                while (_connection.Session.Items.Any())
                {
                    // Same per-item try/catch reasoning as the live ItemReceived
                    // handler above -- this whole method is already wrapped by the
                    // outer try/catch, but that alone would let one bad historical
                    // item's exception abort this loop early, silently leaving every
                    // item after it stuck undequeued in the real queue again.
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

            // Real, explicit user follow-up request: "merge sends to self into one
            // line" -- see QueuedItem.SenderSlot's own comment and Notify()'s below
            // for the full reasoning. Computed once here (not per-branch) since every
            // branch below that can call Notify() needs the same answer.
            bool isSelfSend = _connection.Session != null
                && queued.SenderSlot == _connection.Session.ConnectionInfo.Slot;

            if (itemId == 0)
            {
                // Victory has code=None on the Python side, which the network layer
                // surfaces as 0 here -- nothing to apply in-game for it, it's a pure
                // logic/goal marker, and not something worth a notification either.
                return;
            }

            if (itemId == LevelCatalog.WhiteSpaceItemId)
            {
                // The pool's filler item (see LevelCatalog.WhiteSpaceItemId) -- expected
                // and harmless, not a real unlock, so this gets its own friendly log
                // rather than falling through to the "unknown item id" warning below,
                // which would otherwise fire on every single filler item received.
                _log.Msg("Received 'White Space' -- filler, nothing to apply.");
                Notify("White Space", queued.SenderDisplayName, queued.IsCatchUp, queued.LocationId, isSelfSend, queued.Flags);
                return;
            }

            if (!LevelCatalog.ItemIdToLevel.TryGetValue(itemId, out LevelEntry? level))
            {
                _log.Warning($"Received unknown item id {itemId} -- no matching level in LevelCatalog.");
                return;
            }

            // See Core/UnlockState.cs for why this doesn't touch the game's own save
            // data directly -- it can't express "level 20 unlocked but level 3 isn't."
            // Tracked by LevelId (the real game's LevelInfo.ID), not scene name -- see
            // LevelCatalog.LevelEntry's comment for why scene name isn't safe to use.
            UnlockState.Unlock(level.LevelId);
            _log.Msg($"Unlocked '{level.DisplayName}' (level id {level.LevelId}, scene '{level.SceneName}') from a received item.");
            Notify(level.DisplayName, queued.SenderDisplayName, queued.IsCatchUp, queued.LocationId, isSelfSend, queued.Flags);

            // The hub only re-evaluates locks when piOsMenu.LockUnfinishedLevels() runs
            // (see Patches/HubUnlockPatch.cs), which happens on its own triggers inside
            // the menu code (e.g. opening/refreshing the hub view) -- we don't force a
            // refresh here. If it turns out unlocks don't visibly apply until the next
            // natural hub refresh, that's the first place to look.
        }

        /// <summary>
        /// Records (and, for genuinely live items, pops up) a "you received X"
        /// notification, worded "Received [item] from [player]" -- item and player each
        /// in their own color (see Core/NotificationColors.cs), real, explicit user
        /// request. The plain-text popup keeps the same wording (TextManager's native
        /// uptitle queue can't render per-substring color -- see NotificationLog.Add's
        /// own comment).
        ///
        /// Real, explicit user follow-up request: "merge sends to self into one line
        /// 'Miikurb found their X'". isSelfSend means the location that granted this
        /// item was our own check -- the exact same real-world event that just made
        /// LocationManager.cs's ScoutAndQueueSentNotification/OnConnectedCore produce
        /// their own single merged "X found their Y" line for it (comparing the same
        /// sender/receiver slot equality this class does, just resolved from the
        /// opposite side of the same item). Logging anything here too would be a
        /// second, redundant line for one real event, so this returns immediately
        /// instead -- live or catch-up, log or popup, all skipped. The one edge case
        /// this can't cover: if LocationManager's own scout call happens to fail for
        /// this location (a real network hiccup), its fallback message can't detect
        /// self-send (it never gets scouted item/player data at all) and prints a
        /// generic "Sent check for X" -- and since this method still returns early
        /// here, no "Received" line appears in that case either. Rare and harmless
        /// (nothing is lost from UnlockState, just one notification line stays
        /// generic), not worth adding a second detection path for.
        ///
        /// Real bug found by live testing: catch-up entries from a reconnect's history
        /// resync were landing in the log as one solid block, all appearing well before
        /// (or well after) the matching "Sent" entries for the same locations -- see
        /// NotificationLog's own _entryOrderKeys comment for the full root cause. For a
        /// catch-up entry, locationId (the location that granted this item -- Session.Items'
        /// own ItemInfo.LocationId) is passed as the sort key so it lands right next to
        /// LocationManager.cs's own resync entry for that same location, which uses the
        /// exact same location id numbering. Live entries keep the old plain-append
        /// behavior (NotificationLog.Add's 2-arg overload, i.e. long.MaxValue).
        /// </summary>
        private void Notify(string itemDisplayName, string senderDisplayName, bool isCatchUp, long locationId, bool isSelfSend, ItemFlags flags)
        {
            if (isSelfSend)
            {
                return;
            }

            // Real bug found by the user's own follow-up report: this used to key
            // color off a hardcoded "is this the White Space item" check, which only
            // ever recognized this world's own two item kinds. Reading the real Flags
            // Archipelago reports on every ItemInfo is correct for any item from any
            // game -- see NotificationColors.ForItemFlags's own doc.
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
