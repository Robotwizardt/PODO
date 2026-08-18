using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerServiceStorageSynchronizationTests
{
    [Fact]
    public async Task GetItemsAsync_RegistersAFileSavedDirectlyIntoANormalBox()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = (await workspace.Service.GetBoxesAsync())
            .Single(candidate => candidate.Type == BoxType.Normal);
        var savedPath = Path.Combine(box.StoragePath!, "browser-download.txt");
        await File.WriteAllTextAsync(savedPath, "downloaded");

        var items = await workspace.Service.GetItemsAsync(box.Id);

        var item = Assert.Single(items);
        Assert.Equal("browser-download.txt", item.DisplayName);
        Assert.Equal(ItemKind.File, item.ItemKind);
        Assert.Null(item.SourcePath);
        Assert.Equal(Path.GetFullPath(savedPath), item.StoredPath);
    }

    [Fact]
    public async Task GetItemsAsync_IgnoresIncompleteBrowserDownloadFilesUntilFinalNameExists()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = (await workspace.Service.GetBoxesAsync())
            .Single(candidate => candidate.Type == BoxType.Normal);
        await File.WriteAllTextAsync(Path.Combine(box.StoragePath!, "report.pdf.crdownload"), "partial");
        await File.WriteAllTextAsync(Path.Combine(box.StoragePath!, "upload.tmp"), "partial");

        Assert.Empty(await workspace.Service.GetItemsAsync(box.Id));

        var finalPath = Path.Combine(box.StoragePath!, "report.pdf");
        await File.WriteAllTextAsync(finalPath, "complete");
        var items = await workspace.Service.GetItemsAsync(box.Id);

        var item = Assert.Single(items);
        Assert.Equal("report.pdf", item.DisplayName);
    }

    [Fact]
    public async Task GetAllItemsAsync_CalibratesManagedStorageBoxesInTheBackgroundPath()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var drawer = await workspace.Service.CreateBoxAsync("drawer", BoxType.Drawer);
        var savedPath = Path.Combine(drawer.StoragePath!, "incoming.md");
        await File.WriteAllTextAsync(savedPath, "external");

        var items = await workspace.Service.GetAllItemsAsync();

        var item = Assert.Single(items, candidate => candidate.BoxId == drawer.Id);
        Assert.Equal(Path.GetFullPath(savedPath), item.StoredPath);
        Assert.Null(item.SourcePath);
    }

    [Fact]
    public async Task SynchronizeExternalRenameAsync_PreservesImportedItemIdentityAndSourceDirectory()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = (await workspace.Service.GetBoxesAsync())
            .Single(candidate => candidate.Type == BoxType.Normal);
        var sourceDirectory = Path.Combine(workspace.Root, "source");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "before.txt");
        await File.WriteAllTextAsync(sourcePath, "content");
        var imported = await workspace.Service.ImportPathAsync(box.Id, sourcePath);
        var renamedPath = Path.Combine(box.StoragePath!, "after.txt");
        File.Move(imported.StoredPath!, renamedPath);

        await workspace.Service.SynchronizeExternalRenameAsync(
            box.Id,
            imported.StoredPath!,
            renamedPath);

        var renamed = Assert.Single(await workspace.Service.GetItemsAsync(box.Id));
        Assert.Equal(imported.Id, renamed.Id);
        Assert.Equal("after.txt", renamed.DisplayName);
        Assert.Equal(Path.Combine(sourceDirectory, "after.txt"), renamed.SourcePath);
        Assert.Equal(Path.GetFullPath(renamedPath), renamed.StoredPath);
    }

    [Fact]
    public async Task GetItemsAsync_WhenOneManagedBoxDirectoryIsTemporarilyUnavailable_KeepsRecords()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = (await workspace.Service.GetBoxesAsync())
            .Single(candidate => candidate.Type == BoxType.Normal);
        var sourceDirectory = Path.Combine(workspace.Root, "source-offline");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "kept.txt");
        await File.WriteAllTextAsync(sourcePath, "content");
        var imported = await workspace.Service.ImportPathAsync(box.Id, sourcePath);
        var offlinePath = box.StoragePath + ".offline";
        Directory.Move(box.StoragePath!, offlinePath);

        try
        {
            var items = await workspace.Service.GetItemsAsync(box.Id);
            Assert.Contains(items, candidate => candidate.Id == imported.Id);
        }
        finally
        {
            Directory.Move(offlinePath, box.StoragePath!);
        }

        Assert.Contains(
            await workspace.Service.GetItemsAsync(box.Id),
            candidate => candidate.Id == imported.Id);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root, DrawerService service)
        {
            Root = root;
            Service = service;
        }

        public string Root { get; }

        public DrawerService Service { get; }

        public static async Task<TestWorkspace> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "WitchDrawer.StorageSyncTests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            return new TestWorkspace(root, service);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Temporary cleanup must not hide the test result.
            }
        }
    }
}
