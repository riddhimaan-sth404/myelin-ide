using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core.Services;

namespace Myelin.UI.ViewModels
{
    public partial class WorkspaceSearchViewModel : ViewModelBase
    {
        private readonly WorkspaceSearchService _searchService = WorkspaceSearchService.Instance;
        private string? _workspaceRoot;
        private Action<string, int>? _openFileAtLineAction;
        private CancellationTokenSource? _searchCts;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _replaceQuery = string.Empty;

        [ObservableProperty]
        private bool _isReplaceVisible = false;

        [ObservableProperty]
        private bool _matchCase = false;

        [ObservableProperty]
        private bool _matchWholeWord = false;

        [ObservableProperty]
        private bool _useRegex = false;

        [ObservableProperty]
        private bool _isSearching = false;

        [ObservableProperty]
        private ObservableCollection<SearchFileResult> _searchResults = new();

        [ObservableProperty]
        private string _resultSummary = "No search results yet.";

        [ObservableProperty]
        private int _totalMatchCount = 0;

        public bool HasResults => TotalMatchCount > 0;

        public WorkspaceSearchViewModel()
        {
        }

        public void Initialize(string? workspaceRoot, Action<string, int> openFileAtLineAction)
        {
            _workspaceRoot = workspaceRoot;
            _openFileAtLineAction = openFileAtLineAction;
        }

        public void SetWorkspaceRoot(string? workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
            SearchResults.Clear();
            TotalMatchCount = 0;
            ResultSummary = "No search results yet.";
            OnPropertyChanged(nameof(HasResults));
        }

        [RelayCommand]
        public async Task SearchAsync()
        {
            if (string.IsNullOrEmpty(_workspaceRoot) || string.IsNullOrWhiteSpace(SearchQuery))
            {
                SearchResults.Clear();
                TotalMatchCount = 0;
                ResultSummary = "No search results yet.";
                OnPropertyChanged(nameof(HasResults));
                return;
            }

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            IsSearching = true;
            ResultSummary = "Searching...";

            try
            {
                var options = new SearchOptions
                {
                    Query = SearchQuery,
                    MatchCase = MatchCase,
                    MatchWholeWord = MatchWholeWord,
                    UseRegex = UseRegex
                };

                var results = await _searchService.SearchAsync(_workspaceRoot, options, _searchCts.Token);

                SearchResults.Clear();
                int total = 0;
                foreach (var r in results)
                {
                    SearchResults.Add(r);
                    total += r.MatchCount;
                }

                TotalMatchCount = total;
                ResultSummary = total > 0 
                    ? $"{total} results in {results.Count} files" 
                    : "No results found.";
                OnPropertyChanged(nameof(HasResults));
            }
            catch (OperationCanceledException)
            {
                // Ignored
            }
            finally
            {
                IsSearching = false;
            }
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchQuery = string.Empty;
            SearchResults.Clear();
            TotalMatchCount = 0;
            ResultSummary = "No search results yet.";
            OnPropertyChanged(nameof(HasResults));
        }

        [RelayCommand]
        public void ToggleReplace()
        {
            IsReplaceVisible = !IsReplaceVisible;
        }

        [RelayCommand]
        public void ToggleMatchCase()
        {
            MatchCase = !MatchCase;
            _ = SearchAsync();
        }

        [RelayCommand]
        public void ToggleMatchWholeWord()
        {
            MatchWholeWord = !MatchWholeWord;
            _ = SearchAsync();
        }

        [RelayCommand]
        public void ToggleUseRegex()
        {
            UseRegex = !UseRegex;
            _ = SearchAsync();
        }

        [RelayCommand]
        public void OpenMatch(SearchMatchItem match)
        {
            if (match != null && !string.IsNullOrEmpty(match.FilePath))
            {
                _openFileAtLineAction?.Invoke(match.FilePath, match.LineNumber);
            }
        }

        [RelayCommand]
        public void ToggleFileExpanded(SearchFileResult file)
        {
            if (file != null)
            {
                file.IsExpanded = !file.IsExpanded;
                // Force UI update
                int idx = SearchResults.IndexOf(file);
                if (idx >= 0)
                {
                    SearchResults[idx] = file;
                }
            }
        }
    }
}
