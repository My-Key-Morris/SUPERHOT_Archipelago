using System.Collections.Generic;
using System.Xml.Linq;
using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, explicit user request, revising Core/ConnectionUI.cs's original F2-hotkey
    /// design: instead of a hidden keybind, put the Archipelago connection menu behind a
    /// real, visible button on the hub's main screen, the same way "LEVELS"/"ENDLESS"/etc.
    /// already work.
    ///
    /// Confirmed via decompile rather than guessed: piOsMenu.CreateViewFromNode(XElement,
    /// List&lt;int&gt;, List&lt;int&gt;, float, bool) is the one method that builds every hub
    /// screen -- both the top-level root (called with e == null, from
    /// CreateDirectoryStructure) and every folder navigated into (called recursively with
    /// e == the clicked node). It assigns a brand new SHGUIcommanderview to its own private
    /// createdView field on every single call (SHGUIcommanderview sHGUIcommanderview =
    /// (createdView = new SHGUIcommanderview());) and sets that view's public isRoot field
    /// from whether e started out null -- exactly the signal needed to add a button only to
    /// the top-level screen, not every subfolder. Because a fresh view is created every
    /// time (including every time the player returns to the hub), this Postfix runs once
    /// per fresh root view and never needs to guard against adding a duplicate button to
    /// the same view twice.
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
    /// rebuilding it. So connecting successfully while already standing on the hub (open
    /// ARCHIPELAGO, connect, Esc back) left the button's text exactly as stale as it was
    /// the moment the root view was first built -- it never got a second chance to
    /// recompute "ONLINE" until the player left all the way back to the Main Menu and
    /// re-entered, which rebuilds the root view fresh. (Also confirmed
    /// piOsMenu.LockUnfinishedLevels -- which HubUnlockPatch.cs piggybacks on for the
    /// per-level three-state visuals and the Round 16 secret-badge fix -- only fires
    /// inside the "LEVELS" subfolder's own view build, not the root's, so it can't help
    /// refresh this button either; it's simply never present in that view.)
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
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "CreateViewFromNode",
        new[] { typeof(XElement), typeof(List<int>), typeof(List<int>), typeof(float), typeof(bool) })]
    public static class ConnectionButtonPatch
    {
        private const string ButtonLabel = "ARCHIPELAGO";

        // Not cleared on view teardown -- see class doc for why that's unnecessary here.
        internal static SHGUIcommanderbutton? Button { get; private set; }

        public static void Postfix(SHGUIcommanderview ___createdView)
        {
            if (___createdView == null || !___createdView.isRoot)
            {
                return;
            }

            SHGUIcommanderbutton button = new SHGUIcommanderbutton(ButtonLabel.PadRight(12, ' ') + "│OFFLINE", 'w', delegate
            {
                SHGUI.current.AddViewOnTop(new ArchipelagoConnectApp());
            }).SetListLink(___createdView).SetData(
                "Set up or check your Archipelago server connection.");

            ___createdView.AddButtonView(button);

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
