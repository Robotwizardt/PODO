namespace WitchDrawer.Core.Models;

/// <summary>
/// 一条回收站记录。文件被移入 <c>Recycle\{Id:N}</c> 子目录后写入数据库，
/// 保留 30 天，期间可还原或手动彻底删除。
/// </summary>
public sealed record RecycleEntry(
    Guid Id,
    string DisplayName,
    string RecyclePath,
    string? OriginalPath,
    Guid? BoxId,
    string BoxName,
    BoxType BoxType,
    Guid? SourceItemId,
    bool WasDirectory,
    long SizeBytes,
    DateTimeOffset DeletedAtUtc,
    DateTimeOffset ExpiresAtUtc);
