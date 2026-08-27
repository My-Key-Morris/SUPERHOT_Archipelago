using System.Collections.Generic;
using UnityEngine;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Real, explicit user request (Notifications feature): "a log section in the hub to
    /// see older notifications", alongside the live popups NotificationLog.Add already
    /// queues via TextManager.AddUptitleToQueue for genuinely new items/checks. Opened by
    /// Patches/ArchipelagoLogButtonPatch.cs's hub button, same launch mechanism
    /// Patches/ConnectionButtonPatch.cs uses for Core/ArchipelagoConnectApp.cs.
    ///
    /// Considered reusing the native SHGUIcommanderview/SHGUIcommanderbutton list
    /// framework instead (the same one LEVELS/CHALLENGES/etc. use, confirmed to already
    /// support scrolling/pagination via its own "-MORE-" indicator) rather than building
    /// scrolling by hand here. Went with a plain SHGUIappbase + SHGUItext screen instead,
    /// matching ArchipelagoConnectApp.cs's precedent, because commanderview's buttons are
    /// built for *selecting* an item (they navigate somewhere on click) -- this screen has
    /// nothing to select, it's read-only history, so that framework's extra machinery
    /// wouldn't be buying anything here. Revisit if this hand-rolled scrolling ever proves
    /// awkward in practice.
    ///
    /// Entries are shown newest-first (NotificationLog.Entries is oldest-first, so this
    /// reverses it) -- opening the screen should show what just happened without needing
    /// to scroll, matching how a player would actually use this (glance at recent
    /// activity; only scroll further for real history-digging).
    /// </summary>
    public class ArchipelagoLogApp : SHGUIappbase
    {
        // How many entries are visible on screen at once. Kept comfortably under what the
        // app frame actually fits (measured against ArchipelagoConnectApp's own field
        // layout, which uses about half the frame height for 3 fields) rather than
        // measured pixel-exact.
        private const int VisibleLines = 14;

        // Same reasoning as ArchipelagoConnectApp.StatusLineWidth -- kept under the frame's
        // real width for margin. Log lines are truncated (not wrapped) at this length
        // rather than using SHGUItext.BreakTextForLineLength, so each entry always occupies
        // exactly one screen line and the scrolling math below stays simple. Real, explicit
        // user report: entries were getting cut off mid-word. The real fix was shortening
        // the text itself (see LevelCatalog.TryGetShortItemDisplayName) -- this was bumped
        // slightly too, from 54 to AppSHConsole's own ~59-char precedent minus a small
        // margin, so a still-long entry (e.g. a long custom player alias) has a little
        // more room before truncation kicks in at all.
        private const int LineWidth = 58;

        private readonly SHGUItext[] _lines = new SHGUItext[VisibleLines];
        private readonly SHGUItext _footer;

        private int _scrollOffset;

        // Real bug report: a two-word title ("AP LOG") rendered with connecting dashes
        // between the words in-game (the same word-connecting style the footer hint
        // text already uses, e.g. "press-ESC-to-quit" in a screenshot) -- looked off
        // compared to every other screen's single-word title ("ARCHIPELAGO"). A single
        // word sidesteps whatever renders that connector, matching the rest of the
        // game's own title conventions.
        public ArchipelagoLogApp() : base("LOG")
        {
            const int x = 3;
            int y = 3;
            for (int i = 0; i < VisibleLines; i++)
            {
                _lines[i] = (AddSubView(new SHGUItext("", x, y, 'w')) as SHGUItext)!;
                y += 1;
            }

            y += 1;
            _footer = (AddSubView(new SHGUItext("", x, y, 'z')) as SHGUItext)!;

            RefreshDisplay();
        }

        public override void Update()
        {
            base.Update();

            // Same reasoning as ArchipelagoConnectApp.Update -- SHGUIappbase's own
            // ReactToInputKeyboard dispatch for "esc" isn't reliable on its own (confirmed
            // by that screen's own real playtest bug), so this reads Escape directly too.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SHGUI.current.PopView();
                Input.ResetInputAxes();
                return;
            }

            int maxOffset = System.Math.Max(0, NotificationLog.Entries.Count - VisibleLines);

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _scrollOffset = System.Math.Min(maxOffset, _scrollOffset + 1);
                RefreshDisplay();
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _scrollOffset = System.Math.Max(0, _scrollOffset - 1);
                RefreshDisplay();
            }
            else if (Input.GetKeyDown(KeyCode.PageDown))
            {
                _scrollOffset = System.Math.Min(maxOffset, _scrollOffset + VisibleLines);
                RefreshDisplay();
            }
            else if (Input.GetKeyDown(KeyCode.PageUp))
            {
                _scrollOffset = System.Math.Max(0, _scrollOffset - VisibleLines);
                RefreshDisplay();
            }
        }

        private void RefreshDisplay()
        {
            // NotificationLog.Entries is oldest-first; reversed here so the newest entry
            // is always index 0 -- see class doc for why newest-first is the more useful
            // default view.
            IReadOnlyList<string> oldestFirst = NotificationLog.Entries;
            var newestFirst = new List<string>(oldestFirst.Count);
            for (int i = oldestFirst.Count - 1; i >= 0; i--)
            {
                newestFirst.Add(oldestFirst[i]);
            }

            for (int i = 0; i < VisibleLines; i++)
            {
                int entryIndex = _scrollOffset + i;
                if (entryIndex < newestFirst.Count)
                {
                    _lines[i].text = Truncate(newestFirst[entryIndex]);
                }
                else if (entryIndex == 0)
                {
                    _lines[i].text = "No notifications yet.";
                }
                else
                {
                    _lines[i].text = "";
                }
            }

            _footer.text = newestFirst.Count == 0
                ? "[ESC] close"
                : $"[UP/DOWN] scroll  [ESC] close   ({_scrollOffset + 1}-" +
                  $"{System.Math.Min(_scrollOffset + VisibleLines, newestFirst.Count)} of {newestFirst.Count})";
        }

        private static string Truncate(string text)
        {
            return text.Length <= LineWidth ? text : text.Substring(0, LineWidth - 3) + "...";
        }
    }
}
