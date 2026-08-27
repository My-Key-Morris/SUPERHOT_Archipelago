using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, deliberate SUPERHOT design, not a bug: "22 - Hacker"'s ending
    /// (LevelTest77_HackerRoomFlowControlVariantEnding, confirmed via decompile) is a
    /// narrative fake-out -- it calls SaveManager.Instance.SetValue("storyFinished",
    /// true) and detours into a credits scene, and the native hub menu treats
    /// storyFinished=true as "the game is over," scrambling/graying every other option so
    /// only quit.exe works, pushing the player to close the game (the game then continues
    /// for real on the next launch). Legitimate vanilla design; actively hostile to an
    /// AP run where the player is nowhere near actually done.
    ///
    /// Confirmed via decompile that SetValue("storyFinished", true) has exactly two call
    /// sites in the whole assembly: the fake ending above, and a separate "unlock
    /// everything" cheat/exploit tool (APPUnlockEverything) that also force-sets
    /// unlockEverything=true and a bunch of other save state -- not something a normal AP
    /// playthrough should trigger either. Neither site is needed for our own tracking:
    /// goal completion is entirely independent, driven by LocationManager calling
    /// Session.SetGoalAchieved() when the real final level (order 34, "Hackerg") is
    /// completed -- see Core/LocationManager.cs.
    ///
    /// Fix: suppress every write of storyFinished=true, full stop, via a Prefix on
    /// SaveManager.SetValue. Bonus effect: piOsMenu's "storylevels" case only calls
    /// LockUnfinishedLevels() when storyFinished is false (confirmed via decompile) --
    /// which HubUnlockPatch depends on to run at all -- so keeping this flag false also
    /// keeps that working exactly as it already needs to, rather than being a special
    /// case to reconcile.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SetValue))]
    public static class StoryFinishedSuppressPatch
    {
        public static bool Prefix(string key, object value)
        {
            // Real, explicit user request: Archipelago mode can be turned off entirely to
            // play vanilla (see Mod.IsEnabled/Patches/ArchipelagoModeTogglePatch.cs) --
            // while off, the real ending should behave exactly like vanilla SUPERHOT,
            // fake-out and all, not have this suppression silently still running.
            if (!Mod.IsEnabled)
            {
                return true;
            }

            if (key == "storyFinished" && value is bool isFinished && isFinished)
            {
                Mod.Log?.Msg("Suppressed SetValue(\"storyFinished\", true) -- this only ever " +
                    "comes from a fake/early ending or the unlock-everything cheat, neither " +
                    "of which should force-quit an in-progress Archipelago run.");
                return false;
            }

            return true;
        }
    }
}
