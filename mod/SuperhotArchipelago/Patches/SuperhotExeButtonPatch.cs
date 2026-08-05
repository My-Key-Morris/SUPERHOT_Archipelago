using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Real, explicit user request: hide the "superhot.exe" hub shortcut entirely.
    /// Previously deferred (see NOTES.md's round-3 log) over risk of navigation bugs from
    /// removing an existing list entry after the fact -- this avoids that risk by never
    /// letting the button get added in the first place, rather than adding then removing.
    ///
    /// Confirmed via decompile: "superhot.exe" is built in piOsMenu.CreateViewFromNode()'s
    /// "last" case (piOsMenu.cs, the case whose XML node type is "last") by calling
    /// PrepareLevelCommanderButtonForLevel(ref b, LevelSetup.GetNewLevelInfo(),
    /// sHGUIcommanderview, "superhot.exe") -- the literal string "superhot.exe" appears
    /// exactly once in the whole decompiled assembly, always as this customName. That
    /// method builds the button's text as "<customName-or-level-name, padded/truncated to
    /// 12 chars>│<status>" with no translation applied to customName itself -- and
    /// "superhot.exe" is exactly 12 characters, so it survives untouched, making
    /// button.ButtonText.StartsWith("superhot.exe") an exact, reliable match rather than a
    /// guess. The button itself is only actually added to a view later, via a separate
    /// call to SHGUIcommanderview.AddButtonView(button) back in the caller -- so rather
    /// than patch PrepareLevelCommanderButtonForLevel (which runs too early to stop the
    /// add, and would need to fake a return value the rest of that method's caller still
    /// expects to use safely), this patches AddButtonView itself and just declines to add
    /// this one button. AddButtonView (confirmed, SHGUIcommanderview.cs:1315) is a plain
    /// "add to list, position it after whatever's already there" -- skipping the call
    /// entirely leaves no gap and re-indexes nothing, so every other button still lands
    /// exactly where it would if this one had simply never existed.
    /// </summary>
    [HarmonyPatch(typeof(SHGUIcommanderview), nameof(SHGUIcommanderview.AddButtonView))]
    public static class SuperhotExeButtonPatch
    {
        private const string SuperhotExeCustomName = "superhot.exe";

        public static bool Prefix(SHGUIcommanderbutton button)
        {
            if (button?.ButtonText == null)
            {
                return true;
            }

            if (button.ButtonText.StartsWith(SuperhotExeCustomName))
            {
                return false;
            }

            return true;
        }
    }
}
