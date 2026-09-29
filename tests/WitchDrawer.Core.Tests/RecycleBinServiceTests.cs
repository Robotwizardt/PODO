using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

/// <summary>
/// 回收站聚焦测试：文件必须先移入 <c>Recycle\{记录Id}</c> 并登记，
/// 保留 <see cref="RecycleBinService.RetentionDays"/> 天，还原/彻底删除/清空/过期清理都必须可恢复。
/// </summary>
public sealed class RecycleBinServiceTests
{
    [Fact]
    public async Task RecycleAsync_MovesFileUnderRecycleDirectoryAndRecordsEntry()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var original = workspace.CreateSourceFile("outside", "keep.txt", "payload");
        var deletedAtUtc = DateTimeOffset.UtcNow;

        var entry = await workspace.RecycleBin.RecycleAsync(
            new RecycleRequest(
                SourcePath: original,
                DisplayName: "keep.txt",
                OriginalPath: original,
                BoxId: workspace.Box.Id,
                BoxName: workspace.Box.Name,
                BoxType: workspace.Box.Type,
                SourceItemId: null,
                IsDirectory: false));

        Assert.False(File.Exists(original));
        Assert.True(File.Exists(entry.RecyclePath));
        Assert.Equal("payload", await File.ReadAllTextAsync(entry.RecyclePath));
        Assert.StartsWith(workspace.Paths.RecycleDirectory, entry.RecyclePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(entry.Id.ToString("N"), entry.RecyclePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, entry.SizeBytes);
        Assert.False(entry.WasDirectory);
        Assert.Equal(original, entry.OriginalPath);
        var expectedExpiry = deletedAtUtc.AddDays(RecycleBinService.RetentionDays);
        Assert.True(Math.Abs((entry.ExpiresAtUtc - expectedExpiry).TotalMinutes) < 1);

        var entries = await workspace.RecycleBin.GetEntriesAsync();
        Assert.Single(entries);
        Assert.Equal(entry.Id, entries[0].Id);
    }

    [Fact]
    public async Task RecycleAsync_MovesDirectoryRecursively()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var original = workspace.CreateSourceDirectory("folder", "nested.txt", "payload");

        var entry = await workspace.RecycleBin.RecycleAsync(
            new RecycleRequest(
                SourcePath: original,
                DisplayName: "folder",
                OriginalPath: original,
                BoxId: null,
                BoxName: "已删除的盒子",
                BoxType: BoxType.Normal,
                SourceItemId: null,
                IsDirectory: true));

        Assert.False(Directory.Exists(original));
        Assert.True(Directory.Exists(entry.RecyclePath));
        Assert.True(File.Exists(Path.Combine(entry.RecyclePath, "nested.txt")));
        Assert.True(entry.WasDirectory);
        Assert.Equal(7, entry.SizeBytes);
    }

    [Fact]
    public async Task RecycleAsync_ThrowsWhenSourceIsMissing()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var missing = Path.Combine(workspace.Root, "sources", "outside", "gone.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => workspace.RecycleBin.RecycleAsync(
                new RecycleRequest(
                    SourcePath: missing,
                    DisplayName: "gone.txt",
                    OriginalPath: missing,
                    BoxId: null,
                    BoxName: "已删除的盒子",
                    BoxType: BoxType.Normal,
                    SourceItemId: null,
                    IsDirectory: false)));
    }

    [Fact]
    public async Task RecycleAsync_RefusesToRecycleFilesAlreadyInsideTheRecycleDirectory()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var original = workspace.CreateSourceFile("outside", "again.txt", "payload");
        var first = await workspace.RecycleBin.RecycleAsync(
            new RecycleRequest(
                SourcePath: original,
                DisplayName: "again.txt",
                OriginalPath: original,
                BoxId: null,
                BoxName: "已删除的盒子",
                BoxType: BoxType.Normal,
                SourceItemId: null,
                IsDirectory: false));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.RecycleBin.RecycleAsync(
                new RecycleRequest(
                    SourcePath: first.RecyclePath,
                    DisplayName: "again.txt",
                    OriginalPath: original,
                    BoxId: null,
                    BoxName: "已删除的盒子",
                    BoxType: BoxType.Normal,
                    SourceItemId: null,
                    IsDirectory: false)));

        Assert.True(File.Exists(first.RecyclePath));
    }

    [Fact]
    public async Task RestoreAsync_PutsFileBackIntoOriginalDirectory()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var original = workspace.CreateSourceFile("outside", "restore-me.txt", "payload");
        var entry = await RecycleAsync(workspace, original);

        var result = await workspace.RecycleBin.RestoreAsync(entry.Id);

        Assert.True(File.Exists(original));
        Assert.Equal("payload", await File.ReadAllTextAsync(original));
        Assert.False(File.Exists(entry.RecyclePath));
        Assert.False(result.RestoredIntoBox);
        Assert.False(result.RestoredToDesktop);
        Assert.False(result.Missing);
        Assert.Contains("已还原", result.StatusMessage);
        Assert.Empty(await workspace.RecycleBin.GetEntriesAsync());
    }

    [Fact]
    public async Task RestoreAsync_FallsBackToDesktopWhenOriginalDirectoryIsMissing()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var original = workspace.CreateSourceFile("vanishing", "desktop.txt", "payload");
        var entry = await RecycleAsync(workspace, original);
        Directory.Delete(Path.GetDirectoryName(original)!, recursive: true);

        var result = await workspace.RecycleBin.RestoreAsync(entry.Id);

        Assert.True(result.RestoredToDesktop);
        Assert.False(File.Exists(entry.RecyclePath));
        Assert.True(File.Exists(result.RestoredPath));
        Assert.Equal(
            workspace.DesktopDirectory,
            Path.GetDirectoryName(Path.GetFullPath(result.RestoredPath)),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("desktop.txt", Path.GetFileName(result.RestoredPath));
        Assert.Equal("payload", await File.ReadAllTextAsync(result.RestoredPath));
        Assert.Empty(await workspace.RecycleBin.GetEntriesAsync());
    }

    [Fact]
    public async Task RestoreAsync_BringsItemBackIntoBoxAndClearsRecycleState()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("收纳", BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(
            box.Id,
            workspace.CreateSourceFile("boxed", "inside.txt", "payload"));
        var storedPath = item.StoredPath!;

        var delete = await workspace.Service.DeleteItemAsync(item.Id);
        Assert.True(delete.Recycled);

        var recycledItem = await workspace.Repository.GetItemAsync(item.Id);
        Assert.NotNull(recycledItem);
        Assert.True(recycledItem!.IsRecycled);
        Assert.Empty(await workspace.Service.GetItemsAsync(box.Id));

        var entryId = delete.RecycleEntryId!.Value;
        var result = await workspace.RecycleBin.RestoreAsync(entryId);

        Assert.True(result.RestoredIntoBox);
        Assert.Equal(box.Name, result.BoxName);
        Assert.True(File.Exists(storedPath));
        Assert.Equal("payload", await File.ReadAllTextAsync(storedPath));

        var restoredItem = await workspace.Repository.GetItemAsync(item.Id);
        Assert.NotNull(restoredItem);
        Assert.False(restoredItem!.IsRecycled);
        Assert.Null(restoredItem.RecycleEntryId);

        var visibleItems = await workspace.Service.GetItemsAsync(box.Id);
        Assert.Single(visibleItems);
        Assert.Equal(item.Id, visibleItems[0].Id);
        Assert.Empty(await workspace.RecycleBin.GetEntriesAsync());
    }

    [Fact]
    public async Task RestoreAsync_ReportsMissingWhenFileWasRemovedOutsideTheApp()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var original = workspace.CreateSourceFile("outside", "vanished.txt", "payload");
        var entry = await RecycleAsync(workspace, original);
        Directory.Delete(Path.GetDirectoryName(entry.RecyclePath)!, recursive: true);

        var result = await workspace.RecycleBin.RestoreAsync(entry.Id);

        Assert.True(result.Missing);
        Assert.False(result.RestoredIntoBox);
        Assert.False(File.Exists(original));
        Assert.Empty(await workspace.RecycleBin.GetEntriesAsync());
    }

    [Fact]
    public async Task RestoreAsync_ThrowsForUnknownEntry()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.RecycleBin.RestoreAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RestoreAsync_DoesNotOverwriteAnExistingFileAtTheOriginalPath()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var original = workspace.CreateSourceFile("outside", "duplicate.txt", "payload");
        var entry = await RecycleAsync(workspace, original);
        File.WriteAllText(original, "newer");

        var result = await workspace.RecycleBin.RestoreAsync(entry.Id);

        Assert.Equal("newer", await File.ReadAllTextAsync(original));
        Assert.True(File.Exists(result.RestoredPath));
        Assert.NotEqual(original, result.RestoredPath);
        Assert.Equal("payload", await File.ReadAllTextAsync(result.RestoredPath));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileRecordAndItemRow()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("收纳", BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(
            box.Id,
            workspace.CreateSourceFile("boxed", "purge.txt", "payload"));
        var delete = await workspace.Service.DeleteItemAsync(item.Id);
        Assert.True(delete.Recycled);

        var removed = await workspace.RecycleBin.DeleteAsync(delete.RecycleEntryId!.Value);

        Assert.True(removed);
        Assert.False(File.Exists(delete.RecyclePath));
        Assert.Empty(await workspace.RecycleBin.GetEntriesAsync());
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Empty(await workspace.Service.GetItemsAsync(box.Id));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseForUnknownEntry()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        Assert.False(await workspace.RecycleBin.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task EmptyAsync_RemovesEveryEntry()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var first = await RecycleAsync(workspace, workspace.CreateSourceFile("outside", "a.txt", "a"));
        var second = await RecycleAsync(workspace, workspace.CreateSourceFile("outside", "b.txt", "b"));

        var removed = await workspace.RecycleBin.EmptyAsync();

        Assert.Equal(2, removed);
        Assert.False(File.Exists(first.RecyclePath));
        Assert.False(File.Exists(second.RecyclePath));
        Assert.Empty(await workspace.RecycleBin.GetEntriesAsync());
    }

    [Fact]
    public async Task PurgeExpiredAsync_RemovesOnlyExpiredEntries()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var live = await RecycleAsync(workspace, workspace.CreateSourceFile("outside", "live.txt", "live"));
        var expiredId = Guid.NewGuid();
        var expiredPath = Path.Combine(workspace.Paths.RecycleDirectory, expiredId.ToString("N"), "stale.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(expiredPath)!);
        await File.WriteAllTextAsync(expiredPath, "stale");
        await workspace.Repository.AddRecycleEntryAsync(
            new RecycleEntry(
                expiredId,
                "stale.txt",
                expiredPath,
                expiredPath,
                null,
                "已删除的盒子",
                BoxType.Normal,
                null,
                false,
                5,
                DateTimeOffset.UtcNow.AddDays(-40),
                DateTimeOffset.UtcNow.AddDays(-10)));

        var purged = await workspace.RecycleBin.PurgeExpiredAsync();

        Assert.Equal(1, purged);
        Assert.False(File.Exists(expiredPath));
        Assert.True(File.Exists(live.RecyclePath));
        Assert.Single(await workspace.RecycleBin.GetEntriesAsync());
        Assert.Equal(live.Id, (await workspace.RecycleBin.GetEntriesAsync())[0].Id);
    }

    [Fact]
    public async Task GetTotalSizeBytesAsync_SumsEveryEntry()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        await RecycleAsync(workspace, workspace.CreateSourceFile("outside", "a.txt", "12345"));
        await RecycleAsync(workspace, workspace.CreateSourceFile("outside", "b.txt", "678"));

        Assert.Equal(8, await workspace.RecycleBin.GetTotalSizeBytesAsync());
    }

    [Fact]
    public async Task DeleteBoxAsync_DoesNotTouchFilesAlreadyWaitingInTheRecycleBin()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("收纳", BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(
            box.Id,
            workspace.CreateSourceFile("boxed", "survivor.txt", "payload"));
        var delete = await workspace.Service.DeleteItemAsync(item.Id);
        Assert.True(delete.Recycled);

        await workspace.Service.DeleteBoxAsync(box.Id);

        Assert.True(File.Exists(delete.RecyclePath));
        Assert.Single(await workspace.RecycleBin.GetEntriesAsync());

        // 盒子已经不在了：还原只能落到磁盘，不能凭空变回盒内条目。
        var result = await workspace.RecycleBin.RestoreAsync(delete.RecycleEntryId!.Value);
        Assert.False(result.RestoredIntoBox);
        Assert.True(File.Exists(result.RestoredPath));
    }

    [Fact]
    public async Task PruneMissingStoredItemsAsync_KeepsRecycledItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("收纳", BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(
            box.Id,
            workspace.CreateSourceFile("boxed", "pruned.txt", "payload"));
        var delete = await workspace.Service.DeleteItemAsync(item.Id);
        Assert.True(delete.Recycled);

        await workspace.Service.GetItemsAsync(box.Id);
        await workspace.Service.GetAllItemsAsync();
        await workspace.Service.SearchItemsAsync("pruned");

        var tracked = await workspace.Repository.GetItemAsync(item.Id);
        Assert.NotNull(tracked);
        Assert.True(tracked!.IsRecycled);
    }

    [Fact]
    public async Task InitializeAsync_RewritesRecyclePathsAfterTheDataDirectoryMoved()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("收纳", BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(
            box.Id,
            workspace.CreateSourceFile("boxed", "moved.txt", "payload"));
        var delete = await workspace.Service.DeleteItemAsync(item.Id);
        Assert.True(delete.Recycled);

        var entryId = delete.RecycleEntryId!.Value;
        var originalEntry = (await workspace.RecycleBin.GetEntriesAsync()).Single();
        Assert.StartsWith(
            workspace.Paths.RecycleDirectory,
            originalEntry.RecyclePath,
            StringComparison.OrdinalIgnoreCase);

        // 模拟“数据目录迁移”：整根复制到新位置，记录里的绝对路径仍指向旧根目录。
        var movedRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await workspace.Repository.CheckpointAsync();
            CopyDirectory(workspace.Root, movedRoot);

            var movedPaths = new AppPaths(movedRoot);
            var movedRepository = new DrawerRepository(movedPaths.DatabasePath);
            var movedService = new DrawerService(movedPaths, movedRepository);
            await movedService.InitializeAsync();

            var repaired = await movedService.RecycleBin.GetEntryAsync(entryId);
            Assert.NotNull(repaired);
            Assert.StartsWith(
                movedPaths.RecycleDirectory,
                repaired!.RecyclePath,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(repaired.RecyclePath));

            // 路径修好后还原依然可用：文件回到新根目录下的盒子里。
            var restored = await movedService.RecycleBin.RestoreAsync(entryId);
            Assert.True(restored.RestoredIntoBox);
            Assert.True(File.Exists(restored.RestoredPath));
            Assert.StartsWith(movedPaths.BoxesDirectory, restored.RestoredPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(movedRoot))
            {
                Directory.Delete(movedRoot, recursive: true);
            }
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
            {
                // 复制作废的 WAL 会让新库读到半成品事务；调用方已 checkpoint，主库文件就是完整状态。
                continue;
            }

            File.Copy(file, Path.Combine(targetDirectory, name), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(targetDirectory, Path.GetFileName(directory)));
        }
    }

    private static Task<RecycleEntry> RecycleAsync(TestWorkspace workspace, string originalPath)
    {
        return workspace.RecycleBin.RecycleAsync(
            new RecycleRequest(
                SourcePath: originalPath,
                DisplayName: Path.GetFileName(originalPath),
                OriginalPath: originalPath,
                BoxId: null,
                BoxName: "已删除的盒子",
                BoxType: BoxType.Normal,
                SourceItemId: null,
                IsDirectory: Directory.Exists(originalPath)));
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(
            string root,
            AppPaths paths,
            DrawerRepository repository,
            DrawerService service,
            string desktopDirectory)
        {
            Root = root;
            Paths = paths;
            Repository = repository;
            Service = service;
            RecycleBin = service.RecycleBin;
            DesktopDirectory = desktopDirectory;
            Box = new Box(Guid.NewGuid(), "收纳", BoxType.Normal, null, 0, default, default);
        }

        public string Root { get; }

        public AppPaths Paths { get; }

        public DrawerRepository Repository { get; }

        public DrawerService Service { get; }

        public RecycleBinService RecycleBin { get; }

        public string DesktopDirectory { get; }

        public Box Box { get; }

        public static async Task<TestWorkspace> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            // 桌面兜底目录指向测试自己的临时目录，避免测试往用户真实桌面写文件。
            var desktop = Path.Combine(root, "fake-desktop");
            var service = new DrawerService(paths, repository, new RecycleBinService(paths, repository, () => desktop));

            await service.InitializeAsync();
            return new TestWorkspace(root, paths, repository, service, desktop);
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
