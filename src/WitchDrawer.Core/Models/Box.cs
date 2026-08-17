namespace WitchDrawer.Core.Models;

public sealed record Box(
    Guid Id,
    string Name,
    BoxType Type,
    string? StoragePath,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsArchived { get; init; }

    public DateTimeOffset? ArchivedAt { get; init; }
}

