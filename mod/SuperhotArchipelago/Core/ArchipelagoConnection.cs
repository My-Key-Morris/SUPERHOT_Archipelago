using System;
using System.Collections.Generic;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using MelonLoader;
using Newtonsoft.Json.Linq;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Thin wrapper around the Archipelago.MultiClient.Net session, owning the WebSocket connection.
    /// Never touches game/Unity state itself -- that's LocationManager/ItemManager's job.
    /// </summary>
    public class ArchipelagoConnection
    {
        private readonly MelonLogger.Instance _log;
        public ArchipelagoSession? Session { get; private set; }
        public bool IsConnected { get; private set; }

        // Surfaces connection problems in-game (ArchipelagoConnectApp.cs) instead of only in the
        // MelonLoader console; cleared at the start of every Connect(), set on failure.
        public string? LastError { get; private set; }

        // How many other levels must be completed before "34 - Free" opens (enforced in
        // LevelAccessGuard.cs), sourced from slot data / the player's YAML. The 25 fallback matches
        // Options.py's own default so the two can't drift apart.
        public int LevelsRequiredForFree { get; private set; } = 25;

        // Used by ItemManager.cs with a short grace window to tell the server's item-history replay
        // (which fires through the same ItemReceived event on every reconnect) apart from a genuinely
        // live item, since the client library doesn't guarantee AllItemsReceived's population timing.
        public DateTime ConnectedAtUtc { get; private set; }

        // Which levels Options.py's ExcludeSlowLevels toggle removed from the pool, learned from slot
        // data rather than hardcoding a copy that could drift out of sync. Keyed by LevelEntry.Order,
        // matching what fill_slot_data() sends; empty by default for older apworld versions.
        public HashSet<int> ExcludedLevelOrders { get; private set; } = new();

        public event Action? Connected;

        public ArchipelagoConnection(MelonLogger.Instance log)
        {
            _log = log;
        }

        public void Connect(string server, string slotName, string? password = null)
        {
            LastError = null;
            IsConnected = false;

            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(slotName))
            {
                LastError = "Server and Slot are both required.";
                _log.Warning($"Not connecting: {LastError}");
                return;
            }

            Session = ArchipelagoSessionFactory.CreateSession(server);

            // TODO: itemsHandlingFlags -- AllItems is the simplest starting point (get told about every item
            // including our own), can be narrowed later.
            //
            // Wrapped in try/catch since TryConnectAndLogin can throw outright on bad input (e.g. a malformed
            // server string) rather than just returning an unsuccessful result.
            try
            {
                var result = Session.TryConnectAndLogin(
                    "SUPERHOT",
                    slotName,
                    ItemsHandlingFlags.AllItems,
                    password: password
                );

                if (!result.Successful)
                {
                    LastError = "Login failed -- check server address, slot name, and password.";
                    _log.Error($"Failed to connect to Archipelago server: {server}");
                    return;
                }

                // TryConnectAndLogin defaults requestSlotData to true, so a successful result here is always a
                // LoginSuccessful with slot data -- the "is" pattern match is just the type-safe way to reach it.
                if (result is LoginSuccessful success &&
                    success.SlotData.TryGetValue("levels_required_for_free", out object? rawRequired))
                {
                    // Comes back as a boxed numeric type via Newtonsoft.Json -- Convert.ToInt32 handles that
                    // instead of risking an InvalidCastException on a direct (int) cast.
                    LevelsRequiredForFree = Convert.ToInt32(rawRequired);
                    _log.Msg($"Slot data: {LevelsRequiredForFree} other levels required to enter '34 - Free'.");
                }
                else
                {
                    _log.Warning("Slot data had no 'levels_required_for_free' -- falling back to " +
                        $"{LevelsRequiredForFree}. Expected if this room was generated from an " +
                        "apworld version older than this feature.");
                }

                // Comes back as a JArray via Newtonsoft.Json, same boxing pattern as levels_required_for_free
                // above -- ToObject<List<int>>() converts each element.
                if (result is LoginSuccessful excludedSuccess &&
                    excludedSuccess.SlotData.TryGetValue("excluded_level_orders", out object? rawExcluded) &&
                    rawExcluded is JArray excludedArray)
                {
                    ExcludedLevelOrders = new HashSet<int>(excludedArray.ToObject<List<int>>() ?? new List<int>());
                    if (ExcludedLevelOrders.Count > 0)
                    {
                        _log.Msg($"Slot data: {ExcludedLevelOrders.Count} level(s) excluded from " +
                            "tracking (ExcludeSlowLevels) -- always unlocked, no checks sent for them.");
                    }
                }
                else
                {
                    ExcludedLevelOrders = new HashSet<int>();
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _log.Error($"Failed to connect to Archipelago server '{server}': {ex.Message}");
                return;
            }

            IsConnected = true;
            ConnectedAtUtc = DateTime.UtcNow;

            // Clears right before Connected fires and the managers rebuild their half from server state, so
            // every reconnect ends at a clean picture rather than a stale or duplicated one.
            NotificationLog.Clear();

            // UnlockState is a static set that used to never get cleared, so a previous room's unlocks bled
            // into the next connection -- cleared here, then ItemManager.OnConnected() repopulates it from
            // the new room's own history.
            UnlockState.Clear();

            _log.Msg($"Connected to Archipelago as '{slotName}'.");

            // .NET's multicast delegate invocation is NOT fault-isolated -- if one subscriber throws, every
            // subscriber after it in the list is simply never invoked. Invoking each subscriber individually
            // with its own try/catch (here) prevents LocationManager's OnConnected failing from silently
            // skipping ItemManager's, which is what repopulates UnlockState from the server's item replay.
            if (Connected != null)
            {
                foreach (Delegate handler in Connected.GetInvocationList())
                {
                    try
                    {
                        ((Action)handler)();
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"A Connected event handler threw and was skipped: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// Whether this room's slot data marked the level at this Order as excluded (see
        /// Options.py's ExcludeSlowLevels). Callers pass a LevelEntry's own Order field.
        /// </summary>
        public bool IsLevelExcluded(int order) => ExcludedLevelOrders.Contains(order);

        // Lets turning Archipelago mode off actually drop the connection instead of leaving the socket
        // open in the background. Fire-and-forget since awaiting would require this method to become
        // async just to close a socket, with no game/Unity state worth blocking the main thread for.
        public void Disconnect()
        {
            if (Session == null)
            {
                IsConnected = false;
                return;
            }

            try
            {
                _ = Session.Socket.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _log.Warning($"Error while disconnecting from the Archipelago server: {ex.Message}");
            }

            Session = null;
            IsConnected = false;
            _log.Msg("Disconnected from the Archipelago server.");
        }
    }
}
