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

public sealed class AdaptiveLayoutTests
{
    [Fact]
    public async Task LegacyFixedSetting_DoesNotConstrainLoadedContent()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("收纳盒", BoxType.Normal);
            await drawerService.SetSettingAsync(
                GetLegacySizeModeSettingKey(box.Id),
                "Fixed:1:1");

            var sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            foreach (var name in Enumerable.Range(1, 10).Select(index => $"{index}.txt"))
            {
                var path = Path.Combine(sourceDir, name);
                File.WriteAllText(path, "payload");
                await drawerService.ImportPathAsync(box.Id, path);
            }

            var viewModel = CreateViewModel(box, drawerService, repository);
            await viewModel.LoadAsync();

            Assert.Equal(10, viewModel.Items.Count);
            Assert.Equal(
                10,
                viewModel.Items
                    .Select(item => (item.GridColumn, item.GridRow))
                    .Distinct()
                    .Count());
            Assert.Equal(4, viewModel.Items.Max(item => item.GridColumn));
            Assert.Equal(1, viewModel.Items.Max(item => item.GridRow));
            Assert.Equal(
                5 * viewModel.LayoutSettings.ItemSlotWidth,
                viewModel.GridCanvasWidth);
            Assert.Equal(
                2 * viewModel.LayoutSettings.ItemSlotHeight,
                viewModel.GridCanvasHeight);
            Assert.Equal(
                System.Windows.Controls.ScrollBarVisibility.Auto,
                viewModel.GridVerticalScrollBarVisibility);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ImportPaths_AlwaysImportsAllPathsRegardlessOfLegacySetting()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("收纳盒", BoxType.Normal);
            await drawerService.SetSettingAsync(
                GetLegacySizeModeSettingKey(box.Id),
                "Fixed:1:1");

            var sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            var files = Enumerable.Range(1, 3)
                .Select(index =>
                {
                    var path = Path.Combine(sourceDir, $"file{index}.txt");
                    File.WriteAllText(path, "payload");
                    return path;
                })
                .ToArray();

            var viewModel = CreateViewModel(box, drawerService, repository);
            await viewModel.ImportPathsAsync(files);

            Assert.Equal(3, viewModel.Items.Count);
            Assert.Equal("已收纳 3 项", viewModel.StatusText);
            Assert.All(files, file => Assert.False(File.Exists(file)));
            Assert.True(viewModel.HasFreeSlotForDrop());
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task DropSlot_IsNotClampedToLegacyBounds()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("收纳盒", BoxType.Normal);
            var viewModel = CreateViewModel(box, drawerService, repository);

            Assert.True(viewModel.TryGetAvailableDropSlot(9, 9, null, out var slot));
            Assert.Equal((9, 9), slot);

            var sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            var file = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(file, "payload");
            await viewModel.ImportPathsAsync([file]);

            var item = viewModel.Items.Single();
            Assert.True(viewModel.TryGetAvailableDropSlot(9, 9, item.Id, out slot));
            Assert.Equal((9, 9), slot);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task DragExpansion_KeepsExpandedSlotWhilePointerInsideBox()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("收纳盒", BoxType.Normal);
            var viewModel = CreateViewModel(box, drawerService, repository);

            var sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            var file = Path.Combine(sourceDir, "a.txt");
            File.WriteAllText(file, "payload");
            await viewModel.ImportPathsAsync([file]);
            Assert.Single(viewModel.Items);

            var slot = viewModel.LayoutSettings.ItemSlotWidth;
            var expanded = viewModel.GetGridSlot(slot - 5, 10, 200, 200);
            Assert.Equal(1, expanded.Column);
            viewModel.ShowDragPreview(expanded.Column, expanded.Row);

            var kept = viewModel.GetGridSlot(10, 10, 200, 200);
            Assert.Equal(1, kept.Column);

            await Task.Delay(200);
            var followed = viewModel.GetGridSlot(10, 10, 200, 200);
            Assert.Equal(0, followed.Column);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    private static DesktopBoxViewModel CreateViewModel(
        Box box,
        DrawerService drawerService,
        DrawerRepository repository) =>
        new(
            box,
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new RecordingLogger(),
            BoxVisualStyle.Modern);

    private static string GetLegacySizeModeSettingKey(Guid boxId) =>
        $"BoxSizeMode:{boxId:N}";

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

    private static async Task<(DrawerService Service, DrawerRepository Repository)> CreateDrawerServiceAsync(
        string root)
    {
        var paths = new AppPaths(root);
        var repository = new DrawerRepository(paths.DatabasePath);
        var drawerService = new DrawerService(paths, repository);
        await drawerService.InitializeAsync();
        return (drawerService, repository);
    }

    private static void CleanupTempRoot(string root)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }
}
