using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Tests;

public sealed class DesktopPaperManagerViewModelTests
{
    [Fact]
    public void Refresh_ListsHiddenPapersFirstAndReportsTheirCount()
    {
        var service = new FakeDesktopPaperService(
        [
            new DesktopPaperSummary("visible-note", "显示中的笔记", "笔记便签", "8 个字符", true),
            new DesktopPaperSummary("hidden-todo", "已隐藏待办", "待办便签", "1/2 项待办未完成", false)
        ]);
        var viewModel = new DesktopPaperManagerViewModel(service);

        viewModel.Refresh();

        Assert.Equal(2, viewModel.PaperCount);
        Assert.Equal(1, viewModel.HiddenPaperCount);
        Assert.Equal("hidden-todo", viewModel.Papers[0].Id);
        Assert.Equal("1 张便签已隐藏", viewModel.StatusText);
    }

    [Fact]
    public void ShowPaper_RestoresTheHiddenPaperAndRefreshesTheCount()
    {
        var service = new FakeDesktopPaperService(
        [
            new DesktopPaperSummary("hidden-todo", "已隐藏待办", "待办便签", "空白待办", false)
        ]);
        var viewModel = new DesktopPaperManagerViewModel(service);
        viewModel.Refresh();

        var restored = viewModel.ShowPaper(viewModel.Papers.Single());

        Assert.True(restored);
        Assert.Equal("hidden-todo", service.LastShownPaperId);
        Assert.Equal(0, viewModel.HiddenPaperCount);
        Assert.True(viewModel.Papers.Single().IsVisible);
        Assert.Equal("已恢复桌面便签", viewModel.StatusText);
    }

    [Fact]
    public void DeleteHiddenPapers_RemovesOnlyHiddenPapers()
    {
        var service = new FakeDesktopPaperService(
        [
            new DesktopPaperSummary("hidden-note", "隐藏笔记", "笔记便签", "空白笔记", false),
            new DesktopPaperSummary("visible-todo", "显示待办", "待办便签", "空白待办", true),
            new DesktopPaperSummary("hidden-todo", "隐藏待办", "待办便签", "空白待办", false)
        ]);
        var viewModel = new DesktopPaperManagerViewModel(service);
        viewModel.Refresh();

        var deletedCount = viewModel.DeleteHiddenPapers();

        Assert.Equal(2, deletedCount);
        Assert.Equal(["hidden-note", "hidden-todo"], service.DeletedPaperIds);
        Assert.Single(viewModel.Papers);
        Assert.Equal("visible-todo", viewModel.Papers.Single().Id);
        Assert.Equal(0, viewModel.HiddenPaperCount);
    }

    private sealed class FakeDesktopPaperService(
        IEnumerable<DesktopPaperSummary> papers) : IDesktopPaperService
    {
        private readonly List<DesktopPaperSummary> _papers = papers.ToList();

        public List<string> DeletedPaperIds { get; } = [];

        public string? LastShownPaperId { get; private set; }

        public IReadOnlyList<DesktopPaperSummary> GetPapers() =>
            _papers
                .OrderBy(paper => paper.IsVisible)
                .ThenBy(paper => paper.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        public bool ShowPaper(string paperId)
        {
            var index = _papers.FindIndex(paper => paper.Id == paperId);
            if (index < 0)
            {
                return false;
            }

            LastShownPaperId = paperId;
            _papers[index] = _papers[index] with { IsVisible = true };
            return true;
        }

        public bool DeletePaper(string paperId)
        {
            var index = _papers.FindIndex(paper => paper.Id == paperId);
            if (index < 0)
            {
                return false;
            }

            DeletedPaperIds.Add(paperId);
            _papers.RemoveAt(index);
            return true;
        }

        public int DeleteHiddenPapers()
        {
            var hiddenPaperIds = _papers
                .Where(paper => paper.IsHidden)
                .Select(paper => paper.Id)
                .ToArray();
            foreach (var paperId in hiddenPaperIds)
            {
                DeletePaper(paperId);
            }

            return hiddenPaperIds.Length;
        }
    }
}
