using System.Collections.Concurrent;
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
        private readonly ArchipelagoConnection _connection;
        private readonly MelonLogger.Instance _log;
        private readonly ConcurrentQueue<long> _itemQueue = new();

        public ItemManager(ArchipelagoConnection connection, MelonLogger.Instance log)
        {
            _connection = connection;
            _log = log;
            _connection.Connected += OnConnected;
        }

        private void OnConnected()
        {
            if (_connection.Session == null) return;
            _connection.Session.Items.ItemReceived += (helper) =>
            {
                var item = helper.DequeueItem();
                _itemQueue.Enqueue(item.ItemId);
            };
        }

        public void ProcessQueue()
        {
            while (_itemQueue.TryDequeue(out var itemId))
            {
                ApplyItem(itemId);
            }
        }

        private void ApplyItem(long itemId)
        {
            if (itemId == 0)
            {
                // Victory has code=None on the Python side, which the network layer
                // surfaces as 0 here -- nothing to apply in-game for it, it's a pure
                // logic/goal marker.
                return;
            }

            if (itemId == LevelCatalog.WhiteSpaceItemId)
            {
                // The pool's filler item (see LevelCatalog.WhiteSpaceItemId) -- expected
                // and harmless, not a real unlock, so this gets its own friendly log
                // rather than falling through to the "unknown item id" warning below,
                // which would otherwise fire on every single filler item received.
                _log.Msg("Received 'White Space' -- filler, nothing to apply.");
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

            // The hub only re-evaluates locks when piOsMenu.LockUnfinishedLevels() runs
            // (see Patches/HubUnlockPatch.cs), which happens on its own triggers inside
            // the menu code (e.g. opening/refreshing the hub view) -- we don't force a
            // refresh here. If it turns out unlocks don't visibly apply until the next
            // natural hub refresh, that's the first place to look.
        }
    }
}
