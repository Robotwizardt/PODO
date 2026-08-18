namespace WitchDrawer.App.FileDialogAccess;

public sealed record FileDialogAccessSettings(
    bool IsEnabled,
    double Width,
    Guid[] RecentBoxIds,
    string[] BlacklistedApplications)
{
    public static FileDialogAccessSettings Default { get; } = new(
        IsEnabled: true,
        Width: 320,
        RecentBoxIds: [],
        BlacklistedApplications: []);

    public FileDialogAccessSettings RecordRecentBox(Guid boxId)
    {
        if (boxId == Guid.Empty)
        {
            return this;
        }

        return this with
        {
            RecentBoxIds = RecentBoxIds
                .Where(existingId => existingId != boxId)
                .Prepend(boxId)
                .Take(3)
                .ToArray()
        };
    }
}
