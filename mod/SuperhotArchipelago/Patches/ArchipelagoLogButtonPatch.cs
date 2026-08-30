using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Adds an "AP LOG" hub button that opens Core/ArchipelagoLogApp.cs. Unlike CONNECT/AP
    /// MODE it has no live status text, so no per-frame refresh is needed. The button text
    /// must still include a '│' separator + suffix ("VIEW") or SHGUIcommanderbutton.RefreshText()
    /// throws (silently) and the button renders blank.
    /// </summary>
    public static class ArchipelagoLogButtonPatch
    {
        private const string ButtonLabel = "AP LOG";

        /// <summary>
        /// Builds this button into the given view (the "ARCHIPELAGO" subfolder built by
        /// ArchipelagoFolderButtonPatch.cs), not the hub root.
        /// </summary>
        internal static void AddTo(SHGUIcommanderview view)
        {
            SHGUIcommanderbutton button = new SHGUIcommanderbutton(
                ButtonLabel.PadRight(12, ' ') + "│VIEW", 'w', delegate
            {
                SHGUI.current.AddViewOnTop(new ArchipelagoLogApp());
            }).SetListLink(view).SetData(
                "See a history of items you've received and checks you've sent this run.");

            view.AddButtonView(button);
        }
    }
}
