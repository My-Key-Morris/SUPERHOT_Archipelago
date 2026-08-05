using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Detects level completion. LevelSetup.UnlockNextLevel() (static void,
    /// confirmed at LevelSetup.cs:1052 in the decompiled game) is called exactly once
    /// from every distinct level-ending path in the real game -- the normal
    /// trigger-volume ending (LevelEnderTrigger.OnTriggerEnter sets
    /// LevelFlowControl.ENDINGTRIGGER, consumed by WaitForEnding() ->
    /// SuperHotSuperHotEnding() -> WinLevel() -> UnlockNextLevel()), plus
    /// KillemAllFadeout(), NextLEvelFade(), and AlphaEnding(), which all also end in a
    /// call to UnlockNextLevel(). Patching this one method covers every ending style.
    ///
    /// At the moment UnlockNextLevel() runs, LevelSetup.CurrentLevelInfo (confirmed
    /// static field, LevelSetup.cs:87) still refers to the level that was *just
    /// finished* -- UnlockNextLevel() computes the next index internally but doesn't
    /// advance CurrentLevelInfo itself -- so reading it here gives us the completed
    /// level. LevelInfo.ID (confirmed public int field) is what we report with -- NOT
    /// SceneFileName, which a real playtest showed is ambiguous (several levels reuse
    /// the same Unity scene; see Core/LevelCatalog.cs's LevelEntry comment). ID mirrors
    /// LevelSetup's own document-order index, unique per level instance.
    /// </summary>
    [HarmonyPatch(typeof(LevelSetup), nameof(LevelSetup.UnlockNextLevel))]
    public static class LevelCompletePatch
    {
        public static void Postfix()
        {
            LevelInfo finished = LevelSetup.CurrentLevelInfo;
            if (finished == null)
            {
                return;
            }

            SuperhotArchipelago.Core.Mod.Locations?.CheckLocation(finished.ID);
        }
    }
}
