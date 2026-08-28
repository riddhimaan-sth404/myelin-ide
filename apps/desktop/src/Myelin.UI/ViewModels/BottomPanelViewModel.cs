using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core;
using Myelin.Core.Models;
using Myelin.Core.Services;

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

    public partial class TerminalTabItem : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private string _title = "Terminal";

        [ObservableProperty]
        private TerminalProfile _profile;

        [ObservableProperty]
        private NativeTerminal _session;

        [ObservableProperty]
        private bool _isActive;

        public TerminalTabItem(TerminalProfile profile, NativeTerminal session)
        {
            _profile = profile;
            _session = session;
            _title = profile.Name;
        }

        public void Dispose()
        {
            Session?.Dispose();
        }
    }

    public partial class BottomPanelViewModel : ViewModelBase, IDisposable
    {
        [ObservableProperty]
        private bool _isOpen = false;

        [ObservableProperty]
        private double _panelHeight = 260.0;

        [ObservableProperty]
        private bool _isMaximized = false;

        private double _previousHeight = 260.0;

        [RelayCommand]
        public void ToggleMaximize()
        {
            if (IsMaximized)
            {
                PanelHeight = _previousHeight > 100 ? _previousHeight : 260.0;
                IsMaximized = false;
            }
            else
            {
                _previousHeight = PanelHeight > 100 ? PanelHeight : 260.0;
                PanelHeight = 650.0;
                IsMaximized = true;
            }
        }

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

        [ObservableProperty]
        private ObservableCollection<TerminalProfile> _availableProfiles = new();

        [ObservableProperty]
        private TerminalProfile? _selectedProfile;

        [ObservableProperty]
        private ObservableCollection<TerminalTabItem> _terminalTabs = new();

        [ObservableProperty]
        private TerminalTabItem? _activeTerminalTab;

        private string? _workingDirectory;

        public BottomPanelViewModel()
        {
            _workingDirectory = Directory.GetCurrentDirectory();

            // Discover host terminal profiles
            try
            {
                var profiles = TerminalProfileDiscoveryService.Instance.DiscoverProfiles();
                foreach (var p in profiles)
                {
                    AvailableProfiles.Add(p);
                }
                SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.IsDefault) ?? AvailableProfiles.FirstOrDefault();
            }
            catch (Exception ex)
            {
                BuildOutput += $"[Profile Discovery Warning]: {ex.Message}\n";
            }

            // Create initial terminal tab with default profile
            CreateTerminalTab(SelectedProfile);
        }

        public void SetWorkingDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            _workingDirectory = path;

            // If we have active terminal tabs, update or offer to restart
            if (ActiveTerminalTab != null && ActiveTerminalTab.Session.IsAlive)
            {
                // Working directory set for subsequent new terminals
            }
            else
            {
                RestartCurrentTerminalTab();
            }
        }

        [RelayCommand]
        public void CreateTerminalTab(TerminalProfile? profile = null)
        {
            var targetProfile = profile ?? SelectedProfile ?? AvailableProfiles.FirstOrDefault();
            if (targetProfile == null)
            {
                targetProfile = new TerminalProfile
                {
                    Id = "default",
                    Name = "Terminal",
                    ExecutablePath = "powershell.exe",
                    IsDefault = true
                };
            }

            try
            {
                string workDir = _workingDirectory ?? Directory.GetCurrentDirectory();
                var session = new NativeTerminal(120, 30, workDir, targetProfile.ExecutablePath, targetProfile.Arguments);
                var tab = new TerminalTabItem(targetProfile, session);

                TerminalTabs.Add(tab);
                UpdateTabTitles();
                SelectTerminalTab(tab);
            }
            catch (Exception ex)
            {
                BuildOutput += $"[Terminal Launch Error]: Failed to start '{targetProfile.Name}': {ex.Message}\n";
            }
        }

        [RelayCommand]
        public void LaunchProfile(TerminalProfile profile)
        {
            if (profile == null) return;
            SelectedProfile = profile;
            CreateTerminalTab(profile);
        }

        [RelayCommand]
        public void SwitchActiveTerminalProfile(TerminalProfile profile)
        {
            if (profile == null) return;
            SelectedProfile = profile;

            if (ActiveTerminalTab != null)
            {
                int index = TerminalTabs.IndexOf(ActiveTerminalTab);
                var oldTab = ActiveTerminalTab;
                try
                {
                    string workDir = _workingDirectory ?? Directory.GetCurrentDirectory();
                    var session = new NativeTerminal(120, 30, workDir, profile.ExecutablePath, profile.Arguments);
                    var newTab = new TerminalTabItem(profile, session);

                    if (index >= 0 && index < TerminalTabs.Count)
                    {
                        TerminalTabs[index] = newTab;
                    }
                    else
                    {
                        TerminalTabs.Add(newTab);
                    }
                    oldTab.Dispose();
                    UpdateTabTitles();
                    SelectTerminalTab(newTab);
                }
                catch (Exception ex)
                {
                    BuildOutput += $"[Terminal Switch Error]: Failed to switch to '{profile.Name}': {ex.Message}\n";
                }
            }
            else
            {
                CreateTerminalTab(profile);
            }
        }

        private void UpdateTabTitles()
        {
            for (int i = 0; i < TerminalTabs.Count; i++)
            {
                TerminalTabs[i].Title = $"{i + 1}: {TerminalTabs[i].Profile.Name}";
            }
        }

        [RelayCommand]
        public void SelectTerminalTab(TerminalTabItem tab)
        {
            if (tab == null) return;

            foreach (var t in TerminalTabs)
            {
                t.IsActive = (t == tab);
            }

            ActiveTerminalTab = tab;
            TerminalSession = tab.Session;
            SelectedProfile = tab.Profile;
        }

        [RelayCommand]
        public void CloseTerminalTab(TerminalTabItem tab)
        {
            if (tab == null) return;

            int index = TerminalTabs.IndexOf(tab);
            TerminalTabs.Remove(tab);
            tab.Dispose();
            UpdateTabTitles();

            if (TerminalTabs.Count > 0)
            {
                int nextIndex = Math.Clamp(index, 0, TerminalTabs.Count - 1);
                SelectTerminalTab(TerminalTabs[nextIndex]);
            }
            else
            {
                // Always keep at least one active terminal
                CreateTerminalTab(SelectedProfile);
            }
        }

        [RelayCommand]
        public void RestartCurrentTerminalTab()
        {
            var curTab = ActiveTerminalTab;
            var prof = curTab?.Profile ?? SelectedProfile;

            if (curTab != null)
            {
                CloseTerminalTab(curTab);
            }
            CreateTerminalTab(prof);
        }

        [RelayCommand]
        public void ClearTerminal()
        {
            TerminalSession?.Write("\x0c");
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
            string workDir = _workingDirectory ?? Directory.GetCurrentDirectory();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "alacritty",
                    WorkingDirectory = workDir,
                    UseShellExecute = true,
                });
            }
            catch
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedProfile?.ExecutablePath ?? "powershell",
                    WorkingDirectory = workDir,
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
            foreach (var tab in TerminalTabs)
            {
                tab.Dispose();
            }
            TerminalTabs.Clear();
            TerminalSession = null;
        }
    }
}
