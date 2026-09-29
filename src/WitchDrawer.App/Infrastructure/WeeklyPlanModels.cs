namespace WitchDrawer.App.Infrastructure;

public enum WeeklyPlanPeriod { Morning, Afternoon }
public sealed record WeeklyPlanPaper(string Id, string Title);
public sealed record WeeklyPlanProject(string Id, string Name, string Color);
public sealed record WeeklyPlanSourceTodo(string PaperId, string ItemId, string Label, string Title,
    bool IsCompleted, bool IsArchived, string? ProjectId = null, string? ProjectName = null, string? ProjectColor = null);
public sealed record WeeklyPlanEntry(string PaperId, string ItemId, DateOnly Date, WeeklyPlanPeriod Period);

/// <summary>PaperTodo remains the sole owner of todo content and completion.</summary>
public interface IWeeklyPlanTodoSource
{
    event EventHandler? TodoContentChanged;
    IReadOnlyList<WeeklyPlanSourceTodo> GetTodoSources();
    IReadOnlyList<WeeklyPlanPaper> GetTodoPapers();
    WeeklyPlanSourceTodo AddTodoItem(string paperId, string title);
    bool RemoveWeeklyPlanCreatedTodo(string paperId, string itemId);
    bool SetTodoItemCompleted(string paperId, string itemId, bool completed);
}
public sealed class WeeklyPlanState
{
    public List<WeeklyPlanEntry> Entries { get; set; } = [];
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 660;
    public double Height { get; set; } = 410;
    public bool IsLocked { get; set; }
    public bool IsCollapsed { get; set; }
    public bool IsVisible { get; set; }
    public WeeklyPlanState Copy() => new()
    {
        Entries = [.. Entries], Left = Left, Top = Top, Width = Width, Height = Height,
        IsLocked = IsLocked, IsCollapsed = IsCollapsed, IsVisible = IsVisible
    };
}
