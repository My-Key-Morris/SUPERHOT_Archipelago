using UnityEngine;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Real, explicit user follow-up to Rounds 11-13: not just a hub button, but a
    /// genuinely native-styled connection screen -- "actually in the game... like how the
    /// settings are" -- instead of a Unity IMGUI window floating on top of everything.
    ///
    /// This replaces Core/ConnectionUI.cs entirely (removed, along with
    /// Patches/ConnectionCursorPatch.cs -- that whole cursor-visibility fight goes away by
    /// construction once nothing here needs a mouse). Built on the game's own real "app
    /// screen" framework instead of Unity IMGUI:
    ///
    /// - SHGUIappbase (confirmed via decompile) is the base every simple bordered pop-up
    ///   app screen uses -- it already draws a frame, a title label, and an "Esc" hint, and
    ///   already handles Escape-to-close. Free UI chrome, no need to reinvent it.
    /// - SHGUItext (confirmed via decompile) is the game's own positioned, colored text
    ///   widget -- the same primitive every hub label/status string already uses.
    /// - Free-text keyboard entry has a real, working precedent in this exact game:
    ///   AppSHConsole (the native dev console, confirmed via decompile) accumulates typed
    ///   characters every frame via Input.inputString, manually strips backspace ('\b') and
    ///   submits on carriage return ('\r'), and draws a blinking caret with
    ///   Mathf.Sin(Time.realtimeSinceStartup * 10f). This class reuses that exact pattern
    ///   for three fields instead of AppSHConsole's one, with Tab cycling which field is
    ///   focused (SHGUItext.color, inherited from SHGUIview, is a plain settable field --
    ///   used here to make only the focused field's label white, others grey, same 'w'/'z'
    ///   convention as everywhere else in this mod).
    /// - Launched directly via SHGUI.current.AddViewOnTop(new ArchipelagoConnectApp()) from
    ///   Patches/ConnectionButtonPatch.cs's button -- confirmed via decompile this is the
    ///   same general mechanism SHGUI.LaunchAppByName uses internally, no name-registration
    ///   system required to use it directly.
    ///
    /// Deliberately overrides ReactToInputKeyboard to swallow "enter" (SHGUIappbase's own
    /// version treats Enter as "close the app", which would fight this screen's own use of
    /// Enter to advance between fields/submit) while still forwarding "esc" to close, same
    /// as every other app screen in the game.
    ///
    /// Real bug found by playtesting: forwarding "esc" through ReactToInputKeyboard alone
    /// wasn't enough -- Escape didn't close the screen. Update() below now also reads
    /// Input.GetKeyDown(KeyCode.Escape) directly and closes via SHGUI.current.PopView(),
    /// matching how AppSHConsole itself handles Escape (it doesn't trust the enum-dispatch
    /// path alone either, despite inheriting the same SHGUIappbase machinery). The
    /// ReactToInputKeyboard override is kept as a harmless backup, not the real fix.
    /// </summary>
    public class ArchipelagoConnectApp : SHGUIappbase
    {
        private const int FieldCount = 3;

        // Kept comfortably under the app frame's real width (AppSHConsole's own
        // user-visible line-wrap width is 59, for reference) for margin rather than
        // measured exactly against SHGUI.current.resolutionX.
        private const int StatusLineWidth = 50;

        private readonly SHGUItext[] _labels = new SHGUItext[FieldCount];
        private readonly SHGUItext[] _values = new SHGUItext[FieldCount];
        private readonly string[] _labelText = { "SERVER", "SLOT", "PASSWORD" };
        private readonly string[] _buffers = new string[FieldCount];
        private readonly bool[] _masked = { false, false, true };

        private SHGUItext _statusField = null!;
        private int _focusedField;

        public ArchipelagoConnectApp() : base("ARCHIPELAGO")
        {
            _buffers[0] = Config.Server.Value;
            _buffers[1] = Config.Slot.Value;
            _buffers[2] = Config.Password.Value;

            const int x = 3;
            int y = 3;
            for (int i = 0; i < FieldCount; i++)
            {
                _labels[i] = (AddSubView(new SHGUItext(_labelText[i], x, y, 'z')) as SHGUItext)!;
                _values[i] = (AddSubView(new SHGUItext("", x, y + 1, 'w')) as SHGUItext)!;
                y += 3;
            }

            _statusField = (AddSubView(new SHGUItext("", x, y, 'z')) as SHGUItext)!;
            y += 5; // room for StatusText() to wrap up to a few lines -- see RefreshDisplay
            AddSubView(new SHGUItext("[TAB] switch field   [ENTER] next / connect", x, y, 'z'));

            RefreshDisplay();
        }

        public override void Update()
        {
            base.Update();

            // Real bug found by playtesting: Esc didn't close this screen. Root cause --
            // SHGUIappbase.ReactToInputKeyboard (which this class forwards "esc" to, see
            // below) apparently isn't a reliable path for this class, at least not on its
            // own. Real, working precedent for exactly that: AppSHConsole (the game's own
            // native dev console, confirmed via decompile) doesn't rely on that dispatch
            // for Escape either -- it reads Input.GetKeyDown(KeyCode.Escape) directly every
            // frame and closes itself that way, even though it inherits the same
            // ReactToInputKeyboard machinery. Matching that same proven pattern here
            // instead of trusting the enum-dispatch path alone.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SHGUI.current.PopView();
                Input.ResetInputAxes();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _focusedField = (_focusedField + 1) % FieldCount;
            }

            string buffer = _buffers[_focusedField];
            if (Input.inputString.Length > 0)
            {
                buffer += Input.inputString;
            }

            // Same two passes AppSHConsole uses: strip backspaces first (each '\b' also
            // removes the character before it), then look for a submitted line ('\r').
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == '\b')
                {
                    buffer = buffer.Remove(i--, 1);
                    if (i >= 0)
                    {
                        buffer = buffer.Remove(i--, 1);
                    }
                }
            }

            bool submitted = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == '\r' || buffer[i] == '\n')
                {
                    buffer = buffer.Remove(i, buffer.Length - i);
                    submitted = true;
                    break;
                }
            }

            _buffers[_focusedField] = buffer;

            if (submitted)
            {
                if (_focusedField < FieldCount - 1)
                {
                    _focusedField++;
                }
                else
                {
                    Connect();
                }
            }

            RefreshDisplay();
            Input.ResetInputAxes();
        }

        public override void ReactToInputKeyboard(SHGUIinput key)
        {
            if (key == SHGUIinput.esc)
            {
                base.ReactToInputKeyboard(key);
            }

            // Deliberately not forwarding "enter" -- see class docstring. This screen
            // handles it directly in Update() instead of letting the base class close on it.
        }

        private void Connect()
        {
            Mod.ApplyConnectionSettingsAndConnect(_buffers[0], _buffers[1], _buffers[2]);
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            string caret = (Mathf.Sin(Time.realtimeSinceStartup * 10f) > 0f) ? "_" : "";

            for (int i = 0; i < FieldCount; i++)
            {
                bool focused = i == _focusedField;
                _labels[i].color = focused ? 'w' : 'z';

                string shown = _masked[i] ? new string('*', _buffers[i].Length) : _buffers[i];
                _values[i].text = shown + (focused ? caret : "");
            }

            // Real bug found by playtesting: a long status message (e.g. a real connection
            // error) ran straight off the right edge of the app frame instead of wrapping.
            // SHGUItext.BreakTextForLineLength (confirmed via decompile -- it just inserts
            // '\n' into .text wherever a line would exceed the given length, same primitive
            // AppSHConsole's own multi-line output uses) fixes that directly; StatusLineWidth
            // is set well under the frame's real width for margin, not measured exactly.
            _statusField.text = "STATUS: " + StatusText();
            _statusField.BreakTextForLineLength(StatusLineWidth);
        }

        private string StatusText()
        {
            ArchipelagoConnection? connection = Mod.Connection;
            if (connection == null)
            {
                return "NOT INITIALIZED";
            }

            if (connection.IsConnected)
            {
                return $"CONNECTED AS '{Config.Slot.Value}'";
            }

            if (!string.IsNullOrEmpty(connection.LastError))
            {
                return $"ERROR -- {connection.LastError}";
            }

            return "NOT CONNECTED";
        }
    }
}
