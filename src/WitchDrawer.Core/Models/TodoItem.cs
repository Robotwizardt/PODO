namespace WitchDrawer.Core.Models;

public sealed record TodoItem(
    Guid Id,
    Guid BoxId,
    string Title,
    bool IsCompleted,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt = null,
    bool IsArchived = false,
    DateTimeOffset? ArchivedAt = null);
