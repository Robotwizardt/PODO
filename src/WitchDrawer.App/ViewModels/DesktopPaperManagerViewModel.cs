using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.ViewModels;

public sealed class DesktopPaperManagerViewModel : ObservableObject
{
    private readonly IDesktopPaperService _paperService;
    private string _statusText = "正在读取桌面便签";

    public DesktopPaperManagerViewModel(IDesktopPaperService paperService)
    {
        _paperService = paperService;
    }

    public ObservableCollection<DesktopPaperSummary> Papers { get; } = [];

    public int PaperCount => Papers.Count;

    public int HiddenPaperCount => Papers.Count(paper => paper.IsHidden);

    public bool HasPapers => Papers.Count > 0;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void Refresh()
    {
        ReplacePapers();
        StatusText = PaperCount == 0
            ? "没有桌面便签"
            : HiddenPaperCount == 0
                ? "所有桌面便签都在显示"
                : $"{HiddenPaperCount} 张便签已隐藏";
    }

    public bool ShowPaper(DesktopPaperSummary? paper)
    {
        if (paper is null || !_paperService.ShowPaper(paper.Id))
        {
            return false;
        }

        ReplacePapers();
        StatusText = "已恢复桌面便签";
        return true;
    }

    public bool DeletePaper(DesktopPaperSummary? paper)
    {
        if (paper is null || !_paperService.DeletePaper(paper.Id))
        {
            return false;
        }

        ReplacePapers();
        StatusText = "已永久删除桌面便签";
        return true;
    }

    public int DeleteHiddenPapers()
    {
        var deletedCount = _paperService.DeleteHiddenPapers();
        ReplacePapers();
        StatusText = deletedCount == 0
            ? "没有需要清理的隐藏便签"
            : $"已永久删除 {deletedCount} 张隐藏便签";
        return deletedCount;
    }

    private void ReplacePapers()
    {
        Papers.Clear();
        foreach (var paper in _paperService.GetPapers())
        {
            Papers.Add(paper);
        }

        OnPropertyChanged(nameof(PaperCount));
        OnPropertyChanged(nameof(HiddenPaperCount));
        OnPropertyChanged(nameof(HasPapers));
    }
}
