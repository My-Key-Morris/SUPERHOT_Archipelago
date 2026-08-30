using System.Collections.Generic;
using UnityEngine;

namespace SuperhotArchipelago.Core
{
    /// <summary>
    /// Hub screen for browsing past notifications, opened via the same
    /// SHGUIappbase-launch mechanism as Core/ArchipelagoConnectApp.cs. Uses a plain
    /// SHGUIappbase + SHGUItext screen with hand-rolled scrolling rather than the native
    /// SHGUIcommanderview list framework, since that framework is built for selecting an
    /// item and this is read-only history. Entries are shown newest-first even though
    /// NotificationLog.Entries stores them oldest-first.
    /// </summary>
    public class ArchipelagoLogApp : SHGUIappbase
    {
        // How many entries are visible on screen at once, kept comfortably under what the
        // app frame actually fits.
        private const int VisibleLines = 14;

        // Log lines are truncated (not wrapped) at this length so each entry always
        // occupies exactly one screen line, keeping the scrolling math below simple.
        private const int LineWidth = 58;

        // A row needs multiple colors (e.g. per-item coloring like the text client), so
        // each row is built from this many separate SHGUItext instances glued together at
        // increasing x-offsets rather than one flat string. 6 covers the longest format
        // this mod builds today with one spare slot.
        private const int MaxSegmentsPerLine = 6;
        private const int BaseX = 3;

        private readonly SHGUItext[][] _lineSegments = new SHGUItext[VisibleLines][];
        private readonly SHGUItext _footer;

        private int _scrollOffset;

        // Single-word title ("LOG") to avoid the connecting dashes the game renders
        // between words in a multi-word title, matching other screens' convention.
        public ArchipelagoLogApp() : base("LOG")
        {
            int y = 3;
            for (int i = 0; i < VisibleLines; i++)
            {
                _lineSegments[i] = new SHGUItext[MaxSegmentsPerLine];
                for (int s = 0; s < MaxSegmentsPerLine; s++)
                {
                    // x is overwritten per-segment every RefreshDisplay; BaseX is just a
                    // harmless starting value.
                    _lineSegments[i][s] = (AddSubView(new SHGUItext("", BaseX, y, 'w')) as SHGUItext)!;
                }
                y += 1;
            }

            y += 1;
            _footer = (AddSubView(new SHGUItext("", BaseX, y, 'z')) as SHGUItext)!;

            RefreshDisplay();
        }

        public override void Update()
        {
            base.Update();

            // Same as ArchipelagoConnectApp.Update -- ReactToInputKeyboard's "esc" dispatch
            // isn't reliable alone, so Escape is read directly here too.
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
            // is always index 0.
            IReadOnlyList<LogSegment[]> oldestFirst = NotificationLog.Entries;
            var newestFirst = new List<LogSegment[]>(oldestFirst.Count);
            for (int i = oldestFirst.Count - 1; i >= 0; i--)
            {
                newestFirst.Add(oldestFirst[i]);
            }

            for (int i = 0; i < VisibleLines; i++)
            {
                int entryIndex = _scrollOffset + i;
                if (entryIndex < newestFirst.Count)
                {
                    SetRowSegments(i, newestFirst[entryIndex]);
                }
                else if (entryIndex == 0)
                {
                    SetRowSegments(i, new[] { new LogSegment("No notifications yet.", NotificationColors.Default) });
                }
                else
                {
                    SetRowSegments(i, System.Array.Empty<LogSegment>());
                }
            }

            _footer.text = newestFirst.Count == 0
                ? "[ESC] close"
                : $"[UP/DOWN] scroll  [ESC] close   ({_scrollOffset + 1}-" +
                  $"{System.Math.Min(_scrollOffset + VisibleLines, newestFirst.Count)} of {newestFirst.Count})";
        }

        /// <summary>
        /// Lays one entry's colored segments across a row's pre-allocated SHGUItext slots,
        /// left to right, truncating the combined line to LineWidth with "..." if needed.
        /// Unused slots are cleared so a shorter entry can't leave a longer one's leftover
        /// segments on screen.
        /// </summary>
        private void SetRowSegments(int rowIndex, LogSegment[] segments)
        {
            SHGUItext[] slots = _lineSegments[rowIndex];
            int cellsUsed = 0;
            int slotIndex = 0;

            foreach (LogSegment segment in segments)
            {
                if (slotIndex >= slots.Length)
                {
                    // Ran out of segment slots -- shouldn't normally happen, but dropping
                    // the rest is safer than an index-out-of-range crash.
                    break;
                }

                int remaining = LineWidth - cellsUsed;
                string text;
                if (remaining <= 0)
                {
                    text = "";
                }
                else if (segment.Text.Length <= remaining)
                {
                    text = segment.Text;
                }
                else
                {
                    text = remaining <= 3 ? segment.Text.Substring(0, remaining) : segment.Text.Substring(0, remaining - 3) + "...";
                }

                SHGUItext slot = slots[slotIndex];
                slot.x = BaseX + cellsUsed;
                slot.color = segment.Color;
                slot.text = text;
                cellsUsed += text.Length;
                slotIndex++;
            }

            for (; slotIndex < slots.Length; slotIndex++)
            {
                slots[slotIndex].text = "";
            }
        }
    }
}
