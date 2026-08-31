using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core.Models;
using Myelin.Core.Services;

namespace Myelin.UI.ViewModels
{
    public partial class RunAndDebugViewModel : ViewModelBase
    {
        private readonly DebuggerService _service;
        private readonly LaunchConfigurationService _launchService;
        private string? _workspaceRoot;

        [ObservableProperty]
        private ObservableCollection<DebugConfiguration> _configurations = new();

        [ObservableProperty]
        private DebugConfiguration? _selectedConfiguration;

        [ObservableProperty]
        private ObservableCollection<BreakpointItem> _breakpoints = new();

        [ObservableProperty]
        private ObservableCollection<ThreadItem> _threads = new();

        [ObservableProperty]
        private ThreadItem? _selectedThread;

        [ObservableProperty]
        private ObservableCollection<StackFrameItem> _stackFrames = new();

        [ObservableProperty]
        private ObservableCollection<VariableItem> _variables = new();

        [ObservableProperty]
        private ObservableCollection<WatchItem> _watchItems = new();

        [ObservableProperty]
        private ObservableCollection<ExceptionBreakpointItem> _exceptionBreakpoints = new();

        [ObservableProperty]
        private StackFrameItem? _selectedStackFrame;

        [ObservableProperty]
        private bool _isDebugging;

        [ObservableProperty]
        private bool _isPaused;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private string _statusText = "Ready to debug";

        [ObservableProperty]
        private string _newWatchExpression = "";

        [ObservableProperty]
        private bool _isAddingWatch;

        public event Action<string, nuint>? RequestNavigateToFile;
        public event Action? DebugSessionUpdated;

        public RunAndDebugViewModel()
        {
            _service = DebuggerService.Instance;
            _launchService = LaunchConfigurationService.Instance;

            _launchService.ConfigurationsChanged += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshConfigurations);
            };

            _service.StateChanged += state =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsDebugging = state != DebugState.Inactive && state != DebugState.Terminated;
                    IsPaused = state == DebugState.Paused;
                    IsRunning = state == DebugState.Running;
                    StatusText = state switch
                    {
                        DebugState.Launching => "Launching debugger...",
                        DebugState.Running => "Running (Debug)...",
                        DebugState.Paused => "Paused at breakpoint",
                        DebugState.Terminated => "Debug session ended",
                        _ => "Ready to debug"
                    };

                    RefreshDebugTrees();
                    DebugSessionUpdated?.Invoke();
                });
            };

            _service.PausedOnFrame += frame =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (frame != null)
                    {
                        RequestNavigateToFile?.Invoke(frame.SourceFile, frame.Line);
                    }
                    RefreshDebugTrees();
                });
            };

            _service.BreakpointsChanged += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshBreakpoints);
            };

            RefreshConfigurations();
            RefreshBreakpoints();
            RefreshExceptionBreakpoints();
        }

        public void Initialize(string? workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
            _ = _launchService.LoadConfigurationsFromWorkspaceAsync(workspaceRoot);
        }

        public void RefreshConfigurations()
        {
            Configurations.Clear();
            foreach (var cfg in _launchService.Configurations)
            {
                Configurations.Add(cfg);
            }
            if (Configurations.Count > 0)
            {
                SelectedConfiguration = Configurations[0];
            }
        }

        public void RefreshBreakpoints()
        {
            Breakpoints.Clear();
            foreach (var bp in _service.Breakpoints)
            {
                Breakpoints.Add(bp);
            }
        }

        public void RefreshExceptionBreakpoints()
        {
            ExceptionBreakpoints.Clear();
            foreach (var ex in _service.ExceptionBreakpoints)
            {
                ExceptionBreakpoints.Add(ex);
            }
        }

        public void RefreshDebugTrees()
        {
            Threads.Clear();
            foreach (var t in _service.Threads)
            {
                Threads.Add(t);
            }
            if (Threads.Count > 0 && SelectedThread == null)
            {
                SelectedThread = Threads[0];
            }

            StackFrames.Clear();
            foreach (var frame in _service.StackFrames)
            {
                StackFrames.Add(frame);
            }
            if (StackFrames.Count > 0)
            {
                SelectedStackFrame = StackFrames[0];
            }

            Variables.Clear();
            foreach (var v in _service.Variables)
            {
                Variables.Add(v);
            }

            WatchItems.Clear();
            foreach (var w in _service.WatchItems)
            {
                WatchItems.Add(w);
            }
        }

        [RelayCommand]
        public async Task StartDebugging()
        {
            await _service.StartDebuggingAsync(SelectedConfiguration, _workspaceRoot);
        }

        [RelayCommand]
        public async Task Continue()
        {
            await _service.ContinueAsync();
        }

        [RelayCommand]
        public async Task Pause()
        {
            await _service.PauseAsync();
        }

        [RelayCommand]
        public async Task StepOver()
        {
            await _service.StepOverAsync();
        }

        [RelayCommand]
        public async Task StepInto()
        {
            await _service.StepIntoAsync();
        }

        [RelayCommand]
        public async Task StepOut()
        {
            await _service.StepOutAsync();
        }

        [RelayCommand]
        public async Task Restart()
        {
            await _service.RestartAsync();
        }

        [RelayCommand]
        public async Task Stop()
        {
            await _service.StopAsync();
        }

        [RelayCommand]
        public void ToggleBreakpoint(BreakpointItem bp)
        {
            bp.IsEnabled = !bp.IsEnabled;
            _service.NotifyBreakpointsChanged();
        }

        [RelayCommand]
        public void RemoveBreakpoint(BreakpointItem bp)
        {
            _service.RemoveBreakpoint(bp);
        }

        [RelayCommand]
        public void ClearAllBreakpoints()
        {
            _service.ClearAllBreakpoints();
        }

        [RelayCommand]
        public void OpenAddWatch()
        {
            NewWatchExpression = "";
            IsAddingWatch = true;
        }

        [RelayCommand]
        public void SaveWatchExpression()
        {
            if (!string.IsNullOrWhiteSpace(NewWatchExpression))
            {
                _service.AddWatchExpression(NewWatchExpression);
                RefreshDebugTrees();
            }
            IsAddingWatch = false;
        }

        [RelayCommand]
        public void CancelAddWatch()
        {
            IsAddingWatch = false;
        }

        [RelayCommand]
        public void RemoveWatch(WatchItem item)
        {
            _service.RemoveWatchExpression(item);
            RefreshDebugTrees();
        }

        [RelayCommand]
        public void SelectStackFrame(StackFrameItem frame)
        {
            SelectedStackFrame = frame;
            if (frame != null)
            {
                RequestNavigateToFile?.Invoke(frame.SourceFile, frame.Line);
            }
        }

        [RelayCommand]
        public async Task AddLaunchConfiguration()
        {
            if (!string.IsNullOrEmpty(_workspaceRoot))
            {
                await _launchService.CreateDefaultLaunchJsonAsync(_workspaceRoot);
            }
        }
    }
}
