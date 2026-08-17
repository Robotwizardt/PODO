using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class NoteServiceTests
{
    [Fact]
    public async Task SaveAsync_PersistsNoteAndReloadsItFromAnotherService()
    {
        using var workspace = await NoteWorkspace.CreateAsync();

        var saved = await workspace.Service.SaveAsync(
            workspace.NoteBox.Id,
            "# 今日\n\n- 整理桌面");

        var reloadedService = new NoteService(new DrawerRepository(workspace.DatabasePath));
        var reloaded = await reloadedService.GetAsync(workspace.NoteBox.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(saved.BoxId, reloaded!.BoxId);
        Assert.Equal("# 今日\n\n- 整理桌面", reloaded.Content);
        Assert.Equal(saved.UpdatedAt, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task EnsureAsync_CreatesEmptyDocumentOnlyForNoteBoxes()
    {
        using var workspace = await NoteWorkspace.CreateAsync();

        var created = await workspace.Service.EnsureAsync(workspace.NoteBox.Id);
        var existing = await workspace.Service.EnsureAsync(workspace.NoteBox.Id);

        Assert.Equal(string.Empty, created.Content);
        Assert.Equal(created, existing);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.EnsureAsync(workspace.NormalBox.Id));
    }

    [Fact]
    public async Task SaveAsync_RejectsContentOverMaximumLength()
    {
        using var workspace = await NoteWorkspace.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.SaveAsync(
                workspace.NoteBox.Id,
                new string('x', NoteService.MaximumContentLength + 1)));
    }

    private sealed class NoteWorkspace : IDisposable
    {
        private NoteWorkspace(
            string root,
            string databasePath,
            DrawerService drawerService,
            NoteService service,
            Box noteBox,
            Box normalBox)
        {
            Root = root;
            DatabasePath = databasePath;
            DrawerService = drawerService;
            Service = service;
            NoteBox = noteBox;
            NormalBox = normalBox;
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public DrawerService DrawerService { get; }

        public NoteService Service { get; }

        public Box NoteBox { get; }

        public Box NormalBox { get; }

        public static async Task<NoteWorkspace> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "Podo.NoteServiceTests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var noteBox = await drawerService.CreateBoxAsync("笔记便签", BoxType.Note);
            var normalBox = await drawerService.CreateBoxAsync("文件盒", BoxType.Normal);

            return new NoteWorkspace(
                root,
                paths.DatabasePath,
                drawerService,
                new NoteService(repository),
                noteBox,
                normalBox);
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
