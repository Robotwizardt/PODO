using System.Text.Json;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class WeeklyPlanStoreAcceptanceTests : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PODO-WeeklyPlan-Acceptance", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndReopen_PreservesScheduleAndDesktopGeometry()
    {
        var store = new WeeklyPlanStore(_root);
        var original = new SavedPlan
        {
            Entries = [new("paper-1", "todo-1", new DateOnly(2026, 12, 31), "Afternoon")],
            Left = -500, Top = 123, Width = 700, Height = 380, IsLocked = true
        };
        await store.SaveAsync(original);
        var restored = await new WeeklyPlanStore(_root).LoadAsync<SavedPlan>();
        Assert.Equal(original.Entries, restored.Entries);
        Assert.Equal(original.Left, restored.Left);
        Assert.Equal(original.Top, restored.Top);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.True(restored.IsLocked);
    }

    [Fact]
    public async Task MalformedData_IsReportedAndPreserved()
    {
        Directory.CreateDirectory(_root);
        var store = new WeeklyPlanStore(_root);
        const string malformed = "{\"Entries\":[broken";
        await File.WriteAllTextAsync(store.Path, malformed);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync<SavedPlan>());
        Assert.Equal(malformed, await File.ReadAllTextAsync(store.Path));
    }

    [Fact]
    public async Task CancelledSave_DoesNotReplaceExistingSchedule()
    {
        var store = new WeeklyPlanStore(_root);
        await store.SaveAsync(new SavedPlan { Entries = [new("p", "existing", new(2026, 9, 16), "Morning")] });
        var original = await File.ReadAllBytesAsync(store.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new SavedPlan(), cts.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(store.Path));
    }

    [Fact]
    public async Task ConcurrentSaves_LeaveACompleteReadableDocument()
    {
        var store = new WeeklyPlanStore(_root);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => store.SaveAsync(new SavedPlan
        {
            Entries = [new("paper", "todo-" + index, new(2026, 9, 16), "Morning")]
        })));
        var result = await new WeeklyPlanStore(_root).LoadAsync<SavedPlan>();
        Assert.Single(result.Entries);
        Assert.StartsWith("todo-", result.Entries[0].ItemId);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(store.Path));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    public sealed record SavedEntry(string PaperId, string ItemId, DateOnly Date, string Period);
    public sealed class SavedPlan
    {
        public List<SavedEntry> Entries { get; set; } = [];
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsLocked { get; set; }
    }
}
