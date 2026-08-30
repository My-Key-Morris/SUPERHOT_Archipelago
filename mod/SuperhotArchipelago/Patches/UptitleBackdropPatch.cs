using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Our AP notifications (and native LOCKED messages) render through TextManager's shared
    /// uptitle SHGUItext, which is plain white by default -- illegible over the game's many
    /// stark-white void levels, its single biggest visual motif. Tried black text alone
    /// (Round 46) on the theory that white backgrounds dominate, but live testing showed a
    /// scene where the native "ShowUptitle" vignette effect visibly darkened the uptitle's own
    /// band -- black text over that darkened band was completely invisible, worse than the
    /// original white-on-white case. Back to white text (SHGUItext's own default, so no
    /// SetColor call needed) plus a black per-character backdrop (SetBackColor, confirmed via
    /// decompile of SHGUI.DrawText's SetPixelBack call) -- known imperfect (NOTES.md's Rounds
    /// 42-45: the backdrop alone still reads as weak on pure white), but strictly better than
    /// black text vanishing entirely against a darkened background. Postfixes
    /// CreateSHGUITextView (which (re)creates uptitleSHGUI on every scene load, a genuine
    /// instance method so Harmony's "___uptitleSHGUI" field-injection is valid here).
    /// </summary>
    [HarmonyPatch(typeof(TextManager), nameof(TextManager.CreateSHGUITextView))]
    public static class UptitleBackdropPatch
    {
        public static void Postfix(SHGUItext ___uptitleSHGUI)
        {
            if (___uptitleSHGUI == null)
            {
                return;
            }

            ___uptitleSHGUI.SetColor('w');
            ___uptitleSHGUI.SetBackColor('0');
        }
    }
}
