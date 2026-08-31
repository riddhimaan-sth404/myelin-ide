using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class LanguageServerService
    {
        public static readonly LanguageServerService Instance = new();

        private readonly ConcurrentDictionary<string, LanguageServerDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, LspClient> _activeClients = new(StringComparer.OrdinalIgnoreCase);
        private string? _currentWorkspaceRoot;

        public event Action<string, LspDiagnostic[]>? DiagnosticsReceived;
        public event Action<string, string>? ServerStatusChanged;

        public IReadOnlyDictionary<string, LspClient> ActiveClients => _activeClients;

        public LanguageServerService()
        {
            RegisterDefaultDescriptors();
        }

        public void SetWorkspaceRoot(string? workspaceRoot)
        {
            _currentWorkspaceRoot = workspaceRoot;
        }

        private void RegisterDefaultDescriptors()
        {
            // Python (Flask, FastAPI, Django, general Python)
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "python",
                DisplayName = "Python (Pyright / PyLSP / Ruff)",
                FileExtensions = new[] { ".py", ".pyw", ".pyi" },
                ExecutableCandidates = new[]
                {
                    "pyright-langserver",
                    "pylsp",
                    "jedi-language-server",
                    "ruff"
                },
                DefaultArguments = new[] { "--stdio" }
            });

            // JavaScript & TypeScript (Node.js, Express, React, etc.)
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "typescript",
                DisplayName = "TypeScript / JavaScript (Node.js)",
                FileExtensions = new[] { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs" },
                ExecutableCandidates = new[]
                {
                    "typescript-language-server",
                    "vtsls",
                    "deno"
                },
                DefaultArguments = new[] { "--stdio" }
            });

            // HTML
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "html",
                DisplayName = "HTML Language Server",
                FileExtensions = new[] { ".html", ".htm" },
                ExecutableCandidates = new[] { "vscode-html-language-server", "html-languageserver" },
                DefaultArguments = new[] { "--stdio" }
            });

            // CSS
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "css",
                DisplayName = "CSS Language Server",
                FileExtensions = new[] { ".css", ".scss", ".less" },
                ExecutableCandidates = new[] { "vscode-css-language-server", "css-languageserver" },
                DefaultArguments = new[] { "--stdio" }
            });

            // JSON
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "json",
                DisplayName = "JSON Language Server",
                FileExtensions = new[] { ".json", ".jsonc" },
                ExecutableCandidates = new[] { "vscode-json-language-server", "json-languageserver" },
                DefaultArguments = new[] { "--stdio" }
            });

            // Rust
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "rust",
                DisplayName = "rust-analyzer",
                FileExtensions = new[] { ".rs" },
                ExecutableCandidates = new[] { "rust-analyzer" },
                DefaultArguments = Array.Empty<string>()
            });

            // C / C++
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "cpp",
                DisplayName = "clangd",
                FileExtensions = new[] { ".cpp", ".c", ".h", ".hpp", ".cc", ".cxx" },
                ExecutableCandidates = new[] { "clangd", "ccls" },
                DefaultArguments = Array.Empty<string>()
            });

            // Go
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "go",
                DisplayName = "gopls",
                FileExtensions = new[] { ".go" },
                ExecutableCandidates = new[] { "gopls" },
                DefaultArguments = Array.Empty<string>()
            });

            // C#
            RegisterDescriptor(new LanguageServerDescriptor
            {
                LanguageId = "csharp",
                DisplayName = "csharp-ls",
                FileExtensions = new[] { ".cs" },
                ExecutableCandidates = new[] { "csharp-ls", "OmniSharp" },
                DefaultArguments = Array.Empty<string>()
            });
        }

        public void RegisterDescriptor(LanguageServerDescriptor descriptor)
        {
            _descriptors[descriptor.LanguageId] = descriptor;
        }

        public string? GetLanguageIdForFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            foreach (var kvp in _descriptors)
            {
                if (kvp.Value.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        public async Task<LspClient?> EnsureServerForFileAsync(string filePath)
        {
            string? langId = GetLanguageIdForFile(filePath);
            if (string.IsNullOrEmpty(langId)) return null;

            return await EnsureServerStartedAsync(langId, _currentWorkspaceRoot);
        }

        public async Task<LspClient?> EnsureServerStartedAsync(string languageId, string? workspaceRoot)
        {
            if (_activeClients.TryGetValue(languageId, out var existing) && existing.IsRunning)
            {
                return existing;
            }

            if (!_descriptors.TryGetValue(languageId, out var descriptor))
            {
                return null;
            }

            string? exe = FindExecutable(descriptor, workspaceRoot);
            if (string.IsNullOrEmpty(exe))
            {
                ServerStatusChanged?.Invoke(languageId, $"No server executable found for {descriptor.DisplayName}");
                return null;
            }

            var client = new LspClient(languageId);
            client.DiagnosticsReceived += (path, diags) => DiagnosticsReceived?.Invoke(path, diags);
            client.ServerExited += () =>
            {
                _activeClients.TryRemove(languageId, out _);
                ServerStatusChanged?.Invoke(languageId, "Stopped");
            };

            bool started = await client.StartAsync(exe, descriptor.DefaultArguments, workspaceRoot, workspaceRoot);
            if (started)
            {
                _activeClients[languageId] = client;
                ServerStatusChanged?.Invoke(languageId, $"Running ({descriptor.DisplayName})");
                return client;
            }
            else
            {
                client.Dispose();
                ServerStatusChanged?.Invoke(languageId, "Failed to start");
                return null;
            }
        }

        private string? FindExecutable(LanguageServerDescriptor descriptor, string? workspaceRoot)
        {
            // 1. Check workspace local virtualenv / node_modules
            if (!string.IsNullOrEmpty(workspaceRoot) && Directory.Exists(workspaceRoot))
            {
                foreach (var name in descriptor.ExecutableCandidates)
                {
                    // Check node_modules/.bin
                    string nodeBin = Path.Combine(workspaceRoot, "node_modules", ".bin", OperatingSystem.IsWindows() ? $"{name}.cmd" : name);
                    if (File.Exists(nodeBin)) return nodeBin;

                    // Check Python virtualenv (.venv/Scripts or .venv/bin)
                    string[] venvDirs = { ".venv", "venv", "env" };
                    foreach (var venv in venvDirs)
                    {
                        string pyBin = OperatingSystem.IsWindows()
                            ? Path.Combine(workspaceRoot, venv, "Scripts", $"{name}.exe")
                            : Path.Combine(workspaceRoot, venv, "bin", name);

                        if (File.Exists(pyBin)) return pyBin;
                    }
                }
            }

            // 2. Check System PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (var candidate in descriptor.ExecutableCandidates)
            {
                foreach (var dir in paths)
                {
                    try
                    {
                        string fullPath = Path.Combine(dir, candidate);
                        if (File.Exists(fullPath)) return fullPath;

                        if (OperatingSystem.IsWindows())
                        {
                            if (File.Exists($"{fullPath}.exe")) return $"{fullPath}.exe";
                            if (File.Exists($"{fullPath}.cmd")) return $"{fullPath}.cmd";
                            if (File.Exists($"{fullPath}.bat")) return $"{fullPath}.bat";
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        public void StopAll()
        {
            foreach (var client in _activeClients.Values)
            {
                client.Dispose();
            }
            _activeClients.Clear();
        }
    }
}
