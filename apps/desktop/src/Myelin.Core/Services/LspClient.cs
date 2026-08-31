using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class LspClient : IDisposable
    {
        private Process? _process;
        private Stream? _stdin;
        private int _sequenceNumber = 0;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pendingRequests = new();
        private CancellationTokenSource? _cts;
        private bool _isInitialized = false;

        public event Action<string, LspDiagnostic[]>? DiagnosticsReceived;
        public event Action<string>? LogMessageReceived;
        public event Action? ServerExited;

        public bool IsRunning => _process != null && !_process.HasExited;
        public string LanguageId { get; }

        public LspClient(string languageId)
        {
            LanguageId = languageId;
        }

        public async Task<bool> StartAsync(string executable, string[] arguments, string? workingDirectory, string? workspaceRoot)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = string.Join(" ", arguments),
                    WorkingDirectory = workingDirectory ?? workspaceRoot ?? Directory.GetCurrentDirectory(),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                _process = new Process { StartInfo = psi };
                _process.EnableRaisingEvents = true;
                _process.Exited += (s, e) => ServerExited?.Invoke();

                if (!_process.Start())
                {
                    return false;
                }

                _stdin = _process.StandardInput.BaseStream;
                _cts = new CancellationTokenSource();

                _ = Task.Run(() => ReadStdoutLoopAsync(_process.StandardOutput.BaseStream, _cts.Token));
                _ = Task.Run(() => ReadStderrLoopAsync(_process.StandardError, _cts.Token));

                // Initialize LSP Handshake
                var initParams = new JsonObject
                {
                    ["processId"] = Environment.ProcessId,
                    ["rootUri"] = string.IsNullOrEmpty(workspaceRoot) ? null : new Uri(workspaceRoot).AbsoluteUri,
                    ["rootPath"] = workspaceRoot,
                    ["capabilities"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject
                        {
                            ["synchronization"] = new JsonObject
                            {
                                ["dynamicRegistration"] = true,
                                ["willSave"] = false,
                                ["willSaveWaitUntil"] = false,
                                ["didSave"] = true
                            },
                            ["completion"] = new JsonObject
                            {
                                ["dynamicRegistration"] = true,
                                ["completionItem"] = new JsonObject
                                {
                                    ["snippetSupport"] = true,
                                    ["documentationFormat"] = new JsonArray { "markdown", "plaintext" }
                                }
                            },
                            ["hover"] = new JsonObject
                            {
                                ["contentFormat"] = new JsonArray { "markdown", "plaintext" }
                            },
                            ["publishDiagnostics"] = new JsonObject
                            {
                                ["relatedInformation"] = true,
                                ["versionSupport"] = true
                            }
                        },
                        ["workspace"] = new JsonObject
                        {
                            ["applyEdit"] = true,
                            ["workspaceFolders"] = true
                        }
                    }
                };

                var initResponse = await SendRequestAsync("initialize", initParams);
                if (initResponse != null)
                {
                    await SendNotificationAsync("initialized", new JsonObject());
                    _isInitialized = true;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke($"[LSP-{LanguageId}] Failed to start {executable}: {ex.Message}");
                return false;
            }
        }

        public async Task DidOpenAsync(string filePath, string languageId, int version, string content)
        {
            var uri = new Uri(filePath).AbsoluteUri;
            var docParams = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri,
                    ["languageId"] = languageId,
                    ["version"] = version,
                    ["text"] = content
                }
            };
            await SendNotificationAsync("textDocument/didOpen", docParams);
        }

        public async Task DidChangeAsync(string filePath, int version, string fullContent)
        {
            var uri = new Uri(filePath).AbsoluteUri;
            var docParams = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri,
                    ["version"] = version
                },
                ["contentChanges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["text"] = fullContent
                    }
                }
            };
            await SendNotificationAsync("textDocument/didChange", docParams);
        }

        public async Task DidSaveAsync(string filePath, string? text = null)
        {
            var uri = new Uri(filePath).AbsoluteUri;
            var docParams = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                }
            };
            if (text != null)
            {
                docParams["text"] = text;
            }
            await SendNotificationAsync("textDocument/didSave", docParams);
        }

        public async Task DidCloseAsync(string filePath)
        {
            var uri = new Uri(filePath).AbsoluteUri;
            var docParams = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri
                }
            };
            await SendNotificationAsync("textDocument/didClose", docParams);
        }

        public async Task<LspCompletionItem[]> RequestCompletionsAsync(string filePath, int line, int character)
        {
            var uri = new Uri(filePath).AbsoluteUri;
            var req = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character }
            };

            var res = await SendRequestAsync("textDocument/completion", req);
            if (res == null) return Array.Empty<LspCompletionItem>();

            var items = new List<LspCompletionItem>();
            try
            {
                JsonArray? array = null;
                if (res is JsonArray arr) array = arr;
                else if (res is JsonObject obj && obj["items"] is JsonArray innerArr) array = innerArr;

                if (array != null)
                {
                    foreach (var elem in array)
                    {
                        if (elem == null) continue;
                        var item = JsonSerializer.Deserialize<LspCompletionItem>(elem.ToJsonString());
                        if (item != null) items.Add(item);
                    }
                }
            }
            catch { }

            return items.ToArray();
        }

        public async Task<LspHover?> RequestHoverAsync(string filePath, int line, int character)
        {
            var uri = new Uri(filePath).AbsoluteUri;
            var req = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character }
            };

            var res = await SendRequestAsync("textDocument/hover", req);
            if (res == null) return null;

            try
            {
                return JsonSerializer.Deserialize<LspHover>(res.ToJsonString());
            }
            catch
            {
                return null;
            }
        }

        public async Task<LspLocation[]> RequestDefinitionAsync(string filePath, int line, int character)
        {
            var uri = new Uri(filePath).AbsoluteUri;
            var req = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character }
            };

            var res = await SendRequestAsync("textDocument/definition", req);
            if (res == null) return Array.Empty<LspLocation>();

            var locs = new List<LspLocation>();
            try
            {
                if (res is JsonArray arr)
                {
                    foreach (var elem in arr)
                    {
                        if (elem == null) continue;
                        var loc = JsonSerializer.Deserialize<LspLocation>(elem.ToJsonString());
                        if (loc != null) locs.Add(loc);
                    }
                }
                else if (res is JsonObject obj)
                {
                    var loc = JsonSerializer.Deserialize<LspLocation>(obj.ToJsonString());
                    if (loc != null) locs.Add(loc);
                }
            }
            catch { }

            return locs.ToArray();
        }

        public async Task<JsonNode?> SendRequestAsync(string method, JsonObject? parameters)
        {
            int id = Interlocked.Increment(ref _sequenceNumber);
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[id] = tcs;

            var msg = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters
            };

            await SendRawAsync(msg);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using (timeoutCts.Token.Register(() => tcs.TrySetResult(null)))
            {
                return await tcs.Task;
            }
        }

        public async Task SendNotificationAsync(string method, JsonObject? parameters)
        {
            var msg = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            };

            await SendRawAsync(msg);
        }

        private async Task SendRawAsync(JsonObject message)
        {
            if (_stdin == null) return;

            string json = message.ToJsonString();
            byte[] body = Encoding.UTF8.GetBytes(json);
            string header = $"Content-Length: {body.Length}\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);

            lock (_stdin)
            {
                _stdin.Write(headerBytes, 0, headerBytes.Length);
                _stdin.Write(body, 0, body.Length);
                _stdin.Flush();
            }
        }

        private async Task ReadStdoutLoopAsync(Stream stream, CancellationToken ct)
        {
            var reader = new BinaryReader(stream, Encoding.UTF8);

            while (!ct.IsCancellationRequested && IsRunning)
            {
                try
                {
                    int contentLength = -1;
                    while (true)
                    {
                        string line = ReadLine(stream);
                        if (line == null) return;
                        if (line == "\r\n" || line == "\n" || line.Length == 0) break;

                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            string lenStr = line.Substring("Content-Length:".Length).Trim();
                            int.TryParse(lenStr, out contentLength);
                        }
                    }

                    if (contentLength <= 0) continue;

                    byte[] buffer = new byte[contentLength];
                    int totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        int read = await stream.ReadAsync(buffer, totalRead, contentLength - totalRead, ct);
                        if (read <= 0) break;
                        totalRead += read;
                    }

                    if (totalRead == contentLength)
                    {
                        string jsonStr = Encoding.UTF8.GetString(buffer);
                        ProcessMessage(jsonStr);
                    }
                }
                catch (Exception)
                {
                    if (ct.IsCancellationRequested) break;
                }
            }
        }

        private string ReadLine(Stream stream)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) return sb.Length > 0 ? sb.ToString() : null!;
                char c = (char)b;
                sb.Append(c);
                if (c == '\n') return sb.ToString();
            }
        }

        private async Task ReadStderrLoopAsync(StreamReader reader, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsRunning)
            {
                string? line = await reader.ReadLineAsync();
                if (line != null)
                {
                    LogMessageReceived?.Invoke($"[LSP-{LanguageId}-ERR] {line}");
                }
                else break;
            }
        }

        private void ProcessMessage(string json)
        {
            try
            {
                var node = JsonNode.Parse(json);
                if (node == null) return;

                // 1. Response
                if (node["id"] is JsonValue idVal && idVal.TryGetValue<int>(out int id))
                {
                    if (_pendingRequests.TryRemove(id, out var tcs))
                    {
                        var result = node["result"];
                        tcs.TrySetResult(result);
                    }
                    return;
                }

                // 2. Notification
                if (node["method"] is JsonValue methodVal && methodVal.TryGetValue<string>(out string? method))
                {
                    if (method == "textDocument/publishDiagnostics")
                    {
                        var p = node["params"];
                        if (p != null)
                        {
                            string? uri = p["uri"]?.GetValue<string>();
                            if (!string.IsNullOrEmpty(uri))
                            {
                                string filePath = uri;
                                if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) && parsedUri.IsFile)
                                {
                                    filePath = parsedUri.LocalPath;
                                }

                                var diagArray = p["diagnostics"] as JsonArray;
                                var diags = new List<LspDiagnostic>();
                                if (diagArray != null)
                                {
                                    foreach (var d in diagArray)
                                    {
                                        if (d == null) continue;
                                        var diag = JsonSerializer.Deserialize<LspDiagnostic>(d.ToJsonString());
                                        if (diag != null) diags.Add(diag);
                                    }
                                }

                                DiagnosticsReceived?.Invoke(filePath, diags.ToArray());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke($"[LSP-{LanguageId}] Error parsing message: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try
            {
                if (_isInitialized)
                {
                    _ = SendNotificationAsync("exit", new JsonObject());
                }
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                }
                _process?.Dispose();
            }
            catch { }
        }
    }
}
