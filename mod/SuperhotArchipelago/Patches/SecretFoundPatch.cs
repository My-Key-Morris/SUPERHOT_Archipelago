using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, explicit user request: report a location check the first time the player
    /// finds an in-level secret console.
    ///
    /// Confirmed via decompile: each secret console is a GameObject with a
    /// TerminalActivator component (public int SecretNumber, private bool secretFound).
    /// It's reached through ActivatorPickup.Pickup(), which calls
    /// gameObject.SendMessage("OnActivate") -- a Unity SendMessage dispatch, but that
    /// still runs the real, Harmony-patchable TerminalActivator.OnActivate() underneath.
    /// That method's own logic is the "first find" guard already built into the game: if
    /// secretFound is already true, it just plays an error sound and returns; otherwise it
    /// sets SaveManager.Instance.SetValue(CurrentLevelInfo.SceneFileName + SecretNumber +
    /// "unlocked", true), launches the secret's content app, and sets secretFound = true.
    ///
    /// Rather than duplicate that "was this already found" logic ourselves, this patch
    /// just watches for the secretFound field's actual false -> true transition using
    /// Harmony's Prefix/Postfix __state handoff: capture the value before the method runs,
    /// compare to the value after. Only a genuine transition (not a revisit, and not a
    /// no-op call where SHGUIApp was empty and the method bailed before setting anything)
    /// counts as a first find.
    ///
    /// Join key is LevelSetup.CurrentLevelInfo.ID -- NOT TerminalActivator.SecretNumber
    /// combined with SceneFileName, which is what the native save key above actually uses.
    /// That native key could in principle collide across the handful of levels that reuse
    /// a scene (see levels.json's _caveats), but a real extraction of the game's own data
    /// confirmed none of those duplicate-scene levels (Dog1/Dog2/Dog3/Hacker/Free) actually
    /// have a secret, so this is defense in depth rather than a fix for an observed bug --
    /// consistent with how every other check in this mod already joins on LevelInfo.ID.
    ///
    /// Data confirmed via extracting the real GameData Story XML directly (same source as
    /// levels.json's level list): every level has either 0 or 1 secrets, never more, so
    /// there's no need to build a per-secret-index location -- one secret location per
    /// level (LevelCatalog.LevelEntry.HasSecret) is enough. See
    /// apworld/superhot/Locations.py's secret_location_name for the matching AP location.
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

            SuperhotArchipelago.Core.Mod.Locations?.CheckSecretLocation(current.ID);
        }
    }
}
