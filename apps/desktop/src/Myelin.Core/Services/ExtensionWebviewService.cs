using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Myelin.Core.Services
{
    public class ExtensionWebviewPanelState
    {
        public string PanelId { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string HtmlContent { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
    }

    public class ExtensionWebviewService
    {
        private static ExtensionWebviewService? _instance;
        public static ExtensionWebviewService Instance => _instance ??= new ExtensionWebviewService();

        private readonly ConcurrentDictionary<string, ExtensionWebviewPanelState> _panels = new();

        public IReadOnlyCollection<ExtensionWebviewPanelState> ActivePanels => _panels.Values.ToList();

        public event Action<ExtensionWebviewPanelState>? PanelOpened;
        public event Action<string>? PanelClosed;
        public event Action<string, string>? HtmlUpdated;
        public event Action<string, JsonElement>? MessageFromExtensionReceived;

        public ExtensionWebviewService()
        {
            var host = NodeExtensionHostService.Instance;
            host.WebviewPanelCreated += (panelId, viewType, title) =>
            {
                var panel = new ExtensionWebviewPanelState
                {
                    PanelId = panelId,
                    ViewType = viewType,
                    Title = title
                };
                _panels[panelId] = panel;
                PanelOpened?.Invoke(panel);
            };

            host.WebviewPanelDisposed += (panelId) =>
            {
                _panels.TryRemove(panelId, out _);
                PanelClosed?.Invoke(panelId);
            };

            host.WebviewMessageReceived += (panelId, msg) =>
            {
                MessageFromExtensionReceived?.Invoke(panelId, msg);
            };
        }

        public void SetHtml(string panelId, string html)
        {
            if (_panels.TryGetValue(panelId, out var panel))
            {
                panel.HtmlContent = html;
                HtmlUpdated?.Invoke(panelId, html);
            }
        }

        public void PostMessageToExtension(string panelId, object message)
        {
            _ = NodeExtensionHostService.Instance.SendWebviewMessageAsync(panelId, message);
        }

        public ExtensionWebviewPanelState? GetPanel(string panelId)
        {
            return _panels.TryGetValue(panelId, out var p) ? p : null;
        }
    }
}
