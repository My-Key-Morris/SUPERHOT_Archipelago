using System.Collections.Generic;
using System.Xml.Linq;
using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, explicit user request: a way to switch between playing through Archipelago
    /// and playing SUPERHOT normally without uninstalling/reinstalling the mod. Answered
    /// as a dedicated hub button (as opposed to folding it into the existing ARCHIPELAGO
    /// connect screen, Core/ArchipelagoConnectApp.cs) -- a single click flips the state
    /// directly, no screen to open first.
    ///
    /// Structurally this is Patches/ConnectionButtonPatch.cs's twin: same "hook
    /// piOsMenu.CreateViewFromNode's root view, keep a live button reference, and
    /// re-derive its label every frame" pattern for the same reason (a direct
    /// MelonPreferences.cfg edit to Config.Enabled while running is a supported parallel
    /// path, same as Server/Slot/Password already are -- see Mod.cs). Kept as a separate
    /// file rather than folded into ConnectionButtonPatch.cs because it's a conceptually
    /// distinct feature (a mode switch with mod-wide effects, not a connection-status
    /// display) -- consistent with this project's existing habit of one file per distinct
    /// concern (see LevelGatePatch.cs/ViaAppGatePatch.cs/DirectLevelSkipPatch.cs/
    /// TitleCardGatePatch.cs, all separate files gating different launch paths through the
    /// same shared check).
    ///
    /// The click handler itself does nothing but call Mod.SetEnabled(!Config.Enabled.Value)
    /// -- see that method and Core/LevelAccessGuard.cs's IsEnabled check (and every other
    /// patch that reads it) for what turning this off actually does mod-wide.
    ///
    /// Known limitation, accepted rather than engineered around: native
    /// piOsMenu.ShouldBeShown()/LockUnfinishedLevels() (see Patches/MenuVisibilityPatch.cs/
    /// HubUnlockPatch.cs) only re-run when a view is freshly built -- confirmed by
    /// ConnectionButtonPatch.cs's own Round 17 fix, which hit the exact same staleness for
    /// its ONLINE/OFFLINE label before per-frame refresh fixed just the label. Toggling AP
    /// mode won't retroactively re-scramble/re-color level buttons inside an already-open
    /// "LEVELS" view until the player backs out and re-enters it (which reruns the lock
    /// pass). Real level-launch gating itself (LevelAccessGuard.ShouldBlock) is unaffected
    /// by this -- it's checked live at launch time, never baked into a view -- so nothing
    /// actually un-vanilla is ever launchable in the gap; only some cosmetics can lag by one
    /// visit. Not worth forcing a view rebuild for pre-emptively; revisit if a real playtest
    /// finds this confusing in practice.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "CreateViewFromNode",
        new[] { typeof(XElement), typeof(List<int>), typeof(List<int>), typeof(float), typeof(bool) })]
    public static class ArchipelagoModeTogglePatch
    {
        private const string ButtonLabel = "AP MODE";

        // Not cleared on view teardown -- see ConnectionButtonPatch.cs's class doc for why
        // that's unnecessary (SHGUIview.Kill() just marks a plain C# object graph as fading
        // out, confirmed via decompile, not something that can throw on further access).
        internal static SHGUIcommanderbutton? Button { get; private set; }

        public static void Postfix(SHGUIcommanderview ___createdView)
        {
            if (___createdView == null || !___createdView.isRoot)
            {
                return;
            }

            SHGUIcommanderbutton button = new SHGUIcommanderbutton(BuildLabel(), 'w', delegate
            {
                Mod.SetEnabled(!Config.Enabled.Value);
            }).SetListLink(___createdView).SetData(
                "Turn Archipelago mode on or off. Off plays SUPERHOT exactly like vanilla " +
                "-- no level gating, no hub overlay -- and drops any active connection. " +
                "Turning back on reconnects and picks up right where you left off.");

            ___createdView.AddButtonView(button);

            Button = button;
            RefreshLabel();
        }

        /// <summary>
        /// Recomputes and applies the button's ON/OFF status text from live config state.
        /// Called once right after creation above, and every frame from Mod.OnUpdate() --
        /// see class doc for why a one-time compute at creation isn't enough on its own.
        /// </summary>
        internal static void RefreshLabel()
        {
            if (Button == null)
            {
                return;
            }

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
