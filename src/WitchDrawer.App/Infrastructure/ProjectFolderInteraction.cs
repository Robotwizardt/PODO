using System.Windows;

namespace WitchDrawer.App.Infrastructure;

public static class ProjectFolderInteraction
{
    private const double OpenedProjectGap = 12;

    public static Rect GetOpenedProjectPlacement(
        Rect folderBounds,
        Size projectSize,
        Rect workArea,
        IReadOnlyCollection<Rect>? occupiedBounds = null) =>
        GetOpenedProjectPlacement(
            folderBounds,
            new ProjectFolderMemberFootprint(
                projectSize,
                new Rect(0, 0, projectSize.Width, projectSize.Height)),
            workArea,
            occupiedBounds);

    public static Rect GetOpenedProjectPlacement(
        Rect folderBounds,
        ProjectFolderMemberFootprint memberFootprint,
        Rect workArea,
        IReadOnlyCollection<Rect>? occupiedBounds = null)
    {
        var projectWidth = Math.Min(
            Math.Max(memberFootprint.ProjectSize.Width, 1),
            Math.Max(workArea.Width, 1));
        var projectHeight = Math.Min(
            Math.Max(memberFootprint.ProjectSize.Height, 1),
            Math.Max(workArea.Height, 1));
        var projectBounds = new Rect(0, 0, projectWidth, projectHeight);
        var relativeFootprint = memberFootprint.RelativeBounds.IsEmpty
            ? projectBounds
            : memberFootprint.RelativeBounds;
        relativeFootprint.Union(projectBounds);

        var minLeft = workArea.Left - relativeFootprint.Left;
        var maxLeft = workArea.Right - relativeFootprint.Right;
        var minTop = workArea.Top - relativeFootprint.Top;
        var maxTop = workArea.Bottom - relativeFootprint.Bottom;
        if (maxLeft < minLeft)
        {
            minLeft = workArea.Left;
            maxLeft = Math.Max(workArea.Left, workArea.Right - projectWidth);
        }

        if (maxTop < minTop)
        {
            minTop = workArea.Top;
            maxTop = Math.Max(workArea.Top, workArea.Bottom - projectHeight);
        }

        var left = Math.Clamp(folderBounds.Left, minLeft, maxLeft);
        var top = Math.Max(
            folderBounds.Bottom + OpenedProjectGap - relativeFootprint.Top,
            minTop);
        var occupied = occupiedBounds ?? [];
        var candidateLefts = new List<double> { left };
        foreach (var bounds in occupied.OrderBy(bounds => bounds.Left))
        {
            candidateLefts.Add(Math.Clamp(
                bounds.Right + OpenedProjectGap - relativeFootprint.Left,
                minLeft,
                maxLeft));
            candidateLefts.Add(Math.Clamp(
                bounds.Left - relativeFootprint.Right - OpenedProjectGap,
                minLeft,
                maxLeft));
        }

        foreach (var candidateLeft in candidateLefts.Distinct())
        {
            var candidateTop = top;
            while (candidateTop <= maxTop)
            {
                var candidate = relativeFootprint;
                candidate.Offset(candidateLeft, candidateTop);
                var blockingBottom = occupied
                    .Where(candidate.IntersectsWith)
                    .Select(bounds => bounds.Bottom)
                    .DefaultIfEmpty(double.NaN)
                    .Max();
                if (double.IsNaN(blockingBottom))
                {
                    return new Rect(
                        candidateLeft,
                        candidateTop,
                        projectWidth,
                        projectHeight);
                }

                candidateTop = blockingBottom + OpenedProjectGap - relativeFootprint.Top;
            }
        }

        return new Rect(left, maxTop, projectWidth, projectHeight);
    }

    public static ProjectFolderOpenBehavior GetOpenBehavior(
        Rect folderBounds,
        Size projectSize,
        Rect workArea,
        IReadOnlyCollection<Rect>? occupiedBounds = null) =>
        new(
            GetOpenedProjectPlacement(folderBounds, projectSize, workArea, occupiedBounds),
            KeepFolderVisible: true);

    public static bool ShouldDetachMember(
        ProjectFolderDragOrigin dragOrigin,
        Rect folderBounds,
        Point releasePoint) =>
        GetDragOutcome(dragOrigin, folderBounds, releasePoint)
        == ProjectFolderDragOutcome.DetachAndShowProject;

    public static ProjectFolderDragOutcome GetDragOutcome(
        ProjectFolderDragOrigin dragOrigin,
        Rect folderBounds,
        Point releasePoint,
        bool isOverAnotherMember = false)
    {
        if (dragOrigin != ProjectFolderDragOrigin.MemberCard)
        {
            return ProjectFolderDragOutcome.KeepMembership;
        }

        if (!folderBounds.Contains(releasePoint))
        {
            return ProjectFolderDragOutcome.DetachAndShowProject;
        }

        return isOverAnotherMember
            ? ProjectFolderDragOutcome.ReorderMember
            : ProjectFolderDragOutcome.KeepMembership;
    }

    public static ProjectFolderMemberClickAction GetMemberClickAction(
        bool isOpenedFromFolder,
        bool isProjectVisible) =>
        isOpenedFromFolder && isProjectVisible
            ? ProjectFolderMemberClickAction.Hide
            : ProjectFolderMemberClickAction.Open;

    public static ProjectFolderMemberFootprint CreateMemberFootprint(
        Rect projectBounds,
        IReadOnlyCollection<Rect> visibleAttachmentBounds)
    {
        var groupBounds = projectBounds;
        foreach (var attachmentBounds in visibleAttachmentBounds.Where(bounds => !bounds.IsEmpty))
        {
            groupBounds.Union(attachmentBounds);
        }

        groupBounds.Offset(-projectBounds.Left, -projectBounds.Top);
        return new ProjectFolderMemberFootprint(projectBounds.Size, groupBounds);
    }
}

public readonly record struct ProjectFolderOpenBehavior(
    Rect ProjectPlacement,
    bool KeepFolderVisible);

public readonly record struct ProjectFolderMemberFootprint(
    Size ProjectSize,
    Rect RelativeBounds);

public enum ProjectFolderDragOrigin
{
    ProjectWindow,
    MemberCard
}

public enum ProjectFolderDragOutcome
{
    KeepMembership,
    DetachAndShowProject,
    ReorderMember
}

public enum ProjectFolderMemberClickAction
{
    Open,
    Hide
}
