using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;
namespace WitchDrawer.App.Views;

public partial class WeeklyPlanAddWindow : Window
{
    private readonly WeeklyPlanService _service;
    private readonly WeeklyPlanEntry? _entry;
    private bool _busy;
    public WeeklyPlanAddWindow(WeeklyPlanService service, DateOnly monday, WeeklyPlanEntry? entry = null)
    {
        _service = service; _entry = entry; InitializeComponent();
        Source.ItemsSource = service.GetAvailableSources(); Source.SelectedIndex = 0;
        Paper.ItemsSource = service.GetPapers(); Paper.SelectedIndex = 0;
        Day.SelectedDate = (entry?.Date ?? monday).ToDateTime(TimeOnly.MinValue);
        Period.SelectedIndex = entry?.Period == WeeklyPlanPeriod.Afternoon ? 1 : 0;
        if (entry is not null)
        {
            Heading.Text = "待办详情与排期";
            ModePanel.Visibility = ExistingPanel.Visibility = NewPanel.Visibility = Visibility.Collapsed;
            DetailsPanel.Visibility = DetailActions.Visibility = Visibility.Visible;
            AcceptButton.Content = "保存排期";
            var item = service.GetTodoSources().FirstOrDefault(i => i.PaperId == entry.PaperId && i.ItemId == entry.ItemId);
            DetailTitle.Text = item?.Title ?? "来源待办已不存在";
            DetailLabel.Text = "来源标签：" + item?.Label;
            ToggleButton.Content = item?.IsCompleted == true ? "恢复待办" : "标记完成";
        }
        UpdateHint();
        Loaded += (_, _) => AppThemeManager.ApplyToWindow(this, WindowBackdropKind.Transient);
        AppThemeManager.ThemeChanged += OnThemeChanged;
    }
    private void OnThemeChanged(object? sender, AppTheme theme) => AppThemeManager.ApplyToWindow(this, WindowBackdropKind.Transient);
    protected override void OnClosed(EventArgs e)
    { AppThemeManager.ThemeChanged -= OnThemeChanged; base.OnClosed(e); }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void ModeChanged(object s, RoutedEventArgs e)
    {
        if (NewPanel is null) return;
        NewPanel.Visibility = NewMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ExistingPanel.Visibility = NewMode.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        UpdateHint();
    }
    private void SourceChanged(object s, SelectionChangedEventArgs e) { if (ProjectHint is not null) UpdateHint(); }
    private void UpdateHint()
    {
        var paperId = _entry?.PaperId ?? (NewMode.IsChecked == true ? (Paper.SelectedItem as WeeklyPlanPaper)?.Id : (Source.SelectedItem as WeeklyPlanSourceTodo)?.PaperId);
        var project = paperId is null ? null : _service.GetProject(paperId);
        ProjectHint.Text = project is null ? "未关联项目收纳盒" : "关联项目：" + project.Name;
        if (_entry is null && _service.GetPapers().Count == 0) ProjectHint.Text = "还没有待办标签，请先从 PODO 新建一张桌面待办纸片。";
        else if (_entry is null && NewMode.IsChecked != true && Source.Items.Count == 0) ProjectHint.Text = "没有可排期的现有待办。可以切换到“新建待办并排期”。";
    }
    private async void Accept(object s, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (Day.SelectedDate is not DateTime day) throw new InvalidOperationException("请选择日期。");
        var date = DateOnly.FromDateTime(day);
        var period = Period.SelectedIndex == 1 ? WeeklyPlanPeriod.Afternoon : WeeklyPlanPeriod.Morning;
        if (_entry is not null) await _service.MoveAsync(_entry, date, period);
        else if (NewMode.IsChecked == true)
        {
            if (Paper.SelectedItem is not WeeklyPlanPaper paper) throw new InvalidOperationException("请选择待办标签。");
            await _service.AddNewAsync(paper.Id, NewTitle.Text, date, period);
        }
        else
        {
            if (Source.SelectedItem is not WeeklyPlanSourceTodo item) throw new InvalidOperationException("请选择一条未完成待办。");
            await _service.AddExistingAsync(item.PaperId, item.ItemId, date, period);
        }
    });
    private async void Remove(object s, RoutedEventArgs e) => await RunAsync(() => _entry is null ? Task.CompletedTask : _service.RemoveAsync(_entry));
    private async void Toggle(object s, RoutedEventArgs e) => await RunAsync(() =>
    {
        var item = _service.GetTodoSources().FirstOrDefault(i => i.PaperId == _entry?.PaperId && i.ItemId == _entry?.ItemId)
            ?? throw new InvalidOperationException("该待办已不存在。");
        _service.SetCompleted(item.PaperId, item.ItemId, !item.IsCompleted);
        return Task.CompletedTask;
    });
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; IsEnabled = false;
        try { await action(); DialogResult = true; }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
        finally { _busy = false; IsEnabled = true; }
    }
}
