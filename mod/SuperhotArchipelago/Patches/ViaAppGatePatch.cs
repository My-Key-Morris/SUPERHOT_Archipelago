using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Second of three progression gates -- see Core/LevelAccessGuard.cs. Real bug found
    /// by a playtest: even with LevelGatePatch (guarding
    /// SHGUI.LaunchLevelAppTunnels(LevelInfo,...)) and DirectLevelSkipPatch (guarding the
    /// static LevelSetup.LoadNextLevel(bool) click-through), a player could still walk
    /// straight into a locked level. Root cause, confirmed by decompiling
    /// LevelFlowControl.cs: the smooth "no hub visit" level transitions --
    /// LoadNextLevel(), LoadNextLevelWithTunnelsWithoutScramble(), and
    /// LoadNextLevelInstant() among them -- call a *third*, different method,
    /// SHGUI.LaunchLevelViaApp(LevelInfo, float), which neither existing patch touches.
    /// (LoadNextLevelWithTunnels() and LoadNextLevelWithTunnelsWithScrambleOnEnd()
    /// happen to call LaunchLevelAppTunnels instead, so those two were already covered --
    /// but there was no way to know that without checking every single one.)
    ///
    /// This patch closes that gap the same way LevelGatePatch does for its method.
    ///
    /// Second real bug, found investigating "28 - Station" softlocking: this is the one
    /// gate that's also the *last* line of defense for LevelFlowControl.SuperHotSuperHotEnding()
    /// (see TitleCardGatePatch.cs) -- that method starts camera/audio/fade-state effects
    /// synchronously and only calls into here 0.1-0.4s later via DelayedInvokeMarshal. If
    /// anything ever lets a click through that TitleCardGatePatch should have caught (the
    /// intermission-skip bug fixed in LevelAccessGuard.cs was exactly such a case), simply
    /// returning false here left the player stuck mid-transition with no level ever loading
    /// under it -- the same class of soft-lock DirectLevelSkipPatch.cs and
    /// AutoTransitionCheckPatch.cs already redirect out of. Doing the same here now, so a
    /// block at this layer is never a dead end even if it's not the layer that was
    /// supposed to catch it.
    /// </summary>
    [HarmonyPatch(typeof(SHGUI), nameof(SHGUI.LaunchLevelViaApp), typeof(LevelInfo), typeof(float))]
    public static class ViaAppGatePatch
    {
        public static bool Prefix(LevelInfo level)
        {
            if (!Core.LevelAccessGuard.ShouldBlock(level, out string blockMessage))
            {
                return true;
            }

            TextManager.AddUptitleToQueue(new LocalizableText(blockMessage));
            Core.Mod.Log?.Msg($"Blocked launch of '{level.SceneFileName}' via LaunchLevelViaApp -- {blockMessage}. Returning to hub.");

            if (SHGUI.current != null)
            {
                SHGUI.current.LaunchLevelAppTunnels("SHMenu", false);
            }

            return false;
        }
    }
}
