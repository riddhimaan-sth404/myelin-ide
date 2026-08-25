using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core;

namespace Myelin.UI.ViewModels
{
    public class ProblemItem
    {
        public string Severity { get; set; } = "Error";
        public string Message { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public int Line { get; set; } = 1;
        public int Column { get; set; } = 1;

        public string Icon => Severity == "Error" ? "IconError" : "IconWarning";
        public string Color => Severity == "Error" ? "#F48771" : "#CCA700";
    }

    public partial class BottomPanelViewModel : ViewModelBase, IDisposable
    {
        [ObservableProperty]
        private bool _isOpen = false;

        [ObservableProperty]
        private double _panelHeight = 260.0;

        [ObservableProperty]
        private int _selectedTabIndex = 0; // 0 = Terminal, 1 = Output, 2 = Problems

        [ObservableProperty]
        private NativeTerminal? _terminalSession;

        [ObservableProperty]
        private string _buildOutput = "Myelin Build Engine Ready.\n";

        [ObservableProperty]
        private ObservableCollection<ProblemItem> _problems = new();

        [ObservableProperty]
        private int _errorCount = 0;

        [ObservableProperty]
        private int _warningCount = 0;

        public BottomPanelViewModel()
        {
            try
            {
                TerminalSession = new NativeTerminal(120, 30, Directory.GetCurrentDirectory());
            }
            catch (Exception ex)
            {
                BuildOutput += $"[PTY Initialization Warning]: {ex.Message}\n";
            }
        }

        public void SetWorkingDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                var old = TerminalSession;
                TerminalSession = new NativeTerminal(120, 30, path);
                old?.Dispose();
            }
            catch (Exception ex)
            {
                BuildOutput += $"[Terminal Warning]: {ex.Message}\n";
            }
        }

        public void Toggle()
        {
            IsOpen = !IsOpen;
        }

        [RelayCommand]
        public void Close()
        {
            IsOpen = false;
        }

        [RelayCommand]
        public void OpenExternalAlacritty()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "alacritty",
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    UseShellExecute = true,
                });
            }
            catch
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    UseShellExecute = true,
                });
            }
        }

        public void AppendBuildLog(string log)
        {
            BuildOutput += $"[{DateTime.Now:HH:mm:ss}] {log}\n";
        }

        public void AddProblem(string severity, string message, string file, int line, int col)
        {
            Problems.Add(new ProblemItem
            {
                Severity = severity,
                Message = message,
                File = file,
                Line = line,
                Column = col
            });

            if (severity == "Error") ErrorCount++;
            else if (severity == "Warning") WarningCount++;
        }

        public void ClearProblems()
        {
            Problems.Clear();
            ErrorCount = 0;
            WarningCount = 0;
        }

        public void Dispose()
        {
            TerminalSession?.Dispose();
        }
    }
}
