namespace WitchDrawer.Core.Models;

public sealed record DrawerItem(
    Guid Id,
    Guid BoxId,
    string DisplayName,
    ItemKind ItemKind,
    string? SourcePath,
    string? StoredPath,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? GridColumn = null,
    int? GridRow = null,
    string? RecycleEntryId = null)
{
    public string? EffectivePath => StoredPath ?? SourcePath;

    /// <summary>
    /// 条目已被移入回收站（文件暂存于 Recycle\{id}\），
    /// 尚未彻底删除。此类条目在盒内视图中不可见。
    /// </summary>
    public bool IsRecycled => !string.IsNullOrWhiteSpace(RecycleEntryId);
}
