using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerServiceTests
{
    [Fact]
    public async Task InitializeAsync_CreatesDefaultBoxes()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var boxes = await workspace.Service.GetBoxesAsync();

        Assert.Contains(boxes, box => box.Type == BoxType.Normal && box.Name == "普通收纳盒");
        Assert.Contains(boxes, box => box.Type == BoxType.Mapping && box.Name == "映射收纳盒");
    }

    [Fact]
    public async Task ReorderBoxesAsync_PersistsCompleteOrder()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        await workspace.Service.CreateBoxAsync("third", BoxType.Normal);
        var original = await workspace.Service.GetBoxesAsync();
        var expectedIds = original.Select(box => box.Id).Reverse().ToArray();

        await workspace.Service.ReorderBoxesAsync(expectedIds);

        var reordered = await workspace.Service.GetBoxesAsync();
        Assert.Equal(expectedIds, reordered.Select(box => box.Id));
        Assert.Equal(Enumerable.Range(0, expectedIds.Length), reordered.Select(box => box.SortOrder));
    }

    [Fact]
    public async Task ReorderBoxesAsync_RejectsIncompleteOrDuplicateOrder()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var boxes = await workspace.Service.GetBoxesAsync();
        var originalIds = boxes.Select(box => box.Id).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.ReorderBoxesAsync([originalIds[0], originalIds[0]]));
        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.ReorderBoxesAsync([originalIds[0]]));

        Assert.Equal(originalIds, (await workspace.Service.GetBoxesAsync()).Select(box => box.Id));
    }

    [Fact]
    public void EnsureCreatedAndWritable_SucceedsOnWritableDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreatedAndWritable();

            Assert.True(Directory.Exists(paths.RootDirectory));
            Assert.True(Directory.Exists(paths.BoxesDirectory));
            Assert.True(Directory.Exists(paths.LogsDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ForCurrentUser_UsesEnvironmentOverrideWhenConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariableName, root);

            var paths = AppPaths.ForCurrentUser();

            Assert.Equal(Path.GetFullPath(root), paths.RootDirectory);
            Assert.True(Directory.Exists(paths.RootDirectory));
            Assert.Equal(Path.Combine(paths.RootDirectory, AppPaths.DatabaseFileName), paths.DatabasePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariableName, previous);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_WhenDatabaseDirectoryIsNotWritable_ThrowsWithPathContext()
    {
        // 将“目录”做成文件，使 CreateDirectory / SQLite 打开必然失败。
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "not-a-directory");
        var blockedDatabasePath = Path.Combine(root, AppPaths.DatabaseFileName);

        try
        {
            var repository = new DrawerRepository(blockedDatabasePath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.InitializeAsync());

            Assert.Contains(AppPaths.DataDirectoryEnvironmentVariableName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(blockedDatabasePath, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(root))
            {
                File.Delete(root);
            }
        }
    }

    [Fact]
    public async Task ImportPathAsync_NormalBoxMovesFileIntoStorage()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "report.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.False(File.Exists(source));
        Assert.NotNull(item.StoredPath);
        Assert.True(File.Exists(item.StoredPath));
        Assert.Equal(source, item.SourcePath);
        Assert.Equal("report.txt", item.DisplayName);
        Assert.Single(storedItems);
    }

    [Fact]
    public async Task ImportPathAsync_PersistsGridPosition()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "grid.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source, 2, 3);
        var storedItem = await workspace.Repository.GetItemAsync(item.Id);

        Assert.Equal(2, item.GridColumn);
        Assert.Equal(3, item.GridRow);
        Assert.NotNull(storedItem);
        Assert.Equal(2, storedItem.GridColumn);
        Assert.Equal(3, storedItem.GridRow);
    }

    [Fact]
    public async Task UpdateItemGridPositionAsync_PersistsGridPosition()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reposition.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source, 0, 0);

        await workspace.Service.UpdateItemGridPositionAsync(item.Id, 4, 5);
        var storedItem = await workspace.Repository.GetItemAsync(item.Id);

        Assert.NotNull(storedItem);
        Assert.Equal(4, storedItem.GridColumn);
        Assert.Equal(5, storedItem.GridRow);
    }

    [Fact]
    public async Task SetSettingAsync_PersistsAndUpdatesValue()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        await workspace.Service.SetSettingAsync("Theme", "Moe");
        await workspace.Service.SetSettingAsync("Theme", "Crystal");
        var value = await workspace.Service.GetSettingAsync("Theme");

        Assert.Equal("Crystal", value);
    }

    [Fact]
    public async Task ImportPathAsync_PixelBoxMovesFileIntoStorage()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-p", "pixelart.png", "hello");
        var pixelBox = await workspace.Service.CreateBoxAsync("像素收纳盒 1", BoxType.Pixel);

        var item = await workspace.Service.ImportPathAsync(pixelBox.Id, source);
        var storedItems = await workspace.Repository.GetItemsAsync(pixelBox.Id);

        Assert.False(File.Exists(source));
        Assert.NotNull(item.StoredPath);
        Assert.True(File.Exists(item.StoredPath));
        Assert.Equal(source, item.SourcePath);
        Assert.Equal("pixelart.png", item.DisplayName);
        Assert.Single(storedItems);
    }

    [Fact]
    public async Task DeleteBoxAsync_PixelBoxRestoresItemsToOriginalLocationsAndRemovesItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-p", "boxedpixel.txt", "hello");
        var pixelBox = await workspace.Service.CreateBoxAsync("像素收纳盒 1", BoxType.Pixel);
        var item = await workspace.Service.ImportPathAsync(pixelBox.Id, source);
        var storedPath = item.StoredPath!;

        await workspace.Service.DeleteBoxAsync(pixelBox.Id);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(pixelBox.Id);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(storedPath));
        Assert.DoesNotContain(boxes, box => box.Id == pixelBox.Id);
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task DrawerBox_UsesStoredFileSafetyAndRestoresOnDelete()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("drawer-source", "drawer-item.txt", "hello");
        var drawerBox = await workspace.Service.CreateBoxAsync("抽屉盒 1", BoxType.Drawer);

        var imported = await workspace.Service.ImportPathAsync(drawerBox.Id, source);

        Assert.NotNull(drawerBox.StoragePath);
        Assert.NotNull(imported.StoredPath);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(imported.StoredPath));
        Assert.StartsWith(
            Path.GetFullPath(workspace.Paths.BoxesDirectory),
            Path.GetFullPath(imported.StoredPath!),
            StringComparison.OrdinalIgnoreCase);

        var result = await workspace.Service.DeleteBoxAsync(drawerBox.Id);

        Assert.True(result.BoxRemoved);
        Assert.Equal(1, result.RestoredCount);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(imported.StoredPath));
    }

    [Fact]
    public async Task ImportPathAsync_MappingBoxKeepsSourceFileInPlace()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);

        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        Assert.True(File.Exists(source));
        Assert.Equal(source, item.SourcePath);
        Assert.Null(item.StoredPath);
        Assert.Equal("reference.txt", item.DisplayName);
    }

    [Fact]
    public async Task GetItemsAsync_MappingBoxRemovesDeletedReference()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("mapping-delete", "obsolete.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        File.Delete(source);
        var items = await workspace.Service.GetItemsAsync(mappingBox.Id);

        Assert.DoesNotContain(items, candidate => candidate.Id == item.Id);
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
    }

    [Fact]
    public async Task SynchronizeExternalRenameAsync_MappingBoxUpdatesReferencedPath()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("mapping-rename", "before.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);
        var renamed = Path.Combine(Path.GetDirectoryName(source)!, "after.txt");
        File.Move(source, renamed);

        var synchronized = await workspace.Service.SynchronizeExternalRenameAsync(
            mappingBox.Id,
            source,
            renamed);

        Assert.True(synchronized);
        var updated = Assert.Single(await workspace.Service.GetItemsAsync(mappingBox.Id));
        Assert.Equal(item.Id, updated.Id);
        Assert.Equal("after.txt", updated.DisplayName);
        Assert.Equal(renamed, updated.SourcePath);
        Assert.Null(updated.StoredPath);
        Assert.True(File.Exists(renamed));
    }

    [Fact]
    public async Task ImportPathAsync_TodoBoxRejectsFileWithoutMovingIt()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var todoBox = await workspace.Service.CreateBoxAsync("todo", BoxType.Todo);
        var sourcePath = workspace.CreateSourceFile("todo-source", "keep.txt", "content");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.ImportPathAsync(todoBox.Id, sourcePath));

        Assert.True(File.Exists(sourcePath));
        Assert.Empty(await workspace.Service.GetItemsAsync(todoBox.Id));
    }

    [Fact]
    public async Task ImportPathAsync_NormalBoxAddsSuffixForConflictingNames()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var first = workspace.CreateSourceFile("source-a", "report.txt", "one");
        var second = workspace.CreateSourceFile("source-b", "report.txt", "two");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        var firstItem = await workspace.Service.ImportPathAsync(normalBox.Id, first);
        var secondItem = await workspace.Service.ImportPathAsync(normalBox.Id, second);

        Assert.Equal("report.txt", firstItem.DisplayName);
        Assert.Equal("report (1).txt", secondItem.DisplayName);
        Assert.True(File.Exists(firstItem.StoredPath));
        Assert.True(File.Exists(secondItem.StoredPath));
    }

    [Fact]
    public async Task MoveItemToBoxAsync_NormalBoxMovesStoredFileAndPersistsGridPosition()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "move-me.txt", "hello");
        var sourceBox = await workspace.GetBoxAsync(BoxType.Normal);
        var targetBox = await workspace.Service.CreateBoxAsync("target", BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(sourceBox.Id, source, 0, 0);
        var oldStoredPath = item.StoredPath!;

        await workspace.Service.MoveItemToBoxAsync(item.Id, targetBox.Id, 2, 3);
        var movedItem = await workspace.Repository.GetItemAsync(item.Id);
        var sourceItems = await workspace.Repository.GetItemsAsync(sourceBox.Id);

        Assert.NotNull(movedItem);
        Assert.Equal(targetBox.Id, movedItem.BoxId);
        Assert.Equal(source, movedItem.SourcePath);
        Assert.Equal("move-me.txt", movedItem.DisplayName);
        Assert.Equal(2, movedItem.GridColumn);
        Assert.Equal(3, movedItem.GridRow);
        Assert.False(File.Exists(oldStoredPath));
        Assert.NotNull(movedItem.StoredPath);
        Assert.True(File.Exists(movedItem.StoredPath));
        Assert.Empty(sourceItems);
        Assert.StartsWith(Path.GetFullPath(targetBox.StoragePath!), Path.GetFullPath(movedItem.StoredPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveItemToBoxAsync_NormalBoxAddsSuffixForConflictingTargetName()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var first = workspace.CreateSourceFile("source-a", "report.txt", "one");
        var second = workspace.CreateSourceFile("source-b", "report.txt", "two");
        var sourceBox = await workspace.GetBoxAsync(BoxType.Normal);
        var targetBox = await workspace.Service.CreateBoxAsync("target", BoxType.Normal);

        var existingItem = await workspace.Service.ImportPathAsync(targetBox.Id, first);
        var movingItem = await workspace.Service.ImportPathAsync(sourceBox.Id, second);

        await workspace.Service.MoveItemToBoxAsync(movingItem.Id, targetBox.Id, 1, 1);
        var movedItem = await workspace.Repository.GetItemAsync(movingItem.Id);

        Assert.NotNull(movedItem);
        Assert.Equal("report.txt", existingItem.DisplayName);
        Assert.Equal("report (1).txt", movedItem.DisplayName);
        Assert.True(File.Exists(existingItem.StoredPath));
        Assert.NotNull(movedItem.StoredPath);
        Assert.True(File.Exists(movedItem.StoredPath));
    }

    [Fact]
    public async Task MoveItemToBoxAsync_StoredItemToMappingBoxIsRejected()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "stored.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MoveItemToBoxAsync(item.Id, mappingBox.Id, 1, 1));

        var storedItem = await workspace.Repository.GetItemAsync(item.Id);
        Assert.NotNull(storedItem);
        Assert.Equal(normalBox.Id, storedItem.BoxId);
        Assert.True(File.Exists(item.StoredPath));
    }

    [Fact]
    public async Task MoveItemToBoxAsync_MappingBoxMovesReferenceWithoutTouchingSourceFile()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var sourceBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var targetBox = await workspace.Service.CreateBoxAsync("target-map", BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(sourceBox.Id, source, 0, 0);

        await workspace.Service.MoveItemToBoxAsync(item.Id, targetBox.Id, 2, 4);
        var movedItem = await workspace.Repository.GetItemAsync(item.Id);

        Assert.NotNull(movedItem);
        Assert.Equal(targetBox.Id, movedItem.BoxId);
        Assert.Equal(source, movedItem.SourcePath);
        Assert.Null(movedItem.StoredPath);
        Assert.Equal(2, movedItem.GridColumn);
        Assert.Equal(4, movedItem.GridRow);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task MoveItemToBoxAsync_MappingItemToStorageBoxIsRejected()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MoveItemToBoxAsync(item.Id, normalBox.Id, 1, 1));

        var storedItem = await workspace.Repository.GetItemAsync(item.Id);
        Assert.NotNull(storedItem);
        Assert.Equal(mappingBox.Id, storedItem.BoxId);
        Assert.Null(storedItem.StoredPath);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ExportItemToDirectoryAsync_NormalBoxMovesStoredFileAndRemovesItem()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "export-me.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var oldStoredPath = item.StoredPath!;
        var exportDirectory = Path.Combine(workspace.Root, "desktop");

        var exportedPath = await workspace.Service.ExportItemToDirectoryAsync(item.Id, exportDirectory);
        var remainingItem = await workspace.Repository.GetItemAsync(item.Id);

        Assert.Equal(Path.Combine(exportDirectory, "export-me.txt"), exportedPath);
        Assert.True(File.Exists(exportedPath));
        Assert.False(File.Exists(oldStoredPath));
        Assert.Null(remainingItem);
    }

    [Fact]
    public async Task ExportItemToDirectoryAsync_AddsSuffixForConflictingName()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "export-me.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var exportDirectory = Path.Combine(workspace.Root, "desktop");
        Directory.CreateDirectory(exportDirectory);
        File.WriteAllText(Path.Combine(exportDirectory, "export-me.txt"), "existing");

        var exportedPath = await workspace.Service.ExportItemToDirectoryAsync(item.Id, exportDirectory);

        Assert.Equal(Path.Combine(exportDirectory, "export-me (1).txt"), exportedPath);
        Assert.True(File.Exists(exportedPath));
    }

    [Fact]
    public async Task ExportItemToDirectoryAsync_MappingItemIsRejected()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);
        var exportDirectory = Path.Combine(workspace.Root, "desktop");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.ExportItemToDirectoryAsync(item.Id, exportDirectory));

        Assert.True(File.Exists(source));
        Assert.NotNull(await workspace.Repository.GetItemAsync(item.Id));
    }

    [Fact]
    public async Task GetItemsAsync_NormalBoxRemovesMissingStoredItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "moved-out.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var exportedPath = Path.Combine(workspace.Root, "exported", "moved-out.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(exportedPath)!);

        File.Move(item.StoredPath!, exportedPath);
        var items = await workspace.Service.GetItemsAsync(normalBox.Id);
        var storedItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.True(File.Exists(exportedPath));
        Assert.Empty(items);
        Assert.Empty(storedItems);
    }

    [Fact]
    public async Task DeleteItemAsync_NormalBoxRestoresItemToOriginalLocationAndRemovesItem()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "delete-me.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;

        var result = await workspace.Service.DeleteItemAsync(item.Id);
        var remainingItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.True(result.WasStoredItem);
        Assert.True(result.RestoredToOriginal);
        Assert.False(result.RestoredToDesktop);
        Assert.Equal(source, result.RestoredPath);
        Assert.True(File.Exists(source));
        Assert.Equal("hello", File.ReadAllText(source));
        Assert.False(File.Exists(storedPath));
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task DeleteItemAsync_NormalBoxAddsSuffixWhenOriginalPathAlreadyExists()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "conflict.txt", "stored");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;
        File.WriteAllText(source, "existing");

        var result = await workspace.Service.DeleteItemAsync(item.Id);
        var restoredPath = Path.Combine(Path.GetDirectoryName(source)!, "conflict (1).txt");

        Assert.True(result.RestoredToOriginal);
        Assert.Equal(restoredPath, result.RestoredPath);
        Assert.Equal("existing", File.ReadAllText(source));
        Assert.True(File.Exists(restoredPath));
        Assert.Equal("stored", File.ReadAllText(restoredPath));
        Assert.False(File.Exists(storedPath));
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
    }

    [Fact]
    public async Task DeleteItemAsync_FallsBackToDesktopWhenOriginalDirectoryMissing()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-missing", "orphan.txt", "hello");
        var sourceDirectory = Path.GetDirectoryName(source)!;
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;
        Directory.Delete(sourceDirectory, recursive: true);

        var result = await workspace.Service.DeleteItemAsync(item.Id);

        try
        {
            Assert.True(result.WasStoredItem);
            Assert.False(result.RestoredToOriginal);
            Assert.True(result.RestoredToDesktop);
            Assert.False(string.IsNullOrWhiteSpace(result.RestoredPath));
            Assert.True(File.Exists(result.RestoredPath));
            Assert.Equal("hello", File.ReadAllText(result.RestoredPath!));
            Assert.StartsWith(
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                Path.GetFullPath(result.RestoredPath!),
                StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(storedPath));
            Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(result.RestoredPath) && File.Exists(result.RestoredPath))
            {
                File.Delete(result.RestoredPath);
            }
        }
    }

    [Fact]
    public async Task DeleteItemAsync_MappingBoxOnlyRemovesReference()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        var result = await workspace.Service.DeleteItemAsync(item.Id);

        Assert.False(result.WasStoredItem);
        Assert.True(File.Exists(source));
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Contains("引用", result.StatusMessage);
    }

    [Fact]
    public async Task DeleteBoxAsync_NormalBoxRestoresItemsToOriginalLocationsAndRemovesItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "boxed.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;

        var result = await workspace.Service.DeleteBoxAsync(normalBox.Id);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.True(result.BoxRemoved);
        Assert.Equal(1, result.RestoredCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(File.Exists(source));
        Assert.Equal("hello", File.ReadAllText(source));
        Assert.False(File.Exists(storedPath));
        Assert.DoesNotContain(boxes, box => box.Id == normalBox.Id);
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task DeleteBoxAsync_KeepsBoxWhenAnyRestoreFails()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var keepSource = workspace.CreateSourceFile("source-keep", "keep.txt", "keep");
        var failSource = workspace.CreateSourceFile("source-fail", "fail.txt", "fail");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var keepItem = await workspace.Service.ImportPathAsync(normalBox.Id, keepSource);
        var failItem = await workspace.Service.ImportPathAsync(normalBox.Id, failSource);

        // Remove the stored file so restore throws FileNotFoundException for this item.
        File.Delete(failItem.StoredPath!);

        var result = await workspace.Service.DeleteBoxAsync(normalBox.Id);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.False(result.BoxRemoved);
        Assert.Equal(1, result.RestoredCount);
        Assert.Equal(1, result.FailedCount);
        // 失败消息要带首条明细（项目名），否则用户反馈时无法定位是哪一项。
        Assert.Contains("fail.txt", result.StatusMessage);
        Assert.Contains(boxes, box => box.Id == normalBox.Id);
        Assert.True(File.Exists(keepSource));
        Assert.False(File.Exists(keepItem.StoredPath));
        Assert.Single(remainingItems);
        Assert.Equal(failItem.Id, remainingItems[0].Id);
    }

    [Fact]
    public async Task DeleteBoxAsync_MappingBoxOnlyRemovesReferences()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        var result = await workspace.Service.DeleteBoxAsync(mappingBox.Id);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(mappingBox.Id);

        Assert.True(result.BoxRemoved);
        Assert.True(File.Exists(source));
        Assert.DoesNotContain(boxes, box => box.Id == mappingBox.Id);
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task RenameBoxAsync_RejectsEmptyName()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.RenameBoxAsync(normalBox.Id, "   "));
    }

    [Fact]
    public async Task RepositoryMutations_RejectMissingRows()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var missing = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.UpdateBoxNameAsync(missing, "missing"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.RemoveBoxAsync(missing));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.UpdateItemGridPositionAsync(missing, 1, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.RemoveItemAsync(missing));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.UpdateTodoCompletionAsync(missing, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.RemoveTodoAsync(missing));
    }

    [Fact]
    public async Task ImportPathAsync_DirectoryMovesIntoStorage()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var sourceDir = workspace.CreateSourceDirectory("folder-a", "nested.txt", "payload");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        var item = await workspace.Service.ImportPathAsync(normalBox.Id, sourceDir);

        Assert.False(Directory.Exists(sourceDir));
        Assert.NotNull(item.StoredPath);
        Assert.True(Directory.Exists(item.StoredPath));
        Assert.True(File.Exists(Path.Combine(item.StoredPath!, "nested.txt")));
    }

    [Theory]
    [InlineData(BoxType.Normal, ItemKind.Directory, "新建文件夹")]
    [InlineData(BoxType.Normal, ItemKind.File, "新建文本文档.txt")]
    [InlineData(BoxType.Bound, ItemKind.Directory, "新建文件夹")]
    [InlineData(BoxType.Bound, ItemKind.File, "新建文本文档.txt")]
    public async Task CreateFileSystemItemAsync_CreatesAndIndexesItem(
        BoxType boxType,
        ItemKind itemKind,
        string desiredName)
    {
        using var workspace = await TestWorkspace.CreateAsync();
        Box box;
        if (boxType == BoxType.Bound)
        {
            var boundFolder = Path.Combine(workspace.Root, "create-target");
            Directory.CreateDirectory(boundFolder);
            box = await workspace.Service.CreateBoundBoxAsync("目标收纳盒", boundFolder);
        }
        else
        {
            box = await workspace.GetBoxAsync(boxType);
        }

        var item = await workspace.Service.CreateFileSystemItemAsync(
            box.Id,
            itemKind,
            desiredName);

        Assert.Equal(desiredName, item.DisplayName);
        Assert.Equal(itemKind, item.ItemKind);
        Assert.Null(item.SourcePath);
        Assert.NotNull(item.StoredPath);
        Assert.Equal(itemKind == ItemKind.Directory, Directory.Exists(item.StoredPath));
        Assert.Equal(itemKind == ItemKind.File, File.Exists(item.StoredPath));
        Assert.Contains(await workspace.Service.GetItemsAsync(box.Id), candidate => candidate.Id == item.Id);
    }

    [Theory]
    [InlineData(BoxType.Normal, ItemKind.File, "改名后.txt")]
    [InlineData(BoxType.Normal, ItemKind.Directory, "改名后文件夹")]
    [InlineData(BoxType.Bound, ItemKind.File, "改名后.txt")]
    [InlineData(BoxType.Bound, ItemKind.Directory, "改名后文件夹")]
    public async Task RenameFileSystemItemAsync_RenamesDiskEntryAndIndex(
        BoxType boxType,
        ItemKind itemKind,
        string newName)
    {
        using var workspace = await TestWorkspace.CreateAsync();
        Box box;
        if (boxType == BoxType.Bound)
        {
            var boundFolder = Path.Combine(workspace.Root, "rename-target");
            Directory.CreateDirectory(boundFolder);
            box = await workspace.Service.CreateBoundBoxAsync("目标收纳盒", boundFolder);
        }
        else
        {
            box = await workspace.GetBoxAsync(boxType);
        }

        var originalName = itemKind == ItemKind.File ? "原名称.txt" : "原文件夹";
        var item = await workspace.Service.CreateFileSystemItemAsync(box.Id, itemKind, originalName);
        var originalPath = item.StoredPath!;

        var renamed = await workspace.Service.RenameFileSystemItemAsync(item.Id, newName);

        Assert.Equal(newName, renamed.DisplayName);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(originalPath)!, newName), renamed.StoredPath);
        Assert.False(File.Exists(originalPath) || Directory.Exists(originalPath));
        Assert.True(File.Exists(renamed.StoredPath) || Directory.Exists(renamed.StoredPath));
        Assert.Equal(renamed, await workspace.Repository.GetItemAsync(item.Id));
    }

    [Theory]
    [InlineData(BoxType.Normal)]
    [InlineData(BoxType.Bound)]
    public async Task CopyPathsIntoBoxAsync_CopiesFilesAndFoldersWithoutMovingSources(BoxType boxType)
    {
        using var workspace = await TestWorkspace.CreateAsync();
        Box box;
        if (boxType == BoxType.Bound)
        {
            var boundFolder = Path.Combine(workspace.Root, "paste-target");
            Directory.CreateDirectory(boundFolder);
            box = await workspace.Service.CreateBoundBoxAsync("目标收纳盒", boundFolder);
        }
        else
        {
            box = await workspace.GetBoxAsync(boxType);
        }

        var sourceFile = workspace.CreateSourceFile("clipboard", "资料.txt", "file-content");
        var sourceFolder = workspace.CreateSourceDirectory("clipboard-folder", "nested.txt", "folder-content");

        var copied = await workspace.Service.CopyPathsIntoBoxAsync(
            box.Id,
            [sourceFile, sourceFolder]);
        var duplicate = Assert.Single(await workspace.Service.CopyPathsIntoBoxAsync(box.Id, [sourceFile]));

        Assert.True(File.Exists(sourceFile));
        Assert.True(Directory.Exists(sourceFolder));
        Assert.Equal(2, copied.Count);
        Assert.All(copied, item => Assert.Null(item.SourcePath));
        var copiedFile = copied.Single(item => item.ItemKind == ItemKind.File);
        var copiedFolder = copied.Single(item => item.ItemKind == ItemKind.Directory);
        Assert.Equal("file-content", await File.ReadAllTextAsync(copiedFile.StoredPath!));
        Assert.Equal(
            "folder-content",
            await File.ReadAllTextAsync(Path.Combine(copiedFolder.StoredPath!, "nested.txt")));
        Assert.Equal("资料 (1).txt", duplicate.DisplayName);
        Assert.Equal(3, (await workspace.Service.GetItemsAsync(box.Id)).Count);
    }

    [Fact]
    public async Task MovePathsIntoFolderAsync_MovesExternalFileUnderSelectedBoxFolder()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var folder = await workspace.Service.CreateFileSystemItemAsync(
            box.Id,
            ItemKind.Directory,
            "归档资料");
        var sourceFile = workspace.CreateSourceFile("drop-into-folder", "报价单.txt", "payload");

        var movedPaths = await workspace.Service.MovePathsIntoFolderAsync(
            box.Id,
            folder.Id,
            [sourceFile]);

        var movedPath = Assert.Single(movedPaths);
        Assert.Equal(Path.Combine(folder.StoredPath!, "报价单.txt"), movedPath);
        Assert.False(File.Exists(sourceFile));
        Assert.Equal("payload", await File.ReadAllTextAsync(movedPath));
        Assert.DoesNotContain(
            await workspace.Service.GetItemsAsync(box.Id),
            item => item.DisplayName == "报价单.txt");
    }

    [Fact]
    public async Task MovePathsIntoFolderAsync_TargetBoxMovesFileUnderSelectedFolder()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var boundRoot = Path.Combine(workspace.Root, "folder-drop-target");
        Directory.CreateDirectory(boundRoot);
        var box = await workspace.Service.CreateBoundBoxAsync("目标收纳盒", boundRoot);
        var folder = await workspace.Service.CreateFileSystemItemAsync(
            box.Id,
            ItemKind.Directory,
            "交付资料");
        var sourceFile = workspace.CreateSourceFile("bound-folder-drop", "清单.txt", "content");

        var movedPath = Assert.Single(await workspace.Service.MovePathsIntoFolderAsync(
            box.Id,
            folder.Id,
            [sourceFile]));

        Assert.Equal(Path.Combine(folder.StoredPath!, "清单.txt"), movedPath);
        Assert.True(File.Exists(movedPath));
        Assert.DoesNotContain(
            await workspace.Service.GetItemsAsync(box.Id),
            item => item.DisplayName == "清单.txt");
    }

    [Fact]
    public async Task MovePathsIntoFolderAsync_AddsSuffixForConflictingName()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var folder = await workspace.Service.CreateFileSystemItemAsync(
            box.Id,
            ItemKind.Directory,
            "资料");
        await File.WriteAllTextAsync(Path.Combine(folder.StoredPath!, "报告.txt"), "existing");
        var sourceFile = workspace.CreateSourceFile("folder-conflict", "报告.txt", "new");

        var movedPath = Assert.Single(await workspace.Service.MovePathsIntoFolderAsync(
            box.Id,
            folder.Id,
            [sourceFile]));

        Assert.Equal(Path.Combine(folder.StoredPath!, "报告 (1).txt"), movedPath);
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(folder.StoredPath!, "报告.txt")));
        Assert.Equal("new", await File.ReadAllTextAsync(movedPath));
    }

    [Fact]
    public async Task CreateBoundBoxAsync_IndexesExistingFolderContentsWithoutMovingThem()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var boundFolder = Path.Combine(workspace.Root, "existing-project");
        Directory.CreateDirectory(boundFolder);
        var file = Path.Combine(boundFolder, "readme.txt");
        var directory = Path.Combine(boundFolder, "assets");
        File.WriteAllText(file, "hello");
        Directory.CreateDirectory(directory);

        var box = await workspace.Service.CreateBoundBoxAsync("现有项目", boundFolder);
        var items = await workspace.Service.GetItemsAsync(box.Id);

        Assert.Equal(BoxType.Bound, box.Type);
        Assert.Equal(Path.GetFullPath(boundFolder), box.StoragePath);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.DisplayName == "readme.txt" && item.StoredPath == file);
        Assert.Contains(items, item => item.DisplayName == "assets" && item.ItemKind == ItemKind.Directory);
        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public async Task GetItemsAsync_BoundBoxReflectsExternalAddDeleteAndRename()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var boundFolder = Path.Combine(workspace.Root, "watched");
        Directory.CreateDirectory(boundFolder);
        var originalPath = Path.Combine(boundFolder, "before.txt");
        File.WriteAllText(originalPath, "before");
        var box = await workspace.Service.CreateBoundBoxAsync("watched", boundFolder);

        var addedPath = Path.Combine(boundFolder, "added.txt");
        File.WriteAllText(addedPath, "added");
        File.Move(originalPath, Path.Combine(boundFolder, "renamed.txt"));

        var items = await workspace.Service.GetItemsAsync(box.Id);

        Assert.DoesNotContain(items, item => item.DisplayName == "before.txt");
        Assert.Contains(items, item => item.DisplayName == "renamed.txt");
        Assert.Contains(items, item => item.DisplayName == "added.txt");

        File.Delete(addedPath);
        var afterDelete = await workspace.Service.GetItemsAsync(box.Id);
        Assert.DoesNotContain(afterDelete, item => item.DisplayName == "added.txt");
    }

    [Fact]
    public async Task ImportAndDeleteItemInBoundBoxMovesTheRealFile()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var boundFolder = Path.Combine(workspace.Root, "target");
        Directory.CreateDirectory(boundFolder);
        var source = workspace.CreateSourceFile("bound-source", "move-me.txt", "payload");
        var box = await workspace.Service.CreateBoundBoxAsync("target", boundFolder);

        var item = await workspace.Service.ImportPathAsync(box.Id, source);
        var storedPath = Path.Combine(boundFolder, "move-me.txt");
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(storedPath));
        Assert.Equal(storedPath, item.StoredPath);

        var result = await workspace.Service.DeleteItemAsync(item.Id);

        Assert.True(result.RestoredToOriginal);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(storedPath));
        Assert.Empty(await workspace.Service.GetItemsAsync(box.Id));
    }

    [Fact]
    public async Task DeleteBoxAsync_BoundBoxRemovesBindingButKeepsFolderContents()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var boundFolder = Path.Combine(workspace.Root, "keep-me");
        Directory.CreateDirectory(boundFolder);
        var file = Path.Combine(boundFolder, "important.txt");
        File.WriteAllText(file, "keep");
        var box = await workspace.Service.CreateBoundBoxAsync("keep-me", boundFolder);

        var result = await workspace.Service.DeleteBoxAsync(box.Id);

        Assert.True(result.BoxRemoved);
        Assert.Equal(0, result.RestoredCount);
        Assert.True(File.Exists(file));
        Assert.DoesNotContain((await workspace.Service.GetBoxesAsync()), candidate => candidate.Id == box.Id);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root, AppPaths paths, DrawerRepository repository, DrawerService service)
        {
            Root = root;
            Paths = paths;
            Repository = repository;
            Service = service;
        }

        public string Root { get; }

        public AppPaths Paths { get; }

        public DrawerRepository Repository { get; }

        public DrawerService Service { get; }

        public static async Task<TestWorkspace> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);

            await service.InitializeAsync();
            return new TestWorkspace(root, paths, repository, service);
        }

        public string CreateSourceFile(string folderName, string fileName, string content)
        {
            var directory = Path.Combine(Root, "sources", folderName);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public string CreateSourceDirectory(string folderName, string nestedFileName, string content)
        {
            var directory = Path.Combine(Root, "sources", folderName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, nestedFileName), content);
            return directory;
        }

        public async Task<Box> GetBoxAsync(BoxType type)
        {
            var boxes = await Service.GetBoxesAsync();
            return boxes.Single(box => box.Type == type);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup should not hide the test result.
            }
        }
    }
}
