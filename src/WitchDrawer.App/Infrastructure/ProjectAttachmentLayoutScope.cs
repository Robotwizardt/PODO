using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// Limits an attachment-layout pass to the projects whose presentation changed.
/// A missing project id represents the intentionally global refresh used at startup.
/// </summary>
internal readonly record struct ProjectAttachmentLayoutScope(Guid? ProjectBoxId)
{
    public static ProjectAttachmentLayoutScope All { get; } = new(null);

    public static ProjectAttachmentLayoutScope ForProject(Guid projectBoxId) =>
        new(projectBoxId);

    public bool Includes(Box box) =>
        box.Type == BoxType.Project
        && (ProjectBoxId is null || box.Id == ProjectBoxId.Value);
}
