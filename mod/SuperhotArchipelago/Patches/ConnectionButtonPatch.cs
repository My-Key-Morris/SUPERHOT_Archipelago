using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Adds a "CONNECT" hub button that opens Core/ArchipelagoConnectApp.cs via
    /// AddViewOnTop. Its click delegate needs no LevelInfo and bypasses level-launch
    /// gating entirely, since SHGUIcommanderbutton's click handler is a generic delegate,
    /// not hardcoded to level launches. Keeps a live Button reference and refreshes its
    /// ONLINE/OFFLINE label every frame (Mod.OnUpdate), because the hub's root view is
    /// only rebuilt on a fresh visit, not when reopening from a popped sub-view.
    /// </summary>
    public static class ConnectionButtonPatch
    {
        private const string ButtonLabel = "CONNECT";

        // Not cleared on view teardown -- see class doc for why that's unnecessary here.
        internal static SHGUIcommanderbutton? Button { get; private set; }

        // Skips the rebuild below when the status hasn't actually changed since
        // RefreshLabel runs every frame but the connection state rarely does. Reset to
        // null on a new button so its first refresh always applies.
        private static string? _lastStatus;

        /// <summary>
        /// Builds this button into the given view (the "ARCHIPELAGO" subfolder built by
        /// ArchipelagoFolderButtonPatch.cs), not the hub root.
        /// </summary>
        internal static void AddTo(SHGUIcommanderview view)
        {
            SHGUIcommanderbutton button = new SHGUIcommanderbutton(ButtonLabel.PadRight(12, ' ') + "│OFFLINE", 'w', delegate
            {
                SHGUI.current.AddViewOnTop(new ArchipelagoConnectApp());
            }).SetListLink(view).SetData(
                "Set up or check your Archipelago server connection.");

            view.AddButtonView(button);

            Button = button;
            _lastStatus = null;
            RefreshLabel();
        }

        /// <summary>
        /// Recomputes and applies the button's ONLINE/OFFLINE text from live connection
        /// state. Called once at creation and every frame from Mod.OnUpdate() to stay in sync.
        /// </summary>
        internal static void RefreshLabel()
        {
            if (Button == null)
            {
                return;
            }

            bool connected = Mod.Connection?.IsConnected ?? false;
            string status = connected ? "ONLINE" : "OFFLINE";
            if (status == _lastStatus)
            {
                return;
            }

            _lastStatus = status;
            Button.ButtonText = ButtonLabel.PadRight(12, ' ') + "│" + status;
            Button.RefreshText();
        }
    }
}
