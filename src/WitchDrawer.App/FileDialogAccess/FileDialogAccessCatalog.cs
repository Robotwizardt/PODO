using System.IO;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.FileDialogAccess;

internal sealed record FileDialogAccessEntry(
    Guid BoxId,
    string Name,
    BoxType Type,
    string StoragePath,
    bool IsAvailable,
    string? StatusText);

internal static class FileDialogAccessCatalog
{
    public static IReadOnlyList<FileDialogAccessEntry> CreateEntries(IEnumerable<Box> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        return boxes
            .Where(box => !box.IsArchived)
            .Where(box => box.Type is BoxType.Normal or BoxType.Pixel or BoxType.Drawer or BoxType.Bound)
            .Where(box => !string.IsNullOrWhiteSpace(box.StoragePath))
            .OrderBy(box => box.SortOrder)
            .ThenBy(box => box.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(CreateEntry)
            .ToArray();
    }

    public static IReadOnlyList<FileDialogAccessEntry> Search(
        IEnumerable<FileDialogAccessEntry> entries,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return entries.ToArray();
        }

        return entries.Where(entry =>
                entry.Name.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase)
                || GetTypeLabel(entry.Type).Contains(
                    normalizedQuery,
                    StringComparison.CurrentCultureIgnoreCase)
                || entry.StoragePath.Contains(
                    normalizedQuery,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static FileDialogAccessEntry CreateEntry(Box box)
    {
        string storagePath;
        var isAvailable = false;
        try
        {
            storagePath = Path.GetFullPath(box.StoragePath!);
            isAvailable = Directory.Exists(storagePath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            storagePath = box.StoragePath!.Replace("\0", string.Empty).Trim();
            if (storagePath.Length == 0)
            {
                storagePath = "无效路径";
            }
        }

        return new FileDialogAccessEntry(
            box.Id,
            box.Name,
            box.Type,
            storagePath,
            isAvailable,
            isAvailable ? null : "目录不可用");
    }

    private static string GetTypeLabel(BoxType type) => type switch
    {
        BoxType.Normal => "普通收纳盒",
        BoxType.Pixel => "像素收纳盒",
        BoxType.Drawer => "抽屉收纳盒",
        BoxType.Bound => "目标收纳盒",
        _ => type.ToString()
    };
}
