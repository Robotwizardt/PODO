namespace WitchDrawer.Core.Models;

public enum ProjectAttachmentSide
{
    Right = 0,
    Left = 1,
    Top = 2,
    Bottom = 3
}

public static class ProjectAttachmentSideCatalog
{
    public static ProjectAttachmentSide Normalize(ProjectAttachmentSide side) =>
        Enum.IsDefined(typeof(ProjectAttachmentSide), side)
            ? side
            : ProjectAttachmentSide.Right;

    public static string GetLabel(ProjectAttachmentSide side) => Normalize(side) switch
    {
        ProjectAttachmentSide.Left => "左侧",
        ProjectAttachmentSide.Top => "上方",
        ProjectAttachmentSide.Bottom => "下方",
        _ => "右侧"
    };
}
