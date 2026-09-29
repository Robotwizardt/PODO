using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Views;

public partial class WeeklyPlanView : UserControl
{
    private readonly WeeklyPlanService _service;
    private readonly bool _compact;
    private DateOnly _monday = Monday(DateOnly.FromDateTime(DateTime.Today));
    private bool _rendering;
    private bool _queued;
    public event EventHandler? DesktopRequested;
    public WeeklyPlanView(WeeklyPlanService service, bool compact = false)
    {
        _service = service; _compact = compact;
        InitializeComponent();
        Heading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DesktopButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Filters.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Render();
    }
    public static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    private void OnLoaded(object s, RoutedEventArgs e) { _service.Changed += OnChanged; Render(); }
    private void OnUnloaded(object s, RoutedEventArgs e) => _service.Changed -= OnChanged;
    private void OnChanged(object? s, EventArgs e)
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _queued = false; if (IsLoaded) Render(); }));
    }
    public void Refresh() => Render();
    private void PreviousWeek(object s, RoutedEventArgs e) { _monday = _monday.AddDays(-7); Render(); }
    private void NextWeek(object s, RoutedEventArgs e) { _monday = _monday.AddDays(7); Render(); }
    private void CurrentWeek(object s, RoutedEventArgs e) { _monday = Monday(DateOnly.FromDateTime(DateTime.Today)); Render(); }
    private void FilterChanged(object s, SelectionChangedEventArgs e) { if (PlanGrid is not null && !_rendering) Render(); }
    private void OpenDesktop(object s, RoutedEventArgs e) => DesktopRequested?.Invoke(this, EventArgs.Empty);
    private void AddClick(object s, RoutedEventArgs e) => OpenEditor(null);
    private async void OpenEditor(WeeklyPlanEntry? entry)
    {
        try
        {
            await _service.RefreshProjectsAsync();
            var dialog = new WeeklyPlanAddWindow(_service, _monday, entry) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog(); Render();
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
    private static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static T Themed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    { element.SetResourceReference(property, key); return element; }
    private void Render()
    {
        _rendering = true;
        try
        {
            WeekLabel.Text = _compact ? $"{_monday:M.d} – {_monday.AddDays(6):M.d}" : $"{_monday:yyyy.M.d} – {_monday.AddDays(6):M.d}";
            var all = _service.GetTodoSources().ToDictionary(i => (i.PaperId, i.ItemId));
            var entries = _service.GetEntries(_monday);
            var selectedProject = (ProjectFilter.SelectedItem as WeeklyPlanProject)?.Id ?? "";
            var projectOptions = new List<WeeklyPlanProject> { new("", "全部项目", "#94A8BE"), new("none", "未关联项目", "#94A8BE") };
            projectOptions.AddRange(all.Values.Where(i => i.ProjectId is not null).GroupBy(i => i.ProjectId)
                .Select(g => new WeeklyPlanProject(g.Key!, g.First().ProjectName!, g.First().ProjectColor!)));
            ProjectFilter.ItemsSource = projectOptions;
            ProjectFilter.SelectedItem = projectOptions.FirstOrDefault(p => p.Id == selectedProject) ?? projectOptions[0];
            selectedProject = ((WeeklyPlanProject)ProjectFilter.SelectedItem).Id;
            var visible = entries.Where(e =>
            {
                var i = all[(e.PaperId, e.ItemId)];
                return (_compact || StatusFilter.SelectedIndex == 0 || (StatusFilter.SelectedIndex == 2) == i.IsCompleted)
                    && (_compact || selectedProject == "" || (selectedProject == "none" ? i.ProjectId is null : i.ProjectId == selectedProject));
            }).ToArray();
            PlanGrid.Children.Clear(); PlanGrid.ColumnDefinitions.Clear(); PlanGrid.RowDefinitions.Clear();
            PlanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            for (var day = 0; day < 7; day++) PlanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PlanGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            PlanGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
            PlanGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
            for (var d = 0; d < 7; d++)
            {
                var date = _monday.AddDays(d);
                var today = date == DateOnly.FromDateTime(DateTime.Today);
                var text = Themed(new TextBlock { Text = $"周{("一二三四五六日")[d]}  {date.Day}", FontSize = _compact ? 10 : 12, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, today ? "AccentBrush" : "TextPrimaryBrush");
                Place(text, 0, d + 1, today);
            }
            foreach (var period in Enum.GetValues<WeeklyPlanPeriod>())
            {
                var row = period == WeeklyPlanPeriod.Morning ? 1 : 2;
                Place(Themed(new TextBlock { Text = row == 1 ? "上\n午" : "下\n午", TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11 }, TextBlock.ForegroundProperty, "TextMutedBrush"), row, 0, false);
                for (var d = 0; d < 7; d++)
                {
                    var date = _monday.AddDays(d);
                    var panel = new StackPanel { Margin = new Thickness(4) };
                    var slot = visible.Where(e => e.Date == date && e.Period == period).ToArray();
                    foreach (var entry in slot) panel.Children.Add(TaskCard(entry, all[(entry.PaperId, entry.ItemId)]));
                    if (slot.Length == 0) panel.Children.Add(Themed(new TextBlock { Text = "—", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,22,0,0) }, TextBlock.ForegroundProperty, "TextMutedBrush"));
                    Place(panel, row, d + 1, date == DateOnly.FromDateTime(DateTime.Today));
                }
            }
            var done = entries.Count(e => all[(e.PaperId, e.ItemId)].IsCompleted);
            var percent = entries.Count == 0 ? 0 : (int)Math.Round(done * 100d / entries.Count);
            Summary.Text = $"{entries.Count} 项 · {done} 项已完成" + (_compact ? "" : "  ·  上午、下午各最多 3 项");
            Progress.Value = percent; ProgressText.Text = percent + "%";
        }
        finally { _rendering = false; }
    }
    private void Place(UIElement element, int row, int column, bool today)
    {
        var border = new Border { Child = element, BorderThickness = new Thickness(0,0,1,1) };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
        border.SetResourceReference(Border.BackgroundProperty, today ? "AccentSoftBrush" : "GlassInnerBrush");
        Grid.SetRow(border, row); Grid.SetColumn(border, column); PlanGrid.Children.Add(border);
    }
    private FrameworkElement TaskCard(WeeklyPlanEntry entry, WeeklyPlanSourceTodo item)
    {
        var color = Brush(item.ProjectColor ?? "#91A7C0");
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(17) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var check = new CheckBox { IsChecked = item.IsCompleted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0,4,0,0), ToolTip = item.IsCompleted ? "恢复待办" : "标记完成" };
        check.SetResourceReference(StyleProperty, "AppTaskCheckBoxStyle");
        System.Windows.Automation.AutomationProperties.SetName(check, "完成 " + item.Title);
        check.Click += (_, _) =>
        {
            try { _service.SetCompleted(entry.PaperId, entry.ItemId, check.IsChecked == true); ErrorText.Text = ""; }
            catch (Exception ex) { ErrorText.Text = ex.Message; }
            Render();
        };
        grid.Children.Add(check);
        var contents = new StackPanel();
        contents.Children.Add(new TextBlock { Text = item.Title, FontSize = _compact ? 10 : 11, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = _compact ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MaxHeight = _compact ? 16 : 32, TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null });
        contents.Children.Add(Themed(new TextBlock { Text = item.ProjectName ?? item.Label, FontSize = _compact ? 9 : 10,
            Margin = new Thickness(0,2,0,0), TextTrimming = TextTrimming.CharacterEllipsis }, TextBlock.ForegroundProperty, "TextMutedBrush"));
        var detail = new Button { Content = contents, Padding = new Thickness(0), Margin = new Thickness(0), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = item.Title + "\n" + item.Label + "\n点击查看详情或调整排期" };
        detail.SetResourceReference(StyleProperty, "GhostButtonStyle");
        detail.MinHeight = 0;
        detail.Click += (_, _) => OpenEditor(entry);
        Grid.SetColumn(detail, 1); grid.Children.Add(detail);
        var card = new Border { Child = grid, Padding = new Thickness(4), BorderBrush = color, BorderThickness = new Thickness(3,0,0,0),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0,0,0,4), Opacity = item.IsCompleted ? .6 : 1 };
        card.SetResourceReference(Border.BackgroundProperty, "GlassControlBrush");
        if (item.ProjectColor is null) card.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
        return card;
    }
}
