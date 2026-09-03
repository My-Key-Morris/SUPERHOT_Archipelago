using UnityEngine;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Native in-game connection screen (server/slot/password) built on SHGUIappbase,
    /// replacing the old Unity IMGUI popup. Text entry mirrors
    /// AppSHConsole's own pattern (Input.inputString, backspace/enter handling, blinking
    /// caret) across three Tab-cycled fields instead of one.
    ///
    /// ReactToInputKeyboard swallows "enter" so the base class doesn't treat it as "close
    /// app" (this screen uses Enter to advance fields/submit instead), while "esc" is still
    /// forwarded. Update() also reads Escape directly since the ReactToInputKeyboard path
    /// alone didn't reliably close the screen.
    /// </summary>
    public class ArchipelagoConnectApp : SHGUIappbase
    {
        private const int FieldCount = 3;

        // Kept under the app frame's real width for margin, not measured exactly.
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

            // ReactToInputKeyboard's "esc" dispatch alone doesn't reliably close this
            // screen, so Escape is also read directly here, same pattern AppSHConsole uses.
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

            // Same two passes AppSHConsole uses: strip backspaces first, then look for a
            // submitted line ('\r').
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

            // Enter isn't forwarded -- see class summary; Update() handles it directly
            // instead of letting the base class close on it.
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

            // Long status messages (e.g. connection errors) would otherwise run off the
            // frame edge; BreakTextForLineLength inserts newlines to wrap them.
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
