using System.IO;
using PaperTodo;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

[Collection("WPF window tests")]
public sealed class WeeklyPlanPaperTodoIntegrationTests
{
    [Fact]
    public void RealPaperTodoBridge_CreatesSynchronizesAndPersistsOriginalItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "PODO-WeeklyPlan-PaperTodo", Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previousController = AppController.Current;
            try
            {
                string itemId;
                using (var controller = new AppController(root, enableStandaloneTray: false, ownsApplicationLifetime: false))
                {
                    controller.State.Papers.Clear();
                    var paper = new PaperData { Id = "real-paper", Type = PaperTypes.Todo, Title = "真实待办", IsVisible = false };
                    controller.State.Papers.Add(paper);
                    using var host = new PaperTodoHost(controller, root, new TestLogger());
                    using var plan = new WeeklyPlanService(host, new WeeklyPlanStore(root));
                    plan.InitializeAsync().GetAwaiter().GetResult();
                    var notifications = 0;
                    plan.Changed += (_, _) => notifications++;
                    var created = host.AddTodoItem(paper.Id, "验证真实数据");
                    itemId = created.ItemId;
                    plan.AddExistingAsync(paper.Id, itemId, new(2026,9,16), WeeklyPlanPeriod.Morning).GetAwaiter().GetResult();
                    plan.SetCompleted(paper.Id, itemId, true);
                    Assert.True(paper.Items.Single(i => i.Id == itemId).Done);
                    paper.Items.Single(i => i.Id == itemId).Done = false;
                    paper.Items.Single(i => i.Id == itemId).Text = "在原纸片中修改";
                    controller.MarkDirty();
                    Assert.Equal("在原纸片中修改", plan.GetTodoSources().Single().Title);
                    Assert.False(plan.GetTodoSources().Single().IsCompleted);
                    Assert.True(notifications >= 3);
                    paper.IsArchived = true;
                    controller.MarkDirty();
                    Assert.Empty(plan.GetEntries(new(2026,9,14)));
                    paper.IsArchived = false;
                    controller.MarkDirty();
                    Assert.Single(plan.GetEntries(new(2026,9,14)));
                    controller.SaveNow(sync: true);
                }
                using var restoredController = new AppController(root, enableStandaloneTray: false, ownsApplicationLifetime: false);
                using var restoredHost = new PaperTodoHost(restoredController, root, new TestLogger());
                using var restoredPlan = new WeeklyPlanService(restoredHost, new WeeklyPlanStore(root));
                restoredPlan.InitializeAsync().GetAwaiter().GetResult();
                Assert.Equal(itemId, Assert.Single(restoredPlan.GetEntries(new(2026,9,14))).ItemId);
                Assert.Equal("在原纸片中修改", Assert.Single(restoredHost.GetTodoSources()).Title);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                // Disposing the controller initializes PaperWindow's cached templates.
                // Seal them on their owning STA before other window tests reuse them.
                foreach (var field in typeof(PaperWindow).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
                {
                    if (field.GetValue(null) is System.Windows.FrameworkTemplate { IsSealed: false } template && template.CheckAccess()) template.Seal();
                    if (field.GetValue(null) is System.Windows.Style { IsSealed: false } style && style.CheckAccess()) style.Seal();
                }
                typeof(AppController).GetProperty(nameof(AppController.Current))!.SetValue(null, previousController);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        try { Assert.Null(failure); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class TestLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(Exception exception, string message) { }
    }
}

