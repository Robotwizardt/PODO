namespace WitchDrawer.App.Infrastructure;

public interface IProjectTodoCountProvider
{
    event EventHandler<ProjectTodoCountChangedEventArgs>? TodoCountChanged;

    int GetUnfinishedTodoCount(string paperId);
}

public sealed class ProjectTodoCountChangedEventArgs(
    string paperId,
    int unfinishedCount) : EventArgs
{
    public string PaperId { get; } = paperId;

    public int UnfinishedCount { get; } = unfinishedCount;
}
