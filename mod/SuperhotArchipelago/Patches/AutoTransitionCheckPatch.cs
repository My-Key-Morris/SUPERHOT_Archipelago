using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real bug found by a playtest: "32 - Longway" never sent a check on completion.
    /// Root cause, confirmed by decompiling LevelFlowControl.cs: not every level ends
    /// through the normal "kill all -> ending fade -> click to continue" flow that
    /// LevelCompletePatch.cs listens to (LevelSetup.UnlockNextLevel()). Some levels
    /// instead end with a smooth, scripted "no hub visit" transition straight into the
    /// next level -- LevelFlowControl.LoadNextLevel() and its four siblings below --
    /// which never call UnlockNextLevel() at all. For those levels, our only completion
    /// signal was simply never firing. Sending here is intentionally unconditional and
    /// independent of LevelCompletePatch -- if a level happens to trigger both paths, the
    /// server just sees a harmless duplicate (CompleteLocationChecks is idempotent).
    ///
    /// Second real bug, found on the very next playtest: blocking the *inner* launch call
    /// (ViaAppGatePatch/LevelGatePatch, both several calls deep inside these methods,
    /// invoked ~0.1-0.4s later via DelayedInvokeMarshal) was too late. By the time that
    /// inner call runs, these outer methods have already synchronously kicked off their
    /// camera-glitch/static/audio transition effects -- confirmed by decompiling
    /// LevelFlowControl.cs, e.g. CameraEffectsManager.Instance["HotswitchRealtime"].Play(...)
    /// and the AppHotswitch overlay fill both happen immediately, only the actual level
    /// launch is deferred. Blocking just the deferred part left the player stuck in that
    /// half-finished static effect with no real level ever loading under it -- for most
    /// levels apparently escapable by pressing Escape (not something this mod does), but
    /// for "14 - Serv" specifically, not escapable at all, a real soft-lock.
    ///
    /// Fixed by gating here too, *before* any of that starts: Prefix now checks what
    /// LevelSetup.GetNextLevelInfo() would be and blocks the entire method outright (skip
    /// with no camera/audio effects ever triggered) if it's not allowed, redirecting to
    /// the hub immediately instead of leaving anything mid-transition. The four
    /// Status-returning methods share a private `loadedNextLevel` field (confirmed via
    /// decompile) that normally guards their real work against repeated per-frame calls;
    /// since skipping the method means that field never gets set by the original code, we
    /// set it ourselves when blocking so the behavior-tree node doesn't keep re-entering
    /// and re-triggering our redirect every frame.
    /// </summary>
    internal static class AutoTransitionCheck
    {
        public static void ReportCompletionIfFirstCall(bool alreadyStarted)
        {
            if (alreadyStarted)
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

            TextManager.AddUptitleToQueue(new LocalizableText(blockMessage));
            Mod.Log?.Msg($"Blocked auto-transition to '{next.SceneFileName}' before any " +
                "static/camera transition started -- not yet unlocked. Returning to hub.");

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
            // No loadedNextLevel-style guard exists on this one (confirmed via
            // decompile -- its body has no such check), and unlike the four Status-
            // returning siblings above it isn't a polled behavior-tree node, so there's
            // no repeated-call risk to guard against here.
            return !AutoTransitionCheck.TryBlockAndRedirect();
        }
    }
}
