using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core.Models;
using Myelin.Core.Services;

namespace Myelin.UI.ViewModels
{
    public partial class SourceControlViewModel : ViewModelBase
    {
        private readonly GitService _gitService = GitService.Instance;
        private string? _workspaceRoot;
        private Action<string>? _openFileAction;

        [ObservableProperty]
        private bool _isGitRepository = false;

        [ObservableProperty]
        private bool _isGitInstalled = true;

        [ObservableProperty]
        private string _currentBranch = "main";

        [ObservableProperty]
        private string _commitMessage = string.Empty;

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private ObservableCollection<GitFileItem> _stagedChanges = new();

        [ObservableProperty]
        private ObservableCollection<GitFileItem> _workingChanges = new();

        [ObservableProperty]
        private ObservableCollection<string> _branches = new();

        [ObservableProperty]
        private int _aheadCount = 0;

        [ObservableProperty]
        private int _behindCount = 0;

        [ObservableProperty]
        private bool _isStagedExpanded = true;

        [ObservableProperty]
        private bool _isChangesExpanded = true;

        public int StagedCount => StagedChanges.Count;
        public int ChangesCount => WorkingChanges.Count;
        public int TotalChangesCount => StagedChanges.Count + WorkingChanges.Count;

        public bool HasChanges => TotalChangesCount > 0;
        public bool HasStagedChanges => StagedChanges.Count > 0;

        public SourceControlViewModel()
        {
        }

        public void Initialize(string? workspaceRoot, Action<string> openFileAction)
        {
            _workspaceRoot = workspaceRoot;
            _openFileAction = openFileAction;
            _ = RefreshStatusAsync();
        }

        public void SetWorkspaceRoot(string? workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
            _ = RefreshStatusAsync();
        }

        [RelayCommand]
        public async Task RefreshStatusAsync()
        {
            if (string.IsNullOrEmpty(_workspaceRoot))
            {
                IsGitRepository = false;
                StagedChanges.Clear();
                WorkingChanges.Clear();
                OnPropertyChanged(nameof(StagedCount));
                OnPropertyChanged(nameof(ChangesCount));
                OnPropertyChanged(nameof(TotalChangesCount));
                OnPropertyChanged(nameof(HasChanges));
                OnPropertyChanged(nameof(HasStagedChanges));
                return;
            }

            IsLoading = true;
            try
            {
                var status = await _gitService.GetStatusAsync(_workspaceRoot);
                IsGitRepository = status.IsRepository;
                CurrentBranch = status.CurrentBranch;
                AheadCount = status.AheadCount;
                BehindCount = status.BehindCount;

                StagedChanges.Clear();
                foreach (var f in status.StagedFiles) StagedChanges.Add(f);

                WorkingChanges.Clear();
                foreach (var f in status.WorkingFiles) WorkingChanges.Add(f);

                Branches.Clear();
                foreach (var b in status.Branches) Branches.Add(b);

                OnPropertyChanged(nameof(StagedCount));
                OnPropertyChanged(nameof(ChangesCount));
                OnPropertyChanged(nameof(TotalChangesCount));
                OnPropertyChanged(nameof(HasChanges));
                OnPropertyChanged(nameof(HasStagedChanges));
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task StageFileAsync(GitFileItem item)
        {
            if (string.IsNullOrEmpty(_workspaceRoot) || item == null) return;
            bool success = await _gitService.StageFileAsync(_workspaceRoot, item.RelativePath);
            if (success) await RefreshStatusAsync();
        }

        [RelayCommand]
        public async Task StageAllAsync()
        {
            if (string.IsNullOrEmpty(_workspaceRoot)) return;
            bool success = await _gitService.StageAllAsync(_workspaceRoot);
            if (success) await RefreshStatusAsync();
        }

        [RelayCommand]
        public async Task UnstageFileAsync(GitFileItem item)
        {
            if (string.IsNullOrEmpty(_workspaceRoot) || item == null) return;
            bool success = await _gitService.UnstageFileAsync(_workspaceRoot, item.RelativePath);
            if (success) await RefreshStatusAsync();
        }

        [RelayCommand]
        public async Task UnstageAllAsync()
        {
            if (string.IsNullOrEmpty(_workspaceRoot)) return;
            bool success = await _gitService.UnstageAllAsync(_workspaceRoot);
            if (success) await RefreshStatusAsync();
        }

        [RelayCommand]
        public async Task DiscardChangesAsync(GitFileItem item)
        {
            if (string.IsNullOrEmpty(_workspaceRoot) || item == null) return;
            bool isUntracked = item.Status == GitFileStatus.Untracked;
            bool success = await _gitService.DiscardChangesAsync(_workspaceRoot, item.RelativePath, isUntracked);
            if (success) await RefreshStatusAsync();
        }

        [RelayCommand]
        public async Task CommitAsync()
        {
            if (string.IsNullOrEmpty(_workspaceRoot) || string.IsNullOrWhiteSpace(CommitMessage)) return;

            // If no staged changes but has working changes, auto-stage all
            if (StagedChanges.Count == 0 && WorkingChanges.Count > 0)
            {
                await _gitService.StageAllAsync(_workspaceRoot);
            }

            var (success, _) = await _gitService.CommitAsync(_workspaceRoot, CommitMessage);
            if (success)
            {
                CommitMessage = string.Empty;
                await RefreshStatusAsync();
            }
        }

        [RelayCommand]
        public async Task PushAsync()
        {
            if (string.IsNullOrEmpty(_workspaceRoot)) return;
            IsLoading = true;
            try
            {
                await _gitService.PushAsync(_workspaceRoot);
                await RefreshStatusAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task PullAsync()
        {
            if (string.IsNullOrEmpty(_workspaceRoot)) return;
            IsLoading = true;
            try
            {
                await _gitService.PullAsync(_workspaceRoot);
                await RefreshStatusAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task SwitchBranchAsync(string branchName)
        {
            if (string.IsNullOrEmpty(_workspaceRoot) || string.IsNullOrEmpty(branchName)) return;
            var (success, _) = await _gitService.CheckoutBranchAsync(_workspaceRoot, branchName);
            if (success) await RefreshStatusAsync();
        }

        [RelayCommand]
        public async Task InitRepositoryAsync()
        {
            if (string.IsNullOrEmpty(_workspaceRoot)) return;
            var (success, _) = await _gitService.InitRepositoryAsync(_workspaceRoot);
            if (success) await RefreshStatusAsync();
        }

        [RelayCommand]
        public void OpenFile(GitFileItem item)
        {
            if (item != null && !string.IsNullOrEmpty(item.FullPath))
            {
                _openFileAction?.Invoke(item.FullPath);
            }
        }

        [RelayCommand]
        public void ToggleStagedSection()
        {
            IsStagedExpanded = !IsStagedExpanded;
        }

        [RelayCommand]
        public void ToggleChangesSection()
        {
            IsChangesExpanded = !IsChangesExpanded;
        }
    }
}
