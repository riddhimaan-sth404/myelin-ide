using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Myelin.Core.Services;

namespace Myelin.UI.Views
{
    public partial class MarkdownPreviewView : UserControl
    {
        public static readonly StyledProperty<string?> MarkdownContentProperty =
            AvaloniaProperty.Register<MarkdownPreviewView, string?>(nameof(MarkdownContent));

        public string? MarkdownContent
        {
            get => GetValue(MarkdownContentProperty);
            set => SetValue(MarkdownContentProperty, value);
        }

        private string? _tempHtmlPath;

        public MarkdownPreviewView()
        {
            InitializeComponent();
        }

        static MarkdownPreviewView()
        {
            MarkdownContentProperty.Changed.AddClassHandler<MarkdownPreviewView>((control, e) =>
            {
                control.UpdatePreview(e.NewValue as string);
            });
        }

        public void UpdatePreview(string? markdown)
        {
            string md = markdown ?? "";
            PreviewTextBlock.Text = md;

            try
            {
                string html = MarkdownRendererService.Instance.RenderToHtml(md, "Markdown Preview", true);
                string tempDir = Path.Combine(Path.GetTempPath(), "MyelinPreview");
                Directory.CreateDirectory(tempDir);
                _tempHtmlPath = Path.Combine(tempDir, "preview.html");
                File.WriteAllText(_tempHtmlPath, html);
            }
            catch { }
        }

        private void OnOpenInBrowserClicked(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_tempHtmlPath) && File.Exists(_tempHtmlPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _tempHtmlPath,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }
}
