using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core.Commands;
using Myelin.Core.Models;

namespace Myelin.UI.ViewModels
{
    public class PaletteItem
    {
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Shortcut { get; set; }
        public string? FilePath { get; set; }
        public bool IsFile => !string.IsNullOrEmpty(FilePath);
        public Action? Action { get; set; }
    }

    public partial class CommandPaletteViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool _isOpen;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<PaletteItem> _filteredItems = new();

        [ObservableProperty]
        private PaletteItem? _selectedItem;

        private readonly MainWindowViewModel _mainVm;

        public CommandPaletteViewModel(MainWindowViewModel mainVm)
        {
            _mainVm = mainVm;
        }

        public void OpenInCommandMode()
        {
            SearchText = "> ";
            IsOpen = true;
            RefreshItems();
        }

        public void OpenInFileMode()
        {
            SearchText = "";
            IsOpen = true;
            RefreshItems();
        }

        [RelayCommand]
        public void Close()
        {
            IsOpen = false;
        }

        [RelayCommand]
        public void ExecuteSelected()
        {
            if (SelectedItem != null)
            {
                var action = SelectedItem.Action;
                Close();
                action?.Invoke();
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            RefreshItems();
        }

        private void RefreshItems()
        {
            FilteredItems.Clear();

            if (SearchText.StartsWith(">"))
            {
                // Commands mode
                var results = CommandRegistry.Instance.Search(SearchText);
                foreach (var cmd in results)
                {
                    FilteredItems.Add(new PaletteItem
                    {
                        Title = cmd.DisplayText,
                        Shortcut = cmd.Shortcut,
                        Action = cmd.Action
                    });
                }
            }
            else
            {
                // File search mode (Quick Open)
                var files = CollectAllProjectFiles();
                string q = SearchText.Trim().ToLowerInvariant();

                var matching = string.IsNullOrEmpty(q)
                    ? files
                    : files.Where(f => f.Name.ToLowerInvariant().Contains(q) || f.Path.ToLowerInvariant().Contains(q)).ToList();

                foreach (var file in matching.Take(30))
                {
                    FilteredItems.Add(new PaletteItem
                    {
                        Title = file.Name,
                        Subtitle = file.Path,
                        FilePath = file.Path,
                        Action = () => _mainVm.OpenFile(file.Path)
                    });
                }
            }

            if (FilteredItems.Count > 0)
            {
                SelectedItem = FilteredItems[0];
            }
            else
            {
                SelectedItem = null;
            }
        }

        private List<FileNode> CollectAllProjectFiles()
        {
            var list = new List<FileNode>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Collect from open tabs
            foreach (var tab in _mainVm.Tabs)
            {
                if (!string.IsNullOrEmpty(tab.FilePath) && File.Exists(tab.FilePath) && seenPaths.Add(tab.FilePath))
                {
                    list.Add(new FileNode
                    {
                        Name = tab.Title,
                        Path = tab.FilePath,
                        IsDirectory = false
                    });
                }
            }

            // 2. Collect from RootNode tree
            if (_mainVm.RootNode != null)
            {
                void Traverse(FileNode node)
                {
                    if (!node.IsDirectory && seenPaths.Add(node.Path))
                    {
                        list.Add(node);
                    }
                    foreach (var child in node.Children)
                    {
                        Traverse(child);
                    }
                }
                Traverse(_mainVm.RootNode);
            }

            // 3. If workspace root is set and tree empty, do a direct directory search
            if (list.Count == 0 && !string.IsNullOrEmpty(_mainVm.WorkspaceRoot) && Directory.Exists(_mainVm.WorkspaceRoot))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(_mainVm.WorkspaceRoot, "*", SearchOption.AllDirectories).Take(500))
                    {
                        if (seenPaths.Add(f))
                        {
                            list.Add(new FileNode
                            {
                                Name = Path.GetFileName(f),
                                Path = f,
                                IsDirectory = false
                            });
                        }
                    }
                }
                catch { }
            }

            return list;
        }
    }
}
