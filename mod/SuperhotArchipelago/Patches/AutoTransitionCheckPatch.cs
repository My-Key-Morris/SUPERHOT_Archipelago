using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Some levels end via a scripted "no hub visit" transition (LoadNextLevel and its
    /// siblings) that never calls UnlockNextLevel(), so LevelCompletePatch's completion
    /// signal never fires for them -- this reports completion here too (harmless
    /// duplicate if both paths fire, since CompleteLocationChecks is idempotent). Gating
    /// must happen in this Prefix, before camera/audio transition effects start
    /// synchronously; blocking only the deferred inner launch call leaves the player
    /// stuck mid-transition with no level loaded. Also manually sets `loadedNextLevel`
    /// when blocking, since skipping the method means the original code never sets it.
    /// </summary>
    internal static class AutoTransitionCheck
    {
        public static void ReportCompletionIfFirstCall(bool alreadyStarted)
        {
            if (alreadyStarted)
            {
                return;
            }

            // Skip entirely when AP mode is off, so vanilla play doesn't trigger a
            // misleading "not connected" warning.
            if (!Mod.IsEnabled)
            {
                return;
            }

            LevelInfo finished = LevelSetup.CurrentLevelInfo;
            if (finished == null)
            {
                return;
            }

            Mod.Locations?.CheckLocation(finished.ID);
        }

        /// <summary>
        /// Returns true if the caller should be blocked (and has already been redirected
        /// to the hub), false if it's fine to let the original method run.
        /// </summary>
        public static bool TryBlockAndRedirect()
        {
            LevelInfo next = LevelSetup.GetNextLevelInfo();

            if (!LevelAccessGuard.ShouldBlock(next, out string blockMessage))
            {
                return false;
            }

            Mod.Log?.Msg($"Blocked auto-transition to '{next.SceneFileName}' before any " +
                $"static/camera transition started -- {blockMessage}. Returning to hub.");

            if (SHGUI.current != null)
            {
                SHGUI.current.LaunchLevelAppTunnels("SHMenu", false);
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.LoadNextLevel))]
    public static class AutoTransitionCheckPatch_LoadNextLevel
    {
        public static bool Prefix(ref bool ___loadedNextLevel, ref Status __result)
        {
            AutoTransitionCheck.ReportCompletionIfFirstCall(___loadedNextLevel);
            if (___loadedNextLevel || !AutoTransitionCheck.TryBlockAndRedirect())
            {
                return true;
            }

            ___loadedNextLevel = true;
            __result = Status.Success;
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.LoadNextLevelWithTunnels))]
    public static class AutoTransitionCheckPatch_LoadNextLevelWithTunnels
    {
        public static bool Prefix(ref bool ___loadedNextLevel, ref Status __result)
        {
            AutoTransitionCheck.ReportCompletionIfFirstCall(___loadedNextLevel);
            if (___loadedNextLevel || !AutoTransitionCheck.TryBlockAndRedirect())
            {
                return true;
            }

            ___loadedNextLevel = true;
            __result = Status.Success;
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.LoadNextLevelWithTunnelsWithoutScramble))]
    public static class AutoTransitionCheckPatch_LoadNextLevelWithTunnelsWithoutScramble
    {
        public static bool Prefix(ref bool ___loadedNextLevel, ref Status __result)
        {
            AutoTransitionCheck.ReportCompletionIfFirstCall(___loadedNextLevel);
            if (___loadedNextLevel || !AutoTransitionCheck.TryBlockAndRedirect())
            {
                return true;
            }

            ___loadedNextLevel = true;
            __result = Status.Success;
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.LoadNextLevelWithTunnelsWithScrambleOnEnd))]
    public static class AutoTransitionCheckPatch_LoadNextLevelWithTunnelsWithScrambleOnEnd
    {
        public static bool Prefix(ref bool ___loadedNextLevel, ref Status __result)
        {
            AutoTransitionCheck.ReportCompletionIfFirstCall(___loadedNextLevel);
            if (___loadedNextLevel || !AutoTransitionCheck.TryBlockAndRedirect())
            {
                return true;
            }

            ___loadedNextLevel = true;
            __result = Status.Success;
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.LoadNextLevelInstant))]
    public static class AutoTransitionCheckPatch_LoadNextLevelInstant
    {
        public static bool Prefix()
        {
            AutoTransitionCheck.ReportCompletionIfFirstCall(false);
            // This method has no loadedNextLevel-style guard and isn't polled repeatedly
            // like the four siblings above, so no repeated-call guard is needed here.
            return !AutoTransitionCheck.TryBlockAndRedirect();
        }
    }
}
