using System;
using System.Collections.Generic;

namespace Myelin.Core.Models
{
    public enum RemoteTargetType
    {
        SSH,
        WSL,
        Container,
        Tunnel
    }

    public enum RemoteConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    public enum PortForwardStatus
    {
        Inactive,
        Forwarding,
        Error
    }

    public class ForwardedPort
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int LocalPort { get; set; }
        public int RemotePort { get; set; }
        public string RemoteHost { get; set; } = "localhost";
        public string Label { get; set; } = "";
        public PortForwardStatus Status { get; set; } = PortForwardStatus.Forwarding;
        public string StatusMessage { get; set; } = "Active";
        public bool IsAutoDetected { get; set; } = false;
        public string Protocol { get; set; } = "HTTP/TCP";
        public string LocalAddress => $"http://localhost:{LocalPort}";
        public string DisplayText => string.IsNullOrEmpty(Label) ? $"{LocalPort} -> {RemoteHost}:{RemotePort}" : $"{Label} ({LocalPort} -> {RemotePort})";
    }

    public class RemoteFileNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public List<RemoteFileNode> Children { get; set; } = new();
        public bool IsExpanded { get; set; }
        public bool IsLoaded { get; set; }
    }

    public class RemoteTarget
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public RemoteTargetType Type { get; set; } = RemoteTargetType.SSH;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 22;
        public string User { get; set; } = "";
        public string DistroName { get; set; } = "";
        public string KeyPath { get; set; } = "";
        public string RemotePath { get; set; } = "~";
        public string DefaultShell { get; set; } = "/bin/bash";
        public RemoteConnectionStatus Status { get; set; } = RemoteConnectionStatus.Disconnected;
        public string StatusMessage { get; set; } = "";
        public DateTime? LastConnected { get; set; }

        public string DisplaySubtitle => Type switch
        {
            RemoteTargetType.WSL => $"WSL: {DistroName}",
            RemoteTargetType.SSH => !string.IsNullOrEmpty(User) ? $"{User}@{Host}:{Port}" : $"{Host}:{Port}",
            RemoteTargetType.Container => $"Container: {Name}",
            _ => Host
        };

        public string IconKey => Type switch
        {
            RemoteTargetType.WSL => "IconWsl",
            RemoteTargetType.SSH => "IconSsh",
            RemoteTargetType.Container => "IconServer",
            _ => "IconRemote"
        };
    }

    public class RemoteSessionState
    {
        public bool IsConnected { get; set; }
        public RemoteTarget? CurrentTarget { get; set; }
        public DateTime? ConnectedAt { get; set; }
        public string ActiveRemoteWorkspace { get; set; } = "";
        public string RemoteOsInfo { get; set; } = "Linux x86_64";
        public List<ForwardedPort> ActivePorts { get; set; } = new();
    }
}
