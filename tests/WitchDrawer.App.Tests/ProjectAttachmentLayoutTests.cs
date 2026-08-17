using System.Windows;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class ProjectAttachmentLayoutTests
{
    private static readonly Rect WorkArea = new(0, 0, 1600, 1000);
    private static readonly Rect ProjectBounds = new(500, 300, 280, 180);

    [Theory]
    [InlineData(430, 350, ProjectAttachmentSide.Left)]
    [InlineData(800, 350, ProjectAttachmentSide.Right)]
    [InlineData(550, 200, ProjectAttachmentSide.Top)]
    [InlineData(550, 510, ProjectAttachmentSide.Bottom)]
    public void DetermineProjectAttachmentSide_UsesDroppedWindowDirection(
        double left,
        double top,
        ProjectAttachmentSide expected)
    {
        var side = DesktopBoxManager.DetermineProjectAttachmentSide(
            ProjectBounds,
            new Rect(left, top, 120, 90));

        Assert.Equal(expected, side);
    }

    [Fact]
    public void GetProjectAttachmentPlacement_StacksItemsOnTheirAssignedSide()
    {
        var first = DesktopBoxManager.GetProjectAttachmentPlacement(
            ProjectBounds,
            new Size(140, 90),
            ProjectAttachmentSide.Bottom,
            0,
            WorkArea);
        var second = DesktopBoxManager.GetProjectAttachmentPlacement(
            ProjectBounds,
            new Size(140, 90),
            ProjectAttachmentSide.Bottom,
            1,
            WorkArea);

        Assert.True(first.Top >= ProjectBounds.Bottom);
        Assert.True(second.Left > first.Left);
        Assert.Equal(first.Top, second.Top);
    }

    [Fact]
    public void GetProjectAttachmentPlacement_WrapsLargeSideCollectionsInsideWorkArea()
    {
        var placements = Enumerable.Range(0, 14)
            .Select(index => DesktopBoxManager.GetProjectAttachmentPlacement(
                ProjectBounds,
                new Size(260, 210),
                ProjectAttachmentSide.Right,
                index,
                WorkArea))
            .ToArray();

        Assert.All(placements, placement =>
        {
            Assert.True(placement.Left >= WorkArea.Left);
            Assert.True(placement.Top >= WorkArea.Top);
            Assert.True(placement.Right <= WorkArea.Right);
            Assert.True(placement.Bottom <= WorkArea.Bottom);
        });
        Assert.True(placements.Select(placement => placement.Top).Distinct().Count() > 1);
        Assert.True(placements.Select(placement => placement.Left).Distinct().Count() > 1);
    }

    [Fact]
    public void GetProjectAttachmentPlacement_KeepsFiveLaptopSizedRightAttachmentsNonOverlapping()
    {
        var laptopWorkArea = new Rect(0, 0, 1024, 768);
        var laptopProjectBounds = new Rect(350, 300, 310, 190);
        var placements = Enumerable.Range(0, 5)
            .Select(index => DesktopBoxManager.GetProjectAttachmentPlacement(
                laptopProjectBounds,
                new Size(300, 190),
                ProjectAttachmentSide.Right,
                index,
                laptopWorkArea))
            .ToArray();

        var overlappingPair = (from first in placements.Select((placement, index) => (placement, index))
                               from second in placements.Select((placement, index) => (placement, index))
                               where first.index < second.index
                                  && first.placement.IntersectsWith(second.placement)
                               select (
                                   FirstIndex: first.index,
                                   SecondIndex: second.index,
                                   First: first.placement,
                                   Second: second.placement))
            .FirstOrDefault();

        Assert.True(
            overlappingPair == default,
            $"Attachments {overlappingPair.FirstIndex} and {overlappingPair.SecondIndex} overlap: "
            + $"{overlappingPair.First} / {overlappingPair.Second}");
    }

    [Fact]
    public void GetProjectAttachmentPlacement_KeepsMixedWidthBottomAttachmentsNonOverlapping()
    {
        var first = DesktopBoxManager.GetProjectAttachmentPlacement(
            ProjectBounds,
            new Size(400, 200),
            ProjectAttachmentSide.Bottom,
            0,
            WorkArea);
        var second = DesktopBoxManager.GetProjectAttachmentPlacement(
            ProjectBounds,
            new Size(100, 200),
            ProjectAttachmentSide.Bottom,
            1,
            WorkArea,
            occupiedBounds: [first]);

        Assert.False(
            first.IntersectsWith(second),
            $"A wide first attachment and narrow second attachment overlap: {first} / {second}");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void ShouldUnlinkProjectAttachment_RequiresExplicitBlankDrop(
        bool hasProjectDropTarget,
        bool isExplicitUnlinkRequested,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxManager.ShouldUnlinkProjectAttachment(
                hasProjectDropTarget,
                isExplicitUnlinkRequested));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ShouldAutoPlaceProjectAttachment_SkipsManuallyMovedAttachments(
        bool isManuallyPositioned,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxManager.ShouldAutoPlaceProjectAttachment(isManuallyPositioned));
    }

    [Fact]
    public void IsProjectAttachmentDropCandidate_AcceptsNearbyDropAndRejectsFarDrop()
    {
        Assert.True(DesktopBoxManager.IsProjectAttachmentDropCandidate(
            ProjectBounds,
            new Rect(760, 340, 90, 90)));
        Assert.False(DesktopBoxManager.IsProjectAttachmentDropCandidate(
            ProjectBounds,
            new Rect(1000, 700, 90, 90)));
    }

    [Fact]
    public void ResolveProjectAttachmentDropBounds_UsesCapturedPointerAtDrop()
    {
        var droppedBounds = new Rect(740, 330, 120, 90);
        var capturedDropPoint = new Point(780, 375);

        var resolved = DesktopBoxManager.ResolveProjectAttachmentDropBounds(
            droppedBounds,
            capturedDropPoint);

        Assert.Equal(new Rect(capturedDropPoint, new Size(1, 1)), resolved);
    }
}
