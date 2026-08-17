using System.IO;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class ProjectManagementViewModelTests
{
    [Fact]
    public async Task SwitchingProjects_ExpandsModulesAndKeepsOnlyModuleStates()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var firstProject = await drawerService.CreateBoxAsync("项目一", BoxType.Project);
            var secondProject = await drawerService.CreateBoxAsync("项目二", BoxType.Project);
            var viewModel = new ProjectManagementViewModel(
                new ProjectService(repository),
                new NoOpLogger());

            await viewModel.LoadAsync(firstProject.Id, firstProject.Name);

            Assert.True(viewModel.AreModulesExpanded);
            Assert.Equal(
                ["未开发", "开发完成", "上线完成"],
                viewModel.ModuleStateOptions.Select(option => option.Name));

            viewModel.NewModuleTitle = "桌面模块";
            await viewModel.AddModuleCommand.ExecuteAsync(null);

            Assert.Single(viewModel.Modules);
            Assert.True(viewModel.AreModulesExpanded);

            await viewModel.LoadAsync(secondProject.Id, secondProject.Name);

            Assert.True(viewModel.AreModulesExpanded);
            Assert.Empty(viewModel.Modules);
            Assert.Equal(secondProject.Id, viewModel.BoxId);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task DeleteModuleCommand_RemovesModuleAndRefreshesProjectCounts()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var project = await drawerService.CreateBoxAsync("项目收纳盒", BoxType.Project);
            var viewModel = new ProjectManagementViewModel(
                new ProjectService(repository),
                new NoOpLogger());

            await viewModel.LoadAsync(project.Id, project.Name);
            viewModel.NewModuleTitle = "可删除模块";
            await viewModel.AddModuleCommand.ExecuteAsync(null);

            await viewModel.DeleteModuleCommand.ExecuteAsync(viewModel.Modules.Single());

            Assert.Empty(viewModel.Modules);
            Assert.Equal(0, viewModel.TotalIssueCount);
            Assert.Equal("已删除模块“可删除模块”", viewModel.StatusText);
            Assert.Empty(await new ProjectService(repository).GetIssuesAsync(
                project.Id,
                includeResolved: true));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

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
