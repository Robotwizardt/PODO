using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class ItemDeletionConfirmationTests
{
    [Theory]
    [InlineData(BoxType.Normal)]
    [InlineData(BoxType.Mapping)]
    public async Task DeleteItemCommand_WhenConfirmationDeclined_LeavesFileAndItem(
        BoxType boxType)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var box = await drawerService.CreateBoxAsync("待确认删除", boxType);
            var source = Path.Combine(root, "source", "keep.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "keep");
            var item = await drawerService.ImportPathAsync(box.Id, source);
            var pathToKeep = item.EffectivePath!;
            var confirmationCalls = 0;

            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern,
                confirmItemDeletion: _ =>
                {
                    confirmationCalls++;
                    return false;
                });
            await viewModel.LoadAsync();

            await viewModel.DeleteItemCommand.ExecuteAsync(viewModel.Items.Single());

            Assert.Equal(1, confirmationCalls);
            Assert.True(File.Exists(pathToKeep));
            Assert.NotNull(await repository.GetItemAsync(item.Id));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(BoxType.Normal)]
    [InlineData(BoxType.Mapping)]
    public async Task RefreshAfterExternalDrag_RemovesItemAfterShellMovesItsPath(
        BoxType boxType)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var box = await drawerService.CreateBoxAsync("拖出刷新", boxType);
            var source = Path.Combine(root, "source", "move.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "move");
            var item = await drawerService.ImportPathAsync(box.Id, source);
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern);
            await viewModel.LoadAsync();

            var destination = Path.Combine(root, "desktop-folder", "move.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(item.EffectivePath!, destination);

            Assert.True(await viewModel.RefreshAfterExternalDragAsync(viewModel.Items.Single()));
            Assert.Empty(viewModel.Items);
            Assert.True(File.Exists(destination));
            Assert.Null(await repository.GetItemAsync(item.Id));
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
    public async Task MainViewModel_DeleteItemCommand_UsesConfirmationCallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var normalBox = (await drawerService.GetBoxesAsync()).Single(box => box.Type == BoxType.Normal);
            var source = Path.Combine(root, "source", "main.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "keep");
            var item = await drawerService.ImportPathAsync(normalBox.Id, source);
            var storedPath = item.StoredPath!;
            var logger = new NoOpLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var confirmationCalls = 0;
            var viewModel = new MainViewModel(
                drawerService,
                new TodoService(repository),
                launcher,
                logger,
                quickPanel,
                new UpdateService(logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    repository,
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))),
                confirmItemDeletion: _ =>
                {
                    confirmationCalls++;
                    return false;
                });

            await viewModel.LoadAsync();
            await viewModel.DeleteItemCommand.ExecuteAsync(new DrawerItemViewModel(item));

            Assert.Equal(1, confirmationCalls);
            Assert.True(File.Exists(storedPath));
            Assert.NotNull(await repository.GetItemAsync(item.Id));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }
}
