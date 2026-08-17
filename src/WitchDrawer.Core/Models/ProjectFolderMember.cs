namespace WitchDrawer.Core.Models;

public sealed record ProjectFolderMember(
    Guid FolderBoxId,
    Guid ProjectBoxId,
    string ProjectName,
    ProjectStage Stage,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
