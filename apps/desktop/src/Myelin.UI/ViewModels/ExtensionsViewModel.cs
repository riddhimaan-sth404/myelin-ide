using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core.Models;
using Myelin.Core.Services;

namespace Myelin.UI.ViewModels
{
    public partial class ExtensionsViewModel : ViewModelBase
    {
        private readonly OpenVsxClient _client = OpenVsxClient.Instance;
        private readonly ExtensionManagerService _manager = ExtensionManagerService.Instance;
        private CancellationTokenSource? _searchCts;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private int _selectedFilterTab = 0; // 0 = Marketplace / Popular, 1 = Installed

        [ObservableProperty]
        private ObservableCollection<ExtensionItemViewModel> _marketplaceExtensions = new();

        [ObservableProperty]
        private ObservableCollection<ExtensionItemViewModel> _installedExtensions = new();

        [ObservableProperty]
        private ExtensionItemViewModel? _selectedExtension;

        public bool IsMarketplaceTab => SelectedFilterTab == 0;
        public bool IsInstalledTab => SelectedFilterTab == 1;
        public int InstalledCount => InstalledExtensions.Count;
        public bool HasMarketplaceResults => MarketplaceExtensions.Count > 0;

        public event Action<ExtensionItemViewModel, string>? RequestOpenExtensionTab;

        public ExtensionsViewModel()
        {
            _manager.ExtensionInstalled += _ => RefreshInstalled();
            _manager.ExtensionUninstalled += _ => RefreshInstalled();
            _manager.ExtensionStateChanged += _ => RefreshInstalled();

            RefreshInstalled();
            _ = LoadPopularExtensionsAsync();
        }

        partial void OnSelectedFilterTabChanged(int value)
        {
            OnPropertyChanged(nameof(IsMarketplaceTab));
            OnPropertyChanged(nameof(IsInstalledTab));
            if (value == 1)
            {
                RefreshInstalled();
            }
        }

        partial void OnSearchQueryChanged(string value)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            Task.Delay(350, token).ContinueWith(async t =>
            {
                if (!t.IsCanceled)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        await LoadPopularExtensionsAsync();
                    }
                    else
                    {
                        await SearchExtensionsAsync(value);
                    }
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        [RelayCommand]
        public async Task SearchExtensionsAsync(string? query = null)
        {
            string q = query ?? SearchQuery;
            if (string.IsNullOrWhiteSpace(q))
            {
                await LoadPopularExtensionsAsync();
                return;
            }

            IsLoading = true;
            SelectedFilterTab = 0;
            StatusMessage = $"Searching Open VSX for '{q}'...";

            try
            {
                var result = await _client.SearchExtensionsAsync(q, 0, 40);
                MarketplaceExtensions.Clear();
                foreach (var item in result.Extensions)
                {
                    var vm = new ExtensionItemViewModel(item);
                    vm.RequestOpenDetails += OnRequestOpenDetails;
                    MarketplaceExtensions.Add(vm);
                }

                StatusMessage = result.Extensions.Count > 0
                    ? $"Found {result.TotalSize} extensions"
                    : "No extensions found on Open VSX.";
                OnPropertyChanged(nameof(HasMarketplaceResults));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task LoadPopularExtensionsAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading popular Open VSX extensions...";

            try
            {
                var result = await _client.GetPopularExtensionsAsync(40);
                MarketplaceExtensions.Clear();
                foreach (var item in result.Extensions)
                {
                    var vm = new ExtensionItemViewModel(item);
                    vm.RequestOpenDetails += OnRequestOpenDetails;
                    MarketplaceExtensions.Add(vm);
                }
                StatusMessage = "Popular extensions loaded";
                OnPropertyChanged(nameof(HasMarketplaceResults));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load marketplace: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public void SelectTab(object? param)
        {
            if (param is int tab)
            {
                SelectedFilterTab = tab;
            }
            else if (param is string s && int.TryParse(s, out int parsed))
            {
                SelectedFilterTab = parsed;
            }
        }

        [RelayCommand]
        public void RefreshInstalled()
        {
            InstalledExtensions.Clear();
            foreach (var ext in _manager.InstalledExtensions)
            {
                var vm = new ExtensionItemViewModel(ext);
                vm.RequestOpenDetails += OnRequestOpenDetails;
                InstalledExtensions.Add(vm);
            }

            // Sync all marketplace cards
            foreach (var card in MarketplaceExtensions)
            {
                card.UpdateState();
            }

            OnPropertyChanged(nameof(InstalledCount));
        }

        private async void OnRequestOpenDetails(ExtensionItemViewModel item)
        {
            await OpenExtensionDetailsTabAsync(item);
        }

        [RelayCommand]
        public async Task OpenExtensionDetailsTabAsync(ExtensionItemViewModel? item)
        {
            if (item == null) return;
            SelectedExtension = item;

            string readme = string.Empty;
            if (item.MarketplaceItem != null && !string.IsNullOrEmpty(item.MarketplaceItem.ReadmeUrl))
            {
                readme = await _client.FetchReadmeAsync(item.MarketplaceItem.ReadmeUrl) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(readme) && item.InstalledItem != null)
            {
                string localReadme = System.IO.Path.Combine(item.InstalledItem.InstallDirectory, "README.md");
                if (System.IO.File.Exists(localReadme))
                {
                    readme = await System.IO.File.ReadAllTextAsync(localReadme);
                }
            }

            if (string.IsNullOrWhiteSpace(readme))
            {
                readme = $"# {item.Title}\n\n{item.Description}\n\n**Publisher:** {item.Publisher}\n**Version:** {item.Version}\n";
            }

            RequestOpenExtensionTab?.Invoke(item, readme);
        }
    }
}
