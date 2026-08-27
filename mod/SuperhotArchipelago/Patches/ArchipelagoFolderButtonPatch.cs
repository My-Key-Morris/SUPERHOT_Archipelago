using System;
using System.Collections.Generic;
using System.Xml.Linq;
using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, explicit user request: "put all of the archipelago selections that are in the
    /// hub into a folder that then has all of the archipelago stuff in it" -- before this,
    /// CONNECT (ConnectionButtonPatch.cs), AP MODE (ArchipelagoModeTogglePatch.cs), and
    /// AP LOG (ArchipelagoLogButtonPatch.cs) each added their own button straight to the
    /// hub's root view, three separate top-level entries. This class is now the ONLY one of
    /// the four that still hooks piOsMenu.CreateViewFromNode's root-view Postfix -- it adds
    /// a single "ARCHIPELAGO" folder button, and the other three build their buttons into a
    /// subfolder view opened from here instead (via their own AddTo(SHGUIcommanderview)
    /// methods, which used to be their Postfix bodies).
    ///
    /// The subfolder itself is a plain `new SHGUIcommanderview()`, not something built
    /// through the native FolderStructure XML CreateViewFromNode normally reads -- confirmed
    /// via decompile that SHGUIcommanderview's constructor is fully self-contained (draws
    /// its own borders/clock/path line/right-panel, no dependency on the XML-driven
    /// machinery at all), and that CreateViewFromNode's own folder-navigation buttons (e.g.
    /// "LEVELS") do nothing more exotic than build one of these by hand and end with
    /// `SHGUI.current.AddViewOnTop(view)` -- so hand-building one here needs no reflection
    /// into piOsMenu's private CreateViewFromNode at all, just the same public
    /// SHGUIcommanderview/SHGUIcommanderbutton APIs this mod's other hub buttons already
    /// use. The "go up" button mirrors the exact one CreateViewFromNode itself adds to every
    /// non-root view it builds (SHGUI.current.PopView(), confirmed via decompile) -- Escape
    /// also works to back out, for free, since this is the same native SHGUIcommanderview
    /// class real folders like "LEVELS" use, not a subclass with different input handling.
    /// Its suffix ("&lt;UP-FOL&gt;") is the real native MENU_UPFOL8CHARS string, extracted
    /// directly from the game's own English localization data (not guessed) -- real,
    /// explicit user request: match the format every other folder's own "go up" button
    /// already uses.
    ///
    /// Round 28 follow-up, two real explicit user requests:
    ///
    /// 1. "Put archipelago above settings, just so users don't have to scroll down to
    /// interact with it." Root cause: this Postfix runs after piOsMenu.CreateViewFromNode's
    /// whole method body, which has already appended every native root button (LEVELS,
    /// ENDLESS, CHALLENGES, MODS, several app shortcuts, then SETTINGS last) -- extracted
    /// and confirmed directly from the game's own FolderStructure XML asset (not guessed).
    /// AddButtonView's own logic (confirmed via decompile) always appends to the end of
    /// SHGUIcommanderview's public `buttons` list, so this button used to land after
    /// SETTINGS -- the very last entry on the whole screen. InsertAboveSettings() below
    /// moves it: finds SETTINGS by its real, confirmed button text ("SETTINGS", extracted
    /// directly from the game's own English localization data, not guessed), splices this
    /// button in right before it, and recomputes every button's `y` (screen row) from
    /// AddButtonView's own documented formula (row = list index + 1) so the list stays a
    /// clean, gap-free sequence. Falls back to leaving the button wherever AddButtonView put
    /// it if SETTINGS can't be found (e.g. a non-English build) -- never worse than the
    /// pre-existing behavior.
    ///
    /// 2. "Name it &gt;Folder&lt; instead of just folder so it matches the other folders."
    /// Confirmed directly from the game's own English localization data (not guessed): every
    /// native folder's suffix (MENU_FOLDER8CHARS) is literally "&gt;FOLDER&lt;", not "FOLDER"
    /// -- this mod's other buttons already hardcode their own English suffix text rather
    /// than calling into the native localization system (see ConnectionButtonPatch.cs etc.),
    /// so this just matches that exact real string instead of guessing at one.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "CreateViewFromNode",
        new[] { typeof(XElement), typeof(List<int>), typeof(List<int>), typeof(float), typeof(bool) })]
    public static class ArchipelagoFolderButtonPatch
    {
        private const string ButtonLabel = "ARCHIPELAGO";

        // Real button text for the native SETTINGS folder, confirmed directly from the
        // game's own English localization data (MENU_Settings -> "SETTINGS") -- see class
        // doc. Used to find where to insert this button; matched as a prefix since the real
        // button text is this padded to 12 chars plus "│>FOLDER<".
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
                "Connect to a server, toggle Archipelago mode, or view your check/item history.");

            ___createdView.AddButtonView(button);
            InsertAboveSettings(___createdView);
        }

        /// <summary>
        /// Moves this button (just appended to the end by AddButtonView above) to sit right
        /// before the native SETTINGS folder button instead -- see class doc's Round 28
        /// follow-up #1 for the full reasoning. No-ops (leaving the button wherever
        /// AddButtonView put it) if SETTINGS can't be found.
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

            // AddButtonView derives each button's row purely from its list index at the
            // moment it's appended (button.y = buttons.Count - 1 + 1) -- moving this one out
            // of append order means every row needs recomputing from that same formula, or
            // the list would render with a gap/overlap. Cheap enough (a few dozen buttons at
            // most) to just redo the whole list rather than work out a partial range.
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].y = i + 1;
            }
        }

        /// <summary>
        /// Builds and pushes the "ARCHIPELAGO" subfolder view -- see class doc for why this
        /// is a hand-built SHGUIcommanderview rather than anything routed back through
        /// piOsMenu's own XML-driven view builder.
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

            SHGUI.current.AddViewOnTop(folderView);
        }
    }
}
