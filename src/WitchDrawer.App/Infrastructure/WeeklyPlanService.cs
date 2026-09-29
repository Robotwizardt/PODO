using System.IO;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Infrastructure;

/// <summary>One shared scheduling session for the control center and desktop window.</summary>
public sealed class WeeklyPlanService : IDisposable
{
    private readonly IWeeklyPlanTodoSource _source;
    private readonly WeeklyPlanStore _store;
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private readonly Func<Task<IReadOnlyDictionary<string, WeeklyPlanProject>>>? _projectLoader;
    private IReadOnlyDictionary<string, WeeklyPlanProject> _projects = new Dictionary<string, WeeklyPlanProject>();
    private WeeklyPlanState _state = new();
    private bool _initialized;
    public WeeklyPlanService(IWeeklyPlanTodoSource source, WeeklyPlanStore store,
        Func<Task<IReadOnlyDictionary<string, WeeklyPlanProject>>>? projectLoader = null)
    {
        _source = source; _store = store; _projectLoader = projectLoader;
        _source.TodoContentChanged += SourceChanged;
    }
    public event EventHandler? Changed;
    public WeeklyPlanState State => _state.Copy();
    public async Task InitializeAsync()
    {
        await _mutations.WaitAsync();
        try
        {
            if (_initialized) return;
            var loaded = await _store.LoadAsync<WeeklyPlanState>();
            if (loaded.Entries is null || loaded.Entries.Any(e => e is null || string.IsNullOrWhiteSpace(e.PaperId) || string.IsNullOrWhiteSpace(e.ItemId) || !Enum.IsDefined(e.Period))
                || loaded.Entries.GroupBy(e => (e.PaperId, e.ItemId)).Any(g => g.Count() > 1)
                || loaded.Entries.GroupBy(e => (e.Date, e.Period)).Any(g => g.Count() > 3))
                throw new InvalidDataException("周计划数据格式不正确，原文件已保留。请检查 weekly-plan.json。");
            if (!double.IsFinite(loaded.Left) || !double.IsFinite(loaded.Top)) { loaded.Left = 80; loaded.Top = 100; }
            loaded.Width = double.IsFinite(loaded.Width) ? Math.Clamp(loaded.Width, 560, 2400) : 660;
            loaded.Height = double.IsFinite(loaded.Height) ? Math.Clamp(loaded.Height, 300, 1600) : 380;
            _state = loaded; _initialized = true;
        }
        finally { _mutations.Release(); }
        await RefreshProjectsAsync();
    }
    public async Task RefreshProjectsAsync()
    {
        if (_projectLoader is not null) _projects = await _projectLoader();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public IReadOnlyList<WeeklyPlanPaper> GetPapers() => _source.GetTodoPapers();
    public IReadOnlyList<WeeklyPlanSourceTodo> GetTodoSources() => _source.GetTodoSources().Select(item =>
        _projects.TryGetValue(item.PaperId, out var project)
            ? item with { ProjectId = project.Id, ProjectName = project.Name, ProjectColor = project.Color } : item).ToArray();
    public WeeklyPlanProject? GetProject(string paperId) => _projects.GetValueOrDefault(paperId);
    public IReadOnlyList<WeeklyPlanEntry> GetEntries(DateOnly monday) => ValidEntries().Where(e => e.Date >= monday && e.Date <= monday.AddDays(6)).ToArray();
    public IReadOnlyList<WeeklyPlanSourceTodo> GetAvailableSources()
    {
        var scheduled = ValidEntries().Select(e => (e.PaperId, e.ItemId)).ToHashSet();
        return GetTodoSources().Where(i => !i.IsArchived && !i.IsCompleted && !scheduled.Contains((i.PaperId, i.ItemId)))
            .OrderBy(i => i.Label).ThenBy(i => i.Title).ToArray();
    }
    private List<WeeklyPlanEntry> ValidEntries()
    {
        var keys = _source.GetTodoSources().Where(i => !i.IsArchived).Select(i => (i.PaperId, i.ItemId)).ToHashSet();
        return _state.Entries.Where(e => keys.Contains((e.PaperId, e.ItemId))).ToList();
    }
    private void EnsureReady() { if (!_initialized) throw new InvalidOperationException("周计划尚未准备完成。"); }
    private static void EnsureCapacity(IEnumerable<WeeklyPlanEntry> entries, DateOnly date, WeeklyPlanPeriod period)
    {
        if (!Enum.IsDefined(period)) throw new ArgumentOutOfRangeException(nameof(period));
        if (entries.Count(e => e.Date == date && e.Period == period) >= 3)
            throw new InvalidOperationException("这个时段已安排 3 项，请选择其他时段。");
    }
    public async Task AddExistingAsync(string paperId, string itemId, DateOnly date, WeeklyPlanPeriod period)
    {
        await _mutations.WaitAsync();
        try
        {
            EnsureReady();
            var item = _source.GetTodoSources().FirstOrDefault(i => i.PaperId == paperId && i.ItemId == itemId);
            if (item is null || item.IsArchived || item.IsCompleted) throw new InvalidOperationException("该待办已完成、归档或删除，请重新选择。");
            var next = _state.Copy(); next.Entries = ValidEntries();
            if (next.Entries.Any(e => e.PaperId == paperId && e.ItemId == itemId)) throw new InvalidOperationException("该待办已经排入周计划。");
            EnsureCapacity(next.Entries, date, period);
            next.Entries.Add(new(paperId, itemId, date, period));
            await CommitAsync(next);
        }
        finally { _mutations.Release(); }
    }
    public async Task AddNewAsync(string paperId, string title, DateOnly date, WeeklyPlanPeriod period)
    {
        await _mutations.WaitAsync();
        try
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 240) throw new InvalidOperationException("请输入 1–240 字的待办内容。");
            if (!_source.GetTodoPapers().Any(p => p.Id == paperId)) throw new InvalidOperationException("该待办标签已不存在或已归档。");
            var next = _state.Copy(); next.Entries = ValidEntries(); EnsureCapacity(next.Entries, date, period);
            var created = _source.AddTodoItem(paperId, title.Trim());
            next.Entries.Add(new(paperId, created.ItemId, date, period));
            try { await CommitAsync(next); }
            catch { _source.RemoveWeeklyPlanCreatedTodo(paperId, created.ItemId); throw; }
        }
        finally { _mutations.Release(); }
    }
    public async Task MoveAsync(WeeklyPlanEntry entry, DateOnly date, WeeklyPlanPeriod period)
    {
        await _mutations.WaitAsync();
        try
        {
            EnsureReady(); var next = _state.Copy(); next.Entries = ValidEntries();
            if (next.Entries.RemoveAll(e => e.PaperId == entry.PaperId && e.ItemId == entry.ItemId) == 0) throw new InvalidOperationException("该排期已失效。");
            EnsureCapacity(next.Entries, date, period);
            next.Entries.Add(entry with { Date = date, Period = period }); await CommitAsync(next);
        }
        finally { _mutations.Release(); }
    }
    public async Task RemoveAsync(WeeklyPlanEntry entry)
    {
        await _mutations.WaitAsync();
        try
        {
            EnsureReady(); var next = _state.Copy();
            next.Entries.RemoveAll(e => e.PaperId == entry.PaperId && e.ItemId == entry.ItemId);
            await CommitAsync(next);
        }
        finally { _mutations.Release(); }
    }
    public void SetCompleted(string paperId, string itemId, bool completed)
    {
        if (!_source.SetTodoItemCompleted(paperId, itemId, completed)) throw new InvalidOperationException("该待办已不存在或已归档。");
    }
    public async Task SaveWindowAsync(double left, double top, double width, double height, bool locked, bool collapsed, bool visible)
    {
        await _mutations.WaitAsync();
        try
        {
            EnsureReady(); var next = _state.Copy();
            next.Left = left; next.Top = top; next.Width = width; next.Height = height;
            next.IsLocked = locked; next.IsCollapsed = collapsed; next.IsVisible = visible;
            await CommitAsync(next, notify: false);
        }
        finally { _mutations.Release(); }
    }
    private async Task CommitAsync(WeeklyPlanState next, bool notify = true)
    {
        await _store.SaveAsync(next); _state = next;
        if (notify) Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task WaitForPendingChangesAsync()
    {
        await _mutations.WaitAsync();
        _mutations.Release();
    }
    private void SourceChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() => _source.TodoContentChanged -= SourceChanged;
}
