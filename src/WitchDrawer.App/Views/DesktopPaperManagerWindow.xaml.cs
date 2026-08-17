using System.Windows;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Views;

public partial class DesktopPaperManagerWindow : Window
{
    public DesktopPaperManagerWindow(DesktopPaperManagerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        AppThemeManager.ThemeChanged += OnThemeChanged;
    }

    public DesktopPaperManagerViewModel ViewModel => (DesktopPaperManagerViewModel)DataContext;

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppThemeManager.ApplyToWindow(this);
        ViewModel.Refresh();
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        AppThemeManager.ApplyToWindow(this);
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    private void OnShowPaperClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DesktopPaperSummary paper })
        {
            ViewModel.ShowPaper(paper);
        }
    }

    private void OnDeletePaperClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DesktopPaperSummary paper })
        {
            return;
        }

        if (MessageBox.Show(
                $"“{paper.Title}”将被永久删除，无法恢复。是否继续？",
                "删除桌面便签",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            ViewModel.DeletePaper(paper);
        }
    }

    private void OnDeleteHiddenPapersClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HiddenPaperCount == 0)
        {
            return;
        }

        if (MessageBox.Show(
                $"将永久删除 {ViewModel.HiddenPaperCount} 张已隐藏的桌面便签，无法恢复。是否继续？",
                "清空已隐藏便签",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            ViewModel.DeleteHiddenPapers();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
