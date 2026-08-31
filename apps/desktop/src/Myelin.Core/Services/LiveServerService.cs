using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Myelin.Core.Services
{
    public class LiveServerService : IDisposable
    {
        public static readonly LiveServerService Instance = new();

        private HttpListener? _listener;
        private FileSystemWatcher? _watcher;
        private CancellationTokenSource? _cts;
        private string? _rootDirectory;
        private int _port = 5500;
        private readonly ConcurrentBag<WebSocket> _connectedSockets = new();

        public event Action<bool, string>? ServerStateChanged;
        public event Action<string>? FileReloadTriggered;

        public bool IsRunning => _listener != null && _listener.IsListening;
        public string ServerUrl => $"http://127.0.0.1:{_port}/";
        public int ServerPort => _port;

        private const string LiveReloadScript = @"
<script>
(function() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = protocol + '//' + window.location.host + '/live-reload-ws';
    let socket;
    function connect() {
        socket = new WebSocket(wsUrl);
        socket.onmessage = function(event) {
            if (event.data === 'reload') {
                console.log('[Myelin Live Server] Reloading page...');
                window.location.reload();
            }
        };
        socket.onclose = function() {
            setTimeout(connect, 1500);
        };
    }
    connect();
})();
</script>
";

        public async Task<bool> StartAsync(string rootDirectory, int preferredPort = 5500)
        {
            Stop();

            if (!Directory.Exists(rootDirectory)) return false;

            _rootDirectory = rootDirectory;
            _port = preferredPort;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                    _listener.Start();
                    break;
                }
                catch (HttpListenerException)
                {
                    _listener?.Close();
                    _listener = null;
                    _port++;
                }
            }

            if (_listener == null || !_listener.IsListening)
            {
                ServerStateChanged?.Invoke(false, "Failed to bind HTTP port");
                return false;
            }

            _cts = new CancellationTokenSource();

            // Start Request Processing Loop
            _ = Task.Run(() => ListenLoopAsync(_listener, _cts.Token));

            // Start FileSystemWatcher
            StartWatcher(_rootDirectory);

            ServerStateChanged?.Invoke(true, ServerUrl);
            return true;
        }

        public void Stop()
        {
            _cts?.Cancel();

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch { }
                _listener = null;
            }

            foreach (var ws in _connectedSockets)
            {
                try { ws.Dispose(); } catch { }
            }

            ServerStateChanged?.Invoke(false, "Stopped");
        }

        private void StartWatcher(string path)
        {
            try
            {
                _watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
                };

                DateTime lastTrigger = DateTime.MinValue;

                void OnChanged(object sender, FileSystemEventArgs e)
                {
                    if ((DateTime.Now - lastTrigger).TotalMilliseconds < 300) return;
                    lastTrigger = DateTime.Now;

                    FileReloadTriggered?.Invoke(e.FullPath);
                    _ = BroadcastReloadAsync();
                }

                _watcher.Changed += OnChanged;
                _watcher.Created += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Renamed += (s, e) => OnChanged(s, e);
                _watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        private async Task BroadcastReloadAsync()
        {
            byte[] msg = Encoding.UTF8.GetBytes("reload");
            var segment = new ArraySegment<byte>(msg);

            foreach (var ws in _connectedSockets)
            {
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { }
                }
            }
        }

        private async Task ListenLoopAsync(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && listener.IsListening)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(ctx));
                }
                catch
                {
                    if (ct.IsCancellationRequested) break;
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                // WebSocket Live-Reload Endpoint
                if (ctx.Request.IsWebSocketRequest && ctx.Request.Url?.AbsolutePath == "/live-reload-ws")
                {
                    var wsCtx = await ctx.AcceptWebSocketAsync(null);
                    _connectedSockets.Add(wsCtx.WebSocket);
                    return;
                }

                if (_rootDirectory == null)
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                string relativePath = ctx.Request.Url?.AbsolutePath.TrimStart('/') ?? "";
                if (string.IsNullOrEmpty(relativePath))
                {
                    relativePath = "index.html";
                }

                string localPath = Path.Combine(_rootDirectory, relativePath);

                if (Directory.Exists(localPath))
                {
                    localPath = Path.Combine(localPath, "index.html");
                }

                if (!File.Exists(localPath))
                {
                    ctx.Response.StatusCode = 404;
                    byte[] notFound = Encoding.UTF8.GetBytes("<h1>404 Not Found</h1><p>Myelin Live Server</p>");
                    ctx.Response.ContentType = "text/html";
                    ctx.Response.OutputStream.Write(notFound, 0, notFound.Length);
                    ctx.Response.Close();
                    return;
                }

                string ext = Path.GetExtension(localPath).ToLowerInvariant();
                string contentType = GetMimeType(ext);

                ctx.Response.ContentType = contentType;
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");

                if (ext == ".html" || ext == ".htm")
                {
                    string html = await File.ReadAllTextAsync(localPath);
                    if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                    {
                        html = html.Replace("</body>", $"{LiveReloadScript}</body>", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        html += LiveReloadScript;
                    }

                    byte[] data = Encoding.UTF8.GetBytes(html);
                    ctx.Response.ContentLength64 = data.Length;
                    await ctx.Response.OutputStream.WriteAsync(data);
                }
                else
                {
                    using var fs = File.OpenRead(localPath);
                    ctx.Response.ContentLength64 = fs.Length;
                    await fs.CopyToAsync(ctx.Response.OutputStream);
                }

                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Close(); } catch { }
            }
        }

        private string GetMimeType(string extension) => extension switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream"
        };

        public void Dispose()
        {
            Stop();
        }
    }
}
