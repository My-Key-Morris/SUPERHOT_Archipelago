using BepInEx.Configuration;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Adds a "SETTINGS" folder (mirroring the native hub's own SETTINGS folder) inside the
    /// ARCHIPELAGO folder, with one ON/OFF toggle per item classification (Config.Notify*)
    /// controlling whether that classification interrupts play with a popup. AP LOG always
    /// records every item regardless of these toggles -- see Config.ShouldNotify.
    /// </summary>
    public static class NotificationSettingsButtonPatch
    {
        private const string ButtonLabel = "SETTINGS";

        /// <summary>
        /// Builds this button into the given view (the "ARCHIPELAGO" subfolder built by
        /// ArchipelagoFolderButtonPatch.cs), not the hub root.
        /// </summary>
        internal static void AddTo(SHGUIcommanderview parentView)
        {
            SHGUIcommanderbutton button = new SHGUIcommanderbutton(
                ButtonLabel.PadRight(12, ' ') + "│>FOLDER<", 'w', delegate
            {
                OpenFolder();
            }).SetListLink(parentView).SetData(
                "Choose which item classes pop up during play. Everything still shows up " +
                "in AP LOG regardless of these settings.");

            parentView.AddButtonView(button);
        }

        private static void OpenFolder()
        {
            SHGUIcommanderview folderView = new SHGUIcommanderview
            {
                isRoot = false,
                path = "C:\\ARCHIPELAGO\\SETTINGS\\",
            };

            SHGUIcommanderbutton upButton = new SHGUIcommanderbutton(
                "/..         │" + "<UP-FOL>", 'w', delegate
            {
                SHGUI.current.PopView();
            }).SetListLink(folderView).SetData("Go back.");
            folderView.AddButtonView(upButton);

            AddToggle(folderView, "PROGRESSION", Config.NotifyProgression,
                "Popup when a progression item is sent or received.");
            AddToggle(folderView, "USEFUL", Config.NotifyUseful,
                "Popup when a useful item is sent or received.");
            AddToggle(folderView, "NORMAL", Config.NotifyFiller,
                "Popup for normal (filler) items -- most checks.");
            AddToggle(folderView, "TRAP", Config.NotifyTrap,
                "Popup when a trap item is sent or received.");

            SHGUI.current.AddViewOnTop(folderView);
        }

        /// <summary>
        /// One ON/OFF button bound directly to a bool preference. No per-frame refresh
        /// wiring needed (unlike CONNECT/AP MODE) since nothing but this button's own click
        /// ever changes these values.
        /// </summary>
        private static void AddToggle(SHGUIcommanderview view, string label, ConfigEntry<bool> pref, string description)
        {
            SHGUIcommanderbutton button = null!;
            button = new SHGUIcommanderbutton(BuildLabel(label, pref.Value), 'w', delegate
            {
                pref.Value = !pref.Value;
                Config.Save();
                button.ButtonText = BuildLabel(label, pref.Value);
                button.RefreshText();
            }).SetListLink(view).SetData(description);

            view.AddButtonView(button);
        }

        private static string BuildLabel(string label, bool value) =>
            label.PadRight(12, ' ') + "│" + (value ? "ON" : "OFF");
    }
}
