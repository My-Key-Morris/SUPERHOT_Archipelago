using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// On a fresh save, native tag-based progression hides most hub folders (LEVELS,
    /// ENDLESS, etc.) via piOsMenu.ShouldBeShown, since those tags aren't earned yet
    /// under vanilla rules. This forces every menu node visible from the first boot;
    /// actual level-launch gating (LevelGatePatch.cs) is unaffected, only visibility.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "ShouldBeShown")]
    public static class MenuVisibilityPatch
    {
        public static bool Prefix(ref bool __result)
        {
            // When AP mode is off, let native tag-based visibility decide as normal.
            if (!Mod.IsEnabled)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
