namespace WitchDrawer.Core.Models;

public enum ProjectStage
{
    Research = 0,
    Design = 1,
    Development = 2,
    Acceptance = 3,
    Launched = 4,
    Maintenance = 5,
    Paused = 6
}

public sealed record ProjectStageOption(
    ProjectStage Value,
    string Name,
    string Color);

public static class ProjectStageCatalog
{
    /// <summary>
    /// The first stage assigned to a new project. The selected stage is persisted
    /// on ProjectDetails, so every project can override this default independently.
    /// </summary>
    public const ProjectStage DefaultStage = ProjectStage.Research;

    public static IReadOnlyList<ProjectStageOption> Options { get; } =
    [
        new(ProjectStage.Research, "调研", "#52738F"),
        new(ProjectStage.Design, "方案设计", "#D9822B"),
        new(ProjectStage.Development, "执行开发", "#2F74C0"),
        new(ProjectStage.Acceptance, "验收", "#8B52B5"),
        new(ProjectStage.Launched, "上线", "#2F8F5B"),
        new(ProjectStage.Maintenance, "运行维护", "#178A83"),
        new(ProjectStage.Paused, "暂停", "#6F7782")
    ];

    public static ProjectStageOption Get(ProjectStage stage)
    {
        return Options.FirstOrDefault(option => option.Value == stage)
            ?? Options[0];
    }
}
