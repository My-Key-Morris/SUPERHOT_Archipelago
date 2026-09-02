using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Every visual constant PopupOverlay uses, externalized to a JSON file next to the mod
    /// DLL instead of being baked into the compiled code -- added after several rounds of
    /// "tweak a number, rebuild, redeploy, cold-boot SUPERHOT, trigger a popup, screenshot,
    /// repeat" turned out to be the actual bottleneck in getting the popup's look right, not
    /// the underlying logic. MelonLoader can't hot-swap a loaded assembly, so any change to
    /// PopupOverlay's actual *code* still needs a real rebuild + redeploy + game restart --
    /// this only helps for the numbers in this one file. Paired with a debug hotkey (F9, see
    /// Mod.OnUpdate) that reloads this file and shows a sample popup on demand, from any
    /// scene, without needing a real check/lock event to trigger one.
    /// </summary>
    public class PopupTuning
    {
        public float DisplaySeconds = 2.5f;
        public float TransitionSeconds = 0.12f;
        public float MinScaleY = 0.05f;
        public int FontSize = 16;
        public float PixelScale = 1.6f;
        public float TextVerticalNudgeFraction = 0.48f;
        public float TopAnchorY = 0.9f;

        // Fraction of the reference screen width/height (ReferenceWidth/ReferenceHeight in
        // PopupOverlay.cs, 1920x1080) the popup panel may not exceed -- real, explicit user
        // report: an ordinary one-sentence LOCKED message spanned nearly the entire screen
        // once rendered at the real game font's actual pixel size, since nothing previously
        // capped how wide a single unwrapped line could get. A message that would exceed this
        // width instead wraps onto additional lines (see PopupOverlay.WrapText); height has no
        // equivalent reflow, so MaxHeightFraction is a hard clamp on the panel's final pixel
        // size instead, for the rare pathologically long/multi-line message.
        public float MaxWidthFraction = 0.6f;
        public float MaxHeightFraction = 0.5f;

        // Extra blank character cells between the message text and the panel's edges on each
        // side (so the total added width is double this value) -- replaced the old pixel-based
        // BorderThickness/TextPadding fields, which sized the panel from a raw pixel amount
        // that could round away to nothing once divided back into whole character cells for an
        // ASCII border grid this project tried and later removed (see NOTES.md's "Round 49"
        // entries) -- these size the panel directly in cell units instead, so requested padding
        // always survives as whole cells regardless of what's drawn inside it.
        public int PaddingHorizontalCells = 2;

        // Same reasoning as the horizontal padding above.
        public int PaddingVerticalCells = 1;

        // Purely cosmetic, ungrounded nudge applied only to 'A'-'Z' in BuildGameFont() -- the
        // font atlas has no real per-glyph baseline data to justify any specific value, so this
        // starts at 0 (off) until tuned live via F9 + editing this file. Same raw cell-pixel
        // units as CharSize (typically ~38px tall cells); positive values shift capitals down.
        public float CapitalLetterNudge = 0f;

        public float BackgroundR = 0f;
        public float BackgroundG = 0.01f;
        public float BackgroundB = 0.02f;
        public float BackgroundA = 0.92f;

        [JsonIgnore]
        public Color BackgroundColor => new Color(BackgroundR, BackgroundG, BackgroundB, BackgroundA);

        private static string FilePath =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                "PopupTuning.json");

        public static PopupTuning Current { get; private set; } = new();

        /// <summary>
        /// Loads PopupTuning.json next to the mod DLL if present, otherwise writes one out
        /// with the current defaults -- so there's always a real file to open and edit rather
        /// than needing to know these field names/current values up front.
        /// </summary>
        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    PopupTuning? loaded = JsonConvert.DeserializeObject<PopupTuning>(json);
                    if (loaded != null)
                    {
                        Current = loaded;
                        return;
                    }
                }

                Current = new PopupTuning();
                Save();
            }
            catch (Exception ex)
            {
                Mod.Log?.LogError($"PopupTuning.Load failed, using defaults: {ex}");
                Current = new PopupTuning();
            }
        }

        private static void Save()
        {
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));
        }
    }
}
