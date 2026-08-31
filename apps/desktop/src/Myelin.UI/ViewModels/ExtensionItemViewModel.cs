using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core.Models;
using Myelin.Core.Services;

namespace Myelin.UI.ViewModels
{
    public partial class ExtensionItemViewModel : ViewModelBase
    {
        private readonly ExtensionManagerService _manager = ExtensionManagerService.Instance;

        public OpenVsxExtensionItem? MarketplaceItem { get; }
        public InstalledExtension? InstalledItem { get; private set; }

        public string Id => MarketplaceItem?.Id ?? InstalledItem?.Id ?? string.Empty;
        public string Title => MarketplaceItem?.Title ?? InstalledItem?.DisplayName ?? Id;
        public string Publisher => MarketplaceItem?.Publisher ?? InstalledItem?.Publisher ?? string.Empty;
        public string Version => MarketplaceItem?.Version ?? InstalledItem?.Version ?? "1.0.0";
        public string Description => MarketplaceItem?.Description ?? InstalledItem?.Description ?? string.Empty;
        public string? IconUrl => MarketplaceItem?.IconUrl;
        public string? LocalIconPath => InstalledItem?.IconPath;
        public string FormattedDownloads => MarketplaceItem?.FormattedDownloads ?? string.Empty;
        public string FormattedRating => MarketplaceItem?.FormattedRating ?? string.Empty;

        [ObservableProperty]
        private bool _isInstalled;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private double _installProgress;

        public event Action<ExtensionItemViewModel>? RequestOpenDetails;

        public ExtensionItemViewModel(OpenVsxExtensionItem item)
        {
            MarketplaceItem = item;
            UpdateState();
        }

        public ExtensionItemViewModel(InstalledExtension installed)
        {
            InstalledItem = installed;
            MarketplaceItem = new OpenVsxExtensionItem
            {
                Namespace = installed.Publisher,
                Name = installed.Name,
                DisplayName = installed.DisplayName,
                Version = installed.Version,
                Description = installed.Description
            };
            UpdateState();
        }

        public void UpdateState()
        {
            var installed = _manager.GetInstalled(Id);
            InstalledItem = installed;
            IsInstalled = installed != null;
            IsEnabled = installed?.IsEnabled ?? true;
        }

        [RelayCommand]
        public async Task InstallAsync()
        {
            if (MarketplaceItem == null || IsInstalling) return;

            IsInstalling = true;
            InstallProgress = 0;

            try
            {
                var progress = new Progress<double>(p =>
                {
                    InstallProgress = p * 100;
                });

                var installed = await _manager.InstallFromMarketplaceAsync(MarketplaceItem, progress);
                if (installed != null)
                {
                    InstalledItem = installed;
                    IsInstalled = true;
                    IsEnabled = true;

                    if (installed.HasEntrypoint)
                    {
                        _ = NodeExtensionHostService.Instance.ActivateExtensionAsync(installed);
                    }
                }
            }
            finally
            {
                IsInstalling = false;
                InstallProgress = 0;
            }
        }

        [RelayCommand]
        public async Task UninstallAsync()
        {
            if (!IsInstalled) return;

            IsInstalling = true;
            try
            {
                _ = await NodeExtensionHostService.Instance.DeactivateExtensionAsync(Id);
                bool success = await _manager.UninstallExtensionAsync(Id);
                if (success)
                {
                    InstalledItem = null;
                    IsInstalled = false;
                }
            }
            finally
            {
                IsInstalling = false;
            }
        }

        [RelayCommand]
        public void ToggleEnabled()
        {
            if (!IsInstalled) return;

            if (IsEnabled)
            {
                _manager.DisableExtension(Id);
                _ = NodeExtensionHostService.Instance.DeactivateExtensionAsync(Id);
                IsEnabled = false;
            }
            else
            {
                _manager.EnableExtension(Id);
                if (InstalledItem != null && InstalledItem.HasEntrypoint)
                {
                    _ = NodeExtensionHostService.Instance.ActivateExtensionAsync(InstalledItem);
                }
                IsEnabled = true;
            }
        }

        [RelayCommand]
        public void OpenDetails()
        {
            RequestOpenDetails?.Invoke(this);
        }
    }
}
