using System;
using System.Collections.Generic;
using System.Xml.Linq;
using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Adds a single "ARCHIPELAGO" folder button to the hub root (the only class still
    /// hooking piOsMenu.CreateViewFromNode's root Postfix); CONNECT/AP MODE/AP LOG/SETTINGS
    /// build themselves into the subfolder opened from here instead of the root. The
    /// subfolder is a hand-built SHGUIcommanderview (same approach native folders like
    /// "LEVELS" use) so it needs no XML/reflection hookup. Inserted just above the hub's own
    /// native SETTINGS button, with rows recomputed afterward since AddButtonView only
    /// appends to the end of the list.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "CreateViewFromNode",
        new[] { typeof(XElement), typeof(List<int>), typeof(List<int>), typeof(float), typeof(bool) })]
    public static class ArchipelagoFolderButtonPatch
    {
        private const string ButtonLabel = "ARCHIPELAGO";

        // Native SETTINGS button's text prefix, used to find where to insert this button.
        private const string SettingsButtonPrefix = "SETTINGS";

        public static void Postfix(SHGUIcommanderview ___createdView)
        {
            if (___createdView == null || !___createdView.isRoot)
            {
                return;
            }

            SHGUIcommanderbutton button = new SHGUIcommanderbutton(
                ButtonLabel.PadRight(12, ' ') + "│>FOLDER<", 'w', delegate
            {
                OpenFolder();
            }).SetListLink(___createdView).SetData(
                "Connect to a server, toggle Archipelago mode, view your check/item history, " +
                "or configure notification settings.");

            ___createdView.AddButtonView(button);
            InsertAboveSettings(___createdView);
        }

        /// <summary>
        /// Moves this button from the end of the list to just before SETTINGS. No-ops if
        /// SETTINGS can't be found (e.g. non-English build).
        /// </summary>
        private static void InsertAboveSettings(SHGUIcommanderview view)
        {
            List<SHGUIcommanderbutton> buttons = view.buttons;
            int lastIndex = buttons.Count - 1;
            int settingsIndex = buttons.FindIndex(b => b.ButtonText.StartsWith(SettingsButtonPrefix, StringComparison.Ordinal));
            if (settingsIndex < 0 || settingsIndex >= lastIndex)
            {
                return;
            }

            SHGUIcommanderbutton button = buttons[lastIndex];
            buttons.RemoveAt(lastIndex);
            buttons.Insert(settingsIndex, button);

            // Button row (y) is derived from list index, so reordering requires
            // recomputing every row or the list renders with a gap/overlap.
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].y = i + 1;
            }
        }

        /// <summary>
        /// Builds and pushes the "ARCHIPELAGO" subfolder view.
        /// </summary>
        private static void OpenFolder()
        {
            SHGUIcommanderview folderView = new SHGUIcommanderview
            {
                isRoot = false,
                path = "C:\\ARCHIPELAGO\\",
            };

            SHGUIcommanderbutton upButton = new SHGUIcommanderbutton(
                "/..         │" + "<UP-FOL>", 'w', delegate
            {
                SHGUI.current.PopView();
            }).SetListLink(folderView).SetData("Go back.");
            folderView.AddButtonView(upButton);

            ConnectionButtonPatch.AddTo(folderView);
            ArchipelagoModeTogglePatch.AddTo(folderView);
            ArchipelagoLogButtonPatch.AddTo(folderView);
            NotificationSettingsButtonPatch.AddTo(folderView);

            SHGUI.current.AddViewOnTop(folderView);
        }
    }
}
