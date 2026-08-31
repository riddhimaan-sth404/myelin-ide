using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class LaunchConfigurationService
    {
        private static readonly Lazy<LaunchConfigurationService> _instance = new(() => new LaunchConfigurationService());
        public static LaunchConfigurationService Instance => _instance.Value;

        public event Action? ConfigurationsChanged;

        private readonly List<DebugConfiguration> _configurations = new();
        public IReadOnlyList<DebugConfiguration> Configurations => _configurations;

        public LaunchConfigurationService()
        {
            InitializeDefaultConfigurations();
        }

        public void InitializeDefaultConfigurations()
        {
            _configurations.Clear();
            _configurations.Add(new DebugConfiguration
            {
                Name = "Cargo Build & Debug (Rust)",
                Type = "cargo",
                Program = "${workspaceFolder}/target/debug/${workspaceFolderBasename}.exe",
                PreLaunchTask = "cargo build",
                StopOnEntry = false
            });
            _configurations.Add(new DebugConfiguration
            {
                Name = "Cargo Test (Debug)",
                Type = "cargo",
                Program = "cargo test",
                StopOnEntry = false
            });
            _configurations.Add(new DebugConfiguration
            {
                Name = "LLDB: Launch Executable",
                Type = "rust-lldb",
                Program = "${workspaceFolder}/target/debug/app.exe",
                StopOnEntry = true
            });
            _configurations.Add(new DebugConfiguration
            {
                Name = ".NET Core Launch (C#)",
                Type = "coreclr",
                Program = "${workspaceFolder}/bin/Debug/net8.0/${workspaceFolderBasename}.dll",
                StopOnEntry = false
            });
            _configurations.Add(new DebugConfiguration
            {
                Name = "Node.js: Launch Program",
                Type = "node",
                Program = "${workspaceFolder}/index.js",
                StopOnEntry = false
            });
            _configurations.Add(new DebugConfiguration
            {
                Name = "Python: Current File",
                Type = "python",
                Program = "${file}",
                StopOnEntry = false
            });
        }

        public async Task LoadConfigurationsFromWorkspaceAsync(string? workspaceRoot)
        {
            if (string.IsNullOrEmpty(workspaceRoot) || !Directory.Exists(workspaceRoot))
            {
                InitializeDefaultConfigurations();
                ConfigurationsChanged?.Invoke();
                return;
            }

            string myelinLaunch = Path.Combine(workspaceRoot, ".myelin", "launch.json");
            string vscodeLaunch = Path.Combine(workspaceRoot, ".vscode", "launch.json");

            string targetFile = File.Exists(myelinLaunch) ? myelinLaunch : (File.Exists(vscodeLaunch) ? vscodeLaunch : "");

            if (!string.IsNullOrEmpty(targetFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(targetFile);
                    var doc = JsonNode.Parse(json);
                    var configsNode = doc?["configurations"]?.AsArray();
                    if (configsNode != null && configsNode.Count > 0)
                    {
                        _configurations.Clear();
                        foreach (var node in configsNode)
                        {
                            if (node == null) continue;
                            var name = node["name"]?.GetValue<string>() ?? "Unnamed Launch";
                            var type = node["type"]?.GetValue<string>() ?? "cargo";
                            var request = node["request"]?.GetValue<string>() ?? "launch";
                            var program = node["program"]?.GetValue<string>() ?? "";
                            var args = node["args"]?.GetValue<string>() ?? "";
                            var cwd = node["cwd"]?.GetValue<string>() ?? "${workspaceFolder}";
                            var stopOnEntry = node["stopOnEntry"]?.GetValue<bool>() ?? false;
                            var preLaunch = node["preLaunchTask"]?.GetValue<string>();

                            _configurations.Add(new DebugConfiguration
                            {
                                Name = name,
                                Type = type,
                                Request = request,
                                Program = program,
                                Args = args,
                                Cwd = cwd,
                                StopOnEntry = stopOnEntry,
                                PreLaunchTask = preLaunch
                            });
                        }
                        ConfigurationsChanged?.Invoke();
                        return;
                    }
                }
                catch { }
            }

            InitializeDefaultConfigurations();
            ConfigurationsChanged?.Invoke();
        }

        public string ResolveVariables(string input, string? workspaceRoot, string? currentFile = null)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string ws = workspaceRoot ?? Directory.GetCurrentDirectory();
            string wsName = Path.GetFileName(ws.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            string res = input.Replace("${workspaceFolder}", ws)
                             .Replace("${workspaceFolderBasename}", wsName);

            if (!string.IsNullOrEmpty(currentFile))
            {
                res = res.Replace("${file}", currentFile)
                         .Replace("${fileBasename}", Path.GetFileName(currentFile))
                         .Replace("${fileDirname}", Path.GetDirectoryName(currentFile) ?? "");
            }

            return res;
        }

        public async Task CreateDefaultLaunchJsonAsync(string workspaceRoot)
        {
            string dir = Path.Combine(workspaceRoot, ".myelin");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "launch.json");

            var sample = new
            {
                version = "0.2.0",
                configurations = new object[]
                {
                    new
                    {
                        name = "Cargo Debug (lldb)",
                        type = "cargo",
                        request = "launch",
                        program = "${workspaceFolder}/target/debug/${workspaceFolderBasename}.exe",
                        args = "",
                        cwd = "${workspaceFolder}",
                        stopOnEntry = false,
                        preLaunchTask = "cargo build"
                    },
                    new
                    {
                        name = "Cargo Test (Debug)",
                        type = "cargo",
                        request = "launch",
                        program = "cargo test",
                        cwd = "${workspaceFolder}"
                    }
                }
            };

            string json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(file, json);
            await LoadConfigurationsFromWorkspaceAsync(workspaceRoot);
        }
    }
}
