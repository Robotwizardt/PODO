using System.IO;
using WitchDrawer.App.FileDialogAccess;
using WitchDrawer.Core;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class FileDialogAccessSettingsTests
{
    [Fact]
    public void RecordRecentBox_KeepsThreeDistinctMostRecentBoxIds()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var fourth = Guid.NewGuid();
        var settings = FileDialogAccessSettings.Default;

        settings = settings.RecordRecentBox(first);
        settings = settings.RecordRecentBox(second);
        settings = settings.RecordRecentBox(third);
        settings = settings.RecordRecentBox(first);
        settings = settings.RecordRecentBox(fourth);

        Assert.Equal(new[] { fourth, first, third }, settings.RecentBoxIds);
    }

    [Fact]
    public async Task SaveAsync_PersistsOnlyAccessWindowPreferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "PODO-AccessSettings", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var service = new DrawerService(paths, new DrawerRepository(paths.DatabasePath));
            await service.InitializeAsync();
            var store = new FileDialogAccessSettingsStore(service);
            var recentBox = Guid.NewGuid();
            var settings = FileDialogAccessSettings.Default with
            {
                IsEnabled = false,
                Width = 376,
                RecentBoxIds = [recentBox],
                BlacklistedApplications = [@"C:\Program Files\Blocked\blocked.exe"]
            };

            await store.SaveAsync(settings);
            var reloaded = await new FileDialogAccessSettingsStore(service).LoadAsync();

            Assert.Equal(settings.IsEnabled, reloaded.IsEnabled);
            Assert.Equal(settings.Width, reloaded.Width);
            Assert.Equal(settings.RecentBoxIds, reloaded.RecentBoxIds);
            Assert.Equal(settings.BlacklistedApplications, reloaded.BlacklistedApplications);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
