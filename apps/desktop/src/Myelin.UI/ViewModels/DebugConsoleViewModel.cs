using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Myelin.Core.Models;
using Myelin.Core.Services;

namespace Myelin.UI.ViewModels
{
    public partial class DebugConsoleViewModel : ViewModelBase
    {
        private readonly DebuggerService _service;
        private readonly List<string> _history = new();
        private int _historyIndex = -1;

        [ObservableProperty]
        private ObservableCollection<DebugConsoleMessage> _messages = new();

        [ObservableProperty]
        private string _inputExpression = "";

        [ObservableProperty]
        private string _rawText = "";

        public DebugConsoleViewModel()
        {
            _service = DebuggerService.Instance;
            _service.ConsoleMessageReceived += msg =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Messages.Add(msg);
                    RawText += msg.Text;
                });
            };
        }

        [RelayCommand]
        public async Task ExecuteRepl()
        {
            if (string.IsNullOrWhiteSpace(InputExpression)) return;

            string expr = InputExpression.Trim();
            _history.Add(expr);
            _historyIndex = _history.Count;
            InputExpression = "";

            await _service.EvaluateInReplAsync(expr);
        }

        [RelayCommand]
        public void Clear()
        {
            Messages.Clear();
            RawText = "";
        }

        public void HistoryPrevious()
        {
            if (_history.Count == 0) return;
            if (_historyIndex > 0)
            {
                _historyIndex--;
                InputExpression = _history[_historyIndex];
            }
        }

        public void HistoryNext()
        {
            if (_history.Count == 0) return;
            if (_historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                InputExpression = _history[_historyIndex];
            }
            else
            {
                _historyIndex = _history.Count;
                InputExpression = "";
            }
        }
    }
}
