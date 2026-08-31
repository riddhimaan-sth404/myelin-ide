using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Myelin.Core.Services
{
    public enum LocalServerType
    {
        None,
        Flask,
        FastAPI,
        Django,
        NodeExpress,
        NodeVite,
        StaticLiveServer,
        Custom
    }

    public class LocalServerRunnerService : IDisposable
    {
        public static readonly LocalServerRunnerService Instance = new();

        private Process? _process;
        private string? _workspaceRoot;

        public event Action<bool, string?, string?>? ServerStatusChanged;
        public event Action<string>? LogReceived;

        public bool IsRunning => _process != null && !_process.HasExited;
        public string? ActiveServerUrl { get; private set; }
        public LocalServerType CurrentServerType { get; private set; } = LocalServerType.None;

        public async Task<(bool success, string? url, string message)> StartLocalServerAsync(string workspaceRoot)
        {
            Stop();

            if (string.IsNullOrEmpty(workspaceRoot) || !Directory.Exists(workspaceRoot))
            {
                return (false, null, "Workspace directory does not exist.");
            }

            _workspaceRoot = workspaceRoot;
            var detection = DetectProject(workspaceRoot);

            if (detection.Type == LocalServerType.StaticLiveServer)
            {
                bool started = await LiveServerService.Instance.StartAsync(workspaceRoot, 5500);
                if (started)
                {
                    CurrentServerType = LocalServerType.StaticLiveServer;
                    ActiveServerUrl = LiveServerService.Instance.ServerUrl;
                    ServerStatusChanged?.Invoke(true, ActiveServerUrl, "Static Live Server (:5500)");
                    return (true, ActiveServerUrl, "Live Server started on port 5500.");
                }
                return (false, null, "Failed to start Live Server.");
            }

            if (string.IsNullOrEmpty(detection.Command))
            {
                return (false, null, "No runnable server configuration detected in workspace.");
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = detection.Command,
                    Arguments = detection.Arguments,
                    WorkingDirectory = workspaceRoot,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                _process = new Process { StartInfo = psi };
                _process.EnableRaisingEvents = true;
                _process.Exited += (s, e) =>
                {
                    ActiveServerUrl = null;
                    CurrentServerType = LocalServerType.None;
                    ServerStatusChanged?.Invoke(false, null, "Server stopped.");
                };

                if (!_process.Start())
                {
                    return (false, null, "Failed to launch server process.");
                }

                CurrentServerType = detection.Type;
                ActiveServerUrl = detection.ExpectedUrl;

                _ = Task.Run(() => ReadOutputLoopAsync(_process.StandardOutput));
                _ = Task.Run(() => ReadOutputLoopAsync(_process.StandardError));

                ServerStatusChanged?.Invoke(true, ActiveServerUrl, $"{detection.Type} ({ActiveServerUrl})");
                return (true, ActiveServerUrl, $"Started {detection.Type} server on {ActiveServerUrl}");
            }
            catch (Exception ex)
            {
                return (false, null, $"Error starting server: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (CurrentServerType == LocalServerType.StaticLiveServer)
            {
                LiveServerService.Instance.Stop();
            }

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch { }
                _process?.Dispose();
                _process = null;
            }

            ActiveServerUrl = null;
            CurrentServerType = LocalServerType.None;
            ServerStatusChanged?.Invoke(false, null, "Server stopped.");
        }

        private (LocalServerType Type, string? Command, string Arguments, string ExpectedUrl) DetectProject(string root)
        {
            // 1. Python Flask (app.py, wsgi.py)
            string appPy = Path.Combine(root, "app.py");
            string wsgiPy = Path.Combine(root, "wsgi.py");
            if (File.Exists(appPy) || File.Exists(wsgiPy))
            {
                string pyExe = FindPythonExecutable(root);
                string targetFile = File.Exists(appPy) ? "app.py" : "wsgi.py";
                return (LocalServerType.Flask, pyExe, targetFile, "http://127.0.0.1:5000/");
            }

            // 2. Python FastAPI (main.py)
            string mainPy = Path.Combine(root, "main.py");
            if (File.Exists(mainPy))
            {
                string text = File.ReadAllText(mainPy);
                if (text.Contains("FastAPI", StringComparison.OrdinalIgnoreCase))
                {
                    string pyExe = FindPythonExecutable(root);
                    return (LocalServerType.FastAPI, pyExe, "-m uvicorn main:app --reload --port 8000", "http://127.0.0.1:8000/");
                }
                string pyExe2 = FindPythonExecutable(root);
                return (LocalServerType.Flask, pyExe2, "main.py", "http://127.0.0.1:5000/");
            }

            // 3. Python Django (manage.py)
            string managePy = Path.Combine(root, "manage.py");
            if (File.Exists(managePy))
            {
                string pyExe = FindPythonExecutable(root);
                return (LocalServerType.Django, pyExe, "manage.py runserver 8000", "http://127.0.0.1:8000/");
            }

            // 4. Node.js (package.json)
            string pkgJson = Path.Combine(root, "package.json");
            if (File.Exists(pkgJson))
            {
                string json = File.ReadAllText(pkgJson);
                string npmExe = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

                if (json.Contains("\"dev\":", StringComparison.OrdinalIgnoreCase))
                {
                    return (LocalServerType.NodeVite, npmExe, "run dev", "http://localhost:5173/");
                }
                if (json.Contains("\"start\":", StringComparison.OrdinalIgnoreCase))
                {
                    return (LocalServerType.NodeExpress, npmExe, "start", "http://localhost:3000/");
                }

                string nodeExe = OperatingSystem.IsWindows() ? "node.exe" : "node";
                string serverJs = Path.Combine(root, "server.js");
                string indexJs = Path.Combine(root, "index.js");
                if (File.Exists(serverJs)) return (LocalServerType.NodeExpress, nodeExe, "server.js", "http://localhost:3000/");
                if (File.Exists(indexJs)) return (LocalServerType.NodeExpress, nodeExe, "index.js", "http://localhost:3000/");
            }

            // 5. Static HTML Project (index.html)
            string indexHtml = Path.Combine(root, "index.html");
            if (File.Exists(indexHtml))
            {
                return (LocalServerType.StaticLiveServer, null, "", "http://127.0.0.1:5500/");
            }

            return (LocalServerType.None, null, "", "");
        }

        private string FindPythonExecutable(string root)
        {
            string[] venvs = { ".venv", "venv", "env" };
            foreach (var v in venvs)
            {
                string py = OperatingSystem.IsWindows()
                    ? Path.Combine(root, v, "Scripts", "python.exe")
                    : Path.Combine(root, v, "bin", "python");
                if (File.Exists(py)) return py;
            }

            return OperatingSystem.IsWindows() ? "python.exe" : "python3";
        }

        private async Task ReadOutputLoopAsync(StreamReader reader)
        {
            var urlRegex = new Regex(@"(https?://[a-zA-Z0-9\.\-]+:\d+/?)", RegexOptions.Compiled);

            while (_process != null && !_process.HasExited)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null) break;

                LogReceived?.Invoke(line);

                var match = urlRegex.Match(line);
                if (match.Success)
                {
                    string detectedUrl = match.Value;
                    if (!detectedUrl.EndsWith("/")) detectedUrl += "/";
                    ActiveServerUrl = detectedUrl;
                    ServerStatusChanged?.Invoke(true, ActiveServerUrl, $"{CurrentServerType} ({ActiveServerUrl})");
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
