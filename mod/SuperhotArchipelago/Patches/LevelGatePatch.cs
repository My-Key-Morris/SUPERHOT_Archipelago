using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// One of three progression gates (see Core/LevelAccessGuard.cs for the shared check,
    /// and ViaAppGatePatch.cs / DirectLevelSkipPatch.cs for the other two). This one guards
    /// SHGUI.LaunchLevelAppTunnels, used by the hub buttons and "superhot.exe" shortcut;
    /// auto-continue transitions use a different method, hence the other two patches.
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

            Core.PopupOverlay.Show(blockMessage);
            Core.Mod.Log?.LogInfo($"Blocked launch of '{level.SceneFileName}' via LaunchLevelAppTunnels -- {blockMessage}");

            return false;
        }
    }
}
