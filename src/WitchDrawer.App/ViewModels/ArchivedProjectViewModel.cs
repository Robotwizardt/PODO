using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class ArchivedProjectViewModel
{
    public ArchivedProjectViewModel(
        Box model,
        IReadOnlyList<string> linkedBoxNames,
        IReadOnlyList<string> linkedPaperTitles)
    {
        Model = model;
        LinkedBoxNames = linkedBoxNames;
        LinkedPaperTitles = linkedPaperTitles;
    }

    public Box Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public IReadOnlyList<string> LinkedBoxNames { get; }

    public IReadOnlyList<string> LinkedPaperTitles { get; }

    public int LinkedBoxCount => LinkedBoxNames.Count;

    public int LinkedPaperCount => LinkedPaperTitles.Count;

    public string AssociatedContentLabel
    {
        get
        {
            var parts = new List<string>();
            if (LinkedBoxCount > 0)
            {
                parts.Add($"{LinkedBoxCount} 个文件收纳盒");
            }

            if (LinkedPaperCount > 0)
            {
                parts.Add($"{LinkedPaperCount} 张桌面便签");
            }

            return parts.Count == 0 ? "项目工作区" : string.Join(" · ", parts);
        }
    }

    public string AssociatedContentDescription
    {
        get
        {
            var names = LinkedBoxNames
                .Concat(LinkedPaperTitles)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(4)
                .ToArray();
            if (names.Length == 0)
            {
                return "恢复后会回到原来的桌面布局";
            }

            return string.Join("、", names);
        }
    }

    public string ArchivedTimeLabel
    {
        get
        {
            var time = (Model.ArchivedAt ?? Model.UpdatedAt).ToLocalTime();
            return $"归档于 {time:yyyy-MM-dd HH:mm}";
        }
    }
}
