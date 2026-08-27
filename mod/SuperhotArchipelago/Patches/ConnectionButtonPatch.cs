using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, explicit user request, revising Core/ConnectionUI.cs's original F2-hotkey
    /// design: instead of a hidden keybind, put the Archipelago connection menu behind a
    /// real, visible button on the hub's main screen, the same way "LEVELS"/"ENDLESS"/etc.
    /// already work.
    ///
    /// SHGUIcommanderbutton's constructor (confirmed via decompile) takes a genuinely
    /// generic Action&lt;SHGUIcommanderbutton&gt; click delegate -- it is NOT hardcoded to
    /// launching a level the way it first looked from the outside (every level button, and
    /// even the folder-navigation buttons like "LEVELS" itself, just happen to set that
    /// delegate to a level-launch or CreateViewFromNode-recursion call respectively). That
    /// means this button's click handler can go straight to opening the connection screen
    /// with no LevelInfo, no interaction with LevelAccessGuard, and no dependency on any of
    /// the existing level-launch gates at all -- confirmed by reading
    /// SHGUIcommanderbutton.OnActivate's own invocation site: it just checks IsLocked
    /// (defaults to false, never set here, so this button is never locked), then invokes
    /// OnActivate(this) if set.
    ///
    /// Round 14 follow-up: the click handler now pushes Core/ArchipelagoConnectApp.cs (a
    /// real, native app screen) via SHGUI.current.AddViewOnTop -- the same general
    /// mechanism SHGUI.LaunchAppByName uses internally to open any other app screen in the
    /// game -- instead of toggling the old Unity IMGUI overlay (Core/ConnectionUI.cs,
    /// removed).
    ///
    /// Round 17 follow-up: real bug report, "the ONLINE next to Archipelago menu item
    /// sometimes says OFFLINE while being online." Root cause, confirmed via decompile:
    /// piOsMenu.CreateViewFromNode is only called when the hub's ROOT view is actually
    /// rebuilt from scratch -- confirmed the only other caller of it is itself (a
    /// recursive call for subfolders like "LEVELS", and a challenge-mode variant), and
    /// popping a pushed-on-top view (SHGUI.current.PopView(), e.g. pressing Esc to close
    /// ArchipelagoConnectApp) just reveals the same already-built root view again without
    /// rebuilding it. So connecting successfully while already standing on the hub left the
    /// button's text exactly as stale as it was the moment the root view was first built --
    /// it never got a second chance to recompute "ONLINE" until the player left all the way
    /// back to the Main Menu and re-entered, which rebuilds the root view fresh.
    ///
    /// Fix: keep a live reference to whichever button instance is currently on screen
    /// (Button below, overwritten every time a fresh one is built) and re-derive its label
    /// every frame from Mod.OnUpdate() via RefreshLabel(), the same "trust live state, not
    /// whatever was baked in earlier" principle already used for level completion
    /// (LocationManager.IsLevelCompleted) and the secret badge (Round 16). Calling
    /// RefreshText() on a button whose view has since been popped/killed is harmless --
    /// confirmed via decompile that SHGUIview.Kill() just marks the view (and its
    /// children) as fading out, a plain C# object graph rather than a Unity object that
    /// could throw on further access -- so no extra bookkeeping is needed to null this out
    /// when the player leaves the hub; it's simply overwritten the next time a fresh
    /// button is built, and briefly-orphaned updates in between have no visible effect.
    ///
    /// Round 27 follow-up, real explicit user request ("put all of the archipelago
    /// selections that are in the hub into a folder"): this class no longer hooks
    /// piOsMenu.CreateViewFromNode directly or adds itself to the hub's root view --
    /// Patches/ArchipelagoFolderButtonPatch.cs now owns the one root-view hook (a single
    /// "ARCHIPELAGO" folder button), and calls AddTo() below to place this button inside
    /// that folder's own subfolder view instead (a plain SHGUIcommanderview built by hand
    /// the same way piOsMenu.CreateViewFromNode itself builds "LEVELS"'s -- confirmed via
    /// decompile, see that class's own docstring for the full reasoning). Renamed from
    /// "ARCHIPELAGO" to "CONNECT" since living inside a folder already labeled
    /// "ARCHIPELAGO" made the old label redundant. Everything else about this button (the
    /// live ONLINE/OFFLINE label, RefreshLabel()'s own per-frame safety) is unchanged.
    /// </summary>
    public static class ConnectionButtonPatch
    {
        private const string ButtonLabel = "CONNECT";

        // Not cleared on view teardown -- see class doc for why that's unnecessary here.
        internal static SHGUIcommanderbutton? Button { get; private set; }

        /// <summary>
        /// Builds this button and adds it to whatever view the caller passes in --
        /// Patches/ArchipelagoFolderButtonPatch.cs's own "ARCHIPELAGO" subfolder view, not
        /// the hub's root anymore. See class doc for why this changed from a direct
        /// CreateViewFromNode Postfix.
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
            RefreshLabel();
        }

        /// <summary>
        /// Recomputes and applies the button's ONLINE/OFFLINE status text from live
        /// connection state. Called once right after creation above, and every frame from
        /// Mod.OnUpdate() -- see class doc for why a one-time compute at creation isn't
        /// enough on its own.
        /// </summary>
        internal static void RefreshLabel()
        {
            if (Button == null)
            {
                return;
            }

            bool connected = Mod.Connection?.IsConnected ?? false;
            string status = connected ? "ONLINE" : "OFFLINE";
            Button.ButtonText = ButtonLabel.PadRight(12, ' ') + "│" + status;
            Button.RefreshText();
        }
    }
}
