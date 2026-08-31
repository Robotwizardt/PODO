using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.FileDialogAccess;

internal partial class FileDialogAccessWindow : Window
{
    internal FileDialogAccessWindow(FileDialogAccessViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        AppThemeManager.ThemeChanged += OnThemeChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        AppThemeManager.ApplyToWindow(this, WindowBackdropKind.Transient);

    private void OnThemeChanged(object? sender, AppTheme theme) =>
        AppThemeManager.ApplyToWindow(this, WindowBackdropKind.Transient);

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OnAccessScrollViewerPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || scrollViewer.ScrollableHeight <= 0
            || e.Delta == 0)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not FileDialogAccessViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement == SearchBox
            && e.Key is Key.Down or Key.Up
            && AccessList.Items.Count > 0)
        {
            AccessList.SelectedIndex = e.Key == Key.Down ? 0 : AccessList.Items.Count - 1;
            _ = AccessList.ItemContainerGenerator.ContainerFromIndex(AccessList.SelectedIndex)
                is ListBoxItem item && item.Focus();
            e.Handled = true;
        }
    }
}
