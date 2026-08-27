using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Models;
using MelonLoader;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Maps in-game level-complete events to Archipelago location checks. Harmony
    /// patches (see ../Patches/LevelCompletePatch.cs) call CheckLocation(levelId) when
    /// LevelSetup.UnlockNextLevel() fires; this class doesn't know or care how that
    /// detection happens, it just needs the level id (the real game's LevelInfo.ID) to
    /// look up via LevelCatalog.LevelIdToLevel. Real bug found by testing: matching by
    /// scene name instead used to silently fail for levels that reuse a scene (e.g.
    /// "Cyberspace (1)" never linked to anything) -- see LevelCatalog.LevelEntry.
    /// </summary>
    public class LocationManager
    {
        private readonly ArchipelagoConnection _connection;
        private readonly MelonLogger.Instance _log;

        // Real, explicit user request (Notifications feature): "Sent [item] to
        // [player]" needs to know which item was actually at a checked location and
        // who receives it -- neither is known locally at the moment CompleteLocationChecks
        // is sent, only the location id is. Session.Locations.ScoutLocationsAsync (confirmed
        // via reflecting Archipelago.MultiClient.Net.dll) resolves that asynchronously;
        // its continuation runs on a thread-pool thread, not Unity's main thread, so
        // results are queued here (same ConcurrentQueue pattern ItemManager.cs already
        // uses for ItemReceived) and only turned into a NotificationLog entry by
        // ProcessPendingNotifications() on Mod.OnUpdate().
        // Real bug found by live testing: OrderKey exists so historical/catch-up
        // entries can be inserted next to their matching item-received entry instead of
        // always landing wherever this async continuation happens to finish relative to
        // ItemManager's own (synchronous) resync -- see NotificationLog's own
        // _entryOrderKeys comment for the full root cause. Live entries pass
        // long.MaxValue (plain append, same behavior as before this fix).
        private readonly ConcurrentQueue<(string LogText, string? PopupText, long OrderKey)> _pendingNotifications = new();

        public LocationManager(ArchipelagoConnection connection, MelonLogger.Instance log)
        {
            _connection = connection;
            _log = log;
            _connection.Connected += OnConnected;
        }

        /// <summary>
        /// Real, explicit user request (Notifications feature): the AP LOG screen should
        /// show this slot's full history on the server, not just checks sent during the
        /// current process/connection -- toggling AP MODE off and back on (or restarting
        /// the game) shouldn't look like it erased anything. Session.Locations.AllLocationsChecked
        /// is populated by the server on every connect with this slot's *complete*
        /// historical set, not just this session's -- so every connect (including a
        /// reconnect) re-scouts the full set and rebuilds the "sent" half of the log
        /// from scratch. (See NotificationLog.Clear(), called by
        /// ArchipelagoConnection.Connect() right before this fires, and ItemManager.cs's
        /// own OnConnected for the "received" half's equivalent rebuild via the
        /// server's replayed ItemReceived history.) Log-only, never a popup -- this is
        /// necessarily history, not something that just happened live.
        /// </summary>
        private void OnConnected()
        {
            // Real bug found by live testing: this whole body used to run unguarded.
            // ArchipelagoConnection.cs's Connected event now invokes each subscriber
            // with its own try/catch (see that class's own comment for the full
            // incident), but this try/catch is kept here too as real defense in depth
            // -- this method's own failure should never be able to affect any other
            // subscriber (ItemManager.cs's own OnConnected, in particular, is what
            // actually repopulates UnlockState) regardless of how they're invoked.
            try
            {
                OnConnectedCore();
            }
            catch (System.Exception ex)
            {
                _log.Error($"LocationManager.OnConnected failed, notification history resync " +
                    $"skipped for this connection: {ex}");
            }
        }

        private void OnConnectedCore()
        {
            if (_connection.Session == null)
            {
                return;
            }

            long[] checkedLocations = _connection.Session.Locations.AllLocationsChecked
                .Where(id => LevelCatalog.LocationIdToLevel.ContainsKey(id) ||
                             LevelCatalog.SecretLocationIdToLevel.ContainsKey(id))
                .ToArray();

            if (checkedLocations.Length == 0)
            {
                return;
            }

            _connection.Session.Locations.ScoutLocationsAsync(checkedLocations).ContinueWith(task =>
            {
                if (task.IsFaulted || task.IsCanceled || task.Result == null)
                {
                    _log.Warning("Failed to scout this slot's already-checked locations -- " +
                        "the AP LOG screen won't show full history until the next successful reconnect.");
                    return;
                }

                foreach (KeyValuePair<long, ScoutedItemInfo> kvp in task.Result)
                {
                    // Per-item try/catch, same reasoning as ItemManager.cs's own --
                    // one bad scouted entry (e.g. unexpected null Player) should never
                    // be able to abort this foreach and silently lose every entry
                    // after it in the enumeration.
                    try
                    {
                        string itemName = LevelCatalog.TryGetShortItemDisplayName(kvp.Value.ItemId) ?? kvp.Value.ItemDisplayName;
                        string logText = $"Sent {itemName} to {kvp.Value.Player.Alias} " +
                            $"from {ResolveLocationDisplayText(kvp.Key)}";
                        // kvp.Key is this location's own Archipelago id -- same sort-key
                        // scale ItemManager.cs's Notify() uses via ItemInfo.LocationId, so
                        // this entry lands right beside the matching "Received" entry for
                        // whatever item this location actually granted.
                        _pendingNotifications.Enqueue((logText, null, kvp.Key));
                    }
                    catch (System.Exception ex)
                    {
                        _log.Error($"Processing one scouted historical location threw -- it may be " +
                            $"missing from the AP LOG, but every other entry is unaffected: {ex}");
                    }
                }
            });
        }

        /// <summary>
        /// Drains scout results queued by OnConnected/ScoutAndQueueSentNotification into
        /// NotificationLog on the main thread. Called every frame from Mod.OnUpdate(),
        /// same shape as ItemManager.ProcessQueue().
        /// </summary>
        public void ProcessPendingNotifications()
        {
            while (_pendingNotifications.TryDequeue(out var pending))
            {
                NotificationLog.Add(pending.LogText, pending.PopupText, pending.OrderKey);
            }
        }

        public void CheckLocation(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                _log.Warning($"CheckLocation({levelId}) called before connecting -- ignored.");
                return;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                _log.Warning($"Unknown level id {levelId} -- no matching entry in levels.json. " +
                              "Either a level outside our catalog just finished, or levels.json's " +
                              "order/id list is out of sync with the real game.");
                return;
            }

            // Real, explicit user request: the final level shouldn't generate its own
            // real, checkable location at all -- "that's ending the game, and all checks
            // should be released anyways" (a real fillable check behind "beat the entire
            // game" is bad multiworld UX for anyone whose own progression depends on it).
            // apworld/superhot/Locations.py no longer creates a location for it, so
            // sending a CompleteLocationChecks for it here would just be a check for a
            // location id the server doesn't know about. Goal completion is still
            // reported below regardless, via the separate SetGoalAchieved signal.
            //
            // Real, explicit user request (ExcludeSlowLevels, see apworld/superhot/Options.py):
            // a level this room's slot data marked excluded has no real location either --
            // same reasoning as the final level above, just per-player-optional instead of
            // universal. LevelAccessGuard.cs already treats an excluded level as always
            // unlocked, so this only ever skips a check that was never possible to send in
            // the first place, not one the player was blocked from making.
            if (level.Order != LevelCatalog.Levels.Count && !_connection.IsLevelExcluded(level.Order))
            {
                long locationId = LevelCatalog.BaseId + level.Order;

                // Real bug found by live testing: unlocked levels stay freely
                // replayable, and this Harmony hook fires every time
                // LevelSetup.UnlockNextLevel() does -- replaying an already-completed
                // level (or, per AutoTransitionCheckPatch.cs's own docstring, an
                // overlapping second hook catching the same real completion) used to
                // resend a check and, worse, re-queue a duplicate "Sent" popup/log
                // entry every single time, even though nothing new had actually
                // happened. AllLocationsChecked updates instantly and locally the
                // moment CompleteLocationChecks() succeeds (see IsLevelCompleted's own
                // comment) -- checking it first here is what makes this a genuine,
                // reliable "first time only" guard rather than a best-effort one.
                if (_connection.Session.Locations.AllLocationsChecked.Contains(locationId))
                {
                    _log.Msg($"'{level.DisplayName}' already checked -- skipping duplicate check/notification.");
                }
                else
                {
                    _connection.Session.Locations.CompleteLocationChecks(locationId);
                    _log.Msg($"Sent check for '{level.DisplayName}' (level id {levelId}, location id {locationId}).");

                    // Real, explicit user request (Notifications feature: "items
                    // received + your own checks", worded as "Sent [item] to
                    // [player]"). Unlike item receives, a sent check is always a
                    // genuinely live, real-time event -- it only ever fires from an
                    // actual Harmony hook on real gameplay (see
                    // Patches/LevelCompletePatch.cs) -- so this always gets a popup,
                    // not just a log entry.
                    ScoutAndQueueSentNotification(locationId, level.DisplayName, isLive: true);
                }
            }

            // Real bug found by an actual test run: sending the final level's location
            // check is NOT the same as telling the server the player has won. AP's
            // "Victory" event item (apworld/superhot/Items.py) has no real network id, so
            // it never arrives as a received item for the mod to react to -- goal
            // completion is a separate signal the client has to send explicitly
            // (confirmed by decompiling Archipelago.MultiClient.Net.dll:
            // ArchipelagoSession.SetGoalAchieved(), which sends a StatusUpdatePacket with
            // ArchipelagoClientState.ClientGoal). The apworld's Rules.py gates the
            // Victory location on having the *last* level's access item, which in
            // practice means "the last level in levels.json order got completed" -- so
            // that's the trigger here, confirmed correct by an actual playthrough where
            // finishing the level in this position was in fact the game's real ending.
            if (level.Order == LevelCatalog.Levels.Count)
            {
                _connection.Session.SetGoalAchieved();
                _log.Msg($"'{level.DisplayName}' was the final level -- reported goal achieved.");
            }
        }

        /// <summary>
        /// Reports finding a level's secret console (see Patches/SecretFoundPatch.cs for
        /// the Harmony hook that detects this). Uses a distinct location id range from
        /// the level's own completion check -- see LevelCatalog.SecretLocationIdOffset --
        /// so the two are separate, independently checkable Archipelago locations, exactly
        /// matching apworld/superhot/Locations.py's secret_location_name entries.
        /// </summary>
        public void CheckSecretLocation(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                _log.Warning($"CheckSecretLocation({levelId}) called before connecting -- ignored.");
                return;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                _log.Warning($"Unknown level id {levelId} for a secret find -- no matching entry in " +
                              "levels.json. Either a level outside our catalog has a secret we don't " +
                              "know about, or levels.json's order/id list is out of sync with the real game.");
                return;
            }

            if (!level.HasSecret)
            {
                _log.Warning($"Secret found in '{level.DisplayName}', but levels.json says this level " +
                              "has none -- sending the check anyway, but this likely means levels.json's " +
                              "hasSecret is out of sync with the real game.");
            }

            // Real, explicit user request (ExcludeSlowLevels, see apworld/superhot/Options.py):
            // an excluded level's secret location doesn't exist in this room either (see
            // CheckLocation's own matching check) -- nothing to send a check for.
            if (_connection.IsLevelExcluded(level.Order))
            {
                _log.Msg($"'{level.DisplayName}' secret found, but this level is excluded from " +
                    "tracking (ExcludeSlowLevels) -- no check to send.");
                return;
            }

            long locationId = LevelCatalog.BaseId + LevelCatalog.SecretLocationIdOffset + level.Order;

            // Same reasoning as CheckLocation's own already-checked guard -- a secret
            // console can be revisited (or its find re-detected across a level
            // reload), and this stops that from resending a check or re-queuing a
            // duplicate notification.
            if (_connection.Session.Locations.AllLocationsChecked.Contains(locationId))
            {
                _log.Msg($"'{level.DisplayName}' secret already checked -- skipping duplicate check/notification.");
                return;
            }

            _connection.Session.Locations.CompleteLocationChecks(locationId);
            _log.Msg($"Sent secret check for '{level.DisplayName}' (level id {levelId}, location id {locationId}).");

            // Same reasoning as CheckLocation's own ScoutAndQueueSentNotification call
            // above -- always a genuinely live event, never replayed. "Secret" appended
            // here is what actually distinguishes this from a main-completion check in
            // the log's "from X" suffix below -- real bug found by the user's own
            // follow-up question ("how do you differentiate between secrets and level
            // completes?"): an earlier version of this call passed level.DisplayName
            // alone for both this level's main *and* secret checks, so both showed up
            // in the log as "from 29 - Train" with no way to tell them apart.
            ScoutAndQueueSentNotification(locationId, $"{level.DisplayName} Secret", isLive: true);
        }

        /// <summary>
        /// Real, explicit user request (Notifications feature): notification text reads
        /// "Sent [item] to [player]", which needs the item actually placed at this
        /// location and its receiving player -- neither known locally at the moment a
        /// check is sent (see class doc). ScoutLocationsAsync resolves it; the
        /// continuation runs off Unity's main thread, so the result is queued for
        /// ProcessPendingNotifications() to turn into a real NotificationLog entry on
        /// the main thread, same pattern OnConnected's bulk history resync uses.
        ///
        /// locationDisplayText serves two roles: it's the log-only "from X" suffix
        /// (real, explicit user request) on success, and the whole message if scouting
        /// itself fails (e.g. a network hiccup) -- either way a real gameplay event
        /// still produces *some* notification rather than silently vanishing. Callers
        /// pass the level's own display name for a main completion, or "X Secret" for
        /// a secret check -- see CheckLocation/CheckSecretLocation's own call sites.
        /// </summary>
        private void ScoutAndQueueSentNotification(long locationId, string locationDisplayText, bool isLive)
        {
            if (_connection.Session == null)
            {
                return;
            }

            _connection.Session.Locations.ScoutLocationsAsync(new[] { locationId }).ContinueWith(task =>
            {
                // Real defensive fix, same reasoning as ItemManager.cs's own per-item
                // try/catch: this continuation runs on a thread-pool thread with
                // nothing awaiting it, so an uncaught exception here wouldn't just
                // silently drop this one notification -- on .NET Framework, an
                // unobserved faulted Task can raise TaskScheduler.UnobservedTaskException
                // when it's garbage collected, which historically can tear down the
                // whole process. Never confirmed that's happened here, but there's no
                // reason to leave the possibility open when a plain try/catch closes
                // it for free.
                try
                {
                    string popupText;
                    string logText;
                    if (!task.IsFaulted && !task.IsCanceled && task.Result != null &&
                        task.Result.TryGetValue(locationId, out ScoutedItemInfo scouted))
                    {
                        // Real, explicit user request: notification text was running
                        // long enough to get truncated on the AP LOG screen -- see
                        // LevelCatalog.TryGetShortItemDisplayName's own comment for why
                        // this trades AP's own "Level Access: X" item name for this
                        // mod's shorter catalog one wherever it's recognized.
                        string itemName = LevelCatalog.TryGetShortItemDisplayName(scouted.ItemId) ?? scouted.ItemDisplayName;
                        popupText = $"Sent {itemName} to {scouted.Player.Alias}";

                        // Real, explicit user request: show which location the check
                        // came from, but log only -- popupText above stays exactly
                        // what fits a quick glance, logText gets the extra context
                        // since the log screen has room for it.
                        logText = $"{popupText} from {locationDisplayText}";
                    }
                    else
                    {
                        _log.Warning($"Failed to scout location {locationId} for a notification -- " +
                            "showing a generic message instead.");
                        popupText = $"Sent check for {locationDisplayText}";
                        logText = popupText;
                    }

                    // Always a genuinely live event here (see class doc / call sites) --
                    // long.MaxValue keeps this a plain append in true real-time order,
                    // same as before this fix. Only the history-resync path above (which
                    // never calls this method) needs a real location-id order key.
                    _pendingNotifications.Enqueue((logText, isLive ? popupText : null, long.MaxValue));
                }
                catch (System.Exception ex)
                {
                    _log.Error($"Building a notification for location {locationId} threw -- it may be " +
                        $"missing a popup/log entry, but nothing else is affected: {ex}");
                }
            });
        }

        /// <summary>
        /// Resolves a raw checked location id back to a level's display name, for the
        /// history resync's log-only "from X" suffix (see ScoutAndQueueSentNotification's
        /// own comment) -- prefers this mod's own catalog naming over
        /// ScoutedItemInfo.LocationDisplayName for consistency with every other display
        /// string in this codebase.
        /// </summary>
        private static string ResolveLocationDisplayText(long locationId)
        {
            if (LevelCatalog.LocationIdToLevel.TryGetValue(locationId, out LevelEntry? level))
            {
                return level.DisplayName;
            }

            if (LevelCatalog.SecretLocationIdToLevel.TryGetValue(locationId, out LevelEntry? secretLevel))
            {
                return $"{secretLevel.DisplayName} Secret";
            }

            return "an unknown location";
        }

        /// <summary>
        /// Whether this level's location check has actually been sent and confirmed --
        /// distinct from "unlocked" (see Core/UnlockState.cs), which only means the
        /// access item was received, not that the level has been played yet. Used by
        /// Patches/HubUnlockPatch.cs to tell "unlocked but not yet completed" (grey) apart
        /// from "unlocked and completed" (white) -- a real, explicit cosmetic request.
        ///
        /// Deliberately reads the live Archipelago session instead of keeping a second
        /// local set: Session.Locations.AllLocationsChecked (confirmed via decompiling
        /// Archipelago.MultiClient.Net.dll's LocationCheckHelper) is populated from the
        /// server's own ConnectedPacket on every connect/reconnect, and updated instantly
        /// -- both locally the moment CompleteLocationChecks() is called above, and via
        /// the server's RoomUpdatePacket echo. That makes it the authoritative source of
        /// truth, survives a full game restart without us tracking anything ourselves,
        /// and can't drift out of sync with what the server actually has recorded.
        /// </summary>
        public bool IsLevelCompleted(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                return false;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                return false;
            }

            // The final level has no completion location to read here anymore (see
            // CheckLocation above) -- there's no server-authoritative "completed" signal
            // left to distinguish "unlocked but not yet played" from "unlocked and
            // played" for it, the same way level 1 has no access rule to distinguish
            // "locked" from "unlocked". Rather than invent fragile local state that
            // wouldn't survive a restart and would fight the "server is the source of
            // truth" design used everywhere else here, it's just treated as completed as
            // soon as it's unlocked -- HubUnlockPatch.cs shows it white, not grey, the
            // moment the access item is received.
            if (level.Order == LevelCatalog.Levels.Count)
            {
                return UnlockState.IsUnlocked(levelId);
            }

            // Real, explicit user request (ExcludeSlowLevels): same reasoning as the final
            // level above -- an excluded level has no completion location to read here
            // either (see CheckLocation), so there's no server-authoritative "completed"
            // signal for it. LevelAccessGuard.cs already always unlocks it, so treating it
            // as completed too keeps HubUnlockPatch.cs showing it white/normal rather than
            // permanently grey ("unlocked but never played") for a level that was never a
            // real check to begin with.
            if (_connection.IsLevelExcluded(level.Order))
            {
                return true;
            }

            long locationId = LevelCatalog.BaseId + level.Order;
            return _connection.Session.Locations.AllLocationsChecked.Contains(locationId);
        }

        /// <summary>
        /// Same idea as IsLevelCompleted, but for a level's secret console location
        /// (see CheckSecretLocation above) instead of its main completion location.
        ///
        /// Real bug found by playtesting: the hub's native "CRACKED!" badge/description
        /// text (LevelInfo.SecretsFound(), confirmed via decompile) reads straight from
        /// SUPERHOT's own save data (SaveManager key SceneFileName + secretIndex +
        /// "unlocked"), completely independent of anything Archipelago has actually
        /// tracked this run. A save file with leftover flags from an earlier/different
        /// playthrough shows "CRACKED!" for a secret the current multiworld run has never
        /// had checked -- exactly the class of bug IsLevelCompleted above already solves
        /// for main level completion, using the exact same fix: trust the live
        /// Archipelago session instead of native save state. See
        /// Patches/HubUnlockPatch.cs for where this is used to correct the hub display,
        /// and Core/Mod.cs's OnSceneWasLoaded for where this same value is also used to
        /// actively repair the underlying native save flag itself.
        /// </summary>
        public bool IsSecretCompleted(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                return false;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                return false;
            }

            // Real, explicit user request (ExcludeSlowLevels): an excluded level's secret
            // location doesn't exist either (see CheckSecretLocation's own matching
            // guard) -- same "always show completed, never grey" reasoning as
            // IsLevelCompleted above, just for the secret badge instead of the main one.
            if (_connection.IsLevelExcluded(level.Order))
            {
                return true;
            }

            long locationId = LevelCatalog.BaseId + LevelCatalog.SecretLocationIdOffset + level.Order;
            return _connection.Session.Locations.AllLocationsChecked.Contains(locationId);
        }

        /// <summary>
        /// How many of the other 31 story levels (every tracked level except "34 -
        /// Free" itself) have actually had their completion check sent this run.
        /// Used by Core/LevelAccessGuard.cs to gate entry to Free, and by
        /// Patches/HubUnlockPatch.cs to show live progress on its hub button -- see
        /// both for the real, explicit user request this exists for. Deliberately
        /// excludes Free's own completion (IsLevelCompleted's special-cased "treated
        /// as completed the moment it's unlocked" for it -- see that method's own
        /// comment) so playing Free could never count toward its own requirement.
        /// </summary>
        public int CountOtherLevelsCompleted()
        {
            int count = 0;
            foreach (LevelEntry level in LevelCatalog.Levels)
            {
                if (level.Order == LevelCatalog.Levels.Count)
                {
                    continue;
                }

                // Real, explicit user request (ExcludeSlowLevels): an excluded level was
                // never a real check the player could send in the first place (see
                // CheckLocation) -- it shouldn't count toward Free's "other levels
                // completed" requirement any more than Free's own play counts toward
                // itself, immediately above.
                if (_connection.IsLevelExcluded(level.Order))
                {
                    continue;
                }

                if (IsLevelCompleted(level.LevelId))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
