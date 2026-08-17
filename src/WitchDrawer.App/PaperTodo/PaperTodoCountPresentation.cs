namespace PaperTodo;

public static class PaperTodoCountPresentation
{
    public static int GetUnfinishedCount(PaperData paper)
    {
        ArgumentNullException.ThrowIfNull(paper);
        return string.Equals(paper.Type, PaperTypes.Todo, StringComparison.Ordinal)
            ? paper.Items.Count(item => !item.Done && !string.IsNullOrWhiteSpace(item.Text))
            : 0;
    }

    public static string AppendToCapsuleTitle(PaperData paper, string title)
    {
        var unfinishedCount = GetUnfinishedCount(paper);
        return unfinishedCount > 0
            ? $"{title} · {unfinishedCount}项"
            : title;
    }
}
