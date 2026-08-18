using WitchDrawer.App.FileDialogAccess;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class FileDialogAccessViewModelTests
{
    [Fact]
    public void LoadAndSearch_KeepRecentEntriesSeparateAndUnavailableEntriesDisabled()
    {
        var first = new FileDialogAccessEntry(
            Guid.NewGuid(), "资料", BoxType.Normal, @"D:\Docs", true, null);
        var second = new FileDialogAccessEntry(
            Guid.NewGuid(), "临时", BoxType.Drawer, @"E:\Temp", false, "目录不可用");
        var viewModel = new FileDialogAccessViewModel(_ => Task.CompletedTask);

        viewModel.Load([first, second], [second.BoxId]);

        Assert.Equal(new[] { "资料", "临时" }, viewModel.Entries.Select(item => item.Name));
        var recent = Assert.Single(viewModel.RecentEntries);
        Assert.Equal("临时", recent.Name);
        Assert.False(recent.NavigateCommand.CanExecute(null));

        viewModel.SearchText = "docs";

        Assert.Equal(new[] { "资料" }, viewModel.Entries.Select(item => item.Name));
        Assert.Empty(viewModel.RecentEntries);
    }
}
