using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Reports a location check the first time the player finds an in-level secret
    /// console (TerminalActivator.OnActivate). Watches the secretFound field's false ->
    /// true transition via Harmony's __state handoff rather than duplicating the game's
    /// own "already found" guard. Joins on LevelInfo.ID (each level has at most one
    /// secret), matching how every other check in this mod is keyed.
    /// </summary>
    [HarmonyPatch(typeof(TerminalActivator), nameof(TerminalActivator.OnActivate))]
    public static class SecretFoundPatch
    {
        public static void Prefix(bool ___secretFound, out bool __state)
        {
            __state = ___secretFound;
        }

        public static void Postfix(bool ___secretFound, bool __state)
        {
            // Skip entirely when AP mode is off, so vanilla play doesn't trigger a
            // misleading "not connected" warning.
            if (!Mod.IsEnabled)
            {
                return;
            }

            bool justFoundForTheFirstTime = !__state && ___secretFound;
            if (!justFoundForTheFirstTime)
            {
                return;
            }

            LevelInfo current = LevelSetup.CurrentLevelInfo;
            if (current == null)
            {
                return;
            }

            Mod.Locations?.CheckSecretLocation(current.ID);
        }
    }
}
