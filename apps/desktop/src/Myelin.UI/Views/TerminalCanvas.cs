using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Myelin.Core;

namespace Myelin.UI.Views
{
    public struct TerminalCell
    {
        public char Character;
        public Color Foreground;
        public Color? Background;
        public bool Bold;

        public static readonly TerminalCell Empty = new()
        {
            Character = ' ',
            Foreground = Color.Parse("#CCCCCC"),
            Background = null,
            Bold = false
        };
    }

    public class TerminalCanvas : Control
    {
        public static readonly StyledProperty<NativeTerminal?> TerminalProperty =
            AvaloniaProperty.Register<TerminalCanvas, NativeTerminal?>(nameof(Terminal));

        public NativeTerminal? Terminal
        {
            get => GetValue(TerminalProperty);
            set => SetValue(TerminalProperty, value);
        }

        private static readonly FontFamily TerminalFontFamily = new("Consolas, Cascadia Code, Courier New, monospace");
        private readonly Typeface _font = new(TerminalFontFamily);
        private readonly Typeface _boldFont = new(TerminalFontFamily, FontStyle.Normal, FontWeight.Bold);
        private const double FontSize = 13.0;
        private double _lineHeight = 20.0;
        private double _charWidth = 7.8;
        private ushort _terminalCols = 120;
        private ushort _terminalRows = 30;

        public class TerminalSessionState
        {
            public List<List<TerminalCell>> MainLines { get; set; } = new() { new List<TerminalCell>() };
            public int MainCursorRow { get; set; } = 0;
            public int MainCursorCol { get; set; } = 0;
            public int MainSavedCursorRow { get; set; } = 0;
            public int MainSavedCursorCol { get; set; } = 0;

            public List<List<TerminalCell>> AltLines { get; set; } = new();
            public int AltCursorRow { get; set; } = 0;
            public int AltCursorCol { get; set; } = 0;
            public int AltSavedCursorRow { get; set; } = 0;
            public int AltSavedCursorCol { get; set; } = 0;
            public bool IsAltBufferActive { get; set; } = false;

            public int ScrollOffset { get; set; } = 0;
            public bool UserScrolledUp { get; set; } = false;
            public string PendingAnsiBuffer { get; set; } = string.Empty;

            public Color CurrentFg { get; set; } = Color.Parse("#CCCCCC");
            public Color? CurrentBg { get; set; } = null;
            public bool CurrentBold { get; set; } = false;
        }

        private readonly Dictionary<NativeTerminal, TerminalSessionState> _sessionStates = new();
        private TerminalSessionState _currentState = new();

        // Terminal Buffers: Main & Alternate (for full-screen apps like vim, nano, less)
        private List<List<TerminalCell>> _mainLines => _currentState.MainLines;
        private List<List<TerminalCell>> _altLines => _currentState.AltLines;

        private int _mainCursorRow
        {
            get => _currentState.MainCursorRow;
            set => _currentState.MainCursorRow = value;
        }
        private int _mainCursorCol
        {
            get => _currentState.MainCursorCol;
            set => _currentState.MainCursorCol = value;
        }
        private int _mainSavedCursorRow
        {
            get => _currentState.MainSavedCursorRow;
            set => _currentState.MainSavedCursorRow = value;
        }
        private int _mainSavedCursorCol
        {
            get => _currentState.MainSavedCursorCol;
            set => _currentState.MainSavedCursorCol = value;
        }

        private int _altCursorRow
        {
            get => _currentState.AltCursorRow;
            set => _currentState.AltCursorRow = value;
        }
        private int _altCursorCol
        {
            get => _currentState.AltCursorCol;
            set => _currentState.AltCursorCol = value;
        }
        private int _altSavedCursorRow
        {
            get => _currentState.AltSavedCursorRow;
            set => _currentState.AltSavedCursorRow = value;
        }
        private int _altSavedCursorCol
        {
            get => _currentState.AltSavedCursorCol;
            set => _currentState.AltSavedCursorCol = value;
        }
        private bool _isAltBufferActive
        {
            get => _currentState.IsAltBufferActive;
            set => _currentState.IsAltBufferActive = value;
        }

        // Active Buffer Accessors
        private List<List<TerminalCell>> Lines => _isAltBufferActive ? _altLines : _mainLines;

        public int CursorRow
        {
            get => _isAltBufferActive ? _altCursorRow : _mainCursorRow;
            set
            {
                if (_isAltBufferActive) _altCursorRow = value;
                else _mainCursorRow = value;
            }
        }

        public int CursorCol
        {
            get => _isAltBufferActive ? _altCursorCol : _mainCursorCol;
            set
            {
                if (_isAltBufferActive) _altCursorCol = value;
                else _mainCursorCol = value;
            }
        }

        private int SavedCursorRow
        {
            get => _isAltBufferActive ? _altSavedCursorRow : _mainSavedCursorRow;
            set
            {
                if (_isAltBufferActive) _altSavedCursorRow = value;
                else _mainSavedCursorRow = value;
            }
        }

        private int SavedCursorCol
        {
            get => _isAltBufferActive ? _altSavedCursorCol : _mainSavedCursorCol;
            set
            {
                if (_isAltBufferActive) _altSavedCursorCol = value;
                else _mainSavedCursorCol = value;
            }
        }

        /// <summary>
        /// The absolute line index where the active visible screen grid starts in the buffer.
        /// </summary>
        public int ScreenTopRow => Math.Max(0, Lines.Count - _terminalRows);

        public ushort TerminalCols => _terminalCols;
        public ushort TerminalRows => _terminalRows;

        private bool _cursorVisible = true;
        private bool _userScrolledUp
        {
            get => _currentState.UserScrolledUp;
            set => _currentState.UserScrolledUp = value;
        }
        private int _scrollOffset
        {
            get => _currentState.ScrollOffset;
            set => _currentState.ScrollOffset = value;
        }

        // Selection State
        private bool _hasSelection = false;
        private bool _isSelecting = false;
        private int _selAnchorRow = 0;
        private int _selAnchorCol = 0;
        private int _selHeadRow = 0;
        private int _selHeadCol = 0;

        // Current Active ANSI Style
        private Color _currentFg
        {
            get => _currentState.CurrentFg;
            set => _currentState.CurrentFg = value;
        }
        private Color? _currentBg
        {
            get => _currentState.CurrentBg;
            set => _currentState.CurrentBg = value;
        }
        private bool _currentBold
        {
            get => _currentState.CurrentBold;
            set => _currentState.CurrentBold = value;
        }

        private string _pendingAnsiBuffer
        {
            get => _currentState.PendingAnsiBuffer;
            set => _currentState.PendingAnsiBuffer = value;
        }

        // Timers
        private DispatcherTimer? _blinkTimer;
        private DispatcherTimer? _pollTimer;
        private DispatcherTimer? _resizeDebounceTimer;
        private ushort _targetResizeCols;
        private ushort _targetResizeRows;

        // Brush Cache
        private readonly Dictionary<Color, IBrush> _brushCache = new();

        // Standard ANSI Palette (VS Code Dark+ Style)
        private static readonly Color[] StandardColors = new[]
        {
            Color.Parse("#000000"), // 0 Black
            Color.Parse("#CD3131"), // 1 Red
            Color.Parse("#0DBC79"), // 2 Green
            Color.Parse("#E5E510"), // 3 Yellow
            Color.Parse("#2472C8"), // 4 Blue
            Color.Parse("#BC3FBC"), // 5 Magenta
            Color.Parse("#11A8CD"), // 6 Cyan
            Color.Parse("#E5E5E5"), // 7 White
        };

        private static readonly Color[] BrightColors = new[]
        {
            Color.Parse("#666666"), // 8 Bright Black (Gray)
            Color.Parse("#F14C4C"), // 9 Bright Red
            Color.Parse("#23D18B"), // 10 Bright Green
            Color.Parse("#F5F543"), // 11 Bright Yellow
            Color.Parse("#3B8EEA"), // 12 Bright Blue
            Color.Parse("#D670D6"), // 13 Bright Magenta
            Color.Parse("#29B8DB"), // 14 Bright Cyan
            Color.Parse("#FFFFFF"), // 15 Bright White
        };

        static TerminalCanvas()
        {
            AffectsRender<TerminalCanvas>(TerminalProperty, BoundsProperty);
            FocusableProperty.OverrideDefaultValue<TerminalCanvas>(true);
        }

        public TerminalCanvas()
        {
            try
            {
                Cursor = new Cursor(StandardCursorType.Ibeam);
            }
            catch
            {
                // Headless / unit test runner without Avalonia platform initialized
            }

            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
            RecalculateFontMetrics();
            EnsureMinLines();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TerminalProperty)
            {
                var newTerm = change.GetNewValue<NativeTerminal?>();

                // Clean up disposed terminal session states
                var disposed = _sessionStates.Keys.Where(k => k.IsDisposed).ToList();
                foreach (var d in disposed)
                {
                    _sessionStates.Remove(d);
                }

                if (newTerm != null)
                {
                    if (!_sessionStates.TryGetValue(newTerm, out var state))
                    {
                        state = new TerminalSessionState();
                        _sessionStates[newTerm] = state;
                    }
                    _currentState = state;

                    EnsureMinLines();

                    if (newTerm.IsAlive)
                    {
                        try
                        {
                            newTerm.Resize(_terminalCols, _terminalRows);
                        }
                        catch { }
                    }
                }
                else
                {
                    _currentState = new TerminalSessionState();
                    EnsureMinLines();
                }

                InvalidateVisual();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            RecalculateFontMetrics();

            _blinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _blinkTimer.Tick += (s, ev) =>
            {
                _cursorVisible = !_cursorVisible;
                InvalidateVisual();
            };
            _blinkTimer.Start();

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(25)
            };
            _pollTimer.Tick += (s, ev) => PollOutput();
            _pollTimer.Start();

            // Auto-focus on attach
            Focus();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _blinkTimer?.Stop();
            _pollTimer?.Stop();
            _resizeDebounceTimer?.Stop();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            RecalculateFontMetrics();

            double paddingX = 10;
            double paddingY = 6;

            if (e.NewSize.Width > 0 && e.NewSize.Height > 0 && _charWidth > 0 && _lineHeight > 0)
            {
                ushort cols = (ushort)Math.Max(20, Math.Min(300, (int)((e.NewSize.Width - paddingX * 2) / _charWidth)));
                ushort rows = (ushort)Math.Max(5, Math.Min(100, (int)((e.NewSize.Height - paddingY * 2) / _lineHeight)));

                if (cols == _terminalCols && rows == _terminalRows)
                {
                    InvalidateVisual();
                    return;
                }

                _targetResizeCols = cols;
                _targetResizeRows = rows;

                // Debounce ConPTY resize so rapid mouse drag doesn't flood and corrupt the PTY stream
                _resizeDebounceTimer?.Stop();
                _resizeDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(40)
                };
                _resizeDebounceTimer.Tick += (s, ev) =>
                {
                    _resizeDebounceTimer.Stop();
                    ApplyResize(_targetResizeCols, _targetResizeRows);
                };
                _resizeDebounceTimer.Start();
            }

            InvalidateVisual();
        }

        private void ApplyResize(ushort cols, ushort rows)
        {
            if (cols == 0 || rows == 0) return;
            if (cols == _terminalCols && rows == _terminalRows) return;

            _terminalCols = cols;
            _terminalRows = rows;

            EnsureMinLines();

            if (Terminal != null && Terminal.IsAlive)
            {
                try
                {
                    Terminal.Resize(cols, rows);
                }
                catch { }
            }

            InvalidateVisual();
        }

        private void RecalculateFontMetrics()
        {
            try
            {
                var testText = new FormattedText(
                    new string('X', 100),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _font,
                    FontSize,
                    Brushes.White
                );
                _charWidth = testText.Width > 0 ? (testText.Width / 100.0) : 7.8;
                _lineHeight = testText.Height > 0 ? Math.Ceiling(testText.Height + 4.0) : 20.0;
            }
            catch
            {
                _charWidth = 7.8;
                _lineHeight = 20.0;
            }
        }

        private void EnsureMinLines()
        {
            while (Lines.Count <= CursorRow)
            {
                Lines.Add(new List<TerminalCell>());
            }
        }

        private int ToAbsoluteRow(int screenRow0Based)
        {
            EnsureMinLines();
            int top = ScreenTopRow;
            int target = top + Math.Clamp(screenRow0Based, 0, Math.Max(0, _terminalRows - 1));
            while (Lines.Count <= target)
            {
                Lines.Add(new List<TerminalCell>());
            }
            return target;
        }

        private IBrush GetCachedBrush(Color color)
        {
            if (!_brushCache.TryGetValue(color, out var brush))
            {
                brush = new ImmutableSolidColorBrush(color);
                _brushCache[color] = brush;
            }
            return brush;
        }

        private void PollOutput()
        {
            if (Terminal == null || !Terminal.IsAlive) return;

            string chunk = Terminal.ReadAvailableRaw();
            if (!string.IsNullOrEmpty(chunk) || !string.IsNullOrEmpty(_pendingAnsiBuffer))
            {
                string fullText = _pendingAnsiBuffer + (chunk ?? string.Empty);
                _pendingAnsiBuffer = string.Empty;
                if (!string.IsNullOrEmpty(fullText))
                {
                    ProcessAnsiStream(fullText);
                    _cursorVisible = true;
                    InvalidateVisual();
                }
            }
        }

        public void ProcessAnsiStream(string text)
        {
            int i = 0;
            int n = text.Length;

            while (i < n)
            {
                char c = text[i];

                // ESC sequence start
                if (c == '\x1b')
                {
                    if (i + 1 >= n)
                    {
                        // Incomplete escape at chunk end: buffer and return
                        _pendingAnsiBuffer = text.Substring(i);
                        break;
                    }

                    char next = text[i + 1];

                    // CSI sequence: ESC [
                    if (next == '[')
                    {
                        int seqStart = i + 2;
                        int seqEnd = seqStart;

                        // Advance until final byte (0x40 - 0x7E)
                        while (seqEnd < n && (text[seqEnd] < 0x40 || text[seqEnd] > 0x7E))
                        {
                            // If an unexpected nested escape occurs, break to avoid eating stream
                            if (text[seqEnd] == '\x1b') break;
                            seqEnd++;
                        }

                        if (seqEnd < n && text[seqEnd] >= 0x40 && text[seqEnd] <= 0x7E)
                        {
                            char finalChar = text[seqEnd];
                            string paramStr = text[seqStart..seqEnd];
                            ApplyCsiSequence(paramStr, finalChar);
                            i = seqEnd + 1;
                            continue;
                        }
                        else if (seqEnd < n && text[seqEnd] == '\x1b')
                        {
                            // Malformed sequence followed by new ESC: skip malformed prefix
                            i = seqEnd;
                            continue;
                        }
                        else
                        {
                            // Incomplete CSI sequence across chunk boundary
                            _pendingAnsiBuffer = text.Substring(i);
                            break;
                        }
                    }
                    // OSC sequence: ESC ]
                    else if (next == ']')
                    {
                        int seqEnd = i + 2;
                        while (seqEnd < n && text[seqEnd] != '\x07' && !(text[seqEnd] == '\x1b' && seqEnd + 1 < n && text[seqEnd + 1] == '\\'))
                        {
                            seqEnd++;
                        }

                        if (seqEnd < n)
                        {
                            i = text[seqEnd] == '\x07' ? seqEnd + 1 : (seqEnd + 2 <= n ? seqEnd + 2 : n);
                            continue;
                        }
                        else
                        {
                            // Incomplete OSC sequence across chunk boundary
                            _pendingAnsiBuffer = text.Substring(i);
                            break;
                        }
                    }
                    else
                    {
                        // 2-char escape sequences
                        if (next == '7') // DECSC (Save cursor)
                        {
                            SavedCursorRow = CursorRow - ScreenTopRow;
                            SavedCursorCol = CursorCol;
                            i += 2;
                            continue;
                        }
                        else if (next == '8') // DECRC (Restore cursor)
                        {
                            CursorRow = ToAbsoluteRow(SavedCursorRow);
                            CursorCol = Math.Clamp(SavedCursorCol, 0, Math.Max(0, _terminalCols - 1));
                            i += 2;
                            continue;
                        }
                        else if (next == 'M') // RI (Reverse Index / cursor up)
                        {
                            if (CursorRow > ScreenTopRow)
                            {
                                CursorRow--;
                            }
                            else
                            {
                                Lines.Insert(ScreenTopRow, new List<TerminalCell>());
                            }
                            i += 2;
                            continue;
                        }
                        else if (next == 'D') // IND (Index / cursor down)
                        {
                            AdvanceLine();
                            i += 2;
                            continue;
                        }
                        else if (next == 'E') // NEL (Next Line)
                        {
                            AdvanceLine();
                            CursorCol = 0;
                            i += 2;
                            continue;
                        }
                        else if (next == 'c') // RIS (Reset to Initial State)
                        {
                            Lines.Clear();
                            EnsureMinLines();
                            CursorRow = 0;
                            CursorCol = 0;
                            _isAltBufferActive = false;
                            _currentFg = Color.Parse("#CCCCCC");
                            _currentBg = null;
                            _currentBold = false;
                            i += 2;
                            continue;
                        }

                        // Ignore other 2-char escapes (e.g. keypad modes ESC =, ESC >)
                        i += 2;
                        continue;
                    }
                }

                // Control characters
                if (c == '\r')
                {
                    CursorCol = 0;
                    i++;
                }
                else if (c == '\n')
                {
                    AdvanceLine();
                    CursorCol = 0;
                    i++;
                }
                else if (c == '\b')
                {
                    if (CursorCol > 0) CursorCol--;
                    i++;
                }
                else if (c == '\t')
                {
                    // Standard VT 8-column tab stop
                    int spaces = 8 - (CursorCol % 8);
                    for (int s = 0; s < spaces; s++)
                    {
                        PutCell(' ');
                    }
                    i++;
                }
                else if (c >= ' ')
                {
                    PutCell(c);
                    i++;
                }
                else
                {
                    i++;
                }
            }

            // Cap main buffer at 5000 lines
            if (_mainLines.Count > 5000)
            {
                int removed = _mainLines.Count - 5000;
                _mainLines.RemoveRange(0, removed);
                _mainCursorRow = Math.Max(0, _mainCursorRow - removed);

                if (_hasSelection)
                {
                    _selAnchorRow = Math.Max(0, _selAnchorRow - removed);
                    _selHeadRow = Math.Max(0, _selHeadRow - removed);
                }
            }

            // Auto-scroll unless user scrolled up
            if (!_userScrolledUp)
            {
                int visibleCount = (int)Math.Max(1, Bounds.Height / _lineHeight);
                _scrollOffset = Math.Max(0, Lines.Count - visibleCount);
            }
        }

        private void PutCell(char c)
        {
            EnsureMinLines();

            if (CursorRow < ScreenTopRow) CursorRow = ScreenTopRow;

            // Auto-wrap if cursor column reaches or exceeds the terminal column width
            if (CursorCol >= _terminalCols && _terminalCols > 0)
            {
                AdvanceLine();
                CursorCol = 0;
            }

            while (CursorRow >= Lines.Count)
            {
                Lines.Add(new List<TerminalCell>());
            }

            var line = Lines[CursorRow];
            if (CursorCol < 0) CursorCol = 0;
            while (line.Count < CursorCol)
            {
                line.Add(TerminalCell.Empty);
            }

            var cell = new TerminalCell
            {
                Character = c,
                Foreground = _currentFg,
                Background = _currentBg,
                Bold = _currentBold
            };

            if (CursorCol < line.Count)
            {
                line[CursorCol] = cell;
            }
            else
            {
                line.Add(cell);
            }

            CursorCol++;
        }

        private void AdvanceLine()
        {
            if (CursorRow >= ScreenTopRow + _terminalRows - 1)
            {
                // Bottom of screen: add new line to scroll screen up
                Lines.Add(new List<TerminalCell>());
                CursorRow = Lines.Count - 1;
            }
            else
            {
                CursorRow++;
                while (CursorRow >= Lines.Count)
                {
                    Lines.Add(new List<TerminalCell>());
                }
            }
        }

        private void ApplyCsiSequence(string paramStr, char finalChar)
        {
            EnsureMinLines();

            bool isDecPrivate = paramStr.StartsWith('?');
            string cleanParams = isDecPrivate ? paramStr.Substring(1) : paramStr;

            switch (finalChar)
            {
                case 'm': // SGR (Select Graphic Rendition)
                    ApplySgr(paramStr);
                    break;

                case 'H': // CUP (Cursor Position)
                case 'f': // HVP (Horizontal and Vertical Position)
                    {
                        var parts = cleanParams.Split(';');
                        int r = parts.Length > 0 && int.TryParse(parts[0], out int pr) ? Math.Max(1, pr) - 1 : 0;
                        int c = parts.Length > 1 && int.TryParse(parts[1], out int pc) ? Math.Max(1, pc) - 1 : 0;
                        CursorRow = ToAbsoluteRow(r);
                        CursorCol = Math.Clamp(c, 0, Math.Max(0, _terminalCols - 1));
                    }
                    break;

                case 'G': // CHA (Cursor Horizontal Absolute)
                case '`': // HPA
                    {
                        int col = int.TryParse(cleanParams, out int v) ? Math.Max(1, v) - 1 : 0;
                        CursorCol = Math.Clamp(col, 0, Math.Max(0, _terminalCols - 1));
                    }
                    break;

                case 'd': // VPA (Line Position Absolute)
                    {
                        int row = int.TryParse(cleanParams, out int v) ? Math.Max(1, v) - 1 : 0;
                        CursorRow = ToAbsoluteRow(row);
                    }
                    break;

                case 'A': // CUU (Cursor Up)
                    {
                        int amount = int.TryParse(cleanParams, out int v) ? Math.Max(1, v) : 1;
                        CursorRow = Math.Max(ScreenTopRow, CursorRow - amount);
                    }
                    break;

                case 'B': // CUD (Cursor Down)
                    {
                        int amount = int.TryParse(cleanParams, out int v) ? Math.Max(1, v) : 1;
                        CursorRow = Math.Min(ScreenTopRow + _terminalRows - 1, CursorRow + amount);
                        while (CursorRow >= Lines.Count) Lines.Add(new List<TerminalCell>());
                    }
                    break;

                case 'C': // CUF (Cursor Forward)
                    {
                        int amount = int.TryParse(cleanParams, out int v) ? Math.Max(1, v) : 1;
                        CursorCol = Math.Min(Math.Max(0, _terminalCols - 1), CursorCol + amount);
                    }
                    break;

                case 'D': // CUB (Cursor Back)
                    {
                        int amount = int.TryParse(cleanParams, out int v) ? Math.Max(1, v) : 1;
                        CursorCol = Math.Max(0, CursorCol - amount);
                    }
                    break;

                case 'E': // CNL (Cursor Next Line)
                    {
                        int amount = int.TryParse(cleanParams, out int v) ? Math.Max(1, v) : 1;
                        CursorRow = Math.Min(ScreenTopRow + _terminalRows - 1, CursorRow + amount);
                        CursorCol = 0;
                        while (CursorRow >= Lines.Count) Lines.Add(new List<TerminalCell>());
                    }
                    break;

                case 'F': // CPL (Cursor Previous Line)
                    {
                        int amount = int.TryParse(cleanParams, out int v) ? Math.Max(1, v) : 1;
                        CursorRow = Math.Max(ScreenTopRow, CursorRow - amount);
                        CursorCol = 0;
                    }
                    break;

                case 'J': // ED (Erase In Display)
                    {
                        int mode = int.TryParse(cleanParams, out int m) ? m : 0;
                        if (mode == 0) // Erase from cursor to end of screen
                        {
                            if (CursorRow >= 0 && CursorRow < Lines.Count)
                            {
                                var line = Lines[CursorRow];
                                if (CursorCol < line.Count) line.RemoveRange(CursorCol, line.Count - CursorCol);
                            }
                            for (int r = CursorRow + 1; r < ScreenTopRow + _terminalRows && r < Lines.Count; r++)
                            {
                                Lines[r].Clear();
                            }
                        }
                        else if (mode == 1) // Erase from start of screen to cursor
                        {
                            for (int r = ScreenTopRow; r < CursorRow && r < Lines.Count; r++)
                            {
                                Lines[r].Clear();
                            }
                            if (CursorRow >= 0 && CursorRow < Lines.Count)
                            {
                                var line = Lines[CursorRow];
                                int count = Math.Min(CursorCol + 1, line.Count);
                                for (int k = 0; k < count; k++) line[k] = TerminalCell.Empty;
                            }
                        }
                        else if (mode == 2) // Erase entire active screen
                        {
                            int screenStart = ScreenTopRow;
                            int screenEnd = Math.Min(Lines.Count, screenStart + _terminalRows);
                            for (int r = screenStart; r < screenEnd; r++)
                            {
                                Lines[r].Clear();
                            }
                        }
                        else if (mode == 3) // Erase scrollback history
                        {
                            int screenStart = ScreenTopRow;
                            if (screenStart > 0 && screenStart <= Lines.Count)
                            {
                                Lines.RemoveRange(0, screenStart);
                                CursorRow = Math.Max(0, CursorRow - screenStart);
                                _scrollOffset = 0;
                            }
                        }
                    }
                    break;

                case 'K': // EL (Erase In Line)
                    {
                        int mode = int.TryParse(cleanParams, out int m) ? m : 0;
                        if (CursorRow >= 0 && CursorRow < Lines.Count)
                        {
                            var line = Lines[CursorRow];
                            if (mode == 0) // Erase from cursor to end of line
                            {
                                if (CursorCol >= 0 && CursorCol < line.Count)
                                {
                                    line.RemoveRange(CursorCol, line.Count - CursorCol);
                                }
                            }
                            else if (mode == 1) // Erase from start to cursor
                            {
                                int clearCount = Math.Min(Math.Max(0, CursorCol + 1), line.Count);
                                for (int k = 0; k < clearCount; k++) line[k] = TerminalCell.Empty;
                            }
                            else if (mode == 2) // Erase entire line
                            {
                                line.Clear();
                            }
                        }
                    }
                    break;

                case 'X': // ECH (Erase Character)
                    {
                        int count = int.TryParse(cleanParams, out int cnt) ? Math.Max(1, cnt) : 1;
                        if (CursorRow >= 0 && CursorRow < Lines.Count)
                        {
                            var line = Lines[CursorRow];
                            int maxErase = Math.Min(line.Count, Math.Max(0, CursorCol) + count);
                            for (int k = Math.Max(0, CursorCol); k < maxErase; k++)
                            {
                                line[k] = TerminalCell.Empty;
                            }
                        }
                    }
                    break;

                case 'P': // DCH (Delete Character)
                    {
                        int count = int.TryParse(cleanParams, out int cnt) ? Math.Max(1, cnt) : 1;
                        if (CursorRow >= 0 && CursorRow < Lines.Count)
                        {
                            var line = Lines[CursorRow];
                            if (CursorCol >= 0 && CursorCol < line.Count)
                            {
                                int rem = Math.Min(count, line.Count - CursorCol);
                                line.RemoveRange(CursorCol, rem);
                            }
                        }
                    }
                    break;

                case '@': // ICH (Insert Character)
                    {
                        int count = int.TryParse(cleanParams, out int cnt) ? Math.Max(1, cnt) : 1;
                        if (CursorRow >= 0 && CursorRow < Lines.Count)
                        {
                            var line = Lines[CursorRow];
                            while (line.Count < CursorCol) line.Add(TerminalCell.Empty);
                            if (CursorCol >= 0 && CursorCol <= line.Count)
                            {
                                for (int k = 0; k < count; k++)
                                {
                                    line.Insert(CursorCol, TerminalCell.Empty);
                                }
                            }
                        }
                    }
                    break;

                case 'M': // DL (Delete Line)
                    {
                        int count = int.TryParse(cleanParams, out int cnt) ? Math.Max(1, cnt) : 1;
                        int screenBottom = ScreenTopRow + _terminalRows;
                        if (CursorRow >= ScreenTopRow && CursorRow < screenBottom && CursorRow < Lines.Count)
                        {
                            int rem = Math.Min(count, screenBottom - CursorRow);
                            rem = Math.Min(rem, Lines.Count - CursorRow);
                            Lines.RemoveRange(CursorRow, rem);
                            for (int k = 0; k < rem; k++)
                            {
                                if (screenBottom - 1 <= Lines.Count)
                                    Lines.Insert(Math.Min(Lines.Count, screenBottom - 1), new List<TerminalCell>());
                            }
                        }
                    }
                    break;

                case 'L': // IL (Insert Line)
                    {
                        int count = int.TryParse(cleanParams, out int cnt) ? Math.Max(1, cnt) : 1;
                        int screenBottom = ScreenTopRow + _terminalRows;
                        if (CursorRow >= ScreenTopRow && CursorRow < screenBottom)
                        {
                            for (int k = 0; k < count; k++)
                            {
                                Lines.Insert(CursorRow, new List<TerminalCell>());
                                if (Lines.Count > screenBottom)
                                {
                                    Lines.RemoveAt(screenBottom);
                                }
                            }
                        }
                    }
                    break;

                case 'S': // SU (Scroll Up)
                    {
                        int count = int.TryParse(cleanParams, out int cnt) ? Math.Max(1, cnt) : 1;
                        for (int k = 0; k < count; k++)
                        {
                            Lines.Add(new List<TerminalCell>());
                        }
                    }
                    break;

                case 'T': // SD (Scroll Down)
                    {
                        int count = int.TryParse(cleanParams, out int cnt) ? Math.Max(1, cnt) : 1;
                        for (int k = 0; k < count; k++)
                        {
                            Lines.Insert(ScreenTopRow, new List<TerminalCell>());
                            if (Lines.Count > ScreenTopRow + _terminalRows + 1000)
                            {
                                Lines.RemoveAt(Lines.Count - 1);
                            }
                        }
                    }
                    break;

                case 's': // Save Cursor Position
                    SavedCursorRow = CursorRow - ScreenTopRow;
                    SavedCursorCol = CursorCol;
                    break;

                case 'u': // Restore Cursor Position
                    CursorRow = ToAbsoluteRow(SavedCursorRow);
                    CursorCol = Math.Clamp(SavedCursorCol, 0, Math.Max(0, _terminalCols - 1));
                    break;

                case 'h': // Set Mode
                    if (isDecPrivate)
                    {
                        if (cleanParams == "25") _cursorVisible = true;
                        else if (cleanParams == "1049" || cleanParams == "47")
                        {
                            // Switch to alternate screen buffer
                            if (!_isAltBufferActive)
                            {
                                _isAltBufferActive = true;
                                _altLines.Clear();
                                for (int r = 0; r < _terminalRows; r++) _altLines.Add(new List<TerminalCell>());
                                _altCursorRow = 0;
                                _altCursorCol = 0;
                            }
                        }
                    }
                    break;

                case 'l': // Reset Mode
                    if (isDecPrivate)
                    {
                        if (cleanParams == "25") _cursorVisible = false;
                        else if (cleanParams == "1049" || cleanParams == "47")
                        {
                            // Switch back to main screen buffer
                            if (_isAltBufferActive)
                            {
                                _isAltBufferActive = false;
                                _altLines.Clear();
                            }
                        }
                    }
                    break;
            }
        }

        private static Color Get256Color(int idx)
        {
            if (idx >= 0 && idx < 8) return StandardColors[idx];
            if (idx >= 8 && idx < 16) return BrightColors[idx - 8];
            if (idx >= 16 && idx < 232)
            {
                int r = ((idx - 16) / 36) * 51;
                int g = (((idx - 16) % 36) / 6) * 51;
                int b = ((idx - 16) % 6) * 51;
                return Color.FromRgb((byte)r, (byte)g, (byte)b);
            }
            if (idx >= 232 && idx < 256)
            {
                byte gray = (byte)(8 + (idx - 232) * 10);
                return Color.FromRgb(gray, gray, gray);
            }
            return Color.Parse("#CCCCCC");
        }

        private void ApplySgr(string paramStr)
        {
            if (string.IsNullOrEmpty(paramStr) || paramStr == "0")
            {
                _currentFg = Color.Parse("#CCCCCC");
                _currentBg = null;
                _currentBold = false;
                return;
            }

            var parts = paramStr.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int code)) continue;

                switch (code)
                {
                    case 0:
                        _currentFg = Color.Parse("#CCCCCC");
                        _currentBg = null;
                        _currentBold = false;
                        break;
                    case 1:
                        _currentBold = true;
                        break;
                    case 22:
                        _currentBold = false;
                        break;
                    case 38 when i + 1 < parts.Length:
                        if (parts[i + 1] == "2" && i + 4 < parts.Length &&
                            byte.TryParse(parts[i + 2], out byte r) &&
                            byte.TryParse(parts[i + 3], out byte g) &&
                            byte.TryParse(parts[i + 4], out byte b))
                        {
                            _currentFg = Color.FromRgb(r, g, b);
                            i += 4;
                        }
                        else if (parts[i + 1] == "5" && i + 2 < parts.Length &&
                                 int.TryParse(parts[i + 2], out int colIdx))
                        {
                            _currentFg = Get256Color(colIdx);
                            i += 2;
                        }
                        break;
                    case 48 when i + 1 < parts.Length:
                        if (parts[i + 1] == "2" && i + 4 < parts.Length &&
                            byte.TryParse(parts[i + 2], out byte br) &&
                            byte.TryParse(parts[i + 3], out byte bg) &&
                            byte.TryParse(parts[i + 4], out byte bb))
                        {
                            _currentBg = Color.FromRgb(br, bg, bb);
                            i += 4;
                        }
                        else if (parts[i + 1] == "5" && i + 2 < parts.Length &&
                                 int.TryParse(parts[i + 2], out int bColIdx))
                        {
                            _currentBg = Get256Color(bColIdx);
                            i += 2;
                        }
                        break;
                    case >= 30 and <= 37:
                        _currentFg = _currentBold ? BrightColors[code - 30] : StandardColors[code - 30];
                        break;
                    case 39:
                        _currentFg = Color.Parse("#CCCCCC");
                        break;
                    case >= 40 and <= 47:
                        _currentBg = StandardColors[code - 40];
                        break;
                    case 49:
                        _currentBg = null;
                        break;
                    case >= 90 and <= 97:
                        _currentFg = BrightColors[code - 90];
                        break;
                    case >= 100 and <= 107:
                        _currentBg = BrightColors[code - 100];
                        break;
                }
            }
        }

        public string GetSelectedText()
        {
            if (!_hasSelection || Lines.Count == 0) return string.Empty;

            int startRow = _selAnchorRow < _selHeadRow ? _selAnchorRow : _selHeadRow;
            int endRow = _selAnchorRow > _selHeadRow ? _selAnchorRow : _selHeadRow;
            int startCol = _selAnchorRow == _selHeadRow ? Math.Min(_selAnchorCol, _selHeadCol) : (_selAnchorRow < _selHeadRow ? _selAnchorCol : _selHeadCol);
            int endCol = _selAnchorRow == _selHeadRow ? Math.Max(_selAnchorCol, _selHeadCol) : (_selAnchorRow > _selHeadRow ? _selAnchorCol : _selHeadCol);

            startRow = Math.Clamp(startRow, 0, Lines.Count - 1);
            endRow = Math.Clamp(endRow, 0, Lines.Count - 1);

            var sb = new StringBuilder();
            for (int r = startRow; r <= endRow; r++)
            {
                var line = Lines[r];
                int sC = (r == startRow) ? Math.Min(startCol, line.Count) : 0;
                int eC = (r == endRow) ? Math.Min(endCol, line.Count) : line.Count;

                for (int c = sC; c < eC; c++)
                {
                    sb.Append(line[c].Character);
                }
                if (r < endRow)
                {
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        private (int start, int end) GetWordRangeAt(int row, int col)
        {
            if (row < 0 || row >= Lines.Count) return (0, 0);
            var line = Lines[row];
            if (line.Count == 0) return (0, 0);
            int c = Math.Clamp(col, 0, line.Count - 1);

            char ch = line[c].Character;
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.' || ch == '/' || ch == '\\')
            {
                int start = c;
                int end = c + 1;
                while (start > 0 && (char.IsLetterOrDigit(line[start - 1].Character) || "_-./\\:".Contains(line[start - 1].Character))) start--;
                while (end < line.Count && (char.IsLetterOrDigit(line[end].Character) || "_-./\\:".Contains(line[end].Character))) end++;
                return (start, end);
            }
            return (c, c + 1);
        }

        public async Task ExecuteCopyAsync()
        {
            if (!_hasSelection) return;
            string text = GetSelectedText();
            if (!string.IsNullOrEmpty(text))
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(text);
                }
            }
        }

        public async Task ExecutePasteAsync()
        {
            if (Terminal == null || !Terminal.IsAlive) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                string? text = await topLevel.Clipboard.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    Terminal.Write(text);
                    _userScrolledUp = false;
                    _cursorVisible = true;
                    InvalidateVisual();
                }
            }
        }

        protected override async void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            _cursorVisible = true;

            var point = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;

            double paddingX = 10;
            double paddingY = 6;

            int clickedRow = Math.Clamp((int)((point.Y - paddingY) / _lineHeight) + _scrollOffset, 0, Math.Max(0, Lines.Count - 1));
            int clickedCol = Math.Max(0, (int)Math.Round((point.X - paddingX) / _charWidth));

            // Right Click in Terminal: Copy selection if present, else Paste
            if (props.IsRightButtonPressed)
            {
                if (_hasSelection)
                {
                    await ExecuteCopyAsync();
                    _hasSelection = false;
                    InvalidateVisual();
                }
                else
                {
                    await ExecutePasteAsync();
                }
                e.Handled = true;
                return;
            }

            if (e.ClickCount == 2)
            {
                // Double click: select word
                var (start, end) = GetWordRangeAt(clickedRow, clickedCol);
                _selAnchorRow = clickedRow;
                _selAnchorCol = start;
                _selHeadRow = clickedRow;
                _selHeadCol = end;
                _hasSelection = start != end;
                _isSelecting = false;
            }
            else if (e.ClickCount == 3)
            {
                // Triple click: select full line
                _selAnchorRow = clickedRow;
                _selAnchorCol = 0;
                _selHeadRow = clickedRow;
                _selHeadCol = Lines[clickedRow].Count;
                _hasSelection = true;
                _isSelecting = false;
            }
            else
            {
                // Single click: start drag selection
                _selAnchorRow = clickedRow;
                _selAnchorCol = clickedCol;
                _selHeadRow = clickedRow;
                _selHeadCol = clickedCol;
                _hasSelection = false;
                _isSelecting = true;
            }

            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_isSelecting || Lines.Count == 0) return;

            var point = e.GetPosition(this);
            double paddingX = 10;
            double paddingY = 6;

            int targetRow = Math.Clamp((int)((point.Y - paddingY) / _lineHeight) + _scrollOffset, 0, Math.Max(0, Lines.Count - 1));
            int targetCol = Math.Max(0, (int)Math.Round((point.X - paddingX) / _charWidth));

            // Auto-scroll on edge drag
            if (point.Y < 0 && _scrollOffset > 0)
            {
                _scrollOffset = _scrollOffset > 1 ? _scrollOffset - 1 : 0;
                _userScrolledUp = true;
            }
            else if (point.Y > Bounds.Height)
            {
                int visibleLines = (int)Math.Max(1, Bounds.Height / _lineHeight);
                int maxScroll = Math.Max(0, Lines.Count - visibleLines);
                if (_scrollOffset < maxScroll)
                {
                    _scrollOffset++;
                    _userScrolledUp = true;
                }
            }

            _selHeadRow = targetRow;
            _selHeadCol = targetCol;
            _hasSelection = (_selHeadRow != _selAnchorRow || _selHeadCol != _selAnchorCol);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _isSelecting = false;
            if (_selHeadRow == _selAnchorRow && _selHeadCol == _selAnchorCol)
            {
                _hasSelection = false;
                InvalidateVisual();
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            int visibleCount = (int)Math.Max(1, Bounds.Height / _lineHeight);
            int maxScroll = Math.Max(0, Lines.Count - visibleCount);

            if (e.Delta.Y > 0)
            {
                _scrollOffset = Math.Max(0, _scrollOffset - 3);
                _userScrolledUp = _scrollOffset < maxScroll;
            }
            else if (e.Delta.Y < 0)
            {
                _scrollOffset = Math.Min(maxScroll, _scrollOffset + 3);
                _userScrolledUp = _scrollOffset < maxScroll;
            }

            InvalidateVisual();
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            if (Terminal == null || !Terminal.IsAlive || string.IsNullOrEmpty(e.Text)) return;

            // Forward printable characters (>= space, not control, not DEL 0x7F)
            if (e.Text.Length > 0 && e.Text[0] >= ' ' && e.Text[0] != 0x7F)
            {
                Terminal.Write(e.Text);
                _userScrolledUp = false;
                _cursorVisible = true;
                InvalidateVisual();
            }
        }

        protected override async void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Terminal == null || !Terminal.IsAlive) return;

            bool handled = true;

            // Handle Copy (Ctrl+Shift+C or Ctrl+C with active selection)
            if ((e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ||
                (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && _hasSelection))
            {
                await ExecuteCopyAsync();
                _hasSelection = false;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // Handle Paste (Ctrl+V or Ctrl+Shift+V)
            if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                await ExecutePasteAsync();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    Terminal.Write("\r");
                    _userScrolledUp = false;
                    break;
                case Key.Back:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        Terminal.Write("\x17"); // Ctrl+W: delete word backward
                    }
                    else
                    {
                        Terminal.Write("\x08"); // BS
                    }
                    _userScrolledUp = false;
                    break;
                case Key.Delete:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        Terminal.Write("\x1b[3;5~"); // Ctrl+Delete
                    }
                    else
                    {
                        Terminal.Write("\x1b[3~"); // Delete
                    }
                    _userScrolledUp = false;
                    break;
                case Key.Insert:
                    Terminal.Write("\x1b[2~");
                    break;
                case Key.Tab:
                    Terminal.Write("\t");
                    _userScrolledUp = false;
                    break;
                case Key.Up:
                    Terminal.Write("\x1b[A");
                    _userScrolledUp = false;
                    break;
                case Key.Down:
                    Terminal.Write("\x1b[B");
                    _userScrolledUp = false;
                    break;
                case Key.Right:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        Terminal.Write("\x1b[1;5C"); // Ctrl+Right: word forward
                    }
                    else
                    {
                        Terminal.Write("\x1b[C");
                    }
                    _userScrolledUp = false;
                    break;
                case Key.Left:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        Terminal.Write("\x1b[1;5D"); // Ctrl+Left: word backward
                    }
                    else
                    {
                        Terminal.Write("\x1b[D");
                    }
                    _userScrolledUp = false;
                    break;
                case Key.Home:
                    Terminal.Write("\x1b[H");
                    _userScrolledUp = false;
                    break;
                case Key.End:
                    Terminal.Write("\x1b[F");
                    _userScrolledUp = false;
                    break;
                case Key.PageUp:
                    Terminal.Write("\x1b[5~");
                    break;
                case Key.PageDown:
                    Terminal.Write("\x1b[6~");
                    break;
                case Key.F1: Terminal.Write("\x1bOP"); break;
                case Key.F2: Terminal.Write("\x1bOQ"); break;
                case Key.F3: Terminal.Write("\x1bOR"); break;
                case Key.F4: Terminal.Write("\x1bOS"); break;
                case Key.F5: Terminal.Write("\x1b[15~"); break;
                case Key.F6: Terminal.Write("\x1b[17~"); break;
                case Key.F7: Terminal.Write("\x1b[18~"); break;
                case Key.F8: Terminal.Write("\x1b[19~"); break;
                case Key.F9: Terminal.Write("\x1b[20~"); break;
                case Key.F10: Terminal.Write("\x1b[21~"); break;
                case Key.F11: Terminal.Write("\x1b[23~"); break;
                case Key.F12: Terminal.Write("\x1b[24~"); break;
                case Key.A when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x01"); // Start of line
                    _userScrolledUp = false;
                    break;
                case Key.B when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x02"); // Char backward
                    _userScrolledUp = false;
                    break;
                case Key.C when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x03"); // SIGINT
                    _userScrolledUp = false;
                    break;
                case Key.D when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x04"); // EOF
                    _userScrolledUp = false;
                    break;
                case Key.E when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x05"); // End of line
                    _userScrolledUp = false;
                    break;
                case Key.F when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x06"); // Char forward
                    _userScrolledUp = false;
                    break;
                case Key.K when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x0B"); // Kill line to end
                    _userScrolledUp = false;
                    break;
                case Key.L when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x0c"); // FormFeed / redraw shell prompt cleanly
                    _userScrolledUp = false;
                    break;
                case Key.U when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x15"); // Kill line to start
                    _userScrolledUp = false;
                    break;
                case Key.W when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x17"); // Delete word backward
                    _userScrolledUp = false;
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
                _cursorVisible = true;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            using var clip = context.PushClip(bounds);

            // Terminal Background (#141414)
            context.FillRectangle(GetCachedBrush(Color.Parse("#141414")), bounds);

            var cursorBrush = GetCachedBrush(Color.Parse("#528BFF"));
            var unfocusedCursorBrush = GetCachedBrush(Color.Parse("#666666"));
            var selectionBrush = GetCachedBrush(Color.FromArgb(110, 38, 79, 120));

            int visibleLineCount = (int)Math.Ceiling(bounds.Height / _lineHeight) + 1;
            int startIdx = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, Lines.Count - 1)));
            int endIdx = Math.Min(Lines.Count, startIdx + visibleLineCount);

            double paddingX = 10;
            double paddingY = 6;

            int selStartRow = 0, selEndRow = 0, selStartCol = 0, selEndCol = 0;
            if (_hasSelection)
            {
                selStartRow = _selAnchorRow < _selHeadRow ? _selAnchorRow : _selHeadRow;
                selEndRow = _selAnchorRow > _selHeadRow ? _selAnchorRow : _selHeadRow;
                selStartCol = _selAnchorRow == _selHeadRow ? Math.Min(_selAnchorCol, _selHeadCol) : (_selAnchorRow < _selHeadRow ? _selAnchorCol : _selHeadCol);
                selEndCol = _selAnchorRow == _selHeadRow ? Math.Max(_selAnchorCol, _selHeadCol) : (_selAnchorRow > _selHeadRow ? _selAnchorCol : _selHeadCol);
            }

            for (int i = startIdx; i < endIdx; i++)
            {
                var line = Lines[i];
                double y = Math.Round(paddingY + ((i - startIdx) * _lineHeight));

                // Draw Selection Highlight Background
                if (_hasSelection && i >= selStartRow && i <= selEndRow)
                {
                    int sC = (i == selStartRow) ? selStartCol : 0;
                    int eC = (i == selEndRow) ? selEndCol : Math.Max(line.Count, CursorCol);
                    if (eC > sC)
                    {
                        double selX = Math.Round(paddingX + (sC * _charWidth));
                        double selW = Math.Round((eC - sC) * _charWidth);
                        context.FillRectangle(selectionBrush, new Rect(selX, y, selW, _lineHeight));
                    }
                }

                // Render line runs
                if (line.Count > 0)
                {
                    int spanStart = 0;
                    while (spanStart < line.Count)
                    {
                        var cell = line[spanStart];
                        int spanEnd = spanStart + 1;

                        // Group contiguous cells with identical formatting
                        while (spanEnd < line.Count &&
                               line[spanEnd].Foreground == cell.Foreground &&
                               line[spanEnd].Background == cell.Background &&
                               line[spanEnd].Bold == cell.Bold)
                        {
                            spanEnd++;
                        }

                        // Build text run with sanitized printable characters
                        var chars = new char[spanEnd - spanStart];
                        for (int k = 0; k < chars.Length; k++)
                        {
                            char ch = line[spanStart + k].Character;
                            chars[k] = (ch == '\0' || char.IsControl(ch)) ? ' ' : ch;
                        }
                        string runText = new(chars);

                        double spanX = Math.Round(paddingX + (spanStart * _charWidth), 2);
                        double spanW = Math.Round((spanEnd - spanStart) * _charWidth, 2);

                        // Draw cell background if set
                        if (cell.Background.HasValue)
                        {
                            context.FillRectangle(
                                GetCachedBrush(cell.Background.Value),
                                new Rect(spanX, y, spanW, _lineHeight)
                            );
                        }

                        // Draw formatted text run
                        if (!string.IsNullOrEmpty(runText))
                        {
                            var activeFont = cell.Bold ? _boldFont : _font;
                            var ft = new FormattedText(
                                runText,
                                CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight,
                                activeFont,
                                FontSize,
                                GetCachedBrush(cell.Foreground)
                            );
                            double textY = Math.Round(y + Math.Max(0, (_lineHeight - ft.Height) / 2.0));
                            context.DrawText(ft, new Point(spanX, textY));
                        }

                        spanStart = spanEnd;
                    }
                }

                // Render terminal cursor directly on active line
                if (i == CursorRow && _cursorVisible)
                {
                    double cursorX = Math.Round(paddingX + (CursorCol * _charWidth), 2);
                    double cursorY = Math.Round(y + 2);
                    double cursorH = Math.Round(_lineHeight - 4);
                    var activeCursorBrush = IsFocused ? cursorBrush : unfocusedCursorBrush;
                    context.FillRectangle(activeCursorBrush, new Rect(cursorX, cursorY, 2, cursorH));
                }
            }
        }
    }
}
