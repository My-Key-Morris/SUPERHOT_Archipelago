using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Adds an "AP MODE" hub button to toggle Archipelago mode on/off without
    /// uninstalling the mod (Mod.SetEnabled). Mirrors ConnectionButtonPatch.cs's
    /// live-label-refresh pattern. Note: toggling doesn't retroactively re-scramble
    /// buttons in an already-open "LEVELS" view (it re-syncs on next visit) -- actual
    /// launch gating is unaffected since that's checked live, not baked into the view.
    /// </summary>
    public static class ArchipelagoModeTogglePatch
    {
        private const string ButtonLabel = "AP MODE";

        // Not cleared on view teardown -- SHGUIview.Kill() just fades a plain C# object,
        // it can't throw on further access.
        internal static SHGUIcommanderbutton? Button { get; private set; }

        // Skips the rebuild below when enabled-state hasn't actually changed since
        // RefreshLabel runs every frame. Reset to null on a new button so its first
        // refresh always applies.
        private static bool? _lastEnabled;

        /// <summary>
        /// Builds this button into the given view (the "ARCHIPELAGO" subfolder built by
        /// ArchipelagoFolderButtonPatch.cs), not the hub root.
        /// </summary>
        internal static void AddTo(SHGUIcommanderview view)
        {
            SHGUIcommanderbutton button = new SHGUIcommanderbutton(BuildLabel(), 'w', delegate
            {
                Mod.SetEnabled(!Config.Enabled.Value);
            }).SetListLink(view).SetData(
                "Turn Archipelago mode on or off. Off plays SUPERHOT exactly like vanilla " +
                "-- no level gating, no hub overlay -- and drops any active connection. " +
                "Turning back on reconnects and picks up right where you left off.");

            view.AddButtonView(button);

            Button = button;
            _lastEnabled = null;
            RefreshLabel();
        }

        /// <summary>
        /// Recomputes and applies the button's ON/OFF text from live config state. Called
        /// once at creation and every frame from Mod.OnUpdate() to stay in sync.
        /// </summary>
        internal static void RefreshLabel()
        {
            if (Button == null)
            {
                return;
            }

            bool enabled = Config.Enabled.Value;
            if (enabled == _lastEnabled)
            {
                return;
            }

            _lastEnabled = enabled;
            Button.ButtonText = BuildLabel();
            Button.RefreshText();
        }

        private static string BuildLabel()
        {
            string status = Config.Enabled.Value ? "ON" : "OFF";
            return ButtonLabel.PadRight(12, ' ') + "│" + status;
        }
    }
}
