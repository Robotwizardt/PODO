namespace WitchDrawer.Core.Models;

/// <summary>
/// A file box attached to a project box. Visibility is controlled from the
/// project surface and is independent from the box's own persisted position.
/// </summary>
public sealed record ProjectBoxLink(
    Guid ProjectBoxId,
    Guid LinkedBoxId,
    string LinkedBoxName,
    BoxType LinkedBoxType,
    bool IsVisible,
    ProjectAttachmentSide AttachmentSide,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
