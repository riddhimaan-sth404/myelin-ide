using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class TerminalProfileDiscoveryService
    {
        private static readonly Lazy<TerminalProfileDiscoveryService> _instance =
            new(() => new TerminalProfileDiscoveryService());

        public static TerminalProfileDiscoveryService Instance => _instance.Value;

        public IReadOnlyList<TerminalProfile> DiscoverProfiles()
        {
            var profiles = new List<TerminalProfile>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                DiscoverWindowsProfiles(profiles);
            }
            else
            {
                DiscoverUnixProfiles(profiles);
            }

            // Ensure at least one default profile is set
            if (profiles.Count > 0 && !profiles.Exists(p => p.IsDefault))
            {
                profiles[0].IsDefault = true;
            }

            // Fallback if none found
            if (profiles.Count == 0)
            {
                profiles.Add(new TerminalProfile
                {
                    Id = "default",
                    Name = "Terminal",
                    ExecutablePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : "/bin/sh",
                    Icon = "IconTerminal",
                    IsDefault = true
                });
            }

            return profiles;
        }

        private void DiscoverWindowsProfiles(List<TerminalProfile> profiles)
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string system32 = Path.Combine(systemRoot, "System32");

            // 1. PowerShell 7 (Core)
            string pwshPath7 = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            string pwshPath7X86 = Path.Combine(programFilesX86, "PowerShell", "7", "pwsh.exe");
            string pwshLocal = Path.Combine(localAppData, "Microsoft", "PowerShell", "7", "pwsh.exe");
            string? foundPwsh = FirstExisting(pwshPath7, pwshPath7X86, pwshLocal, FindInPath("pwsh.exe"));

            if (foundPwsh != null)
            {
                profiles.Add(new TerminalProfile
                {
                    Id = "pwsh",
                    Name = "PowerShell 7",
                    ExecutablePath = foundPwsh,
                    Icon = "IconTerminal",
                    IsDefault = true
                });
            }

            // 2. Windows PowerShell (5.1)
            string winPowershell = Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe");
            string? foundWinPowershell = FirstExisting(winPowershell, FindInPath("powershell.exe"));

            if (foundWinPowershell != null)
            {
                profiles.Add(new TerminalProfile
                {
                    Id = "powershell",
                    Name = "Windows PowerShell",
                    ExecutablePath = foundWinPowershell,
                    Icon = "IconTerminal",
                    IsDefault = foundPwsh == null
                });
            }

            // 3. Command Prompt
            string cmdPath = Path.Combine(system32, "cmd.exe");
            string? foundCmd = FirstExisting(cmdPath, FindInPath("cmd.exe"));

            if (foundCmd != null)
            {
                profiles.Add(new TerminalProfile
                {
                    Id = "cmd",
                    Name = "Command Prompt",
                    ExecutablePath = foundCmd,
                    Icon = "IconTerminal",
                    IsDefault = false
                });
            }

            // 4. Git Bash
            string gitBash1 = Path.Combine(programFiles, "Git", "bin", "bash.exe");
            string gitBash2 = Path.Combine(programFilesX86, "Git", "bin", "bash.exe");
            string gitBash3 = Path.Combine(localAppData, "Programs", "Git", "bin", "bash.exe");
            string? foundGitBash = FirstExisting(gitBash1, gitBash2, gitBash3);

            if (foundGitBash != null)
            {
                profiles.Add(new TerminalProfile
                {
                    Id = "git-bash",
                    Name = "Git Bash",
                    ExecutablePath = foundGitBash,
                    Arguments = "--login -i",
                    Icon = "IconTerminal",
                    IsDefault = false
                });
            }

            // 5. WSL (Windows Subsystem for Linux)
            string wslPath = Path.Combine(system32, "wsl.exe");
            string? foundWsl = FirstExisting(wslPath, FindInPath("wsl.exe"));

            if (foundWsl != null)
            {
                profiles.Add(new TerminalProfile
                {
                    Id = "wsl",
                    Name = "WSL (Linux)",
                    ExecutablePath = foundWsl,
                    Icon = "IconTerminal",
                    IsDefault = false
                });
            }

            // 6. MSYS2 / MinGW
            string msys64 = @"C:\msys64\usr\bin\bash.exe";
            if (File.Exists(msys64))
            {
                profiles.Add(new TerminalProfile
                {
                    Id = "msys2",
                    Name = "MSYS2 Bash",
                    ExecutablePath = msys64,
                    Arguments = "--login -i",
                    Icon = "IconTerminal",
                    IsDefault = false
                });
            }
        }

        private void DiscoverUnixProfiles(List<TerminalProfile> profiles)
        {
            string? defaultShell = Environment.GetEnvironmentVariable("SHELL");

            // Check /etc/shells if available
            var candidateShells = new List<string>();
            if (File.Exists("/etc/shells"))
            {
                try
                {
                    foreach (var line in File.ReadAllLines("/etc/shells"))
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#") && File.Exists(trimmed))
                        {
                            candidateShells.Add(trimmed);
                        }
                    }
                }
                catch { }
            }

            if (candidateShells.Count == 0)
            {
                string[] defaults = { "/bin/zsh", "/bin/bash", "/bin/fish", "/bin/sh", "/usr/bin/zsh", "/usr/bin/bash", "/usr/bin/fish" };
                foreach (var s in defaults)
                {
                    if (File.Exists(s)) candidateShells.Add(s);
                }
            }

            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Put user's current SHELL first if present
            if (!string.IsNullOrEmpty(defaultShell) && File.Exists(defaultShell))
            {
                string name = Path.GetFileName(defaultShell);
                profiles.Add(new TerminalProfile
                {
                    Id = name.ToLowerInvariant(),
                    Name = char.ToUpperInvariant(name[0]) + name.Substring(1) + " (Default)",
                    ExecutablePath = defaultShell,
                    Icon = "IconTerminal",
                    IsDefault = true
                });
                added.Add(defaultShell);
            }

            foreach (var shellPath in candidateShells)
            {
                if (added.Add(shellPath))
                {
                    string name = Path.GetFileName(shellPath);
                    profiles.Add(new TerminalProfile
                    {
                        Id = name.ToLowerInvariant(),
                        Name = char.ToUpperInvariant(name[0]) + name.Substring(1),
                        ExecutablePath = shellPath,
                        Icon = "IconTerminal",
                        IsDefault = profiles.Count == 0
                    });
                }
            }
        }

        private static string? FirstExisting(params string?[] paths)
        {
            foreach (var path in paths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        private static string? FindInPath(string fileName)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            char sep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
            foreach (var dir in pathEnv.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string fullPath = Path.Combine(dir, fileName);
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch { }
            }
            return null;
        }
    }
}
