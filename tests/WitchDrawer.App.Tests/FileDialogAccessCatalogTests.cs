using System.IO;
using WitchDrawer.App.FileDialogAccess;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class FileDialogAccessCatalogTests
{
    [Fact]
    public void CreateEntries_ShowsOnlyUnarchivedBoxesWithAUniquePhysicalDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var existingPath = Path.Combine(root, "existing");
        Directory.CreateDirectory(existingPath);

        try
        {
            var boxes = new[]
            {
                CreateBox("normal", BoxType.Normal, existingPath, 3),
                CreateBox("legacy pixel", BoxType.Pixel, Path.Combine(root, "missing"), 1),
                CreateBox("drawer", BoxType.Drawer, existingPath, 2),
                CreateBox("target", BoxType.Bound, existingPath, 0),
                CreateBox("mapping", BoxType.Mapping, existingPath, 4),
                CreateBox("project", BoxType.Project, existingPath, 5),
                CreateBox("project folder", BoxType.ProjectFolder, existingPath, 6),
                CreateBox("todo", BoxType.Todo, existingPath, 7),
                CreateBox("note", BoxType.Note, existingPath, 8),
                CreateBox("no path", BoxType.Normal, null, 9),
                CreateBox("archived", BoxType.Normal, existingPath, 10) with { IsArchived = true }
            };

            var entries = FileDialogAccessCatalog.CreateEntries(boxes);

            Assert.Collection(
                entries,
                entry => AssertEntry(entry, "target", isAvailable: true),
                entry => AssertEntry(entry, "legacy pixel", isAvailable: false),
                entry => AssertEntry(entry, "drawer", isAvailable: true),
                entry => AssertEntry(entry, "normal", isAvailable: true));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Search_MatchesBoxNameTypeAndPhysicalPathWithoutChangingSourceOrder()
    {
        var entries = new[]
        {
            new FileDialogAccessEntry(Guid.NewGuid(), "发票", BoxType.Normal, @"D:\Work\Bills", true, null),
            new FileDialogAccessEntry(Guid.NewGuid(), "素材", BoxType.Drawer, @"D:\Assets", true, null),
            new FileDialogAccessEntry(Guid.NewGuid(), "交付", BoxType.Bound, @"E:\Client\Release", true, null)
        };

        Assert.Equal(new[] { "发票", "素材", "交付" },
            FileDialogAccessCatalog.Search(entries, "").Select(entry => entry.Name));
        Assert.Equal(new[] { "素材" },
            FileDialogAccessCatalog.Search(entries, "抽屉").Select(entry => entry.Name));
        Assert.Equal(new[] { "交付" },
            FileDialogAccessCatalog.Search(entries, "client").Select(entry => entry.Name));
        Assert.Equal(new[] { "发票" },
            FileDialogAccessCatalog.Search(entries, "发票").Select(entry => entry.Name));
    }

    [Fact]
    public void CreateEntries_MalformedPhysicalPathKeepsTheBoxDisabled()
    {
        var entry = Assert.Single(FileDialogAccessCatalog.CreateEntries(
            [CreateBox("旧盒", BoxType.Normal, "\0", 0)]));

        Assert.False(entry.IsAvailable);
        Assert.Equal("目录不可用", entry.StatusText);
    }

    private static Box CreateBox(
        string name,
        BoxType type,
        string? storagePath,
        int sortOrder)
    {
        var now = DateTimeOffset.UtcNow;
        return new Box(Guid.NewGuid(), name, type, storagePath, sortOrder, now, now);
    }

    private static void AssertEntry(
        FileDialogAccessEntry entry,
        string expectedName,
        bool isAvailable)
    {
        Assert.Equal(expectedName, entry.Name);
        Assert.Equal(isAvailable, entry.IsAvailable);
        Assert.Equal(isAvailable ? null : "目录不可用", entry.StatusText);
    }
}
