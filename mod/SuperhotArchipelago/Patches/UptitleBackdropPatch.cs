using HarmonyLib;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// Our AP notifications (and native LOCKED messages) render through TextManager's shared
    /// uptitle SHGUItext, which is plain white by default -- illegible in the game's many
    /// stark-white void levels. SHGUItext already supports a per-character solid backdrop
    /// (SetBackColor, confirmed via decompile of SHGUI.DrawText's SetPixelBack call), so this
    /// Postfixes CreateSHGUITextView (which (re)creates uptitleSHGUI on every scene load) and
    /// gives it a black backdrop, guaranteeing contrast against any background.
    /// </summary>
    [HarmonyPatch(typeof(TextManager), nameof(TextManager.CreateSHGUITextView))]
    public static class UptitleBackdropPatch
    {
        public static void Postfix(SHGUItext ___uptitleSHGUI)
        {
            ___uptitleSHGUI?.SetBackColor('0');
        }
    }
}
