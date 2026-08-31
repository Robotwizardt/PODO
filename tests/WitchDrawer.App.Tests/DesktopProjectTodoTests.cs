using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

[Collection(WpfWindowTestCollection.Name)]
public sealed class DesktopProjectTodoTests
{
    [Fact]
    public void ProjectFolderMemberIcon_IdentifiesProjectAndSummarizesStage()
    {
        var now = DateTimeOffset.UtcNow;
        var member = new ProjectFolderMember(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "桌面工具",
            ProjectStage.Development,
            0,
            now,
            now);

        var viewModel = new ProjectFolderMemberViewModel(member, 5);

        Assert.Equal("桌面", viewModel.IconText);
        Assert.Equal("桌面工具 · 执行开发 · 5 项待办", viewModel.SummaryLabel);
    }

    [Fact]
    public async Task DesktopProjectModules_AddsAndUpdatesWithoutTheManager()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var projectBox = await drawerService.CreateBoxAsync("项目收纳盒", BoxType.Project);
            var viewModel = new DesktopBoxViewModel(
                projectBox,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern,
                projectService: new ProjectService(repository));

            await viewModel.LoadAsync();
            Assert.False(viewModel.AreProjectModulesExpanded);
            viewModel.NewProjectModuleTitle = "从桌面直接添加";

            Assert.True(viewModel.AddProjectModuleCommand.CanExecute(null));
            await viewModel.AddProjectModuleCommand.ExecuteAsync(null);

            var module = Assert.Single(viewModel.ProjectModules);
            Assert.Equal("从桌面直接添加", module.Title);
            Assert.Equal(ProjectResolutionState.NotDeveloped, module.ModuleState);
            Assert.Equal("", viewModel.NewProjectModuleTitle);

            module.ModuleState = ProjectResolutionState.DevelopmentCompleted;
            await viewModel.UpdateProjectModuleStateCommand.ExecuteAsync(module);
            Assert.Equal(ProjectResolutionState.DevelopmentCompleted, viewModel.ProjectModules.Single().ModuleState);

            module = viewModel.ProjectModules.Single();
            module.ModuleState = ProjectResolutionState.Released;
            await viewModel.UpdateProjectModuleStateCommand.ExecuteAsync(module);

            Assert.Single(viewModel.ProjectModules);
            Assert.Equal(1, viewModel.ProjectCompletedIssueCount);
            Assert.Equal("1/1 已上线", viewModel.ProjectModuleSummary);

            await viewModel.DeleteProjectModuleCommand.ExecuteAsync(viewModel.ProjectModules.Single());

            Assert.Empty(viewModel.ProjectModules);
            Assert.Equal(0, viewModel.ProjectTotalIssueCount);
            Assert.Empty(await new ProjectService(repository).GetIssuesAsync(projectBox.Id, includeResolved: true));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task DesktopProjectModules_AttachmentsTrackDirectionAndUnlinkCount()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var projectBox = await drawerService.CreateBoxAsync("项目收纳盒", BoxType.Project);
            var fileBox = await drawerService.CreateBoxAsync("项目资料", BoxType.Normal);
            var projectService = new ProjectService(repository);
            var viewModel = new DesktopBoxViewModel(
                projectBox,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern,
                projectService: projectService);

            await viewModel.LoadAsync();

            Assert.True(await viewModel.LinkProjectBoxAtSideAsync(
                fileBox.Id,
                ProjectAttachmentSide.Left));
            Assert.Contains("已关联文件收纳盒到左侧", viewModel.ProjectAssociationMessage);
            Assert.True(await viewModel.LinkProjectPaperAtSideAsync(
                "paper-001",
                ProjectAttachmentSide.Bottom));

            Assert.Equal(2, viewModel.ProjectAttachmentCount);
            Assert.Equal(1, viewModel.ProjectLeftAttachmentCount);
            Assert.Equal(1, viewModel.ProjectBottomAttachmentCount);
            Assert.True(viewModel.HasProjectLeftAttachments);
            Assert.True(viewModel.HasProjectBottomAttachments);
            Assert.False(viewModel.HasProjectTopAttachments);
            Assert.True(viewModel.IsProjectLeftAttachmentsVisible);
            await viewModel.ToggleProjectLeftAttachmentsCommand.ExecuteAsync(null);
            Assert.False(viewModel.IsProjectLeftAttachmentsVisible);
            await viewModel.ToggleProjectTopAttachmentsCommand.ExecuteAsync(null);
            await viewModel.ToggleProjectBottomAttachmentsCommand.ExecuteAsync(null);
            await viewModel.ToggleProjectRightAttachmentsCommand.ExecuteAsync(null);
            Assert.False(viewModel.IsProjectTopAttachmentsVisible);
            Assert.False(viewModel.IsProjectBottomAttachmentsVisible);
            Assert.False(viewModel.IsProjectRightAttachmentsVisible);
            Assert.True(viewModel.HasProjectAssociationMessage);
            var fileLink = Assert.Single(await projectService.GetLinkedBoxesAsync(projectBox.Id));
            Assert.Equal(ProjectAttachmentSide.Left, fileLink.AttachmentSide);
            var paperLink = Assert.Single(await projectService.GetLinkedPapersAsync(projectBox.Id));
            Assert.Equal(ProjectAttachmentSide.Bottom, paperLink.AttachmentSide);

            await projectService.UnlinkPaperAsync(projectBox.Id, "paper-001");
            await viewModel.RefreshProjectLinksAsync();
            Assert.Equal(1, viewModel.ProjectAttachmentCount);

            var reloadedViewModel = new DesktopBoxViewModel(
                projectBox,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern,
                projectService: projectService);
            await reloadedViewModel.LoadAsync();
            Assert.False(reloadedViewModel.IsProjectLeftAttachmentsVisible);
            Assert.False(reloadedViewModel.IsProjectTopAttachmentsVisible);
            Assert.False(reloadedViewModel.IsProjectBottomAttachmentsVisible);
            Assert.False(reloadedViewModel.IsProjectRightAttachmentsVisible);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task DesktopProjectFolder_ShowsAllMembersAndOpensSelectedProject()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var projectService = new ProjectService(repository);
            var folderService = new ProjectFolderService(repository);
            var firstProject = await drawerService.CreateBoxAsync("网站改版", BoxType.Project);
            var secondProject = await drawerService.CreateBoxAsync("桌面工具", BoxType.Project);
            await projectService.GetOrCreateProjectAsync(firstProject.Id);
            await projectService.GetOrCreateProjectAsync(secondProject.Id);
            await projectService.UpdateProjectAsync(
                (await projectService.GetOrCreateProjectAsync(secondProject.Id)) with
                {
                    Stage = ProjectStage.Development
                });
            var folder = await folderService.CreateAsync(
                "产品项目",
                [firstProject.Id, secondProject.Id]);
            await projectService.LinkPaperAsync(
                secondProject.Id,
                "todo-paper-a",
                ProjectAttachmentSide.Right);
            await projectService.LinkPaperAsync(
                secondProject.Id,
                "todo-paper-b",
                ProjectAttachmentSide.Right);
            var todoCountProvider = new StaticProjectTodoCountProvider(new Dictionary<string, int>
            {
                ["todo-paper-a"] = 2,
                ["todo-paper-b"] = 3
            });
            var viewModel = new DesktopBoxViewModel(
                folder,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern,
                projectService: projectService,
                projectFolderService: folderService,
                projectTodoCountProvider: todoCountProvider);

            await viewModel.LoadAsync();

            Assert.True(viewModel.IsProjectFolder);
            Assert.Equal(2, viewModel.ProjectFolderMembers.Count);
            Assert.Contains(viewModel.ProjectFolderMembers, member =>
                member.Name == "桌面工具"
                && member.StageName == "执行开发"
                && member.StageColor == ProjectStageCatalog.Get(ProjectStage.Development).Color
                && member.RemainingTodoCount == 5
                && member.HasRemainingTodos);

            Guid? openedProjectId = null;
            viewModel.ProjectFolderMemberOpenRequested += id => openedProjectId = id;
            var selectedMember = viewModel.ProjectFolderMembers.Single(member =>
                member.ProjectBoxId == secondProject.Id);
            await viewModel.OpenProjectFolderMemberCommand.ExecuteAsync(selectedMember);
            Assert.Equal(secondProject.Id, openedProjectId);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task DesktopProjectFolder_UsesContentAdaptiveWindowSizing()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var folder = await drawerService.CreateBoxAsync("项目文件夹", BoxType.ProjectFolder);
            var viewModel = new DesktopBoxViewModel(
                folder,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern);
            await viewModel.LoadAsync();

            Assert.True(viewModel.IsProjectFolder);
            Assert.Equal(
                System.Windows.Controls.ScrollBarVisibility.Auto,
                viewModel.GridHorizontalScrollBarVisibility);
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
    public async Task DesktopFileManagement_CreatesRenamesAndPastesThroughViewModel()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("普通收纳盒", BoxType.Normal);
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new NoOpLogger(),
                BoxVisualStyle.Modern);
            await viewModel.LoadAsync();

            var fileId = await viewModel.CreateFileSystemItemAsync(ItemKind.File);
            var folderId = await viewModel.CreateFileSystemItemAsync(ItemKind.Directory);
            var fileItem = viewModel.Items.Single(item => item.Id == fileId);
            Assert.True(await viewModel.RenameFileSystemItemAsync(fileItem, "桌面改名.txt"));
            var clipboardFile = Path.Combine(root, "剪贴板.txt");
            await File.WriteAllTextAsync(clipboardFile, "clipboard");
            var pastedIds = await viewModel.CopyPathsIntoBoxAsync([clipboardFile]);

            Assert.NotEqual(Guid.Empty, fileId);
            Assert.NotEqual(Guid.Empty, folderId);
            Assert.Single(pastedIds);
            Assert.Equal(3, viewModel.Items.Count);
            Assert.Contains(viewModel.Items, item => item.DisplayName == "桌面改名.txt");
            Assert.Contains(viewModel.Items, item => item.DisplayName == "剪贴板.txt");
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public void DesktopBoxWindow_AllDesktopBoxTypesLoadWithoutWindowErrors()
    {
        var root = CreateTempRoot();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Application? application = null;
            try
            {
                application = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                InitializeWindowTestResources(application);

                var (drawerService, repository) = CreateDrawerServiceAsync(root).GetAwaiter().GetResult();
                var boundFolder = Path.Combine(root, "bound-folder");
                Directory.CreateDirectory(boundFolder);
                var boxes = new[]
                {
                    drawerService.CreateBoxAsync("普通收纳盒", BoxType.Normal).GetAwaiter().GetResult(),
                    drawerService.CreateBoxAsync("映射收纳盒", BoxType.Mapping).GetAwaiter().GetResult(),
                    drawerService.CreateBoxAsync("像素收纳盒", BoxType.Pixel).GetAwaiter().GetResult(),
                    drawerService.CreateBoxAsync("抽屉收纳盒", BoxType.Drawer).GetAwaiter().GetResult(),
                    drawerService.CreateBoxAsync("项目收纳盒", BoxType.Project).GetAwaiter().GetResult(),
                    drawerService.CreateBoxAsync("项目文件夹", BoxType.ProjectFolder).GetAwaiter().GetResult(),
                    drawerService.CreateBoundBoxAsync("目标收纳盒", boundFolder).GetAwaiter().GetResult()
                };
                var searchableBox = boxes.Single(box => box.Type == BoxType.Normal);
                drawerService.CreateFileSystemItemAsync(
                        searchableBox.Id,
                        ItemKind.File,
                        "Project Plan.txt")
                    .GetAwaiter()
                    .GetResult();
                drawerService.CreateFileSystemItemAsync(
                        searchableBox.Id,
                        ItemKind.Directory,
                        "Project Archive")
                    .GetAwaiter()
                    .GetResult();
                drawerService.CreateFileSystemItemAsync(
                        searchableBox.Id,
                        ItemKind.File,
                        "Meeting Notes.txt")
                    .GetAwaiter()
                    .GetResult();

                foreach (var box in boxes)
                {
                    var viewModel = new DesktopBoxViewModel(
                        box,
                        drawerService,
                        new TodoService(repository),
                        new NoOpFileLauncher(),
                        new NoOpLogger(),
                        BoxVisualStyle.Modern,
                        projectService: new ProjectService(repository));
                    viewModel.LoadAsync().GetAwaiter().GetResult();

                    var window = new DesktopBoxWindow(viewModel)
                    {
                        Left = -10000,
                        Top = -10000,
                        ShowInTaskbar = false
                    };
                    try
                    {
                        window.Show();
                        window.UpdateLayout();
                        PumpDispatcher();

                        Assert.True(window.IsVisible, $"{box.Type} desktop window is not visible.");
                        Assert.True(window.ActualWidth > 0, $"{box.Type} desktop window has no width.");
                        Assert.True(window.ActualHeight > 0, $"{box.Type} desktop window has no height.");
                        if (box.Type == BoxType.Normal)
                        {
                            var searchItemsList = Assert.IsType<ListBox>(window.FindName("IconList"));
                            var searchBox = Assert.IsType<TextBox>(window.FindName("FileSearchBox"));
                            Assert.False(searchBox.IsVisible);

                            searchItemsList.RaiseEvent(new MouseWheelEventArgs(
                                Mouse.PrimaryDevice,
                                Environment.TickCount,
                                Mouse.MouseWheelDeltaForOneLine)
                            {
                                RoutedEvent = Mouse.PreviewMouseWheelEvent,
                                Source = searchItemsList
                            });
                            PumpDispatcher();

                            Assert.True(searchBox.IsVisible);
                            searchBox.Text = "project";
                            PumpDispatcher();

                            Assert.Equal(
                                ["Project Archive", "Project Plan.txt"],
                                searchItemsList.Items
                                    .Cast<DrawerItemViewModel>()
                                    .Select(item => item.DisplayName)
                                    .OrderBy(name => name, StringComparer.Ordinal)
                                    .ToArray());

                            searchItemsList.RaiseEvent(new MouseWheelEventArgs(
                                Mouse.PrimaryDevice,
                                Environment.TickCount,
                                -Mouse.MouseWheelDeltaForOneLine)
                            {
                                RoutedEvent = Mouse.PreviewMouseWheelEvent,
                                Source = searchItemsList
                            });
                            PumpDispatcher();

                            Assert.False(searchBox.IsVisible);
                            Assert.Equal(string.Empty, searchBox.Text);
                            Assert.Equal(3, searchItemsList.Items.Count);
                        }

                        var actionsButton = Assert.IsType<Button>(
                            window.FindName("ProjectDesktopActionsButton"));
                        Assert.True(
                            actionsButton.IsVisible,
                            $"{box.Type} desktop window does not expose its actions menu.");
                        actionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        PumpDispatcher();
                        var actionsPopup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(
                            window.FindName("ProjectDesktopActionsPopup"));
                        Assert.True(
                            actionsPopup.IsOpen,
                            $"{box.Type} desktop actions menu did not open.");
                        var windowSurface = Assert.IsType<Border>(window.FindName("WindowBorder"));
                        RaisePreviewMouseDown(windowSurface);
                        PumpDispatcher();
                        Assert.False(
                            actionsPopup.IsOpen,
                            $"{box.Type} desktop actions menu stayed open after an outside click.");
                        viewModel.ApplyTitleVisibility(false);
                        window.UpdateLayout();
                        PumpDispatcher();
                        Assert.False(viewModel.IsTitleVisible);
                        Assert.True(
                            viewModel.IsHeaderVisible,
                            $"{box.Type} desktop header disappeared when only its name was hidden.");
                        Assert.True(
                            actionsButton.IsVisible,
                            $"{box.Type} desktop actions menu disappeared with the hidden name.");
                        if (box.Type == BoxType.Normal)
                        {
                            AssertDesktopActionsMenuPhysicalClick(window, actionsButton, actionsPopup);
                        }

                        var iconList = Assert.IsType<ListBox>(window.FindName("IconList"));
                        var supportsFileManagement = box.Type is BoxType.Normal or BoxType.Bound;
                        Assert.Equal(
                            supportsFileManagement,
                            ContextMenuService.GetIsEnabled(iconList));
                        if (supportsFileManagement)
                        {
                            var fileMenu = Assert.IsType<ContextMenu>(iconList.ContextMenu);
                            var menuHeaders = fileMenu.Items
                                .OfType<MenuItem>()
                                .Select(item => item.Header as string)
                                .Where(header => header is not null)
                                .ToArray();
                            Assert.Contains("新建文件夹", menuHeaders);
                            Assert.Contains("新建文本文档", menuHeaders);
                            Assert.Contains("重命名", menuHeaders);
                            Assert.Contains("复制", menuHeaders);
                            Assert.Contains("粘贴", menuHeaders);
                            Assert.Contains("删除", menuHeaders);
                        }

                        if (box.Type == BoxType.Project)
                        {
                            AssertNoProjectProgressIndicators(window);
                            AssertProjectWindowAddsModule(window, viewModel);
                        }
                    }
                    finally
                    {
                        window.ForceClose();
                    }
                }

                var projectBox = boxes.Single(box => box.Type == BoxType.Project);
                AssertProjectManagementViewHasNoProgress(
                    new ProjectService(repository),
                    projectBox);
                AssertFileBoxResizeReflows(drawerService, repository, root);
                AssertLinkedProjectBoxResizeHitTests(drawerService, repository, root);
                AssertLinkingManualSizedBoundBoxPreservesVisibleSize(
                    drawerService,
                    repository,
                    root);
                AssertProjectAttachmentButtonsHaveUsableHitTargets(drawerService, repository);
                AssertDesktopPaperManagerWindowArchivesPaper();
                AssertMainArchivePageShowsDesktopPapers(drawerService, repository, root);
                AssertManagedDesktopActionsMenuExecutes(
                    drawerService,
                    repository,
                    boxes.Single(box => box.Type == BoxType.Normal));
                AssertManagedFileContextMenuRename(
                    drawerService,
                    repository,
                    boxes.Single(box => box.Type == BoxType.Normal),
                    root);
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
            finally
            {
                application?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            Assert.Null(threadException);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    private static void AssertProjectAttachmentButtonsHaveUsableHitTargets(
        DrawerService drawerService,
        DrawerRepository repository)
    {
        var projectService = new ProjectService(repository);
        var projectBox = drawerService.CreateBoxAsync("外置关联按钮巡检", BoxType.Project)
            .GetAwaiter().GetResult();
        var viewModel = new DesktopBoxViewModel(
            projectBox,
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new NoOpLogger(),
            BoxVisualStyle.Modern,
            projectService: projectService);
        viewModel.LoadAsync().GetAwaiter().GetResult();

        var buttonNames = new[]
        {
            ("ProjectLeftAttachmentToggleButton", ProjectAttachmentSide.Left),
            ("ProjectRightAttachmentToggleButton", ProjectAttachmentSide.Right),
            ("ProjectTopAttachmentToggleButton", ProjectAttachmentSide.Top),
            ("ProjectBottomAttachmentToggleButton", ProjectAttachmentSide.Bottom)
        };
        foreach (var (_, side) in buttonNames)
        {
            var linkedBox = drawerService.CreateBoxAsync(
                $"{ProjectAttachmentSideCatalog.GetLabel(side)}关联资料",
                BoxType.Normal).GetAwaiter().GetResult();
            Assert.True(viewModel.LinkProjectBoxAtSideAsync(linkedBox.Id, side)
                .GetAwaiter().GetResult());
        }

        var window = new DesktopBoxWindow(viewModel)
        {
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            PumpDispatcher();

            var clientArea = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
            var targetBounds = buttonNames.Select(item =>
            {
                var button = Assert.IsType<Button>(window.FindName(item.Item1));
                Assert.True(button.IsVisible, $"{item.Item1} should be visible when its side has a link.");
                Assert.True(button.IsHitTestVisible, $"{item.Item1} should remain hit-testable while transparent.");
                var bounds = button.TransformToAncestor(window).TransformBounds(
                    new Rect(new Point(), button.RenderSize));
                var interactiveBounds = Rect.Intersect(bounds, clientArea);
                return (Name: item.Item1, Declared: button.RenderSize, Interactive: interactiveBounds);
            }).ToArray();

            var clippedTargets = targetBounds
                .Where(target => target.Interactive.Width < 24 || target.Interactive.Height < 24)
                .Select(target =>
                    $"{target.Name}: declared={target.Declared.Width:0.#}×{target.Declared.Height:0.#}, "
                    + $"inside-client={target.Interactive.Width:0.#}×{target.Interactive.Height:0.#}")
                .ToArray();
            Assert.True(
                clippedTargets.Length == 0,
                "Every external association button needs a usable 24×24 DIP hit target within the "
                + "desktop window. Measured: " + string.Join("; ", clippedTargets));

            var leftButton = Assert.IsType<Button>(
                window.FindName("ProjectLeftAttachmentToggleButton"));
            var leftInvoke = Assert.IsAssignableFrom<IInvokeProvider>(
                new ButtonAutomationPeer(leftButton).GetPattern(PatternInterface.Invoke));
            leftInvoke.Invoke();
            WaitFor(
                () => !viewModel.IsProjectLeftAttachmentsVisible,
                "Invoking the left external hit target did not toggle the left association side.");
            Assert.True(viewModel.IsProjectRightAttachmentsVisible);
        }
        finally
        {
            window.ForceClose();
        }
    }

    private static void AssertLinkedProjectBoxResizeHitTests(
        DrawerService drawerService,
        DrawerRepository repository,
        string root)
    {
        const uint wmNcHitTest = 0x0084;
        const uint wmSetCursor = 0x0020;
        const uint wmMouseMove = 0x0200;
        const int htRight = 11;
        const int htBottom = 15;
        var projectService = new ProjectService(repository);
        var projectFolderService = new ProjectFolderService(repository);
        var memberProject = drawerService.CreateBoxAsync("小卫", BoxType.Project)
            .GetAwaiter()
            .GetResult();
        var companionProject = drawerService.CreateBoxAsync("小卫同组项目", BoxType.Project)
            .GetAwaiter()
            .GetResult();
        var linkedFolder = Path.Combine(root, "小卫电器");
        Directory.CreateDirectory(linkedFolder);
        var linkedBox = drawerService.CreateBoundBoxAsync("小卫电器", linkedFolder)
            .GetAwaiter()
            .GetResult();
        projectService.GetOrCreateProjectAsync(memberProject.Id)
            .GetAwaiter()
            .GetResult();
        projectService.GetOrCreateProjectAsync(companionProject.Id)
            .GetAwaiter()
            .GetResult();
        var projectFolder = projectFolderService.CreateAsync(
                "小卫项目文件夹",
                [memberProject.Id, companionProject.Id])
            .GetAwaiter()
            .GetResult();
        var projectViewModel = new DesktopBoxViewModel(
            memberProject,
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new NoOpLogger(),
            BoxVisualStyle.Modern,
            projectService: projectService,
            projectFolderService: projectFolderService);
        projectViewModel.LoadAsync().GetAwaiter().GetResult();
        Assert.True(
            projectViewModel.LinkProjectBoxAtSideAsync(
                    linkedBox.Id,
                    ProjectAttachmentSide.Left)
                .GetAwaiter()
                .GetResult());
        drawerService.SetSettingAsync(
                BoxViewModel.GetLayoutPresetSettingKey(linkedBox.Id),
                "3x3")
            .GetAwaiter()
            .GetResult();
        drawerService.SetSettingAsync(
                $"BoxSizeMode:{linkedBox.Id:N}",
                "Fixed:5:4")
            .GetAwaiter()
            .GetResult();
        drawerService.SetSettingAsync(
                BoxViewModel.GetWindowSizeSettingKey(linkedBox.Id),
                "850.6666666666666,597.3333333333334")
            .GetAwaiter()
            .GetResult();
        drawerService.SetSettingAsync(
                $"ProjectLinkedBoxManualPosition:{linkedBox.Id:N}",
                bool.FalseString)
            .GetAwaiter()
            .GetResult();

        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(
                System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var manager = new DesktopBoxManager(
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new NoOpLogger(),
            new BoxVisualStyleStore(drawerService, new NoOpLogger()),
            new BoxPositionLockStateStore(drawerService, new NoOpLogger()));
        try
        {
            AwaitWithDispatcher(manager.RefreshAsync());
            var folderWindow = Application.Current.Windows
                .OfType<DesktopBoxWindow>()
                .Single(window => window.ViewModel.BoxId == projectFolder.Id);
            var member = folderWindow.ViewModel.ProjectFolderMembers
                .Single(item => item.ProjectBoxId == memberProject.Id);

            folderWindow.ViewModel.OpenProjectFolderMemberCommand
                .ExecuteAsync(member)
                .GetAwaiter()
                .GetResult();
            WaitFor(
                () => Application.Current.Windows
                    .OfType<DesktopBoxWindow>()
                    .Any(window =>
                        window.ViewModel.BoxId == linkedBox.Id
                        && window.IsVisible),
                "Opening the project-folder member did not show its linked box.");

            var linkedWindow = Application.Current.Windows
                .OfType<DesktopBoxWindow>()
                .Single(window => window.ViewModel.BoxId == linkedBox.Id);
            Assert.True(linkedWindow.IsVisible);
            linkedWindow.UpdateLayout();
            PumpDispatcher();
            var handle = new WindowInteropHelper(linkedWindow).Handle;
            Assert.NotEqual(nint.Zero, handle);
            Assert.True(
                GetWindowRect(handle, out var rectangle),
                $"GetWindowRect failed with Win32 error {Marshal.GetLastWin32Error()}.");
            var centerX = rectangle.Left + ((rectangle.Right - rectangle.Left) / 2);
            var centerY = rectangle.Top + ((rectangle.Bottom - rectangle.Top) / 2);
            var rightResult = SendMessage(
                handle,
                wmNcHitTest,
                nint.Zero,
                MakeScreenPointLParam(rectangle.Right - 2, centerY)).ToInt32();
            Assert.True(
                rightResult == htRight,
                $"The linked box right edge returned {rightResult}, expected {htRight}.");
            var bottomResult = SendMessage(
                handle,
                wmNcHitTest,
                nint.Zero,
                MakeScreenPointLParam(centerX, rectangle.Bottom - 2)).ToInt32();
            Assert.True(
                bottomResult == htBottom,
                $"The linked box bottom edge returned {bottomResult}, expected {htBottom}.");
            var rightCursorResult = SendMessage(
                handle,
                wmSetCursor,
                handle,
                MakeMessageLParam(htRight, (int)wmMouseMove));
            Assert.True(
                rightCursorResult != nint.Zero,
                "The linked box did not handle WM_SETCURSOR for its right resize edge.");
            var bottomCursorResult = SendMessage(
                handle,
                wmSetCursor,
                handle,
                MakeMessageLParam(htBottom, (int)wmMouseMove));
            Assert.True(
                bottomCursorResult != nint.Zero,
                "The linked box did not handle WM_SETCURSOR for its bottom resize edge.");

            PerformNativeLeftResize(handle, rectangle);
            linkedWindow.UpdateLayout();
            PumpDispatcher();
            var resizedWidth = linkedWindow.ActualWidth;
            var resizedHeight = linkedWindow.ActualHeight;
            Assert.True(
                resizedWidth > 0 && resizedHeight > 0,
                $"The linked box native left resize produced {resizedWidth}x{resizedHeight}.");

            var memberWindow = Application.Current.Windows
                .OfType<DesktopBoxWindow>()
                .Single(window => window.ViewModel.BoxId == memberProject.Id);
            var returnButton = FindVisualDescendant<Button>(
                memberWindow,
                button => string.Equals(
                    button.ToolTip as string,
                    "收回文件夹",
                    StringComparison.Ordinal));
            Assert.NotNull(returnButton);
            InvokeButton(returnButton);
            WaitFor(
                () => !memberWindow.IsVisible && !linkedWindow.IsVisible,
                "Returning the member project to its folder did not hide the project and linked box.");

            folderWindow.ViewModel.OpenProjectFolderMemberCommand
                .ExecuteAsync(member)
                .GetAwaiter()
                .GetResult();
            WaitFor(
                () => memberWindow.IsVisible && linkedWindow.IsVisible,
                "Reopening the project-folder member did not show the project and linked box.");
            linkedWindow.UpdateLayout();
            PumpDispatcher();
            Assert.InRange(linkedWindow.ActualWidth, resizedWidth - 1, resizedWidth + 1);
            Assert.InRange(linkedWindow.ActualHeight, resizedHeight - 1, resizedHeight + 1);

            Assert.True(
                GetWindowRect(handle, out rectangle),
                $"GetWindowRect after reopening failed with Win32 error {Marshal.GetLastWin32Error()}.");
            centerX = rectangle.Left + ((rectangle.Right - rectangle.Left) / 2);
            centerY = rectangle.Top + ((rectangle.Bottom - rectangle.Top) / 2);
            rightResult = SendMessage(
                handle,
                wmNcHitTest,
                nint.Zero,
                MakeScreenPointLParam(rectangle.Right - 2, centerY)).ToInt32();
            Assert.True(
                rightResult == htRight,
                $"The reopened linked box right edge returned {rightResult}, expected {htRight}.");
            bottomResult = SendMessage(
                handle,
                wmNcHitTest,
                nint.Zero,
                MakeScreenPointLParam(centerX, rectangle.Bottom - 2)).ToInt32();
            Assert.True(
                bottomResult == htBottom,
                $"The reopened linked box bottom edge returned {bottomResult}, expected {htBottom}.");
            rightCursorResult = SendMessage(
                handle,
                wmSetCursor,
                handle,
                MakeMessageLParam(htRight, (int)wmMouseMove));
            Assert.True(
                rightCursorResult != nint.Zero,
                "The reopened linked box did not handle WM_SETCURSOR for its right resize edge.");
            bottomCursorResult = SendMessage(
                handle,
                wmSetCursor,
                handle,
                MakeMessageLParam(htBottom, (int)wmMouseMove));
            Assert.True(
                bottomCursorResult != nint.Zero,
                "The reopened linked box did not handle WM_SETCURSOR for its bottom resize edge.");
        }
        finally
        {
            AwaitWithDispatcher(manager.CloseAllAsync());
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    private static void AssertLinkingManualSizedBoundBoxPreservesVisibleSize(
        DrawerService drawerService,
        DrawerRepository repository,
        string root)
    {
        var projectService = new ProjectService(repository);
        var projectFolderService = new ProjectFolderService(repository);
        var memberProject = drawerService.CreateBoxAsync("小卫", BoxType.Project)
            .GetAwaiter()
            .GetResult();
        var companionProject = drawerService.CreateBoxAsync("小卫同组项目", BoxType.Project)
            .GetAwaiter()
            .GetResult();
        var linkedFolder = Path.Combine(root, "小卫电器-关联瞬间");
        Directory.CreateDirectory(linkedFolder);
        var linkedBox = drawerService.CreateBoundBoxAsync("小卫电器", linkedFolder)
            .GetAwaiter()
            .GetResult();
        projectService.GetOrCreateProjectAsync(memberProject.Id)
            .GetAwaiter()
            .GetResult();
        projectService.GetOrCreateProjectAsync(companionProject.Id)
            .GetAwaiter()
            .GetResult();
        var projectFolder = projectFolderService.CreateAsync(
                "小卫项目文件夹-关联瞬间",
                [memberProject.Id, companionProject.Id])
            .GetAwaiter()
            .GetResult();

        // This is the same persisted state reported for the user's historical box:
        // the association layout may move its Left/Top, but it must not replace the
        // manually resized Width/Height when the drop callback completes.
        drawerService.SetSettingAsync(
                BoxViewModel.GetLayoutPresetSettingKey(linkedBox.Id),
                "3x3")
            .GetAwaiter()
            .GetResult();
        drawerService.SetSettingAsync(
                $"BoxSizeMode:{linkedBox.Id:N}",
                "Fixed:5:4")
            .GetAwaiter()
            .GetResult();
        drawerService.SetSettingAsync(
                BoxViewModel.GetWindowSizeSettingKey(linkedBox.Id),
                "850.6666666666666,597.3333333333334")
            .GetAwaiter()
            .GetResult();
        drawerService.SetSettingAsync(
                $"ProjectLinkedBoxManualPosition:{linkedBox.Id:N}",
                bool.FalseString)
            .GetAwaiter()
            .GetResult();

        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(
                System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var manager = new DesktopBoxManager(
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new NoOpLogger(),
            new BoxVisualStyleStore(drawerService, new NoOpLogger()),
            new BoxPositionLockStateStore(drawerService, new NoOpLogger()));
        try
        {
            AwaitWithDispatcher(manager.RefreshAsync());
            var folderWindow = Application.Current.Windows
                .OfType<DesktopBoxWindow>()
                .Single(window => window.ViewModel.BoxId == projectFolder.Id);
            var member = folderWindow.ViewModel.ProjectFolderMembers
                .Single(item => item.ProjectBoxId == memberProject.Id);

            // Open the member through the same public folder command used by the UI.
            folderWindow.ViewModel.OpenProjectFolderMemberCommand
                .ExecuteAsync(member)
                .GetAwaiter()
                .GetResult();
            WaitFor(
                () => Application.Current.Windows
                    .OfType<DesktopBoxWindow>()
                    .Any(window =>
                        window.ViewModel.BoxId == memberProject.Id
                        && window.IsVisible),
                "Opening the project-folder member did not show the member project.");

            var memberWindow = Application.Current.Windows
                .OfType<DesktopBoxWindow>()
                .Single(window => window.ViewModel.BoxId == memberProject.Id);
            var linkedWindow = Application.Current.Windows
                .OfType<DesktopBoxWindow>()
                .Single(window => window.ViewModel.BoxId == linkedBox.Id);
            WaitFor(
                () => linkedWindow.IsVisible,
                "The unassociated Bound box did not become visible before the public drop.");

            PumpRenderFrame();
            var before = CaptureWindowGeometry(linkedWindow);
            Assert.True(
                before.Width > 0
                && before.Height > 0
                && before.ActualWidth > 0
                && before.ActualHeight > 0
                && before.NativeOuterWidth > 0
                && before.NativeOuterHeight > 0
                && before.NativeClientWidth > 0
                && before.NativeClientHeight > 0,
                $"The manual linked-box fixture was not measurable before linking: {before}.");

            Assert.Equal(0, memberWindow.ViewModel.ProjectAttachmentCount);
            Assert.True(
                memberWindow.ViewModel.LinkProjectBoxAtSideAsync(
                        linkedBox.Id,
                        ProjectAttachmentSide.Left)
                    .GetAwaiter()
                    .GetResult());

            WaitFor(
                () => memberWindow.ViewModel.ProjectAttachmentCount == 1
                    && memberWindow.ViewModel.ProjectLeftAttachmentCount == 1
                    && memberWindow.ViewModel.ProjectAssociationMessage.Contains(
                        "已关联文件收纳盒到左侧",
                        StringComparison.Ordinal),
                "The public Bound-box link did not complete the left project association.");
            PumpRenderFrame();
            var after = CaptureWindowGeometry(linkedWindow);
            AssertWindowGeometryUnchanged(before, after, "public association completion");
        }
        finally
        {
            AwaitWithDispatcher(manager.CloseAllAsync());
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    private static WindowGeometry CaptureWindowGeometry(DesktopBoxWindow window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Assert.NotEqual(nint.Zero, handle);
        Assert.True(
            GetWindowRect(handle, out var outer),
            $"GetWindowRect failed with Win32 error {Marshal.GetLastWin32Error()}.");
        Assert.True(
            GetClientRect(handle, out var client),
            $"GetClientRect failed with Win32 error {Marshal.GetLastWin32Error()}.");
        return new WindowGeometry(
            window.Width,
            window.Height,
            window.ActualWidth,
            window.ActualHeight,
            outer.Right - outer.Left,
            outer.Bottom - outer.Top,
            client.Right - client.Left,
            client.Bottom - client.Top);
    }

    private static void AssertWindowGeometryUnchanged(
        WindowGeometry before,
        WindowGeometry after,
        string phase)
    {
        AssertDimensionUnchanged("Width", before.Width, after.Width, before, after, phase);
        AssertDimensionUnchanged("Height", before.Height, after.Height, before, after, phase);
        AssertDimensionUnchanged(
            "ActualWidth",
            before.ActualWidth,
            after.ActualWidth,
            before,
            after,
            phase);
        AssertDimensionUnchanged(
            "ActualHeight",
            before.ActualHeight,
            after.ActualHeight,
            before,
            after,
            phase);
        AssertDimensionUnchanged(
            "native outer width",
            before.NativeOuterWidth,
            after.NativeOuterWidth,
            before,
            after,
            phase);
        AssertDimensionUnchanged(
            "native outer height",
            before.NativeOuterHeight,
            after.NativeOuterHeight,
            before,
            after,
            phase);
        AssertDimensionUnchanged(
            "native client width",
            before.NativeClientWidth,
            after.NativeClientWidth,
            before,
            after,
            phase);
        AssertDimensionUnchanged(
            "native client height",
            before.NativeClientHeight,
            after.NativeClientHeight,
            before,
            after,
            phase);
    }

    private static void AssertDimensionUnchanged(
        string name,
        double expected,
        double actual,
        WindowGeometry before,
        WindowGeometry after,
        string phase)
    {
        Assert.True(
            Math.Abs(expected - actual) <= 1,
            $"{name} changed during {phase}: expected {expected:0.###} ±1, actual {actual:0.###}. "
            + $"Before {before}; after {after}.");
    }

    private static void AssertDimensionUnchanged(
        string name,
        int expected,
        int actual,
        WindowGeometry before,
        WindowGeometry after,
        string phase)
    {
        Assert.True(
            Math.Abs(expected - actual) <= 1,
            $"{name} changed during {phase}: expected {expected} ±1, actual {actual}. "
            + $"Before {before}; after {after}.");
    }

    private readonly record struct WindowGeometry(
        double Width,
        double Height,
        double ActualWidth,
        double ActualHeight,
        int NativeOuterWidth,
        int NativeOuterHeight,
        int NativeClientWidth,
        int NativeClientHeight);

    private static void AssertProjectManagementViewHasNoProgress(
        ProjectService projectService,
        Box projectBox)
    {
        var viewModel = new ProjectManagementViewModel(projectService, new NoOpLogger());
        viewModel.LoadAsync(projectBox.Id, projectBox.Name).GetAwaiter().GetResult();
        var view = new ProjectManagementView { DataContext = viewModel };
        var host = new Window
        {
            Content = view,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            Width = 760,
            Height = 640
        };
        try
        {
            host.Show();
            host.UpdateLayout();
            PumpDispatcher();
            AssertNoProjectProgressIndicators(view);
            Assert.True(viewModel.AreModulesExpanded);

            var titleTextBox = Assert.IsType<TextBox>(
                view.FindName("NewProjectModuleTitleTextBox"));
            titleTextBox.Text = "从主窗口回车添加";
            titleTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            RaiseEnterKey(titleTextBox);

            WaitFor(
                () => viewModel.Modules.Any(module => module.Title == "从主窗口回车添加"),
                "The main project module was not added after pressing Enter.");
        }
        finally
        {
            host.Close();
        }
    }

    private static void AssertFileBoxResizeReflows(
        DrawerService drawerService,
        DrawerRepository repository,
        string root)
    {
        var source = Path.Combine(root, "resize-source");
        Directory.CreateDirectory(source);
        var sourcePaths = new List<string>();
        foreach (var index in Enumerable.Range(1, 10))
        {
            var itemPath = Path.Combine(source, $"item-{index:00}");
            Directory.CreateDirectory(itemPath);
            sourcePaths.Add(itemPath);
        }

        var box = drawerService.CreateBoxAsync("尺寸重排测试", BoxType.Normal)
            .GetAwaiter()
            .GetResult();
        foreach (var itemPath in sourcePaths)
        {
            drawerService.ImportPathAsync(box.Id, itemPath)
                .GetAwaiter()
                .GetResult();
        }
        var viewModel = new DesktopBoxViewModel(
            box,
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new NoOpLogger(),
            BoxVisualStyle.Modern);
        viewModel.LoadAsync().GetAwaiter().GetResult();

        var window = new DesktopBoxWindow(viewModel)
        {
            Left = 40,
            Top = 40,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Manual,
            Width = 640,
            Height = 1000
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            PumpDispatcher();

            var movedItem = viewModel.Items[0];
            Assert.True(
                viewModel.DropDrawerItemAsync(movedItem.Id, targetColumn: 0, targetRow: 2)
                    .GetAwaiter()
                    .GetResult());
            var persistedAfterDrag = drawerService
                .GetItemsAsync(box.Id)
                .GetAwaiter()
                .GetResult()
                .OrderBy(item => item.Id)
                .Select(item => (item.Id, item.GridColumn, item.GridRow))
                .ToArray();
            Assert.Equal(
                (0, 2),
                (movedItem.GridColumn, movedItem.GridRow));
            Assert.Contains(
                persistedAfterDrag,
                item => item.Id == movedItem.Id
                    && item.GridColumn == 0
                    && item.GridRow == 2);
            window.UpdateLayout();
            PumpDispatcher();

            var initial = ReadVisibleItemLayout(window, viewModel);
            Assert.Equal(10, initial.Count);
            var expectedOrder = GetVisualOrder(initial);
            Assert.Equal(
                Enumerable.Range(2, 9)
                    .Select(index => $"item-{index:00}")
                    .Append("item-01"),
                expectedOrder);

            // Simulate the user's native edge resize through the public WPF window
            // surface. The tall viewport keeps all ten real containers realized so
            // the assertions observe their actual positions, not VM coordinates.
            window.Width = 220;
            window.UpdateLayout();
            PumpDispatcher();
            var narrow = ReadVisibleItemLayout(window, viewModel);

            window.Width = 640;
            window.UpdateLayout();
            PumpDispatcher();
            var wide = ReadVisibleItemLayout(window, viewModel);
            var persistedAfterResize = drawerService
                .GetItemsAsync(box.Id)
                .GetAwaiter()
                .GetResult()
                .OrderBy(item => item.Id)
                .Select(item => (item.Id, item.GridColumn, item.GridRow))
                .ToArray();
            var narrowVisual = GetVisualLayout(narrow);
            var wideVisual = GetVisualLayout(wide);

            Assert.Equal(expectedOrder, GetVisualOrder(narrow));
            Assert.Equal(expectedOrder, GetVisualOrder(wide));
            Assert.Equal(persistedAfterDrag, persistedAfterResize);
            Assert.Equal(10, narrow.Count);
            Assert.Equal(10, wide.Count);

            var narrowRows = narrow.Select(item => item.Top).Distinct().Count();
            var wideRows = wide.Select(item => item.Top).Distinct().Count();
            Assert.True(
                narrowRows > wideRows,
                $"Expected a narrower window to use more rows, got {narrowRows} vs {wideRows}. " +
                $"Narrow: {FormatItemLayout(narrow)}; wide: {FormatItemLayout(wide)}");
            Assert.Equal(narrowVisual[0].Top, narrowVisual[1].Top, precision: 3);
            Assert.True(
                narrowVisual[^1].Top > narrowVisual[0].Top,
                $"Expected the last item to wrap in the narrow window: {FormatItemLayout(narrow)}");
            Assert.All(
                narrow.Zip(narrow.Skip(1)),
                pair => Assert.NotEqual(
                    (pair.First.Left, pair.First.Top),
                    (pair.Second.Left, pair.Second.Top)));
            var overlappingItems = narrow
                .SelectMany((item, index) => narrow
                    .Skip(index + 1)
                    .Where(other => Rect.Intersect(
                        new Rect(item.Left, item.Top, item.Width, item.Height),
                        new Rect(other.Left, other.Top, other.Width, other.Height))
                        is { Width: > 1, Height: > 1 })
                    .Select(other => (item.Name, other.Name)))
                .ToArray();
            Assert.Empty(overlappingItems);
            Assert.Equal(wideVisual[0].Top, wideVisual[^1].Top, precision: 3);
        }
        finally
        {
            window.ForceClose();
        }
    }

    private static IReadOnlyList<(string Name, double Left, double Top, double Width, double Height)> ReadVisibleItemLayout(
        DesktopBoxWindow window,
        DesktopBoxViewModel viewModel)
    {
        window.UpdateLayout();
        PumpDispatcher();
        var iconList = Assert.IsType<ListBox>(window.FindName("IconList"));
        Assert.Equal(10, iconList.Items.Count);
        iconList.Items.Refresh();
        iconList.ScrollIntoView(iconList.Items[0]);
        window.UpdateLayout();
        PumpDispatcher();
        var layout = new List<(string Name, double Left, double Top, double Width, double Height)>(
            iconList.Items.Count);
        for (var index = 0; index < iconList.Items.Count; index++)
        {
            var container = Assert.IsType<ListBoxItem>(
                iconList.ItemContainerGenerator.ContainerFromIndex(index));
            Assert.True(container.IsVisible);
            Assert.True(container.ActualWidth > 0);
            Assert.True(container.ActualHeight > 0);
            var item = Assert.IsType<DrawerItemViewModel>(container.DataContext);
            layout.Add((
                item.DisplayName,
                Canvas.GetLeft(container),
                Canvas.GetTop(container),
                container.ActualWidth,
                container.ActualHeight));
        }

        Assert.Equal(10, viewModel.Items.Count);
        return layout;
    }

    private static string FormatItemLayout(
        IReadOnlyList<(string Name, double Left, double Top, double Width, double Height)> layout) =>
        string.Join(
            ", ",
            layout.Select(item => $"{item.Name}@({item.Left:0.##},{item.Top:0.##})"));

    private static string[] GetVisualOrder(
        IReadOnlyList<(string Name, double Left, double Top, double Width, double Height)> layout) =>
        GetVisualLayout(layout)
            .Select(item => item.Name)
            .ToArray();

    private static IReadOnlyList<(string Name, double Left, double Top, double Width, double Height)> GetVisualLayout(
        IReadOnlyList<(string Name, double Left, double Top, double Width, double Height)> layout) =>
        layout
            .OrderBy(item => item.Top)
            .ThenBy(item => item.Left)
            .ToArray();

    private static void AssertNoProjectProgressIndicators(DependencyObject root)
    {
        Assert.Null(FindVisualDescendant<TextBlock>(root, textBlock =>
            textBlock.IsVisible
            && textBlock.Text.Contains('%', StringComparison.Ordinal)));
        Assert.Null(FindVisualDescendant<ProgressBar>(root, progressBar =>
            progressBar.IsVisible));
    }

    private static void AssertProjectWindowAddsModule(
        DesktopBoxWindow window,
        DesktopBoxViewModel viewModel)
    {
        var stageComboBox = Assert.IsType<ComboBox>(window.FindName("ProjectStageComboBox"));
        Assert.Equal(32, stageComboBox.Height);
        Assert.Equal("点击切换项目阶段", stageComboBox.ToolTip);

        viewModel.AreProjectModulesExpanded = true;
        window.UpdateLayout();
        var titleTextBox = Assert.IsType<TextBox>(
            window.FindName("ProjectIssueTitleTextBox"));
        titleTextBox.Text = "从桌面收纳盒回车添加";
        titleTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        RaiseEnterKey(titleTextBox);

        WaitFor(
            () => viewModel.ProjectIssues.Count == 1,
            "The desktop project module was not added after pressing Enter.");
        Assert.Equal("从桌面收纳盒回车添加", viewModel.ProjectIssues.Single().Title);
        PumpDispatcher();
        window.UpdateLayout();

        var deleteButton = FindVisualDescendant<Button>(window, button =>
            string.Equals(button.ToolTip as string, "删除模块", StringComparison.Ordinal));
        Assert.NotNull(deleteButton);
        var moduleStateComboBox = FindVisualDescendant<ComboBox>(window, comboBox =>
            string.Equals(
                comboBox.ToolTip as string,
                "模块状态：未开发 / 开发完成 / 上线完成",
                StringComparison.Ordinal));
        Assert.NotNull(moduleStateComboBox);
        var moduleRow = Assert.IsType<Grid>(VisualTreeHelper.GetParent(deleteButton));
        Assert.Same(moduleRow, VisualTreeHelper.GetParent(moduleStateComboBox));
        Assert.Equal(3, moduleRow.ColumnDefinitions.Count);
        Assert.Equal(1, Grid.GetColumn(moduleStateComboBox));
        Assert.Equal(2, Grid.GetColumn(deleteButton));
        var deleteInvokeProvider = Assert.IsAssignableFrom<IInvokeProvider>(
            new ButtonAutomationPeer(deleteButton).GetPattern(PatternInterface.Invoke));
        deleteInvokeProvider.Invoke();

        WaitFor(
            () => viewModel.ProjectIssues.Count == 0,
            "The desktop project module was not deleted after clicking the delete button.");

        viewModel.ApplyTitleVisibility(false);
        viewModel.ApplyPositionLockState(true);
        window.UpdateLayout();
        var actionsButton = Assert.IsType<Button>(window.FindName("ProjectDesktopActionsButton"));
        Assert.True(actionsButton.IsVisible);
        Assert.Equal("解锁桌面位置", viewModel.PositionLockActionLabel);

        actionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var actionsPopup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(
            window.FindName("ProjectDesktopActionsPopup"));
        Assert.True(actionsPopup.IsOpen);
        actionsPopup.IsOpen = false;
    }

    private static void RaiseEnterKey(UIElement target)
    {
        var source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException("The keyboard target is not connected to a presentation source.");
        target.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });
    }

    private static void AssertDesktopActionsMenuPhysicalClick(
        DesktopBoxWindow window,
        Button actionsButton,
        System.Windows.Controls.Primitives.Popup popup)
    {
        GetCursorPos(out var originalCursor);
        using var mouseMonitor = new GlobalMouseButtonMonitor();
        Assert.True(mouseMonitor.IsActive);
        var observedClickHandles = new List<nint>();
        void CloseMenuIfOutside(int screenX, int screenY)
        {
            var clickedHandle = GlobalMouseButtonMonitor.HitTestWindowHandle(screenX, screenY);
            observedClickHandles.Add(clickedHandle);
            window.CloseDesktopActionsMenuIfOutside(clickedHandle);
        }
        mouseMonitor.MouseButtonDown += CloseMenuIfOutside;
        var positionLockInvocations = 0;
        window.SetProjectBoxActionsCallbacks(
            (_, _) => Task.CompletedTask,
            _ =>
            {
                positionLockInvocations++;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask);
        try
        {
            window.Left = 100;
            window.Top = 100;
            window.UpdateLayout();
            PumpDispatcher();
            SetWindowPos(
                new WindowInteropHelper(window).Handle,
                new nint(-1),
                0,
                0,
                0,
                0,
                0x0001 | 0x0002 | 0x0010);

            var center = actionsButton.PointToScreen(
                new Point(actionsButton.ActualWidth / 2, actionsButton.ActualHeight / 2));
            var actionsButtonClicks = 0;
            actionsButton.Click += (_, _) => actionsButtonClicks++;
            Assert.True(SetCursorPos((int)Math.Round(center.X), (int)Math.Round(center.Y)));
            PumpDispatcher();
            Thread.Sleep(10);
            SetWindowPos(
                new WindowInteropHelper(window).Handle,
                new nint(-1),
                0,
                0,
                0,
                0,
                0x0001 | 0x0002 | 0x0010);
            Assert.Equal(
                window.NativeHandle,
                GlobalMouseButtonMonitor.HitTestWindowHandle(
                    (int)Math.Round(center.X),
                    (int)Math.Round(center.Y)));
            MouseEvent(0x0002, 0, 0, 0, 0);
            PumpDispatcher();
            Thread.Sleep(20);
            MouseEvent(0x0004, 0, 0, 0, 0);
            PumpDispatcher();
            Thread.Sleep(120);
            PumpDispatcher();

            Assert.True(
                popup.IsOpen,
                $"The no-activate desktop menu closed during the opening click. " +
                $"Button clicks: {actionsButtonClicks}; owner: {window.NativeHandle}; " +
                $"hit handles: {string.Join(",", observedClickHandles)}.");

            var popupVisual = Assert.IsAssignableFrom<Visual>(popup.Child);
            var popupHandle = Assert.IsType<HwndSource>(
                PresentationSource.FromVisual(popupVisual)).Handle;
            var lockButton = FindVisualDescendant<Button>(popupVisual, button =>
                FindVisualDescendant<TextBlock>(button, textBlock =>
                    string.Equals(textBlock.Text, "锁定桌面位置", StringComparison.Ordinal)) is not null);
            Assert.NotNull(lockButton);
            var lockButtonClicks = 0;
            lockButton.Click += (_, _) => lockButtonClicks++;
            var lockCenter = lockButton.PointToScreen(
                new Point(lockButton.ActualWidth / 2, lockButton.ActualHeight / 2));
            Assert.Equal(
                popupHandle,
                GlobalMouseButtonMonitor.HitTestWindowHandle(
                    (int)Math.Round(lockCenter.X),
                    (int)Math.Round(lockCenter.Y)));
            Assert.True(SetCursorPos((int)Math.Round(lockCenter.X), (int)Math.Round(lockCenter.Y)));
            PumpDispatcher();
            Thread.Sleep(10);
            MouseEvent(0x0002, 0, 0, 0, 0);
            PumpDispatcher();
            Thread.Sleep(20);
            var closeStopwatch = Stopwatch.StartNew();
            MouseEvent(0x0004, 0, 0, 0, 0);
            while ((positionLockInvocations == 0 || IsWindowVisible(popupHandle))
                   && closeStopwatch.ElapsedMilliseconds < 1000)
            {
                PumpDispatcher();
                Thread.Sleep(2);
            }

            Assert.True(
                positionLockInvocations == 1,
                $"The lock action was not invoked. Button clicks: {lockButtonClicks}; " +
                $"popup: {popupHandle}; hit handles: {string.Join(",", observedClickHandles)}.");
            Assert.False(popup.IsOpen, "The menu action did not close the desktop actions menu.");
            Assert.True(
                closeStopwatch.ElapsedMilliseconds < 120,
                $"The desktop actions menu took {closeStopwatch.ElapsedMilliseconds} ms to disappear.");

            actionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            Assert.True(popup.IsOpen);
            var outsidePoint = window.PointToScreen(
                new Point(window.ActualWidth + 80, window.ActualHeight + 80));
            window.CloseDesktopActionsMenuIfOutside(
                GlobalMouseButtonMonitor.HitTestWindowHandle(
                    (int)Math.Round(outsidePoint.X),
                    (int)Math.Round(outsidePoint.Y)));
            Assert.False(popup.IsOpen, "A global click outside the box did not close the menu.");
        }
        finally
        {
            mouseMonitor.MouseButtonDown -= CloseMenuIfOutside;
            SetCursorPos(originalCursor.X, originalCursor.Y);
        }
    }

    private static void AssertManagedDesktopActionsMenuExecutes(
        DrawerService drawerService,
        DrawerRepository repository,
        Box normalBox)
    {
        GetCursorPos(out var originalCursor);
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(
                System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var logger = new NoOpLogger();
        var manager = new DesktopBoxManager(
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            logger,
            new BoxVisualStyleStore(drawerService, logger),
            new BoxPositionLockStateStore(drawerService, logger));
        try
        {
            AwaitWithDispatcher(manager.RefreshAsync());
            var window = Application.Current.Windows
                .OfType<DesktopBoxWindow>()
                .Single(candidate => candidate.ViewModel.BoxId == normalBox.Id);
            window.Left = 100;
            window.Top = 100;
            window.UpdateLayout();
            PumpDispatcher();
            SetWindowPos(
                new WindowInteropHelper(window).Handle,
                new nint(-1),
                0,
                0,
                0,
                0,
                0x0001 | 0x0002 | 0x0010);

            var actionsButton = Assert.IsType<Button>(window.FindName("ProjectDesktopActionsButton"));
            InvokeButton(actionsButton);
            var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(
                window.FindName("ProjectDesktopActionsPopup"));
            WaitFor(() => popup.IsOpen, "The managed desktop actions menu did not open.");
            var lockButton = FindVisualDescendant<Button>(
                Assert.IsAssignableFrom<Visual>(popup.Child),
                button => FindVisualDescendant<TextBlock>(button, textBlock =>
                    string.Equals(textBlock.Text, "锁定桌面位置", StringComparison.Ordinal)) is not null);
            Assert.NotNull(lockButton);

            InvokeButton(lockButton);
            WaitFor(
                () => window.ViewModel.IsPositionLocked,
                "The managed desktop actions menu did not execute the position-lock action.");
            Assert.False(popup.IsOpen);

            InvokeButton(actionsButton);
            WaitFor(() => popup.IsOpen, "The managed desktop actions menu did not reopen.");
            var titleVisibilityButton = FindVisualDescendant<Button>(
                Assert.IsAssignableFrom<Visual>(popup.Child),
                button => FindVisualDescendant<TextBlock>(button, textBlock =>
                    string.Equals(textBlock.Text, "隐藏名称", StringComparison.Ordinal)) is not null);
            Assert.NotNull(titleVisibilityButton);
            InvokeButton(titleVisibilityButton);
            WaitFor(
                () => !window.ViewModel.IsTitleVisible,
                "The managed desktop actions menu did not execute the hide-title action.");
            Assert.True(actionsButton.IsVisible, "Hiding the title also hid the actions button.");
            Assert.False(popup.IsOpen);

            InvokeButton(actionsButton);
            WaitFor(() => popup.IsOpen, "The menu did not open while the title was hidden.");
            var legacyFixedSizeButton = FindVisualDescendant<Button>(
                Assert.IsAssignableFrom<Visual>(popup.Child),
                button => string.Equals(button.Content as string, "固定", StringComparison.Ordinal));
            Assert.Null(legacyFixedSizeButton);
            Assert.True(popup.IsOpen);

            var renameButton = FindVisualDescendant<Button>(
                Assert.IsAssignableFrom<Visual>(popup.Child),
                button => FindVisualDescendant<TextBlock>(button, textBlock =>
                    string.Equals(textBlock.Text, "重命名", StringComparison.Ordinal)) is not null);
            Assert.NotNull(renameButton);
            InvokeButton(renameButton);
            var renamePopup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(
                window.FindName("ProjectDesktopRenamePopup"));
            WaitFor(() => renamePopup.IsOpen, "The rename action did not open its editor.");
            var renameTextBox = Assert.IsType<TextBox>(window.FindName("ProjectDesktopRenameTextBox"));
            renameTextBox.Text = "真实点击后的新名称";
            var confirmRenameButton = FindVisualDescendant<Button>(
                Assert.IsAssignableFrom<Visual>(renamePopup.Child),
                button => string.Equals(button.Content as string, "确认", StringComparison.Ordinal));
            Assert.NotNull(confirmRenameButton);
            InvokeButton(confirmRenameButton);
            WaitFor(
                () => string.Equals(window.ViewModel.Name, "真实点击后的新名称", StringComparison.Ordinal),
                "The managed desktop actions menu did not execute the rename action.");
            Assert.False(renamePopup.IsOpen);
        }
        finally
        {
            AwaitWithDispatcher(manager.CloseAllAsync());
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
            SetCursorPos(originalCursor.X, originalCursor.Y);
        }
    }

    private static void AssertManagedFileContextMenuRename(
        DrawerService drawerService,
        DrawerRepository repository,
        Box normalBox,
        string root)
    {
        var sourcePath = Path.Combine(root, "右键菜单原文件.txt");
        File.WriteAllText(sourcePath, "context-menu");
        var imported = drawerService.ImportPathAsync(normalBox.Id, sourcePath)
            .GetAwaiter()
            .GetResult();
        var dotFilePath = Path.Combine(root, ".gitignore");
        File.WriteAllText(dotFilePath, "bin/");
        var importedDotFile = drawerService.ImportPathAsync(normalBox.Id, dotFilePath)
            .GetAwaiter()
            .GetResult();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(
                System.Windows.Threading.Dispatcher.CurrentDispatcher));
        GetCursorPos(out var originalCursor);
        IDataObject? originalClipboard = null;
        try
        {
            originalClipboard = Clipboard.GetDataObject();
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
        }
        var logger = new NoOpLogger();
        var manager = new DesktopBoxManager(
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            logger,
            new BoxVisualStyleStore(drawerService, logger),
            new BoxPositionLockStateStore(drawerService, logger));
        try
        {
            AwaitWithDispatcher(manager.RefreshAsync());
            var window = Application.Current.Windows
                .OfType<DesktopBoxWindow>()
                .Single(candidate => candidate.ViewModel.BoxId == normalBox.Id);
            window.Left = 140;
            window.Top = 140;
            window.UpdateLayout();
            PumpDispatcher();

            var iconList = Assert.IsType<ListBox>(window.FindName("IconList"));
            var item = window.ViewModel.Items.Single(candidate => candidate.Id == imported.Id);
            iconList.ScrollIntoView(item);
            window.UpdateLayout();
            PumpDispatcher();
            var itemContainer = Assert.IsType<ListBoxItem>(
                iconList.ItemContainerGenerator.ContainerFromItem(item));
            var contextMenu = Assert.IsType<ContextMenu>(iconList.ContextMenu);
            iconList.SelectedItem = item;
            contextMenu.PlacementTarget = itemContainer;
            contextMenu.IsOpen = true;
            PumpDispatcher();
            WaitFor(() => contextMenu.IsOpen, "The file context menu did not open after right-clicking an item.");
            Assert.Same(item, iconList.SelectedItem);
            var renameMenuItem = contextMenu.Items
                .OfType<MenuItem>()
                .Single(menuItem => string.Equals(menuItem.Header as string, "重命名", StringComparison.Ordinal));
            InvokeMenuItem(renameMenuItem);

            var renamePopup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(
                window.FindName("FileItemRenamePopup"));
            WaitFor(
                () => renamePopup.IsOpen,
                "Clicking Rename from the file context menu did not open the rename editor.");
            var renameTextBox = Assert.IsType<TextBox>(window.FindName("FileItemRenameTextBox"));
            var renameExtension = Assert.IsType<TextBlock>(window.FindName("FileItemRenameExtensionText"));
            Assert.Equal("右键菜单原文件", renameTextBox.Text);
            Assert.Equal(".txt", renameExtension.Text);
            renameTextBox.Text = "右键菜单已改名";
            var confirmRenameButton = FindVisualDescendant<Button>(
                Assert.IsAssignableFrom<Visual>(renamePopup.Child),
                button => string.Equals(button.Content as string, "确认", StringComparison.Ordinal));
            Assert.NotNull(confirmRenameButton);
            InvokeButton(confirmRenameButton);
            WaitFor(
                () => window.ViewModel.Items.Any(candidate =>
                    candidate.Id == imported.Id
                    && string.Equals(candidate.DisplayName, "右键菜单已改名.txt", StringComparison.Ordinal)),
                "Confirming the file context-menu rename did not rename the item.");
            var renamedItem = window.ViewModel.Items.Single(candidate => candidate.Id == imported.Id);
            Assert.True(File.Exists(renamedItem.Model.EffectivePath));
            var expectedRestorePath = Assert.IsType<string>(renamedItem.Model.SourcePath);

            var dotFileItem = window.ViewModel.Items.Single(candidate => candidate.Id == importedDotFile.Id);
            iconList.SelectedItem = dotFileItem;
            contextMenu.PlacementTarget = iconList;
            contextMenu.IsOpen = true;
            PumpDispatcher();
            InvokeMenuItem(renameMenuItem);
            WaitFor(() => renamePopup.IsOpen, "The rename editor did not open for a dot file.");
            var renameExtensionContainer = Assert.IsType<Border>(
                window.FindName("FileItemRenameExtensionContainer"));
            Assert.Equal(".gitignore", renameTextBox.Text);
            Assert.Equal(Visibility.Collapsed, renameExtensionContainer.Visibility);
            renameTextBox.Text = ".editorconfig";
            InvokeButton(confirmRenameButton);
            WaitFor(
                () => window.ViewModel.Items.Any(candidate =>
                    candidate.Id == importedDotFile.Id
                    && string.Equals(candidate.DisplayName, ".editorconfig", StringComparison.Ordinal)),
                "Confirming the rename did not rename an extensionless dot file.");

            iconList.ScrollIntoView(renamedItem);
            window.UpdateLayout();
            PumpDispatcher();
            var renamedContainer = Assert.IsType<ListBoxItem>(
                iconList.ItemContainerGenerator.ContainerFromItem(renamedItem));
            RightClickPhysical(window, renamedContainer);
            WaitFor(() => contextMenu.IsOpen, "The file context menu did not reopen for Copy.");
            Assert.Same(renamedItem, iconList.SelectedItem);
            var copyMenuItem = contextMenu.Items
                .OfType<MenuItem>()
                .Single(menuItem => string.Equals(menuItem.Header as string, "复制", StringComparison.Ordinal));
            Assert.True(copyMenuItem.IsEnabled);
            DrawerItemViewModel? selectedWhenCopyClicked = null;
            copyMenuItem.Click += (_, _) =>
                selectedWhenCopyClicked = iconList.SelectedItem as DrawerItemViewModel;
            InvokeMenuItem(copyMenuItem);
            contextMenu.IsOpen = false;
            Assert.Same(renamedItem, selectedWhenCopyClicked);
            WaitFor(
                () => Clipboard.ContainsFileDropList()
                    && Clipboard.GetFileDropList().Cast<string>().Any(path =>
                        string.Equals(
                            path,
                            renamedItem.Model.EffectivePath,
                            StringComparison.OrdinalIgnoreCase)),
                "Clicking Copy from the file context menu did not copy the selected path.");
            WaitFor(() => !contextMenu.IsOpen, "The file context menu stayed open after Copy.");

            var itemCountBeforePaste = window.ViewModel.Items.Count;
            iconList.SelectedItem = null;
            contextMenu.PlacementTarget = iconList;
            contextMenu.IsOpen = true;
            PumpDispatcher();
            WaitFor(() => contextMenu.IsOpen, "The file context menu did not open for Paste.");
            var pasteMenuItem = contextMenu.Items
                .OfType<MenuItem>()
                .Single(menuItem => string.Equals(menuItem.Header as string, "粘贴", StringComparison.Ordinal));
            Assert.True(pasteMenuItem.IsEnabled);
            var pasteClicks = 0;
            pasteMenuItem.Click += (_, _) => pasteClicks++;
            var statusBeforePaste = window.ViewModel.StatusText;
            InvokeMenuItem(pasteMenuItem);
            contextMenu.IsOpen = false;
            WaitFor(
                () => !window.ViewModel.IsBusy
                    && !string.Equals(window.ViewModel.StatusText, statusBeforePaste, StringComparison.Ordinal),
                "Clicking Paste from the file context menu did not start a paste operation.");
            Assert.True(
                window.ViewModel.Items.Count == itemCountBeforePaste + 1,
                $"Paste clicks: {pasteClicks}; status: {window.ViewModel.StatusText}; " +
                $"items before/after: {itemCountBeforePaste}/{window.ViewModel.Items.Count}.");

            var deleteTarget = window.ViewModel.Items.Single(candidate => candidate.Id == imported.Id);
            iconList.ScrollIntoView(deleteTarget);
            window.UpdateLayout();
            PumpDispatcher();
            var deleteTargetContainer = Assert.IsType<ListBoxItem>(
                iconList.ItemContainerGenerator.ContainerFromItem(deleteTarget));
            RightClickPhysical(window, deleteTargetContainer);
            WaitFor(() => contextMenu.IsOpen, "The file context menu did not reopen for Delete.");
            Assert.Same(deleteTarget, iconList.SelectedItem);
            var deleteMenuItem = contextMenu.Items
                .OfType<MenuItem>()
                .Single(menuItem => string.Equals(menuItem.Header as string, "删除", StringComparison.Ordinal));
            Assert.True(deleteMenuItem.IsEnabled);
            InvokeMenuItem(deleteMenuItem);
            contextMenu.IsOpen = false;
            WaitFor(
                () => window.ViewModel.Items.All(candidate => candidate.Id != imported.Id),
                "Clicking Delete from the file context menu did not remove the item from the box.");
            Assert.True(File.Exists(expectedRestorePath));
            Assert.Equal("context-menu", File.ReadAllText(expectedRestorePath));

            var itemCountBeforeCreate = window.ViewModel.Items.Count;
            contextMenu.PlacementTarget = iconList;
            contextMenu.IsOpen = true;
            PumpDispatcher();
            var createFolderMenuItem = contextMenu.Items
                .OfType<MenuItem>()
                .Single(menuItem => string.Equals(menuItem.Header as string, "新建文件夹", StringComparison.Ordinal));
            InvokeMenuItem(createFolderMenuItem);
            contextMenu.IsOpen = false;
            WaitFor(
                () => window.ViewModel.Items.Count == itemCountBeforeCreate + 1,
                "Clicking New Folder from the file context menu did not create a folder.");
            var createdFolder = window.ViewModel.Items.Single(candidate =>
                candidate.Model.ItemKind == ItemKind.Directory
                && string.Equals(candidate.DisplayName, "新建文件夹", StringComparison.Ordinal));
            Assert.True(Directory.Exists(createdFolder.Model.EffectivePath));

            contextMenu.IsOpen = true;
            PumpDispatcher();
            var createTextFileMenuItem = contextMenu.Items
                .OfType<MenuItem>()
                .Single(menuItem => string.Equals(menuItem.Header as string, "新建文本文档", StringComparison.Ordinal));
            InvokeMenuItem(createTextFileMenuItem);
            contextMenu.IsOpen = false;
            WaitFor(
                () => window.ViewModel.Items.Count == itemCountBeforeCreate + 2,
                "Clicking New Text Document from the file context menu did not create a file.");
            var createdTextFile = window.ViewModel.Items.Single(candidate =>
                candidate.Model.ItemKind == ItemKind.File
                && string.Equals(candidate.DisplayName, "新建文本文档.txt", StringComparison.Ordinal));
            Assert.True(File.Exists(createdTextFile.Model.EffectivePath));
        }
        finally
        {
            AwaitWithDispatcher(manager.CloseAllAsync());
            try
            {
                if (originalClipboard is null)
                {
                    Clipboard.Clear();
                }
                else
                {
                    Clipboard.SetDataObject(originalClipboard, copy: true);
                }
            }
            catch (Exception) when (OperatingSystem.IsWindows())
            {
            }
            SetCursorPos(originalCursor.X, originalCursor.Y);
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    private static void RightClickPhysical(Window window, FrameworkElement element)
    {
        var center = element.PointToScreen(
            new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        Assert.True(SetCursorPos((int)Math.Round(center.X), (int)Math.Round(center.Y)));
        PumpDispatcher();
        Thread.Sleep(10);
        SetWindowPos(
            new WindowInteropHelper(window).Handle,
            new nint(-1),
            0,
            0,
            0,
            0,
            0x0001 | 0x0002 | 0x0010);
        Assert.Equal(
            new WindowInteropHelper(window).Handle,
            GlobalMouseButtonMonitor.HitTestWindowHandle(
                (int)Math.Round(center.X),
                (int)Math.Round(center.Y)));
        MouseEvent(0x0008, 0, 0, 0, 0);
        PumpDispatcher();
        Thread.Sleep(20);
        MouseEvent(0x0010, 0, 0, 0, 0);
        PumpDispatcher();
    }

    private static void ClickPhysical(FrameworkElement element)
    {
        var center = element.PointToScreen(
            new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        Assert.True(SetCursorPos((int)Math.Round(center.X), (int)Math.Round(center.Y)));
        PumpDispatcher();
        Thread.Sleep(10);
        var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(element));
        SetWindowPos(
            source.Handle,
            new nint(-1),
            0,
            0,
            0,
            0,
            0x0001 | 0x0002 | 0x0010);
        Assert.Equal(
            source.Handle,
            GlobalMouseButtonMonitor.HitTestWindowHandle(
                (int)Math.Round(center.X),
                (int)Math.Round(center.Y)));
        MouseEvent(0x0002, 0, 0, 0, 0);
        PumpDispatcher();
        Thread.Sleep(20);
        MouseEvent(0x0004, 0, 0, 0, 0);
        PumpDispatcher();
    }

    private static void InvokeButton(Button button)
    {
        var invokeProvider = Assert.IsAssignableFrom<IInvokeProvider>(
            new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke));
        invokeProvider.Invoke();
        PumpDispatcher();
    }

    private static void InvokeMenuItem(MenuItem menuItem)
    {
        menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        PumpDispatcher();
    }

    private static void AwaitWithDispatcher(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            PumpDispatcher();
            Thread.Sleep(5);
        }

        Assert.True(task.IsCompleted, "The desktop manager operation timed out.");
        task.GetAwaiter().GetResult();
    }

    private static void RaisePreviewMouseDown(UIElement target)
    {
        var source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException("The mouse target is not connected to a presentation source.");
        target.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
            Source = target
        });
    }

    private static void AssertDesktopPaperManagerWindowArchivesPaper()
    {
        var service = new ArchivableDesktopPaperService(
            new DesktopPaperSummary(
                "hidden-note",
                "已隐藏笔记",
                "笔记便签",
                "12 个字符",
                false));
        var viewModel = new DesktopPaperManagerViewModel(service);
        var window = new DesktopPaperManagerWindow(viewModel)
        {
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            PumpDispatcher();

            Assert.True(window.IsVisible);
            Assert.Equal(1, viewModel.HiddenPaperCount);
            Assert.True(viewModel.Papers.Single().IsHidden);
            Assert.NotNull(FindVisualDescendant<Button>(
                window,
                button => string.Equals(
                    button.ToolTip as string,
                    "永久删除所有已隐藏的便签",
                    StringComparison.Ordinal)));
            var archiveButton = FindVisualDescendant<Button>(
                window,
                button => string.Equals(
                    button.ToolTip as string,
                    "归档到归档区",
                    StringComparison.Ordinal));
            Assert.NotNull(archiveButton);

            archiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();

            Assert.Empty(viewModel.Papers);
            Assert.Equal(["hidden-note"], service.ArchivedPaperIds);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertMainArchivePageShowsDesktopPapers(
        DrawerService drawerService,
        DrawerRepository repository,
        string root)
    {
        var logger = new NoOpLogger();
        var launcher = new NoOpFileLauncher();
        var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
        var quickPanelViewModel = new QuickPanelViewModel(
            drawerService,
            launcher,
            logger,
            visualStyleStore);
        var paperService = new ArchivePageDesktopPaperService(
            new DesktopPaperSummary(
                "archived-paper",
                "归档区便签",
                "笔记便签",
                "5 个字符",
                false));
        var paths = new AppPaths(root);
        var viewModel = new MainViewModel(
            drawerService,
            new TodoService(repository),
            launcher,
            logger,
            quickPanelViewModel,
            new UpdateService(logger),
            visualStyleStore,
            new BoxPositionLockStateStore(drawerService, logger),
            paths,
            new DataStorageMigrationService(
                paths,
                repository,
                new StorageLocationStore(Path.Combine(root, "storage-location.json"))),
            paperTodoHost: paperService);
        viewModel.ShowArchiveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        using var paperTodoHost = new PaperTodoHost(Path.Combine(root, "paper-host"), logger);
        var quickPanelWindow = new QuickPanelWindow(quickPanelViewModel);
        var window = new MainWindow(
            viewModel,
            quickPanelWindow,
            logger,
            new QuickPanelHotKeySettingsStore(drawerService),
            QuickPanelHotKey.Default,
            paperTodoHost)
        {
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            PumpDispatcher();

            var archivedPaperList = FindVisualDescendant<ListBox>(
                window,
                list => ReferenceEquals(list.ItemsSource, viewModel.ArchivedPapers));
            Assert.NotNull(archivedPaperList);
            Assert.NotNull(FindVisualDescendant<Button>(
                archivedPaperList,
                button => ReferenceEquals(
                    button.Command,
                    viewModel.RestoreArchivedPaperCommand)));
            Assert.NotNull(FindVisualDescendant<Button>(
                archivedPaperList,
                button => ReferenceEquals(
                    button.Command,
                    viewModel.DeleteArchivedPaperCommand)));
        }
        finally
        {
            window.ForceClose();
        }
    }

    private static void WaitFor(Func<bool> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { },
                System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(10);
        }

        Assert.True(condition(), failureMessage);
    }

    private static void PumpDispatcher()
    {
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void PumpRenderFrame()
    {
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private static void InitializeWindowTestResources(Application application)
    {
        application.Resources["BooleanToVisibilityConverter"] =
            new BooleanToVisibilityConverter();
        application.Resources["InverseBooleanToVisibilityConverter"] =
            new InverseBooleanToVisibilityConverter();
        application.Resources["ZeroToVisibilityConverter"] =
            new ZeroToVisibilityConverter();
        application.Resources["NonEmptyToVisibilityConverter"] =
            new NonEmptyToVisibilityConverter();
        application.Resources["BoxVisualStyleEqualityConverter"] =
            new BoxVisualStyleEqualityConverter();
        application.Resources["NoteHeadingFontSizeConverter"] =
            new NoteHeadingFontSizeConverter();
        application.Resources["NoteHeadingFontWeightConverter"] =
            new NoteHeadingFontWeightConverter();
        application.Resources[typeof(ListBox)] = new Style(typeof(ListBox));
        application.Resources["ProjectCompactComboBoxStyle"] = new Style(typeof(ComboBox));
        application.Resources["MappingModeButtonStyle"] = new Style(typeof(Button));
        application.Resources["TodoArchiveButtonStyle"] = new Style(typeof(Button));
        application.Resources["TodoCompletionButtonStyle"] = new Style(typeof(Button));
        application.Resources["DrawerTileButtonStyle"] = new Style(typeof(Button));
        application.Resources["DrawerScrollBarStyle"] =
            new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
        application.Resources["GhostButtonStyle"] = new Style(typeof(Button));
        application.Resources["DangerButtonStyle"] = new Style(typeof(Button));
        application.Resources["PrimaryButtonStyle"] = new Style(typeof(Button));
        application.Resources["AppBackgroundBrush"] = Brushes.White;
        application.Resources["PanelBrush"] = Brushes.White;
        application.Resources["PanelAltBrush"] = Brushes.WhiteSmoke;
        application.Resources["BorderBrushSoft"] = Brushes.LightGray;
        application.Resources["TextPrimaryBrush"] = Brushes.Black;
        application.Resources["TextMutedBrush"] = Brushes.DimGray;
        application.Resources["AccentBrush"] = Brushes.DodgerBlue;
        application.Resources["AccentSoftBrush"] = Brushes.LightBlue;
        application.Resources["HoverBrush"] = Brushes.Gainsboro;
        application.Resources["DangerBrush"] = Brushes.IndianRed;
        application.Resources["DangerSoftBrush"] = Brushes.MistyRose;
    }

    private static T? FindVisualDescendant<T>(
        DependencyObject parent,
        Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T typed && predicate(typed))
            {
                return typed;
            }

            var descendant = FindVisualDescendant(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static async Task<(DrawerService Service, DrawerRepository Repository)> CreateDrawerServiceAsync(
        string root)
    {
        var paths = new AppPaths(root);
        var repository = new DrawerRepository(paths.DatabasePath);
        var drawerService = new DrawerService(paths, repository);
        await drawerService.InitializeAsync();
        return (drawerService, repository);
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

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

    private sealed class NoOpLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Error(Exception exception, string message) { }
    }

    private sealed class ArchivableDesktopPaperService(
        DesktopPaperSummary paper) : IDesktopPaperService
    {
        private DesktopPaperSummary? _paper = paper;

        public List<string> ArchivedPaperIds { get; } = [];

        public void CreateTodoPaper() { }

        public void CreateNotePaper() { }

        public IReadOnlyList<DesktopPaperSummary> GetPapers() =>
            _paper is null ? [] : [_paper];

        public IReadOnlyList<DesktopPaperSummary> GetArchivedPapers() => [];

        public bool ShowPaper(string paperId) => false;

        public bool ArchivePaper(string paperId)
        {
            if (_paper?.Id != paperId)
            {
                return false;
            }

            ArchivedPaperIds.Add(paperId);
            _paper = null;
            return true;
        }

        public bool DeletePaper(string paperId) => false;

        public int DeleteHiddenPapers() => 0;

        public IReadOnlyList<string> ArchivePapers(IEnumerable<string> paperIds) =>
            paperIds.Where(ArchivePaper).ToArray();

        public IReadOnlyList<string> RestoreArchivedPapers(IEnumerable<string> paperIds) => [];
    }

    private sealed class ArchivePageDesktopPaperService(
        DesktopPaperSummary archivedPaper) : IDesktopPaperService
    {
        public void CreateTodoPaper() { }

        public void CreateNotePaper() { }

        public IReadOnlyList<DesktopPaperSummary> GetPapers() => [];

        public IReadOnlyList<DesktopPaperSummary> GetArchivedPapers() => [archivedPaper];

        public bool ShowPaper(string paperId) => false;

        public bool ArchivePaper(string paperId) => false;

        public bool DeletePaper(string paperId) => false;

        public int DeleteHiddenPapers() => 0;

        public IReadOnlyList<string> ArchivePapers(IEnumerable<string> paperIds) => [];

        public IReadOnlyList<string> RestoreArchivedPapers(IEnumerable<string> paperIds) => [];
    }

    private sealed class StaticProjectTodoCountProvider(
        IReadOnlyDictionary<string, int> counts) : IProjectTodoCountProvider
    {
        public event EventHandler<ProjectTodoCountChangedEventArgs>? TodoCountChanged;

        public int GetUnfinishedTodoCount(string paperId) =>
            counts.TryGetValue(paperId, out var count) ? count : 0;

        public void RaiseChanged(string paperId) =>
            TodoCountChanged?.Invoke(
                this,
                new ProjectTodoCountChangedEventArgs(
                    paperId,
                    GetUnfinishedTodoCount(paperId)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        nuint extraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private static nint MakeScreenPointLParam(int x, int y) =>
        unchecked((nint)(int)(((y & 0xFFFF) << 16) | (x & 0xFFFF)));

    private static nint MakeMessageLParam(int lowWord, int highWord) =>
        unchecked((nint)(int)(((highWord & 0xFFFF) << 16) | (lowWord & 0xFFFF)));

    private static void PerformNativeLeftResize(
        nint windowHandle,
        NativeRect currentRectangle)
    {
        const uint wmEnterSizeMove = 0x0231;
        const uint wmSizing = 0x0214;
        const uint wmExitSizeMove = 0x0232;
        const int wmszLeft = 1;
        const uint swpNoZOrder = 0x0004;
        const uint swpNoActivate = 0x0010;
        var targetRectangle = new NativeRect
        {
            Left = currentRectangle.Left - 160,
            Top = currentRectangle.Top,
            Right = currentRectangle.Right,
            Bottom = currentRectangle.Bottom,
        };
        var rectangleBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(targetRectangle, rectangleBuffer, false);
            SendMessage(
                windowHandle,
                wmEnterSizeMove,
                nint.Zero,
                nint.Zero);
            SendMessage(
                windowHandle,
                wmSizing,
                (nint)wmszLeft,
                rectangleBuffer);
            Assert.True(
                SetWindowPos(
                    windowHandle,
                    nint.Zero,
                    targetRectangle.Left,
                    targetRectangle.Top,
                    targetRectangle.Right - targetRectangle.Left,
                    targetRectangle.Bottom - targetRectangle.Top,
                    swpNoZOrder | swpNoActivate),
                $"SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.");
            SendMessage(
                windowHandle,
                wmExitSizeMove,
                nint.Zero,
                nint.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(rectangleBuffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);
}
