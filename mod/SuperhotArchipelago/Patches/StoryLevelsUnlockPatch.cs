using System.Linq;
using System.Xml.Linq;
using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Once storyFinished becomes true (needed for MODS folder access), native code's
    /// CreateViewFromNode stops calling LockUnfinishedLevels() for the "storylevels" case,
    /// which would silently freeze HubUnlockPatch's overlay for the rest of the save. This
    /// Postfixes CreateViewFromNode (a private method, hence string-name targeting) and
    /// calls LockUnfinishedLevels() itself whenever a "storylevels" folder was built,
    /// regardless of storyFinished -- safe since that call is idempotent.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "CreateViewFromNode")]
    public static class StoryLevelsUnlockPatch
    {
        public static void Postfix(piOsMenu __instance, XElement e)
        {
            // This Postfix on piOsMenu.CreateViewFromNode -- plausibly involved in building the
            // hub menu tree from its XML config -- calls the NATIVE LockUnfinishedLevels()
            // synchronously below. If that call (or HubUnlockPatch's own Postfix on it) throws,
            // the exception would propagate straight back into whatever native code is building
            // the rest of the menu tree, with nothing useful in the log. Added during "Round
            // 48"'s cold-boot freeze investigation (the actual bug turned out to be elsewhere --
            // see PopupOverlay.EnsureCanvas's comment) but the risk is real regardless.
            try
            {
                // While Archipelago mode is off, HubUnlockPatch's Postfix already no-ops, so
                // forcing a native LockUnfinishedLevels() call here would just be wasted work.
                if (!Mod.IsEnabled)
                {
                    return;
                }

                if (e == null)
                {
                    return;
                }

                bool builtStoryLevelsFolder = e.Elements()
                    .Any(child => child.Name.ToString() == "item"
                        && child.Attribute("type")?.Value == "storylevels");

                if (!builtStoryLevelsFolder)
                {
                    return;
                }

                __instance.LockUnfinishedLevels();
            }
            catch (System.Exception ex)
            {
                Mod.Log?.Error($"StoryLevelsUnlockPatch.Postfix threw: {ex}");
            }
        }
    }
}
