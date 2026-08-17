using System.Globalization;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class ProjectFolderMemberViewModel(
    ProjectFolderMember member,
    int remainingTodoCount = 0)
{
    public Guid ProjectBoxId => member.ProjectBoxId;

    public string Name => member.ProjectName;

    public string IconText => CreateIconText(Name);

    public ProjectStage Stage => member.Stage;

    public string StageName => ProjectStageCatalog.Get(Stage).Name;

    public string StageColor => ProjectStageCatalog.Get(Stage).Color;

    public int RemainingTodoCount { get; } = Math.Max(0, remainingTodoCount);

    public bool HasRemainingTodos => RemainingTodoCount > 0;

    public string RemainingTodoLabel => $"还有 {RemainingTodoCount} 项待办";

    public string SummaryLabel => HasRemainingTodos
        ? $"{Name} · {StageName} · {RemainingTodoCount} 项待办"
        : $"{Name} · {StageName}";

    private static string CreateIconText(string name)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return "项目";
        }

        var words = trimmedName.Split(
            [' ', '\t', '\r', '\n', '-', '_'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length > 1)
        {
            return string.Concat(words.Take(2).Select(word =>
                StringInfo.GetNextTextElement(word)));
        }

        var textElements = StringInfo.GetTextElementEnumerator(trimmedName);
        var iconText = new List<string>(2);
        while (iconText.Count < 2 && textElements.MoveNext())
        {
            if (textElements.Current is string textElement)
            {
                iconText.Add(textElement);
            }
        }

        return string.Concat(iconText);
    }
}
