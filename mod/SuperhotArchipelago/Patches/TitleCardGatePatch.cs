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
    /// native method reads it, so nothing starts in the first place when blocked.
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
            Mod.Log?.Msg($"Suppressed SUPERHOT title-card click-through to '{next.SceneFileName}' -- {blockMessage}.");
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
