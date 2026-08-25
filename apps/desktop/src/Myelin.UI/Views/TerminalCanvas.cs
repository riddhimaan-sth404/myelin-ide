using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const double FontSize = 13.0;
        private const double LineHeight = 20.0;
        private double _charWidth = 7.8;
        private ushort _terminalCols = 120;

        // Terminal Grid State
        private readonly List<List<TerminalCell>> _lines = new() { new List<TerminalCell>() };
        private int _cursorRow = 0;
        private int _cursorCol = 0;
        private int _savedCursorRow = 0;
        private int _savedCursorCol = 0;
        private bool _cursorVisible = true;
        private bool _userScrolledUp = false;
        private int _scrollOffset = 0;

        // Terminal Selection State
        private bool _hasSelection = false;
        private bool _isSelecting = false;
        private int _selAnchorRow = 0;
        private int _selAnchorCol = 0;
        private int _selHeadRow = 0;
        private int _selHeadCol = 0;

        // Current Active ANSI Style
        private Color _currentFg = Color.Parse("#CCCCCC");
        private Color? _currentBg = null;
        private bool _currentBold = false;

        // Timers
        private DispatcherTimer? _blinkTimer;
        private DispatcherTimer? _pollTimer;

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
            Cursor = new Cursor(StandardCursorType.Ibeam);
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _charWidth = MeasureCharWidth();

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
                Interval = TimeSpan.FromMilliseconds(30)
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
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            _charWidth = MeasureCharWidth();

            if (e.NewSize.Width > 0 && e.NewSize.Height > 0 && _charWidth > 0 && LineHeight > 0)
            {
                ushort cols = (ushort)Math.Max(20, Math.Min(300, (int)((e.NewSize.Width - 20) / _charWidth)));
                ushort rows = (ushort)Math.Max(5, Math.Min(100, (int)((e.NewSize.Height - 12) / LineHeight)));
                _terminalCols = cols;

                if (Terminal != null && Terminal.IsAlive)
                {
                    try
                    {
                        Terminal.Resize(cols, rows);
                    }
                    catch { }
                }
            }

            InvalidateVisual();
        }

        private double MeasureCharWidth()
        {
            var testText = new FormattedText(
                "01234567890123456789",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _font,
                FontSize,
                Brushes.White
            );
            return testText.Width > 0 ? (testText.Width / 20.0) : 7.8;
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

        private string _pendingAnsiBuffer = string.Empty;

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

        private void ProcessAnsiStream(string text)
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
                            seqEnd++;
                        }

                        if (seqEnd < n)
                        {
                            char finalChar = text[seqEnd];
                            string paramStr = text[seqStart..seqEnd];
                            ApplyCsiSequence(paramStr, finalChar);
                            i = seqEnd + 1;
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
                            _savedCursorRow = _cursorRow;
                            _savedCursorCol = _cursorCol;
                            i += 2;
                            continue;
                        }
                        else if (next == '8') // DECRC (Restore cursor)
                        {
                            _cursorRow = Math.Max(0, _savedCursorRow);
                            while (_cursorRow >= _lines.Count)
                            {
                                _lines.Add(new List<TerminalCell>());
                            }
                            _cursorCol = Math.Max(0, _savedCursorCol);
                            i += 2;
                            continue;
                        }
                        else if (next == 'M') // RI (Reverse Index / cursor up)
                        {
                            _cursorRow = Math.Max(0, _cursorRow - 1);
                            i += 2;
                            continue;
                        }

                        i += 2;
                        continue;
                    }
                }

                // Control characters
                if (c == '\r')
                {
                    _cursorCol = 0;
                    i++;
                }
                else if (c == '\n')
                {
                    _cursorRow++;
                    while (_cursorRow >= _lines.Count)
                    {
                        _lines.Add(new List<TerminalCell>());
                    }
                    _cursorCol = 0;
                    i++;
                }
                else if (c == '\b')
                {
                    if (_cursorCol > 0) _cursorCol--;
                    i++;
                }
                else if (c == '\t')
                {
                    // Standard VT 8-column tab stop
                    int spaces = 8 - (_cursorCol % 8);
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

            // Cap buffer at 2000 lines
            if (_lines.Count > 2000)
            {
                int removed = _lines.Count - 2000;
                _lines.RemoveRange(0, removed);
                _cursorRow = Math.Max(0, _cursorRow - removed);

                if (_hasSelection)
                {
                    _selAnchorRow = Math.Max(0, _selAnchorRow - removed);
                    _selHeadRow = Math.Max(0, _selHeadRow - removed);
                }
            }

            // Auto-scroll unless user scrolled up
            if (!_userScrolledUp)
            {
                int visibleCount = (int)Math.Max(1, Bounds.Height / LineHeight);
                _scrollOffset = Math.Max(0, _lines.Count - visibleCount);
            }
        }

        private void PutCell(char c)
        {
            if (_cursorRow < 0) _cursorRow = 0;
            while (_cursorRow >= _lines.Count)
            {
                _lines.Add(new List<TerminalCell>());
            }

            // Auto-wrap if cursor column reaches or exceeds the terminal column width
            if (_cursorCol >= _terminalCols && _terminalCols > 0)
            {
                _cursorRow++;
                while (_cursorRow >= _lines.Count)
                {
                    _lines.Add(new List<TerminalCell>());
                }
                _cursorCol = 0;
            }

            var line = _lines[_cursorRow];
            if (_cursorCol < 0) _cursorCol = 0;
            while (line.Count < _cursorCol)
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

            if (_cursorCol < line.Count)
            {
                line[_cursorCol] = cell;
            }
            else
            {
                line.Add(cell);
            }

            _cursorCol++;
        }

        private void ApplyCsiSequence(string paramStr, char finalChar)
        {
            switch (finalChar)
            {
                case 'm': // SGR (Select Graphic Rendition)
                    ApplySgr(paramStr);
                    break;
                case 'J': // Erase Display
                    if (paramStr == "2" || paramStr == "3")
                    {
                        _lines.Clear();
                        _lines.Add(new List<TerminalCell>());
                        _cursorRow = 0;
                        _cursorCol = 0;
                        _scrollOffset = 0;
                    }
                    break;
                case 'K': // Erase Line
                    if (_cursorRow >= 0 && _cursorRow < _lines.Count)
                    {
                        var line = _lines[_cursorRow];
                        if (paramStr == "0" || string.IsNullOrEmpty(paramStr))
                        {
                            if (_cursorCol >= 0 && _cursorCol < line.Count)
                            {
                                line.RemoveRange(_cursorCol, line.Count - _cursorCol);
                            }
                        }
                        else if (paramStr == "1")
                        {
                            int clearCount = Math.Min(Math.Max(0, _cursorCol + 1), line.Count);
                            for (int k = 0; k < clearCount; k++) line[k] = TerminalCell.Empty;
                        }
                        else if (paramStr == "2")
                        {
                            line.Clear();
                        }
                    }
                    break;
                case 'A': // Cursor Up
                    {
                        int amount = int.TryParse(paramStr, out int v) ? Math.Max(1, v) : 1;
                        _cursorRow = Math.Max(0, _cursorRow - amount);
                    }
                    break;
                case 'B': // Cursor Down
                    {
                        int amount = int.TryParse(paramStr, out int v) ? Math.Max(1, v) : 1;
                        _cursorRow = Math.Max(0, _cursorRow + amount);
                        while (_cursorRow >= _lines.Count)
                        {
                            _lines.Add(new List<TerminalCell>());
                        }
                    }
                    break;
                case 'C': // Cursor Forward
                    {
                        int amount = int.TryParse(paramStr, out int v) ? Math.Max(1, v) : 1;
                        _cursorCol = Math.Max(0, _cursorCol + amount);
                    }
                    break;
                case 'D': // Cursor Back
                    {
                        int amount = int.TryParse(paramStr, out int v) ? Math.Max(1, v) : 1;
                        _cursorCol = Math.Max(0, _cursorCol - amount);
                    }
                    break;
                case 'H': // Cursor Position
                case 'f':
                    {
                        var parts = paramStr.Split(';');
                        int r = parts.Length > 0 && int.TryParse(parts[0], out int pr) ? Math.Max(1, pr) - 1 : 0;
                        int c = parts.Length > 1 && int.TryParse(parts[1], out int pc) ? Math.Max(1, pc) - 1 : 0;
                        _cursorRow = Math.Max(0, r);
                        while (_cursorRow >= _lines.Count)
                        {
                            _lines.Add(new List<TerminalCell>());
                        }
                        _cursorCol = Math.Max(0, c);
                    }
                    break;
                case 'G': // Cursor Horizontal Absolute (CHA)
                case '`': // HPA
                    {
                        int col = int.TryParse(paramStr, out int v) ? Math.Max(1, v) - 1 : 0;
                        _cursorCol = Math.Max(0, col);
                    }
                    break;
                case 'd': // Line Position Absolute (VPA)
                    {
                        int row = int.TryParse(paramStr, out int v) ? Math.Max(1, v) - 1 : 0;
                        _cursorRow = Math.Max(0, row);
                        while (_cursorRow >= _lines.Count)
                        {
                            _lines.Add(new List<TerminalCell>());
                        }
                    }
                    break;
                case 's': // Save Cursor Position
                    _savedCursorRow = _cursorRow;
                    _savedCursorCol = _cursorCol;
                    break;
                case 'u': // Restore Cursor Position
                    _cursorRow = Math.Max(0, _savedCursorRow);
                    while (_cursorRow >= _lines.Count)
                    {
                        _lines.Add(new List<TerminalCell>());
                    }
                    _cursorCol = Math.Max(0, _savedCursorCol);
                    break;
                case 'X': // Erase Character (ECH)
                    if (_cursorRow >= 0 && _cursorRow < _lines.Count)
                    {
                        var line = _lines[_cursorRow];
                        int count = int.TryParse(paramStr, out int cnt) ? Math.Max(1, cnt) : 1;
                        int maxErase = Math.Min(line.Count, Math.Max(0, _cursorCol) + count);
                        for (int k = Math.Max(0, _cursorCol); k < maxErase; k++)
                        {
                            line[k] = TerminalCell.Empty;
                        }
                    }
                    break;
                case 'P': // Delete Character (DCH)
                    if (_cursorRow >= 0 && _cursorRow < _lines.Count)
                    {
                        var line = _lines[_cursorRow];
                        int count = int.TryParse(paramStr, out int cnt) ? Math.Max(1, cnt) : 1;
                        if (_cursorCol >= 0 && _cursorCol < line.Count)
                        {
                            int rem = Math.Min(count, line.Count - _cursorCol);
                            line.RemoveRange(_cursorCol, rem);
                        }
                    }
                    break;
                case '@': // Insert Character (ICH)
                    if (_cursorRow >= 0 && _cursorRow < _lines.Count)
                    {
                        var line = _lines[_cursorRow];
                        int count = int.TryParse(paramStr, out int cnt) ? Math.Max(1, cnt) : 1;
                        while (line.Count < _cursorCol) line.Add(TerminalCell.Empty);
                        if (_cursorCol >= 0 && _cursorCol <= line.Count)
                        {
                            for (int k = 0; k < count; k++)
                            {
                                line.Insert(_cursorCol, TerminalCell.Empty);
                            }
                        }
                    }
                    break;
                case 'M': // Delete Line (DL)
                    if (_cursorRow >= 0 && _cursorRow < _lines.Count)
                    {
                        int count = int.TryParse(paramStr, out int cnt) ? Math.Max(1, cnt) : 1;
                        int rem = Math.Min(count, _lines.Count - _cursorRow);
                        _lines.RemoveRange(_cursorRow, rem);
                        if (_lines.Count == 0) _lines.Add(new List<TerminalCell>());
                    }
                    break;
                case 'L': // Insert Line (IL)
                    if (_cursorRow >= 0 && _cursorRow <= _lines.Count)
                    {
                        int count = int.TryParse(paramStr, out int cnt) ? Math.Max(1, cnt) : 1;
                        for (int k = 0; k < count; k++)
                        {
                            _lines.Insert(_cursorRow, new List<TerminalCell>());
                        }
                    }
                    break;
                case 'h': // Set Mode
                    if (paramStr == "?25") _cursorVisible = true;
                    break;
                case 'l': // Reset Mode
                    if (paramStr == "?25") _cursorVisible = false;
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
            if (!_hasSelection || _lines.Count == 0) return string.Empty;

            int startRow = _selAnchorRow < _selHeadRow ? _selAnchorRow : _selHeadRow;
            int endRow = _selAnchorRow > _selHeadRow ? _selAnchorRow : _selHeadRow;
            int startCol = _selAnchorRow == _selHeadRow ? Math.Min(_selAnchorCol, _selHeadCol) : (_selAnchorRow < _selHeadRow ? _selAnchorCol : _selHeadCol);
            int endCol = _selAnchorRow == _selHeadRow ? Math.Max(_selAnchorCol, _selHeadCol) : (_selAnchorRow > _selHeadRow ? _selAnchorCol : _selHeadCol);

            startRow = Math.Clamp(startRow, 0, _lines.Count - 1);
            endRow = Math.Clamp(endRow, 0, _lines.Count - 1);

            var sb = new StringBuilder();
            for (int r = startRow; r <= endRow; r++)
            {
                var line = _lines[r];
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
            if (row < 0 || row >= _lines.Count) return (0, 0);
            var line = _lines[row];
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

            int clickedRow = Math.Clamp((int)((point.Y - paddingY) / LineHeight) + _scrollOffset, 0, Math.Max(0, _lines.Count - 1));
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
                _selHeadCol = _lines[clickedRow].Count;
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
            if (!_isSelecting || _lines.Count == 0) return;

            var point = e.GetPosition(this);
            double paddingX = 10;
            double paddingY = 6;

            int targetRow = Math.Clamp((int)((point.Y - paddingY) / LineHeight) + _scrollOffset, 0, Math.Max(0, _lines.Count - 1));
            int targetCol = Math.Max(0, (int)Math.Round((point.X - paddingX) / _charWidth));

            // Auto-scroll on edge drag
            if (point.Y < 0 && _scrollOffset > 0)
            {
                _scrollOffset = _scrollOffset > 1 ? _scrollOffset - 1 : 0;
                _userScrolledUp = true;
            }
            else if (point.Y > Bounds.Height)
            {
                int visibleLines = (int)Math.Max(1, Bounds.Height / LineHeight);
                int maxScroll = Math.Max(0, _lines.Count - visibleLines);
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
            int visibleCount = (int)Math.Max(1, Bounds.Height / LineHeight);
            int maxScroll = Math.Max(0, _lines.Count - visibleCount);

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

            // Only forward printable characters (>= space, not control, not DEL 0x7F)
            // Control keys (Backspace, Enter, Tab, Arrows, Ctrl+...) are handled in OnKeyDown
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
                    break;
                case Key.Insert:
                    Terminal.Write("\x1b[2~");
                    break;
                case Key.Tab:
                    Terminal.Write("\t");
                    break;
                case Key.Up:
                    Terminal.Write("\x1b[A");
                    break;
                case Key.Down:
                    Terminal.Write("\x1b[B");
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
                    break;
                case Key.Home:
                    Terminal.Write("\x1b[H");
                    break;
                case Key.End:
                    Terminal.Write("\x1b[F");
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
                    break;
                case Key.B when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x02"); // Char backward
                    break;
                case Key.C when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x03"); // SIGINT
                    break;
                case Key.D when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x04"); // EOF
                    break;
                case Key.E when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x05"); // End of line
                    break;
                case Key.F when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x06"); // Char forward
                    break;
                case Key.K when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x0B"); // Kill line to end
                    break;
                case Key.L when e.KeyModifiers == KeyModifiers.Control:
                    _lines.Clear();
                    _lines.Add(new List<TerminalCell>());
                    _cursorRow = 0;
                    _cursorCol = 0;
                    _scrollOffset = 0;
                    _userScrolledUp = false;
                    break;
                case Key.U when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x15"); // Kill line to start
                    break;
                case Key.W when e.KeyModifiers == KeyModifiers.Control:
                    Terminal.Write("\x17"); // Delete word backward
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

            // Use local coordinates (0,0) for rendering, ignoring parent layout offsets
            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            using var clip = context.PushClip(bounds);

            // Terminal Background (#141414)
            context.FillRectangle(GetCachedBrush(Color.Parse("#141414")), bounds);

            var cursorBrush = GetCachedBrush(Color.Parse("#528BFF"));
            var selectionBrush = GetCachedBrush(Color.FromArgb(110, 38, 79, 120));

            int visibleLineCount = (int)Math.Ceiling(bounds.Height / LineHeight) + 1;
            int startIdx = Math.Max(0, Math.Min(_scrollOffset, _lines.Count - 1));
            int endIdx = Math.Min(_lines.Count, startIdx + visibleLineCount);

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
                var line = _lines[i];
                double y = paddingY + ((i - startIdx) * LineHeight);

                // Draw Selection Highlight Background
                if (_hasSelection && i >= selStartRow && i <= selEndRow)
                {
                    int sC = (i == selStartRow) ? selStartCol : 0;
                    int eC = (i == selEndRow) ? selEndCol : Math.Max(line.Count, _cursorCol);
                    if (eC > sC)
                    {
                        double selX = paddingX + (sC * _charWidth);
                        double selW = (eC - sC) * _charWidth;
                        context.FillRectangle(selectionBrush, new Rect(selX, y, selW, LineHeight));
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

                        double spanX = paddingX + (spanStart * _charWidth);

                        // Draw cell background if set
                        if (cell.Background.HasValue)
                        {
                            double bgW = chars.Length * _charWidth;
                            context.FillRectangle(
                                GetCachedBrush(cell.Background.Value),
                                new Rect(spanX, y, bgW, LineHeight)
                            );
                        }

                        // Draw formatted text run
                        if (!string.IsNullOrEmpty(runText))
                        {
                            var ft = new FormattedText(
                                runText,
                                CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight,
                                _font,
                                FontSize,
                                GetCachedBrush(cell.Foreground)
                            );
                            context.DrawText(ft, new Point(spanX, y));
                        }

                        spanStart = spanEnd;
                    }
                }

                // Render terminal cursor directly on active line
                if (i == _cursorRow && _cursorVisible && IsFocused)
                {
                    double cursorX = paddingX + (_cursorCol * _charWidth);
                    context.FillRectangle(cursorBrush, new Rect(cursorX, y + 1, 2, LineHeight - 2));
                }
            }
        }
    }
}
