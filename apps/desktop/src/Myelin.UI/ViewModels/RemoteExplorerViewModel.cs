using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core.Models;
using Myelin.Core.Services;

namespace Myelin.UI.ViewModels
{
    public partial class RemoteExplorerViewModel : ViewModelBase
    {
        private readonly RemoteConnectionService _service;
        private readonly PortForwardingService _portService;

        [ObservableProperty]
        private ObservableCollection<RemoteTarget> _sshTargets = new();

        [ObservableProperty]
        private ObservableCollection<RemoteTarget> _wslTargets = new();

        [ObservableProperty]
        private ObservableCollection<RemoteTarget> _containerTargets = new();

        [ObservableProperty]
        private ObservableCollection<ForwardedPort> _forwardedPorts = new();

        [ObservableProperty]
        private ObservableCollection<RemoteFileNode> _remoteFiles = new();

        [ObservableProperty]
        private RemoteTarget? _selectedTarget;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _statusMessage = "Disconnected";

        [ObservableProperty]
        private string _activeTargetTitle = "No Remote Session";

        [ObservableProperty]
        private string _newHostInput = "";

        [ObservableProperty]
        private string _newUserHostInput = "";

        [ObservableProperty]
        private int _newPortInput = 22;

        [ObservableProperty]
        private bool _isAddTargetDialogOpen;

        // Port Forwarding Form Inputs
        [ObservableProperty]
        private int _newForwardLocalPort = 3000;

        [ObservableProperty]
        private int _newForwardRemotePort = 3000;

        [ObservableProperty]
        private string _newForwardLabel = "Web App";

        [ObservableProperty]
        private bool _isAddPortDialogOpen;

        public event Action<string, string>? RequestLaunchRemoteTerminal;
        public event Action<string>? RequestOpenFile;

        public RemoteExplorerViewModel()
        {
            _service = RemoteConnectionService.Instance;
            _portService = PortForwardingService.Instance;

            _service.ConnectionStatusChanged += (target, status) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshCollections);
            };

            _service.SessionStateChanged += state =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    IsConnected = state.IsConnected;
                    if (state.IsConnected && state.CurrentTarget != null)
                    {
                        StatusMessage = $"Connected to {state.CurrentTarget.Name}";
                        ActiveTargetTitle = state.CurrentTarget.Type == RemoteTargetType.WSL
                            ? $"WSL: {state.CurrentTarget.DistroName}"
                            : state.CurrentTarget.Type == RemoteTargetType.Container
                                ? $"Container: {state.CurrentTarget.Name}"
                                : $"SSH: {state.CurrentTarget.DisplaySubtitle}";

                        await LoadRemoteFilesAsync(state.ActiveRemoteWorkspace);
                    }
                    else
                    {
                        StatusMessage = "Disconnected";
                        ActiveTargetTitle = "No Remote Session";
                        RemoteFiles.Clear();
                    }
                });
            };

            _portService.PortsChanged += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPorts);
            };

            RefreshCollections();
            RefreshPorts();
        }

        public void RefreshCollections()
        {
            SshTargets.Clear();
            WslTargets.Clear();
            ContainerTargets.Clear();

            foreach (var target in _service.Targets)
            {
                if (target.Type == RemoteTargetType.SSH) SshTargets.Add(target);
                else if (target.Type == RemoteTargetType.WSL) WslTargets.Add(target);
                else ContainerTargets.Add(target);
            }
        }

        public void RefreshPorts()
        {
            ForwardedPorts.Clear();
            foreach (var port in _portService.ForwardedPorts)
            {
                ForwardedPorts.Add(port);
            }
        }

        public async Task LoadRemoteFilesAsync(string path)
        {
            RemoteFiles.Clear();
            var files = await _service.GetRemoteDirectoryAsync(path);
            foreach (var f in files)
            {
                RemoteFiles.Add(f);
            }
        }

        [RelayCommand]
        public async Task Connect(RemoteTarget? target)
        {
            target ??= SelectedTarget;
            if (target == null) return;

            StatusMessage = $"Connecting to {target.Name}...";
            bool success = await _service.ConnectAsync(target);
            if (success)
            {
                string cmd = _service.GetTerminalLaunchCommand(target);
                string title = target.Type == RemoteTargetType.WSL
                    ? $"WSL: {target.DistroName}"
                    : target.Type == RemoteTargetType.Container
                        ? $"Container: {target.Name}"
                        : $"SSH: {target.Name}";
                RequestLaunchRemoteTerminal?.Invoke(cmd, title);
            }
        }

        [RelayCommand]
        public void Disconnect()
        {
            _service.Disconnect();
        }

        [RelayCommand]
        public void OpenAddTargetDialog()
        {
            NewHostInput = "";
            NewUserHostInput = "dev";
            NewPortInput = 22;
            IsAddTargetDialogOpen = true;
        }

        [RelayCommand]
        public void CloseAddTargetDialog()
        {
            IsAddTargetDialogOpen = false;
        }

        [RelayCommand]
        public void SaveNewTarget()
        {
            if (string.IsNullOrWhiteSpace(NewHostInput)) return;

            var newTarget = new RemoteTarget
            {
                Name = NewHostInput.Trim(),
                Host = NewHostInput.Trim(),
                User = NewUserHostInput.Trim(),
                Port = NewPortInput > 0 ? NewPortInput : 22,
                Type = RemoteTargetType.SSH,
                RemotePath = $"~"
            };

            _service.AddTarget(newTarget);
            RefreshCollections();
            IsAddTargetDialogOpen = false;
            _ = Connect(newTarget);
        }

        [RelayCommand]
        public void RemoveTarget(RemoteTarget target)
        {
            _service.RemoveTarget(target);
            RefreshCollections();
        }

        [RelayCommand]
        public void RefreshTargets()
        {
            _service.InitializeDefaultTargets();
            RefreshCollections();
            StatusMessage = "Targets refreshed";
        }

        [RelayCommand]
        public void OpenPortForwardDialog()
        {
            NewForwardLocalPort = 3000;
            NewForwardRemotePort = 3000;
            NewForwardLabel = "Web Service";
            IsAddPortDialogOpen = true;
        }

        [RelayCommand]
        public void ClosePortForwardDialog()
        {
            IsAddPortDialogOpen = false;
        }

        [RelayCommand]
        public void SavePortForward()
        {
            _portService.ForwardPort(NewForwardLocalPort, NewForwardRemotePort, "localhost", NewForwardLabel);
            RefreshPorts();
            IsAddPortDialogOpen = false;
        }

        [RelayCommand]
        public void StopPortForward(ForwardedPort port)
        {
            _portService.StopForwarding(port);
            RefreshPorts();
        }

        [RelayCommand]
        public void OpenPortInBrowser(ForwardedPort port)
        {
            _portService.OpenInBrowser(port);
        }

        [RelayCommand]
        public void OpenRemoteFile(RemoteFileNode node)
        {
            if (!node.IsDirectory && !string.IsNullOrEmpty(node.FullPath))
            {
                RequestOpenFile?.Invoke(node.FullPath);
            }
        }

        [RelayCommand]
        public void OpenSshConfigFile()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string sshConfig = Path.Combine(home, ".ssh", "config");
            if (File.Exists(sshConfig))
            {
                RequestOpenFile?.Invoke(sshConfig);
            }
        }
    }
}
