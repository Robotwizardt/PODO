using System.IO;
using System.Xml.Linq;
using System.Windows.Markup;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Views;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

[Collection("WPF window tests")]
public sealed class WeeklyPlanPresentationTests
{
    [Fact]
    public void NativeViews_RenderAtMainAndDesktopSizes()
    {
        Exception? failure = null;
        var root = Path.Combine(Path.GetTempPath(), "PODO-WeeklyPlan-Render", Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            try
            {
                var source = new WeeklyPlanTestSource();
                using var service = new WeeklyPlanService(source, new WeeklyPlanStore(root),
                    () => Task.FromResult<IReadOnlyDictionary<string, WeeklyPlanProject>>(new Dictionary<string, WeeklyPlanProject>
                    { ["paper"] = new("podo", "PODO 2.0", "#54B8FF") }));
                service.InitializeAsync().GetAwaiter().GetResult();
                var monday = WeeklyPlanView.Monday(DateOnly.FromDateTime(DateTime.Today));
                for (var index = 0; index < source.Items.Count; index++)
                    service.AddExistingAsync("paper", source.Items[index].ItemId, monday.AddDays(index < 3 ? 2 : index), index < 3 ? WeeklyPlanPeriod.Morning : WeeklyPlanPeriod.Afternoon).GetAwaiter().GetResult();
                service.SetCompleted("paper", "1", true);
                var main = new WeeklyPlanView(service);
                Render(main, 720, 500, "weekly-plan-main.png");
                Assert.Equal(5, Find<CheckBox>(main).Count());
                var status = (ComboBox)main.FindName("StatusFilter");
                Assert.NotNull(status.Template.FindName("PART_Popup", status));
                Assert.Equal("全部状态", ((ComboBoxItem)status.SelectedItem).Content);
                var themedColor = Colors.MediumPurple;
                main.Resources["TextPrimaryBrush"] = new SolidColorBrush(themedColor);
                Assert.Equal(themedColor, ((SolidColorBrush)main.Foreground).Color);
                main.Resources["TextPrimaryBrush"] = LoadAppResources()["TextPrimaryBrush"];
                Assert.Contains(Find<TextBlock>(main), t => t.Text.Contains("5 项"));
                var compact = new WeeklyPlanView(service, true);
                Render(compact, 640, 340, "weekly-plan-desktop-content.png");
                Assert.Equal(5, Find<CheckBox>(compact).Count());
                var desktop = new WeeklyPlanWindow(service);
                try
                {
                    Render((FrameworkElement)desktop.Content, 660, 410, "weekly-plan-desktop.png");
                    ((Button)desktop.FindName("LockButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(ResizeMode.NoResize, desktop.ResizeMode);
                    ((Button)desktop.FindName("CollapseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(Visibility.Collapsed, ((Grid)desktop.FindName("Root")).Visibility);
                    ((Button)desktop.FindName("CollapseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    desktop.FlushAsync().GetAwaiter().GetResult();
                    Assert.True(service.State.IsLocked);
                    Assert.False(service.State.IsCollapsed);
                    Assert.Equal(410, service.State.Height);
                }
                finally { desktop.ForceClose(); }
                source.AddTodoItem("paper", "整理下一阶段目标");
                var add = new WeeklyPlanAddWindow(service, monday);
                try
                {
                    Render((FrameworkElement)add.Content, 510, 620, "weekly-plan-add.png");
                    var period = (ComboBox)add.FindName("Period");
                    Assert.NotNull(period.Template.FindName("PART_Popup", period));
                    period.SelectedIndex = 1;
                    Assert.Equal("下午", ((ComboBoxItem)period.SelectedItem).Content);
                    var day = (DatePicker)add.FindName("Day");
                    Assert.NotNull(day.Template.FindName("PART_Button", day));
                    var calendar = new Calendar { Style = day.CalendarStyle, SelectedDate = monday.ToDateTime(TimeOnly.MinValue), DisplayDate = monday.ToDateTime(TimeOnly.MinValue) };
                    Render(calendar, 270, 285, "weekly-plan-calendar.png");
                    Assert.Contains(Find<CalendarDayButton>(calendar), b => b.IsSelected);
                    calendar.DisplayMode = CalendarMode.Year;
                    Render(calendar, 270, 285, "weekly-plan-calendar-months.png");
                    var calendarItem = Find<CalendarItem>(calendar).Single();
                    Assert.Equal(Visibility.Visible, ((Grid)calendarItem.Template.FindName("PART_YearView", calendarItem)).Visibility);
                    ((RadioButton)add.FindName("NewMode")).IsChecked = true;
                    Render((FrameworkElement)add.Content, 510, 620, "weekly-plan-add-new.png");
                }
                finally { add.Close(); }
                var details = new WeeklyPlanAddWindow(service, monday, service.GetEntries(monday)[0]);
                try { Render((FrameworkElement)details.Content, 510, 620, "weekly-plan-details.png"); }
                finally { details.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "WPF rendering did not finish.");
        try { Assert.Null(failure); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Render(FrameworkElement element, int width, int height, string name)
    {
        if (!element.Resources.Contains("AppComboBoxStyle")) element.Resources = LoadAppResources();
        if (element.Parent is Window window) window.Resources = element.Resources;
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); Assert.True(stream.Length > 2000, name + " rendered an empty image (" + stream.Length + " bytes).");
        var directory = Environment.GetEnvironmentVariable("PODO_WEEKLY_PLAN_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name), stream.ToArray());
        }
    }
    private static ResourceDictionary LoadAppResources()
    {
        // Load the real shared controls without creating a second WPF Application in the test host.
        var app = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AppResources.xaml")).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(presentation + "ResourceDictionary", app.Attributes().Where(a => a.IsNamespaceDeclaration), app.Element(presentation + "Application.Resources")!.Elements());
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString().Replace("clr-namespace:WitchDrawer.App.Infrastructure", "clr-namespace:WitchDrawer.App.Infrastructure;assembly=PODO"));
    }
    private static IEnumerable<T> Find<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T typed) yield return typed;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(element, index))) yield return child;
    }
}

