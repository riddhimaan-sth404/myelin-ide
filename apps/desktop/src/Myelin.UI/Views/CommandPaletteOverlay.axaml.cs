using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Myelin.UI.ViewModels;

namespace Myelin.UI.Views
{
    public partial class CommandPaletteOverlay : UserControl
    {
        public CommandPaletteOverlay()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (DataContext is CommandPaletteViewModel vm)
                {
                    vm.PropertyChanged += (vs, ve) =>
                    {
                        if (ve.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && vm.IsOpen)
                        {
                            SearchBox.Focus();
                            SearchBox.SelectAll();
                        }
                    };
                }
            };
        }

        private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
        {
            // Only close if clicking directly on the outer backdrop panel
            if (e.Source == sender && DataContext is CommandPaletteViewModel vm)
            {
                vm.Close();
            }
        }

        private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not CommandPaletteViewModel vm) return;

            if (e.Key == Key.Escape)
            {
                vm.Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                vm.ExecuteSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                int idx = vm.SelectedItem != null ? vm.FilteredItems.IndexOf(vm.SelectedItem) : -1;
                if (idx + 1 < vm.FilteredItems.Count)
                {
                    vm.SelectedItem = vm.FilteredItems[idx + 1];
                    ResultsList?.ScrollIntoView(vm.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                int idx = vm.SelectedItem != null ? vm.FilteredItems.IndexOf(vm.SelectedItem) : -1;
                if (idx > 0)
                {
                    vm.SelectedItem = vm.FilteredItems[idx - 1];
                    ResultsList?.ScrollIntoView(vm.SelectedItem);
                }
                e.Handled = true;
            }
        }

        private void OnItemDoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (DataContext is CommandPaletteViewModel vm)
            {
                vm.ExecuteSelected();
            }
        }
    }
}
