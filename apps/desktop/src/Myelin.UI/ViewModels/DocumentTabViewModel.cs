using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Myelin.UI.ViewModels
{
    public partial class DocumentTabViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ulong _docId;

        [ObservableProperty]
        private string _title = "Untitled";

        [ObservableProperty]
        private string? _filePath;

        [ObservableProperty]
        private bool _isDirty;

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

        public string Breadcrumbs
        {
            get
            {
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
                if (string.IsNullOrEmpty(FilePath)) return "Plain Text";
                string ext = Path.GetExtension(FilePath).ToLowerInvariant();
                return ext switch
                {
                    ".rs" => "Rust",
                    ".cs" => "C#",
                    ".axaml" or ".xaml" => "XAML",
                    ".json" => "JSON",
                    ".toml" => "TOML",
                    ".md" => "Markdown",
                    ".txt" => "Plain Text",
                    ".bat" or ".cmd" => "Batch",
                    ".ps1" => "PowerShell",
                    ".sh" => "Shell",
                    ".js" or ".mjs" => "JavaScript",
                    ".ts" => "TypeScript",
                    ".py" => "Python",
                    ".cpp" or ".c" or ".h" or ".hpp" => "C/C++",
                    ".html" => "HTML",
                    ".css" => "CSS",
                    _ => "Plain Text"
                };
            }
        }

        public DocumentTabViewModel(ulong docId, string? filePath = null)
        {
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
    }
}
