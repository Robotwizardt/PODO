namespace WitchDrawer.Core.Services;

public sealed record ItemDeleteResult(
    Guid ItemId,
    string DisplayName,
    bool WasStoredItem,
    string? RestoredPath,
    bool RestoredToOriginal,
    bool RestoredToDesktop,
    bool PermanentlyDeleted = false,
    string? DeletedPath = null,
    bool Recycled = false,
    string? RecyclePath = null,
    Guid? RecycleEntryId = null)
{
    public static ItemDeleteResult RecycledItem(
        Guid itemId,
        string displayName,
        bool wasStoredItem,
        string recyclePath,
        Guid recycleEntryId)
    {
        return new ItemDeleteResult(
            itemId,
            displayName,
            WasStoredItem: wasStoredItem,
            RestoredPath: null,
            RestoredToOriginal: false,
            RestoredToDesktop: false,
            PermanentlyDeleted: false,
            DeletedPath: null,
            Recycled: true,
            RecyclePath: recyclePath,
            RecycleEntryId: recycleEntryId);
    }

    public static ItemDeleteResult ReferenceRemoved(Guid itemId, string displayName)
    {
        return new ItemDeleteResult(
            itemId,
            displayName,
            WasStoredItem: false,
            RestoredPath: null,
            RestoredToOriginal: false,
            RestoredToDesktop: false,
            PermanentlyDeleted: false,
            DeletedPath: null);
    }

    public static ItemDeleteResult PermanentlyDeletedItem(
        Guid itemId,
        string displayName,
        bool wasStoredItem,
        string deletedPath)
    {
        return new ItemDeleteResult(
            itemId,
            displayName,
            WasStoredItem: wasStoredItem,
            RestoredPath: null,
            RestoredToOriginal: false,
            RestoredToDesktop: false,
            PermanentlyDeleted: true,
            DeletedPath: deletedPath);
    }

    public string StatusMessage
    {
        get
        {
            if (Recycled)
            {
                return $"已移入回收站 {DisplayName}";
            }

            if (PermanentlyDeleted)
            {
                return $"已删除 {DisplayName}";
            }

            if (!WasStoredItem)
            {
                return $"已移除引用 {DisplayName}";
            }

            if (RestoredToDesktop)
            {
                return $"已还原 {DisplayName} 到桌面（原位置不可用）";
            }

            return $"已还原 {DisplayName} 到原位置";
        }
    }
}
