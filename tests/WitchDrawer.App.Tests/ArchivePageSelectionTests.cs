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

public sealed class ArchivePageSelectionTests
{
    [Fact]
    public async Task ShowArchiveCommand_ClearsSelectedBox()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
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
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))));

            await viewModel.CreateDrawerBoxCommand.ExecuteAsync(null);
            Assert.NotNull(viewModel.SelectedBox);

            await viewModel.ShowArchiveCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsArchivePage);
            Assert.Null(viewModel.SelectedBox);

            viewModel.ShowDashboardCommand.Execute(null);

            Assert.False(viewModel.IsArchivePage);
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
    public async Task ArchiveProjectCommand_HidesProjectWorkspaceAndRestoresItFromArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var projectService = new ProjectService(repository);
            var projectBox = await drawerService.CreateBoxAsync("归档测试项目", BoxType.Project);
            var fileBox = await drawerService.CreateBoxAsync("归档测试资料", BoxType.Normal);
            await projectService.LinkBoxAsync(projectBox.Id, fileBox.Id);

            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
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
                projectService);

            await viewModel.LoadCommand.ExecuteAsync(null);
            viewModel.SelectedBox = viewModel.Boxes.Single(box => box.Id == projectBox.Id);
            await Task.Delay(100);

            await viewModel.ArchiveSelectedProjectCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsArchivePage);
            Assert.Single(viewModel.ArchivedProjects);
            Assert.DoesNotContain(viewModel.Boxes, box => box.Id == projectBox.Id || box.Id == fileBox.Id);
            Assert.DoesNotContain(await drawerService.GetBoxesAsync(), box => box.Id == projectBox.Id);
            Assert.DoesNotContain(await drawerService.GetBoxesAsync(), box => box.Id == fileBox.Id);

            await viewModel.RestoreArchivedProjectCommand.ExecuteAsync(viewModel.ArchivedProjects.Single());

            Assert.Empty(viewModel.ArchivedProjects);
            Assert.Contains(viewModel.Boxes, box => box.Id == projectBox.Id);
            Assert.Contains(viewModel.Boxes, box => box.Id == fileBox.Id);
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
    public async Task ShowArchiveCommand_ListsStandaloneArchivedDesktopPapers()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var paperService = new FakeDesktopPaperService(
            [
                new DesktopPaperSummary(
                    "archived-paper",
                    "归档便签",
                    "笔记便签",
                    "12 个字符",
                    false)
            ]);
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
                paperTodoHost: paperService);

            await viewModel.ShowArchiveCommand.ExecuteAsync(null);

            Assert.Equal("archived-paper", viewModel.ArchivedPapers.Single().Id);
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
    public async Task ShowArchiveCommand_DoesNotDuplicatePapersOwnedByArchivedProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var projectService = new ProjectService(repository);
            var project = await drawerService.CreateBoxAsync("已归档项目", BoxType.Project);
            await projectService.LinkPaperAsync(
                project.Id,
                "project-paper",
                ProjectAttachmentSide.Right);
            await projectService.ArchiveProjectAsync(project.Id);

            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var paperService = new FakeDesktopPaperService(
            [
                new DesktopPaperSummary(
                    "project-paper",
                    "项目关联便签",
                    "笔记便签",
                    "8 个字符",
                    false)
            ]);
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
                projectService,
                paperService);

            await viewModel.ShowArchiveCommand.ExecuteAsync(null);

            Assert.Single(viewModel.ArchivedProjects);
            Assert.Empty(viewModel.ArchivedPapers);
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
    public async Task ProjectArchiveTransitions_RefreshStandaloneArchivedPapers()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var projectService = new ProjectService(repository);
            var project = await drawerService.CreateBoxAsync("状态刷新项目", BoxType.Project);
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var paperService = new FakeDesktopPaperService(
            [
                new DesktopPaperSummary(
                    "standalone-paper",
                    "独立归档便签",
                    "笔记便签",
                    "6 个字符",
                    false)
            ]);
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
                projectService,
                paperService);
            await viewModel.LoadCommand.ExecuteAsync(null);
            viewModel.SelectedBox = viewModel.Boxes.Single(box => box.Id == project.Id);
            await Task.Delay(100);

            await viewModel.ArchiveSelectedProjectCommand.ExecuteAsync(null);

            Assert.Equal("standalone-paper", viewModel.ArchivedPapers.Single().Id);

            await viewModel.RestoreArchivedProjectCommand.ExecuteAsync(
                viewModel.ArchivedProjects.Single());

            Assert.Equal("standalone-paper", viewModel.ArchivedPapers.Single().Id);
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
    public async Task RestoreArchivedPaperCommand_ReturnsThePaperToTheDesktop()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var paperService = new FakeDesktopPaperService(
            [
                new DesktopPaperSummary(
                    "paper-to-restore",
                    "恢复这张便签",
                    "待办便签",
                    "1 项待办未完成",
                    false)
            ]);
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
                paperTodoHost: paperService);
            await viewModel.ShowArchiveCommand.ExecuteAsync(null);

            await viewModel.RestoreArchivedPaperCommand.ExecuteAsync(
                viewModel.ArchivedPapers.Single());

            Assert.Empty(viewModel.ArchivedPapers);
            Assert.Equal(["paper-to-restore"], paperService.RestoredPaperIds);
            Assert.True(paperService.GetPapers().Single().IsVisible);
            Assert.Equal("已恢复桌面便签“恢复这张便签”", viewModel.StatusText);
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
    public async Task DeleteArchivedPaperCommand_PermanentlyRemovesThePaper()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var paperService = new FakeDesktopPaperService(
            [
                new DesktopPaperSummary(
                    "paper-to-delete",
                    "不再需要",
                    "笔记便签",
                    "4 个字符",
                    false)
            ]);
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
                paperTodoHost: paperService);
            await viewModel.ShowArchiveCommand.ExecuteAsync(null);

            await viewModel.DeleteArchivedPaperCommand.ExecuteAsync(
                viewModel.ArchivedPapers.Single());

            Assert.Empty(viewModel.ArchivedPapers);
            Assert.Equal(["paper-to-delete"], paperService.DeletedPaperIds);
            Assert.Equal("已永久删除归档便签“不再需要”", viewModel.StatusText);
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
    public async Task ShowSettingsAndAboutCommands_ClearSelectedBox()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
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
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))));

            await viewModel.CreateDrawerBoxCommand.ExecuteAsync(null);
            Assert.NotNull(viewModel.SelectedBox);

            viewModel.ShowSettingsCommand.Execute(null);

            Assert.True(viewModel.IsSettingsPage);
            Assert.Null(viewModel.SelectedBox);

            // Re-select a box so the About page can be verified the same way.
            viewModel.ShowDashboardCommand.Execute(null);
            await SelectFirstBoxAsync(viewModel);
            Assert.NotNull(viewModel.SelectedBox);

            viewModel.ShowAboutCommand.Execute(null);

            Assert.True(viewModel.IsAboutPage);
            Assert.Null(viewModel.SelectedBox);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task SelectFirstBoxAsync(MainViewModel viewModel)
    {
        viewModel.SelectedBox = viewModel.Boxes.First();
        // SelectedBox setter queues a fire-and-forget items load; give it a moment
        // so the SQLite connection is released before temp directory cleanup.
        await Task.Delay(200);
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDesktopPaperService(
        IReadOnlyList<DesktopPaperSummary> archivedPapers) : IDesktopPaperService
    {
        private readonly List<DesktopPaperSummary> _activePapers = [];
        private readonly List<DesktopPaperSummary> _archivedPapers = archivedPapers.ToList();

        public List<string> RestoredPaperIds { get; } = [];

        public List<string> DeletedPaperIds { get; } = [];

        public void CreateTodoPaper() { }

        public void CreateNotePaper() { }

        public IReadOnlyList<DesktopPaperSummary> GetPapers() => _activePapers.ToArray();

        public IReadOnlyList<DesktopPaperSummary> GetArchivedPapers() => _archivedPapers.ToArray();

        public bool ShowPaper(string paperId)
        {
            var index = _activePapers.FindIndex(paper => paper.Id == paperId);
            if (index < 0)
            {
                return false;
            }

            _activePapers[index] = _activePapers[index] with { IsVisible = true };
            return true;
        }

        public bool ArchivePaper(string paperId) => false;

        public bool DeletePaper(string paperId)
        {
            if (_archivedPapers.RemoveAll(paper => paper.Id == paperId) == 0)
            {
                return false;
            }

            DeletedPaperIds.Add(paperId);
            return true;
        }

        public int DeleteHiddenPapers() => 0;

        public IReadOnlyList<string> ArchivePapers(IEnumerable<string> paperIds) => [];

        public IReadOnlyList<string> RestoreArchivedPapers(IEnumerable<string> paperIds)
        {
            var restoredIds = new List<string>();
            foreach (var paperId in paperIds.Distinct(StringComparer.Ordinal))
            {
                var index = _archivedPapers.FindIndex(paper => paper.Id == paperId);
                if (index < 0)
                {
                    continue;
                }

                var paper = _archivedPapers[index] with { IsVisible = false };
                _archivedPapers.RemoveAt(index);
                _activePapers.Add(paper);
                RestoredPaperIds.Add(paperId);
                restoredIds.Add(paperId);
            }

            return restoredIds;
        }
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
