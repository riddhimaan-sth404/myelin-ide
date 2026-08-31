using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Myelin.UI.Views
{
    public partial class WebPreviewView : UserControl
    {
        public static readonly StyledProperty<string?> UrlProperty =
            AvaloniaProperty.Register<WebPreviewView, string?>(nameof(Url), "http://127.0.0.1:5500/");

        public string? Url
        {
            get => GetValue(UrlProperty);
            set => SetValue(UrlProperty, value);
        }

        public WebPreviewView()
        {
            InitializeComponent();
        }

        static WebPreviewView()
        {
            UrlProperty.Changed.AddClassHandler<WebPreviewView>((control, e) =>
            {
                if (e.NewValue is string url)
                {
                    control.UrlTextBox.Text = url;
                    control.UrlDisplayBlock.Text = url;
                }
            });
        }

        private void OnUrlKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Url = UrlTextBox.Text;
            }
        }

        private void OnRefreshClicked(object? sender, RoutedEventArgs e)
        {
            Url = UrlTextBox.Text;
        }

        private void OnOpenInExternalBrowserClicked(object? sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text ?? Url ?? "http://127.0.0.1:5500/";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
