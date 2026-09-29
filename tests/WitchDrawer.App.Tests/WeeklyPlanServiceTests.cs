using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class WeeklyPlanServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PODO-WeeklyPlan-ServiceTests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Day = new(2026, 9, 16);

    [Fact]
    public async Task CapacityAndDuplicateChecks_DoNotCreateAnExtraSourceTodo()
    {
        var source = new WeeklyPlanTestSource();
        using var service = await Create(source);
        foreach (var item in source.Items.Take(3).ToArray()) await service.AddExistingAsync(item.PaperId, item.ItemId, Day, WeeklyPlanPeriod.Morning);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddNewAsync("paper", "must not exist", Day, WeeklyPlanPeriod.Morning));
        Assert.Equal(5, source.Items.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingAsync("paper", "1", Day.AddDays(1), WeeklyPlanPeriod.Afternoon));
        Assert.Equal(3, service.GetEntries(WeeklyPlanServiceTests.Day.AddDays(-2)).Count);
    }

    [Fact]
    public async Task MovingToFullSlot_PreservesTheOriginalSchedule()
    {
        var source = new WeeklyPlanTestSource();
        using var service = await Create(source);
        foreach (var item in source.Items.Take(3).ToArray()) await service.AddExistingAsync(item.PaperId, item.ItemId, Day, WeeklyPlanPeriod.Morning);
        await service.AddExistingAsync("paper", "4", Day, WeeklyPlanPeriod.Afternoon);
        var entry = service.GetEntries(Day.AddDays(-2)).Single(e => e.ItemId == "4");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveAsync(entry, Day, WeeklyPlanPeriod.Morning));
        Assert.Contains(entry, service.GetEntries(Day.AddDays(-2)));
    }

    [Fact]
    public async Task RestartAndCrossYearMove_KeepIdentity_AndCancelKeepsSource()
    {
        var source = new WeeklyPlanTestSource();
        using var service = await Create(source);
        await service.AddExistingAsync("paper", "1", new(2026, 12, 31), WeeklyPlanPeriod.Morning);
        var entry = Assert.Single(service.GetEntries(new(2026, 12, 28)));
        await service.MoveAsync(entry, new(2027, 1, 4), WeeklyPlanPeriod.Afternoon);
        using var reopened = await Create(source);
        var restored = Assert.Single(reopened.GetEntries(new(2027, 1, 4)));
        Assert.Equal("1", restored.ItemId);
        Assert.Equal(WeeklyPlanPeriod.Afternoon, restored.Period);
        await reopened.RemoveAsync(restored);
        Assert.Empty(reopened.GetEntries(new(2027, 1, 4)));
        Assert.Contains(source.Items, i => i.ItemId == "1");
        Assert.Contains(reopened.GetAvailableSources(), i => i.ItemId == "1");
    }

    [Fact]
    public async Task SourceRenameCompletionAndArchive_AreResolvedLive()
    {
        var source = new WeeklyPlanTestSource();
        using var service = await Create(source);
        await service.AddExistingAsync("paper", "1", Day, WeeklyPlanPeriod.Morning);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;
        source.Replace("1", item => item with { Title = "renamed", IsCompleted = true });
        Assert.Equal("renamed", service.GetTodoSources().Single(i => i.ItemId == "1").Title);
        service.SetCompleted("paper", "1", false);
        Assert.False(source.Items.Single(i => i.ItemId == "1").IsCompleted);
        Assert.True(notifications >= 2);
        source.Replace("1", item => item with { IsArchived = true });
        Assert.Empty(service.GetEntries(Day.AddDays(-2)));
        Assert.DoesNotContain(service.GetAvailableSources(), i => i.ItemId == "1");
    }

    [Fact]
    public async Task ArchivedAndDeletedSources_DoNotOccupyCapacity()
    {
        var source = new WeeklyPlanTestSource();
        using var service = await Create(source);
        foreach (var item in source.Items.Take(3).ToArray()) await service.AddExistingAsync(item.PaperId, item.ItemId, Day, WeeklyPlanPeriod.Morning);
        source.Replace("1", item => item with { IsArchived = true });
        source.Items.RemoveAll(i => i.ItemId == "2");
        await service.AddNewAsync("paper", "new", Day, WeeklyPlanPeriod.Morning);
        Assert.Equal(2, service.GetEntries(Day.AddDays(-2)).Count);
    }

    [Fact]
    public async Task SaveFailure_RollsBackNewSourceAndDoesNotCommitMemory()
    {
        var source = new WeeklyPlanTestSource();
        using var service = await Create(source);
        // A directory at the final filename makes replacement fail without affecting other data.
        Directory.CreateDirectory(Path.Combine(_root, "weekly-plan.json"));
        var failure = await Record.ExceptionAsync(() => service.AddNewAsync("paper", "rollback", Day, WeeklyPlanPeriod.Morning));
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Assert.Equal(5, source.Items.Count);
        Assert.Empty(service.GetEntries(Day.AddDays(-2)));
    }

    [Fact]
    public async Task ProjectMetadata_IsInheritedFromThePaperAndRefreshes()
    {
        var source = new WeeklyPlanTestSource();
        var project = new WeeklyPlanProject("project-box", "PODO 2.0", "#2F74C0");
        using var service = new WeeklyPlanService(source, new WeeklyPlanStore(_root),
            () => Task.FromResult<IReadOnlyDictionary<string, WeeklyPlanProject>>(new Dictionary<string, WeeklyPlanProject> { ["paper"] = project }));
        await service.InitializeAsync();
        Assert.All(service.GetTodoSources(), item => Assert.Equal("project-box", item.ProjectId));
        project = project with { Name = "PODO 3.0", Color = "#2F8F5B" };
        await service.RefreshProjectsAsync();
        Assert.All(service.GetTodoSources(), item => { Assert.Equal("PODO 3.0", item.ProjectName); Assert.Equal("#2F8F5B", item.ProjectColor); });
    }

    [Fact]
    public async Task ConcurrentAdds_AllowOnlyThreeAndPersistThatResult()
    {
        var source = new WeeklyPlanTestSource();
        using var service = await Create(source);
        var attempts = source.Items.Select(async item =>
        {
            try { await service.AddExistingAsync(item.PaperId, item.ItemId, Day, WeeklyPlanPeriod.Morning); return true; }
            catch (InvalidOperationException) { return false; }
        }).ToArray();
        Assert.Equal(3, (await Task.WhenAll(attempts)).Count(success => success));
        using var reopened = await Create(source);
        Assert.Equal(3, reopened.GetEntries(Day.AddDays(-2)).Count);
    }

    private async Task<WeeklyPlanService> Create(WeeklyPlanTestSource source)
    {
        var service = new WeeklyPlanService(source, new WeeklyPlanStore(_root));
        await service.InitializeAsync(); return service;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

internal sealed class WeeklyPlanTestSource : IWeeklyPlanTodoSource
{
    public List<WeeklyPlanSourceTodo> Items { get; } = Enumerable.Range(1, 5).Select(n =>
        new WeeklyPlanSourceTodo("paper", n.ToString(), "产品开发", new[] { "整理周计划需求", "确认桌面交互", "完善数据同步", "准备验收记录", "回顾本周工作" }[n-1], false, false)).ToList();
    public event EventHandler? TodoContentChanged;
    public IReadOnlyList<WeeklyPlanSourceTodo> GetTodoSources() => Items.ToArray();
    public IReadOnlyList<WeeklyPlanPaper> GetTodoPapers() => [new("paper", "产品开发")];
    public WeeklyPlanSourceTodo AddTodoItem(string paperId, string title)
    {
        var item = new WeeklyPlanSourceTodo(paperId, Guid.NewGuid().ToString(), "产品开发", title, false, false);
        Items.Add(item); TodoContentChanged?.Invoke(this, EventArgs.Empty); return item;
    }
    public bool RemoveWeeklyPlanCreatedTodo(string paperId, string itemId)
    {
        var removed = Items.RemoveAll(i => i.ItemId == itemId && i.PaperId == paperId) > 0;
        TodoContentChanged?.Invoke(this, EventArgs.Empty); return removed;
    }
    public bool SetTodoItemCompleted(string paperId, string itemId, bool completed)
    {
        if (!Items.Any(i => i.PaperId == paperId && i.ItemId == itemId)) return false;
        Replace(itemId, item => item with { IsCompleted = completed }); return true;
    }
    public void Replace(string id, Func<WeeklyPlanSourceTodo, WeeklyPlanSourceTodo> update)
    {
        var index = Items.FindIndex(i => i.ItemId == id); Items[index] = update(Items[index]);
        TodoContentChanged?.Invoke(this, EventArgs.Empty);
    }
}

