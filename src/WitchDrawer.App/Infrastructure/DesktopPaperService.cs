namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// The small management surface PODO exposes for PaperTodo papers.
/// It deliberately works with snapshots so the WPF view does not depend on
/// PaperTodo's mutable persistence model.
/// </summary>
public interface IDesktopPaperService
{
    void CreateTodoPaper();

    void CreateNotePaper();

    IReadOnlyList<DesktopPaperSummary> GetPapers();

    IReadOnlyList<DesktopPaperSummary> GetArchivedPapers();

    bool ShowPaper(string paperId);

    bool ArchivePaper(string paperId);

    bool DeletePaper(string paperId);

    int DeleteHiddenPapers();

    IReadOnlyList<string> ArchivePapers(IEnumerable<string> paperIds);

    IReadOnlyList<string> RestoreArchivedPapers(IEnumerable<string> paperIds);
}

public sealed record DesktopPaperSummary(
    string Id,
    string Title,
    string KindLabel,
    string DetailLabel,
    bool IsVisible)
{
    public bool IsHidden => !IsVisible;

    public string VisibilityLabel => IsVisible ? "显示中" : "已隐藏";
}
