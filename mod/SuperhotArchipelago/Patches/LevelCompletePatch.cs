using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Detects level completion by patching LevelSetup.UnlockNextLevel(), which every
    /// ending path (trigger-volume, fade, kill-all, etc.) calls exactly once. At this
    /// point CurrentLevelInfo still refers to the just-finished level, and its ID (not
    /// SceneFileName, which several levels share) is what we report.
    /// </summary>
    [HarmonyPatch(typeof(LevelSetup), nameof(LevelSetup.UnlockNextLevel))]
    public static class LevelCompletePatch
    {
        public static void Postfix()
        {
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
    }
}
