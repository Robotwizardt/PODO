using System.Windows;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class ProjectFolderInteractionTests
{
    [Fact]
    public void OpeningMemberPlacesProjectBelowItsFolder()
    {
        var folderBounds = new Rect(100, 100, 300, 200);
        var workArea = new Rect(0, 0, 1920, 1080);

        var placement = ProjectFolderInteraction.GetOpenedProjectPlacement(
            folderBounds,
            new Size(250, 180),
            workArea);

        Assert.Equal(new Rect(100, 312, 250, 180), placement);
    }

    [Fact]
    public void OpeningMemberKeepsItsFolderVisible()
    {
        var behavior = ProjectFolderInteraction.GetOpenBehavior(
            new Rect(100, 100, 300, 200),
            new Size(250, 180),
            new Rect(0, 0, 1920, 1080));

        Assert.True(behavior.KeepFolderVisible);
    }

    [Fact]
    public void OpeningAnotherMemberUsesNextEmptyPositionBelow()
    {
        var placement = ProjectFolderInteraction.GetOpenedProjectPlacement(
            new Rect(100, 100, 300, 200),
            new Size(250, 180),
            new Rect(0, 0, 1920, 1080),
            [new Rect(100, 312, 250, 180)]);

        Assert.Equal(new Rect(100, 504, 250, 180), placement);
    }

    [Fact]
    public void OpeningMemberAvoidsWholeProjectGroupIncludingVisibleAttachments()
    {
        var placement = ProjectFolderInteraction.GetOpenedProjectPlacement(
            new Rect(100, 100, 300, 200),
            new ProjectFolderMemberFootprint(
                new Size(250, 180),
                new Rect(0, 0, 250, 372)),
            new Rect(0, 0, 1920, 1400),
            [new Rect(100, 312, 250, 372)]);

        Assert.Equal(new Rect(100, 696, 250, 180), placement);
    }

    [Fact]
    public void VisibleAttachmentsOnEverySideExpandMemberFootprint()
    {
        var footprint = ProjectFolderInteraction.CreateMemberFootprint(
            new Rect(500, 300, 250, 180),
            [
                new Rect(348, 300, 140, 90),
                new Rect(762, 300, 140, 90),
                new Rect(500, 188, 160, 100),
                new Rect(500, 492, 180, 100)
            ]);

        Assert.Equal(new Size(250, 180), footprint.ProjectSize);
        Assert.Equal(new Rect(-152, -112, 554, 404), footprint.RelativeBounds);
    }

    [Fact]
    public void OpeningMemberKeepsTopAttachmentBelowItsFolder()
    {
        var placement = ProjectFolderInteraction.GetOpenedProjectPlacement(
            new Rect(100, 100, 300, 200),
            new ProjectFolderMemberFootprint(
                new Size(250, 180),
                new Rect(0, -112, 250, 292)),
            new Rect(0, 0, 1920, 1080));

        Assert.Equal(new Rect(100, 424, 250, 180), placement);
    }

    [Fact]
    public void OpeningMemberUsesNextColumnWhenDownwardSpaceIsFull()
    {
        var placement = ProjectFolderInteraction.GetOpenedProjectPlacement(
            new Rect(100, 50, 200, 150),
            new Size(250, 180),
            new Rect(0, 0, 900, 500),
            [new Rect(100, 212, 250, 180)]);

        Assert.Equal(new Rect(362, 212, 250, 180), placement);
    }

    [Fact]
    public void MovingOpenedProjectOutsideFolderKeepsMembership()
    {
        var shouldDetach = ProjectFolderInteraction.ShouldDetachMember(
            ProjectFolderDragOrigin.ProjectWindow,
            new Rect(100, 100, 300, 200),
            new Point(900, 700));

        Assert.False(shouldDetach);
    }

    [Theory]
    [InlineData(150, 150, false)]
    [InlineData(900, 700, true)]
    public void MemberCardOnlyDetachesWhenReleasedOutsideFolder(
        double releaseX,
        double releaseY,
        bool expected)
    {
        var shouldDetach = ProjectFolderInteraction.ShouldDetachMember(
            ProjectFolderDragOrigin.MemberCard,
            new Rect(100, 100, 300, 200),
            new Point(releaseX, releaseY));

        Assert.Equal(expected, shouldDetach);
    }

    [Fact]
    public void DraggingMemberOutsideDetachesAndShowsProject()
    {
        var outcome = ProjectFolderInteraction.GetDragOutcome(
            ProjectFolderDragOrigin.MemberCard,
            new Rect(100, 100, 300, 200),
            new Point(900, 700));

        Assert.Equal(ProjectFolderDragOutcome.DetachAndShowProject, outcome);
    }

    [Fact]
    public void DroppingMemberOnAnotherCardReordersInsideFolder()
    {
        var outcome = ProjectFolderInteraction.GetDragOutcome(
            ProjectFolderDragOrigin.MemberCard,
            new Rect(100, 100, 300, 200),
            new Point(180, 160),
            isOverAnotherMember: true);

        Assert.Equal(ProjectFolderDragOutcome.ReorderMember, outcome);
    }

    [Theory]
    [InlineData(false, false, ProjectFolderMemberClickAction.Open)]
    [InlineData(true, false, ProjectFolderMemberClickAction.Open)]
    [InlineData(false, true, ProjectFolderMemberClickAction.Open)]
    [InlineData(true, true, ProjectFolderMemberClickAction.Hide)]
    public void ClickingMemberTogglesOnlyAnOpenedVisibleProject(
        bool isOpenedFromFolder,
        bool isProjectVisible,
        ProjectFolderMemberClickAction expected)
    {
        var action = ProjectFolderInteraction.GetMemberClickAction(
            isOpenedFromFolder,
            isProjectVisible);

        Assert.Equal(expected, action);
    }
}
