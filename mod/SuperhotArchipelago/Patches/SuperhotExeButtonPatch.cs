using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Hides the "superhot.exe" hub shortcut by declining to add it in AddButtonView,
    /// rather than removing it after the fact (avoids navigation-index bugs). Matching on
    /// the ButtonText prefix works because "superhot.exe" is exactly 12 chars, so it's
    /// never truncated or padded away.
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
