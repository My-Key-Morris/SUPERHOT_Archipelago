using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real diagnosis (from an actual fresh-save playthrough log): after finishing "Kick"
    /// and receiving a non-sequential AP item ("Desperados"), the hub still only offered
    /// the single "superhot.exe" icon -- no "LEVELS" folder to browse into at all. That's
    /// different from the earlier "smart-jump" investigation's assumption; this confirms
    /// the "LEVELS" folder (and "ENDLESS"/"CHALLENGES"/etc.) are themselves hidden on a
    /// genuinely fresh save, not just their contents.
    ///
    /// Root cause, confirmed by decompiling `piOsMenu.cs`: every folder/app node in the
    /// hub's menu tree (`FolderStructure` XML, walked by `CreateViewFromNode`) is filtered
    /// through `ShouldBeShown(XElement node, List&lt;int&gt; allowedTags)` -- a node with a
    /// `tag` attribute only shows if that tag is in `allowedTags`, which on a fresh save is
    /// just `{0, 1}` plus whatever tags `SaveManager.Instance.GetTags()` has actually earned
    /// through native progression. "LEVELS" (and most of the rest of the hub) needs tags
    /// that simply haven't been earned yet this early -- nothing to do with story items.
    ///
    /// Fix, at the user's request: instead of waiting on native progression to reveal the
    /// browser, always show every menu node -- effectively the same hub layout you'd see on
    /// a finished save, available from the very first boot. This does expose some non-AP
    /// content early too (ENDLESS, CHALLENGES, recruit.exe, credits.exe, etc.) -- accepted
    /// tradeoff, and none of it is tracked by Archipelago anyway (see NOTES.md's "Design
    /// decisions still open" on endless/challenge scope).
    ///
    /// This does NOT touch the "storyFinished" save flag or any other save data, and
    /// doesn't disable per-level locking: browsing into "LEVELS" still runs the native
    /// `piOsMenu.LockUnfinishedLevels()` pass (untouched by this patch, since
    /// "storyFinished" stays false for real), which `HubUnlockPatch.cs` still layers
    /// Archipelago's own unlocks on top of. `LevelGatePatch.cs` still blocks the actual
    /// level launch regardless of what any icon visually shows. So level-by-level gating is
    /// unaffected -- only whether you can see the picker at all changes.
    /// </summary>
    [HarmonyPatch(typeof(piOsMenu), "ShouldBeShown")]
    public static class MenuVisibilityPatch
    {
        public static bool Prefix(ref bool __result)
        {
            __result = true;
            return false;
        }
    }
}
