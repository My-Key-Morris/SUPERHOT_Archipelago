using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Second of three progression gates (see Core/LevelAccessGuard.cs). Guards
    /// SHGUI.LaunchLevelViaApp, used by several smooth "no hub visit" level transitions
    /// that bypass the other two gates. Also acts as the last line of defense behind
    /// TitleCardGatePatch, so a block here redirects to the hub instead of leaving the
    /// player stuck mid-transition with no level loaded.
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

            Core.PopupOverlay.Show(blockMessage);
            Core.Mod.Log?.LogInfo($"Blocked launch of '{level.SceneFileName}' via LaunchLevelViaApp -- {blockMessage}. Returning to hub.");

            if (SHGUI.current != null)
            {
                SHGUI.current.LaunchLevelAppTunnels("SHMenu", false);
            }

            return false;
        }
    }
}
