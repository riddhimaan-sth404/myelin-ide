using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Myelin.UI.ViewModels
{
    public enum TabType
    {
        TextDocument,
        MarkdownPreview,
        WebPreview,
        ExtensionDetails,
        ExtensionWebview
    }

    public partial class DocumentTabViewModel : ViewModelBase
    {
        [ObservableProperty]
        private TabType _tabType = TabType.TextDocument;

        [ObservableProperty]
        private ulong _docId;

        [ObservableProperty]
        private string _title = "Untitled";

        [ObservableProperty]
        private string? _filePath;

        [ObservableProperty]
        private bool _isDirty;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private ulong _scrollLineOffset = 0;

        [ObservableProperty]
        private nuint _desiredColumn = 0;

        [ObservableProperty]
        private nuint? _selectionAnchorLine;

        [ObservableProperty]
        private nuint? _selectionAnchorColumn;

        [ObservableProperty]
        private nuint? _selectionHeadLine;

        [ObservableProperty]
        private nuint? _selectionHeadColumn;

        // Markdown / Web Preview Tab Properties
        [ObservableProperty]
        private string _markdownText = string.Empty;

        [ObservableProperty]
        private string _webPreviewUrl = "http://127.0.0.1:5500/";

        [ObservableProperty]
        private bool _isMarkdownPreviewActive;

        // Extension Details Tab Properties
        [ObservableProperty]
        private ExtensionItemViewModel? _extensionItem;

        [ObservableProperty]
        private string _readmeText = string.Empty;

        // Extension Webview Tab Properties
        [ObservableProperty]
        private string? _webviewPanelId;

        [ObservableProperty]
        private string? _webviewHtml;

        public bool IsTextDocument => TabType == TabType.TextDocument;
        public bool IsMarkdownPreview => TabType == TabType.MarkdownPreview || IsMarkdownPreviewActive;
        public bool IsWebPreview => TabType == TabType.WebPreview;
        public bool IsExtensionDetails => TabType == TabType.ExtensionDetails;
        public bool IsExtensionWebview => TabType == TabType.ExtensionWebview;

        public bool IsMarkdownFile => Language == "Markdown";
        public bool IsHtmlFile => Language == "HTML";

        public string Breadcrumbs
        {
            get
            {
                if (TabType == TabType.MarkdownPreview) return $"Preview  ›  {Title}";
                if (TabType == TabType.WebPreview) return $"Live Preview  ›  {Title}";
                if (TabType == TabType.ExtensionDetails) return $"Extensions  ›  {Title}";
                if (TabType == TabType.ExtensionWebview) return $"Webview  ›  {Title}";
                if (string.IsNullOrEmpty(FilePath)) return Title;
                var dir = Path.GetDirectoryName(FilePath);
                var dirName = !string.IsNullOrEmpty(dir) ? Path.GetFileName(dir) : "";
                return !string.IsNullOrEmpty(dirName) ? $"{dirName}  ›  {Title}" : Title;
            }
        }

        public string Language
        {
            get
            {
                if (TabType == TabType.ExtensionDetails) return "Extension Details";
                if (TabType == TabType.ExtensionWebview) return "Extension Webview";
                if (TabType == TabType.MarkdownPreview) return "Markdown Preview";
                if (TabType == TabType.WebPreview) return "Web Preview";
                if (string.IsNullOrEmpty(FilePath)) return "Plain Text";
                string ext = Path.GetExtension(FilePath).ToLowerInvariant();
                return ext switch
                {
                    ".rs" => "Rust",
                    ".cs" => "C#",
                    ".axaml" or ".xaml" => "XAML",
                    ".json" => "JSON",
                    ".toml" => "TOML",
                    ".md" or ".markdown" => "Markdown",
                    ".txt" => "Plain Text",
                    ".bat" or ".cmd" => "Batch",
                    ".ps1" => "PowerShell",
                    ".sh" => "Shell",
                    ".js" or ".mjs" => "JavaScript",
                    ".ts" or ".tsx" => "TypeScript",
                    ".py" => "Python",
                    ".cpp" or ".c" or ".h" or ".hpp" => "C/C++",
                    ".html" or ".htm" => "HTML",
                    ".css" => "CSS",
                    _ => "Plain Text"
                };
            }
        }

        public DocumentTabViewModel(ulong docId, string? filePath = null)
        {
            TabType = TabType.TextDocument;
            DocId = docId;
            FilePath = filePath;
            Title = filePath != null ? Path.GetFileName(filePath) : "Untitled";
            ScrollLineOffset = 0;
            DesiredColumn = 0;
            SelectionAnchorLine = null;
            SelectionAnchorColumn = null;
            SelectionHeadLine = null;
            SelectionHeadColumn = null;
        }

        public DocumentTabViewModel(ExtensionItemViewModel extensionItem, string readmeText)
        {
            TabType = TabType.ExtensionDetails;
            ExtensionItem = extensionItem;
            ReadmeText = readmeText;
            Title = $"Extension: {extensionItem.Title}";
        }

        public DocumentTabViewModel(string webviewPanelId, string title)
        {
            TabType = TabType.ExtensionWebview;
            WebviewPanelId = webviewPanelId;
            Title = title;
        }

        public static DocumentTabViewModel CreateWebPreview(string url, string title = "Web Preview")
        {
            var tab = new DocumentTabViewModel(0, null)
            {
                TabType = TabType.WebPreview,
                WebPreviewUrl = url,
                Title = title
            };
            return tab;
        }

        public static DocumentTabViewModel CreateMarkdownPreview(string markdownText, string title = "Markdown Preview")
        {
            var tab = new DocumentTabViewModel(0, null)
            {
                TabType = TabType.MarkdownPreview,
                MarkdownText = markdownText,
                Title = $"Preview {title}"
            };
            return tab;
        }

        [RelayCommand]
        public void ToggleMarkdownPreview()
        {
            IsMarkdownPreviewActive = !IsMarkdownPreviewActive;
        }
    }
}
