using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

public sealed class DrawerService
{
    private readonly AppPaths _paths;
    private readonly DrawerRepository _repository;
    private readonly SemaphoreSlim _boundSyncGate = new(1, 1);

    private async Task<IDisposable?> AcquireBoundSyncGateAsync(
        bool shouldAcquire,
        CancellationToken cancellationToken)
    {
        if (!shouldAcquire)
        {
            return null;
        }

        await _boundSyncGate.WaitAsync(cancellationToken);
        return new SemaphoreLease(_boundSyncGate);
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }

    public DrawerService(AppPaths paths, DrawerRepository repository)
    {
        _paths = paths;
        _repository = repository;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        await _repository.InitializeAsync(cancellationToken);
        await RepairStoredPathsAsync(cancellationToken);
        await EnsureDefaultBoxesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Box>> GetBoxesAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetBoxesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Box>> GetArchivedBoxesAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetArchivedBoxesAsync(cancellationToken);
    }

    public async Task ReorderBoxesAsync(
        IReadOnlyList<Guid> orderedBoxIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedBoxIds);

        var requestedIds = orderedBoxIds.ToArray();
        if (requestedIds.Distinct().Count() != requestedIds.Length)
        {
            throw new ArgumentException("Box order cannot contain duplicate ids.", nameof(orderedBoxIds));
        }

        var existingBoxes = await _repository.GetBoxesAsync(cancellationToken);
        var existingIds = existingBoxes.Select(box => box.Id).ToHashSet();
        if (requestedIds.Length != existingIds.Count || requestedIds.Any(id => !existingIds.Contains(id)))
        {
            throw new ArgumentException(
                "Box order must contain every existing box exactly once.",
                nameof(orderedBoxIds));
        }

        await _repository.UpdateBoxSortOrdersAsync(requestedIds, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> GetItemsAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        if (box.Type == BoxType.Bound)
        {
            await SynchronizeBoundBoxAsync(box, cancellationToken);
            return await _repository.GetItemsAsync(boxId, cancellationToken);
        }

        await PruneMissingStoredItemsAsync(boxId, cancellationToken);
        return await _repository.GetItemsAsync(boxId, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> GetAllItemsAsync(CancellationToken cancellationToken = default)
    {
        await SynchronizeAllBoundBoxesAsync(cancellationToken);
        await PruneMissingStoredItemsAsync(null, cancellationToken);
        return await _repository.GetItemsAsync(null, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> SearchItemsAsync(string query, int limit = 200, CancellationToken cancellationToken = default)
    {
        await SynchronizeAllBoundBoxesAsync(cancellationToken);
        await PruneMissingStoredItemsAsync(null, cancellationToken);
        return await _repository.SearchItemsAsync(query.Trim(), limit, cancellationToken);
    }

    public async Task<Box> CreateBoxAsync(string name, BoxType type, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Box name cannot be empty.", nameof(name));
        }

        if (type == BoxType.Bound)
        {
            throw new InvalidOperationException("目标收纳盒必须绑定一个现有文件夹。请使用 CreateBoundBoxAsync。");
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var storagePath = type is BoxType.Normal or BoxType.Pixel or BoxType.Drawer
            ? Path.Combine(_paths.BoxesDirectory, id.ToString("N"))
            : null;
        if (storagePath is not null)
        {
            Directory.CreateDirectory(storagePath);
        }

        var box = new Box(
            id,
            name.Trim(),
            type,
            storagePath,
            await _repository.GetNextBoxSortOrderAsync(cancellationToken),
            now,
            now);

        await _repository.AddBoxAsync(box, cancellationToken);
        return box;
    }

    public async Task<Box> CreateBoundBoxAsync(
        string name,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Box name cannot be empty.", nameof(name));
        }

        var fullFolderPath = GetExistingDirectoryPath(folderPath);
        PathSafety.EnsureNoReparsePoints(fullFolderPath);

        if (ArePathsOverlapping(fullFolderPath, _paths.BoxesDirectory))
        {
            throw new InvalidOperationException("不能把 PODO 自身的数据目录绑定为目标收纳盒。");
        }

        var existingBoundBoxes = await _repository.GetAllBoxesAsync(cancellationToken);
        if (existingBoundBoxes.Any(existingBox =>
                existingBox.Type == BoxType.Bound
                && ArePathsOverlapping(fullFolderPath, existingBox.StoragePath)))
        {
            throw new InvalidOperationException("这个文件夹或其上级目录已经被目标收纳盒绑定。");
        }

        var now = DateTimeOffset.UtcNow;
        var box = new Box(
            Guid.NewGuid(),
            name.Trim(),
            BoxType.Bound,
            fullFolderPath,
            await _repository.GetNextBoxSortOrderAsync(cancellationToken),
            now,
            now);

        await _repository.AddBoxAsync(box, cancellationToken);
        try
        {
            await SynchronizeBoundBoxAsync(box, cancellationToken);
            return box;
        }
        catch
        {
            await _repository.RemoveBoxAsync(box.Id, CancellationToken.None);
            throw;
        }
    }

    public async Task<DrawerItem> ImportPathAsync(
        Guid boxId,
        string sourcePath,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        using var boundSyncGate = await AcquireBoundSyncGateAsync(
            box.Type == BoxType.Bound,
            cancellationToken);

        if (box.Type is BoxType.Todo or BoxType.Note or BoxType.Project or BoxType.ProjectFolder)
        {
            throw new InvalidOperationException("便签和项目盒不接受文件拖入。");
        }

        var fullSourcePath = PathSafety.GetFullExistingPath(sourcePath);
        var isDirectory = Directory.Exists(fullSourcePath);
        var itemKind = isDirectory ? ItemKind.Directory : ItemKind.File;
        var displayName = Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var sortOrder = await _repository.GetNextItemSortOrderAsync(boxId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        DrawerItem item;
        if (box.Type == BoxType.Mapping)
        {
            item = new DrawerItem(
                Guid.NewGuid(),
                box.Id,
                displayName,
                itemKind,
                fullSourcePath,
                null,
                sortOrder,
                now,
                now,
                gridColumn,
                gridRow);
        }
        else
        {
            var storageRoot = GetStorageRoot(box, createIfMissing: box.Type != BoxType.Bound);
            if (box.Type == BoxType.Bound && IsDirectChildPath(storageRoot, fullSourcePath))
            {
                var existingItem = (await _repository.GetItemsAsync(box.Id, cancellationToken))
                    .FirstOrDefault(candidate => PathsEqual(candidate.StoredPath, fullSourcePath));
                if (existingItem is not null)
                {
                    if (gridColumn is not null || gridRow is not null)
                    {
                        await _repository.UpdateItemGridPositionAsync(
                            existingItem.Id,
                            gridColumn,
                            gridRow,
                            cancellationToken);
                    }

                    return existingItem with
                    {
                        GridColumn = gridColumn ?? existingItem.GridColumn,
                        GridRow = gridRow ?? existingItem.GridRow
                    };
                }

                item = new DrawerItem(
                    Guid.NewGuid(),
                    box.Id,
                    displayName,
                    itemKind,
                    fullSourcePath,
                    fullSourcePath,
                    sortOrder,
                    now,
                    now,
                    gridColumn,
                    gridRow);
                await _repository.AddItemAsync(item, cancellationToken);
                return item;
            }

            var targetPath = FileNameService.GetUniqueDestinationPath(storageRoot, displayName, isDirectory);
            PathSafety.EnsureChildPath(storageRoot, targetPath);

            cancellationToken.ThrowIfCancellationRequested();
            await SafeFileOps.MoveAsync(fullSourcePath, targetPath, isDirectory, cancellationToken);

            item = new DrawerItem(
                Guid.NewGuid(),
                box.Id,
                Path.GetFileName(targetPath),
                itemKind,
                fullSourcePath,
                targetPath,
                sortOrder,
                now,
                now,
                gridColumn,
                gridRow);

            try
            {
                await _repository.AddItemAsync(item, CancellationToken.None);
            }
            catch
            {
                await TryCompensateMoveAsync(targetPath, fullSourcePath, isDirectory);
                throw;
            }

            return item;
        }

        await _repository.AddItemAsync(item, cancellationToken);
        return item;
    }

    public Task UpdateItemGridPositionAsync(
        Guid itemId,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken = default)
    {
        return _repository.UpdateItemGridPositionAsync(itemId, gridColumn, gridRow, cancellationToken);
    }

    public async Task<DrawerItem> CreateFileSystemItemAsync(
        Guid boxId,
        ItemKind itemKind,
        string desiredName,
        CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        if (box.Type is not (BoxType.Normal or BoxType.Bound))
        {
            throw new InvalidOperationException("只有普通收纳盒和目标收纳盒支持新建文件或文件夹。");
        }

        var normalizedName = ValidateFileSystemItemName(desiredName);
        var isDirectory = itemKind switch
        {
            ItemKind.Directory => true,
            ItemKind.File => false,
            _ => throw new ArgumentOutOfRangeException(nameof(itemKind))
        };
        using var boundSyncGate = await AcquireBoundSyncGateAsync(
            box.Type == BoxType.Bound,
            cancellationToken);
        var storageRoot = GetStorageRoot(box, createIfMissing: box.Type != BoxType.Bound);
        var targetPath = FileNameService.GetUniqueDestinationPath(
            storageRoot,
            normalizedName,
            isDirectory);
        PathSafety.EnsureChildPath(storageRoot, targetPath);

        await SafeFileOps.CreateAsync(targetPath, isDirectory, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(),
            box.Id,
            Path.GetFileName(targetPath),
            itemKind,
            null,
            targetPath,
            await _repository.GetNextItemSortOrderAsync(boxId, cancellationToken),
            now,
            now);
        try
        {
            await _repository.AddItemAsync(item, CancellationToken.None);
            return item;
        }
        catch
        {
            await Task.Run(() =>
            {
                if (isDirectory && Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, recursive: true);
                }
                else if (!isDirectory && File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
            });
            throw;
        }
    }

    private static string ValidateFileSystemItemName(string desiredName)
    {
        var name = desiredName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || Path.IsPathRooted(name)
            || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("请输入有效的文件或文件夹名称。", nameof(desiredName));
        }

        return name;
    }

    public async Task<DrawerItem> RenameFileSystemItemAsync(
        Guid itemId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");
        var box = await _repository.GetBoxAsync(item.BoxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        if (box.Type is not (BoxType.Normal or BoxType.Bound))
        {
            throw new InvalidOperationException("只有普通收纳盒和目标收纳盒支持文件改名。");
        }

        var normalizedName = ValidateFileSystemItemName(newName);
        var storedPath = item.StoredPath
            ?? throw new InvalidOperationException("Item does not have a managed file path.");
        EnsureStoredItemPathBelongsToBox(box, storedPath);
        if (string.Equals(item.DisplayName, normalizedName, StringComparison.Ordinal))
        {
            return item;
        }

        using var boundSyncGate = await AcquireBoundSyncGateAsync(
            box.Type == BoxType.Bound,
            cancellationToken);
        var parentDirectory = Path.GetDirectoryName(storedPath)
            ?? throw new InvalidOperationException("Item directory is unavailable.");
        var targetPath = Path.Combine(parentDirectory, normalizedName);
        PathSafety.EnsureChildPath(GetStorageRoot(box, createIfMissing: false), targetPath);
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            throw new IOException($"同名文件或文件夹已存在：{normalizedName}");
        }

        var isDirectory = item.ItemKind == ItemKind.Directory;
        await SafeFileOps.MoveAsync(storedPath, targetPath, isDirectory, cancellationToken);
        var renamedSourcePath = string.IsNullOrWhiteSpace(item.SourcePath)
            ? null
            : Path.Combine(Path.GetDirectoryName(item.SourcePath) ?? parentDirectory, normalizedName);
        var updatedAt = DateTimeOffset.UtcNow;
        try
        {
            await _repository.UpdateItemFileSystemIdentityAsync(
                item.Id,
                normalizedName,
                renamedSourcePath,
                targetPath,
                updatedAt,
                CancellationToken.None);
        }
        catch
        {
            await TryCompensateMoveAsync(targetPath, storedPath, isDirectory);
            throw;
        }

        return item with
        {
            DisplayName = normalizedName,
            SourcePath = renamedSourcePath,
            StoredPath = targetPath,
            UpdatedAt = updatedAt
        };
    }

    public async Task<IReadOnlyList<DrawerItem>> CopyPathsIntoBoxAsync(
        Guid boxId,
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        if (box.Type is not (BoxType.Normal or BoxType.Bound))
        {
            throw new InvalidOperationException("只有普通收纳盒和目标收纳盒支持粘贴文件。");
        }

        var normalizedSources = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(PathSafety.GetFullExistingPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedSources.Length == 0)
        {
            return [];
        }

        using var boundSyncGate = await AcquireBoundSyncGateAsync(
            box.Type == BoxType.Bound,
            cancellationToken);
        var storageRoot = GetStorageRoot(box, createIfMissing: box.Type != BoxType.Bound);
        var nextSortOrder = await _repository.GetNextItemSortOrderAsync(boxId, cancellationToken);
        var copiedItems = new List<DrawerItem>(normalizedSources.Length);
        foreach (var sourcePath in normalizedSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = Directory.Exists(sourcePath);
            var displayName = Path.GetFileName(
                sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var targetPath = FileNameService.GetUniqueDestinationPath(
                storageRoot,
                displayName,
                isDirectory);
            PathSafety.EnsureChildPath(storageRoot, targetPath);
            await SafeFileOps.CopyAsync(sourcePath, targetPath, isDirectory, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var item = new DrawerItem(
                Guid.NewGuid(),
                box.Id,
                Path.GetFileName(targetPath),
                isDirectory ? ItemKind.Directory : ItemKind.File,
                null,
                targetPath,
                nextSortOrder++,
                now,
                now);
            try
            {
                await _repository.AddItemAsync(item, CancellationToken.None);
                copiedItems.Add(item);
            }
            catch
            {
                await SafeFileOps.DeleteAsync(targetPath, isDirectory);
                throw;
            }
        }

        return copiedItems;
    }

    public async Task MoveItemToBoxAsync(
        Guid itemId,
        Guid targetBoxId,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");
        var sourceBox = await _repository.GetBoxAsync(item.BoxId, cancellationToken)
            ?? throw new InvalidOperationException("Source box does not exist.");
        var targetBox = await _repository.GetBoxAsync(targetBoxId, cancellationToken)
            ?? throw new InvalidOperationException("Target box does not exist.");
        using var boundSyncGate = await AcquireBoundSyncGateAsync(
            sourceBox.Type == BoxType.Bound || targetBox.Type == BoxType.Bound,
            cancellationToken);

        if (sourceBox.Type is BoxType.Todo or BoxType.Note or BoxType.Project or BoxType.ProjectFolder
            || targetBox.Type is BoxType.Todo or BoxType.Note or BoxType.Project or BoxType.ProjectFolder)
        {
            throw new InvalidOperationException("文件不能移入或移出便签盒、项目盒。");
        }

        if (item.BoxId == targetBoxId)
        {
            await UpdateItemGridPositionAsync(itemId, gridColumn, gridRow, cancellationToken);
            return;
        }

        var targetSortOrder = await _repository.GetNextItemSortOrderAsync(targetBoxId, cancellationToken);
        var sourcePath = item.SourcePath;
        var storedPath = item.StoredPath;
        var displayName = item.DisplayName;
        var isDirectory = item.ItemKind == ItemKind.Directory;

        if (targetBox.Type == BoxType.Mapping)
        {
            if (!string.IsNullOrWhiteSpace(item.StoredPath))
            {
                throw new InvalidOperationException("Stored items cannot be moved into a mapping box.");
            }

            storedPath = null;
        }
        else
        {
            if (sourceBox.Type == BoxType.Mapping)
            {
                throw new InvalidOperationException("Mapping references cannot be moved into a storage box.");
            }

            var sourceFilePath = item.EffectivePath;
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new InvalidOperationException("Item has no file path.");
            }

            var fullSourcePath = PathSafety.GetFullExistingPath(sourceFilePath);
            if (!string.IsNullOrWhiteSpace(item.StoredPath))
            {
                EnsureStoredItemPathBelongsToBox(sourceBox, fullSourcePath);
            }

            var storageRoot = GetStorageRoot(targetBox, createIfMissing: targetBox.Type != BoxType.Bound);
            var targetPath = FileNameService.GetUniqueDestinationPath(storageRoot, displayName, isDirectory);
            PathSafety.EnsureChildPath(storageRoot, targetPath);

            await SafeFileOps.MoveAsync(fullSourcePath, targetPath, isDirectory, cancellationToken);

            displayName = Path.GetFileName(targetPath);
            storedPath = targetPath;

            try
            {
                await _repository.MoveItemToBoxAsync(
                    item,
                    targetBox.Id,
                    displayName,
                    sourcePath,
                    storedPath,
                    targetSortOrder,
                    gridColumn,
                    gridRow,
                    CancellationToken.None);
            }
            catch
            {
                await TryCompensateMoveAsync(targetPath, fullSourcePath, isDirectory);
                throw;
            }

            return;
        }

        await _repository.MoveItemToBoxAsync(
            item,
            targetBox.Id,
            displayName,
            sourcePath,
            storedPath,
            targetSortOrder,
            gridColumn,
            gridRow,
            cancellationToken);
    }

    public async Task<string> ExportItemToDirectoryAsync(
        Guid itemId,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            throw new InvalidOperationException("Only stored items can be exported.");
        }

        var sourceBox = await _repository.GetBoxAsync(item.BoxId, cancellationToken)
            ?? throw new InvalidOperationException("Source box does not exist.");
        using var boundSyncGate = await AcquireBoundSyncGateAsync(
            sourceBox.Type == BoxType.Bound,
            cancellationToken);
        var sourcePath = PathSafety.GetFullExistingPath(item.StoredPath);
        EnsureStoredItemPathBelongsToBox(sourceBox, sourcePath);

        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        if (sourceBox.Type == BoxType.Bound
            && IsSamePath(fullTargetDirectory, sourceBox.StoragePath))
        {
            fullTargetDirectory = GetBoundRestoreDirectory(sourceBox);
        }
        Directory.CreateDirectory(fullTargetDirectory);

        var displayName = string.IsNullOrWhiteSpace(item.DisplayName)
            ? Path.GetFileName(sourcePath)
            : item.DisplayName;
        var isDirectory = item.ItemKind == ItemKind.Directory;
        var targetPath = FileNameService.GetUniqueDestinationPath(fullTargetDirectory, displayName, isDirectory);
        PathSafety.EnsureChildPath(fullTargetDirectory, targetPath);

        cancellationToken.ThrowIfCancellationRequested();
        await SafeFileOps.MoveAsync(sourcePath, targetPath, isDirectory, cancellationToken);

        try
        {
            await _repository.RemoveItemAsync(itemId, CancellationToken.None);
        }
        catch
        {
            await TryCompensateMoveAsync(targetPath, sourcePath, isDirectory);
            throw;
        }

        return targetPath;
    }

    public async Task<ItemDeleteResult> DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
            return ItemDeleteResult.ReferenceRemoved(item.Id, item.DisplayName);
        }

        var sourceBox = await _repository.GetBoxAsync(item.BoxId, cancellationToken)
            ?? throw new InvalidOperationException("Source box does not exist.");
        using var boundSyncGate = await AcquireBoundSyncGateAsync(
            sourceBox.Type == BoxType.Bound,
            cancellationToken);
        var restore = await RestoreStoredItemAsync(item, sourceBox, reservedTargets: null, cancellationToken);
        try
        {
            await _repository.RemoveItemAsync(itemId, CancellationToken.None);
        }
        catch
        {
            // Best effort: try to put the file back into box storage if the DB write failed.
            if (!string.IsNullOrWhiteSpace(item.StoredPath) && !string.IsNullOrWhiteSpace(restore.RestoredPath))
            {
                var isDirectory = item.ItemKind == ItemKind.Directory;
                await TryCompensateMoveAsync(restore.RestoredPath, item.StoredPath, isDirectory);
            }

            throw;
        }

        return restore;
    }

    public async Task<BoxDeleteResult> DeleteBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        var containingProjectFolderId = box.Type == BoxType.Project
            ? await _repository.GetProjectFolderForProjectAsync(boxId, cancellationToken)
            : null;

        if (box.Type is BoxType.Mapping or BoxType.Todo or BoxType.Note or BoxType.Project
            or BoxType.ProjectFolder or BoxType.Bound)
        {
            await _repository.RemoveBoxAsync(boxId, cancellationToken);
            if (containingProjectFolderId is Guid folderId)
            {
                await new ProjectFolderService(_repository).DissolveIfSparseAsync(
                    folderId,
                    cancellationToken);
            }
            return new BoxDeleteResult(
                box.Id,
                box.Name,
                box.Type,
                BoxRemoved: true,
                RestoredCount: 0,
                FailedCount: 0,
                Failures: Array.Empty<string>());
        }

        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        var reservedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var restoredCount = 0;
        var failures = new List<string>();

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.StoredPath))
            {
                await _repository.RemoveItemAsync(item.Id, cancellationToken);
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RestoreStoredItemAsync(item, box, reservedTargets, cancellationToken);
                await _repository.RemoveItemAsync(item.Id, CancellationToken.None);
                restoredCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add($"{item.DisplayName}: {exception.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return new BoxDeleteResult(
                box.Id,
                box.Name,
                box.Type,
                BoxRemoved: false,
                RestoredCount: restoredCount,
                FailedCount: failures.Count,
                Failures: failures);
        }

        await _repository.RemoveBoxAsync(boxId, cancellationToken);
        TryDeleteBoxStorageDirectory(box);

        return new BoxDeleteResult(
            box.Id,
            box.Name,
            box.Type,
            BoxRemoved: true,
            RestoredCount: restoredCount,
            FailedCount: 0,
            Failures: Array.Empty<string>());
    }

    private async Task<ItemDeleteResult> RestoreStoredItemAsync(
        DrawerItem item,
        Box sourceBox,
        HashSet<string>? reservedTargets,
        CancellationToken cancellationToken)
    {
        var plan = CreateRestorePlan(item, sourceBox, reservedTargets);
        await SafeFileOps.MoveAsync(plan.SourcePath, plan.TargetPath, plan.IsDirectory, cancellationToken);

        return new ItemDeleteResult(
            item.Id,
            item.DisplayName,
            WasStoredItem: true,
            RestoredPath: plan.TargetPath,
            RestoredToOriginal: plan.RestoredToOriginal,
            RestoredToDesktop: plan.RestoredToDesktop);
    }

    private RestorePlan CreateRestorePlan(
        DrawerItem item,
        Box sourceBox,
        HashSet<string>? reservedTargets)
    {
        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            throw new InvalidOperationException("Mapping items do not have stored files to restore.");
        }

        var storedPath = PathSafety.GetFullExistingPath(item.StoredPath);
        EnsureStoredItemPathBelongsToBox(sourceBox, storedPath);

        var isDirectory = Directory.Exists(storedPath);
        var originalName = ResolveRestoreFileName(item, storedPath);

        if (TryGetExistingOriginalDirectory(item.SourcePath, out var originalDirectory)
            && !(sourceBox.Type == BoxType.Bound
                && IsSamePath(originalDirectory, sourceBox.StoragePath)))
        {
            var targetPath = GetReservedUniqueDestinationPath(originalDirectory, originalName, isDirectory, reservedTargets);
            PathSafety.EnsureChildPath(originalDirectory, targetPath);
            return new RestorePlan(storedPath, targetPath, isDirectory, RestoredToOriginal: true, RestoredToDesktop: false);
        }

        var desktopDirectory = sourceBox.Type == BoxType.Bound
            ? GetBoundRestoreDirectory(sourceBox)
            : GetDesktopDirectory();
        Directory.CreateDirectory(desktopDirectory);
        var desktopTarget = GetReservedUniqueDestinationPath(desktopDirectory, originalName, isDirectory, reservedTargets);
        PathSafety.EnsureChildPath(desktopDirectory, desktopTarget);
        return new RestorePlan(storedPath, desktopTarget, isDirectory, RestoredToOriginal: false, RestoredToDesktop: true);
    }

    private static string ResolveRestoreFileName(DrawerItem item, string storedPath)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            try
            {
                var originalPath = Path.GetFullPath(item.SourcePath);
                var fromSource = Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(fromSource))
                {
                    return fromSource;
                }
            }
            catch
            {
                // Fall through to display name / stored path.
            }
        }

        if (!string.IsNullOrWhiteSpace(item.DisplayName))
        {
            return item.DisplayName;
        }

        var fromStored = Path.GetFileName(storedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fromStored))
        {
            throw new InvalidOperationException("Item does not contain a file name to restore.");
        }

        return fromStored;
    }

    private static bool TryGetExistingOriginalDirectory(string? sourcePath, out string directory)
    {
        directory = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        try
        {
            var originalPath = Path.GetFullPath(sourcePath);
            var originalDirectory = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrWhiteSpace(originalDirectory) || !Directory.Exists(originalDirectory))
            {
                return false;
            }

            directory = originalDirectory;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDesktopDirectory()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            throw new InvalidOperationException("Desktop directory is not available for restore fallback.");
        }

        return Path.GetFullPath(desktopPath);
    }

    private string GetBoundRestoreDirectory(Box sourceBox)
    {
        var desktopDirectory = GetDesktopDirectory();
        if (sourceBox.Type != BoxType.Bound
            || !IsSamePath(desktopDirectory, sourceBox.StoragePath))
        {
            return desktopDirectory;
        }

        var recoveryDirectory = Path.Combine(_paths.RootDirectory, "RestoredFromBound");
        if (IsSamePath(recoveryDirectory, sourceBox.StoragePath)
            || IsSameOrDescendantPath(recoveryDirectory, sourceBox.StoragePath!))
        {
            recoveryDirectory = Path.Combine(Path.GetTempPath(), "PODO-RestoredFromBound");
        }

        return Path.GetFullPath(recoveryDirectory);
    }

    private async Task SynchronizeAllBoundBoxesAsync(CancellationToken cancellationToken)
    {
        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        foreach (var box in boxes.Where(box => box.Type == BoxType.Bound))
        {
            await SynchronizeBoundBoxAsync(box, cancellationToken);
        }
    }

    private async Task SynchronizeBoundBoxAsync(
        Box box,
        CancellationToken cancellationToken)
    {
        if (box.Type != BoxType.Bound || string.IsNullOrWhiteSpace(box.StoragePath))
        {
            return;
        }

        await _boundSyncGate.WaitAsync(cancellationToken);
        try
        {
            string storageRoot;
            try
            {
                storageRoot = GetStorageRoot(box, createIfMissing: false);
                if (!Directory.Exists(storageRoot))
                {
                    return;
                }
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            FileSystemEntry[] entries;
            try
            {
                entries = await Task.Run(
                    () => Directory
                        .EnumerateFileSystemEntries(storageRoot, "*", SearchOption.TopDirectoryOnly)
                        .Select(path => CreateBoundFileSystemEntry(path))
                        .Where(entry => entry is not null)
                        .Select(entry => entry!)
                        .ToArray(),
                    cancellationToken);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            var currentItems = await _repository.GetItemsAsync(box.Id, cancellationToken);
            var actualByPath = entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
            var existingByPath = currentItems
                .Where(item => !string.IsNullOrWhiteSpace(item.StoredPath))
                .GroupBy(item => Path.GetFullPath(item.StoredPath!), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var item in currentItems)
            {
                if (string.IsNullOrWhiteSpace(item.StoredPath)
                    || !actualByPath.ContainsKey(Path.GetFullPath(item.StoredPath)))
                {
                    await _repository.RemoveItemAsync(item.Id, cancellationToken);
                }
            }

            var nextSortOrder = currentItems
                .Select(item => item.SortOrder)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in entries)
            {
                if (existingByPath.ContainsKey(entry.Path))
                {
                    continue;
                }

                var item = new DrawerItem(
                    Guid.NewGuid(),
                    box.Id,
                    entry.DisplayName,
                    entry.IsDirectory ? ItemKind.Directory : ItemKind.File,
                    entry.Path,
                    entry.Path,
                    nextSortOrder++,
                    now,
                    now);
                await _repository.AddItemAsync(item, cancellationToken);
            }
        }
        finally
        {
            _boundSyncGate.Release();
        }
    }

    private static FileSystemEntry? CreateBoundFileSystemEntry(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            var fullPath = Path.GetFullPath(path);
            return new FileSystemEntry(
                fullPath,
                Path.GetFileName(fullPath),
                (attributes & FileAttributes.Directory) != 0);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string GetStorageRoot(Box box, bool createIfMissing)
    {
        var storageRoot = box.StoragePath
            ?? Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
        storageRoot = Path.GetFullPath(storageRoot);

        if (box.Type == BoxType.Bound)
        {
            if (!Directory.Exists(storageRoot))
            {
                throw new DirectoryNotFoundException($"绑定文件夹不可用：{storageRoot}");
            }

            PathSafety.EnsureNoReparsePoints(storageRoot);
            return storageRoot;
        }

        if (createIfMissing)
        {
            Directory.CreateDirectory(storageRoot);
        }

        return storageRoot;
    }

    private void EnsureStoredItemPathBelongsToBox(Box box, string storedPath)
    {
        if (box.Type == BoxType.Bound)
        {
            PathSafety.EnsureChildPath(GetStorageRoot(box, createIfMissing: false), storedPath);
            return;
        }

        PathSafety.EnsureChildPath(_paths.BoxesDirectory, storedPath);
    }

    private static string GetExistingDirectoryPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
        }

        var fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"绑定文件夹不存在：{fullPath}");
        }

        return fullPath;
    }

    private static bool IsDirectChildPath(string rootDirectory, string candidatePath)
    {
        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !IsSamePath(root, candidate)
            && string.Equals(Path.GetDirectoryName(candidate), root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ArePathsOverlapping(string? first, string? second)
    {
        return !string.IsNullOrWhiteSpace(first)
            && !string.IsNullOrWhiteSpace(second)
            && (IsSameOrDescendantPath(first, second) || IsSameOrDescendantPath(second, first));
    }

    private static bool IsSameOrDescendantPath(string candidatePath, string ancestorPath)
    {
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var ancestor = Path.GetFullPath(ancestorPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return IsSamePath(candidate, ancestor)
            || candidate.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? first, string? second)
    {
        return !string.IsNullOrWhiteSpace(first)
            && !string.IsNullOrWhiteSpace(second)
            && IsSamePath(first, second);
    }

    private static bool IsSamePath(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetReservedUniqueDestinationPath(
        string directory,
        string fileName,
        bool isDirectory,
        HashSet<string>? reservedTargets)
    {
        var targetPath = FileNameService.GetUniqueDestinationPath(directory, fileName, isDirectory);
        if (reservedTargets is null)
        {
            return targetPath;
        }

        var normalizedTargetPath = Path.GetFullPath(targetPath);
        if (reservedTargets.Add(normalizedTargetPath))
        {
            return targetPath;
        }

        var nameWithoutExtension = isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName);
        var extension = isDirectory ? string.Empty : Path.GetExtension(fileName);
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{nameWithoutExtension} ({index}){extension}");
            var normalizedCandidate = Path.GetFullPath(candidate);
            if ((File.Exists(candidate) || Directory.Exists(candidate))
                || !reservedTargets.Add(normalizedCandidate))
            {
                continue;
            }

            return candidate;
        }

        throw new IOException($"Could not find a unique destination for {fileName}.");
    }

    private void TryDeleteBoxStorageDirectory(Box box)
    {
        try
        {
            var storagePath = box.StoragePath;
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                storagePath = Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            }

            var fullStoragePath = Path.GetFullPath(storagePath);
            PathSafety.EnsureChildPath(_paths.BoxesDirectory, fullStoragePath);

            if (Directory.Exists(fullStoragePath)
                && Directory.GetFileSystemEntries(fullStoragePath).Length == 0)
            {
                Directory.Delete(fullStoragePath, recursive: false);
            }
        }
        catch
        {
            // Storage cleanup is best-effort.
        }
    }

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        return _repository.GetSettingAsync(key, cancellationToken);
    }

    public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        return _repository.SetSettingAsync(key, value, cancellationToken);
    }

    public async Task RenameBoxAsync(Guid boxId, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Box name cannot be empty.", nameof(newName));
        }

        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        await _repository.UpdateBoxNameAsync(boxId, newName.Trim(), cancellationToken);
    }

    public async Task OpenItemAsync(Guid itemId, IFileLauncher launcher, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        var path = item.EffectivePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Item has no file path.");
        }

        await launcher.OpenAsync(path, cancellationToken);
    }

    private async Task PruneMissingStoredItemsAsync(Guid? boxId, CancellationToken cancellationToken)
    {
        // 存储根不可达（可移动盘/网络盘暂时掉线）时绝不能清理：
        // 文件仍然存在只是暂时不可见，把"看不到"当成"已删除"会永久销毁记录与恢复信息，
        // 驱动器重新挂载后文件就变成无人知晓的孤儿。
        if (!Directory.Exists(_paths.BoxesDirectory))
        {
            return;
        }

        var boundBoxIds = (await _repository.GetAllBoxesAsync(cancellationToken))
            .Where(box => box.Type == BoxType.Bound)
            .Select(box => box.Id)
            .ToHashSet();
        if (boxId is Guid requestedBoxId && boundBoxIds.Contains(requestedBoxId))
        {
            return;
        }

        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        var missingItemIds = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return items
                .Where(item => !boundBoxIds.Contains(item.BoxId))
                .Where(IsMissingStoredItem)
                .Select(item => item.Id)
                .ToArray();
        }, cancellationToken);

        foreach (var itemId in missingItemIds)
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
        }
    }

    private static bool IsMissingStoredItem(DrawerItem item)
    {
        return !string.IsNullOrWhiteSpace(item.StoredPath)
            && !File.Exists(item.StoredPath)
            && !Directory.Exists(item.StoredPath);
    }

    private async Task RepairStoredPathsAsync(CancellationToken cancellationToken)
    {
        var boxes = await _repository.GetAllBoxesAsync(cancellationToken);
        foreach (var box in boxes.Where(box => box.Type is BoxType.Normal or BoxType.Pixel or BoxType.Drawer))
        {
            var expectedStoragePath = Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            if (!Directory.Exists(expectedStoragePath))
            {
                continue;
            }

            if (!string.Equals(
                    Path.GetFullPath(box.StoragePath ?? expectedStoragePath),
                    Path.GetFullPath(expectedStoragePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                await _repository.UpdateBoxStoragePathAsync(
                    box.Id,
                    expectedStoragePath,
                    cancellationToken);
            }

            var items = await _repository.GetItemsAsync(box.Id, cancellationToken);
            foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.StoredPath)))
            {
                var name = Path.GetFileName(item.StoredPath);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var expectedStoredPath = Path.Combine(expectedStoragePath, name);
                if ((!File.Exists(expectedStoredPath) && !Directory.Exists(expectedStoredPath))
                    || string.Equals(
                        Path.GetFullPath(item.StoredPath!),
                        Path.GetFullPath(expectedStoredPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await _repository.UpdateItemStoredPathAsync(
                    item.Id,
                    expectedStoredPath,
                    cancellationToken);
            }
        }
    }

    private async Task EnsureDefaultBoxesAsync(CancellationToken cancellationToken)
    {
        var boxes = await _repository.GetAllBoxesAsync(cancellationToken);
        if (boxes.Count > 0)
        {
            return;
        }

        await CreateBoxAsync("普通收纳盒", BoxType.Normal, cancellationToken);
        await CreateBoxAsync("映射收纳盒", BoxType.Mapping, cancellationToken);
    }

    private static async Task TryCompensateMoveAsync(
        string movedPath,
        string originalPath,
        bool isDirectory)
    {
        try
        {
            if ((isDirectory && Directory.Exists(movedPath)) || (!isDirectory && File.Exists(movedPath)))
            {
                await SafeFileOps.MoveAsync(movedPath, originalPath, isDirectory, CancellationToken.None);
            }
        }
        catch
        {
            // Best-effort compensation only; the original failure is rethrown by the caller.
        }
    }

    private sealed record RestorePlan(
        string SourcePath,
        string TargetPath,
        bool IsDirectory,
        bool RestoredToOriginal,
        bool RestoredToDesktop);

    private sealed record FileSystemEntry(
        string Path,
        string DisplayName,
        bool IsDirectory);
}
