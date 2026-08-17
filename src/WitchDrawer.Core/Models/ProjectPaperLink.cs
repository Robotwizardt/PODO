namespace WitchDrawer.Core.Models;

/// <summary>
/// A PaperTodo desktop paper attached to a project surface. Paper content remains
/// owned by PaperTodo; PODO only stores the project relationship and placement.
/// </summary>
public sealed record ProjectPaperLink(
    Guid ProjectBoxId,
    string PaperId,
    ProjectAttachmentSide AttachmentSide,
    bool IsVisible,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
