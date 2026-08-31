using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class MappingReferenceWatcherTests
{
    [Fact]
    public void MappingWatchDirectories_UsesDistinctParentsOfMappedItems()
    {
        var firstParent = Path.Combine(Path.GetTempPath(), "Podo.MappingWatch", "first");
        var secondParent = Path.Combine(Path.GetTempPath(), "Podo.MappingWatch", "second");
        var boxId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var items = new[]
        {
            CreateMappingItem(boxId, Path.Combine(firstParent, "a.txt"), now),
            CreateMappingItem(boxId, Path.Combine(firstParent, "b.txt"), now),
            CreateMappingItem(boxId, Path.Combine(secondParent, "folder"), now)
        };

        var directories = DesktopBoxManager.GetMappingWatchDirectories(items);

        Assert.Equal(2, directories.Count);
        Assert.Contains(Path.GetFullPath(firstParent), directories);
        Assert.Contains(Path.GetFullPath(secondParent), directories);
    }

    private static DrawerItem CreateMappingItem(
        Guid boxId,
        string sourcePath,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            boxId,
            Path.GetFileName(sourcePath),
            ItemKind.File,
            sourcePath,
            StoredPath: null,
            SortOrder: 0,
            CreatedAt: now,
            UpdatedAt: now);
}
