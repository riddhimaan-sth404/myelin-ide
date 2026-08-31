using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Myelin.Core.Services;

namespace Myelin.UI.Views
{
    public class ExtensionWebviewControl : UserControl
    {
        public static readonly StyledProperty<string> PanelIdProperty =
            AvaloniaProperty.Register<ExtensionWebviewControl, string>(nameof(PanelId), string.Empty);

        public static readonly StyledProperty<string> HtmlContentProperty =
            AvaloniaProperty.Register<ExtensionWebviewControl, string>(nameof(HtmlContent), string.Empty);

        public string PanelId
        {
            get => GetValue(PanelIdProperty);
            set => SetValue(PanelIdProperty, value);
        }

        public string HtmlContent
        {
            get => GetValue(HtmlContentProperty);
            set => SetValue(HtmlContentProperty, value);
        }

        private readonly TextBlock _statusBlock;
        private readonly ScrollViewer _scrollViewer;
        private readonly StackPanel _contentPanel;

        public ExtensionWebviewControl()
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

            _statusBlock = new TextBlock
            {
                Text = "Loading Webview...",
                Foreground = new SolidColorBrush(Color.Parse("#888888")),
                FontSize = 12,
                Margin = new Thickness(12)
            };

            _contentPanel = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
            _contentPanel.Children.Add(_statusBlock);

            _scrollViewer = new ScrollViewer
            {
                Content = _contentPanel,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };

            Content = _scrollViewer;

            ExtensionWebviewService.Instance.HtmlUpdated += OnHtmlUpdated;
        }

        private void OnHtmlUpdated(string panelId, string html)
        {
            if (PanelId == panelId)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    HtmlContent = html;
                    UpdateRenderedContent(html);
                });
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == HtmlContentProperty && change.NewValue is string html)
            {
                UpdateRenderedContent(html);
            }
        }

        private void UpdateRenderedContent(string html)
        {
            _contentPanel.Children.Clear();

            if (string.IsNullOrWhiteSpace(html))
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = "Empty Webview content.",
                    Foreground = new SolidColorBrush(Color.Parse("#888888")),
                    FontSize = 12
                });
                return;
            }

            // Strip basic tags for text display or render rich representation
            string stripped = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
            var lines = stripped.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            var headerBlock = new TextBlock
            {
                Text = $"[Extension Webview: {PanelId}]",
                Foreground = new SolidColorBrush(Color.Parse("#0078D4")),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _contentPanel.Children.Add(headerBlock);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = line.Trim(),
                    Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
    }
}
