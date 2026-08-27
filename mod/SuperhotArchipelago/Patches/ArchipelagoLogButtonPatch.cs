using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, explicit user request (Notifications feature): "a log section in the hub to
    /// see older notifications". Sits alongside CONNECT (ConnectionButtonPatch.cs) and
    /// AP MODE (ArchipelagoModeTogglePatch.cs) -- same "one file per distinct concern"
    /// reasoning as those two's own class docs.
    ///
    /// Unlike those two buttons, this one has no live status text of its own to keep
    /// refreshed every frame -- it's a static label that just opens
    /// Core/ArchipelagoLogApp.cs (SHGUI.current.AddViewOnTop, same mechanism
    /// ConnectionButtonPatch.cs uses for ArchipelagoConnectApp) -- so there's no
    /// RefreshLabel()/Mod.OnUpdate() wiring needed here at all.
    ///
    /// Real bug found by a live playtest: the button rendered completely blank (just a
    /// stray cursor-like artifact where the label should be). Root cause, confirmed via
    /// decompile: SHGUIcommanderbutton.RefreshText() unconditionally does
    /// ButtonText.Substring(0, ButtonText.IndexOf('│')) to split the label from its
    /// status suffix -- every other button in the game (including this mod's other
    /// two) always includes that '│' separator, but this one originally didn't, so
    /// IndexOf returned -1 and the Substring call threw. RefreshText() swallows that
    /// exception silently (bare catch (Exception) {}), so the button's text fields
    /// were simply never populated, with no error surfaced anywhere. Fixed by matching
    /// the same PadRight(12) + '│' + suffix convention ConnectionButtonPatch.cs and
    /// ArchipelagoModeTogglePatch.cs already use -- "VIEW" as a static suffix since
    /// this button has no live status to show.
    ///
    /// Round 27 follow-up, real explicit user request ("put all of the archipelago
    /// selections that are in the hub into a folder"): this class no longer hooks
    /// piOsMenu.CreateViewFromNode directly or adds itself to the hub's root view --
    /// Patches/ArchipelagoFolderButtonPatch.cs now owns the one root-view hook, and calls
    /// AddTo() below to place this button inside its own "ARCHIPELAGO" subfolder view
    /// instead. Label/behavior otherwise unchanged.
    /// </summary>
    public static class ArchipelagoLogButtonPatch
    {
        private const string ButtonLabel = "AP LOG";

        /// <summary>
        /// Builds this button and adds it to whatever view the caller passes in --
        /// Patches/ArchipelagoFolderButtonPatch.cs's own "ARCHIPELAGO" subfolder view, not
        /// the hub's root anymore. See class doc for why this changed from a direct
        /// CreateViewFromNode Postfix.
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
