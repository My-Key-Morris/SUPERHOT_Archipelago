using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using BepInEx.Logging;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Maps in-game level-complete events to Archipelago location checks via CheckLocation(levelId),
    /// looked up by level id rather than scene name (some levels reuse scenes, e.g. "Cyberspace (1)").
    /// </summary>
    public class LocationManager
    {
        private readonly ArchipelagoConnection _connection;
        private readonly ManualLogSource _log;

        // Item/receiver for a "Sent X to Y" notification aren't known until ScoutLocationsAsync
        // resolves them off the main thread, so results are queued here for ProcessPendingNotifications()
        // to consume; OrderKey lets historical entries be inserted next to their matching item-received
        // entry instead of always landing at the end (see NotificationLog._entryOrderKeys).
        private readonly ConcurrentQueue<(LogSegment[] Segments, string? PopupText, long OrderKey)> _pendingNotifications = new();

        public LocationManager(ArchipelagoConnection connection, ManualLogSource log)
        {
            _connection = connection;
            _log = log;
            _connection.Connected += OnConnected;
        }

        /// <summary>
        /// On every connect, re-scouts this slot's full server-recorded history (AllLocationsChecked)
        /// and rebuilds the "sent" half of the log from scratch, log-only with no popups since it's
        /// history rather than something that just happened live.
        /// </summary>
        private void OnConnected()
        {
            // Defense in depth: ArchipelagoConnection already isolates subscribers with try/catch, but this
            // ensures a failure here can never affect ItemManager's OnConnected, which repopulates UnlockState.
            try
            {
                OnConnectedCore();
            }
            catch (System.Exception ex)
            {
                _log.LogError($"LocationManager.OnConnected failed, notification history resync " +
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
                    _log.LogWarning("Failed to scout this slot's already-checked locations -- " +
                        "the AP LOG screen won't show full history until the next successful reconnect.");
                    return;
                }

                foreach (KeyValuePair<long, ScoutedItemInfo> kvp in task.Result)
                {
                    // Per-item try/catch so one bad scouted entry can't abort the loop and lose every entry after it.
                    try
                    {
                        string itemName = LevelCatalog.TryGetShortItemDisplayName(kvp.Value.ItemId) ?? kvp.Value.ItemDisplayName;

                        // Uses the item's real Flags (not an id check) so coloring works for any game in the multiworld.
                        char itemColor = NotificationColors.ForItemFlags(kvp.Value.Flags);

                        // Merges sends to self into one "X found their Y" line instead of separate Sent/Received lines --
                        // same check as ScoutAndQueueSentNotification, needed here too for the history-resync path.
                        bool isSelfSend = _connection.Session != null
                            && kvp.Value.Player.Slot == _connection.Session.ConnectionInfo.Slot;

                        LogSegment[] segments = isSelfSend
                            ? new[]
                              {
                                  new LogSegment(kvp.Value.Player.Alias, NotificationColors.Player),
                                  new LogSegment(" found their ", NotificationColors.Default),
                                  new LogSegment(itemName, itemColor),
                              }
                            : new[]
                              {
                                  new LogSegment("Sent ", NotificationColors.Default),
                                  new LogSegment(itemName, itemColor),
                                  new LogSegment(" to ", NotificationColors.Default),
                                  new LogSegment(kvp.Value.Player.Alias, NotificationColors.Player),
                                  new LogSegment($" from {ResolveLocationDisplayText(kvp.Key)}", NotificationColors.Default),
                              };

                        // kvp.Key (the location id) is the same sort-key scale ItemManager.Notify() uses, so this entry
                        // lands beside the matching "Received" entry.
                        _pendingNotifications.Enqueue((segments, null, kvp.Key));
                    }
                    catch (System.Exception ex)
                    {
                        _log.LogError($"Processing one scouted historical location threw -- it may be " +
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
                NotificationLog.Add(pending.Segments, pending.PopupText, pending.OrderKey);
            }
        }

        public void CheckLocation(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                _log.LogWarning($"CheckLocation({levelId}) called before connecting -- ignored.");
                return;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                _log.LogWarning($"Unknown level id {levelId} -- no matching entry in levels.json. " +
                              "Either a level outside our catalog just finished, or levels.json's " +
                              "order/id list is out of sync with the real game.");
                return;
            }

            // The final level has no real location (apworld/superhot/Locations.py doesn't create one for it;
            // goal completion is reported separately via SetGoalAchieved), and an excluded level
            // (ExcludeSlowLevels) never had a real location either -- so both are skipped here.
            if (level.Order != LevelCatalog.Levels.Count && !_connection.IsLevelExcluded(level.Order))
            {
                long locationId = LevelCatalog.BaseId + level.Order;

                // Levels stay replayable and this hook can fire more than once per real completion, so this
                // checks AllLocationsChecked (updated instantly on CompleteLocationChecks) first to guard
                // against resending a check or duplicate notification.
                if (_connection.Session.Locations.AllLocationsChecked.Contains(locationId))
                {
                    _log.LogInfo($"'{level.DisplayName}' already checked -- skipping duplicate check/notification.");
                }
                else
                {
                    _connection.Session.Locations.CompleteLocationChecks(locationId);
                    _log.LogInfo($"Sent check for '{level.DisplayName}' (level id {levelId}, location id {locationId}).");

                    // A sent check is always a genuinely live event (only fires from real gameplay), so it always
                    // gets a popup, not just a log entry.
                    ScoutAndQueueSentNotification(locationId, level.DisplayName, isLive: true);
                }
            }

            // Sending the final level's check isn't the same as telling the server the player won -- AP's
            // Victory item has no network id and never arrives as a received item, so goal completion must be
            // reported explicitly via SetGoalAchieved() here.
            if (level.Order == LevelCatalog.Levels.Count)
            {
                _connection.Session.SetGoalAchieved();
                _log.LogInfo($"'{level.DisplayName}' was the final level -- reported goal achieved.");

                // Unlocks the native MODS folder, which requires SaveManager's "storyFinished" flag -- normally
                // only set by "22 - Hacker"'s scripted ending scene, which an AP run may skip or never revisit.
                // Setting it explicitly here guarantees it's granted once the real game is finished via AP.
                SaveManager.Instance.SetValue("storyFinished", true);
            }
        }

        /// <summary>
        /// Reports finding a level's secret console (see Patches/SecretFoundPatch.cs). Uses a distinct
        /// location id range (LevelCatalog.SecretLocationIdOffset) so it's a separate checkable location
        /// from the level's own completion.
        /// </summary>
        public void CheckSecretLocation(int levelId)
        {
            if (_connection.Session == null || !_connection.IsConnected)
            {
                _log.LogWarning($"CheckSecretLocation({levelId}) called before connecting -- ignored.");
                return;
            }

            if (!LevelCatalog.LevelIdToLevel.TryGetValue(levelId, out LevelEntry? level))
            {
                _log.LogWarning($"Unknown level id {levelId} for a secret find -- no matching entry in " +
                              "levels.json. Either a level outside our catalog has a secret we don't " +
                              "know about, or levels.json's order/id list is out of sync with the real game.");
                return;
            }

            if (!level.HasSecret)
            {
                _log.LogWarning($"Secret found in '{level.DisplayName}', but levels.json says this level " +
                              "has none -- sending the check anyway, but this likely means levels.json's " +
                              "hasSecret is out of sync with the real game.");
            }

            // An excluded level's secret location doesn't exist either (see CheckLocation's matching check).
            if (_connection.IsLevelExcluded(level.Order))
            {
                _log.LogInfo($"'{level.DisplayName}' secret found, but this level is excluded from " +
                    "tracking (ExcludeSlowLevels) -- no check to send.");
                return;
            }

            long locationId = LevelCatalog.BaseId + LevelCatalog.SecretLocationIdOffset + level.Order;

            // Same guard as CheckLocation -- a secret can be revisited/re-detected, so this stops a duplicate
            // check or notification.
            if (_connection.Session.Locations.AllLocationsChecked.Contains(locationId))
            {
                _log.LogInfo($"'{level.DisplayName}' secret already checked -- skipping duplicate check/notification.");
                return;
            }

            _connection.Session.Locations.CompleteLocationChecks(locationId);
            _log.LogInfo($"Sent secret check for '{level.DisplayName}' (level id {levelId}, location id {locationId}).");

            // Always a genuinely live event, like CheckLocation's own call. Appending "Secret" distinguishes
            // this from a main-completion check in the log's "from X" suffix.
            ScoutAndQueueSentNotification(locationId, $"{level.DisplayName} Secret", isLive: true);
        }

        /// <summary>
        /// Scouts a just-sent check's item/player (unknown locally at send time) off the main thread and
        /// queues the result for ProcessPendingNotifications(). locationDisplayText is the log-only
        /// "from X" suffix on success or the whole fallback message on failure; a self-send (same slot as
        /// us) produces a merged "X found their Y" line instead, matching ItemManager.Notify()'s other half.
        /// </summary>
        private void ScoutAndQueueSentNotification(long locationId, string locationDisplayText, bool isLive)
        {
            if (_connection.Session == null)
            {
                return;
            }

            _connection.Session.Locations.ScoutLocationsAsync(new[] { locationId }).ContinueWith(task =>
            {
                // This continuation runs unawaited on a thread-pool thread -- an uncaught exception here could
                // become an UnobservedTaskException on GC, which can tear down the process on .NET Framework.
                try
                {
                    string popupText;
                    LogSegment[] segments;
                    ItemFlags? scoutedFlags = null;
                    if (!task.IsFaulted && !task.IsCanceled && task.Result != null &&
                        task.Result.TryGetValue(locationId, out ScoutedItemInfo scouted))
                    {
                        scoutedFlags = scouted.Flags;
                        // Uses the mod's shorter catalog item name where recognized, since AP's own name was
                        // getting truncated on the AP LOG screen.
                        string itemName = LevelCatalog.TryGetShortItemDisplayName(scouted.ItemId) ?? scouted.ItemDisplayName;

                        // Same as OnConnectedCore's loop above -- real Flags, not a narrow id check.
                        char itemColor = NotificationColors.ForItemFlags(scouted.Flags);

                        bool isSelfSend = _connection.Session != null
                            && scouted.Player.Slot == _connection.Session.ConnectionInfo.Slot;

                        if (isSelfSend)
                        {
                            // No location suffix here -- a self-send's location is always this player's own
                            // current check, not new information.
                            popupText = $"{scouted.Player.Alias} found their {itemName}";
                            segments = new[]
                            {
                                new LogSegment(scouted.Player.Alias, NotificationColors.Player),
                                new LogSegment(" found their ", NotificationColors.Default),
                                new LogSegment(itemName, itemColor),
                            };
                        }
                        else
                        {
                            popupText = $"Sent {itemName} to {scouted.Player.Alias}";

                            // Location suffix is log-only -- popupText stays short for a quick glance.
                            segments = new[]
                            {
                                new LogSegment("Sent ", NotificationColors.Default),
                                new LogSegment(itemName, itemColor),
                                new LogSegment(" to ", NotificationColors.Default),
                                new LogSegment(scouted.Player.Alias, NotificationColors.Player),
                                new LogSegment($" from {locationDisplayText}", NotificationColors.Default),
                            };
                        }
                    }
                    else
                    {
                        _log.LogWarning($"Failed to scout location {locationId} for a notification -- " +
                            "showing a generic message instead.");
                        popupText = $"Sent check for {locationDisplayText}";
                        segments = new[] { new LogSegment(popupText, NotificationColors.Default) };
                    }

                    // Always live here, so long.MaxValue keeps this a plain chronological append; only the
                    // history-resync path needs a real location-id order key. The log entry (segments) is
                    // always queued; only the popup itself respects the ARCHIPELAGO > SETTINGS filter
                    // toggles below, and only when a real classification was scouted (scoutedFlags).
                    bool showPopup = isLive && (scoutedFlags == null || Config.ShouldNotify(NotificationColors.Classify(scoutedFlags.Value)));
                    _pendingNotifications.Enqueue((segments, showPopup ? popupText : null, long.MaxValue));
                }
                catch (System.Exception ex)
                {
                    _log.LogError($"Building a notification for location {locationId} threw -- it may be " +
                        $"missing a popup/log entry, but nothing else is affected: {ex}");
                }
            });
        }

        /// <summary>
        /// Resolves a checked location id back to a level's display name for the history resync's
        /// log-only "from X" suffix, using this mod's own catalog naming for consistency.
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
        /// Whether this level's check has been sent, distinct from "unlocked" (item received but not
        /// necessarily played) -- used by HubUnlockPatch.cs for grey vs. white. Reads the live
        /// AllLocationsChecked session state directly rather than local tracking, so it can never drift
        /// out of sync with the server and survives a restart.
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

            // The final level has no completion location (see CheckLocation), so there's no server signal to
            // distinguish played/unplayed -- treated as completed the moment it's unlocked instead.
            if (level.Order == LevelCatalog.Levels.Count)
            {
                return UnlockState.IsUnlocked(levelId);
            }

            // Same reasoning as the final level above -- an excluded level has no completion location either,
            // so it's treated as completed to avoid showing permanently grey.
            if (_connection.IsLevelExcluded(level.Order))
            {
                return true;
            }

            long locationId = LevelCatalog.BaseId + level.Order;
            return _connection.Session.Locations.AllLocationsChecked.Contains(locationId);
        }

        /// <summary>
        /// Same idea as IsLevelCompleted, but for a secret console location. The native "CRACKED!" badge
        /// reads straight from SUPERHOT's own save data, which can show stale state from an earlier
        /// playthrough -- this trusts the live Archipelago session instead. Used by HubUnlockPatch.cs and
        /// Mod.cs's OnSceneWasLoaded to correct both the display and the underlying save flag.
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

            // An excluded level's secret location doesn't exist either -- same "always completed" reasoning
            // as IsLevelCompleted, for the secret badge instead of the main one.
            if (_connection.IsLevelExcluded(level.Order))
            {
                return true;
            }

            long locationId = LevelCatalog.BaseId + LevelCatalog.SecretLocationIdOffset + level.Order;
            return _connection.Session.Locations.AllLocationsChecked.Contains(locationId);
        }

        /// <summary>
        /// Every level that counts toward the "34 - Free" gate: all tracked levels except Free itself and
        /// anything ExcludeSlowLevels excluded. Shared by CountOtherLevelsCompleted and
        /// GetLevelsRequiredForFree so they can't disagree on the definition.
        /// </summary>
        private IEnumerable<LevelEntry> OtherTrackedLevels()
        {
            foreach (LevelEntry level in LevelCatalog.Levels)
            {
                if (level.Order == LevelCatalog.Levels.Count)
                {
                    continue;
                }

                if (_connection.IsLevelExcluded(level.Order))
                {
                    continue;
                }

                yield return level;
            }
        }

        /// <summary>
        /// How many other tracked levels have had their completion check sent this run. Used by
        /// LevelAccessGuard.cs to gate entry to Free and by HubUnlockPatch.cs for progress display;
        /// excludes Free itself so it can't count toward its own requirement.
        /// </summary>
        public int CountOtherLevelsCompleted()
        {
            int count = 0;
            foreach (LevelEntry level in OtherTrackedLevels())
            {
                if (IsLevelCompleted(level.LevelId))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Clamps the configured LevelsRequiredForFree down to OtherTrackedLevels().Count(), since the
        /// raw YAML value doesn't account for ExcludeSlowLevels and could otherwise set an unreachable
        /// target -- a permanent softlock. LevelAccessGuard.cs and HubUnlockPatch.cs must call this
        /// instead of reading LevelsRequiredForFree directly.
        /// </summary>
        public int GetLevelsRequiredForFree()
        {
            return System.Math.Min(_connection.LevelsRequiredForFree, OtherTrackedLevels().Count());
        }
    }
}
