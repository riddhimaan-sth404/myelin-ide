using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Myelin.Core.Models;
using Myelin.UI.ViewModels;

namespace Myelin.UI.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _currentVm;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Closed += OnClosed;

            PropertyChanged += (s, e) =>
            {
                if (e.Property == WindowStateProperty && MainRootGrid != null)
                {
                    MainRootGrid.Margin = WindowState == WindowState.Maximized
                        ? new Avalonia.Thickness(7)
                        : new Avalonia.Thickness(0);
                }
            };
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_currentVm != null)
            {
                _currentVm.RequestOpenFile -= OnRequestOpenFile;
                _currentVm.RequestOpenFolder -= OnRequestOpenFolder;
                _currentVm.ClipboardTextRequested -= OnClipboardTextRequested;
                _currentVm.RequestSetClipboard -= OnRequestSetClipboard;
                _currentVm = null;
            }

            if (DataContext is MainWindowViewModel vm)
            {
                _currentVm = vm;
                vm.RequestOpenFile += OnRequestOpenFile;
                vm.RequestOpenFolder += OnRequestOpenFolder;
                vm.ClipboardTextRequested += OnClipboardTextRequested;
                vm.RequestSetClipboard += OnRequestSetClipboard;
            }
        }

        private async void OnRequestSetClipboard(string text)
        {
            var topLevel = GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }

        private async System.Threading.Tasks.Task<string?> OnClipboardTextRequested()
        {
            var topLevel = GetTopLevel(this);
            return topLevel?.Clipboard != null ? await topLevel.Clipboard.GetTextAsync() : null;
        }

        private async void OnRequestOpenFile()
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open File",
                AllowMultiple = false
            });
            if (files.Count > 0) _currentVm?.OpenFile(files[0].Path.LocalPath);
        }

        private async void OnRequestOpenFolder()
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open Workspace Folder",
                AllowMultiple = false
            });
            if (folders.Count > 0) _currentVm?.OpenFolder(folders[0].Path.LocalPath);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _currentVm?.Dispose();
            _currentVm = null;
        }

        private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open Workspace Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0 && DataContext is MainWindowViewModel vm)
            {
                string localPath = folders[0].Path.LocalPath;
                vm.OpenFolder(localPath);
            }
        }

        private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open File",
                AllowMultiple = false
            });

            if (files.Count > 0 && DataContext is MainWindowViewModel vm)
            {
                string localPath = files[0].Path.LocalPath;
                vm.OpenFile(localPath);
            }
        }

        private void OnFileSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is FileNode node)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.SelectedNode = node;
                    if (!node.IsDirectory)
                    {
                        vm.OpenFile(node.Path);
                    }
                }
            }
        }

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm)
            {
                _ = vm.Search.SearchAsync();
                e.Handled = true;
            }
        }

        private void OnCommitKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) != 0 && DataContext is MainWindowViewModel vm)
            {
                _ = vm.SourceControl.CommitAsync();
                e.Handled = true;
            }
        }

        private void OnToggleTerminalClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BottomPanel.Toggle();
                if (vm.BottomPanel.IsOpen && vm.BottomPanel.SelectedTabIndex == 0)
                {
                    TerminalCanvasControl?.Focus();
                }
            }
        }

        private void OnTerminalTabClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BottomPanel.IsOpen = true;
                vm.BottomPanel.SelectedTabIndex = 0;
                TerminalCanvasControl?.Focus();
            }
        }

        private void OnOutputTabClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BottomPanel.IsOpen = true;
                vm.BottomPanel.SelectedTabIndex = 1;
            }
        }

        private void OnProblemsTabClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BottomPanel.IsOpen = true;
                vm.BottomPanel.SelectedTabIndex = 2;
            }
        }

        private void OnDebugConsoleTabClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BottomPanel.IsOpen = true;
                vm.BottomPanel.SelectedTabIndex = 3;
            }
        }

        private void OnCargoBuildClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.RunCargoTask("build");
            }
        }

        private void OnCargoCheckClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.RunCargoTask("check");
            }
        }

        private void OnCargoTestClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.RunCargoTask("test");
            }
        }

        private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save As"
            });
            if (file != null && DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                string path = file.Path.LocalPath;
                vm.SaveTabAs(vm.SelectedTab, path);
            }
        }

        private void OnSaveAllClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                foreach (var tab in vm.Tabs)
                {
                    vm.SaveTab(tab);
                }
            }
        }

        private void OnCloseCurrentTabClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                vm.CloseTab(vm.SelectedTab);
            }
        }

        private void OnCloseAllTabsClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var tabsCopy = System.Linq.Enumerable.ToList(vm.Tabs);
                foreach (var tab in tabsCopy)
                {
                    vm.CloseTab(tab);
                }
            }
        }

        private void OnCloseFolderClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.WorkspaceRoot = null;
                vm.RootNode = null;
                vm.StatusMessage = "Folder closed";
            }
        }

        private void OnCargoCleanClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.RunCargoTask("clean");
            }
        }

        private void OnClearTerminalClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BottomPanel.TerminalSession?.Write("\x0c");
            }
        }

        private void OnNewWindowClick(object? sender, RoutedEventArgs e)
        {
            var newWin = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
            newWin.Show();
        }

        private async void OnCutClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                var sel = vm.Workspace.GetSelection(docId);
                if (sel.anchorLine != sel.headLine || sel.anchorCol != sel.headCol)
                {
                    string text = GetSelectedText(vm.Workspace, docId, sel);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var topLevel = GetTopLevel(this);
                        if (topLevel?.Clipboard != null) await topLevel.Clipboard.SetTextAsync(text);
                        vm.Workspace.InsertAtCursor(docId, "");
                        vm.UpdateStatus();
                    }
                }
            }
        }

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                var sel = vm.Workspace.GetSelection(docId);
                if (sel.anchorLine != sel.headLine || sel.anchorCol != sel.headCol)
                {
                    string text = GetSelectedText(vm.Workspace, docId, sel);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var topLevel = GetTopLevel(this);
                        if (topLevel?.Clipboard != null) await topLevel.Clipboard.SetTextAsync(text);
                    }
                }
            }
        }

        private async void OnPasteClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                var topLevel = GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    string? text = await topLevel.Clipboard.GetTextAsync();
                    if (!string.IsNullOrEmpty(text))
                    {
                        vm.Workspace.InsertAtCursor(vm.SelectedTab.DocId, text);
                        vm.UpdateStatus();
                    }
                }
            }
        }

        private void OnSelectAllClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                nuint totalLines = vm.Workspace.GetLineCount(docId);
                nuint lastLine = totalLines > 0 ? totalLines - 1 : 0;
                string lastLineText = vm.Workspace.GetLine(docId, lastLine);
                vm.Workspace.SetSelection(docId, 0, 0, lastLine, (nuint)lastLineText.Length);
                vm.UpdateStatus();
            }
        }

        private void OnExpandSelectionClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                var (line, _) = vm.Workspace.GetCursor(docId);
                string lineText = vm.Workspace.GetLine(docId, line);
                vm.Workspace.SetSelection(docId, line, 0, line, (nuint)lineText.Length);
                vm.UpdateStatus();
            }
        }

        private void OnShrinkSelectionClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                var (line, col) = vm.Workspace.GetCursor(docId);
                vm.Workspace.SetSelection(docId, line, col, line, col);
                vm.UpdateStatus();
            }
        }

        private void OnCopyLineUpClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                var (line, col) = vm.Workspace.GetCursor(docId);
                string lineText = vm.Workspace.GetLine(docId, line);
                vm.Workspace.SetCursor(docId, line, 0);
                vm.Workspace.InsertAtCursor(docId, lineText + "\n");
                vm.Workspace.SetCursor(docId, line, col);
                vm.UpdateStatus();
            }
        }

        private void OnCopyLineDownClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                var (line, col) = vm.Workspace.GetCursor(docId);
                string lineText = vm.Workspace.GetLine(docId, line);
                vm.Workspace.SetCursor(docId, line, (nuint)lineText.Length);
                vm.Workspace.InsertAtCursor(docId, "\n" + lineText);
                vm.Workspace.SetCursor(docId, line + 1, col);
                vm.UpdateStatus();
            }
        }

        private void OnMoveLineUpClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                var (line, col) = vm.Workspace.GetCursor(docId);
                if (line > 0)
                {
                    string curLine = vm.Workspace.GetLine(docId, line);
                    string prevLine = vm.Workspace.GetLine(docId, line - 1);
                    vm.Workspace.SetSelection(docId, line - 1, 0, line, (nuint)curLine.Length);
                    vm.Workspace.InsertAtCursor(docId, curLine + "\n" + prevLine);
                    vm.Workspace.SetCursor(docId, line - 1, col);
                    vm.UpdateStatus();
                }
            }
        }

        private void OnMoveLineDownClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                ulong docId = vm.SelectedTab.DocId;
                nuint total = vm.Workspace.GetLineCount(docId);
                var (line, col) = vm.Workspace.GetCursor(docId);
                if (line + 1 < total)
                {
                    string curLine = vm.Workspace.GetLine(docId, line);
                    string nextLine = vm.Workspace.GetLine(docId, line + 1);
                    vm.Workspace.SetSelection(docId, line, 0, line + 1, (nuint)nextLine.Length);
                    vm.Workspace.InsertAtCursor(docId, nextLine + "\n" + curLine);
                    vm.Workspace.SetCursor(docId, line + 1, col);
                    vm.UpdateStatus();
                }
            }
        }

        private void OnCargoRunClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.RunCargoTask("run");
            }
        }

        private void OnNextProblemClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BottomPanel.IsOpen = true;
                vm.BottomPanel.SelectedTabIndex = 2;
            }
        }

        private void OnPreviousProblemClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BottomPanel.IsOpen = true;
                vm.BottomPanel.SelectedTabIndex = 2;
            }
        }

        private void OnWelcomeClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                string welcomeContent = "# Welcome to Myelin IDE\n\nHigh-Performance IDE built with Rust & Avalonia UI.\n\n### Key Shortcuts:\n- Ctrl+P: Quick Open\n- Ctrl+Shift+P: Command Palette\n- Ctrl+B: Toggle Sidebar\n- Ctrl+J: Toggle Terminal\n- Ctrl+Shift+B: Build Cargo Workspace\n- F5: Run Project\n";
                ulong docId = vm.Workspace.OpenScratch(welcomeContent);
                var tab = new DocumentTabViewModel(docId, "Welcome.md");
                vm.Tabs.Add(tab);
                vm.SelectedTab = tab;
                vm.UpdateStatus();
            }
        }

        private void OnDocumentationClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void OnAboutClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.StatusMessage = "Myelin IDE v0.1.0 — Powered by Rust & Avalonia UI (MIT License)";
            }
        }

        private static string GetSelectedText(Myelin.Core.NativeWorkspace ws, ulong docId, (nuint anchorLine, nuint anchorCol, nuint headLine, nuint headCol) sel)
        {
            nuint startLine = sel.anchorLine < sel.headLine ? sel.anchorLine : sel.headLine;
            nuint startCol = sel.anchorLine == sel.headLine ? Math.Min(sel.anchorCol, sel.headCol) : (sel.anchorLine < sel.headLine ? sel.anchorCol : sel.headCol);
            nuint endLine = sel.anchorLine > sel.headLine ? sel.anchorLine : sel.headLine;
            nuint endCol = sel.anchorLine == sel.headLine ? Math.Max(sel.anchorCol, sel.headCol) : (sel.anchorLine > sel.headLine ? sel.anchorCol : sel.headCol);

            if (startLine == endLine)
            {
                string line = ws.GetLine(docId, startLine);
                int s = Math.Min((int)startCol, line.Length);
                int e = Math.Min((int)endCol, line.Length);
                return line.Substring(s, e - s);
            }

            var sb = new System.Text.StringBuilder();
            string first = ws.GetLine(docId, startLine);
            sb.Append(first.Substring((int)Math.Min(startCol, (nuint)first.Length)));

            for (nuint l = startLine + 1; l < endLine; l++)
            {
                sb.AppendLine();
                sb.Append(ws.GetLine(docId, l));
            }

            string last = ws.GetLine(docId, endLine);
            sb.AppendLine();
            sb.Append(last.Substring(0, Math.Min((int)endCol, last.Length)));
            return sb.ToString();
        }

        private void OnToggleFullScreenClick(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // If the user clicked inside a Menu, MenuItem, Button, or TextBox, let the control handle the click
            if (e.Source is Avalonia.Visual visual)
            {
                if (Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.Menu>(visual) != null ||
                    Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.MenuItem>(visual) != null ||
                    Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.Button>(visual) != null ||
                    Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.TextBox>(visual) != null)
                {
                    return;
                }
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                }
                else
                {
                    BeginMoveDrag(e);
                }
            }
        }

        private void OnMinimizeClick(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnCloseWindowClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(sender as Avalonia.Visual).Properties;
            if (props.IsMiddleButtonPressed)
            {
                if (sender is Control control && control.DataContext is DocumentTabViewModel tab)
                {
                    if (DataContext is MainWindowViewModel vm)
                    {
                        vm.CloseTab(tab);
                        e.Handled = true;
                    }
                }
            }
        }

        private void OnOpenEditorItemPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(sender as Avalonia.Visual).Properties;
            if (props.IsMiddleButtonPressed)
            {
                if (sender is Control control && control.DataContext is DocumentTabViewModel tab)
                {
                    if (DataContext is MainWindowViewModel vm)
                    {
                        vm.CloseTab(tab);
                        e.Handled = true;
                    }
                }
            }
        }

        private void OnTerminalTabItemPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(sender as Avalonia.Visual).Properties;
            if (props.IsMiddleButtonPressed)
            {
                if (sender is Control control && control.DataContext is TerminalTabItem tab)
                {
                    if (DataContext is MainWindowViewModel vm)
                    {
                        vm.BottomPanel.CloseTerminalTab(tab);
                        e.Handled = true;
                    }
                }
            }
        }

        private void OnBottomPanelTabPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(sender as Avalonia.Visual).Properties;
            if (props.IsMiddleButtonPressed)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.BottomPanel.Close();
                    e.Handled = true;
                }
            }
        }

        private void OnExitClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
