using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core;
using Myelin.Core.Commands;
using Myelin.Core.Models;

namespace Myelin.UI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly NativeWorkspace _workspace;

        [ObservableProperty]
        private string? _workspaceRoot;

        [ObservableProperty]
        private FileNode? _rootNode;

        [ObservableProperty]
        private ObservableCollection<DocumentTabViewModel> _tabs = new();

        [ObservableProperty]
        private DocumentTabViewModel? _selectedTab;

        [ObservableProperty]
        private bool _isSidebarOpen = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveSidebarTitle))]
        private int _activeActivityIndex = 0; // 0 = Explorer, 1 = Search, 2 = Git, 3 = Settings

        public string ActiveSidebarTitle => ActiveActivityIndex switch
        {
            1 => "SEARCH",
            2 => "SOURCE CONTROL",
            3 => "SETTINGS",
            _ => "EXPLORER"
        };

        public string WorkspaceName => !string.IsNullOrEmpty(WorkspaceRoot) 
            ? Path.GetFileName(WorkspaceRoot) 
            : "Myelin IDE";

        [RelayCommand]
        public void RefreshExplorer()
        {
            if (!string.IsNullOrEmpty(WorkspaceRoot) && Directory.Exists(WorkspaceRoot))
            {
                RootNode = NativeWorkspace.ScanDirectory(WorkspaceRoot, 4);
                StatusMessage = "Explorer refreshed";
            }
        }

        [ObservableProperty]
        private BottomPanelViewModel _bottomPanel = new();

        [ObservableProperty]
        private CommandPaletteViewModel _commandPalette;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private string _gitBranch = "main";

        [ObservableProperty]
        private nuint _cursorLine = 1;

        [ObservableProperty]
        private nuint _cursorCol = 1;

        [ObservableProperty]
        private nuint _lineCount = 0;

        [ObservableProperty]
        private string _selectionStatus = "";

        public NativeWorkspace Workspace => _workspace;

        public MainWindowViewModel()
        {
            _workspace = new NativeWorkspace();
            _commandPalette = new CommandPaletteViewModel(this);

            RegisterCommands();

            // Open initial scratch buffer
            ulong initialDoc = _workspace.OpenScratch("fn main() {\n    println!(\"Hello from Myelin!\");\n}\n");
            var initialTab = new DocumentTabViewModel(initialDoc, "main.rs");
            Tabs.Add(initialTab);
            SelectedTab = initialTab;
        }

        private void RegisterCommands()
        {
            var reg = CommandRegistry.Instance;
            reg.Register("file.new", "File", "New File", "Ctrl+N", () => NewFile());
            reg.Register("file.open", "File", "Open File...", "Ctrl+O", () => OpenFilePrompt());
            reg.Register("file.open_folder", "File", "Open Folder...", "Ctrl+Shift+O", () => OpenFolderPrompt());
            reg.Register("file.save", "File", "Save", "Ctrl+S", () => SaveCurrent());

            reg.Register("view.toggle_sidebar", "View", "Toggle Primary Side Bar", "Ctrl+B", () => ToggleSidebar());
            reg.Register("view.toggle_terminal", "View", "Toggle Terminal", "Ctrl+J", () => ToggleTerminal());
            reg.Register("view.command_palette", "View", "Command Palette...", "Ctrl+Shift+P", () => OpenCommandPalette());
            reg.Register("view.quick_open", "View", "Go to File...", "Ctrl+P", () => OpenQuickFile());

            reg.Register("edit.undo", "Edit", "Undo", "Ctrl+Z", () => UndoCurrent());
            reg.Register("edit.redo", "Edit", "Redo", "Ctrl+Y", () => RedoCurrent());

            reg.Register("cargo.build", "Cargo", "Build Workspace", "Ctrl+Shift+B", () => RunCargoTask("build"));
            reg.Register("cargo.check", "Cargo", "Check Workspace", "", () => RunCargoTask("check"));
            reg.Register("cargo.test", "Cargo", "Test Workspace", "", () => RunCargoTask("test"));
            reg.Register("cargo.run", "Cargo", "Run Project", "F5", () => RunCargoTask("run"));
        }

        public event Action? RequestOpenFile;
        public event Action? RequestOpenFolder;

        [RelayCommand]
        public void OpenFilePrompt()
        {
            RequestOpenFile?.Invoke();
        }

        [RelayCommand]
        public void OpenFolderPrompt()
        {
            RequestOpenFolder?.Invoke();
        }

        public void OpenFolder(string path)
        {
            if (!Directory.Exists(path)) return;

            WorkspaceRoot = path;
            RootNode = NativeWorkspace.ScanDirectory(path, 4);
            BottomPanel.SetWorkingDirectory(path);
            StatusMessage = $"Workspace: {Path.GetFileName(path)}";
            IsSidebarOpen = true;
            ActiveActivityIndex = 0;
        }

        public void OpenFile(string path)
        {
            if (!File.Exists(path)) return;

            foreach (var tab in Tabs)
            {
                if (tab.FilePath == path)
                {
                    SelectedTab = tab;
                    return;
                }
            }

            ulong docId = _workspace.OpenFile(path);
            if (docId != 0)
            {
                var tab = new DocumentTabViewModel(docId, path);
                Tabs.Add(tab);
                SelectedTab = tab;
                UpdateStatus();
            }
        }

        [RelayCommand]
        public void ToggleSidebar()
        {
            IsSidebarOpen = !IsSidebarOpen;
        }

        [RelayCommand]
        public void ToggleTerminal()
        {
            BottomPanel.Toggle();
        }

        [RelayCommand]
        public void SelectActivity(object? param)
        {
            int index = 0;
            if (param is int i) index = i;
            else if (param is string s && int.TryParse(s, out int parsed)) index = parsed;

            if (ActiveActivityIndex == index && IsSidebarOpen)
            {
                IsSidebarOpen = false;
            }
            else
            {
                ActiveActivityIndex = index;
                IsSidebarOpen = true;
            }
        }

        [RelayCommand]
        public void OpenCommandPalette()
        {
            CommandPalette.OpenInCommandMode();
        }

        [RelayCommand]
        public void OpenQuickFile()
        {
            CommandPalette.OpenInFileMode();
        }

        [RelayCommand]
        public void NewFile()
        {
            ulong docId = _workspace.OpenScratch("// New File\n");
            var tab = new DocumentTabViewModel(docId, null);
            Tabs.Add(tab);
            SelectedTab = tab;
            UpdateStatus();
        }

        [RelayCommand]
        public void SaveCurrent()
        {
            if (SelectedTab != null)
            {
                SaveTab(SelectedTab);
            }
        }

        public void SaveTab(DocumentTabViewModel tab)
        {
            if (tab != null)
            {
                _workspace.Save(tab.DocId);
                tab.IsDirty = false;
                StatusMessage = $"Saved {tab.Title}";
            }
        }

        public void SaveTabAs(DocumentTabViewModel tab, string newPath)
        {
            if (tab != null && !string.IsNullOrEmpty(newPath))
            {
                nuint totalLines = _workspace.GetLineCount(tab.DocId);
                var lines = _workspace.GetVisibleLines(tab.DocId, 0, totalLines);
                File.WriteAllText(newPath, string.Join("\n", lines));
                tab.FilePath = newPath;
                tab.Title = Path.GetFileName(newPath);
                tab.IsDirty = false;
                StatusMessage = $"Saved {tab.Title}";
            }
        }

        [RelayCommand]
        public void UndoCurrent()
        {
            if (SelectedTab != null)
            {
                _workspace.Undo(SelectedTab.DocId);
                UpdateStatus();
            }
        }

        [RelayCommand]
        public void RedoCurrent()
        {
            if (SelectedTab != null)
            {
                _workspace.Redo(SelectedTab.DocId);
                UpdateStatus();
            }
        }

        [RelayCommand]
        public void CloseTab(DocumentTabViewModel tab)
        {
            int idx = Tabs.IndexOf(tab);
            _workspace.CloseDocument(tab.DocId);
            Tabs.Remove(tab);

            if (SelectedTab == tab)
            {
                if (Tabs.Count > 0)
                {
                    int nextIdx = Math.Min(idx, Tabs.Count - 1);
                    SelectedTab = Tabs[nextIdx];
                }
                else
                {
                    SelectedTab = null;
                }
            }
            UpdateStatus();
        }

        private static string FindCargoExecutable()
        {
            // 1. Try USERPROFILE/.cargo/bin/cargo.exe or HOME/.cargo/bin/cargo
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                string cargoHomePath = Path.Combine(home, ".cargo", "bin", OperatingSystem.IsWindows() ? "cargo.exe" : "cargo");
                if (File.Exists(cargoHomePath))
                {
                    return cargoHomePath;
                }
            }

            // 2. Check PATH environment variable
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string targetName = OperatingSystem.IsWindows() ? "cargo.exe" : "cargo";
                foreach (string part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        string candidate = Path.Combine(part.Trim(), targetName);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch { }
                }
            }

            return "cargo";
        }

        [RelayCommand]
        public void RunCargoTask(string task)
        {
            // Automatically save all dirty open documents to disk so Cargo compiles actual edits
            foreach (var tab in Tabs)
            {
                if (tab.IsDirty && !string.IsNullOrEmpty(tab.FilePath))
                {
                    _workspace.Save(tab.DocId);
                    tab.IsDirty = false;
                }
            }

            if (task == "run")
            {
                BottomPanel.IsOpen = true;
                BottomPanel.SelectedTabIndex = 0; // 0 = Terminal Tab
                if (BottomPanel.TerminalSession != null && BottomPanel.TerminalSession.IsAlive)
                {
                    BottomPanel.TerminalSession.Write("cargo run\r\n");
                    StatusMessage = "Running `cargo run` in terminal...";
                }
                else
                {
                    BottomPanel.AppendBuildLog("[Terminal Warning]: Active terminal session is not available.");
                    StatusMessage = "Terminal session unavailable";
                }
                return;
            }

            BottomPanel.IsOpen = true;
            BottomPanel.SelectedTabIndex = 1; // 1 = Output Tab
            BottomPanel.ClearProblems();
            BottomPanel.AppendBuildLog($"\n==========================================");
            BottomPanel.AppendBuildLog($"[Cargo] Starting task: `cargo {task}`");
            BottomPanel.AppendBuildLog($"==========================================");
            StatusMessage = $"Building ({task})...";

            string workingDir = WorkspaceRoot ?? Directory.GetCurrentDirectory();
            string cargoExe = FindCargoExecutable();

            Task.Run(async () =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = cargoExe,
                        Arguments = task,
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using var proc = new System.Diagnostics.Process { StartInfo = psi };

                    string? lastSeverity = null;
                    string? lastMessage = null;

                    void ParseLine(string line)
                    {
                        if (string.IsNullOrWhiteSpace(line)) return;

                        // Check for rustc error/warning headers
                        if (line.StartsWith("error[") || line.StartsWith("error:"))
                        {
                            lastSeverity = "Error";
                            lastMessage = line;
                        }
                        else if (line.StartsWith("warning:") || line.StartsWith("warning["))
                        {
                            lastSeverity = "Warning";
                            lastMessage = line;
                        }
                        else if (line.TrimStart().StartsWith("-->") && lastMessage != null && lastSeverity != null)
                        {
                            // Parse " --> src\main.rs:12:5"
                            string locationPart = line.TrimStart().Substring(3).Trim();
                            string[] pieces = locationPart.Split(':');
                            if (pieces.Length >= 3 && int.TryParse(pieces[1], out int pLine) && int.TryParse(pieces[2], out int pCol))
                            {
                                string pFile = pieces[0].Trim();
                                string sev = lastSeverity;
                                string msg = lastMessage;
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    BottomPanel.AddProblem(sev, msg, pFile, pLine, pCol);
                                });
                            }
                            lastSeverity = null;
                            lastMessage = null;
                        }
                    }

                    proc.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            string data = e.Data;
                            ParseLine(data);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                BottomPanel.AppendBuildLog(data);
                            });
                        }
                    };

                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            string data = e.Data;
                            ParseLine(data);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                BottomPanel.AppendBuildLog(data);
                            });
                        }
                    };

                    if (!proc.Start())
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            BottomPanel.AppendBuildLog($"[Process Error]: Failed to start `{cargoExe}`.");
                            StatusMessage = "Cargo start failed";
                        });
                        return;
                    }

                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    await proc.WaitForExitAsync();

                    int exitCode = proc.ExitCode;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (exitCode == 0)
                        {
                            BottomPanel.AppendBuildLog($"\n[Cargo] `cargo {task}` completed successfully.");
                            StatusMessage = $"Cargo {task} succeeded";
                        }
                        else
                        {
                            BottomPanel.AppendBuildLog($"\n[Cargo] `cargo {task}` exited with code {exitCode}.");
                            StatusMessage = $"Cargo {task} failed (exit code {exitCode})";
                        }
                    });
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        BottomPanel.AppendBuildLog($"\n[Execution Error]: {ex.Message}");
                        StatusMessage = "Cargo execution error";
                    });
                }
            });
        }

        public void UpdateStatus()
        {
            if (SelectedTab != null)
            {
                var (line, col) = _workspace.GetCursor(SelectedTab.DocId);
                CursorLine = line + 1;
                CursorCol = col + 1;
                LineCount = _workspace.GetLineCount(SelectedTab.DocId);
                SelectedTab.IsDirty = _workspace.IsDirty(SelectedTab.DocId);

                var sel = _workspace.GetSelection(SelectedTab.DocId);
                if (sel.anchorLine != sel.headLine || sel.anchorCol != sel.headCol)
                {
                    if (sel.anchorLine == sel.headLine)
                    {
                        nuint chars = sel.anchorCol > sel.headCol ? sel.anchorCol - sel.headCol : sel.headCol - sel.anchorCol;
                        SelectionStatus = $" ({chars} selected)";
                    }
                    else
                    {
                        nuint lines = sel.anchorLine > sel.headLine ? sel.anchorLine - sel.headLine + 1 : sel.headLine - sel.anchorLine + 1;
                        SelectionStatus = $" ({lines} lines selected)";
                    }
                }
                else
                {
                    SelectionStatus = "";
                }
            }
            else
            {
                CursorLine = 0;
                CursorCol = 0;
                LineCount = 0;
                SelectionStatus = "";
            }
        }

        partial void OnSelectedTabChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
        {
            UpdateStatus();
        }

        /// <summary>
        /// Pastes clipboard text into the currently selected document.
        /// Re-validates the selected tab after the async clipboard fetch so a
        /// tab switch during the await cannot paste into the wrong document.
        /// </summary>
        public async System.Threading.Tasks.Task PasteAsync()
        {
            var targetTab = SelectedTab;
            if (targetTab == null) return;

            // The clipboard access is provided by the view via this event hook.
            string? text = ClipboardTextRequested != null ? await ClipboardTextRequested.Invoke() : null;
            if (string.IsNullOrEmpty(text)) return;

            // Guard: user may have switched tabs while the clipboard was read.
            if (SelectedTab == null || SelectedTab != targetTab) return;

            // Normalize Windows CRLF / lone CR to LF so the rope stays clean.
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            _workspace.InsertAtCursor(targetTab.DocId, text);
            UpdateStatus();
        }

        /// <summary>View supplies clipboard text through this hook (TopLevel-bound).</summary>
        public event Func<System.Threading.Tasks.Task<string?>>? ClipboardTextRequested;

        public void Dispose()
        {
            BottomPanel.Dispose();
            _workspace.Dispose();
        }
    }
}
