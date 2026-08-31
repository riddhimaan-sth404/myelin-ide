using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Myelin.Core.Commands;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class NodeExtensionHostService : IDisposable
    {
        private static NodeExtensionHostService? _instance;
        public static NodeExtensionHostService Instance => _instance ??= new NodeExtensionHostService();

        private Process? _hostProcess;
        private StreamWriter? _hostStdin;
        private int _rpcCounter = 1;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();

        public bool IsRunning => _hostProcess != null && !_hostProcess.HasExited;

        public event Action<string, string>? MessageReceived;
        public event Action<string, string, string>? WebviewPanelCreated; // panelId, viewType, title
        public event Action<string>? WebviewPanelDisposed; // panelId
        public event Action<string, JsonElement>? WebviewMessageReceived; // panelId, message
        public event Action<string, string>? OutputAppended; // channelName, text
        public event Action<string>? StatusBarUpdated; // text

        public static string? FindNodeExecutable()
        {
            if (OperatingSystem.IsWindows())
            {
                string[] candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "npm", "node.exe")
                };

                foreach (var path in candidates)
                {
                    if (File.Exists(path)) return path;
                }
            }

            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string nodeName = OperatingSystem.IsWindows() ? "node.exe" : "node";
                foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), nodeName);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }

            return "node";
        }

        public static string FindBootstrapScript()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(baseDir, "Assets", "extension-host-bootstrap.js");
            if (File.Exists(candidate)) return candidate;

            // Fallback for development
            string devCandidate = Path.Combine(baseDir, "..", "..", "..", "..", "Myelin.Core", "Assets", "extension-host-bootstrap.js");
            if (File.Exists(devCandidate)) return Path.GetFullPath(devCandidate);

            return candidate;
        }

        public async Task<bool> StartAsync(string? workspaceRoot = null)
        {
            if (IsRunning) return true;

            string? nodeExe = FindNodeExecutable();
            string bootstrapJs = FindBootstrapScript();

            if (!File.Exists(bootstrapJs))
            {
                System.Diagnostics.Debug.WriteLine($"[NodeExtensionHost] Bootstrap script not found at {bootstrapJs}");
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = nodeExe,
                    Arguments = $"\"{bootstrapJs}\"",
                    WorkingDirectory = workspaceRoot ?? Directory.GetCurrentDirectory(),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _hostProcess = new Process { StartInfo = psi };
                _hostProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        System.Diagnostics.Debug.WriteLine($"[NodeExtensionHost stderr]: {e.Data}");
                    }
                };

                _hostProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        ProcessIncomingRpc(e.Data);
                    }
                };

                if (!_hostProcess.Start())
                {
                    return false;
                }

                _hostStdin = _hostProcess.StandardInput;
                _hostProcess.BeginOutputReadLine();
                _hostProcess.BeginErrorReadLine();

                // Send init handshake
                await SendRequestAsync("init", new { workspaceRoot }).ConfigureAwait(false);

                // Auto-activate installed enabled extensions with entrypoints
                foreach (var ext in ExtensionManagerService.Instance.InstalledExtensions)
                {
                    if (ext.IsEnabled && ext.HasEntrypoint)
                    {
                        _ = ActivateExtensionAsync(ext);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NodeExtensionHost] Failed to start: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ActivateExtensionAsync(InstalledExtension ext)
        {
            if (!IsRunning)
            {
                await StartAsync().ConfigureAwait(false);
            }

            if (!IsRunning || !ext.HasEntrypoint) return false;

            try
            {
                var res = await SendRequestAsync("activateExtension", new
                {
                    extensionId = ext.Id,
                    entrypointPath = ext.EntrypointJsPath,
                    extensionPath = ext.InstallDirectory
                }).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NodeExtensionHost] Failed to activate {ext.Id}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeactivateExtensionAsync(string extensionId)
        {
            if (!IsRunning) return true;

            try
            {
                await SendRequestAsync("deactivateExtension", new { extensionId }).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void ExecuteCommand(string command, params object[] args)
        {
            _ = ExecuteCommandAsync(command, args);
        }

        public async Task<JsonElement?> ExecuteCommandAsync(string command, params object[] args)
        {
            if (!IsRunning)
            {
                await StartAsync().ConfigureAwait(false);
            }

            if (!IsRunning) return null;

            try
            {
                return await SendRequestAsync("executeCommand", new { command, args }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NodeExtensionHost] ExecuteCommand error ({command}): {ex.Message}");
                return null;
            }
        }

        public async Task SendWebviewMessageAsync(string panelId, object message)
        {
            if (!IsRunning) return;
            try
            {
                await SendRequestAsync("webview.onMessage", new { panelId, message }).ConfigureAwait(false);
            }
            catch { }
        }

        private Task<JsonElement> SendRequestAsync(string method, object? @params)
        {
            int id = Interlocked.Increment(ref _rpcCounter);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[id] = tcs;

            var payload = new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params
            };

            string json = JsonSerializer.Serialize(payload);
            SendRaw(json);

            return tcs.Task;
        }

        private void SendRaw(string line)
        {
            if (_hostStdin != null && IsRunning)
            {
                lock (_hostStdin)
                {
                    try
                    {
                        _hostStdin.WriteLine(line);
                        _hostStdin.Flush();
                    }
                    catch { }
                }
            }
        }

        private void ProcessIncomingRpc(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // Handle RPC Response
                if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                {
                    int id = idProp.GetInt32();
                    if (_pendingRequests.TryRemove(id, out var tcs))
                    {
                        if (root.TryGetProperty("result", out var resultProp))
                        {
                            tcs.TrySetResult(resultProp.Clone());
                        }
                        else if (root.TryGetProperty("error", out var errProp))
                        {
                            string errMsg = errProp.TryGetProperty("message", out var m) ? m.GetString() ?? "RPC error" : "RPC error";
                            tcs.TrySetException(new Exception(errMsg));
                        }
                    }
                    return;
                }

                // Handle RPC Notification
                if (root.TryGetProperty("method", out var methodProp))
                {
                    string method = methodProp.GetString() ?? string.Empty;
                    root.TryGetProperty("params", out var paramsProp);

                    DispatchNotification(method, paramsProp);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NodeExtensionHost] ProcessIncomingRpc error: {ex.Message}");
            }
        }

        private void DispatchNotification(string method, JsonElement @params)
        {
            switch (method)
            {
                case "window.showInformationMessage":
                case "window.showWarningMessage":
                case "window.showErrorMessage":
                    if (@params.TryGetProperty("message", out var msgProp))
                    {
                        MessageReceived?.Invoke(method, msgProp.GetString() ?? string.Empty);
                    }
                    break;

                case "window.createWebviewPanel":
                    if (@params.TryGetProperty("panelId", out var pid) &&
                        @params.TryGetProperty("viewType", out var vt) &&
                        @params.TryGetProperty("title", out var title))
                    {
                        WebviewPanelCreated?.Invoke(pid.GetString()!, vt.GetString()!, title.GetString()!);
                    }
                    break;

                case "window.disposeWebviewPanel":
                    if (@params.TryGetProperty("panelId", out var dpid))
                    {
                        WebviewPanelDisposed?.Invoke(dpid.GetString()!);
                    }
                    break;

                case "webview.postMessage":
                    if (@params.TryGetProperty("panelId", out var mpid) && @params.TryGetProperty("message", out var msg))
                    {
                        WebviewMessageReceived?.Invoke(mpid.GetString()!, msg.Clone());
                    }
                    break;

                case "window.appendOutput":
                    if (@params.TryGetProperty("name", out var ch) && @params.TryGetProperty("text", out var txt))
                    {
                        OutputAppended?.Invoke(ch.GetString()!, txt.GetString()!);
                    }
                    break;

                case "window.setStatusBar":
                    if (@params.TryGetProperty("text", out var sbTxt))
                    {
                        StatusBarUpdated?.Invoke(sbTxt.GetString() ?? string.Empty);
                    }
                    break;

                case "commands.registerCommand":
                    if (@params.TryGetProperty("command", out var cmdId))
                    {
                        string cId = cmdId.GetString()!;
                        CommandRegistry.Instance.Register(cId, "Extension", cId, "", () =>
                        {
                            ExecuteCommand(cId);
                        });
                    }
                    break;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_hostProcess != null && !_hostProcess.HasExited)
                {
                    _hostProcess.Kill(true);
                }
                _hostProcess?.Dispose();
                _hostProcess = null;
            }
            catch { }
        }
    }
}
