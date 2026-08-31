using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform;

namespace Myelin.UI.Views
{
    public class NativeWebViewControl : NativeControlHost
    {
        public static readonly StyledProperty<string?> HtmlContentProperty =
            AvaloniaProperty.Register<NativeWebViewControl, string?>(nameof(HtmlContent));

        public static readonly StyledProperty<string?> SourceUrlProperty =
            AvaloniaProperty.Register<NativeWebViewControl, string?>(nameof(SourceUrl));

        public string? HtmlContent
        {
            get => GetValue(HtmlContentProperty);
            set => SetValue(HtmlContentProperty, value);
        }

        public string? SourceUrl
        {
            get => GetValue(SourceUrlProperty);
            set => SetValue(SourceUrlProperty, value);
        }

        private string? _tempHtmlFile;

        public NativeWebViewControl()
        {
        }

        static NativeWebViewControl()
        {
            HtmlContentProperty.Changed.AddClassHandler<NativeWebViewControl>((control, e) =>
            {
                if (e.NewValue is string html && !string.IsNullOrEmpty(html))
                {
                    control.LoadHtml(html);
                }
            });

            SourceUrlProperty.Changed.AddClassHandler<NativeWebViewControl>((control, e) =>
            {
                if (e.NewValue is string url && !string.IsNullOrEmpty(url))
                {
                    control.Navigate(url);
                }
            });
        }

        public void LoadHtml(string html)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "MyelinPreview");
                Directory.CreateDirectory(tempDir);
                _tempHtmlFile = Path.Combine(tempDir, $"preview_{Guid.NewGuid():N}.html");
                File.WriteAllText(_tempHtmlFile, html);
                SourceUrl = new Uri(_tempHtmlFile).AbsoluteUri;
            }
            catch { }
        }

        public void Navigate(string url)
        {
            SourceUrl = url;
        }

        public void OpenInExternalBrowser()
        {
            string? target = SourceUrl ?? _tempHtmlFile;
            if (!string.IsNullOrEmpty(target))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            // Return platform handle
            return base.CreateNativeControlCore(parent);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            base.DestroyNativeControlCore(control);
            if (!string.IsNullOrEmpty(_tempHtmlFile) && File.Exists(_tempHtmlFile))
            {
                try { File.Delete(_tempHtmlFile); } catch { }
            }
        }
    }
}
