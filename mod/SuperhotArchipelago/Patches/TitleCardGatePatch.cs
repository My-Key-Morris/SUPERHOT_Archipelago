using HarmonyLib;
using InputSystem;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// A third auto-advance path: the title-card sequence starts camera/audio effects
    /// synchronously on skip-button press, then defers the actual level launch (already
    /// gated by ViaAppGatePatch) too late to block cleanly. Instead of unwinding those
    /// effects after the fact, this suppresses the skip-button input itself before the
    /// native method reads it, so nothing starts in the first place when blocked -- then,
    /// same as every other gate (ViaAppGatePatch/DirectLevelSkipPatch/AutoTransitionCheckPatch),
    /// redirects to the hub instead of just eating the click, so a blocked click still goes
    /// somewhere rather than leaving the player stuck staring at a frozen title card with no
    /// feedback (real, explicit user request -- this was the one gate that didn't already do this).
    /// </summary>
    internal static class TitleCardGate
    {
        public static void SuppressAdvanceIfBlocked(ref SHInputGUI.InputData inputData)
        {
            bool wasTryingToAdvance = (inputData.skipButton & SHInput.ButtonState.pressed) != 0
                || inputData.skipButton == SHInput.ButtonState.justUnpressed;
            if (!wasTryingToAdvance)
            {
                return;
            }

            LevelInfo next = LevelSetup.GetNextLevelInfo();
            if (!LevelAccessGuard.ShouldBlock(next, out string blockMessage))
            {
                return;
            }

            inputData.skipButton = SHInput.ButtonState.unpressed;
            Mod.Log?.Msg($"Suppressed SUPERHOT title-card click-through to '{next.SceneFileName}' -- " +
                $"{blockMessage}. Returning to hub.");

            if (SHGUI.current != null)
            {
                SHGUI.current.LaunchLevelAppTunnels("SHMenu", false);
            }
        }
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.SuperHotSuperHotEnding))]
    public static class TitleCardGatePatch_Ending
    {
        public static void Prefix(ref SHInputGUI.InputData ___inputData) => TitleCardGate.SuppressAdvanceIfBlocked(ref ___inputData);
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.SuperHotSuperHotEndingClickThrough))]
    public static class TitleCardGatePatch_EndingClickThrough
    {
        public static void Prefix(ref SHInputGUI.InputData ___inputData) => TitleCardGate.SuppressAdvanceIfBlocked(ref ___inputData);
    }
}
