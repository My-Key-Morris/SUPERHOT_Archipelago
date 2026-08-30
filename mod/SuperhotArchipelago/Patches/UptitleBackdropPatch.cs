using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Our AP notifications (and native LOCKED messages) render through TextManager's shared
    /// uptitle SHGUItext, which is plain white by default -- illegible over the game's many
    /// stark-white void levels, its single biggest visual motif. Tried a black per-character
    /// backdrop box first (SHGUItext.SetBackColor) plus padding to enlarge it, but live
    /// testing (NOTES.md's Rounds 43-45) showed it still read as a weak, insufficient gradient
    /// over pure white screens no matter how it was tuned, and the padding approach caused a
    /// serious regression (broke hub button clicks) along the way. Real, explicit user
    /// decision: simpler and more reliable to just make the text itself black, since white is
    /// the dominant background color throughout the game -- trades away legibility over the
    /// minority of dark/black scenes, a tradeoff the user accepted. Postfixes
    /// CreateSHGUITextView (which (re)creates uptitleSHGUI on every scene load, a genuine
    /// instance method so Harmony's "___uptitleSHGUI" field-injection is valid here) and sets
    /// its foreground color via SHGUItext's inherited SetColor(char) instead of touching the
    /// backdrop at all.
    /// </summary>
    [HarmonyPatch(typeof(TextManager), nameof(TextManager.CreateSHGUITextView))]
    public static class UptitleTextColorPatch
    {
        public static void Postfix(SHGUItext ___uptitleSHGUI)
        {
            ___uptitleSHGUI?.SetColor('0');
        }
    }
}
