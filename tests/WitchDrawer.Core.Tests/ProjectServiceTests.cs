using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;
using Microsoft.Data.Sqlite;

namespace WitchDrawer.Core.Tests;

public sealed class ProjectServiceTests
{
    [Fact]
    public async Task ProjectIssueLifecycle_PersistsIndependentStatesAndReopensPreviousState()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);

        var project = await projectService.GetOrCreateProjectAsync(workspace.ProjectBox.Id);
        Assert.Equal(ProjectStageCatalog.DefaultStage, project.Stage);
        var updatedProject = await projectService.UpdateProjectAsync(
            project with
            {
                Stage = ProjectStage.Development,
                OwnerName = "小林",
                Description = "验证项目阶段和问题清单"
            });

        var issue = await projectService.AddIssueAsync(
            workspace.ProjectBox.Id,
            "移动端首屏加载过慢",
            "首屏资源加载超过目标时间。",
            ProjectSolutionState.Proposed,
            "拆分首屏资源并延迟加载次要模块。",
            ProjectPriority.High,
            "小林",
            null);

        var inProgress = await projectService.SetResolutionStateAsync(
            issue.Id,
            ProjectResolutionState.InProgress);
        var resolved = await projectService.ResolveIssueAsync(issue.Id);

        Assert.Equal(ProjectStage.Development, updatedProject.Stage);
        Assert.Equal("小林", updatedProject.OwnerName);
        Assert.Equal(ProjectSolutionState.Proposed, resolved.SolutionState);
        Assert.Equal(ProjectResolutionState.InProgress, resolved.PreviousResolutionState);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal(ProjectResolutionState.InProgress, inProgress.ResolutionState);
        Assert.Empty(await projectService.GetIssuesAsync(workspace.ProjectBox.Id, false));
        Assert.Single(await projectService.GetIssuesAsync(workspace.ProjectBox.Id, true));

        var reopened = await projectService.ReopenIssueAsync(issue.Id);
        Assert.Equal(ProjectResolutionState.InProgress, reopened.ResolutionState);
        Assert.Null(reopened.ResolvedAt);
        Assert.Null(reopened.PreviousResolutionState);
        Assert.Single(await projectService.GetIssuesAsync(workspace.ProjectBox.Id, false));
    }

    [Fact]
    public async Task ProjectBoxRejectsFileImportAndCascadeDeletesProjectData()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        await projectService.GetOrCreateProjectAsync(workspace.ProjectBox.Id);
        await projectService.AddIssueAsync(
            workspace.ProjectBox.Id,
            "需要确认的事项",
            string.Empty,
            ProjectSolutionState.None,
            string.Empty,
            ProjectPriority.Normal,
            string.Empty,
            null);

        var sourcePath = Path.Combine(workspace.Root, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "do not move");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.DrawerService.ImportPathAsync(workspace.ProjectBox.Id, sourcePath));
        Assert.Contains("项目盒", exception.Message);
        Assert.True(File.Exists(sourcePath));

        await workspace.DrawerService.DeleteBoxAsync(workspace.ProjectBox.Id);
        Assert.Null(await workspace.Repository.GetProjectAsync(workspace.ProjectBox.Id));
        Assert.Empty(await workspace.Repository.GetProjectIssuesAsync(
            workspace.ProjectBox.Id,
            includeResolved: true));
    }

    [Fact]
    public async Task ProjectFolders_GroupMoveAndDissolveWithoutChangingProjects()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        var folderService = new ProjectFolderService(workspace.Repository);
        var secondProject = await workspace.DrawerService.CreateBoxAsync("第二个项目", BoxType.Project);
        var thirdProject = await workspace.DrawerService.CreateBoxAsync("第三个项目", BoxType.Project);
        await projectService.GetOrCreateProjectAsync(workspace.ProjectBox.Id);
        await projectService.GetOrCreateProjectAsync(secondProject.Id);
        await projectService.GetOrCreateProjectAsync(thirdProject.Id);
        await projectService.UpdateProjectAsync(
            (await projectService.GetOrCreateProjectAsync(secondProject.Id)) with
            {
                Stage = ProjectStage.Acceptance
            });

        var folder = await folderService.CreateAsync(
            "项目文件夹",
            [workspace.ProjectBox.Id, secondProject.Id]);

        var initialMembers = await folderService.GetMembersAsync(folder.Id);
        Assert.Equal(2, initialMembers.Count);
        Assert.Equal(ProjectStage.Acceptance, initialMembers.Single(member =>
            member.ProjectBoxId == secondProject.Id).Stage);
        Assert.Equal(folder.Id, await folderService.GetFolderForProjectAsync(secondProject.Id));

        await folderService.AddProjectAsync(folder.Id, thirdProject.Id);
        Assert.Equal(3, (await folderService.GetMembersAsync(folder.Id)).Count);

        await folderService.RemoveProjectAsync(thirdProject.Id);
        Assert.NotNull(await workspace.Repository.GetBoxAsync(folder.Id));
        await folderService.RemoveProjectAsync(secondProject.Id);

        Assert.Null(await workspace.Repository.GetBoxAsync(folder.Id));
        Assert.Null(await folderService.GetFolderForProjectAsync(workspace.ProjectBox.Id));
        Assert.NotNull(await workspace.Repository.GetProjectAsync(workspace.ProjectBox.Id));
        Assert.NotNull(await workspace.Repository.GetProjectAsync(secondProject.Id));
    }

    [Fact]
    public async Task ProjectFolderMember_CanMoveBeforeAnotherMemberAndPersistOrder()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        var folderService = new ProjectFolderService(workspace.Repository);
        var secondProject = await workspace.DrawerService.CreateBoxAsync("第二个项目", BoxType.Project);
        var thirdProject = await workspace.DrawerService.CreateBoxAsync("第三个项目", BoxType.Project);
        await projectService.GetOrCreateProjectAsync(workspace.ProjectBox.Id);
        await projectService.GetOrCreateProjectAsync(secondProject.Id);
        await projectService.GetOrCreateProjectAsync(thirdProject.Id);
        var folder = await folderService.CreateAsync(
            "可排序文件夹",
            [workspace.ProjectBox.Id, secondProject.Id, thirdProject.Id]);

        await folderService.MoveProjectBeforeAsync(
            folder.Id,
            thirdProject.Id,
            workspace.ProjectBox.Id);

        var persistedMembers = await new ProjectFolderService(workspace.Repository)
            .GetMembersAsync(folder.Id);
        Assert.Equal(
            [thirdProject.Id, workspace.ProjectBox.Id, secondProject.Id],
            persistedMembers.Select(member => member.ProjectBoxId));
    }

    [Fact]
    public async Task ArchivingOrDeletingFolderMember_RemovesMembershipAndDissolvesSparseFolder()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        var folderService = new ProjectFolderService(workspace.Repository);
        var secondProject = await workspace.DrawerService.CreateBoxAsync("第二个项目", BoxType.Project);
        await projectService.GetOrCreateProjectAsync(workspace.ProjectBox.Id);
        await projectService.GetOrCreateProjectAsync(secondProject.Id);

        var archivedFolder = await folderService.CreateAsync(
            "待归档",
            [workspace.ProjectBox.Id, secondProject.Id]);
        await projectService.ArchiveProjectAsync(secondProject.Id);

        Assert.Null(await workspace.Repository.GetBoxAsync(archivedFolder.Id));
        Assert.Null(await folderService.GetFolderForProjectAsync(workspace.ProjectBox.Id));

        await projectService.RestoreProjectAsync(secondProject.Id);
        var deletedFolder = await folderService.CreateAsync(
            "待删除",
            [workspace.ProjectBox.Id, secondProject.Id]);
        await workspace.DrawerService.DeleteBoxAsync(secondProject.Id);

        Assert.Null(await workspace.Repository.GetBoxAsync(deletedFolder.Id));
        Assert.NotNull(await workspace.Repository.GetProjectAsync(workspace.ProjectBox.Id));
    }

    [Fact]
    public async Task ProjectBoxLinksPersistVisibilityAndCascadeWhenLinkedBoxIsDeleted()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        var fileBox = await workspace.DrawerService.CreateBoxAsync("项目资料", BoxType.Normal);
        var todoBox = await workspace.DrawerService.CreateBoxAsync("不应关联", BoxType.Todo);

        var available = await projectService.GetLinkableBoxesAsync(workspace.ProjectBox.Id);
        Assert.Contains(available, box => box.Id == fileBox.Id);
        Assert.DoesNotContain(available, box => box.Id == todoBox.Id);

        var link = await projectService.LinkBoxAsync(workspace.ProjectBox.Id, fileBox.Id);
        Assert.Equal(fileBox.Id, link.LinkedBoxId);
        Assert.Equal(ProjectAttachmentSide.Right, link.AttachmentSide);
        Assert.Equal(
            workspace.ProjectBox.Id,
            await projectService.GetProjectBoxForLinkedBoxAsync(fileBox.Id));
        Assert.Single(await projectService.GetLinkedBoxesAsync(workspace.ProjectBox.Id));
        Assert.DoesNotContain(
            await projectService.GetLinkableBoxesAsync(workspace.ProjectBox.Id),
            box => box.Id == fileBox.Id);

        await projectService.SetLinkedBoxVisibilityAsync(
            workspace.ProjectBox.Id,
            fileBox.Id,
            isVisible: false);
        var hiddenLink = Assert.Single(
            await projectService.GetLinkedBoxesAsync(workspace.ProjectBox.Id));
        Assert.False(hiddenLink.IsVisible);

        await projectService.SetLinkedBoxAttachmentSideAsync(
            workspace.ProjectBox.Id,
            fileBox.Id,
            ProjectAttachmentSide.Bottom);
        var bottomLink = Assert.Single(
            await projectService.GetLinkedBoxesAsync(workspace.ProjectBox.Id));
        Assert.Equal(ProjectAttachmentSide.Bottom, bottomLink.AttachmentSide);

        await workspace.DrawerService.DeleteBoxAsync(fileBox.Id);
        Assert.Empty(await projectService.GetLinkedBoxesAsync(workspace.ProjectBox.Id));
        Assert.Null(await projectService.GetProjectBoxForLinkedBoxAsync(fileBox.Id));
    }

    [Fact]
    public async Task ProjectBoxLinks_ConcurrentCrossProjectAttemptsKeepOneOwner()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var firstService = new ProjectService(workspace.Repository);
        var secondService = new ProjectService(new DrawerRepository(
            Path.Combine(workspace.Root, AppPaths.DatabaseFileName)));
        var otherProject = await workspace.DrawerService.CreateBoxAsync("并发目标项目", BoxType.Project);
        string? duplicateEvidence = null;

        for (var attempt = 0; attempt < 80 && duplicateEvidence is null; attempt++)
        {
            var fileBox = await workspace.DrawerService.CreateBoxAsync(
                $"并发资料 {attempt}",
                BoxType.Normal);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstAttempt = Task.Run(async () =>
            {
                await release.Task;
                try
                {
                    await firstService.LinkBoxAsync(workspace.ProjectBox.Id, fileBox.Id);
                    return (Exception?)null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });
            var secondAttempt = Task.Run(async () =>
            {
                await release.Task;
                try
                {
                    await secondService.LinkBoxAsync(otherProject.Id, fileBox.Id);
                    return (Exception?)null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });

            release.SetResult(true);
            await Task.WhenAll(firstAttempt, secondAttempt);

            var restartedService = new ProjectService(new DrawerRepository(
                Path.Combine(workspace.Root, AppPaths.DatabaseFileName)));
            var firstLinks = await restartedService.GetLinkedBoxesAsync(workspace.ProjectBox.Id);
            var secondLinks = await restartedService.GetLinkedBoxesAsync(otherProject.Id);
            if (firstLinks.Any(link => link.LinkedBoxId == fileBox.Id)
                && secondLinks.Any(link => link.LinkedBoxId == fileBox.Id))
            {
                duplicateEvidence = $"Attempt {attempt} linked {fileBox.Id} to both "
                    + $"{workspace.ProjectBox.Id} and {otherProject.Id}.";
            }
        }

        Assert.True(
            duplicateEvidence is null,
            duplicateEvidence ?? "A linked file box must have exactly one project owner.");
    }

    [Fact]
    public async Task ProjectBoxLinks_MoveToAnotherProjectTransfersTheSingleOwner()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        var otherProject = await workspace.DrawerService.CreateBoxAsync("接收项目", BoxType.Project);
        var fileBox = await workspace.DrawerService.CreateBoxAsync("待移动资料", BoxType.Normal);
        await projectService.LinkBoxAsync(workspace.ProjectBox.Id, fileBox.Id);

        var moved = await projectService.MoveBoxLinkAsync(
            otherProject.Id,
            fileBox.Id,
            ProjectAttachmentSide.Left);

        Assert.Equal(otherProject.Id, moved.ProjectBoxId);
        Assert.Equal(fileBox.Id, moved.LinkedBoxId);
        Assert.Equal(ProjectAttachmentSide.Left, moved.AttachmentSide);
        Assert.Empty(await projectService.GetLinkedBoxesAsync(workspace.ProjectBox.Id));
        var targetLink = Assert.Single(await projectService.GetLinkedBoxesAsync(otherProject.Id));
        Assert.Equal(fileBox.Id, targetLink.LinkedBoxId);
        Assert.Equal(ProjectAttachmentSide.Left, targetLink.AttachmentSide);
        Assert.Equal(otherProject.Id, await projectService.GetProjectBoxForLinkedBoxAsync(fileBox.Id));
    }

    [Fact]
    public async Task ProjectBoxLinks_ConcurrentDistinctLinksKeepStableSortOrderAfterRestart()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        var linkedBoxes = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(index => workspace.DrawerService.CreateBoxAsync(
                $"并发排序资料 {index:D2}",
                BoxType.Normal)));
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var linkAttempts = linkedBoxes.Select(linkedBox => Task.Run(async () =>
        {
            await release.Task;
            try
            {
                await new ProjectService(new DrawerRepository(
                    Path.Combine(workspace.Root, AppPaths.DatabaseFileName)))
                    .LinkBoxAsync(workspace.ProjectBox.Id, linkedBox.Id);
                return (Exception?)null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        })).ToArray();

        release.SetResult(true);
        var errors = await Task.WhenAll(linkAttempts);
        Assert.DoesNotContain(errors, error => error is not null);

        var restartedService = new ProjectService(new DrawerRepository(
            Path.Combine(workspace.Root, AppPaths.DatabaseFileName)));
        var links = await restartedService.GetLinkedBoxesAsync(workspace.ProjectBox.Id);
        var missingLinkedBoxIds = linkedBoxes
            .Select(box => box.Id)
            .Except(links.Select(link => link.LinkedBoxId))
            .ToArray();
        Assert.True(
            links.Count == linkedBoxes.Length,
            $"Expected {linkedBoxes.Length} successful links but found {links.Count}. Missing: "
            + string.Join(", ", missingLinkedBoxIds)
            + ". Returned sort orders: "
            + string.Join(", ", links.Select(link => link.SortOrder)));
        var duplicateSortOrders = links
            .GroupBy(link => link.SortOrder)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(link => link.LinkedBoxName))}")
            .ToArray();
        Assert.True(
            duplicateSortOrders.Length == 0,
            "Concurrent links must retain a unique, restart-stable order. Duplicates: "
            + string.Join("; ", duplicateSortOrders));
    }

    [Fact]
    public async Task ProjectPaperLinksPersistSideAndAllowOnlyOneProject()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        var otherProject = await workspace.DrawerService.CreateBoxAsync("另一个项目", BoxType.Project);

        var link = await projectService.LinkPaperAsync(
            workspace.ProjectBox.Id,
            "paper-001",
            ProjectAttachmentSide.Top);

        Assert.Equal(ProjectAttachmentSide.Top, link.AttachmentSide);
        Assert.Single(await projectService.GetLinkedPapersAsync(workspace.ProjectBox.Id));
        Assert.Equal(
            workspace.ProjectBox.Id,
            await projectService.GetProjectBoxForLinkedPaperAsync("paper-001"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => projectService.LinkPaperAsync(
                otherProject.Id,
                "paper-001",
                ProjectAttachmentSide.Left));
        Assert.Contains("另一个项目", exception.Message);

        await projectService.UnlinkPaperAsync(workspace.ProjectBox.Id, "paper-001");
        Assert.Empty(await projectService.GetLinkedPapersAsync(workspace.ProjectBox.Id));
        Assert.Null(await projectService.GetProjectBoxForLinkedPaperAsync("paper-001"));
    }

    [Fact]
    public async Task ProjectArchive_HidesLinkedBoxesAndKeepsLinksForRestore()
    {
        using var workspace = await ProjectWorkspace.CreateAsync();
        var projectService = new ProjectService(workspace.Repository);
        var fileBox = await workspace.DrawerService.CreateBoxAsync("项目资料", BoxType.Normal);
        var sourcePath = Path.Combine(workspace.Root, "project-archive-item.txt");
        await File.WriteAllTextAsync(sourcePath, "archive coverage");
        var importedItem = await workspace.DrawerService.ImportPathAsync(fileBox.Id, sourcePath);
        await projectService.LinkBoxAsync(workspace.ProjectBox.Id, fileBox.Id);
        await projectService.LinkPaperAsync(
            workspace.ProjectBox.Id,
            "paper-archive-001",
            ProjectAttachmentSide.Left);

        var archived = await projectService.ArchiveProjectAsync(workspace.ProjectBox.Id);

        Assert.True(archived.ProjectBox.IsArchived);
        Assert.Single(archived.LinkedBoxes);
        Assert.Single(archived.LinkedPapers);
        Assert.True((await workspace.Repository.GetBoxAsync(workspace.ProjectBox.Id))!.IsArchived);
        Assert.True((await workspace.Repository.GetBoxAsync(fileBox.Id))!.IsArchived);
        Assert.DoesNotContain(
            await workspace.DrawerService.GetBoxesAsync(),
            box => box.Id == workspace.ProjectBox.Id || box.Id == fileBox.Id);
        Assert.DoesNotContain(
            await workspace.DrawerService.GetAllItemsAsync(),
            item => item.Id == importedItem.Id);
        Assert.Contains(
            await workspace.DrawerService.GetArchivedBoxesAsync(),
            box => box.Id == workspace.ProjectBox.Id);
        Assert.Equal(
            workspace.ProjectBox.Id,
            await projectService.GetProjectBoxForLinkedBoxAsync(fileBox.Id));
        Assert.Equal(
            workspace.ProjectBox.Id,
            await projectService.GetProjectBoxForLinkedPaperAsync("paper-archive-001"));

        var restored = await projectService.RestoreProjectAsync(workspace.ProjectBox.Id);

        Assert.False(restored.ProjectBox.IsArchived);
        Assert.False((await workspace.Repository.GetBoxAsync(workspace.ProjectBox.Id))!.IsArchived);
        Assert.False((await workspace.Repository.GetBoxAsync(fileBox.Id))!.IsArchived);
        Assert.Contains(
            await workspace.DrawerService.GetBoxesAsync(),
            box => box.Id == workspace.ProjectBox.Id);
        Assert.Contains(
            await workspace.DrawerService.GetBoxesAsync(),
            box => box.Id == fileBox.Id);
        Assert.Contains(
            await workspace.DrawerService.GetAllItemsAsync(),
            item => item.Id == importedItem.Id);
        Assert.Empty(await workspace.DrawerService.GetArchivedBoxesAsync());
        Assert.Single(await projectService.GetLinkedBoxesAsync(workspace.ProjectBox.Id));
        Assert.Single(await projectService.GetLinkedPapersAsync(workspace.ProjectBox.Id));
    }

    [Fact]
    public async Task RepositoryInitialization_DeduplicatesLegacyProjectPaperLinks()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Podo.ProjectServiceTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var databasePath = Path.Combine(root, "podo.db");
            Directory.CreateDirectory(root);
            var projectId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow.ToString("O");
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE ProjectPaperLinks (
                        ProjectBoxId TEXT NOT NULL,
                        PaperId TEXT NOT NULL,
                        AttachmentSide INTEGER NOT NULL,
                        IsVisible INTEGER NOT NULL,
                        SortOrder INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    INSERT INTO ProjectPaperLinks VALUES
                        ($projectId, 'paper-001', 0, 1, 0, $now, $now),
                        ($projectId, 'paper-001', 2, 1, 1, $now, $now);
                    """;
                command.Parameters.AddWithValue("$projectId", projectId.ToString());
                command.Parameters.AddWithValue("$now", now);
                await command.ExecuteNonQueryAsync();
            }

            var repository = new DrawerRepository(databasePath);
            await repository.InitializeAsync();

            await using var verificationConnection = new SqliteConnection($"Data Source={databasePath}");
            await verificationConnection.OpenAsync();
            var verificationCommand = verificationConnection.CreateCommand();
            verificationCommand.CommandText =
                "SELECT COUNT(*) FROM ProjectPaperLinks WHERE PaperId = 'paper-001';";
            Assert.Equal(1L, (long)(await verificationCommand.ExecuteScalarAsync())!);
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
    public async Task RepositoryInitialization_DeduplicatesLegacyProjectBoxLinks()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Podo.ProjectServiceTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var databasePath = Path.Combine(root, "podo.db");
            Directory.CreateDirectory(root);
            var firstProjectId = Guid.NewGuid();
            var secondProjectId = Guid.NewGuid();
            var linkedBoxId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow.ToString("O");
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE ProjectBoxLinks (
                        ProjectBoxId TEXT NOT NULL,
                        LinkedBoxId TEXT NOT NULL,
                        IsVisible INTEGER NOT NULL,
                        AttachmentSide INTEGER NOT NULL,
                        SortOrder INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    INSERT INTO ProjectBoxLinks VALUES
                        ($firstProjectId, $linkedBoxId, 1, 0, 0, $now, $now),
                        ($secondProjectId, $linkedBoxId, 1, 1, 1, $now, $now);
                    """;
                command.Parameters.AddWithValue("$firstProjectId", firstProjectId.ToString());
                command.Parameters.AddWithValue("$secondProjectId", secondProjectId.ToString());
                command.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
                command.Parameters.AddWithValue("$now", now);
                await command.ExecuteNonQueryAsync();
            }

            var repository = new DrawerRepository(databasePath);
            await repository.InitializeAsync();

            await using var verificationConnection = new SqliteConnection($"Data Source={databasePath}");
            await verificationConnection.OpenAsync();
            var countCommand = verificationConnection.CreateCommand();
            countCommand.CommandText =
                "SELECT COUNT(*) FROM ProjectBoxLinks WHERE LinkedBoxId = $linkedBoxId;";
            countCommand.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
            Assert.Equal(1L, (long)(await countCommand.ExecuteScalarAsync())!);

            var duplicateCommand = verificationConnection.CreateCommand();
            duplicateCommand.CommandText =
                "INSERT INTO ProjectBoxLinks VALUES ($projectBoxId, $linkedBoxId, 1, 0, 2, $now, $now);";
            duplicateCommand.Parameters.AddWithValue("$projectBoxId", Guid.NewGuid().ToString());
            duplicateCommand.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
            duplicateCommand.Parameters.AddWithValue("$now", now);
            await Assert.ThrowsAsync<SqliteException>(() => duplicateCommand.ExecuteNonQueryAsync());
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

    private sealed class ProjectWorkspace : IDisposable
    {
        private ProjectWorkspace(
            string root,
            DrawerRepository repository,
            DrawerService drawerService,
            Box projectBox)
        {
            Root = root;
            Repository = repository;
            DrawerService = drawerService;
            ProjectBox = projectBox;
        }

        public string Root { get; }

        public DrawerRepository Repository { get; }

        public DrawerService DrawerService { get; }

        public Box ProjectBox { get; }

        public static async Task<ProjectWorkspace> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "Podo.ProjectServiceTests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var projectBox = await drawerService.CreateBoxAsync(
                "项目收纳盒",
                BoxType.Project);
            return new ProjectWorkspace(root, repository, drawerService, projectBox);
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
            }
        }
    }
}
