using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

/// <summary>
/// 应用内回收站。删除应用自己管理的文件时，先把文件移动到
/// <see cref="AppPaths.RecycleDirectory"/>\{记录Id} 下并写入数据库，
/// 而不是直接物理删除；保留 30 天后由 <see cref="PurgeExpiredAsync"/> 自动清理。
/// </summary>
public sealed class RecycleBinService
{
    /// <summary>
    /// 回收记录保留天数。到期后由自动清理物理删除。
    /// </summary>
    public const int RetentionDays = 30;

    private readonly AppPaths _paths;
    private readonly DrawerRepository _repository;
    private readonly Func<string> _desktopDirectoryProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecycleBinService(
        AppPaths paths,
        DrawerRepository repository,
        Func<string>? desktopDirectoryProvider = null)
    {
        _paths = paths;
        _repository = repository;
        _desktopDirectoryProvider = desktopDirectoryProvider
            ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    }

    public string RecycleDirectory => _paths.RecycleDirectory;

    public void EnsureDirectory()
    {
        Directory.CreateDirectory(_paths.RecycleDirectory);
    }

    public Task<IReadOnlyList<RecycleEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetRecycleEntriesAsync(cancellationToken);
    }

    public Task<RecycleEntry?> GetEntryAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        return _repository.GetRecycleEntryAsync(entryId, cancellationToken);
    }

    /// <summary>
    /// 把文件移入回收站并登记。移动成功但写库失败时会尝试把文件搬回原处，
    /// 避免出现"文件在回收站里却没有记录"的不可恢复状态。
    /// </summary>
    public async Task<RecycleEntry> RecycleAsync(
        RecycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sourcePath = PathSafety.GetFullExistingPath(request.SourcePath);
        var isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("要回收的文件不存在。", sourcePath);
        }

        EnsureDirectory();
        EnsureNotInsideRecycleDirectory(sourcePath);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entryId = Guid.NewGuid();
            var containerDirectory = Path.Combine(_paths.RecycleDirectory, entryId.ToString("N"));
            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? entryId.ToString("N")
                    : request.DisplayName;
            }

            var recyclePath = Path.Combine(containerDirectory, fileName);
            Directory.CreateDirectory(containerDirectory);

            await SafeFileOps.MoveAsync(sourcePath, recyclePath, isDirectory, cancellationToken);

            var sizeBytes = await Task.Run(() => ComputeSize(recyclePath, isDirectory), cancellationToken);
            var deletedAtUtc = DateTimeOffset.UtcNow;
            var entry = new RecycleEntry(
                entryId,
                string.IsNullOrWhiteSpace(request.DisplayName) ? fileName : request.DisplayName,
                recyclePath,
                string.IsNullOrWhiteSpace(request.OriginalPath) ? sourcePath : request.OriginalPath,
                request.BoxId,
                request.BoxName,
                request.BoxType,
                request.SourceItemId,
                isDirectory,
                sizeBytes,
                deletedAtUtc,
                deletedAtUtc.AddDays(RetentionDays));

            try
            {
                await _repository.AddRecycleEntryAsync(entry, cancellationToken);
            }
            catch
            {
                // 写库失败：尽力把文件放回原位，让调用方的删除操作整体失败，
                // 而不是留下一个没有记录、用户永远找不到的文件。
                await TryCompensateRestoreAsync(recyclePath, sourcePath, isDirectory);
                throw;
            }

            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 还原一条回收记录。来源盒与条目行都还在时还原回盒内（并恢复条目可见性），
    /// 否则退回到磁盘上的原位置；原位置目录已不存在时落到桌面。
    /// </summary>
    public async Task<RecycleRestoreResult> RestoreAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entry = await _repository.GetRecycleEntryAsync(entryId, cancellationToken)
                ?? throw new InvalidOperationException("回收站记录不存在。");

            if (!File.Exists(entry.RecyclePath) && !Directory.Exists(entry.RecyclePath))
            {
                // 记录还在但文件已丢失（手动清理过目录等）：清掉记录，不让界面留下死条目。
                await _repository.RemoveRecycleEntryAsync(entry.Id, CancellationToken.None);
                return RecycleRestoreResult.FileMissing(entry);
            }

            var restoredIntoBox = await TryRestoreIntoBoxAsync(entry, cancellationToken);
            if (restoredIntoBox is not null)
            {
                return restoredIntoBox;
            }

            return await RestoreToDiskAsync(entry, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 彻底删除一条回收记录对应的文件。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entry = await _repository.GetRecycleEntryAsync(entryId, cancellationToken);
            if (entry is null)
            {
                return false;
            }

            await DeleteEntryAsync(entry, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 清空回收站，返回被彻底删除的记录数。
    /// </summary>
    public async Task<int> EmptyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await _repository.GetRecycleEntriesAsync(cancellationToken);
            var removed = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteEntryAsync(entry, cancellationToken);
                removed++;
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 清理超过 <see cref="RetentionDays"/> 天的记录，返回清理条数。
    /// </summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var expired = await _repository.GetExpiredRecycleEntriesAsync(
                DateTimeOffset.UtcNow,
                cancellationToken);
            var purged = 0;
            foreach (var entry in expired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteEntryAsync(entry, cancellationToken);
                purged++;
            }

            return purged;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> GetTotalSizeBytesAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _repository.GetRecycleEntriesAsync(cancellationToken);
        return await Task.Run(
            () => entries.Sum(entry => entry.SizeBytes),
            cancellationToken);
    }

    private async Task<RecycleRestoreResult?> TryRestoreIntoBoxAsync(
        RecycleEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.BoxId is not Guid boxId
            || entry.SourceItemId is not Guid itemId
            || string.IsNullOrWhiteSpace(entry.OriginalPath))
        {
            return null;
        }

        var box = await _repository.GetBoxAsync(boxId, cancellationToken);
        if (box is null)
        {
            return null;
        }

        var item = await _repository.GetItemAsync(itemId, cancellationToken);
        if (item is null || !string.Equals(item.RecycleEntryId, entry.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var storageRoot = GetBoxStorageRoot(box);
        var targetPath = ResolveRestoreTargetPath(entry, storageRoot);
        if (targetPath is null)
        {
            return null;
        }

        await SafeFileOps.MoveAsync(entry.RecyclePath, targetPath, entry.WasDirectory, cancellationToken);
        await _repository.UpdateItemRecycleStateAsync(itemId, recycleEntryId: null, CancellationToken.None);
        await _repository.RemoveRecycleEntryAsync(entry.Id, CancellationToken.None);

        return RecycleRestoreResult.RestoredToBox(entry, targetPath, box.Name);
    }

    private async Task<RecycleRestoreResult> RestoreToDiskAsync(
        RecycleEntry entry,
        CancellationToken cancellationToken)
    {
        var preferredPath = entry.OriginalPath;
        var preferredDirectory = string.IsNullOrWhiteSpace(preferredPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(preferredPath));

        string targetPath;
        var fellBackToDesktop = false;
        if (!string.IsNullOrWhiteSpace(preferredDirectory) && Directory.Exists(preferredDirectory))
        {
            targetPath = FileNameService.GetUniqueDestinationPath(
                preferredDirectory,
                Path.GetFileName(Path.GetFullPath(preferredPath)),
                entry.WasDirectory);
        }
        else
        {
            var desktop = GetDesktopDirectory();
            targetPath = FileNameService.GetUniqueDestinationPath(
                desktop,
                entry.DisplayName,
                entry.WasDirectory);
            fellBackToDesktop = true;
        }

        await SafeFileOps.MoveAsync(entry.RecyclePath, targetPath, entry.WasDirectory, cancellationToken);
        await _repository.RemoveRecycleEntryAsync(entry.Id, CancellationToken.None);

        return RecycleRestoreResult.RestoredToDisk(entry, targetPath, fellBackToDesktop);
    }

    private static string? ResolveRestoreTargetPath(RecycleEntry entry, string storageRoot)
    {
        if (string.IsNullOrWhiteSpace(entry.OriginalPath))
        {
            return null;
        }

        var originalPath = Path.GetFullPath(entry.OriginalPath);
        var directory = Path.GetDirectoryName(originalPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        // 原路径必须仍位于盒子存储根之内，避免把记录里被篡改的路径当成还原目标。
        // 这里先做一次纯字符串的前缀判断，避免在真正校验通过前就去碰文件系统。
        var root = Path.GetFullPath(storageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!originalPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            // 盒子存储根可能已经被清空删除，补齐父目录后再做重解析点校验。
            Directory.CreateDirectory(directory);
            PathSafety.EnsureChildPath(storageRoot, originalPath);
        }
        catch
        {
            return null;
        }

        return FileNameService.GetUniqueDestinationPath(
            directory,
            Path.GetFileName(originalPath),
            entry.WasDirectory);
    }

    private string GetBoxStorageRoot(Box box)
    {
        var storagePath = string.IsNullOrWhiteSpace(box.StoragePath)
            ? Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"))
            : box.StoragePath;
        return Path.GetFullPath(storagePath);
    }

    private async Task DeleteEntryAsync(RecycleEntry entry, CancellationToken cancellationToken)
    {
        var recyclePath = entry.RecyclePath;
        if (File.Exists(recyclePath) || Directory.Exists(recyclePath))
        {
            try
            {
                await SafeFileOps.DeleteAsync(recyclePath, entry.WasDirectory, cancellationToken);
            }
            catch
            {
                // 单个文件删除失败（占用/权限）不应阻塞其余记录的清理；
                // 记录先保留，下次清理还会再试。
                return;
            }
        }

        // 条目行仍在回收状态时一并移除，避免留下永远不可见的僵尸条目。
        if (entry.SourceItemId is Guid itemId)
        {
            try
            {
                await _repository.RemoveItemAsync(itemId, CancellationToken.None);
            }
            catch
            {
                // 条目行可能已被盒子级删除清掉，忽略。
            }
        }

        await _repository.RemoveRecycleEntryAsync(entry.Id, CancellationToken.None);
        TryDeleteEmptyContainerDirectory(entry);
    }

    private void TryDeleteEmptyContainerDirectory(RecycleEntry entry)
    {
        try
        {
            var containerDirectory = Path.GetDirectoryName(Path.GetFullPath(entry.RecyclePath));
            if (string.IsNullOrWhiteSpace(containerDirectory)
                || !Directory.Exists(containerDirectory)
                || Directory.GetFileSystemEntries(containerDirectory).Length != 0)
            {
                return;
            }

            // 只允许删除回收站内部的空容器目录。
            PathSafety.EnsureChildPath(_paths.RecycleDirectory, containerDirectory);
            Directory.Delete(containerDirectory, recursive: false);
        }
        catch
        {
            // 容器目录清理是尽力而为，失败不影响主流程。
        }
    }

    private void EnsureNotInsideRecycleDirectory(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var recycleRoot = Path.GetFullPath(_paths.RecycleDirectory);
        if (fullSourcePath.Equals(recycleRoot, StringComparison.OrdinalIgnoreCase)
            || fullSourcePath.StartsWith(
                recycleRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("不能把回收站里的文件再次移入回收站。");
        }
    }

    private static async Task TryCompensateRestoreAsync(string recyclePath, string originalPath, bool isDirectory)
    {
        try
        {
            if (!File.Exists(recyclePath) && !Directory.Exists(recyclePath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(originalPath));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            if (File.Exists(originalPath) || Directory.Exists(originalPath))
            {
                return;
            }

            await SafeFileOps.MoveAsync(recyclePath, originalPath, isDirectory);
        }
        catch
        {
            // 补偿失败时文件仍在回收站目录里，至少不会丢失。
        }
    }

    private static long ComputeSize(string path, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => SafeGetLength(file));
            }

            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long SafeGetLength(FileInfo file)
    {
        try
        {
            return file.Exists ? file.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private string GetDesktopDirectory()
    {
        var desktop = _desktopDirectoryProvider();
        if (string.IsNullOrWhiteSpace(desktop))
        {
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(desktop))
        {
            throw new InvalidOperationException("无法解析桌面目录。");
        }

        Directory.CreateDirectory(desktop);
        return desktop;
    }
}

/// <summary>
/// 一次回收操作所需的文件与来源快照。
/// </summary>
public sealed record RecycleRequest(
    string SourcePath,
    string DisplayName,
    string? OriginalPath,
    Guid? BoxId,
    string BoxName,
    BoxType BoxType,
    Guid? SourceItemId,
    bool IsDirectory);

/// <summary>
/// 还原结果。<see cref="RestoredIntoBox"/> 为 true 表示文件回到了原收纳盒并恢复可见。
/// </summary>
public sealed record RecycleRestoreResult(
    RecycleEntry Entry,
    string RestoredPath,
    bool RestoredIntoBox,
    bool RestoredToDesktop,
    bool Missing)
{
    public static RecycleRestoreResult RestoredToBox(RecycleEntry entry, string restoredPath, string boxName)
    {
        return new RecycleRestoreResult(entry, restoredPath, RestoredIntoBox: true, RestoredToDesktop: false, Missing: false)
        {
            BoxName = boxName
        };
    }

    public static RecycleRestoreResult RestoredToDisk(
        RecycleEntry entry,
        string restoredPath,
        bool restoredToDesktop)
    {
        return new RecycleRestoreResult(
            entry,
            restoredPath,
            RestoredIntoBox: false,
            RestoredToDesktop: restoredToDesktop,
            Missing: false);
    }

    public static RecycleRestoreResult FileMissing(RecycleEntry entry)
    {
        return new RecycleRestoreResult(entry, string.Empty, RestoredIntoBox: false, RestoredToDesktop: false, Missing: true);
    }

    public string BoxName { get; init; } = string.Empty;

    public string StatusMessage
    {
        get
        {
            if (Missing)
            {
                return $"{Entry.DisplayName} 的文件已不存在，已清理记录";
            }

            if (RestoredIntoBox)
            {
                return $"已还原 {Entry.DisplayName} 到 {BoxName}";
            }

            if (RestoredToDesktop)
            {
                return $"已还原 {Entry.DisplayName} 到桌面（原位置不可用）";
            }

            return $"已还原 {Entry.DisplayName} 到原位置";
        }
    }
}
