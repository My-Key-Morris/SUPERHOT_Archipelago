using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Third of three progression gates (see Core/LevelAccessGuard.cs). Clicking through
    /// the end-of-level fade calls LevelSetup.LoadNextLevel() directly, bypassing the
    /// two methods LevelGatePatch/ViaAppGatePatch guard, letting a player skip into an
    /// unearned level. This Prefix applies the same unlock check and redirects to the
    /// hub instead if blocked.
    /// </summary>
    [HarmonyPatch(typeof(LevelSetup), nameof(LevelSetup.LoadNextLevel))]
    public static class DirectLevelSkipPatch
    {
        public static bool Prefix()
        {
            LevelInfo next = LevelSetup.GetNextLevelInfo();

            if (!Core.LevelAccessGuard.ShouldBlock(next, out string blockMessage))
            {
                return true;
            }

            Core.Mod.Log?.Msg($"Blocked direct-continue to '{next.SceneFileName}' -- " +
                $"{blockMessage}. Returning to hub instead of leaving the player stuck.");

            if (SHGUI.current != null)
            {
                SHGUI.current.LaunchLevelAppTunnels("SHMenu", false);
            }

            return false;
        }
    }
}
