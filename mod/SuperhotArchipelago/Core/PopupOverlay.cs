using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Display mechanism for every popup this mod shows (check notifications and LOCKED-style
    /// block messages). Draws through a real Unity uGUI Canvas (RenderMode.ScreenSpaceOverlay)
    /// rather than SHGUI's own terminal-text renderer, because SHGUI's text composites via an
    /// additive fullscreen shader blit (AsciiText's OnRenderImage) that washes out or vanishes
    /// entirely against the game's many pure-white/blown-out scenes -- a uGUI Canvas renders in
    /// its own pass after all camera/image-effect shaders with standard alpha blending, so it
    /// can't be cancelled the same way, and as a fully separate GameObject tree (DontDestroyOnLoad,
    /// never touching SHGUI.current.views) it also can't be starved by SHGUI's own view-queue
    /// timing.
    ///
    /// Renders text using the game's actual terminal font rather than an OS-font approximation:
    /// AsciiText.FontInit loads a public SHGUIFontAsset ScriptableObject from
    /// Resources.Load("Fonts/SHGUIFontAsset_EU"), whose FontDescription is an AngelCode-BMFont-
    /// style glyph XML. BuildGameFont() below parses that same XML into a real UnityEngine.Font's
    /// CharacterInfo[] array pointed at the same FontTexture -- the game's own glyphs, no font
    /// file to bundle. Falls back to an OS-font approximation if the asset can't be loaded (e.g.
    /// a different game version/language pack).
    ///
    /// The popup box is a plain solid-color panel sized from the message's character count, and
    /// fades in/out with a brief vertical squeeze evoking the game's own CRT/scanline transitions.
    /// See EnsureCanvas's own comment for why there's no border.
    /// </summary>
    public static class PopupOverlay
    {
        private enum State
        {
            Hidden,
            FadingIn,
            Visible,
            FadingOut,
        }

        // Every value that's purely a visual tuning number (durations, sizes, colors, the
        // vertical nudge, etc.) lives in PopupTuning instead of as consts here -- see that
        // class's own doc comment for why: tuning these by rebuild+redeploy+restart per
        // change turned out to be the actual bottleneck in getting the popup's look right.
        private static PopupTuning T => PopupTuning.Current;

        // Reference resolution for CanvasScaler's ScaleWithScreenSize mode, so the popup reads
        // at a consistent size regardless of the player's actual screen resolution. Structural
        // rather than a visual tuning knob, so it stays a real const here.
        private const int ReferenceWidth = 1920;
        private const int ReferenceHeight = 1080;

        // Suppresses re-queuing the exact same message if it comes in again within this many
        // real seconds of the last time -- several of LevelAccessGuard's callers (see
        // TitleCardGatePatch/DirectLevelSkipPatch/etc.) can end up calling Show() more than
        // once for what's really a single blocked attempt, when the native code they patch
        // keeps retrying every frame rather than just once per press/click. Centralizing the
        // de-dupe here protects every call site at once instead of chasing down which native
        // method retries and patching a one-off guard into each.
        private const float DedupeWindowSeconds = 1f;

        private static RectTransform? _panel;
        private static CanvasGroup? _canvasGroup;
        private static Text? _text;
        private static readonly Queue<string> _queue = new();

        // Set by BuildGameFont() on success, left null on the OS-font fallback path. Text's
        // own preferredWidth/preferredHeight turned out unusable for sizing the box against
        // the real game font (see DisplayNext's comment) -- these let DisplayNext measure text
        // itself instead, using the same fixed per-character/per-line cell size AsciiText's
        // own terminal grid advances by.
        private static float? _fixedCharAdvance;
        private static float? _fixedLineHeight;

        private static State _state = State.Hidden;
        private static float _timer;

        private static string? _lastShownMessage;
        private static float _lastShownRealtime = float.NegativeInfinity;

        /// <summary>Queues a message; shown once whatever's currently up (if anything) finishes.</summary>
        public static void Show(string text)
        {
            // The timestamp is refreshed whether or not this call turns out to be a duplicate,
            // so a sustained retry (e.g. a skip button held down against a blocked level)
            // keeps sliding the suppression window forward instead of letting a duplicate
            // back in the moment DedupeWindowSeconds has passed since the *first* attempt.
            bool isDuplicate = text == _lastShownMessage
                && Time.unscaledTime - _lastShownRealtime < DedupeWindowSeconds;
            _lastShownMessage = text;
            _lastShownRealtime = Time.unscaledTime;

            if (isDuplicate)
            {
                return;
            }

            _queue.Enqueue(text);
        }

        /// <summary>
        /// No-op: unlike the old SHGUItext-backed version, this Canvas is DontDestroyOnLoad
        /// and never touches SHGUI's own per-scene view tree, so there's nothing to rebuild on
        /// scene load. Kept as a real method (not removed) so Mod.OnSceneWasLoaded's existing
        /// call site doesn't need to change.
        /// </summary>
        public static void OnSceneLoaded()
        {
        }

        /// <summary>Called every frame from Mod.OnUpdate() to advance the queue/transition/display timer.</summary>
        public static void Update()
        {
            EnsureCanvas();

            switch (_state)
            {
                case State.Hidden:
                    if (_queue.Count > 0)
                    {
                        DisplayNext();
                    }
                    break;

                case State.FadingIn:
                    _timer += Time.unscaledDeltaTime;
                    ApplyTransition(Mathf.Clamp01(_timer / T.TransitionSeconds));
                    if (_timer >= T.TransitionSeconds)
                    {
                        _state = State.Visible;
                        _timer = T.DisplaySeconds;
                    }
                    break;

                case State.Visible:
                    _timer -= Time.unscaledDeltaTime;
                    if (_timer <= 0f)
                    {
                        _state = State.FadingOut;
                        _timer = 0f;
                    }
                    break;

                case State.FadingOut:
                    _timer += Time.unscaledDeltaTime;
                    ApplyTransition(1f - Mathf.Clamp01(_timer / T.TransitionSeconds));
                    if (_timer >= T.TransitionSeconds)
                    {
                        _state = State.Hidden;
                        _panel!.gameObject.SetActive(false);
                    }
                    break;
            }
        }

        // t=0 is fully collapsed/transparent (start of fade-in, end of fade-out), t=1 is fully
        // shown -- squeezes the box to a thin horizontal sliver rather than just fading alpha,
        // meant to read closer to a scan-line opening/closing than a plain UI fade.
        private static void ApplyTransition(float t)
        {
            _canvasGroup!.alpha = t;
            _panel!.localScale = new Vector3(1f, Mathf.Lerp(T.MinScaleY, 1f, t), 1f);
        }

        /// <summary>
        /// Tears down the current Canvas (if any) so the next Update() rebuilds it fresh from
        /// PopupTuning.json, and reloads that file. Wired to a debug hotkey (see Mod.OnUpdate)
        /// so tuning changes can be seen by pressing a key and reading a fresh file, instead of
        /// a full rebuild + redeploy + game restart per value tweaked.
        /// </summary>
        public static void ReloadTuning()
        {
            if (_canvasRoot != null)
            {
                UnityEngine.Object.Destroy(_canvasRoot);
            }

            _canvasRoot = null;
            _text = null;
            _panel = null;
            _canvasGroup = null;
            _fixedCharAdvance = null;
            _fixedLineHeight = null;
            _state = State.Hidden;
            _timer = 0f;
        }

        private static GameObject? _canvasRoot;

        private static void EnsureCanvas()
        {
            if (_text != null)
            {
                return;
            }

            PopupTuning.Load();

            var rootGO = new GameObject("SuperhotArchipelago_PopupCanvas");
            _canvasRoot = rootGO;
            UnityEngine.Object.DontDestroyOnLoad(rootGO);

            var canvas = rootGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Arbitrary high value -- this is expected to be the only ScreenSpaceOverlay
            // canvas this mod (or the base game, which has none) ever creates, so the exact
            // number doesn't matter, only that it stays on top of anything else that shows up.
            canvas.sortingOrder = 1000;

            var scaler = rootGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Panel anchored by a single top-center point (pivot (0.5, 1)) rather than a
            // stretch band, so its size can track the current message's length -- set fresh
            // in DisplayNext() -- without needing to recompute a vertical position each time.
            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(rootGO.transform, worldPositionStays: false);
            _panel = panelGO.AddComponent<RectTransform>();
            _panel.anchorMin = new Vector2(0.5f, T.TopAnchorY);
            _panel.anchorMax = new Vector2(0.5f, T.TopAnchorY);
            _panel.pivot = new Vector2(0.5f, 1f);

            // Drives the fade in/out; scaling happens on _panel's own Transform directly (see
            // ApplyTransition) since CanvasGroup only controls alpha/interactability, not scale.
            _canvasGroup = panelGO.AddComponent<CanvasGroup>();

            // Solid full-stretch background fill (no sprite assigned -- Image defaults to a
            // solid-color quad without one). An ASCII box-drawing border (literal '_'/'|'
            // characters, matching SUPERHOT's own hub UI) was tried here and lived for a few
            // rounds, but was pulled entirely per direct user feedback ("too much") once seen
            // live against real gameplay -- a plain solid panel reads cleaner. See git history
            // if that look is ever revisited.
            CreateStretchImage(_panel, "Background", T.BackgroundColor, 0f);

            Font resolvedFont = ResolveFont();

            // Not stretch-anchored like the background -- sized directly to the text's own
            // (unscaled) preferred size and then magnified via localScale (see PixelScale's
            // own comment), so the scale-up doesn't also stretch the rect it's centered in.
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(_panel, worldPositionStays: false);
            RectTransform textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.localScale = new Vector3(T.PixelScale, T.PixelScale, 1f);

            _text = textGO.AddComponent<Text>();
            _text.font = resolvedFont;
            _text.fontSize = T.FontSize;
            _text.fontStyle = FontStyle.Bold;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.color = Color.white;

            // The real game font's cell (CharSize.y) has headroom above typical glyphs baked
            // in (room for accents/diacritics most Latin text never uses), so MiddleCenter's
            // vertical centering -- which centers the *cell*, not the actual ink -- leaves
            // real text sitting visibly above true center with empty space pooling below.
            // Nudging the Text object itself down by a fraction of that cell height corrects
            // for it directly. TextVerticalNudgeFraction was tuned live; only applies on the
            // real-game-font path (_fixedLineHeight is null on the OS-font fallback, which
            // doesn't have this particular cell-padding quirk).
            if (_fixedLineHeight.HasValue)
            {
                // *PixelScale here because this offset is applied in the panel's coordinate
                // space, which is already magnified relative to the font's raw cell units --
                // without it the nudge is too small by that same factor (see PopupTuning's
                // own comment for how that shipped wrong once already).
                textRect.anchoredPosition = new Vector2(0f, -_fixedLineHeight.Value * T.PixelScale * T.TextVerticalNudgeFraction);
            }

            // Point-filtering the font's glyph texture is what makes PixelScale's magnification
            // actually look blocky/pixelated instead of just blurry -- without this, scaling up
            // a small-rasterized glyph bitmap linearly-interpolates it into a smooth blur. Both
            // Text components share the same Font/Material, so filtering it once here covers
            // the border grid too.
            if (resolvedFont.material != null && resolvedFont.material.mainTexture != null)
            {
                resolvedFont.material.mainTexture.filterMode = FilterMode.Point;
            }

            panelGO.SetActive(false);
        }

        private static Image CreateStretchImage(Transform parent, string name, Color color, float inset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);

            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        // Where AsciiText itself loads the game's own font asset from (GetFontAssetPathForLang,
        // decompile-confirmed) -- hardcoded to the EU/Latin variant regardless of the player's
        // actual game language, since this mod's own popup text is always English regardless
        // of the UI language and the EU asset's Latin glyph set is what covers it.
        private const string GameFontAssetPath = "Fonts/SHGUIFontAsset_EU";

        // Preferred monospace OS fonts, tried only if the real game font (BuildGameFont)
        // couldn't be loaded -- closer to a terminal look than Arial as a fallback, tried in
        // order via CreateDynamicFontFromOSFont's array overload (returns the first installed
        // match), with the builtin Arial.ttf resource as a last resort so there's always
        // *some* usable font rather than none (which renders nothing).
        private static readonly string[] PreferredMonospaceFonts =
        {
            "Consolas", "Courier New", "DejaVu Sans Mono", "Lucida Console", "Monospace",
        };

        private static Font ResolveFont()
        {
            Font? gameFont = BuildGameFont();
            if (gameFont != null)
            {
                return gameFont;
            }

            Font dynamic = Font.CreateDynamicFontFromOSFont(PreferredMonospaceFonts, T.FontSize);
            if (dynamic != null)
            {
                return dynamic;
            }

            Font builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (builtin != null)
            {
                return builtin;
            }

            return Font.CreateDynamicFontFromOSFont("Arial", T.FontSize);
        }

        // Builds a real UnityEngine.Font from the game's own SHGUIFontAsset (see this class's
        // doc comment) by parsing its BMFont-style XML into a CharacterInfo[] array and
        // pointing the Font's material straight at the same FontTexture the game itself uses.
        // Returns null (rather than throwing) on any missing/malformed piece, so ResolveFont
        // can fall back to an OS font instead of leaving the popup with no font at all.
        private static Font? BuildGameFont()
        {
            SHGUIFontAsset fontAsset = Resources.Load<SHGUIFontAsset>(GameFontAssetPath);
            if (fontAsset == null || fontAsset.FontTexture == null || fontAsset.FontDescription == null)
            {
                return null;
            }

            Texture2D texture = fontAsset.FontTexture;
            float texWidth = texture.width;
            float texHeight = texture.height;

            var characters = new List<CharacterInfo>();
            foreach (XElement charElement in XDocument.Parse(fontAsset.FontDescription.text).Descendants("char"))
            {
                if (!int.TryParse(charElement.Attribute("id")?.Value, out int id)
                    || !int.TryParse(charElement.Attribute("x")?.Value, out int x)
                    || !int.TryParse(charElement.Attribute("y")?.Value, out int y)
                    || !int.TryParse(charElement.Attribute("width")?.Value, out int width)
                    || !int.TryParse(charElement.Attribute("height")?.Value, out int height))
                {
                    continue;
                }

                // The XML's x/y are image-space (origin top-left, like every BMFont-style
                // description) pixel coordinates into FontTexture, but Unity's own UV space
                // has v=0 at the *bottom* of a texture (a fixed consequence of how Unity
                // stores imported image data, regardless of which shader later samples it --
                // not specific to AsciiText's own shader convention) -- so v has to be flipped
                // here even though AsciiText's own Charset lookup (decompiled from FontInit)
                // just divides x/y by texture size with no flip, because that lookup only ever
                // feeds AsciiText's own custom shader, not Unity's standard UV convention this
                // Font/Text pipeline relies on.
                float u0 = x / texWidth;
                float u1 = (x + width) / texWidth;
                float vTop = 1f - y / texHeight;
                float vBottom = 1f - (y + height) / texHeight;

                // CharacterInfo.uv/vert/width are marked [Obsolete] in favor of
                // uvTopLeft/uvBottomRight/minX/maxX/advance-style convenience properties, but
                // those setters read back other already-set corners to compute width/height --
                // that only works when editing an existing Rect, not building one from scratch
                // here, so the raw (still fully functional) fields are used directly instead.
                // The font atlas carries no per-glyph baseline/offset metadata at all (every
                // XML entry has identical xoffset="0" yoffset="0" regardless of glyph, confirmed
                // via debug logging real entries), so there's no real data to say capitals
                // "should" sit anywhere different from lowercase -- this is a purely cosmetic,
                // ungrounded nudge, applied only to 'A'-'Z' (ASCII 65-90), tunable live via
                // PopupTuning.CapitalLetterNudge (in the same raw cell-pixel units as CharSize)
                // instead of baked in here, since getting this look right is guesswork either way.
                float vertTop = fontAsset.CharSize.y;
                if (id is >= 65 and <= 90)
                {
                    vertTop -= T.CapitalLetterNudge;
                }

#pragma warning disable CS0618
                var info = new CharacterInfo
                {
                    index = id,
                    uv = new Rect(u0, vBottom, u1 - u0, vTop - vBottom),
                    // vert.y is the fixed cell height (not this glyph's own, shorter height)
                    // for *every* character, so every glyph hangs from the same shared top
                    // line rather than each other's own ink height -- matching how AsciiText
                    // itself renders every cell flush within its fixed-size grid. This also
                    // matters for a reason that has nothing to do with individual glyph shape:
                    // Font.ascent/lineHeight are read-only and derived internally from the
                    // characterInfo array as a whole, not per string. The first live test used
                    // each glyph's own (often much shorter) height here, so whichever
                    // character in the *entire* font happened to have the tallest XML height
                    // set the derived ascent -- taller than most real strings' own glyphs --
                    // and Text's MiddleCenter alignment centered against that inflated ascent,
                    // pushing visibly shorter strings up against the top of the box instead of
                    // centering them. Sharing one fixed top reference across every character
                    // makes the derived ascent match the box's real height consistently.
                    // Capitals get vertTop (nudged down by CapitalLetterNudge) instead of the
                    // shared top directly -- see the comment above this block for why.
                    vert = new Rect(0f, vertTop, width, -height),
                    // .width is the backing field for the (rounded-to-int) advance property --
                    // set to the font asset's fixed cell width (not this glyph's own width) so
                    // spacing matches AsciiText's fixed-width terminal grid rather than this
                    // one glyph's ink width.
                    width = fontAsset.CharSize.x,
                };
#pragma warning restore CS0618

                characters.Add(info);
            }

            if (characters.Count == 0)
            {
                return null;
            }

            var font = new Font
            {
                material = new Material(Shader.Find("UI/Default")) { mainTexture = texture },
                characterInfo = characters.ToArray(),
            };

            _fixedCharAdvance = fontAsset.CharSize.x;
            _fixedLineHeight = fontAsset.CharSize.y;
            return font;
        }

        private static void DisplayNext()
        {
            string text = _queue.Dequeue();
            _panel!.gameObject.SetActive(true);

            float cellWidth = _fixedCharAdvance ?? T.FontSize * 0.6f;
            float cellHeight = _fixedLineHeight ?? T.FontSize * 1.2f;

            // MaxWidthFraction/MaxHeightFraction, converted to raw character cells the same way
            // PaddingHorizontalCells/PaddingVerticalCells already are -- real, explicit user
            // report: an ordinary one-sentence LOCKED message spanned nearly the entire screen
            // once rendered at the real game font's actual pixel size, since nothing previously
            // capped how wide a single unwrapped line could get. Subtracting the padding here
            // (rather than after wrapping) reserves room for it up front, so the padded box
            // still respects the fraction rather than exceeding it by the padding amount.
            int maxCols = Mathf.Max(4, Mathf.FloorToInt(T.MaxWidthFraction * ReferenceWidth / (cellWidth * T.PixelScale)) - T.PaddingHorizontalCells * 2);
            text = WrapText(text, maxCols);
            _text!.text = text;

            // Text.preferredWidth/preferredHeight turned out unusable for a manually-built
            // static/bitmap Font: an early live test with the real font rendered correctly
            // but massively overflowed a box sized from those properties, a known rough edge
            // in Unity's legacy TextGenerator around custom CharacterInfo it didn't itself
            // generate. The panel is instead sized from the message's own raw character-count
            // dimensions (below) rather than any pixel measurement of the rendered text, so
            // preferredWidth/Height aren't needed at all, even on the OS-font fallback path --
            // cellWidth/cellHeight's own fallback estimate covers it.
            string[] lines = text.Split('\n');
            int longestLine = 0;
            foreach (string line in lines)
            {
                longestLine = Math.Max(longestLine, line.Length);
            }

            // Panel (and its Background fill) sized directly from the message's own
            // character-count dimensions (longestLine/lines.Length) plus PaddingHorizontalCells/
            // PaddingVerticalCells of breathing room on each side, in whole character cells --
            // not any pixel measurement of the rendered text, so the requested padding always
            // survives instead of rounding away to nothing.
            int paddedCols = longestLine + T.PaddingHorizontalCells * 2;
            int paddedRows = lines.Length + T.PaddingVerticalCells * 2;

            // Clamped again here as cheap insurance against off-by-one rounding in the cell
            // math above -- WrapText already keeps width within budget, so this should
            // normally be a no-op; height has no equivalent reflow (wrapping only affects
            // width), so this is what actually keeps a pathologically long message's box
            // from growing past MaxHeightFraction instead of merely a suggestion.
            _panel.sizeDelta = new Vector2(
                Mathf.Min(paddedCols * cellWidth * T.PixelScale, T.MaxWidthFraction * ReferenceWidth),
                Mathf.Min(paddedRows * cellHeight * T.PixelScale, T.MaxHeightFraction * ReferenceHeight));

            _state = State.FadingIn;
            _timer = 0f;
            ApplyTransition(0f);
        }

        // Greedy word-wrap: breaks each existing line ('\n'-separated, so any message that
        // deliberately wants multiple lines still gets them) into as many additional lines as
        // needed to keep every line within maxCols characters, without ever splitting a single
        // word. A word alone longer than maxCols is left on its own line rather than being cut
        // mid-word -- rare in practice (level names/messages are hand-authored, not arbitrary
        // user input) and a slightly-too-wide line degrades far more gracefully than truncated
        // or hyphenated text would.
        private static string WrapText(string text, int maxCols)
        {
            var outputLines = new List<string>();
            foreach (string paragraph in text.Split('\n'))
            {
                if (paragraph.Length <= maxCols)
                {
                    outputLines.Add(paragraph);
                    continue;
                }

                var current = new StringBuilder();
                foreach (string word in paragraph.Split(' '))
                {
                    if (current.Length == 0)
                    {
                        current.Append(word);
                    }
                    else if (current.Length + 1 + word.Length <= maxCols)
                    {
                        current.Append(' ').Append(word);
                    }
                    else
                    {
                        outputLines.Add(current.ToString());
                        current.Clear();
                        current.Append(word);
                    }
                }

                if (current.Length > 0)
                {
                    outputLines.Add(current.ToString());
                }
            }

            return string.Join("\n", outputLines);
        }
    }
}
