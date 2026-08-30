using System.Linq;
using System.Xml.Linq;
using HarmonyLib;
using SuperhotArchipelago.Core;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, explicit user report: "I don't have access to a mod (the ones inside of
    /// SUPERHOT) that I believe you get when you finish the story." Confirmed via
    /// decompile that the native MODS folder's own unlock check
    /// (piOsMenu.AppendGameDataListFromNode) requires SaveManager's "storyFinished"
    /// flag to be true, on top of a kill count -- and that this mod used to keep that
    /// flag permanently false (Patches/StoryFinishedSuppressPatch.cs, plus a block in
    /// Core/Mod.cs's OnSceneWasLoaded that actively reset it back to false if it was
    /// ever found true), both removed this round. See Core/LocationManager.cs's
    /// CheckLocation for where storyFinished now actually gets set true through an
    /// Archipelago run.
    ///
    /// Letting storyFinished become true has one real side effect worth guarding
    /// against, confirmed via decompile of piOsMenu.CreateViewFromNode's "storylevels"
    /// case:
    /// <code>
    /// case "storylevels":
    ///     AppendLevelData(sHGUIcommanderview, LevelSetup.Levels, isChallenge);
    ///     if (!(bool)SaveManager.Instance.GetValue("storyFinished", false) || GameplayModifiers.CurrentChallenge != null)
    ///         LockUnfinishedLevels();
    ///     break;
    /// </code>
    /// Native code only re-locks/re-scrambles the LEVELS folder's own buttons when
    /// storyFinished is still false (or a challenge is active) -- once it's
    /// permanently true, this stops calling LockUnfinishedLevels() at all for every
    /// future room played on the same save, which is the one method
    /// Patches/HubUnlockPatch.cs's entire Postfix depends on running in the first
    /// place to draw this mod's own lock/color/badge overlay. Without a fix here,
    /// finishing the real game once would silently freeze HubUnlockPatch for every
    /// subsequent AP room -- exactly the kind of regression the old
    /// storyFinished-always-false approach was accidentally also preventing, just by
    /// breaking something else (MODS) to do it.
    ///
    /// Fix: Postfix on CreateViewFromNode itself. It's a private instance method
    /// (confirmed via decompile: `private void CreateViewFromNode(XElement e = null,
    /// List&lt;int&gt; allowedTags = null, List&lt;int&gt; downloadedTags = null, float
    /// downloadSpeed = 1f, bool isChallenge = false)`), so it's targeted here by
    /// string name via [HarmonyPatch(Type, string)] rather than nameof, same
    /// constraint MenuVisibilityPatch.cs already works around for "ShouldBeShown".
    /// Each call builds one whole folder's worth of buttons by iterating `e`'s child
    /// &lt;item&gt; elements and switching on each one's own "type" attribute
    /// (confirmed via decompile -- "storylevels" is a per-child value, not a property
    /// of `e`), so the Postfix checks whether any child of `e` is a "storylevels" item
    /// and, if so, calls LockUnfinishedLevels() itself unconditionally -- confirmed
    /// idempotent/safe to call an extra time (HubUnlockPatch.cs's own doc comment
    /// already established this). This makes the hub's per-level overlay keep working
    /// forever, regardless of storyFinished's value, instead of only before the real
    /// game is ever finished.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "CreateViewFromNode")]
    public static class StoryLevelsUnlockPatch
    {
        public static void Postfix(piOsMenu __instance, XElement e)
        {
            // Real, explicit user request: Archipelago mode can be turned off entirely
            // to play vanilla (see Mod.IsEnabled/Patches/ArchipelagoModeTogglePatch.cs).
            // While off, HubUnlockPatch's own Postfix already no-ops immediately, so
            // forcing an extra native LockUnfinishedLevels() call here would just be
            // wasted, redundant work -- skip it entirely to match every other patch's
            // convention.
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
    }
}
