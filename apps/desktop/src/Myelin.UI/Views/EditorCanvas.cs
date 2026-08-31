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
using Myelin.Core.Models;
using Myelin.Core.Services;
using Myelin.UI.ViewModels;

namespace Myelin.UI.Views
{
    public class EditorCanvas : Control
    {
        public static readonly StyledProperty<ulong> DocIdProperty =
            AvaloniaProperty.Register<EditorCanvas, ulong>(nameof(DocId));

        public static readonly StyledProperty<NativeWorkspace?> WorkspaceProperty =
            AvaloniaProperty.Register<EditorCanvas, NativeWorkspace?>(nameof(Workspace));

        public ulong DocId
        {
            get => GetValue(DocIdProperty);
            set => SetValue(DocIdProperty, value);
        }

        public NativeWorkspace? Workspace
        {
            get => GetValue(WorkspaceProperty);
            set => SetValue(WorkspaceProperty, value);
        }

        private readonly Typeface _font = new("Cascadia Code, Consolas, Courier New, monospace");
        private const double FontSize = 14.0;
        private double _lineHeight = 22.0;
        private double _charWidth = 8.4;
        private double _digitWidth = 8.4;

        private bool _cursorVisible = true;
        private DispatcherTimer? _blinkTimer;
        private nuint _scrollLineOffset = 0;
        private double _scrollXOffset = 0;
        private double _contentWidth = 0;
        private ulong _contentDocId;

        // Selection & Dragging
        private enum MouseSelectionMode { Character, Word, Line }
        private MouseSelectionMode _mouseSelectionMode = MouseSelectionMode.Character;
        private bool _isDraggingText;
        private nuint _dragAnchorLine;
        private nuint _dragAnchorCol;
        private nuint _wordDragAnchorStartLine;
        private nuint _wordDragAnchorStartCol;
        private nuint _wordDragAnchorEndLine;
        private nuint _wordDragAnchorEndCol;
        private nuint _lineDragAnchorLine;
        private bool _hasKeyboardAnchor;
        private nuint _keyboardAnchorLine;
        private nuint _keyboardAnchorCol;

        // Interactive Scrollbar
        private const double ScrollbarWidth = 14.0;
        private bool _isDraggingScrollThumb;
        private double _scrollThumbDragStartY;
        private nuint _scrollThumbDragStartOffset;
        private bool _isScrollbarHovered;
        private int? _hoveredGutterLine;

        private readonly Dictionary<Color, IBrush> _brushCache = new();
        private readonly IPen _gutterPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#333333")), 1);

        private static readonly Dictionary<string, Color> HexColorCache = new()
        {
            ["#C586C0"] = Color.Parse("#C586C0"),
            ["#4EC9B0"] = Color.Parse("#4EC9B0"),
            ["#DCDCAA"] = Color.Parse("#DCDCAA"),
            ["#CE9178"] = Color.Parse("#CE9178"),
            ["#B5CEA8"] = Color.Parse("#B5CEA8"),
            ["#6A9955"] = Color.Parse("#6A9955"),
            ["#D4D4D4"] = Color.Parse("#D4D4D4"),
            ["#569CD6"] = Color.Parse("#569CD6"),
            ["#D7BA7D"] = Color.Parse("#D7BA7D"),
            ["#4FC1FF"] = Color.Parse("#4FC1FF"),
            ["#9CDCFE"] = Color.Parse("#9CDCFE"),
            ["#808080"] = Color.Parse("#808080"),
            ["#858585"] = Color.Parse("#858585"),
            ["#C6C6C6"] = Color.Parse("#C6C6C6"),
            ["#282828"] = Color.Parse("#282828"),
            ["#252526"] = Color.Parse("#252526"),
            ["#1E1E1E"] = Color.Parse("#1E1E1E"),
            ["#528BFF"] = Color.Parse("#528BFF"),
            ["#333333"] = Color.Parse("#333333"),
            ["#FFFFFF"] = Color.Parse("#FFFFFF"),
        };

        static EditorCanvas()
        {
            AffectsRender<EditorCanvas>(DocIdProperty, WorkspaceProperty, BoundsProperty);
            FocusableProperty.OverrideDefaultValue<EditorCanvas>(true);
        }

        public EditorCanvas()
        {
            Cursor = new Cursor(StandardCursorType.Ibeam);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == DocIdProperty)
            {
                ulong oldDocId = change.GetOldValue<ulong>();
                ulong newDocId = change.GetNewValue<ulong>();

                // Save outgoing tab scroll position
                if (oldDocId != 0 && oldDocId != newDocId)
                {
                    if (DataContext is MainWindowViewModel vm)
                    {
                        var oldTab = vm.Tabs.FirstOrDefault(t => t.DocId == oldDocId);
                        if (oldTab != null)
                        {
                            oldTab.ScrollLineOffset = _scrollLineOffset;
                        }
                    }
                }

                // Reset horizontal scroll and restore incoming tab scroll position
                _scrollXOffset = 0;
                _scrollLineOffset = 0;

                if (newDocId != 0)
                {
                    if (DataContext is MainWindowViewModel vm)
                    {
                        var newTab = vm.Tabs.FirstOrDefault(t => t.DocId == newDocId);
                        if (newTab != null)
                        {
                            _scrollLineOffset = (nuint)newTab.ScrollLineOffset;
                        }
                    }

                    if (Workspace != null)
                    {
                        nuint totalLines = Workspace.GetLineCount(newDocId);
                        int visibleLineCount = (int)Math.Ceiling(Bounds.Height / _lineHeight) + 1;
                        nuint maxScrollOffset = totalLines > (nuint)visibleLineCount
                            ? totalLines - (nuint)visibleLineCount
                            : 0;
                        if (_scrollLineOffset > maxScrollOffset)
                            _scrollLineOffset = maxScrollOffset;
                    }
                }

                ResetBlinkPhase();
                InvalidateVisual();
            }
            else if (change.Property == WorkspaceProperty)
            {
                InvalidateVisual();
            }
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            RecalculateFontMetrics();
            if (Workspace != null && DocId != 0)
            {
                ClampScrollX(ComputeGutterWidth(Workspace.GetLineCount(DocId)));
            }
            InvalidateVisual();
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

            DebuggerService.Instance.BreakpointsChanged += OnDebuggerVisualChanged;
            DebuggerService.Instance.PausedOnFrame += OnDebuggerFrameChanged;
            DebuggerService.Instance.StateChanged += OnDebuggerStateChanged;

            Focus();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _blinkTimer?.Stop();

            DebuggerService.Instance.BreakpointsChanged -= OnDebuggerVisualChanged;
            DebuggerService.Instance.PausedOnFrame -= OnDebuggerFrameChanged;
            DebuggerService.Instance.StateChanged -= OnDebuggerStateChanged;
        }

        private void OnDebuggerVisualChanged() => Dispatcher.UIThread.Post(InvalidateVisual);
        private void OnDebuggerFrameChanged(StackFrameItem? frame) => Dispatcher.UIThread.Post(InvalidateVisual);
        private void OnDebuggerStateChanged(DebugState state) => Dispatcher.UIThread.Post(InvalidateVisual);

        private void RecalculateFontMetrics()
        {
            var testText = new FormattedText(
                "Xg0123456789",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _font,
                FontSize,
                Brushes.White
            );
            _charWidth = testText.Width / 12.0;
            if (_charWidth <= 0) _charWidth = 8.4;

            var digitText = new FormattedText(
                "0",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _font,
                12.0,
                Brushes.White
            );
            _digitWidth = digitText.Width > 0 ? digitText.Width : 7.0;

            _lineHeight = testText.Height > 0 ? testText.Height + 6.0 : 22.0;
        }

        /// <summary>
        /// Converts tab characters to 4 spaces for exact visual and metric parity.
        /// </summary>
        private static string ExpandTabs(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains('\t')) return text;
            return text.Replace("\t", "    ");
        }

        /// <summary>
        /// Measures the pixel width of the first charCount characters with tab expansion.
        /// </summary>
        private double MeasurePrefixWidth(string lineText, int charCount)
        {
            if (string.IsNullOrEmpty(lineText) || charCount <= 0) return 0;
            int count = Math.Min(charCount, lineText.Length);
            string prefix = lineText.Substring(0, count);
            string expanded = ExpandTabs(prefix);
            return expanded.Length * _charWidth;
        }

        /// <summary>
        /// Returns the column index at the given pixel X offset within a line.
        /// </summary>
        private int HitTestColumn(string lineText, double x)
        {
            if (string.IsNullOrEmpty(lineText) || x <= 0) return 0;

            double accumulated = 0;
            for (int i = 0; i < lineText.Length; i++)
            {
                char c = lineText[i];
                double charAdvance = (c == '\t' ? 4.0 : 1.0) * _charWidth;
                if (accumulated + charAdvance / 2.0 > x)
                    return i;
                accumulated += charAdvance;
            }
            return lineText.Length;
        }

        private double ComputeGutterWidth(nuint totalLines)
        {
            int digits = totalLines > 0 ? (int)Math.Floor(Math.Log10(totalLines)) + 1 : 1;
            return Math.Max(54.0, digits * _digitWidth + 22.0);
        }

        private double ViewportTextWidth(double gutterWidth)
        {
            return Math.Max(0.0, Bounds.Width - gutterWidth - 10 - ScrollbarWidth);
        }

        private void ClampScrollX(double gutterWidth)
        {
            double maxScrollX = Math.Max(0.0, _contentWidth - ViewportTextWidth(gutterWidth));
            if (_scrollXOffset < 0) _scrollXOffset = 0;
            if (_scrollXOffset > maxScrollX) _scrollXOffset = maxScrollX;
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

        private static (int start, int end) GetWordRangeAt(string lineText, int col)
        {
            if (string.IsNullOrEmpty(lineText)) return (0, 0);
            int c = Math.Clamp(col, 0, lineText.Length - 1);
            if (char.IsLetterOrDigit(lineText[c]) || lineText[c] == '_')
            {
                int start = c;
                int end = c + 1;
                while (start > 0 && (char.IsLetterOrDigit(lineText[start - 1]) || lineText[start - 1] == '_')) start--;
                while (end < lineText.Length && (char.IsLetterOrDigit(lineText[end]) || lineText[end] == '_')) end++;
                return (start, end);
            }
            else if (char.IsWhiteSpace(lineText[c]))
            {
                int start = c;
                int end = c + 1;
                while (start > 0 && char.IsWhiteSpace(lineText[start - 1])) start--;
                while (end < lineText.Length && char.IsWhiteSpace(lineText[end])) end++;
                return (start, end);
            }
            else
            {
                int start = c;
                int end = c + 1;
                while (start > 0 && !char.IsLetterOrDigit(lineText[start - 1]) && !char.IsWhiteSpace(lineText[start - 1]) && lineText[start - 1] != '_') start--;
                while (end < lineText.Length && !char.IsLetterOrDigit(lineText[end]) && !char.IsWhiteSpace(lineText[end]) && lineText[end] != '_') end++;
                return (start, end);
            }
        }

        private static int FindPreviousWordBoundary(string text, int col)
        {
            if (string.IsNullOrEmpty(text) || col <= 0) return 0;
            int idx = Math.Min(col, text.Length);
            while (idx > 0 && char.IsWhiteSpace(text[idx - 1])) idx--;
            if (idx == 0) return 0;
            bool isWord = char.IsLetterOrDigit(text[idx - 1]) || text[idx - 1] == '_';
            while (idx > 0)
            {
                char prev = text[idx - 1];
                if (char.IsWhiteSpace(prev)) break;
                if (isWord != (char.IsLetterOrDigit(prev) || prev == '_')) break;
                idx--;
            }
            return idx;
        }

        private static int FindNextWordBoundary(string text, int col)
        {
            if (string.IsNullOrEmpty(text) || col >= text.Length) return text?.Length ?? 0;
            int idx = Math.Max(0, col);
            bool isWord = char.IsLetterOrDigit(text[idx]) || text[idx] == '_';
            while (idx < text.Length)
            {
                char curr = text[idx];
                if (char.IsWhiteSpace(curr)) break;
                if (isWord != (char.IsLetterOrDigit(curr) || curr == '_')) break;
                idx++;
            }
            while (idx < text.Length && char.IsWhiteSpace(text[idx])) idx++;
            return idx;
        }

        private ContextMenu CreateEditorContextMenu()
        {
            var menu = new ContextMenu();
            var cutItem = new MenuItem { Header = "Cut", InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
            cutItem.Click += async (s, e) => await ExecuteCutAsync();
            var copyItem = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
            copyItem.Click += async (s, e) => await ExecuteCopyAsync();
            var pasteItem = new MenuItem { Header = "Paste", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
            pasteItem.Click += async (s, e) => await ExecutePasteAsync();
            var selectAllItem = new MenuItem { Header = "Select All", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
            selectAllItem.Click += (s, e) => ExecuteSelectAll();

            menu.Items.Add(cutItem);
            menu.Items.Add(copyItem);
            menu.Items.Add(pasteItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(selectAllItem);
            return menu;
        }

        public async Task ExecuteCopyAsync()
        {
            if (Workspace == null || DocId == 0) return;
            var sel = Workspace.GetSelection(DocId);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null) return;

            if (sel.anchorLine != sel.headLine || sel.anchorCol != sel.headCol)
            {
                string text = GetSelectedText(sel);
                if (!string.IsNullOrEmpty(text))
                {
                    await topLevel.Clipboard.SetTextAsync(text);
                }
            }
            else
            {
                // VS Code style: copy entire active line with newline
                var (curLine, _) = Workspace.GetCursor(DocId);
                string lineText = Workspace.GetLine(DocId, curLine);
                await topLevel.Clipboard.SetTextAsync(lineText + "\n");
            }
        }

        public async Task ExecuteCutAsync()
        {
            if (Workspace == null || DocId == 0) return;
            var sel = Workspace.GetSelection(DocId);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null) return;

            if (sel.anchorLine != sel.headLine || sel.anchorCol != sel.headCol)
            {
                string text = GetSelectedText(sel);
                if (!string.IsNullOrEmpty(text))
                {
                    await topLevel.Clipboard.SetTextAsync(text);
                    Workspace.InsertAtCursor(DocId, "");
                }
            }
            else
            {
                // VS Code style: cut entire active line
                var (curLine, _) = Workspace.GetCursor(DocId);
                string lineText = Workspace.GetLine(DocId, curLine);
                await topLevel.Clipboard.SetTextAsync(lineText + "\n");
                nuint totalLines = Workspace.GetLineCount(DocId);
                if (totalLines <= 1)
                {
                    Workspace.SetSelection(DocId, 0, 0, 0, (nuint)lineText.Length);
                    Workspace.InsertAtCursor(DocId, "");
                }
                else if (curLine + 1 < totalLines)
                {
                    Workspace.SetSelection(DocId, curLine, 0, curLine + 1, 0);
                    Workspace.InsertAtCursor(DocId, "");
                }
                else
                {
                    string prevLine = Workspace.GetLine(DocId, curLine - 1);
                    Workspace.SetSelection(DocId, curLine - 1, (nuint)prevLine.Length, curLine, (nuint)lineText.Length);
                    Workspace.InsertAtCursor(DocId, "");
                }
            }
            ResetBlinkPhase();
            ScrollCaretIntoView();
            InvalidateVisual();
            if (DataContext is MainWindowViewModel vm) vm.UpdateStatus();
        }

        public async Task ExecutePasteAsync()
        {
            if (Workspace == null || DocId == 0) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null) return;
            string? text = await topLevel.Clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                Workspace.InsertAtCursor(DocId, text);
                ResetBlinkPhase();
                ScrollCaretIntoView();
                InvalidateVisual();
                if (DataContext is MainWindowViewModel vm) vm.UpdateStatus();
            }
        }

        public void ExecuteSelectAll()
        {
            if (Workspace == null || DocId == 0) return;
            nuint totalLines = Workspace.GetLineCount(DocId);
            nuint lastLine = totalLines > 0 ? totalLines - 1 : 0;
            string lastLineText = Workspace.GetLine(DocId, lastLine);
            Workspace.SetSelection(DocId, 0, 0, lastLine, (nuint)lastLineText.Length);
            ResetBlinkPhase();
            InvalidateVisual();
            if (DataContext is MainWindowViewModel vm) vm.UpdateStatus();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            ResetBlinkPhase();

            if (Workspace == null || DocId == 0) return;

            var point = e.GetPosition(this);
            var bounds = Bounds;
            nuint totalLines = Workspace.GetLineCount(DocId);
            int visibleLineCount = (int)Math.Ceiling(bounds.Height / _lineHeight);
            nuint maxScroll = totalLines > (nuint)visibleLineCount ? totalLines - (nuint)visibleLineCount : 0;

            // 1. Check if clicking on the Vertical Scrollbar (Right 14px)
            if (point.X >= bounds.Width - ScrollbarWidth)
            {
                if (maxScroll > 0)
                {
                    double trackHeight = bounds.Height;
                    double thumbHeight = Math.Max(24.0, (Math.Min(1.0, (double)visibleLineCount / totalLines) * trackHeight));
                    double thumbY = ((double)_scrollLineOffset / maxScroll) * (trackHeight - thumbHeight);

                    if (point.Y >= thumbY && point.Y <= thumbY + thumbHeight)
                    {
                        _isDraggingScrollThumb = true;
                        _scrollThumbDragStartY = point.Y;
                        _scrollThumbDragStartOffset = _scrollLineOffset;
                    }
                    else
                    {
                        double ratio = Math.Clamp(point.Y / trackHeight, 0.0, 1.0);
                        _scrollLineOffset = (nuint)Math.Round(ratio * maxScroll);
                        InvalidateVisual();
                    }
                }
                e.Handled = true;
                return;
            }

            var props = e.GetCurrentPoint(this).Properties;

            // 2. Right-Click Context Menu
            if (props.IsRightButtonPressed)
            {
                double gWidth = ComputeGutterWidth(totalLines);
                double relX = point.X - (gWidth + 10) + _scrollXOffset;
                int clickLine = (int)(point.Y / _lineHeight) + (int)_scrollLineOffset;
                if (clickLine >= 0 && (nuint)clickLine < totalLines)
                {
                    string clickLineText = Workspace.GetLine(DocId, (nuint)clickLine);
                    int clickCol = Math.Min(HitTestColumn(clickLineText, relX), clickLineText.Length);
                    var sel = Workspace.GetSelection(DocId);
                    bool insideSelection = false;
                    if (sel.anchorLine != sel.headLine || sel.anchorCol != sel.headCol)
                    {
                        nuint sL = Math.Min(sel.anchorLine, sel.headLine);
                        nuint eL = Math.Max(sel.anchorLine, sel.headLine);
                        nuint sC = sel.anchorLine == sel.headLine ? Math.Min(sel.anchorCol, sel.headCol) : (sel.anchorLine < sel.headLine ? sel.anchorCol : sel.headCol);
                        nuint eC = sel.anchorLine == sel.headLine ? Math.Max(sel.anchorCol, sel.headCol) : (sel.anchorLine > sel.headLine ? sel.anchorCol : sel.headCol);
                        if ((nuint)clickLine > sL && (nuint)clickLine < eL) insideSelection = true;
                        else if ((nuint)clickLine == sL && (nuint)clickLine == eL) insideSelection = (nuint)clickCol >= sC && (nuint)clickCol <= eC;
                        else if ((nuint)clickLine == sL) insideSelection = (nuint)clickCol >= sC;
                        else if ((nuint)clickLine == eL) insideSelection = (nuint)clickCol <= eC;
                    }
                    if (!insideSelection)
                    {
                        Workspace.SetCursor(DocId, (nuint)clickLine, (nuint)clickCol);
                    }
                }
                ContextMenu = CreateEditorContextMenu();
                ContextMenu.Open(this);
                e.Handled = true;
                return;
            }

            // 3. Middle-Click: Position cursor and Paste from Clipboard
            if (props.IsMiddleButtonPressed)
            {
                double gWidth = ComputeGutterWidth(totalLines);
                double relX = point.X - (gWidth + 10) + _scrollXOffset;
                int targetL = Math.Clamp((int)(point.Y / _lineHeight) + (int)_scrollLineOffset, 0, (int)(totalLines > 0 ? totalLines - 1 : 0));
                string lineT = Workspace.GetLine(DocId, (nuint)targetL);
                int targetC = Math.Min(HitTestColumn(lineT, relX), lineT.Length);
                Workspace.SetCursor(DocId, (nuint)targetL, (nuint)targetC);

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        string? clipText = await Dispatcher.UIThread.InvokeAsync(async () => await topLevel.Clipboard.GetTextAsync());
                        if (!string.IsNullOrEmpty(clipText))
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                Workspace.InsertAtCursor(DocId, clipText);
                                InvalidateVisual();
                                (DataContext as MainWindowViewModel)?.UpdateStatus();
                            });
                        }
                    });
                }
                InvalidateVisual();
                (DataContext as MainWindowViewModel)?.UpdateStatus();
                e.Handled = true;
                return;
            }

            // 4. Normal Left-Click / Multi-Click / Gutter Selection
            double gutterWidth = ComputeGutterWidth(totalLines);
            double relativeX = point.X - (gutterWidth + 10) + _scrollXOffset;
            double relativeY = point.Y;

            int targetLine = Math.Clamp((int)(relativeY / _lineHeight) + (int)_scrollLineOffset, 0, (int)(totalLines > 0 ? totalLines - 1 : 0));
            string lineText = Workspace.GetLine(DocId, (nuint)targetLine);
            int targetCol = Math.Min(HitTestColumn(lineText, relativeX), lineText.Length);

            // Gutter Click (Breakpoint Toggle or Line Selection)
            if (point.X < gutterWidth)
            {
                if (point.X <= 22)
                {
                    string filePath = (DataContext as MainWindowViewModel)?.SelectedTab?.FilePath ?? "";
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        DebuggerService.Instance.ToggleBreakpoint(filePath, (nuint)(targetLine + 1));
                        InvalidateVisual();
                        e.Handled = true;
                        return;
                    }
                }

                _mouseSelectionMode = MouseSelectionMode.Line;
                _lineDragAnchorLine = (nuint)targetLine;
                if (targetLine + 1 < (int)totalLines)
                {
                    Workspace.SetSelection(DocId, (nuint)targetLine, 0, (nuint)targetLine + 1, 0);
                }
                else
                {
                    Workspace.SetSelection(DocId, (nuint)targetLine, 0, (nuint)targetLine, (nuint)lineText.Length);
                }
                _isDraggingText = true;
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _mouseSelectionMode = MouseSelectionMode.Character;
                var (curLine, curCol) = Workspace.GetCursor(DocId);
                Workspace.SetSelection(DocId, curLine, curCol, (nuint)targetLine, (nuint)targetCol);
                _isDraggingText = true;
                _dragAnchorLine = curLine;
                _dragAnchorCol = curCol;
            }
            else if (e.ClickCount == 3)
            {
                // Triple Click -> Select full line
                _mouseSelectionMode = MouseSelectionMode.Line;
                _lineDragAnchorLine = (nuint)targetLine;
                if (targetLine + 1 < (int)totalLines)
                {
                    Workspace.SetSelection(DocId, (nuint)targetLine, 0, (nuint)targetLine + 1, 0);
                }
                else
                {
                    Workspace.SetSelection(DocId, (nuint)targetLine, 0, (nuint)targetLine, (nuint)lineText.Length);
                }
                _isDraggingText = true;
            }
            else if (e.ClickCount == 2)
            {
                // Double Click -> Select word
                _mouseSelectionMode = MouseSelectionMode.Word;
                var (wStart, wEnd) = GetWordRangeAt(lineText, targetCol);
                _wordDragAnchorStartLine = (nuint)targetLine;
                _wordDragAnchorStartCol = (nuint)wStart;
                _wordDragAnchorEndLine = (nuint)targetLine;
                _wordDragAnchorEndCol = (nuint)wEnd;

                Workspace.SetSelection(DocId, (nuint)targetLine, (nuint)wStart, (nuint)targetLine, (nuint)wEnd);
                _isDraggingText = true;
            }
            else
            {
                // Single Click -> Move caret & reset selection
                _mouseSelectionMode = MouseSelectionMode.Character;
                Workspace.SetCursor(DocId, (nuint)targetLine, (nuint)targetCol);

                _isDraggingText = true;
                _dragAnchorLine = (nuint)targetLine;
                _dragAnchorCol = (nuint)targetCol;
            }

            _cursorVisible = true;
            ScrollCaretIntoView();
            InvalidateVisual();

            if (DataContext is MainWindowViewModel vm)
            {
                vm.UpdateStatus();
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var point = e.GetPosition(this);
            var bounds = Bounds;

            // Handle Scrollbar Dragging
            if (_isDraggingScrollThumb && Workspace != null && DocId != 0)
            {
                nuint totalLines = Workspace.GetLineCount(DocId);
                int visibleLineCount = (int)Math.Ceiling(bounds.Height / _lineHeight);
                nuint maxScroll = totalLines > (nuint)visibleLineCount ? totalLines - (nuint)visibleLineCount : 0;

                if (maxScroll > 0)
                {
                    double trackHeight = bounds.Height;
                    double thumbHeight = Math.Max(24.0, (Math.Min(1.0, (double)visibleLineCount / totalLines) * trackHeight));
                    double availableTrack = trackHeight - thumbHeight;

                    if (availableTrack > 0)
                    {
                        double deltaY = point.Y - _scrollThumbDragStartY;
                        double linesPerPixel = (double)maxScroll / availableTrack;
                        int newOffset = (int)_scrollThumbDragStartOffset + (int)Math.Round(deltaY * linesPerPixel);
                        _scrollLineOffset = (nuint)Math.Clamp(newOffset, 0, (int)maxScroll);
                        InvalidateVisual();
                    }
                }
                return;
            }

            // Update Scrollbar Hover State
            bool isOverScrollbar = point.X >= bounds.Width - ScrollbarWidth;
            if (isOverScrollbar != _isScrollbarHovered)
            {
                _isScrollbarHovered = isOverScrollbar;
                InvalidateVisual();
            }

            // Update Gutter Hover Breakpoint Ghost State
            if (Workspace != null && DocId != 0)
            {
                nuint totalL = Workspace.GetLineCount(DocId);
                double gW = ComputeGutterWidth(totalL);
                if (point.X <= gW && point.X >= 0)
                {
                    int hLine = (int)(point.Y / _lineHeight) + (int)_scrollLineOffset;
                    if (_hoveredGutterLine != hLine && hLine >= 0 && (nuint)hLine < totalL)
                    {
                        _hoveredGutterLine = hLine;
                        InvalidateVisual();
                    }
                }
                else if (_hoveredGutterLine != null)
                {
                    _hoveredGutterLine = null;
                    InvalidateVisual();
                }
            }

            // Handle Text Selection Dragging
            if (!_isDraggingText || Workspace == null || DocId == 0) return;

            nuint docLines = Workspace.GetLineCount(DocId);
            double gutterWidth = ComputeGutterWidth(docLines);
            double relativeX = point.X - (gutterWidth + 10) + _scrollXOffset;
            double relativeY = point.Y;

            // Auto-scroll on edge drag
            if (point.Y < 0 && _scrollLineOffset > 0)
            {
                _scrollLineOffset = _scrollLineOffset > 1 ? _scrollLineOffset - 1 : 0;
            }
            else if (point.Y > bounds.Height)
            {
                int visibleLines = (int)Math.Ceiling(bounds.Height / _lineHeight);
                nuint maxScroll = docLines > (nuint)visibleLines ? docLines - (nuint)visibleLines : 0;
                if (_scrollLineOffset < maxScroll)
                {
                    _scrollLineOffset++;
                }
            }

            int targetLine = Math.Clamp((int)(relativeY / _lineHeight) + (int)_scrollLineOffset, 0, (int)(docLines > 0 ? docLines - 1 : 0));
            string targetLineText = Workspace.GetLine(DocId, (nuint)targetLine);
            int targetCol = Math.Clamp(HitTestColumn(targetLineText, relativeX), 0, targetLineText.Length);

            if (_mouseSelectionMode == MouseSelectionMode.Character)
            {
                Workspace.SetSelection(DocId, _dragAnchorLine, _dragAnchorCol, (nuint)targetLine, (nuint)targetCol);
            }
            else if (_mouseSelectionMode == MouseSelectionMode.Word)
            {
                var (wStart, wEnd) = GetWordRangeAt(targetLineText, targetCol);
                if ((nuint)targetLine > _wordDragAnchorStartLine || ((nuint)targetLine == _wordDragAnchorStartLine && (nuint)targetCol >= _wordDragAnchorStartCol))
                {
                    Workspace.SetSelection(DocId, _wordDragAnchorStartLine, _wordDragAnchorStartCol, (nuint)targetLine, (nuint)wEnd);
                }
                else
                {
                    Workspace.SetSelection(DocId, _wordDragAnchorEndLine, _wordDragAnchorEndCol, (nuint)targetLine, (nuint)wStart);
                }
            }
            else if (_mouseSelectionMode == MouseSelectionMode.Line)
            {
                if ((nuint)targetLine >= _lineDragAnchorLine)
                {
                    if (targetLine + 1 < (int)docLines)
                    {
                        Workspace.SetSelection(DocId, _lineDragAnchorLine, 0, (nuint)targetLine + 1, 0);
                    }
                    else
                    {
                        Workspace.SetSelection(DocId, _lineDragAnchorLine, 0, (nuint)targetLine, (nuint)targetLineText.Length);
                    }
                }
                else
                {
                    string anchorLineText = Workspace.GetLine(DocId, _lineDragAnchorLine);
                    Workspace.SetSelection(DocId, _lineDragAnchorLine, (nuint)anchorLineText.Length, (nuint)targetLine, 0);
                }
            }

            InvalidateVisual();
            if (DataContext is MainWindowViewModel vm) vm.UpdateStatus();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _isDraggingText = false;
            _isDraggingScrollThumb = false;
            _mouseSelectionMode = MouseSelectionMode.Character;
        }

        private void SelectWordAt(nuint line, nuint col)
        {
            if (Workspace == null) return;
            string lineText = Workspace.GetLine(DocId, line);
            if (lineText.Length == 0) return;

            var (start, end) = GetWordRangeAt(lineText, (int)col);
            Workspace.SetSelection(DocId, line, (nuint)start, line, (nuint)end);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            if (Workspace == null || DocId == 0 || string.IsNullOrEmpty(e.Text)) return;

            if (e.Text != "\r" && e.Text != "\n" && e.Text != "\b" && e.Text != "\t" && e.Text != " ")
            {
                Workspace.InsertAtCursor(DocId, e.Text);
                ResetBlinkPhase();
                ScrollCaretIntoView();
                InvalidateVisual();

                if (DataContext is MainWindowViewModel vm)
                {
                    vm.UpdateStatus();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Workspace == null || DocId == 0) return;

            var (curLine, curCol) = Workspace.GetCursor(DocId);
            nuint totalLines = Workspace.GetLineCount(DocId);
            bool handled = true;

            switch (e.Key)
            {
                case Key.Enter:
                    Workspace.InsertAtCursor(DocId, "\n");
                    break;
                case Key.Back:
                    Workspace.Backspace(DocId);
                    break;
                case Key.Delete:
                    Workspace.DeleteForward(DocId);
                    break;
                case Key.Tab:
                    Workspace.InsertAtCursor(DocId, "    ");
                    break;
                case Key.Space:
                    Workspace.InsertAtCursor(DocId, " ");
                    break;
                case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    ExecuteSelectAll();
                    break;
                case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    _ = ExecuteCopyAsync();
                    break;
                case Key.X when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    _ = ExecuteCutAsync();
                    break;
                case Key.V when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    _ = ExecutePasteAsync();
                    break;
                case Key.Left:
                    {
                        string curLineText = Workspace.GetLine(DocId, curLine);
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                        {
                            int newCol = FindPreviousWordBoundary(curLineText, (int)curCol);
                            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                            {
                                if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                                Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine, (nuint)newCol);
                            }
                            else
                            {
                                _hasKeyboardAnchor = false;
                                Workspace.SetCursor(DocId, curLine, (nuint)newCol);
                            }
                        }
                        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            if (curCol > 0)
                            {
                                Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine, curCol - 1);
                            }
                            else if (curLine > 0)
                            {
                                string prevLine = Workspace.GetLine(DocId, curLine - 1);
                                Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine - 1, (nuint)prevLine.Length);
                            }
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            if (curCol > 0)
                            {
                                Workspace.SetCursor(DocId, curLine, curCol - 1);
                            }
                            else if (curLine > 0)
                            {
                                string prevLine = Workspace.GetLine(DocId, curLine - 1);
                                Workspace.SetCursor(DocId, curLine - 1, (nuint)prevLine.Length);
                            }
                            else
                            {
                                Workspace.SetCursor(DocId, 0, 0);
                            }
                        }
                    }
                    break;
                case Key.Right:
                    {
                        string curLineText = Workspace.GetLine(DocId, curLine);
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                        {
                            int newCol = FindNextWordBoundary(curLineText, (int)curCol);
                            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                            {
                                if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                                Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine, (nuint)newCol);
                            }
                            else
                            {
                                _hasKeyboardAnchor = false;
                                Workspace.SetCursor(DocId, curLine, (nuint)newCol);
                            }
                        }
                        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            if (curCol < (nuint)curLineText.Length)
                            {
                                Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine, curCol + 1);
                            }
                            else if (curLine + 1 < totalLines)
                            {
                                Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine + 1, 0);
                            }
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            if (curCol < (nuint)curLineText.Length)
                            {
                                Workspace.SetCursor(DocId, curLine, curCol + 1);
                            }
                            else if (curLine + 1 < totalLines)
                            {
                                Workspace.SetCursor(DocId, curLine + 1, 0);
                            }
                        }
                    }
                    break;
                case Key.Up:
                    if (curLine > 0)
                    {
                        string targetLineText = Workspace.GetLine(DocId, curLine - 1);
                        var vm = DataContext as MainWindowViewModel;
                        var tab = vm?.Tabs.FirstOrDefault(t => t.DocId == DocId);
                        nuint goalCol = tab?.DesiredColumn ?? curCol;
                        nuint clampedCol = Math.Min(goalCol, (nuint)targetLineText.Length);
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine - 1, clampedCol);
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            Workspace.SetCursor(DocId, curLine - 1, clampedCol);
                        }
                        if (tab != null && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                            tab.DesiredColumn = goalCol;
                    }
                    else if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        _hasKeyboardAnchor = false;
                        Workspace.SetCursor(DocId, 0, 0);
                    }
                    break;
                case Key.Down:
                    if (curLine + 1 < totalLines)
                    {
                        string targetLineText = Workspace.GetLine(DocId, curLine + 1);
                        var vm = DataContext as MainWindowViewModel;
                        var tab = vm?.Tabs.FirstOrDefault(t => t.DocId == DocId);
                        nuint goalCol = tab?.DesiredColumn ?? curCol;
                        nuint clampedCol = Math.Min(goalCol, (nuint)targetLineText.Length);
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine + 1, clampedCol);
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            Workspace.SetCursor(DocId, curLine + 1, clampedCol);
                        }
                        if (tab != null && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                            tab.DesiredColumn = goalCol;
                    }
                    else if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        string curLineText = Workspace.GetLine(DocId, curLine);
                        _hasKeyboardAnchor = false;
                        Workspace.SetCursor(DocId, curLine, (nuint)curLineText.Length);
                    }
                    break;
                case Key.PageUp:
                    {
                        int visibleLineCount = (int)Math.Ceiling(Bounds.Height / _lineHeight) + 1;
                        var newLine = curLine > (nuint)visibleLineCount ? curLine - (nuint)visibleLineCount : 0;
                        string targetLineText = Workspace.GetLine(DocId, newLine);
                        nuint clampedCol = Math.Min(curCol, (nuint)targetLineText.Length);
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, newLine, clampedCol);
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            Workspace.SetCursor(DocId, newLine, clampedCol);
                        }
                    }
                    break;
                case Key.PageDown:
                    {
                        int visibleLineCount = (int)Math.Ceiling(Bounds.Height / _lineHeight) + 1;
                        var newLine = curLine + (nuint)visibleLineCount;
                        if (newLine >= totalLines) newLine = totalLines > 0 ? totalLines - 1 : 0;
                        string targetLineText = Workspace.GetLine(DocId, newLine);
                        nuint clampedCol = Math.Min(curCol, (nuint)targetLineText.Length);
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, newLine, clampedCol);
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            Workspace.SetCursor(DocId, newLine, clampedCol);
                        }
                    }
                    break;
                case Key.Home:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, 0, 0);
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            Workspace.SetCursor(DocId, 0, 0);
                        }
                    }
                    else
                    {
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine, 0);
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            Workspace.SetCursor(DocId, curLine, 0);
                        }
                    }
                    break;
                case Key.End:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        nuint lastL = totalLines > 0 ? totalLines - 1 : 0;
                        string lastLineText = Workspace.GetLine(DocId, lastL);
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, lastL, (nuint)lastLineText.Length);
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            Workspace.SetCursor(DocId, lastL, (nuint)lastLineText.Length);
                        }
                    }
                    else
                    {
                        string activeLine = Workspace.GetLine(DocId, curLine);
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        {
                            if (!_hasKeyboardAnchor) { _hasKeyboardAnchor = true; _keyboardAnchorLine = curLine; _keyboardAnchorCol = curCol; }
                            Workspace.SetSelection(DocId, _keyboardAnchorLine, _keyboardAnchorCol, curLine, (nuint)activeLine.Length);
                        }
                        else
                        {
                            _hasKeyboardAnchor = false;
                            Workspace.SetCursor(DocId, curLine, (nuint)activeLine.Length);
                        }
                    }
                    break;
                case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    Workspace.Undo(DocId);
                    break;
                case Key.Y when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    Workspace.Redo(DocId);
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
                ResetBlinkPhase();
                ScrollCaretIntoView();
                InvalidateVisual();

                if (DataContext is MainWindowViewModel vm)
                {
                    vm.UpdateStatus();
                }
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _hasKeyboardAnchor = false;
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (Workspace == null || DocId == 0) return;

            nuint totalLines = Workspace.GetLineCount(DocId);
            double gutterWidth = ComputeGutterWidth(totalLines);

            bool horizontal = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y);
            if (horizontal)
            {
                double delta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
                _scrollXOffset -= delta * _charWidth * 3.0;
                ClampScrollX(gutterWidth);
                InvalidateVisual();
                return;
            }

            int visibleLineCount = (int)Math.Ceiling(Bounds.Height / _lineHeight) + 1;
            nuint maxScrollOffset = totalLines > (nuint)visibleLineCount
                ? totalLines - (nuint)visibleLineCount
                : 0;

            if (e.Delta.Y > 0)
            {
                _scrollLineOffset = _scrollLineOffset > 3 ? _scrollLineOffset - 3 : 0;
            }
            else if (e.Delta.Y < 0)
            {
                _scrollLineOffset = Math.Min(_scrollLineOffset + 3, maxScrollOffset);
            }

            InvalidateVisual();
        }

        private void ScrollCaretIntoView()
        {
            if (Workspace == null || DocId == 0) return;

            var (cursorLine, cursorCol) = Workspace.GetCursor(DocId);
            int visibleLineCount = (int)Math.Ceiling(Bounds.Height / _lineHeight) + 1;

            if (cursorLine < _scrollLineOffset)
            {
                _scrollLineOffset = cursorLine;
            }
            else if (cursorLine >= _scrollLineOffset + (nuint)visibleLineCount)
            {
                _scrollLineOffset = cursorLine - (nuint)visibleLineCount + 1;
            }

            double gutterWidth = ComputeGutterWidth(Workspace.GetLineCount(DocId));
            string lineText = Workspace.GetLine(DocId, cursorLine);
            double caretX = MeasurePrefixWidth(lineText, (int)cursorCol);
            double viewport = ViewportTextWidth(gutterWidth);
            const double margin = 20.0;

            if (caretX - margin < _scrollXOffset)
            {
                _scrollXOffset = Math.Max(0.0, caretX - margin);
            }
            else if (caretX + margin > _scrollXOffset + viewport)
            {
                _scrollXOffset = Math.Max(0.0, caretX + margin - viewport);
            }
            ClampScrollX(gutterWidth);
        }

        private void ResetBlinkPhase()
        {
            _cursorVisible = true;
            _blinkTimer?.Stop();
            _blinkTimer?.Start();
        }

        private string GetSelectedText((nuint anchorLine, nuint anchorCol, nuint headLine, nuint headCol) sel)
        {
            if (Workspace == null) return string.Empty;

            nuint startLine = sel.anchorLine < sel.headLine ? sel.anchorLine : sel.headLine;
            nuint startCol = sel.anchorLine == sel.headLine ? Math.Min(sel.anchorCol, sel.headCol) : (sel.anchorLine < sel.headLine ? sel.anchorCol : sel.headCol);
            nuint endLine = sel.anchorLine > sel.headLine ? sel.anchorLine : sel.headLine;
            nuint endCol = sel.anchorLine == sel.headLine ? Math.Max(sel.anchorCol, sel.headCol) : (sel.anchorLine > sel.headLine ? sel.anchorCol : sel.headCol);

            if (startLine == endLine)
            {
                string line = Workspace.GetLine(DocId, startLine);
                int s = Math.Min((int)startCol, line.Length);
                int e = Math.Min((int)endCol, line.Length);
                return line.Substring(s, e - s);
            }

            var sb = new StringBuilder();
            string firstLine = Workspace.GetLine(DocId, startLine);
            sb.Append(firstLine.Substring((int)Math.Min(startCol, (nuint)firstLine.Length)));

            for (nuint l = startLine + 1; l < endLine; l++)
            {
                sb.AppendLine();
                sb.Append(Workspace.GetLine(DocId, l));
            }

            string lastLine = Workspace.GetLine(DocId, endLine);
            sb.AppendLine();
            sb.Append(lastLine.Substring(0, Math.Min((int)endCol, lastLine.Length)));

            return sb.ToString();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            // Use local coordinates (0,0) for rendering, ignoring parent layout offsets
            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            using var clip = context.PushClip(bounds);

            // Editor background (#1F1F1F)
            context.FillRectangle(GetCachedBrush(Color.Parse("#1F1F1F")), bounds);

            nuint totalLines = (Workspace != null && DocId != 0) ? Workspace.GetLineCount(DocId) : 0;
            double gutterWidth = ComputeGutterWidth(totalLines);

            if (Workspace == null || DocId == 0)
            {
                var emptyText = new FormattedText(
                    "No file open",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _font,
                    FontSize,
                    GetCachedBrush(Color.Parse("#808080"))
                );
                double emptyX = (bounds.Width - emptyText.Width) / 2.0;
                double emptyY = (bounds.Height - emptyText.Height) / 2.0;
                context.DrawText(emptyText, new Point(Math.Max(gutterWidth + 10, emptyX), emptyY));
                return;
            }

            int visibleLineCount = (int)Math.Ceiling(bounds.Height / _lineHeight) + 1;
            nuint endLine = _scrollLineOffset + (nuint)visibleLineCount;

            List<List<StyledSpan>> styledLines = Workspace.GetStyledLines(DocId, _scrollLineOffset, endLine);
            var (cursorLine, cursorCol) = Workspace.GetCursor(DocId);

            var sel = Workspace.GetSelection(DocId);
            bool hasSelection = sel.anchorLine != sel.headLine || sel.anchorCol != sel.headCol;
            nuint selStartLine = 0, selStartCol = 0, selEndLine = 0, selEndCol = 0;
            if (hasSelection)
            {
                if (sel.anchorLine < sel.headLine || (sel.anchorLine == sel.headLine && sel.anchorCol <= sel.headCol))
                {
                    selStartLine = sel.anchorLine; selStartCol = sel.anchorCol;
                    selEndLine = sel.headLine; selEndCol = sel.headCol;
                }
                else
                {
                    selStartLine = sel.headLine; selStartCol = sel.headCol;
                    selEndLine = sel.anchorLine; selEndCol = sel.anchorCol;
                }
            }

            var gutterBrush = GetCachedBrush(Color.Parse("#858585"));
            var activeGutterBrush = GetCachedBrush(Color.Parse("#C6C6C6"));
            var cursorBrush = GetCachedBrush(Color.Parse("#528BFF"));
            var unfocusedCursorBrush = GetCachedBrush(Color.Parse("#404040"));
            var selectionBrush = GetCachedBrush(Color.Parse("#264F78"));
            var cursorLinePen = new Pen(GetCachedBrush(Color.Parse("#2A2D2E")), 1);
            var breakpointBrush = GetCachedBrush(Color.Parse("#E51400"));
            var pausedLineBrush = GetCachedBrush(Color.Parse("#383515"));
            var pausedLinePen = new Pen(GetCachedBrush(Color.Parse("#FFE66D")), 1);

            string currentFilePath = (DataContext as MainWindowViewModel)?.SelectedTab?.FilePath ?? "";
            bool isDebuggingPaused = DebuggerService.Instance.State == DebugState.Paused;
            nuint? pausedLine = isDebuggingPaused && DebuggerService.Instance.CurrentFrame != null ? DebuggerService.Instance.CurrentFrame.Line : null;

            double textOriginX = gutterWidth + 10 - _scrollXOffset;
            double maxLineWidthSeen = 0;

            // Separate Text Drawing Layer with Gutter Isolation Clipping
            using (context.PushClip(new Rect(gutterWidth + 1, 0, Math.Max(0, bounds.Width - gutterWidth - ScrollbarWidth), bounds.Height)))
            {
                for (int i = 0; i < styledLines.Count; i++)
                {
                    nuint currentLineNumber = _scrollLineOffset + (nuint)i;
                    double y = i * _lineHeight;

                    // Debugger Paused Line Highlight
                    if (pausedLine.HasValue && (currentLineNumber + 1) == pausedLine.Value)
                    {
                        var pausedRect = new Rect(gutterWidth + 1, y + 0.5, Math.Max(0, bounds.Width - gutterWidth - ScrollbarWidth - 2), _lineHeight - 1.0);
                        context.FillRectangle(pausedLineBrush, pausedRect);
                        context.DrawRectangle(null, pausedLinePen, pausedRect);
                    }
                    // Highlight active line (subtle outline kept within bounds)
                    else if (currentLineNumber == cursorLine && IsFocused)
                    {
                        var lineRect = new Rect(gutterWidth + 1, y + 0.5, Math.Max(0, bounds.Width - gutterWidth - ScrollbarWidth - 2), _lineHeight - 1.0);
                        context.DrawRectangle(null, cursorLinePen, lineRect);
                    }

                    // Selection highlight
                    if (hasSelection && currentLineNumber >= selStartLine && currentLineNumber <= selEndLine)
                    {
                        string selLineText = Workspace.GetLine(DocId, currentLineNumber);
                        int sCol = currentLineNumber == selStartLine ? (int)selStartCol : 0;
                        int eCol = currentLineNumber == selEndLine ? (int)selEndCol : selLineText.Length;
                        double selStartX = textOriginX + MeasurePrefixWidth(selLineText, sCol);
                        double selEndX = textOriginX + MeasurePrefixWidth(selLineText, eCol);
                        if (currentLineNumber < selEndLine || (currentLineNumber == selStartLine && currentLineNumber == selEndLine && eCol == selLineText.Length && selStartCol < (nuint)selLineText.Length))
                        {
                            selEndX += _charWidth;
                        }
                        if (selEndX <= selStartX && currentLineNumber < selEndLine)
                        {
                            selEndX = selStartX + _charWidth;
                        }
                        if (selEndX > selStartX)
                        {
                            context.FillRectangle(selectionBrush, new Rect(selStartX, y + 1, selEndX - selStartX, _lineHeight - 2));
                        }
                    }

                    // Line Content with Tab Expansion & Monospace Column Snapping
                    var spans = styledLines[i];
                    double currentSpanX = textOriginX;

                    foreach (var span in spans)
                    {
                        if (string.IsNullOrEmpty(span.Text)) continue;

                        string expandedSpanText = ExpandTabs(span.Text);

                        Color color;
                        if (!HexColorCache.TryGetValue(span.Color, out color))
                        {
                            try { color = Color.Parse(span.Color); }
                            catch { color = Color.Parse("#D4D4D4"); }
                        }

                        var formattedSpan = new FormattedText(
                            expandedSpanText,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            _font,
                            FontSize,
                            GetCachedBrush(color)
                        );

                        if (currentSpanX + formattedSpan.Width >= gutterWidth)
                        {
                            context.DrawText(formattedSpan, new Point(currentSpanX, y + 2));
                        }
                        currentSpanX += expandedSpanText.Length * _charWidth;
                    }

                    maxLineWidthSeen = Math.Max(maxLineWidthSeen, currentSpanX - textOriginX);

                    // Caret Rendering (Exact Monospace Column Alignment)
                    if (currentLineNumber == cursorLine && _cursorVisible)
                    {
                        string lineText = Workspace.GetLine(DocId, currentLineNumber);
                        double cursorX = textOriginX + MeasurePrefixWidth(lineText, (int)cursorCol);
                        var activeCursor = IsFocused ? cursorBrush : unfocusedCursorBrush;
                        context.DrawRectangle(activeCursor, null, new Rect(cursorX, y + 2, 2, _lineHeight - 4), 1, 1);
                    }
                }
            }

            // Gutter Breakpoints & Line Numbers (Rendered outside text clip for crisp numbers)
            for (int i = 0; i < styledLines.Count; i++)
            {
                nuint currentLineNumber = _scrollLineOffset + (nuint)i;
                double y = i * _lineHeight;

                // 1. Breakpoint Dot or Ghost Breakpoint
                var bp = !string.IsNullOrEmpty(currentFilePath) ? DebuggerService.Instance.GetBreakpoint(currentFilePath, currentLineNumber + 1) : null;
                if (bp != null)
                {
                    if (!bp.IsEnabled)
                    {
                        var disabledPen = new Pen(breakpointBrush, 1.5);
                        context.DrawEllipse(null, disabledPen, new Point(9, y + _lineHeight / 2.0), 4, 4);
                    }
                    else if (bp.Kind == BreakpointKind.Logpoint)
                    {
                        context.FillRectangle(breakpointBrush, new Rect(6, y + _lineHeight / 2.0 - 3.5, 7, 7));
                    }
                    else
                    {
                        context.DrawEllipse(breakpointBrush, null, new Point(9, y + _lineHeight / 2.0), 4.5, 4.5);
                    }
                }
                else if (_hoveredGutterLine.HasValue && _hoveredGutterLine.Value == (int)currentLineNumber)
                {
                    var ghostBrush = GetCachedBrush(Color.FromArgb(100, 229, 20, 0));
                    context.DrawEllipse(ghostBrush, null, new Point(9, y + _lineHeight / 2.0), 4.5, 4.5);
                }

                // 2. Debug Paused Indicator
                if (pausedLine.HasValue && (currentLineNumber + 1) == pausedLine.Value)
                {
                    var arrowBrush = GetCachedBrush(Color.Parse("#FFE66D"));
                    var geom = new StreamGeometry();
                    using (var sgc = geom.Open())
                    {
                        sgc.BeginFigure(new Point(5, y + _lineHeight / 2.0 - 4), true);
                        sgc.LineTo(new Point(11, y + _lineHeight / 2.0));
                        sgc.LineTo(new Point(5, y + _lineHeight / 2.0 + 4));
                        sgc.EndFigure(true);
                    }
                    context.DrawGeometry(arrowBrush, null, geom);
                }

                // 3. Line Number
                string lineNumStr = (currentLineNumber + 1).ToString();
                var formattedLineNum = new FormattedText(
                    lineNumStr,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _font,
                    12.0,
                    currentLineNumber == cursorLine ? activeGutterBrush : gutterBrush
                );
                double numX = gutterWidth - formattedLineNum.Width - 10;
                context.DrawText(formattedLineNum, new Point(numX, y + 3));
            }

            if (_contentDocId != DocId)
            {
                _contentWidth = 0;
                _contentDocId = DocId;
            }
            _contentWidth = Math.Max(_contentWidth, maxLineWidthSeen);
            ClampScrollX(gutterWidth);

            // =========================================================================
            // Vertical VS Code-Style Interactive Scrollbar (Right 14px)
            // =========================================================================
            nuint maxScrollLines = totalLines > (nuint)visibleLineCount ? totalLines - (nuint)visibleLineCount : 0;
            if (maxScrollLines > 0)
            {
                double trackX = bounds.Width - ScrollbarWidth;
                double trackHeight = bounds.Height;

                // Track Background (#1E1E1E)
                context.FillRectangle(GetCachedBrush(Color.Parse("#1E1E1E")), new Rect(trackX, 0, ScrollbarWidth, trackHeight));

                // Scrollbar Thumb (#424242 / #4F4F4F)
                double thumbHeight = Math.Max(24.0, (Math.Min(1.0, (double)visibleLineCount / totalLines) * trackHeight));
                double availableTrack = trackHeight - thumbHeight;
                double thumbY = ((double)_scrollLineOffset / maxScrollLines) * availableTrack;

                Color thumbColor = (_isDraggingScrollThumb || _isScrollbarHovered) ? Color.Parse("#4F4F4F") : Color.Parse("#424242");
                var thumbBrush = GetCachedBrush(thumbColor);

                context.FillRectangle(thumbBrush, new Rect(trackX + 3, thumbY, ScrollbarWidth - 6, thumbHeight));
            }
        }
    }
}
