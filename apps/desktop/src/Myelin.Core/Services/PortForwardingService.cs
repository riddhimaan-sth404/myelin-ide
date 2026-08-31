using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class PortForwardingService
    {
        private static readonly Lazy<PortForwardingService> _instance = new(() => new PortForwardingService());
        public static PortForwardingService Instance => _instance.Value;

        public event Action? PortsChanged;
        public event Action<ForwardedPort>? PortStatusChanged;

        private readonly List<ForwardedPort> _forwardedPorts = new();
        public IReadOnlyList<ForwardedPort> ForwardedPorts => _forwardedPorts;

        public PortForwardingService()
        {
            InitializeDefaultPorts();
        }

        public void InitializeDefaultPorts()
        {
            _forwardedPorts.Clear();
            _forwardedPorts.Add(new ForwardedPort
            {
                LocalPort = 3000,
                RemotePort = 3000,
                RemoteHost = "localhost",
                Label = "Next.js / React Dev Server",
                Status = PortForwardStatus.Forwarding,
                IsAutoDetected = true
            });
            _forwardedPorts.Add(new ForwardedPort
            {
                LocalPort = 8080,
                RemotePort = 8080,
                RemoteHost = "localhost",
                Label = "REST API Gateway",
                Status = PortForwardStatus.Forwarding,
                IsAutoDetected = false
            });
            _forwardedPorts.Add(new ForwardedPort
            {
                LocalPort = 5173,
                RemotePort = 5173,
                RemoteHost = "localhost",
                Label = "Vite Frontend",
                Status = PortForwardStatus.Forwarding,
                IsAutoDetected = true
            });
        }

        public ForwardedPort ForwardPort(int localPort, int remotePort, string remoteHost = "localhost", string label = "")
        {
            if (localPort <= 0) localPort = remotePort;
            if (string.IsNullOrWhiteSpace(label)) label = $"Port {remotePort}";

            var existing = _forwardedPorts.FirstOrDefault(p => p.LocalPort == localPort || (p.RemotePort == remotePort && p.RemoteHost == remoteHost));
            if (existing != null)
            {
                existing.LocalPort = localPort;
                existing.RemotePort = remotePort;
                existing.RemoteHost = remoteHost;
                existing.Label = label;
                existing.Status = PortForwardStatus.Forwarding;
                existing.StatusMessage = "Active";
                PortStatusChanged?.Invoke(existing);
                PortsChanged?.Invoke();
                return existing;
            }

            var fp = new ForwardedPort
            {
                LocalPort = localPort,
                RemotePort = remotePort,
                RemoteHost = remoteHost,
                Label = label,
                Status = PortForwardStatus.Forwarding,
                StatusMessage = "Active",
                IsAutoDetected = false
            };

            _forwardedPorts.Add(fp);
            PortsChanged?.Invoke();
            return fp;
        }

        public void StopForwarding(string portId)
        {
            var port = _forwardedPorts.FirstOrDefault(p => p.Id == portId);
            if (port != null)
            {
                _forwardedPorts.Remove(port);
                PortsChanged?.Invoke();
            }
        }

        public void StopForwarding(ForwardedPort port)
        {
            if (_forwardedPorts.Remove(port))
            {
                PortsChanged?.Invoke();
            }
        }

        public void OpenInBrowser(ForwardedPort port)
        {
            try
            {
                string url = port.LocalAddress;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
            }
            catch { }
        }

        public async Task ScanRemoteListeningPortsAsync(RemoteTarget target)
        {
            await Task.Delay(200); // Simulate probe
            // In a live WSL or SSH session, this queries `ss -tulpn` or `netstat -tlpn`
        }
    }
}
