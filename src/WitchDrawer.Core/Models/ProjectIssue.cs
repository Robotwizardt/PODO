namespace WitchDrawer.Core.Models;

public enum ProjectSolutionState
{
    None = 0,
    Proposed = 1,
    Confirmed = 2
}

public enum ProjectResolutionState
{
    Open = 0,
    InProgress = 1,
    Verifying = 2,
    Resolved = 3,

    // ProjectIssues is the persisted legacy name. The project-box UI now presents
    // these values as a lightweight module checklist without changing saved data.
    NotDeveloped = Open,
    DevelopmentCompleted = InProgress,
    Released = Resolved
}

public enum ProjectPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public sealed record ProjectIssue(
    Guid Id,
    Guid ProjectBoxId,
    string Title,
    string Description,
    ProjectSolutionState SolutionState,
    string SolutionText,
    ProjectResolutionState ResolutionState,
    ProjectResolutionState? PreviousResolutionState,
    ProjectPriority Priority,
    string AssigneeName,
    DateTimeOffset? DueAt,
    DateTimeOffset? ResolvedAt,
    string ResolvedBy,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectSolutionStateOption(
    ProjectSolutionState Value,
    string Name);

public sealed record ProjectResolutionStateOption(
    ProjectResolutionState Value,
    string Name);

public sealed record ProjectPriorityOption(
    ProjectPriority Value,
    string Name);

public static class ProjectIssueCatalog
{
    public static IReadOnlyList<ProjectSolutionStateOption> SolutionStates { get; } =
    [
        new(ProjectSolutionState.None, "无方案"),
        new(ProjectSolutionState.Proposed, "已有方案"),
        new(ProjectSolutionState.Confirmed, "方案已确认")
    ];

    public static IReadOnlyList<ProjectResolutionStateOption> ResolutionStates { get; } =
    [
        new(ProjectResolutionState.NotDeveloped, "未开发"),
        new(ProjectResolutionState.DevelopmentCompleted, "开发完成"),
        new(ProjectResolutionState.Released, "上线完成")
    ];

    public static IReadOnlyList<ProjectPriorityOption> Priorities { get; } =
    [
        new(ProjectPriority.Low, "低"),
        new(ProjectPriority.Normal, "普通"),
        new(ProjectPriority.High, "高"),
        new(ProjectPriority.Urgent, "紧急")
    ];

    public static string GetSolutionStateLabel(ProjectSolutionState state) =>
        SolutionStates.FirstOrDefault(item => item.Value == state)?.Name ?? "无方案";

    public static ProjectResolutionState NormalizeModuleState(ProjectResolutionState state) => state switch
    {
        ProjectResolutionState.Verifying => ProjectResolutionState.DevelopmentCompleted,
        ProjectResolutionState.Open or ProjectResolutionState.InProgress or ProjectResolutionState.Resolved => state,
        _ => ProjectResolutionState.NotDeveloped
    };

    public static string GetResolutionStateLabel(ProjectResolutionState state) =>
        ResolutionStates.FirstOrDefault(item => item.Value == NormalizeModuleState(state))?.Name
        ?? "未开发";

    public static string GetPriorityLabel(ProjectPriority priority) =>
        Priorities.FirstOrDefault(item => item.Value == priority)?.Name ?? "普通";
}
