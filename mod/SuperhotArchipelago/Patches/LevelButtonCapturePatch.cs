using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Snapshots each story level's hub button text as soon as it's built, before
    /// piOsMenu.LockUnfinishedLevels() can scramble it (see Core/ButtonTextCache.cs and
    /// Patches/HubUnlockPatch.cs). Only caches when customName == "", since the same
    /// builder method is also called for the "superhot.exe" icon with a different name.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "PrepareLevelCommanderButtonForLevel")]
    public static class LevelButtonCapturePatch
    {
        public static void Postfix(ref SHGUIcommanderbutton b, LevelInfo element, string customName)
        {
            if (customName != "" || b == null || element == null)
            {
                return;
            }

            if (!LevelCatalog.LevelIdToLevel.ContainsKey(element.ID))
            {
                // Not a tracked story level (e.g. endless-mode entries use a separate ID range).
                return;
            }

            // Cache just the name portion; the status suffix is rebuilt fresh by
            // HubUnlockPatch since the unlock decision can change after this point.
            int separatorIndex = b.ButtonText.IndexOf('│');
            string cleanName = separatorIndex >= 0 ? b.ButtonText.Substring(0, separatorIndex) : b.ButtonText;
            ButtonTextCache.Remember(element.ID, cleanName);

            // Also cache the right-panel description text before anything else can touch it,
            // so HubUnlockPatch has a known-clean original to rebuild from.
            ButtonTextCache.RememberData(element.ID, b.data);
        }
    }
}
