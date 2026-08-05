using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// One of three progression gates -- see Core/LevelAccessGuard.cs for the shared
    /// unlock check, and ViaAppGatePatch.cs / DirectLevelSkipPatch.cs for the other two.
    /// A real playtest proved a single gate here wasn't enough: SUPERHOT has multiple
    /// independent ways to actually start a level (the hub's per-level buttons and the
    /// single "superhot.exe" shortcut both funnel through
    /// SHGUI.LaunchLevelAppTunnels(LevelInfo, bool, bool), confirmed by reading
    /// piOsMenu.PrepareLevelCommanderButtonForLevel() -- but level-to-level auto-continue
    /// transitions (LevelFlowControl.LoadNextLevel() and friends) go through a
    /// *different* method, SHGUI.LaunchLevelViaApp(LevelInfo, float), entirely bypassing
    /// this patch. Gating all the real entry points needed all three patches.
    /// </summary>
    [HarmonyPatch(typeof(SHGUI), nameof(SHGUI.LaunchLevelAppTunnels), typeof(LevelInfo), typeof(bool), typeof(bool))]
    public static class LevelGatePatch
    {
        public static bool Prefix(LevelInfo level)
        {
            if (!Core.LevelAccessGuard.ShouldBlock(level, out string blockMessage))
            {
                return true;
            }

            TextManager.AddUptitleToQueue(new LocalizableText(blockMessage));
            Core.Mod.Log?.Msg($"Blocked launch of '{level.SceneFileName}' via LaunchLevelAppTunnels -- not yet unlocked.");

            return false;
        }
    }
}
