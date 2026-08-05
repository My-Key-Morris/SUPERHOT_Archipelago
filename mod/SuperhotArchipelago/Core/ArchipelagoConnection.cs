using System;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using MelonLoader;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Thin wrapper around the official Archipelago.MultiClient.Net session. Owns the
    /// WebSocket connection to the AP server; ArchipelagoConnection itself never touches
    /// game/Unity state -- that's LocationManager/ItemManager's job.
    /// </summary>
    public class ArchipelagoConnection
    {
        private readonly MelonLogger.Instance _log;
        public ArchipelagoSession? Session { get; private set; }
        public bool IsConnected { get; private set; }

        // Real, explicit user request: Core/ArchipelagoConnectApp.cs surfaces connection
        // problems in-game instead of only in the (easy to miss) MelonLoader console.
        // Cleared at the start of every Connect() attempt, set on failure so the UI has
        // something more specific than "Not connected" to show.
        public string? LastError { get; private set; }

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

            // TODO: itemsHandlingFlags -- AllItems is the simplest starting point (get told
            // about every item including our own), can be narrowed later.
            //
            // The whole call is wrapped in a try/catch (needed once connecting became
            // reachable from in-game text entry, see Core/ArchipelagoConnectApp.cs):
            // TryConnectAndLogin can throw outright on some bad inputs (e.g. a malformed
            // server string) rather than just returning an unsuccessful result -- without
            // this, an in-game typo could throw past this method's caller and take down
            // more than just this one connect attempt.
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
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _log.Error($"Failed to connect to Archipelago server '{server}': {ex.Message}");
                return;
            }

            IsConnected = true;
            _log.Msg($"Connected to Archipelago as '{slotName}'.");
            Connected?.Invoke();
        }
    }
}
