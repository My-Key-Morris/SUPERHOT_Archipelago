using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Snapshots each story level's hub button text the moment it's built, before
    /// piOsMenu.LockUnfinishedLevels() gets a chance to scramble it for anything beyond
    /// the native "highest finished + 1" cutoff. See Core/ButtonTextCache.cs for why
    /// (the user wants level names to always stay legible, with only color -- not
    /// scrambled text -- showing lock state), and Patches/HubUnlockPatch.cs for where
    /// this gets restored.
    ///
    /// piOsMenu.PrepareLevelCommanderButtonForLevel(ref SHGUIcommanderbutton b,
    /// LevelInfo element, SHGUIcommanderview l, string customName = "", bool isChallenge
    /// = false) is private, confirmed via decompile -- it's the one place that actually
    /// builds a level's button, called both for the real per-level "LEVELS" browser and
    /// for the single "superhot.exe" hub icon (with customName "superhot.exe" in that
    /// second case). Only caching when customName == "" avoids polling that second,
    /// differently-labeled call into corrupting this level's real cached name.
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
                // Not one of our tracked story levels (e.g. an endless-mode entry,
                // which uses a disjoint ID range starting at 1337 -- see
                // LevelSetup.AddEndlessLevelInfo) -- nothing for us to restore later.
                return;
            }

            // Cache just the name portion (before the '│' status separator) -- the
            // status text itself ("CRACKED!" vs locked) is rebuilt fresh in
            // HubUnlockPatch based on our own unlock decision, not this snapshot, since
            // that decision can change after this button was first built.
            int separatorIndex = b.ButtonText.IndexOf('│');
            string cleanName = separatorIndex >= 0 ? b.ButtonText.Substring(0, separatorIndex) : b.ButtonText;
            ButtonTextCache.Remember(element.ID, cleanName);

            // Also cache the button's right-panel description text (`b.data`, already
            // built by piOsMenu.PrepareLevelDescription() at this point) before anything
            // else can touch it -- see ButtonTextCache.cs's own comment for why
            // HubUnlockPatch.cs needs a known-clean original to rebuild from.
            ButtonTextCache.RememberData(element.ID, b.data);
        }
    }
}
