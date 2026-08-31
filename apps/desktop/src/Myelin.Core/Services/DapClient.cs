using System;
using System.Collections.Generic;
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
    public class DapStoppedEventArgs : EventArgs
    {
        public string Reason { get; set; } = "breakpoint";
        public int ThreadId { get; set; } = 1;
        public string Description { get; set; } = "";
        public bool AllThreadsStopped { get; set; } = true;
    }

    public class DapOutputEventArgs : EventArgs
    {
        public string Category { get; set; } = "stdout";
        public string Output { get; set; } = "";
    }

    public class DapClient : IDisposable
    {
        private Process? _adapterProcess;
        private int _seq = 1;
        private readonly Dictionary<int, TaskCompletionSource<JsonNode>> _pendingRequests = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public event EventHandler<DapStoppedEventArgs>? Stopped;
        public event EventHandler<int>? Continued;
        public event EventHandler<DapOutputEventArgs>? OutputReceived;
        public event EventHandler? Terminated;
        public event EventHandler<int>? Exited;

        public bool IsRunning => _adapterProcess != null && !_adapterProcess.HasExited;

        public async Task<bool> StartAdapterAsync(string executable, string arguments, string? workingDir = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _adapterProcess = new Process { StartInfo = psi };
                _adapterProcess.EnableRaisingEvents = true;

                _adapterProcess.Exited += (s, e) =>
                {
                    Exited?.Invoke(this, _adapterProcess?.ExitCode ?? 0);
                    Terminated?.Invoke(this, EventArgs.Empty);
                };

                if (!_adapterProcess.Start()) return false;

                _ = Task.Run(ReadAdapterOutputLoopAsync);
                _ = Task.Run(ReadAdapterErrorLoopAsync);

                return true;
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke(this, new DapOutputEventArgs
                {
                    Category = "stderr",
                    Output = $"Failed to start DAP adapter '{executable}': {ex.Message}\n"
                });
                return false;
            }
        }

        public async Task<JsonNode?> SendRequestAsync(string command, object? args = null)
        {
            if (_adapterProcess == null || _adapterProcess.HasExited) return null;

            int seq = Interlocked.Increment(ref _seq);
            var tcs = new TaskCompletionSource<JsonNode>();
            lock (_pendingRequests)
            {
                _pendingRequests[seq] = tcs;
            }

            var reqObj = new Dictionary<string, object>
            {
                ["seq"] = seq,
                ["type"] = "request",
                ["command"] = command
            };
            if (args != null) reqObj["arguments"] = args;

            string json = JsonSerializer.Serialize(reqObj);
            string message = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

            await _sendLock.WaitAsync();
            try
            {
                await _adapterProcess.StandardInput.WriteAsync(message);
                await _adapterProcess.StandardInput.FlushAsync();
            }
            finally
            {
                _sendLock.Release();
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() => tcs.TrySetCanceled());

            try
            {
                return await tcs.Task;
            }
            catch
            {
                return null;
            }
        }

        private async Task ReadAdapterOutputLoopAsync()
        {
            if (_adapterProcess == null) return;
            var stream = _adapterProcess.StandardOutput.BaseStream;

            try
            {
                while (!_adapterProcess.HasExited)
                {
                    int contentLength = -1;
                    while (true)
                    {
                        string? line = await ReadHeaderLineAsync(stream);
                        if (line == null) return;
                        if (string.IsNullOrEmpty(line)) break; // End of headers

                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = line.Substring("Content-Length:".Length).Trim();
                            int.TryParse(val, out contentLength);
                        }
                    }

                    if (contentLength > 0)
                    {
                        byte[] buffer = new byte[contentLength];
                        int totalRead = 0;
                        while (totalRead < contentLength)
                        {
                            int read = await stream.ReadAsync(buffer, totalRead, contentLength - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        string json = Encoding.UTF8.GetString(buffer, 0, totalRead);
                        HandleDapMessage(json);
                    }
                }
            }
            catch { }
        }

        private async Task ReadAdapterErrorLoopAsync()
        {
            if (_adapterProcess == null) return;
            try
            {
                while (!_adapterProcess.HasExited)
                {
                    string? line = await _adapterProcess.StandardError.ReadLineAsync();
                    if (line == null) break;
                    OutputReceived?.Invoke(this, new DapOutputEventArgs { Category = "stderr", Output = line + "\n" });
                }
            }
            catch { }
        }

        private static async Task<string?> ReadHeaderLineAsync(Stream stream)
        {
            var bytes = new List<byte>();
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) return null;
                if (b == '\n')
                {
                    string res = Encoding.UTF8.GetString(bytes.ToArray());
                    return res.TrimEnd('\r');
                }
                bytes.Add((byte)b);
            }
        }

        private void HandleDapMessage(string json)
        {
            try
            {
                var doc = JsonNode.Parse(json);
                if (doc == null) return;

                string type = doc["type"]?.GetValue<string>() ?? "";

                if (type == "response")
                {
                    int reqSeq = doc["request_seq"]?.GetValue<int>() ?? 0;
                    lock (_pendingRequests)
                    {
                        if (_pendingRequests.TryGetValue(reqSeq, out var tcs))
                        {
                            _pendingRequests.Remove(reqSeq);
                            tcs.TrySetResult(doc);
                        }
                    }
                }
                else if (type == "event")
                {
                    string evt = doc["event"]?.GetValue<string>() ?? "";
                    var body = doc["body"];

                    switch (evt)
                    {
                        case "stopped":
                            Stopped?.Invoke(this, new DapStoppedEventArgs
                            {
                                Reason = body?["reason"]?.GetValue<string>() ?? "breakpoint",
                                ThreadId = body?["threadId"]?.GetValue<int>() ?? 1,
                                Description = body?["description"]?.GetValue<string>() ?? ""
                            });
                            break;

                        case "continued":
                            Continued?.Invoke(this, body?["threadId"]?.GetValue<int>() ?? 1);
                            break;

                        case "output":
                            OutputReceived?.Invoke(this, new DapOutputEventArgs
                            {
                                Category = body?["category"]?.GetValue<string>() ?? "stdout",
                                Output = body?["output"]?.GetValue<string>() ?? ""
                            });
                            break;

                        case "terminated":
                            Terminated?.Invoke(this, EventArgs.Empty);
                            break;

                        case "exited":
                            Exited?.Invoke(this, body?["exitCode"]?.GetValue<int>() ?? 0);
                            break;
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (_adapterProcess != null && !_adapterProcess.HasExited)
                {
                    _adapterProcess.Kill();
                    _adapterProcess.Dispose();
                }
            }
            catch { }
            _sendLock.Dispose();
        }
    }
}
