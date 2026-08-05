using HarmonyLib;
using InputSystem;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// A third distinct auto-advance path, found after "28 - Station" and "30 - Gate"
    /// both got stuck indefinitely even with the earlier LevelFlowControl gate in place.
    /// Confirmed by decompiling LevelFlowControl.cs: most levels end with the classic
    /// "SUPERHOT" title-card sequence (`SuperHotSuperHotEnding()` /
    /// `SuperHotSuperHotEndingClickThrough()`), which waits for the skip button (LMB) to
    /// be pressed, then -- still *inside* that same method, synchronously, before any of
    /// our existing gates ever run -- plays audio, starts a camera effect, and sets
    /// `PlayerActions.CURRENT.state = PlayerState.FadeOut`, only *then* deferring the
    /// actual `SHGUI.current.LaunchLevelViaApp(LevelSetup.GetNextLevelInfo(), ...)` call
    /// (already gated by ViaAppGatePatch.cs) by 0.4s via DelayedInvokeMarshal. Blocking
    /// only that deferred call is the same "too late" mistake fixed for the
    /// LevelFlowControl auto-transition methods -- by the time it fires, the player is
    /// already mid-fade with no level loading under it and no clean way back.
    ///
    /// Matches the user's own diagnosis exactly: don't let the LMB click-through even
    /// register as a "start next level" input when the next level isn't unlocked. Rather
    /// than try to unwind everything the native code already started (camera effects,
    /// player state, audio -- fragile to replicate perfectly), this patch neutralizes the
    /// *input* itself before the native method reads it, via Harmony's private-field
    /// access to LevelFlowControl's own `inputData` (confirmed field, type
    /// `SHInputGUI.InputData`, holds the skip-button state each method checks). With the
    /// button state forced to `unpressed`, the native method's own
    /// "if skip button pressed, advance" branch simply doesn't trigger this frame --
    /// nothing starts, nothing needs undoing, and the title card just waits like the
    /// level hasn't ended yet, exactly as if the player hadn't clicked. Everything else
    /// in these methods (the WinLevel() call, achievement flags, the title-card display
    /// itself) is untouched and keeps working normally.
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
            TextManager.AddUptitleToQueue(new LocalizableText(blockMessage));
            Mod.Log?.Msg($"Suppressed SUPERHOT title-card click-through to '{next.SceneFileName}' -- not yet unlocked.");
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
