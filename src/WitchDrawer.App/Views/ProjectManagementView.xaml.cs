using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Views;

public partial class ProjectManagementView : UserControl
{
    public ProjectManagementView()
    {
        InitializeComponent();
    }

    private void OnRenameProjectClicked(object sender, RoutedEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        ProjectRenameTextBox.Text = mainWindow?.ViewModel.SelectedBox?.Name ?? string.Empty;
        ProjectRenamePopup.IsOpen = true;
        Dispatcher.InvokeAsync(() =>
        {
            ProjectRenameTextBox.Focus();
            Keyboard.Focus(ProjectRenameTextBox);
            ProjectRenameTextBox.SelectAll();
        });
        e.Handled = true;
    }

    private void OnConfirmProjectRenameClicked(object sender, RoutedEventArgs e)
    {
        var name = ProjectRenameTextBox.Text;
        var mainWindow = Window.GetWindow(this) as MainWindow;
        ProjectRenamePopup.IsOpen = false;
        if (mainWindow?.ViewModel.RenameSelectedBoxCommand.CanExecute(name) == true)
        {
            mainWindow.ViewModel.RenameSelectedBoxCommand.Execute(name);
        }
        e.Handled = true;
    }

    private void OnCancelProjectRenameClicked(object sender, RoutedEventArgs e)
    {
        ProjectRenamePopup.IsOpen = false;
        e.Handled = true;
    }

    private void OnProjectRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnConfirmProjectRenameClicked(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            OnCancelProjectRenameClicked(sender, e);
        }
    }

    private void OnOpenProjectBoxActionsMenu(object sender, RoutedEventArgs e)
    {
        ProjectBoxActionsPopup.IsOpen = !ProjectBoxActionsPopup.IsOpen;
        e.Handled = true;
    }

    private void OnProjectBoxActionMenuItemClicked(object sender, RoutedEventArgs e)
    {
        ProjectBoxActionsPopup.IsOpen = false;
        e.Handled = true;
    }

    private void OnShowProjectDesktopBoxClicked(object sender, RoutedEventArgs e)
    {
        ProjectBoxActionsPopup.IsOpen = false;
        (Window.GetWindow(this) as MainWindow)?.ShowSelectedBoxOnDesktop();
        e.Handled = true;
    }

    private void OnDeleteProjectBoxClicked(object sender, RoutedEventArgs e)
    {
        ProjectBoxActionsPopup.IsOpen = false;
        ProjectDeleteConfirmPopup.IsOpen = true;
        e.Handled = true;
    }

    private void OnArchiveProjectBoxClicked(object sender, RoutedEventArgs e)
    {
        ProjectBoxActionsPopup.IsOpen = false;
        ProjectArchiveConfirmPopup.IsOpen = true;
        e.Handled = true;
    }

    private void OnCancelProjectArchiveClicked(object sender, RoutedEventArgs e)
    {
        ProjectArchiveConfirmPopup.IsOpen = false;
        e.Handled = true;
    }

    private async void OnConfirmProjectArchiveClicked(object sender, RoutedEventArgs e)
    {
        ProjectArchiveConfirmPopup.IsOpen = false;
        try
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                await mainWindow.ArchiveSelectedProjectAsync();
            }
        }
        catch
        {
            // MainWindow records unexpected command failures and preserves the current project view.
        }

        e.Handled = true;
    }

    private void OnCancelProjectDeleteClicked(object sender, RoutedEventArgs e)
    {
        ProjectDeleteConfirmPopup.IsOpen = false;
        e.Handled = true;
    }

    private async void OnConfirmProjectDeleteClicked(object sender, RoutedEventArgs e)
    {
        ProjectDeleteConfirmPopup.IsOpen = false;
        if (Window.GetWindow(this) is MainWindow mainWindow
            && mainWindow.ViewModel.DeleteSelectedBoxCommand.CanExecute(null))
        {
            await mainWindow.ViewModel.DeleteSelectedBoxCommand.ExecuteAsync(null);
        }

        e.Handled = true;
    }

    private async void OnStageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0
            || e.RemovedItems.Count == 0
            || DataContext is not ProjectManagementViewModel viewModel)
        {
            return;
        }

        if (viewModel.SaveProjectCommand.CanExecute(null))
        {
            await viewModel.SaveProjectCommand.ExecuteAsync(null);
        }
    }

    private async void OnIssueStateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0
            || e.RemovedItems.Count == 0
            || sender is not FrameworkElement { DataContext: ProjectIssueViewModel issue }
            || DataContext is not ProjectManagementViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedIssue = issue;
        if (viewModel.SaveIssueCommand.CanExecute(null))
        {
            await viewModel.SaveIssueCommand.ExecuteAsync(null);
        }
    }

    private async void OnNewModuleTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || sender is not TextBox textBox
            || DataContext is not ProjectManagementViewModel viewModel)
        {
            return;
        }

        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (!viewModel.AddModuleCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        await viewModel.AddModuleCommand.ExecuteAsync(null);
        textBox.Focus();
        Keyboard.Focus(textBox);
    }

}
