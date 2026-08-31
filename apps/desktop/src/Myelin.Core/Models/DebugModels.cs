using System;
using System.Collections.Generic;

namespace Myelin.Core.Models
{
    public enum DebugState
    {
        Inactive,
        Launching,
        Running,
        Paused,
        Terminated
    }

    public enum BreakpointKind
    {
        Standard,
        Conditional,
        HitCount,
        Logpoint
    }

    public class DebugConfiguration
    {
        public string Name { get; set; } = "Cargo Debug (lldb)";
        public string Type { get; set; } = "cargo"; // cargo, rust-lldb, gdb, coreclr, node, python
        public string Request { get; set; } = "launch"; // launch, attach
        public string Program { get; set; } = "${workspaceFolder}/target/debug/${workspaceFolderBasename}.exe";
        public string Args { get; set; } = "";
        public string Cwd { get; set; } = "${workspaceFolder}";
        public bool StopOnEntry { get; set; } = false;
        public string? PreLaunchTask { get; set; }
        public Dictionary<string, string> Environment { get; set; } = new();

        public override string ToString() => Name;
    }

    public class BreakpointItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FilePath { get; set; } = "";
        public nuint Line { get; set; } = 1; // 1-indexed
        public nuint Column { get; set; } = 1;
        public bool IsEnabled { get; set; } = true;
        public BreakpointKind Kind { get; set; } = BreakpointKind.Standard;
        public string? Condition { get; set; }
        public string? HitCondition { get; set; }
        public string? LogMessage { get; set; }
        public int HitCount { get; set; } = 0;

        public string DisplayLocation => $"{System.IO.Path.GetFileName(FilePath)} : Line {Line}";
        public string TooltipText => Kind switch
        {
            BreakpointKind.Conditional => $"Breakpoint (Condition: {Condition})",
            BreakpointKind.HitCount => $"Breakpoint (Hit Count: {HitCondition})",
            BreakpointKind.Logpoint => $"Logpoint (Message: {LogMessage})",
            _ => $"Breakpoint at line {Line}"
        };
    }

    public class ThreadItem
    {
        public int Id { get; set; } = 1;
        public string Name { get; set; } = "Main Thread";
        public bool IsActive { get; set; } = true;
        public string DisplayText => $"{Name} (ID: {Id})";
    }

    public class StackFrameItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string SourceFile { get; set; } = "";
        public nuint Line { get; set; } = 1;
        public nuint Column { get; set; } = 1;
        public string ModuleName { get; set; } = "";
        public int ThreadId { get; set; } = 1;

        public string DisplayText => $"{Name} - {System.IO.Path.GetFileName(SourceFile)}:{Line}";
    }

    public class VariableItem
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Type { get; set; } = "";
        public int VariablesReference { get; set; }
        public List<VariableItem> Children { get; set; } = new();
        public bool HasChildren => Children.Count > 0;
    }

    public class WatchItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Expression { get; set; } = "";
        public string Value { get; set; } = "";
        public bool HasError { get; set; } = false;
    }

    public class ExceptionBreakpointItem
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public bool IsEnabled { get; set; } = false;
        public string Description { get; set; } = "";
    }

    public class DebugConsoleMessage
    {
        public string Text { get; set; } = "";
        public string Category { get; set; } = "stdout"; // stdout, stderr, console, dap, error
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
