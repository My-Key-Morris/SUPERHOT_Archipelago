using System;
using System.Collections.Generic;
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

    /// <summary>
    /// The black backdrop above only covers each character's own cell, so a user could still
    /// see native "ShowUptitle" -- a screen-space gradient/vignette effect (asset-driven, not
    /// inspectable via decompile) meant to darken behind the uptitle -- bleeding around it
    /// instead of a clean box, and it doesn't reach past a short single-line message's height.
    /// Since SHGUItext draws a backdrop under spaces too (DrawSpaces = true on every
    /// TextManager-owned SHGUItext), padding the queued text with margin spaces and blank
    /// rows makes our own solid black box bigger than the text itself, on all sides, without
    /// depending on that native effect at all. Applied once here (a Prefix on
    /// AddUptitleToQueue, ref-rewriting its parameter) rather than at each of this mod's 7
    /// call sites, so every current and future uptitle message gets it automatically.
    /// </summary>
    [HarmonyPatch(typeof(TextManager), nameof(TextManager.AddUptitleToQueue))]
    public static class UptitlePaddingPatch
    {
        private const int HorizontalMargin = 2;

        public static void Prefix(ref LocalizableText text)
        {
            if (text == null)
            {
                return;
            }

            text = new LocalizableText(Pad(text.Get()));
        }

        private static string Pad(string raw)
        {
            string[] lines = raw.Split('\n');
            int width = 0;
            foreach (string line in lines)
            {
                width = Math.Max(width, line.Length);
            }
            width += HorizontalMargin * 2;

            string blankRow = new string(' ', width);
            var padded = new List<string> { blankRow };
            foreach (string line in lines)
            {
                int leftPad = (width - line.Length) / 2;
                int rightPad = width - line.Length - leftPad;
                padded.Add(new string(' ', leftPad) + line + new string(' ', rightPad));
            }
            padded.Add(blankRow);

            return string.Join("\n", padded.ToArray());
        }
    }
}
