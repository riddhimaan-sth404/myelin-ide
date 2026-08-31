using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class DebuggerService
    {
        private static readonly Lazy<DebuggerService> _instance = new(() => new DebuggerService());
        public static DebuggerService Instance => _instance.Value;

        public event Action<DebugState>? StateChanged;
        public event Action<StackFrameItem?>? PausedOnFrame;
        public event Action<string>? OutputReceived;
        public event Action<DebugConsoleMessage>? ConsoleMessageReceived;
        public event Action<BreakpointItem>? BreakpointAdded;
        public event Action<BreakpointItem>? BreakpointRemoved;
        public event Action? BreakpointsChanged;

        public void NotifyBreakpointsChanged() => BreakpointsChanged?.Invoke();

        private DebugState _state = DebugState.Inactive;
        public DebugState State
        {
            get => _state;
            private set
            {
                if (_state != value)
                {
                    _state = value;
                    StateChanged?.Invoke(_state);
                }
            }
        }

        private readonly List<BreakpointItem> _breakpoints = new();
        public IReadOnlyList<BreakpointItem> Breakpoints => _breakpoints;

        private readonly List<ThreadItem> _threads = new();
        public IReadOnlyList<ThreadItem> Threads => _threads;

        private readonly List<StackFrameItem> _stackFrames = new();
        public IReadOnlyList<StackFrameItem> StackFrames => _stackFrames;

        private readonly List<VariableItem> _variables = new();
        public IReadOnlyList<VariableItem> Variables => _variables;

        private readonly List<WatchItem> _watchItems = new();
        public IReadOnlyList<WatchItem> WatchItems => _watchItems;

        private readonly List<ExceptionBreakpointItem> _exceptionBreakpoints = new();
        public IReadOnlyList<ExceptionBreakpointItem> ExceptionBreakpoints => _exceptionBreakpoints;

        public IReadOnlyList<DebugConfiguration> Configurations => LaunchConfigurationService.Instance.Configurations;
        public DebugConfiguration? ActiveConfiguration { get; set; }
        public StackFrameItem? CurrentFrame { get; private set; }
        public ThreadItem? ActiveThread { get; set; }

        public DapClient DapClient => _dapClient ??= new DapClient();
        private DapClient? _dapClient;

        public DebuggerService()
        {
            InitializeExceptionBreakpoints();
        }

        public void InitializeExceptionBreakpoints()
        {
            _exceptionBreakpoints.Clear();
            _exceptionBreakpoints.Add(new ExceptionBreakpointItem { Id = "all", Label = "All Exceptions", IsEnabled = false, Description = "Break whenever any exception is thrown" });
            _exceptionBreakpoints.Add(new ExceptionBreakpointItem { Id = "uncaught", Label = "Uncaught Exceptions", IsEnabled = true, Description = "Break only when an exception is not caught by user code" });
        }

        public bool HasBreakpoint(string filePath, nuint line)
        {
            string norm = NormalizePath(filePath);
            return _breakpoints.Any(b => b.Line == line && NormalizePath(b.FilePath) == norm && b.IsEnabled);
        }

        public BreakpointItem? GetBreakpoint(string filePath, nuint line)
        {
            string norm = NormalizePath(filePath);
            return _breakpoints.FirstOrDefault(b => b.Line == line && NormalizePath(b.FilePath) == norm);
        }

        public BreakpointItem ToggleBreakpoint(string filePath, nuint line)
        {
            string norm = NormalizePath(filePath);
            var existing = _breakpoints.FirstOrDefault(b => b.Line == line && NormalizePath(b.FilePath) == norm);
            if (existing != null)
            {
                _breakpoints.Remove(existing);
                BreakpointRemoved?.Invoke(existing);
                BreakpointsChanged?.Invoke();
                return existing;
            }

            var bp = new BreakpointItem
            {
                FilePath = filePath,
                Line = line,
                IsEnabled = true,
                Kind = BreakpointKind.Standard
            };
            _breakpoints.Add(bp);
            BreakpointAdded?.Invoke(bp);
            BreakpointsChanged?.Invoke();
            return bp;
        }

        public BreakpointItem SetConditionalBreakpoint(string filePath, nuint line, string condition)
        {
            var bp = ToggleBreakpoint(filePath, line);
            bp.Kind = BreakpointKind.Conditional;
            bp.Condition = condition;
            BreakpointsChanged?.Invoke();
            return bp;
        }

        public BreakpointItem SetLogpoint(string filePath, nuint line, string logMessage)
        {
            var bp = ToggleBreakpoint(filePath, line);
            bp.Kind = BreakpointKind.Logpoint;
            bp.LogMessage = logMessage;
            BreakpointsChanged?.Invoke();
            return bp;
        }

        public void RemoveBreakpoint(BreakpointItem bp)
        {
            if (_breakpoints.Remove(bp))
            {
                BreakpointRemoved?.Invoke(bp);
                BreakpointsChanged?.Invoke();
            }
        }

        public void ClearAllBreakpoints()
        {
            _breakpoints.Clear();
            BreakpointsChanged?.Invoke();
        }

        public List<BreakpointItem> GetBreakpointsForFile(string filePath)
        {
            string norm = NormalizePath(filePath);
            return _breakpoints.Where(b => NormalizePath(b.FilePath) == norm).ToList();
        }

        public async Task StartDebuggingAsync(DebugConfiguration? config = null, string? workspaceRoot = null)
        {
            config ??= ActiveConfiguration ?? LaunchConfigurationService.Instance.Configurations.FirstOrDefault();
            if (config == null) return;

            ActiveConfiguration = config;
            State = DebugState.Launching;

            var msg = $"[Debugger] Launching session '{config.Name}' ({config.Type})...\n";
            OutputReceived?.Invoke(msg);
            ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = msg, Category = "dap" });

            if (!string.IsNullOrEmpty(config.PreLaunchTask))
            {
                var preMsg = $"[Task] Executing preLaunchTask: {config.PreLaunchTask}...\n";
                OutputReceived?.Invoke(preMsg);
                ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = preMsg, Category = "console" });
            }

            await Task.Delay(300);

            // Populate threads
            _threads.Clear();
            _threads.Add(new ThreadItem { Id = 1, Name = "Main Thread", IsActive = true });
            _threads.Add(new ThreadItem { Id = 2, Name = "Worker Pool #1", IsActive = false });
            ActiveThread = _threads[0];

            // Populate stack frames and variables
            _stackFrames.Clear();
            _variables.Clear();

            var firstBp = _breakpoints.FirstOrDefault(b => b.IsEnabled);
            string activeFile = firstBp?.FilePath ?? (!string.IsNullOrEmpty(workspaceRoot) ? Path.Combine(workspaceRoot, "src", "main.rs") : "src/main.rs");
            nuint activeLine = firstBp?.Line ?? 12;

            _stackFrames.Add(new StackFrameItem
            {
                Id = 1,
                Name = "main()",
                SourceFile = activeFile,
                Line = activeLine,
                ModuleName = "myelin_core",
                ThreadId = 1
            });
            _stackFrames.Add(new StackFrameItem
            {
                Id = 2,
                Name = "myelin_runtime::entry_point()",
                SourceFile = "runtime.rs",
                Line = 48,
                ModuleName = "myelin_runtime",
                ThreadId = 1
            });
            _stackFrames.Add(new StackFrameItem
            {
                Id = 3,
                Name = "std::rt::lang_start()",
                SourceFile = "rt.rs",
                Line = 166,
                ModuleName = "std",
                ThreadId = 1
            });

            // Populate sample variable scopes
            _variables.Add(new VariableItem
            {
                Name = "Locals",
                Type = "Scope",
                Value = "",
                Children = new List<VariableItem>
                {
                    new VariableItem { Name = "workspace_root", Value = $"\"{workspaceRoot ?? "d:/Projects/myelin"}\"", Type = "&str" },
                    new VariableItem { Name = "buffer_len", Value = "14280", Type = "usize" },
                    new VariableItem { Name = "is_dirty", Value = "false", Type = "bool" },
                    new VariableItem { Name = "cursor", Value = "Position { line: 12, col: 4 }", Type = "Position",
                        Children = new List<VariableItem>
                        {
                            new VariableItem { Name = "line", Value = "12", Type = "nuint" },
                            new VariableItem { Name = "col", Value = "4", Type = "nuint" }
                        }
                    }
                }
            });
            _variables.Add(new VariableItem
            {
                Name = "Registers",
                Type = "RegisterSet",
                Value = "",
                Children = new List<VariableItem>
                {
                    new VariableItem { Name = "RAX", Value = "0x00007FF61A2B3C00", Type = "u64" },
                    new VariableItem { Name = "RBX", Value = "0x0000000000000001", Type = "u64" },
                    new VariableItem { Name = "RIP", Value = "0x00007FF61A2B5890", Type = "u64" },
                    new VariableItem { Name = "RSP", Value = "0x0000004C8F9FE120", Type = "u64" }
                }
            });

            EvaluateWatchExpressions();

            State = DebugState.Paused;
            CurrentFrame = _stackFrames.FirstOrDefault();
            PausedOnFrame?.Invoke(CurrentFrame);

            var pauseMsg = $"[Debugger] Paused at {CurrentFrame?.DisplayText}\n";
            OutputReceived?.Invoke(pauseMsg);
            ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = pauseMsg, Category = "stdout" });
        }

        public async Task ContinueAsync()
        {
            if (State != DebugState.Paused) return;

            State = DebugState.Running;
            var msg = "[Debugger] Resuming execution (Continue)...\n";
            OutputReceived?.Invoke(msg);
            ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = msg, Category = "console" });

            await Task.Delay(400);

            if (_breakpoints.Count > 1)
            {
                var nextBp = _breakpoints.Last();
                if (CurrentFrame != null)
                {
                    CurrentFrame.Line = nextBp.Line;
                    CurrentFrame.SourceFile = nextBp.FilePath;
                }
                State = DebugState.Paused;
                PausedOnFrame?.Invoke(CurrentFrame);
                var hitMsg = $"[Debugger] Hit breakpoint at {nextBp.DisplayLocation}\n";
                OutputReceived?.Invoke(hitMsg);
                ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = hitMsg, Category = "stdout" });
            }
            else
            {
                await Task.Delay(300);
                State = DebugState.Terminated;
                var termMsg = "[Debugger] Program exited with code 0 (0x0).\n";
                OutputReceived?.Invoke(termMsg);
                ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = termMsg, Category = "console" });
            }
        }

        public async Task StepOverAsync()
        {
            if (State != DebugState.Paused) return;

            if (CurrentFrame != null)
            {
                CurrentFrame.Line += 1;
                EvaluateWatchExpressions();
                PausedOnFrame?.Invoke(CurrentFrame);
                var stepMsg = $"[Debugger] Stepped over -> Line {CurrentFrame.Line}\n";
                OutputReceived?.Invoke(stepMsg);
                ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = stepMsg, Category = "stdout" });
            }
            await Task.CompletedTask;
        }

        public async Task StepIntoAsync()
        {
            if (State != DebugState.Paused) return;

            if (CurrentFrame != null)
            {
                CurrentFrame.Line += 1;
                EvaluateWatchExpressions();
                PausedOnFrame?.Invoke(CurrentFrame);
                var stepMsg = $"[Debugger] Stepped into -> {CurrentFrame.Name} Line {CurrentFrame.Line}\n";
                OutputReceived?.Invoke(stepMsg);
                ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = stepMsg, Category = "stdout" });
            }
            await Task.CompletedTask;
        }

        public async Task StepOutAsync()
        {
            if (State != DebugState.Paused) return;

            if (_stackFrames.Count > 1)
            {
                _stackFrames.RemoveAt(0);
                CurrentFrame = _stackFrames[0];
                EvaluateWatchExpressions();
                PausedOnFrame?.Invoke(CurrentFrame);
                var stepMsg = $"[Debugger] Stepped out -> {CurrentFrame.DisplayText}\n";
                OutputReceived?.Invoke(stepMsg);
                ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = stepMsg, Category = "stdout" });
            }
            await Task.CompletedTask;
        }

        public async Task PauseAsync()
        {
            if (State == DebugState.Running)
            {
                State = DebugState.Paused;
                PausedOnFrame?.Invoke(CurrentFrame);
                var pauseMsg = "[Debugger] Execution paused by user.\n";
                OutputReceived?.Invoke(pauseMsg);
                ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = pauseMsg, Category = "stdout" });
            }
            await Task.CompletedTask;
        }

        public async Task RestartAsync()
        {
            await StopAsync();
            await StartDebuggingAsync();
        }

        public async Task StopAsync()
        {
            State = DebugState.Terminated;
            CurrentFrame = null;
            _stackFrames.Clear();
            _variables.Clear();
            _threads.Clear();
            PausedOnFrame?.Invoke(null);
            var stopMsg = "[Debugger] Debugging session stopped.\n";
            OutputReceived?.Invoke(stopMsg);
            ConsoleMessageReceived?.Invoke(new DebugConsoleMessage { Text = stopMsg, Category = "dap" });
            State = DebugState.Inactive;
            await Task.CompletedTask;
        }

        public void AddWatchExpression(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return;
            var item = new WatchItem
            {
                Expression = expr.Trim(),
                Value = EvaluateExpression(expr)
            };
            _watchItems.Add(item);
        }

        public void RemoveWatchExpression(WatchItem item)
        {
            _watchItems.Remove(item);
        }

        public void EvaluateWatchExpressions()
        {
            foreach (var item in _watchItems)
            {
                item.Value = EvaluateExpression(item.Expression);
            }
        }

        public string EvaluateExpression(string expr)
        {
            string lower = expr.ToLowerInvariant().Trim();
            if (lower == "buffer_len") return "14280";
            if (lower == "is_dirty") return "false";
            if (lower == "cursor.line") return CurrentFrame?.Line.ToString() ?? "1";
            if (lower.Contains("+") || lower.Contains("*") || int.TryParse(lower, out _)) return "42";
            return $"\"{expr}\"";
        }

        public async Task<string> EvaluateInReplAsync(string expr)
        {
            await Task.Delay(50);
            string res = EvaluateExpression(expr);
            ConsoleMessageReceived?.Invoke(new DebugConsoleMessage
            {
                Text = $"> {expr}\n{res}\n",
                Category = "stdout"
            });
            return res;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace('\\', '/').ToLowerInvariant();
        }
    }
}
