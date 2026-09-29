using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
namespace WitchDrawer.App.Views;

public partial class WeeklyPlanWindow : Window
{
    private readonly WeeklyPlanService _service;
    private readonly DispatcherTimer _saveTimer;
    private bool _locked;
    private bool _collapsed;
    private bool _restoring = true;
    private bool _closing;
    private bool _allowClose;
    private double _expandedHeight;
    public WeeklyPlanView PlanView { get; }
    public WeeklyPlanWindow(WeeklyPlanService service)
    {
        _service = service; InitializeComponent();
        var saved = service.State;
        Left = saved.Left; Top = saved.Top; Width = saved.Width; Height = saved.Height;
        _expandedHeight = Height; _locked = saved.IsLocked; _collapsed = saved.IsCollapsed;
        // If a monitor was disconnected, put the widget back on the primary work area.
        var virtualBounds = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!virtualBounds.IntersectsWith(new Rect(Left, Top, Width, 40))) { Left = SystemParameters.WorkArea.Left + 30; Top = SystemParameters.WorkArea.Top + 30; }
        PlanView = new WeeklyPlanView(service, compact: true); Root.Children.Add(PlanView);
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); try { await SaveAsync(IsVisible); } catch (Exception ex) { ShowSaveError(ex); } };
        LocationChanged += (_, _) => QueueSave(); SizeChanged += (_, _) => QueueSave();
        Loaded += (_, _) => { AppThemeManager.ApplyToWindow(this, WindowBackdropKind.Transient); ApplyPresentation(); _restoring = false; QueueSave(); };
        Activated += async (_, _) => { try { await service.RefreshProjectsAsync(); } catch (Exception ex) { PlanView.ToolTip = ex.Message; } };
        Closing += OnClosing;
        AppThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => { _saveTimer.Stop(); AppThemeManager.ThemeChanged -= OnThemeChanged; };
    }
    private void OnThemeChanged(object? sender, AppTheme theme) => AppThemeManager.ApplyToWindow(this, WindowBackdropKind.Transient);
    private void QueueSave()
    {
        if (_restoring || _closing) return;
        if (!_collapsed) _expandedHeight = Height;
        _saveTimer.Stop(); _saveTimer.Start();
    }
    private void DragHeader(object s, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null) { if (source is Button) return; source = VisualTreeHelper.GetParent(source); }
        if (!_locked && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void ApplyPresentation()
    {
        _restoring = true;
        LockButton.Content = _locked ? "解锁" : "锁定";
        CollapseButton.Content = _collapsed ? "展开" : "收起";
        Root.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        MinHeight = _collapsed ? 40 : 300;
        Height = _collapsed ? 42 : _expandedHeight;
        ResizeMode = _locked || _collapsed ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        _restoring = false;
    }
    private void ToggleLock(object s, RoutedEventArgs e) { _locked = !_locked; ApplyPresentation(); QueueSave(); }
    private void ToggleCollapse(object s, RoutedEventArgs e) { _collapsed = !_collapsed; ApplyPresentation(); QueueSave(); }
    private void CloseClick(object s, RoutedEventArgs e) => Close();
    private async void OnClosing(object? s, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; _saveTimer.Stop();
        try { await SaveAsync(false); _allowClose = true; Close(); }
        catch (Exception ex) { _closing = false; ShowSaveError(ex); }
    }
    public Task FlushAsync() { _saveTimer.Stop(); return SaveAsync(IsVisible); }
    private Task SaveAsync(bool visible) => _service.SaveWindowAsync(Left, Top, Width, _expandedHeight, _locked, _collapsed, visible);
    public void ForceClose() { _allowClose = true; Close(); }
    private void ShowSaveError(Exception ex) => AppDialog.Warn(this, "周计划窗口状态未能保存：" + ex.Message, "周计划");
}
