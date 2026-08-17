namespace WitchDrawer.Core.Models;

public sealed record ProjectDetails(
    Guid BoxId,
    ProjectStage Stage,
    string OwnerName,
    string Description,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedLaunchAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
