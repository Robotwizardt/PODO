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

public sealed class DiagnosisWorkflowTests
{
    [Fact]
    public async Task BoundBoxImport_DoesNotReassignExistingUnpersistedItemSlot()
    {
        var result = await RunBoundImportScenarioAsync(persistExistingPosition: false);

        Assert.Equal(0, result.ExistingColumn);
        Assert.Equal(0, result.ExistingRow);
        Assert.Equal(1, result.NewColumn);
        Assert.Equal(0, result.NewRow);
        Assert.Equal(2, result.ItemCount);
    }

    [Fact]
    public async Task BoundBoxImport_PersistingExistingSlotPreventsReassignment()
    {
        var result = await RunBoundImportScenarioAsync(persistExistingPosition: true);

        Assert.Equal(0, result.ExistingColumn);
        Assert.Equal(0, result.ExistingRow);
        Assert.Equal(1, result.NewColumn);
        Assert.Equal(0, result.NewRow);
        Assert.Equal(2, result.ItemCount);
    }

    [Fact]
    public async Task ProjectAssociation_MatrixPersistsMultiSideMoveAndCascade()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Podo.Diagnosis",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var projectService = new ProjectService(repository);

            var firstProject = await drawerService.CreateBoxAsync(
                "项目一",
                BoxType.Project);
            var secondProject = await drawerService.CreateBoxAsync(
                "项目二",
                BoxType.Project);
            var firstFileBox = await drawerService.CreateBoxAsync(
                "资料一",
                BoxType.Normal);
            var secondFileBox = await drawerService.CreateBoxAsync(
                "资料二",
                BoxType.Mapping);
            await projectService.GetOrCreateProjectAsync(firstProject.Id);
            await projectService.GetOrCreateProjectAsync(secondProject.Id);

            await projectService.LinkBoxAsync(
                firstProject.Id,
                firstFileBox.Id);
            await projectService.LinkBoxAsync(
                firstProject.Id,
                secondFileBox.Id);
            await projectService.SetLinkedBoxAttachmentSideAsync(
                firstProject.Id,
                firstFileBox.Id,
                ProjectAttachmentSide.Bottom);
            await projectService.SetLinkedBoxVisibilityAsync(
                firstProject.Id,
                firstFileBox.Id,
                isVisible: false);
            await projectService.LinkPaperAsync(
                firstProject.Id,
                "paper-diagnosis",
                ProjectAttachmentSide.Top);

            var persistedFirstProject = new ProjectService(
                new DrawerRepository(paths.DatabasePath));
            var initialLinks = await persistedFirstProject.GetLinkedBoxesAsync(firstProject.Id);
            Assert.Equal(2, initialLinks.Count);
            Assert.Equal(
                ProjectAttachmentSide.Bottom,
                initialLinks.Single(link => link.LinkedBoxId == firstFileBox.Id).AttachmentSide);
            Assert.False(initialLinks.Single(link => link.LinkedBoxId == firstFileBox.Id).IsVisible);
            Assert.Single(await persistedFirstProject.GetLinkedPapersAsync(firstProject.Id));

            await projectService.UnlinkBoxAsync(firstProject.Id, secondFileBox.Id);
            await projectService.LinkBoxAsync(secondProject.Id, secondFileBox.Id);
            var afterMove = new ProjectService(
                new DrawerRepository(paths.DatabasePath));
            Assert.DoesNotContain(
                await afterMove.GetLinkedBoxesAsync(firstProject.Id),
                link => link.LinkedBoxId == secondFileBox.Id);
            Assert.Contains(
                await afterMove.GetLinkedBoxesAsync(secondProject.Id),
                link => link.LinkedBoxId == secondFileBox.Id);

            await drawerService.DeleteBoxAsync(firstFileBox.Id);
            var afterDelete = new ProjectService(
                new DrawerRepository(paths.DatabasePath));
            Assert.DoesNotContain(
                await afterDelete.GetLinkedBoxesAsync(firstProject.Id),
                link => link.LinkedBoxId == firstFileBox.Id);
            Assert.Single(await afterDelete.GetLinkedPapersAsync(firstProject.Id));

            await afterDelete.UnlinkPaperAsync(firstProject.Id, "paper-diagnosis");
            Assert.Empty(await afterDelete.GetLinkedPapersAsync(firstProject.Id));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ProjectAssociation_ReaddingSameBoxPreservesExistingLinkState()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Podo.Diagnosis",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var projectService = new ProjectService(repository);
            var project = await drawerService.CreateBoxAsync(
                "项目",
                BoxType.Project);
            var linkedBox = await drawerService.CreateBoxAsync(
                "资料",
                BoxType.Normal);
            await projectService.GetOrCreateProjectAsync(project.Id);

            var initial = await projectService.LinkBoxAsync(
                project.Id,
                linkedBox.Id);
            await projectService.SetLinkedBoxAttachmentSideAsync(
                project.Id,
                linkedBox.Id,
                ProjectAttachmentSide.Bottom);
            await projectService.SetLinkedBoxVisibilityAsync(
                project.Id,
                linkedBox.Id,
                isVisible: false);

            // Simulate dropping the already-linked box onto the same project again.
            await projectService.LinkBoxAsync(project.Id, linkedBox.Id);

            var relinked = Assert.Single(
                await new ProjectService(new DrawerRepository(paths.DatabasePath))
                    .GetLinkedBoxesAsync(project.Id));
            Assert.Equal(initial.SortOrder, relinked.SortOrder);
            Assert.Equal(ProjectAttachmentSide.Bottom, relinked.AttachmentSide);
            Assert.False(relinked.IsVisible);

            var initialPaper = await projectService.LinkPaperAsync(
                project.Id,
                "paper-diagnosis-relink",
                ProjectAttachmentSide.Top);
            await projectService.SetLinkedPaperVisibilityAsync(
                project.Id,
                "paper-diagnosis-relink",
                isVisible: false);
            await projectService.LinkPaperAsync(
                project.Id,
                "paper-diagnosis-relink",
                ProjectAttachmentSide.Bottom);

            var relinkedPaper = Assert.Single(
                await new ProjectService(new DrawerRepository(paths.DatabasePath))
                    .GetLinkedPapersAsync(project.Id));
            Assert.Equal(initialPaper.SortOrder, relinkedPaper.SortOrder);
            Assert.Equal(ProjectAttachmentSide.Bottom, relinkedPaper.AttachmentSide);
            Assert.False(relinkedPaper.IsVisible);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task BoundBox_ConcurrentImportAndRefreshDoesNotCreateDuplicateRows()
    {
        var root = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".scratch",
            "diagnosis-runtime",
            Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(
            Path.GetTempPath(),
            "Podo.Diagnosis.Source",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();

            var boundFolder = Path.Combine(root, "target");
            Directory.CreateDirectory(boundFolder);
            Directory.CreateDirectory(sourceRoot);
            var sourcePath = Path.Combine(sourceRoot, "large.bin");
            await using (var source = File.Create(sourcePath))
            {
                source.SetLength(32 * 1024 * 1024);
            }

            var box = await drawerService.CreateBoundBoxAsync(
                "目标收纳盒",
                boundFolder);
            var importTask = drawerService.ImportPathAsync(box.Id, sourcePath);
            var refreshTask = Task.Run(async () =>
            {
                while (!importTask.IsCompleted)
                {
                    await drawerService.GetItemsAsync(box.Id);
                    await Task.Yield();
                }

                for (var index = 0; index < 12; index++)
                {
                    await drawerService.GetItemsAsync(box.Id);
                }
            });

            await Task.WhenAll(importTask, refreshTask);

            var items = await drawerService.GetItemsAsync(box.Id);
            var item = Assert.Single(items);
            Assert.Equal(
                Path.Combine(boundFolder, "large.bin"),
                item.StoredPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }

            try
            {
                if (Directory.Exists(sourceRoot))
                {
                    Directory.Delete(sourceRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task BoundBoxImport_RestartKeepsFileIdentityAndSinglePersistedRow()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Podo.Diagnosis",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();

            var boundFolder = Path.Combine(root, "target");
            var sourceFolder = Path.Combine(root, "source");
            Directory.CreateDirectory(boundFolder);
            Directory.CreateDirectory(sourceFolder);
            var sourcePath = Path.Combine(sourceFolder, "persisted.txt");
            await File.WriteAllTextAsync(sourcePath, "persisted");

            var box = await drawerService.CreateBoundBoxAsync(
                "目标收纳盒",
                boundFolder);
            var imported = await drawerService.ImportPathAsync(box.Id, sourcePath);

            var restartedService = new DrawerService(
                paths,
                new DrawerRepository(paths.DatabasePath));
            await restartedService.InitializeAsync();
            var reloadedBox = (await restartedService.GetBoxesAsync())
                .Single(candidate => candidate.Id == box.Id);
            var reloadedItems = await restartedService.GetItemsAsync(reloadedBox.Id);

            var reloaded = Assert.Single(reloadedItems);
            Assert.Equal(imported.Id, reloaded.Id);
            Assert.Equal(sourcePath, reloaded.SourcePath);
            Assert.Equal(
                Path.Combine(boundFolder, "persisted.txt"),
                reloaded.StoredPath);
            Assert.True(File.Exists(reloaded.StoredPath));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<(
        int ExistingColumn,
        int ExistingRow,
        int NewColumn,
        int NewRow,
        int ItemCount)>
        RunBoundImportScenarioAsync(bool persistExistingPosition)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Podo.Diagnosis",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();

            var boundFolder = Path.Combine(root, "target");
            Directory.CreateDirectory(boundFolder);
            var existingPath = Path.Combine(boundFolder, "existing.txt");
            await File.WriteAllTextAsync(existingPath, "existing");

            var box = await drawerService.CreateBoundBoxAsync(
                "目标收纳盒",
                boundFolder);
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern);
            await viewModel.LoadAsync();

            var existingBeforeImport = Assert.Single(
                viewModel.Items,
                item => item.DisplayName == "existing.txt");
            Assert.Equal(0, existingBeforeImport.GridColumn);
            Assert.Equal(0, existingBeforeImport.GridRow);

            if (persistExistingPosition)
            {
                await drawerService.UpdateItemGridPositionAsync(
                    existingBeforeImport.Id,
                    0,
                    0);
            }

            var sourceDirectory = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDirectory);
            var newPath = Path.Combine(sourceDirectory, "new.txt");
            await File.WriteAllTextAsync(newPath, "new");

            await viewModel.ImportPathsAsync([newPath], startColumn: 0, startRow: 0);

            var existingAfterImport = Assert.Single(
                viewModel.Items,
                item => item.DisplayName == "existing.txt");
            var newAfterImport = Assert.Single(
                viewModel.Items,
                item => item.DisplayName == "new.txt");
            return (
                existingAfterImport.GridColumn,
                existingAfterImport.GridRow,
                newAfterImport.GridColumn,
                newAfterImport.GridRow,
                viewModel.Items.Count);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(
            string path,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
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
