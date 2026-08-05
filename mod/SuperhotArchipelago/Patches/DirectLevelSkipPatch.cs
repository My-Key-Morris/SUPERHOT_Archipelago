using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Third of three progression gates -- see Core/LevelAccessGuard.cs. Closes a real
    /// exploit a playtest found: clicking through the end-of-level fade (e.g. left-click
    /// right as a level finishes) calls LevelSetup.LoadNextLevel(), which goes straight
    /// to LevelSetup.GetNextLevelInfo() -> LoadLevel(...) -- confirmed by decompiling
    /// LevelSetup.cs -- completely bypassing both SHGUI.LaunchLevelAppTunnels() and
    /// SHGUI.LaunchLevelViaApp(), the two methods LevelGatePatch.cs and
    /// ViaAppGatePatch.cs guard. That let a player walk from level 3 straight into level
    /// 4 without ever holding its access item, as long as they stayed in the "just keep
    /// clicking through" flow instead of going back to the hub.
    ///
    /// GetNextLevelInfo() (distinct from GetNewLevelInfo(), which the "superhot.exe" hub
    /// button uses) is even more naive: it's just
    /// Levels[GetLevelIndexByID(CurrentLevelInfo.ID) + 1] -- the literal next entry in
    /// document order, no highestfinishedLevel involved at all.
    ///
    /// Fix: Prefix on LoadNextLevel(bool) computes what GetNextLevelInfo() would return
    /// and applies the same unlock check the other two gates use. If it's not allowed,
    /// skip the original method entirely (so none of its side effects -- resetting kill
    /// counts, pausing time, actually loading a scene -- happen) and kick the player back
    /// to the hub instead of leaving them stuck on the fade screen.
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

            TextManager.AddUptitleToQueue(new LocalizableText(blockMessage));
            Core.Mod.Log?.Msg($"Blocked direct-continue to '{next.SceneFileName}' -- " +
                "not yet unlocked. Returning to hub instead of leaving the player stuck.");

            if (SHGUI.current != null)
            {
                SHGUI.current.LaunchLevelAppTunnels("SHMenu", false);
            }

            return false;
        }
    }
}
