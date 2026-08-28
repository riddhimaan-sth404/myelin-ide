using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Myelin.UI.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _searchFilter = string.Empty;

        // Editor Settings
        [ObservableProperty]
        private double _editorFontSize = 13.0;

        [ObservableProperty]
        private int _editorTabSize = 4;

        [ObservableProperty]
        private string _editorFontFamily = "Cascadia Code, Consolas";

        [ObservableProperty]
        private bool _wordWrap = false;

        [ObservableProperty]
        private bool _renderWhitespace = false;

        // Terminal Settings
        [ObservableProperty]
        private double _terminalFontSize = 13.0;

        [ObservableProperty]
        private string _defaultTerminalProfile = "PowerShell 7";

        [ObservableProperty]
        private bool _cursorBlinking = true;

        // Appearance
        [ObservableProperty]
        private string _selectedTheme = "VS Code Dark Modern";

        [ObservableProperty]
        private string _selectedIconTheme = "Philipp Kief Material Icon Theme (Official 1,250+ Icons)";

        // Available Options
        public ObservableCollection<string> AvailableThemes { get; } = new()
        {
            "VS Code Dark Modern",
            "VS Code Dark+ (Default Dark)",
            "GitHub Dark",
            "One Dark Pro"
        };

        public ObservableCollection<int> AvailableTabSizes { get; } = new() { 2, 4, 8 };

        public ObservableCollection<string> AvailableFontFamilies { get; } = new()
        {
            "Cascadia Code, Consolas",
            "Consolas, Courier New",
            "Fira Code, monospace",
            "JetBrains Mono, monospace"
        };

        public SettingsViewModel()
        {
        }

        [RelayCommand]
        public void ResetDefaults()
        {
            EditorFontSize = 13.0;
            EditorTabSize = 4;
            EditorFontFamily = "Cascadia Code, Consolas";
            WordWrap = false;
            RenderWhitespace = false;
            TerminalFontSize = 13.0;
            CursorBlinking = true;
            SelectedTheme = "VS Code Dark Modern";
        }
    }
}
