using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core;
using Myelin.Core.Commands;
using Myelin.Core.Models;
using Myelin.Core.Services;

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
        private FileNode? _selectedNode;

        [ObservableProperty]
        private ObservableCollection<DocumentTabViewModel> _tabs = new();

        [ObservableProperty]
        private DocumentTabViewModel? _selectedTab;

        [ObservableProperty]
        private bool _isSidebarOpen = true;

        [ObservableProperty]
        private double _sidebarWidth = 260.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveSidebarTitle))]
        private int _activeActivityIndex = 0; // 0 = Explorer, 1 = Search, 2 = Git, 3 = Settings, 4 = Extensions, 5 = Run & Debug, 6 = Remote Explorer

        public string ActiveSidebarTitle => ActiveActivityIndex switch
        {
            1 => "SEARCH",
            2 => "SOURCE CONTROL",
            3 => "SETTINGS",
            4 => "EXTENSIONS",
            5 => "RUN AND DEBUG",
            6 => "REMOTE EXPLORER",
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
        private SourceControlViewModel _sourceControl = new();

        [ObservableProperty]
        private WorkspaceSearchViewModel _search = new();

        [ObservableProperty]
        private ExtensionsViewModel _extensions = new();

        [ObservableProperty]
        private RemoteExplorerViewModel _remoteExplorer = new();

        [ObservableProperty]
        private RunAndDebugViewModel _debug = new();

        [ObservableProperty]
        private SettingsViewModel _settings = new();

        [ObservableProperty]
        private CommandPaletteViewModel _commandPalette;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private string _gitBranch = "main";

        [ObservableProperty]
        private bool _isLiveServerRunning;

        [ObservableProperty]
        private string _liveServerStatusText = "Go Live";

        [ObservableProperty]
        private bool _isLocalServerRunning;

        [ObservableProperty]
        private string _localServerStatusText = "Host Server";

        [ObservableProperty]
        private string _activeLanguageServerText = "LSP Ready";

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

            SourceControl.Initialize(WorkspaceRoot, path => OpenFile(path));
            Search.Initialize(WorkspaceRoot, (path, line) => OpenFile(path, line));
            Extensions.RequestOpenExtensionTab += (extItem, readme) => OpenExtensionTab(extItem, readme);
            Debug.Initialize(WorkspaceRoot);
            Debug.RequestNavigateToFile += (path, line) => OpenFile(path, (int)line);
            RemoteExplorer.RequestOpenFile += path => OpenFile(path);
            RemoteExplorer.RequestLaunchRemoteTerminal += (cmd, title) =>
            {
                BottomPanel.IsOpen = true;
                BottomPanel.SelectedTabIndex = 0;
                BottomPanel.CreateTerminalTab();
                BottomPanel.TerminalSession?.Write(cmd + "\r");
            };

            // Hook Live Server & Local Server events
            LiveServerService.Instance.ServerStateChanged += (running, info) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsLiveServerRunning = running;
                    LiveServerStatusText = running ? $"Port: {LiveServerService.Instance.ServerPort}" : "Go Live";
                    StatusMessage = running ? $"Live Server running on {info}" : "Live Server stopped";
                });
            };

            LocalServerRunnerService.Instance.ServerStatusChanged += (running, url, info) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsLocalServerRunning = running;
                    LocalServerStatusText = running ? "Running" : "Host Server";
                    StatusMessage = info ?? (running ? "Server running" : "Server stopped");
                    if (running && !string.IsNullOrEmpty(url))
                    {
                        OpenWebPreviewTab(url, $"{LocalServerRunnerService.Instance.CurrentServerType} Server");
                    }
                });
            };

            LanguageServerService.Instance.ServerStatusChanged += (lang, status) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ActiveLanguageServerText = $"{lang}: {status}";
                });
            };

            RegisterCommands();

            // Wire Extension Host GUI interaction bridge
            var host = NodeExtensionHostService.Instance;
            host.WebviewPanelCreated += (panelId, viewType, title) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var tab = new DocumentTabViewModel(panelId, title);
                    Tabs.Add(tab);
                    SelectedTab = tab;
                });
            };

            host.WebviewPanelDisposed += (panelId) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    for (int i = Tabs.Count - 1; i >= 0; i--)
                    {
                        if (Tabs[i].WebviewPanelId == panelId)
                        {
                            CloseTab(Tabs[i]);
                        }
                    }
                });
            };

            host.StatusBarUpdated += (msg) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!string.IsNullOrEmpty(msg)) StatusMessage = msg;
                });
            };

            host.MessageReceived += (type, msg) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = $"[{type}] {msg}";
                });
            };

            // Start Node.js extension host in background
            _ = host.StartAsync(WorkspaceRoot);

            // Open initial scratch buffer
            ulong initialDoc = _workspace.OpenScratch("fn main() {\n    println!(\"Hello from Myelin!\");\n}\n");
            var initialTab = new DocumentTabViewModel(initialDoc, "main.rs");
            initialTab.IsSelected = true;
            Tabs.Add(initialTab);
            SelectedTab = initialTab;
        }

        private void RegisterCommands()
        {
            var reg = CommandRegistry.Instance;

            // File commands
            reg.Register("file.new", "File", "New File", "Ctrl+N", () => CreateFile(), "IconNewFile");
            reg.Register("file.open", "File", "Open File...", "Ctrl+O", () => OpenFilePrompt(), "IconFile");
            reg.Register("file.open_folder", "File", "Open Folder...", "Ctrl+Shift+O", () => OpenFolderPrompt(), "IconFolderOpened");
            reg.Register("file.save", "File", "Save", "Ctrl+S", () => SaveCurrent(), "IconFile");
            reg.Register("file.close", "File", "Close Editor", "Ctrl+W", () => { if (SelectedTab != null) CloseTab(SelectedTab); }, "IconClose");

            // View commands
            reg.Register("view.explorer", "View", "Show Explorer", "Ctrl+Shift+E", () => SelectActivity(0), "IconExplorer");
            reg.Register("view.search", "View", "Find in Files", "Ctrl+Shift+F", () => SelectActivity(1), "IconSearch");
            reg.Register("view.source_control", "View", "Show Source Control", "Ctrl+Shift+G", () => SelectActivity(2), "IconSourceControl");
            reg.Register("view.settings", "View", "Show Settings", "Ctrl+,", () => SelectActivity(3), "IconSettings");
            reg.Register("view.extensions", "View", "Show Extensions", "Ctrl+Shift+X", () => SelectActivity(4), "IconExtensions");
            reg.Register("view.run_and_debug", "View", "Show Run and Debug", "Ctrl+Shift+D", () => SelectActivity(5), "IconDebug");
            reg.Register("view.remote_explorer", "View", "Show Remote Explorer", "", () => SelectActivity(6), "IconRemoteExplorer");
            reg.Register("view.toggle_sidebar", "View", "Toggle Primary Side Bar", "Ctrl+B", () => ToggleSidebar(), "IconCollapse");
            reg.Register("view.toggle_terminal", "View", "Toggle Terminal", "Ctrl+J", () => ToggleTerminal(), "IconTerminal");
            reg.Register("view.problems", "View", "Show Problems Panel", "Ctrl+Shift+M", () => { BottomPanel.SelectedTabIndex = 2; BottomPanel.IsOpen = true; }, "IconWarning");
            reg.Register("view.debug_console", "View", "Show Debug Console", "Ctrl+Shift+Y", () => { BottomPanel.SelectedTabIndex = 3; BottomPanel.IsOpen = true; }, "IconDebug");
            reg.Register("view.command_palette", "View", "Command Palette...", "Ctrl+Shift+P", () => OpenCommandPalette(), "IconCommand");
            reg.Register("view.quick_open", "View", "Go to File...", "Ctrl+P", () => OpenQuickFile(), "IconSearch");

            // Edit commands
            reg.Register("edit.undo", "Edit", "Undo", "Ctrl+Z", () => UndoCurrent(), "IconDiscard");
            reg.Register("edit.redo", "Edit", "Redo", "Ctrl+Y", () => RedoCurrent(), "IconSync");

            // Debug commands
            reg.Register("debug.start", "Debug", "Start Debugging", "F5", () => _ = Debug.StartDebugging(), "IconPlay");
            reg.Register("debug.pause", "Debug", "Pause Execution", "F6", () => _ = Debug.Pause(), "IconPause");
            reg.Register("debug.step_over", "Debug", "Step Over", "F10", () => _ = Debug.StepOver(), "IconStepOver");
            reg.Register("debug.step_into", "Debug", "Step Into", "F11", () => _ = Debug.StepInto(), "IconStepInto");
            reg.Register("debug.step_out", "Debug", "Step Out", "Shift+F11", () => _ = Debug.StepOut(), "IconStepOut");
            reg.Register("debug.restart", "Debug", "Restart Debugging", "Ctrl+Shift+F5", () => _ = Debug.Restart(), "IconRestart");
            reg.Register("debug.stop", "Debug", "Stop Debugging", "Shift+F5", () => _ = Debug.Stop(), "IconStop");

            // Remote commands
            reg.Register("remote.connect_ssh", "Remote", "Connect to SSH Host...", "", () => { SelectActivity(6); RemoteExplorer.OpenAddTargetDialogCommand.Execute(null); }, "IconSsh");
            reg.Register("remote.forward_port", "Remote", "Forward a Port...", "", () => { SelectActivity(6); RemoteExplorer.OpenPortForwardDialogCommand.Execute(null); }, "IconServer");
            reg.Register("remote.refresh", "Remote", "Refresh Targets", "", () => RemoteExplorer.RefreshCollections(), "IconRefresh");

            // Git / Source control commands
            reg.Register("git.refresh", "Git", "Refresh Status", "", () => _ = SourceControl.RefreshStatusAsync(), "IconRefresh");
            reg.Register("git.stage_all", "Git", "Stage All Changes", "", () => _ = SourceControl.StageAllAsync(), "IconPlus");
            reg.Register("git.unstage_all", "Git", "Unstage All Changes", "", () => _ = SourceControl.UnstageAllAsync(), "IconMinus");
            reg.Register("git.commit", "Git", "Commit Staged Changes", "", () => _ = SourceControl.CommitAsync(), "IconCheck");

            // Server & Web commands
            reg.Register("server.host_local", "Server", "Host Local Server (Flask/Node/FastAPI/Live)", "F9", () => _ = HostLocalServer(), "IconPlay");
            reg.Register("server.stop_local", "Server", "Stop Hosted Local Server", "", () => StopLocalServer(), "IconStop");
            reg.Register("liveserver.toggle", "Live Server", "Toggle Live Server (Port 5500)", "", () => _ = ToggleLiveServer(), "IconLink");
            reg.Register("markdown.preview", "Markdown", "Open Preview to the Side", "Ctrl+Shift+V", () => OpenMarkdownPreview(), "IconExplorer");

            // Cargo commands
            reg.Register("cargo.build", "Cargo", "Build Workspace", "Ctrl+Shift+B", () => RunCargoTask("build"), "IconPlay");
            reg.Register("cargo.check", "Cargo", "Check Workspace", "", () => RunCargoTask("check"), "IconCheck");
            reg.Register("cargo.test", "Cargo", "Test Workspace", "", () => RunCargoTask("test"), "IconDebug");
            reg.Register("cargo.run", "Cargo", "Run Project", "F5", () => RunCargoTask("run"), "IconPlay");
        }

        [RelayCommand]
        public async Task ToggleLiveServer()
        {
            if (LiveServerService.Instance.IsRunning)
            {
                LiveServerService.Instance.Stop();
            }
            else
            {
                string root = WorkspaceRoot ?? (SelectedTab?.FilePath != null ? Path.GetDirectoryName(SelectedTab.FilePath)! : Directory.GetCurrentDirectory());
                bool ok = await LiveServerService.Instance.StartAsync(root);
                if (ok)
                {
                    OpenWebPreviewTab(LiveServerService.Instance.ServerUrl, "Live Server :5500");
                }
            }
        }

        [RelayCommand]
        public async Task HostLocalServer()
        {
            string root = WorkspaceRoot ?? (SelectedTab?.FilePath != null ? Path.GetDirectoryName(SelectedTab.FilePath)! : Directory.GetCurrentDirectory());
            var result = await LocalServerRunnerService.Instance.StartLocalServerAsync(root);
            StatusMessage = result.message;
        }

        [RelayCommand]
        public void StopLocalServer()
        {
            LocalServerRunnerService.Instance.Stop();
        }

        [RelayCommand]
        public void OpenMarkdownPreview(DocumentTabViewModel? tab = null)
        {
            tab ??= SelectedTab;
            if (tab == null) return;

            string content = "";
            if (tab.IsTextDocument && tab.DocId != 0)
            {
                nuint lineCount = _workspace.GetLineCount(tab.DocId);
                var lines = _workspace.GetVisibleLines(tab.DocId, 0, lineCount);
                content = string.Join("\n", lines);
            }
            else if (!string.IsNullOrEmpty(tab.FilePath) && File.Exists(tab.FilePath))
            {
                content = File.ReadAllText(tab.FilePath);
            }

            var previewTab = DocumentTabViewModel.CreateMarkdownPreview(content, tab.Title);
            Tabs.Add(previewTab);
            SelectedTab = previewTab;
        }

        public void OpenWebPreviewTab(string url, string title = "Web Preview")
        {
            foreach (var existing in Tabs)
            {
                if (existing.IsWebPreview)
                {
                    existing.WebPreviewUrl = url;
                    existing.Title = title;
                    SelectedTab = existing;
                    return;
                }
            }

            var tab = DocumentTabViewModel.CreateWebPreview(url, title);
            Tabs.Add(tab);
            SelectedTab = tab;
        }

        public event Action? RequestOpenFile;
        public event Action? RequestOpenFolder;
        public event Action<string>? RequestSetClipboard;

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
            SourceControl.SetWorkspaceRoot(path);
            Search.SetWorkspaceRoot(path);
            GitBranch = SourceControl.CurrentBranch;
            StatusMessage = $"Workspace: {Path.GetFileName(path)}";
            IsSidebarOpen = true;
            ActiveActivityIndex = 0;
            _ = NodeExtensionHostService.Instance.StartAsync(path);
        }

        [RelayCommand]
        public void CreateFile(object? param = null)
        {
            string? targetDir = null;
            if (param is FileNode node)
            {
                targetDir = node.IsDirectory ? node.Path : Path.GetDirectoryName(node.Path);
            }
            else if (SelectedNode != null)
            {
                targetDir = SelectedNode.IsDirectory ? SelectedNode.Path : Path.GetDirectoryName(SelectedNode.Path);
            }
            else if (!string.IsNullOrEmpty(WorkspaceRoot) && Directory.Exists(WorkspaceRoot))
            {
                targetDir = WorkspaceRoot;
            }

            if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
            {
                string baseName = "untitled.txt";
                string target = Path.Combine(targetDir, baseName);
                int counter = 1;
                while (File.Exists(target))
                {
                    target = Path.Combine(targetDir, $"untitled_{counter++}.txt");
                }

                try
                {
                    File.WriteAllText(target, "");
                    RefreshExplorer();
                    OpenFile(target);
                    StatusMessage = $"Created file {Path.GetFileName(target)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to create file: {ex.Message}";
                }
            }
            else
            {
                NewFile();
            }
        }

        [RelayCommand]
        public void CreateFolder(object? param = null)
        {
            string? targetDir = null;
            if (param is FileNode node)
            {
                targetDir = node.IsDirectory ? node.Path : Path.GetDirectoryName(node.Path);
            }
            else if (SelectedNode != null)
            {
                targetDir = SelectedNode.IsDirectory ? SelectedNode.Path : Path.GetDirectoryName(SelectedNode.Path);
            }
            else if (!string.IsNullOrEmpty(WorkspaceRoot) && Directory.Exists(WorkspaceRoot))
            {
                targetDir = WorkspaceRoot;
            }

            if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
            {
                string baseName = "new_folder";
                string target = Path.Combine(targetDir, baseName);
                int counter = 1;
                while (Directory.Exists(target))
                {
                    target = Path.Combine(targetDir, $"{baseName}_{counter++}");
                }

                try
                {
                    Directory.CreateDirectory(target);
                    RefreshExplorer();
                    StatusMessage = $"Created folder {Path.GetFileName(target)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to create folder: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public void NewFolder()
        {
            CreateFolder(null);
        }

        [RelayCommand]
        public void DeleteNode(object? param = null)
        {
            FileNode? node = param as FileNode ?? SelectedNode;
            if (node == null || string.IsNullOrEmpty(node.Path)) return;

            try
            {
                if (node.IsDirectory)
                {
                    if (Directory.Exists(node.Path))
                    {
                        Directory.Delete(node.Path, true);
                    }
                }
                else
                {
                    if (File.Exists(node.Path))
                    {
                        for (int i = Tabs.Count - 1; i >= 0; i--)
                        {
                            if (Tabs[i].FilePath == node.Path)
                            {
                                CloseTab(Tabs[i]);
                            }
                        }
                        File.Delete(node.Path);
                    }
                }

                RefreshExplorer();
                StatusMessage = $"Deleted {node.Name}";
                _ = SourceControl.RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to delete {node.Name}: {ex.Message}";
            }
        }

        [RelayCommand]
        public void RevealInExplorer(object? param = null)
        {
            string? targetPath = null;
            if (param is FileNode node)
            {
                targetPath = node.Path;
            }
            else if (param is string p && !string.IsNullOrEmpty(p))
            {
                targetPath = p;
            }
            else if (SelectedNode != null)
            {
                targetPath = SelectedNode.Path;
            }
            else
            {
                targetPath = WorkspaceRoot;
            }

            if (string.IsNullOrEmpty(targetPath)) return;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{targetPath}\"",
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    System.Diagnostics.Process.Start("open", $"-R \"{targetPath}\"");
                }
                else if (OperatingSystem.IsLinux())
                {
                    System.Diagnostics.Process.Start("xdg-open", Path.GetDirectoryName(targetPath) ?? targetPath);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to reveal in explorer: {ex.Message}";
            }
        }

        [RelayCommand]
        public void CopyPath(object? param = null)
        {
            string? targetPath = (param as FileNode)?.Path ?? SelectedNode?.Path ?? WorkspaceRoot;
            if (!string.IsNullOrEmpty(targetPath))
            {
                RequestSetClipboard?.Invoke(targetPath);
                StatusMessage = "Copied path to clipboard";
            }
        }

        [RelayCommand]
        public void CopyRelativePath(object? param = null)
        {
            string? targetPath = (param as FileNode)?.Path ?? SelectedNode?.Path;
            if (!string.IsNullOrEmpty(targetPath) && !string.IsNullOrEmpty(WorkspaceRoot))
            {
                string rel = Path.GetRelativePath(WorkspaceRoot, targetPath);
                RequestSetClipboard?.Invoke(rel);
                StatusMessage = "Copied relative path to clipboard";
            }
            else if (!string.IsNullOrEmpty(targetPath))
            {
                RequestSetClipboard?.Invoke(targetPath);
                StatusMessage = "Copied path to clipboard";
            }
        }

        [RelayCommand]
        public void CollapseAll()
        {
            RefreshExplorer();
            StatusMessage = "Collapsed all folders";
        }

        [RelayCommand]
        public void CloseAllTabs()
        {
            for (int i = Tabs.Count - 1; i >= 0; i--)
            {
                _workspace.CloseDocument(Tabs[i].DocId);
            }
            Tabs.Clear();
            SelectedTab = null;
            UpdateStatus();
        }

        public void OpenFile(string path, int line = 1)
        {
            if (!File.Exists(path)) return;

            foreach (var tab in Tabs)
            {
                if (tab.FilePath == path)
                {
                    SelectedTab = tab;
                    CursorLine = (nuint)Math.Max(1, line);
                    return;
                }
            }

            ulong docId = _workspace.OpenFile(path);
            if (docId != 0)
            {
                var tab = new DocumentTabViewModel(docId, path);
                Tabs.Add(tab);
                SelectedTab = tab;
                CursorLine = (nuint)Math.Max(1, line);
                UpdateStatus();
            }
        }

        public void OpenExtensionTab(ExtensionItemViewModel item, string readme)
        {
            foreach (var existing in Tabs)
            {
                if (existing.IsExtensionDetails && existing.ExtensionItem?.Id == item.Id)
                {
                    existing.ReadmeText = readme;
                    SelectedTab = existing;
                    return;
                }
            }

            var tab = new DocumentTabViewModel(item, readme);
            Tabs.Add(tab);
            SelectedTab = tab;
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
                _ = SourceControl.RefreshStatusAsync();
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
                _ = SourceControl.RefreshStatusAsync();
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
        public void SelectTab(DocumentTabViewModel tab)
        {
            if (tab != null)
            {
                SelectedTab = tab;
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
            if (oldValue != null) oldValue.IsSelected = false;
            if (newValue != null) newValue.IsSelected = true;
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
            NodeExtensionHostService.Instance.Dispose();
            BottomPanel.Dispose();
            _workspace.Dispose();
        }
    }
}
