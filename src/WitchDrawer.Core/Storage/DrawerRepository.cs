using Microsoft.Data.Sqlite;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Storage;

public sealed class DrawerRepository
{
    private readonly string _databasePath;

    public DrawerRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var databaseDirectory = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectory))
        {
            throw new InvalidOperationException("数据库路径无效: " + _databasePath);
        }

        try
        {
            Directory.CreateDirectory(databaseDirectory);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            // journal_mode 需要在同目录创建旁路文件；单独执行便于定位 Error 14。
            await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS Boxes (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    StoragePath TEXT NULL,
                    SortOrder INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    IsArchived INTEGER NOT NULL DEFAULT 0,
                    ArchivedAt TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS Items (
                    Id TEXT PRIMARY KEY,
                    BoxId TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    ItemKind INTEGER NOT NULL,
                    SourcePath TEXT NULL,
                    StoredPath TEXT NULL,
                    SortOrder INTEGER NOT NULL,
                    GridColumn INTEGER NULL,
                    GridRow INTEGER NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY(BoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Todos (
                    Id TEXT PRIMARY KEY,
                    BoxId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    IsCompleted INTEGER NOT NULL,
                    IsArchived INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    CompletedAt TEXT NULL,
                    ArchivedAt TEXT NULL,
                    FOREIGN KEY(BoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS Notes (
                    BoxId TEXT PRIMARY KEY,
                    Content TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY(BoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS Projects (
                    BoxId TEXT PRIMARY KEY,
                    Stage INTEGER NOT NULL,
                    OwnerName TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    PlannedStartAt TEXT NULL,
                    PlannedLaunchAt TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY(BoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ProjectIssues (
                    Id TEXT PRIMARY KEY,
                    ProjectBoxId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    SolutionState INTEGER NOT NULL,
                    SolutionText TEXT NOT NULL,
                    ResolutionState INTEGER NOT NULL,
                    PreviousResolutionState INTEGER NULL,
                    Priority INTEGER NOT NULL,
                    AssigneeName TEXT NOT NULL,
                    DueAt TEXT NULL,
                    ResolvedAt TEXT NULL,
                    ResolvedBy TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY(ProjectBoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ProjectBoxLinks (
                    ProjectBoxId TEXT NOT NULL,
                    LinkedBoxId TEXT NOT NULL,
                    IsVisible INTEGER NOT NULL DEFAULT 1,
                    AttachmentSide INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY(ProjectBoxId, LinkedBoxId),
                    FOREIGN KEY(ProjectBoxId) REFERENCES Boxes(Id) ON DELETE CASCADE,
                    FOREIGN KEY(LinkedBoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ProjectPaperLinks (
                    ProjectBoxId TEXT NOT NULL,
                    PaperId TEXT NOT NULL,
                    AttachmentSide INTEGER NOT NULL DEFAULT 0,
                    IsVisible INTEGER NOT NULL DEFAULT 1,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY(ProjectBoxId, PaperId),
                    FOREIGN KEY(ProjectBoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ProjectFolderMembers (
                    FolderBoxId TEXT NOT NULL,
                    ProjectBoxId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY(FolderBoxId, ProjectBoxId),
                    FOREIGN KEY(FolderBoxId) REFERENCES Boxes(Id) ON DELETE CASCADE,
                    FOREIGN KEY(ProjectBoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_Items_BoxId ON Items(BoxId);
                CREATE INDEX IF NOT EXISTS IX_Items_DisplayName ON Items(DisplayName);
                CREATE INDEX IF NOT EXISTS IX_ProjectIssues_BoxStateSort
                    ON ProjectIssues(ProjectBoxId, ResolutionState, SortOrder);
                CREATE INDEX IF NOT EXISTS IX_ProjectBoxLinks_ProjectBoxSort
                    ON ProjectBoxLinks(ProjectBoxId, SortOrder);
                CREATE INDEX IF NOT EXISTS IX_ProjectPaperLinks_ProjectBoxSort
                    ON ProjectPaperLinks(ProjectBoxId, SortOrder);
                CREATE INDEX IF NOT EXISTS IX_ProjectFolderMembers_FolderSort
                    ON ProjectFolderMembers(FolderBoxId, SortOrder);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ProjectFolderMembers_Project
                    ON ProjectFolderMembers(ProjectBoxId);
                """,
                cancellationToken);

            await EnsureColumnAsync(connection, "Items", "GridColumn", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Items", "GridRow", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Boxes", "IsArchived", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "Boxes", "ArchivedAt", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Todos", "BoxId", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "Todos", "IsArchived", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "Todos", "ArchivedAt", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(
                connection,
                "ProjectBoxLinks",
                "AttachmentSide",
                "INTEGER NOT NULL DEFAULT 0",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                """
                DELETE FROM ProjectBoxLinks
                WHERE rowid NOT IN (
                    SELECT MIN(rowid)
                    FROM ProjectBoxLinks
                    GROUP BY LinkedBoxId
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ProjectBoxLinks_LinkedBoxId
                    ON ProjectBoxLinks(LinkedBoxId);
                """,
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                """
                DELETE FROM ProjectPaperLinks
                WHERE rowid NOT IN (
                    SELECT MIN(rowid)
                    FROM ProjectPaperLinks
                    GROUP BY PaperId
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ProjectPaperLinks_PaperId
                    ON ProjectPaperLinks(PaperId);
                """,
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_Todos_BoxStateSort ON Todos(BoxId, IsCompleted, SortOrder);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_Todos_BoxArchiveStateSort ON Todos(BoxId, IsArchived, IsCompleted, SortOrder);",
                cancellationToken);
        }
        catch (Exception exception) when (IsDatabaseAccessFailure(exception))
        {
            throw CreateDatabaseAccessException(databaseDirectory, exception);
        }
    }

    /// <summary>
    /// 将 WAL 日志完整回写主数据库文件并截断旁路文件。
    /// 数据目录迁移前调用，保证 witchdrawer.db 单文件即为完整数据。
    /// </summary>
    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
    }

    public async Task<IReadOnlyList<Box>> GetBoxesAsync(CancellationToken cancellationToken = default)
    {
        return await QueryBoxesAsync(
            """
            SELECT Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt, IsArchived, ArchivedAt
            FROM Boxes
            WHERE IsArchived = 0
            ORDER BY SortOrder, Name;
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Box>> GetArchivedBoxesAsync(CancellationToken cancellationToken = default)
    {
        return await QueryBoxesAsync(
            """
            SELECT Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt, IsArchived, ArchivedAt
            FROM Boxes
            WHERE IsArchived = 1
            ORDER BY ArchivedAt DESC, SortOrder, Name;
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Box>> GetAllBoxesAsync(CancellationToken cancellationToken = default)
    {
        return await QueryBoxesAsync(
            """
            SELECT Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt, IsArchived, ArchivedAt
            FROM Boxes
            ORDER BY IsArchived, SortOrder, Name;
            """,
            cancellationToken);
    }

    private async Task<IReadOnlyList<Box>> QueryBoxesAsync(
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = commandText;

        var boxes = new List<Box>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            boxes.Add(ReadBox(reader));
        }

        return boxes;
    }

    public async Task<Box?> GetBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt, IsArchived, ArchivedAt
            FROM Boxes
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", boxId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBox(reader) : null;
    }

    public async Task AddBoxAsync(Box box, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Boxes (
                Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt, IsArchived, ArchivedAt)
            VALUES (
                $id, $name, $type, $storagePath, $sortOrder, $createdAt, $updatedAt, $isArchived, $archivedAt);
            """;
        command.Parameters.AddWithValue("$id", box.Id.ToString());
        command.Parameters.AddWithValue("$name", box.Name);
        command.Parameters.AddWithValue("$type", (int)box.Type);
        command.Parameters.AddWithValue("$storagePath", (object?)box.StoragePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", box.SortOrder);
        command.Parameters.AddWithValue("$createdAt", ToDb(box.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(box.UpdatedAt));
        command.Parameters.AddWithValue("$isArchived", box.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue(
            "$archivedAt",
            box.ArchivedAt is null ? DBNull.Value : ToDb(box.ArchivedAt.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateBoxNameAsync(Guid boxId, string newName, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Boxes
            SET Name = $name, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", boxId.ToString());
        command.Parameters.AddWithValue("$name", newName);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Box does not exist.");
        }
    }

    public async Task UpdateBoxStoragePathAsync(
        Guid boxId,
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Boxes
            SET StoragePath = $storagePath, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", boxId.ToString());
        command.Parameters.AddWithValue("$storagePath", storagePath);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Box does not exist.");
        }
    }

    public async Task UpdateBoxSortOrdersAsync(
        IReadOnlyList<Guid> orderedBoxIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE Boxes
            SET SortOrder = $sortOrder, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        var idParameter = command.Parameters.Add("$id", SqliteType.Text);
        var sortOrderParameter = command.Parameters.Add("$sortOrder", SqliteType.Integer);
        var updatedAtParameter = command.Parameters.Add("$updatedAt", SqliteType.Text);
        var updatedAt = ToDb(DateTimeOffset.UtcNow);

        for (var index = 0; index < orderedBoxIds.Count; index++)
        {
            idParameter.Value = orderedBoxIds[index].ToString();
            sortOrderParameter.Value = index;
            updatedAtParameter.Value = updatedAt;

            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Cannot reorder a box that does not exist.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateBoxArchiveStatesAsync(
        IReadOnlyCollection<Guid> boxIds,
        bool isArchived,
        DateTimeOffset? archivedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boxIds);

        var distinctBoxIds = boxIds
            .Where(boxId => boxId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (distinctBoxIds.Length == 0)
        {
            return;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE Boxes
            SET IsArchived = $isArchived,
                ArchivedAt = $archivedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        var idParameter = command.Parameters.Add("$id", SqliteType.Text);
        var isArchivedParameter = command.Parameters.Add("$isArchived", SqliteType.Integer);
        var archivedAtParameter = command.Parameters.Add("$archivedAt", SqliteType.Text);
        var updatedAtParameter = command.Parameters.Add("$updatedAt", SqliteType.Text);
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? effectiveArchivedAt = isArchived ? archivedAt ?? now : null;

        foreach (var boxId in distinctBoxIds)
        {
            idParameter.Value = boxId.ToString();
            isArchivedParameter.Value = isArchived ? 1 : 0;
            archivedAtParameter.Value = effectiveArchivedAt is null
                ? DBNull.Value
                : ToDb(effectiveArchivedAt.Value);
            updatedAtParameter.Value = ToDb(now);

            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Cannot update archive state for a box that does not exist.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var removeTodosCommand = connection.CreateCommand();
        removeTodosCommand.Transaction = (SqliteTransaction)transaction;
        removeTodosCommand.CommandText = "DELETE FROM Todos WHERE BoxId = $id;";
        removeTodosCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeTodosCommand.ExecuteNonQueryAsync(cancellationToken);

        var removeNoteCommand = connection.CreateCommand();
        removeNoteCommand.Transaction = (SqliteTransaction)transaction;
        removeNoteCommand.CommandText = "DELETE FROM Notes WHERE BoxId = $id;";
        removeNoteCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeNoteCommand.ExecuteNonQueryAsync(cancellationToken);

        var removeProjectIssuesCommand = connection.CreateCommand();
        removeProjectIssuesCommand.Transaction = (SqliteTransaction)transaction;
        removeProjectIssuesCommand.CommandText = "DELETE FROM ProjectIssues WHERE ProjectBoxId = $id;";
        removeProjectIssuesCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeProjectIssuesCommand.ExecuteNonQueryAsync(cancellationToken);

        var removeProjectCommand = connection.CreateCommand();
        removeProjectCommand.Transaction = (SqliteTransaction)transaction;
        removeProjectCommand.CommandText = "DELETE FROM Projects WHERE BoxId = $id;";
        removeProjectCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeProjectCommand.ExecuteNonQueryAsync(cancellationToken);

        var removeProjectLinksCommand = connection.CreateCommand();
        removeProjectLinksCommand.Transaction = (SqliteTransaction)transaction;
        removeProjectLinksCommand.CommandText =
            "DELETE FROM ProjectBoxLinks WHERE ProjectBoxId = $id OR LinkedBoxId = $id;";
        removeProjectLinksCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeProjectLinksCommand.ExecuteNonQueryAsync(cancellationToken);

        var removeProjectPaperLinksCommand = connection.CreateCommand();
        removeProjectPaperLinksCommand.Transaction = (SqliteTransaction)transaction;
        removeProjectPaperLinksCommand.CommandText =
            "DELETE FROM ProjectPaperLinks WHERE ProjectBoxId = $id;";
        removeProjectPaperLinksCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeProjectPaperLinksCommand.ExecuteNonQueryAsync(cancellationToken);

        var removeProjectFolderMembersCommand = connection.CreateCommand();
        removeProjectFolderMembersCommand.Transaction = (SqliteTransaction)transaction;
        removeProjectFolderMembersCommand.CommandText =
            "DELETE FROM ProjectFolderMembers WHERE FolderBoxId = $id OR ProjectBoxId = $id;";
        removeProjectFolderMembersCommand.Parameters.AddWithValue("$id", boxId.ToString());
        await removeProjectFolderMembersCommand.ExecuteNonQueryAsync(cancellationToken);

        var removeBoxCommand = connection.CreateCommand();
        removeBoxCommand.Transaction = (SqliteTransaction)transaction;
        removeBoxCommand.CommandText = "DELETE FROM Boxes WHERE Id = $id;";
        removeBoxCommand.Parameters.AddWithValue("$id", boxId.ToString());
        if (await removeBoxCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Box does not exist.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> GetItemsAsync(Guid? boxId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        if (boxId is null)
        {
            command.CommandText =
                """
                SELECT items.Id, items.BoxId, items.DisplayName, items.ItemKind, items.SourcePath, items.StoredPath,
                       items.SortOrder, items.CreatedAt, items.UpdatedAt, items.GridColumn, items.GridRow
                FROM Items AS items
                INNER JOIN Boxes AS boxes ON boxes.Id = items.BoxId
                WHERE boxes.IsArchived = 0
                ORDER BY COALESCE(items.GridRow, 1000000), COALESCE(items.GridColumn, 1000000),
                         items.SortOrder, items.DisplayName;
                """;
        }
        else
        {
            command.CommandText =
                """
                SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
                FROM Items
                WHERE BoxId = $boxId
                ORDER BY COALESCE(GridRow, 1000000), COALESCE(GridColumn, 1000000), SortOrder, DisplayName;
                """;
            command.Parameters.AddWithValue("$boxId", boxId.Value.ToString());
        }

        var items = new List<DrawerItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    public async Task<IReadOnlyList<DrawerItem>> SearchItemsAsync(string query, int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT items.Id, items.BoxId, items.DisplayName, items.ItemKind, items.SourcePath, items.StoredPath,
                   items.SortOrder, items.CreatedAt, items.UpdatedAt, items.GridColumn, items.GridRow
            FROM Items AS items
            INNER JOIN Boxes AS boxes ON boxes.Id = items.BoxId
            WHERE boxes.IsArchived = 0
              AND ($query = '' OR items.DisplayName LIKE $like OR items.SourcePath LIKE $like OR items.StoredPath LIKE $like)
            ORDER BY COALESCE(items.GridRow, 1000000), COALESCE(items.GridColumn, 1000000),
                     items.SortOrder, items.DisplayName
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$like", $"%{query}%");
        command.Parameters.AddWithValue("$limit", limit);

        var items = new List<DrawerItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    public async Task<DrawerItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
            FROM Items
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
    }

    public async Task AddItemAsync(DrawerItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Items (Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder, GridColumn, GridRow, CreatedAt, UpdatedAt)
            VALUES ($id, $boxId, $displayName, $itemKind, $sourcePath, $storedPath, $sortOrder, $gridColumn, $gridRow, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$boxId", item.BoxId.ToString());
        command.Parameters.AddWithValue("$displayName", item.DisplayName);
        command.Parameters.AddWithValue("$itemKind", (int)item.ItemKind);
        command.Parameters.AddWithValue("$sourcePath", (object?)item.SourcePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$storedPath", (object?)item.StoredPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
        command.Parameters.AddWithValue("$gridColumn", (object?)item.GridColumn ?? DBNull.Value);
        command.Parameters.AddWithValue("$gridRow", (object?)item.GridRow ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", ToDb(item.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(item.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateItemStoredPathAsync(
        Guid itemId,
        string storedPath,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Items
            SET StoredPath = $storedPath, UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString());
        command.Parameters.AddWithValue("$storedPath", storedPath);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Item does not exist.");
        }
    }

    public async Task UpdateItemFileSystemIdentityAsync(
        Guid itemId,
        string displayName,
        string? sourcePath,
        string? storedPath,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Items
            SET DisplayName = $displayName,
                SourcePath = $sourcePath,
                StoredPath = $storedPath,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString());
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$sourcePath", (object?)sourcePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$storedPath", (object?)storedPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", ToDb(updatedAt));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Item does not exist.");
        }
    }

    public async Task UpdateItemGridPositionAsync(
        Guid itemId,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Items
            SET GridColumn = $gridColumn,
                GridRow = $gridRow,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString());
        command.Parameters.AddWithValue("$gridColumn", (object?)gridColumn ?? DBNull.Value);
        command.Parameters.AddWithValue("$gridRow", (object?)gridRow ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Item does not exist.");
        }
    }

    public async Task MoveItemToBoxAsync(
        DrawerItem item,
        Guid targetBoxId,
        string displayName,
        string? sourcePath,
        string? storedPath,
        int sortOrder,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Items
            SET BoxId = $boxId,
                DisplayName = $displayName,
                SourcePath = $sourcePath,
                StoredPath = $storedPath,
                SortOrder = $sortOrder,
                GridColumn = $gridColumn,
                GridRow = $gridRow,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$boxId", targetBoxId.ToString());
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$sourcePath", (object?)sourcePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$storedPath", (object?)storedPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$gridColumn", (object?)gridColumn ?? DBNull.Value);
        command.Parameters.AddWithValue("$gridRow", (object?)gridRow ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Item does not exist.");
        }
    }

    public async Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Items WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", itemId.ToString());

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Item does not exist.");
        }
    }

    public async Task<IReadOnlyList<TodoItem>> GetTodosAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt, CompletedAt, IsArchived, ArchivedAt
            FROM Todos
            WHERE BoxId = $boxId AND IsArchived = 0
            ORDER BY IsCompleted, SortOrder, CreatedAt;
            """;
        command.Parameters.AddWithValue("$boxId", boxId.ToString());

        var todos = new List<TodoItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            todos.Add(ReadTodo(reader));
        }

        return todos;
    }

    public async Task<IReadOnlyList<TodoItem>> GetArchivedTodosAsync(
        Guid? boxId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = boxId is null
            ? """
              SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt, CompletedAt, IsArchived, ArchivedAt
              FROM Todos
              WHERE IsArchived = 1
              ORDER BY ArchivedAt DESC, UpdatedAt DESC;
              """
            : """
              SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt, CompletedAt, IsArchived, ArchivedAt
              FROM Todos
              WHERE BoxId = $boxId AND IsArchived = 1
              ORDER BY ArchivedAt DESC, UpdatedAt DESC;
              """;
        if (boxId is not null)
        {
            command.Parameters.AddWithValue("$boxId", boxId.Value.ToString());
        }

        var todos = new List<TodoItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            todos.Add(ReadTodo(reader));
        }

        return todos;
    }

    public async Task<TodoItem?> GetTodoAsync(Guid todoId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt, CompletedAt, IsArchived, ArchivedAt
            FROM Todos
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", todoId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTodo(reader) : null;
    }

    public async Task AddTodoAsync(TodoItem todo, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Todos (
                Id, BoxId, Title, IsCompleted, IsArchived, SortOrder,
                CreatedAt, UpdatedAt, CompletedAt, ArchivedAt)
            VALUES (
                $id, $boxId, $title, $isCompleted, $isArchived, $sortOrder,
                $createdAt, $updatedAt, $completedAt, $archivedAt);
            """;
        command.Parameters.AddWithValue("$id", todo.Id.ToString());
        command.Parameters.AddWithValue("$boxId", todo.BoxId.ToString());
        command.Parameters.AddWithValue("$title", todo.Title);
        command.Parameters.AddWithValue("$isCompleted", todo.IsCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$isArchived", todo.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", todo.SortOrder);
        command.Parameters.AddWithValue("$createdAt", ToDb(todo.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(todo.UpdatedAt));
        command.Parameters.AddWithValue(
            "$completedAt",
            todo.CompletedAt is null ? DBNull.Value : ToDb(todo.CompletedAt.Value));
        command.Parameters.AddWithValue(
            "$archivedAt",
            todo.ArchivedAt is null ? DBNull.Value : ToDb(todo.ArchivedAt.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ArchiveCompletedTodosAsync(
        Guid boxId,
        DateTimeOffset archivedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Todos
            SET IsArchived = 1,
                ArchivedAt = $archivedAt,
                UpdatedAt = $archivedAt
            WHERE BoxId = $boxId
              AND IsCompleted = 1
              AND IsArchived = 0;
            """;
        command.Parameters.AddWithValue("$boxId", boxId.ToString());
        command.Parameters.AddWithValue("$archivedAt", ToDb(archivedAt));

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateTodoArchiveStateAsync(
        Guid todoId,
        bool isArchived,
        DateTimeOffset? archivedAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Todos
            SET IsArchived = $isArchived,
                ArchivedAt = $archivedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", todoId.ToString());
        command.Parameters.AddWithValue("$isArchived", isArchived ? 1 : 0);
        command.Parameters.AddWithValue(
            "$archivedAt",
            archivedAt is null ? DBNull.Value : ToDb(archivedAt.Value));
        command.Parameters.AddWithValue("$updatedAt", ToDb(updatedAt));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Todo does not exist.");
        }
    }

    public async Task UpdateTodoCompletionAsync(
        Guid todoId,
        bool isCompleted,
        DateTimeOffset? completedAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Todos
            SET IsCompleted = $isCompleted,
                CompletedAt = $completedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", todoId.ToString());
        command.Parameters.AddWithValue("$isCompleted", isCompleted ? 1 : 0);
        command.Parameters.AddWithValue(
            "$completedAt",
            completedAt is null ? DBNull.Value : ToDb(completedAt.Value));
        command.Parameters.AddWithValue("$updatedAt", ToDb(updatedAt));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Todo does not exist.");
        }
    }

    public async Task RemoveTodoAsync(Guid todoId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Todos WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", todoId.ToString());

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Todo does not exist.");
        }
    }

    public async Task<NoteDocument?> GetNoteAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT BoxId, Content, UpdatedAt FROM Notes WHERE BoxId = $boxId;";
        command.Parameters.AddWithValue("$boxId", boxId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadNote(reader) : null;
    }

    public async Task UpsertNoteAsync(
        NoteDocument note,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes (BoxId, Content, UpdatedAt)
            VALUES ($boxId, $content, $updatedAt)
            ON CONFLICT(BoxId) DO UPDATE SET
                Content = excluded.Content,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$boxId", note.BoxId.ToString());
        command.Parameters.AddWithValue("$content", note.Content);
        command.Parameters.AddWithValue("$updatedAt", ToDb(note.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProjectDetails?> GetProjectAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT BoxId, Stage, OwnerName, Description, PlannedStartAt,
                   PlannedLaunchAt, CreatedAt, UpdatedAt
            FROM Projects
            WHERE BoxId = $boxId;
            """;
        command.Parameters.AddWithValue("$boxId", boxId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProject(reader) : null;
    }

    public async Task UpsertProjectAsync(
        ProjectDetails project,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Projects (
                BoxId, Stage, OwnerName, Description, PlannedStartAt,
                PlannedLaunchAt, CreatedAt, UpdatedAt)
            VALUES (
                $boxId, $stage, $ownerName, $description, $plannedStartAt,
                $plannedLaunchAt, $createdAt, $updatedAt)
            ON CONFLICT(BoxId) DO UPDATE SET
                Stage = excluded.Stage,
                OwnerName = excluded.OwnerName,
                Description = excluded.Description,
                PlannedStartAt = excluded.PlannedStartAt,
                PlannedLaunchAt = excluded.PlannedLaunchAt,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$boxId", project.BoxId.ToString());
        command.Parameters.AddWithValue("$stage", (int)project.Stage);
        command.Parameters.AddWithValue("$ownerName", project.OwnerName);
        command.Parameters.AddWithValue("$description", project.Description);
        command.Parameters.AddWithValue(
            "$plannedStartAt",
            project.PlannedStartAt is null ? DBNull.Value : ToDb(project.PlannedStartAt.Value));
        command.Parameters.AddWithValue(
            "$plannedLaunchAt",
            project.PlannedLaunchAt is null ? DBNull.Value : ToDb(project.PlannedLaunchAt.Value));
        command.Parameters.AddWithValue("$createdAt", ToDb(project.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(project.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectBoxLink>> GetProjectBoxLinksAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT links.ProjectBoxId, links.LinkedBoxId, boxes.Name, boxes.Type,
                   links.IsVisible, links.AttachmentSide, links.SortOrder, links.CreatedAt, links.UpdatedAt
            FROM ProjectBoxLinks AS links
            INNER JOIN Boxes AS boxes ON boxes.Id = links.LinkedBoxId
            WHERE links.ProjectBoxId = $projectBoxId
            ORDER BY links.SortOrder, boxes.Name;
            """;
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());

        var links = new List<ProjectBoxLink>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            links.Add(ReadProjectBoxLink(reader));
        }

        return links;
    }

    public async Task UpsertProjectBoxLinkAsync(
        ProjectBoxLink link,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ProjectBoxLinks (
                ProjectBoxId, LinkedBoxId, IsVisible, AttachmentSide, SortOrder, CreatedAt, UpdatedAt)
            VALUES (
                $projectBoxId, $linkedBoxId, $isVisible, $attachmentSide, $sortOrder, $createdAt, $updatedAt)
            ON CONFLICT(ProjectBoxId, LinkedBoxId) DO UPDATE SET
                IsVisible = excluded.IsVisible,
                AttachmentSide = excluded.AttachmentSide,
                SortOrder = excluded.SortOrder,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$projectBoxId", link.ProjectBoxId.ToString());
        command.Parameters.AddWithValue("$linkedBoxId", link.LinkedBoxId.ToString());
        command.Parameters.AddWithValue("$isVisible", link.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$attachmentSide", (int)link.AttachmentSide);
        command.Parameters.AddWithValue("$sortOrder", link.SortOrder);
        command.Parameters.AddWithValue("$createdAt", ToDb(link.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(link.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetNextProjectBoxLinkSortOrderAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ProjectBoxLinks WHERE ProjectBoxId = $projectBoxId;";
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Reassigns one existing file-box link without a remove-then-insert gap.
    /// Direct concurrent inserts intentionally keep their existing ordering path;
    /// this transaction is only for an already-linked box being moved between projects.
    /// </summary>
    public async Task MoveProjectBoxLinkAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        ProjectAttachmentSide attachmentSide,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var ownerCommand = connection.CreateCommand();
        ownerCommand.Transaction = (SqliteTransaction)transaction;
        ownerCommand.CommandText =
            "SELECT ProjectBoxId FROM ProjectBoxLinks WHERE LinkedBoxId = $linkedBoxId LIMIT 1;";
        ownerCommand.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
        var ownerValue = await ownerCommand.ExecuteScalarAsync(cancellationToken);
        if (ownerValue is not string rawOwnerId || !Guid.TryParse(rawOwnerId, out var currentProjectBoxId))
        {
            throw new InvalidOperationException("要移动的项目文件盒关联不存在。");
        }

        var now = ToDb(DateTimeOffset.UtcNow);
        var normalizedSide = ProjectAttachmentSideCatalog.Normalize(attachmentSide);
        if (currentProjectBoxId == projectBoxId)
        {
            var updateSideCommand = connection.CreateCommand();
            updateSideCommand.Transaction = (SqliteTransaction)transaction;
            updateSideCommand.CommandText =
                """
                UPDATE ProjectBoxLinks
                SET AttachmentSide = $attachmentSide,
                    UpdatedAt = $updatedAt
                WHERE ProjectBoxId = $projectBoxId AND LinkedBoxId = $linkedBoxId;
                """;
            updateSideCommand.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
            updateSideCommand.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
            updateSideCommand.Parameters.AddWithValue("$attachmentSide", (int)normalizedSide);
            updateSideCommand.Parameters.AddWithValue("$updatedAt", now);
            if (await updateSideCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("项目文件盒关联不存在。");
            }
        }
        else
        {
            var nextSortOrderCommand = connection.CreateCommand();
            nextSortOrderCommand.Transaction = (SqliteTransaction)transaction;
            nextSortOrderCommand.CommandText =
                "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ProjectBoxLinks WHERE ProjectBoxId = $projectBoxId;";
            nextSortOrderCommand.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
            var sortOrder = Convert.ToInt32(
                await nextSortOrderCommand.ExecuteScalarAsync(cancellationToken));

            var moveCommand = connection.CreateCommand();
            moveCommand.Transaction = (SqliteTransaction)transaction;
            moveCommand.CommandText =
                """
                UPDATE ProjectBoxLinks
                SET ProjectBoxId = $projectBoxId,
                    IsVisible = 1,
                    AttachmentSide = $attachmentSide,
                    SortOrder = $sortOrder,
                    CreatedAt = $createdAt,
                    UpdatedAt = $updatedAt
                WHERE ProjectBoxId = $currentProjectBoxId AND LinkedBoxId = $linkedBoxId;
                """;
            moveCommand.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
            moveCommand.Parameters.AddWithValue("$currentProjectBoxId", currentProjectBoxId.ToString());
            moveCommand.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
            moveCommand.Parameters.AddWithValue("$attachmentSide", (int)normalizedSide);
            moveCommand.Parameters.AddWithValue("$sortOrder", sortOrder);
            moveCommand.Parameters.AddWithValue("$createdAt", now);
            moveCommand.Parameters.AddWithValue("$updatedAt", now);
            if (await moveCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("项目文件盒关联不存在。");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid?> GetProjectBoxForLinkedBoxAsync(
        Guid linkedBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ProjectBoxId FROM ProjectBoxLinks WHERE LinkedBoxId = $linkedBoxId LIMIT 1;";
        command.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string raw && Guid.TryParse(raw, out var projectBoxId)
            ? projectBoxId
            : null;
    }

    public async Task<Guid?> GetProjectBoxForLinkedPaperAsync(
        string paperId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ProjectBoxId FROM ProjectPaperLinks WHERE PaperId = $paperId LIMIT 1;";
        command.Parameters.AddWithValue("$paperId", paperId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string raw && Guid.TryParse(raw, out var projectBoxId)
            ? projectBoxId
            : null;
    }

    public async Task UpdateProjectBoxLinkVisibilityAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        bool isVisible,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ProjectBoxLinks
            SET IsVisible = $isVisible, UpdatedAt = $updatedAt
            WHERE ProjectBoxId = $projectBoxId AND LinkedBoxId = $linkedBoxId;
            """;
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        command.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
        command.Parameters.AddWithValue("$isVisible", isVisible ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("项目文件盒关联不存在。");
        }
    }

    public async Task UpdateProjectBoxLinkAttachmentSideAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        ProjectAttachmentSide attachmentSide,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ProjectBoxLinks
            SET AttachmentSide = $attachmentSide, UpdatedAt = $updatedAt
            WHERE ProjectBoxId = $projectBoxId AND LinkedBoxId = $linkedBoxId;
            """;
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        command.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
        command.Parameters.AddWithValue(
            "$attachmentSide",
            (int)ProjectAttachmentSideCatalog.Normalize(attachmentSide));
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("项目文件盒关联不存在。");
        }
    }

    public async Task RemoveProjectBoxLinkAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM ProjectBoxLinks WHERE ProjectBoxId = $projectBoxId AND LinkedBoxId = $linkedBoxId;";
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        command.Parameters.AddWithValue("$linkedBoxId", linkedBoxId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPaperLink>> GetProjectPaperLinksAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ProjectBoxId, PaperId, AttachmentSide, IsVisible, SortOrder, CreatedAt, UpdatedAt
            FROM ProjectPaperLinks
            WHERE ProjectBoxId = $projectBoxId
            ORDER BY SortOrder, PaperId;
            """;
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());

        var links = new List<ProjectPaperLink>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            links.Add(ReadProjectPaperLink(reader));
        }

        return links;
    }

    public async Task UpsertProjectPaperLinkAsync(
        ProjectPaperLink link,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ProjectPaperLinks (
                ProjectBoxId, PaperId, AttachmentSide, IsVisible, SortOrder, CreatedAt, UpdatedAt)
            VALUES (
                $projectBoxId, $paperId, $attachmentSide, $isVisible, $sortOrder, $createdAt, $updatedAt)
            ON CONFLICT(ProjectBoxId, PaperId) DO UPDATE SET
                AttachmentSide = excluded.AttachmentSide,
                IsVisible = excluded.IsVisible,
                SortOrder = excluded.SortOrder,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$projectBoxId", link.ProjectBoxId.ToString());
        command.Parameters.AddWithValue("$paperId", link.PaperId);
        command.Parameters.AddWithValue("$attachmentSide", (int)link.AttachmentSide);
        command.Parameters.AddWithValue("$isVisible", link.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", link.SortOrder);
        command.Parameters.AddWithValue("$createdAt", ToDb(link.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(link.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateProjectPaperLinkVisibilityAsync(
        Guid projectBoxId,
        string paperId,
        bool isVisible,
        CancellationToken cancellationToken = default)
    {
        await UpdateProjectPaperLinkAsync(
            projectBoxId,
            paperId,
            "IsVisible = $value",
            isVisible ? 1 : 0,
            cancellationToken);
    }

    public async Task UpdateProjectPaperLinkAttachmentSideAsync(
        Guid projectBoxId,
        string paperId,
        ProjectAttachmentSide attachmentSide,
        CancellationToken cancellationToken = default)
    {
        await UpdateProjectPaperLinkAsync(
            projectBoxId,
            paperId,
            "AttachmentSide = $value",
            (int)ProjectAttachmentSideCatalog.Normalize(attachmentSide),
            cancellationToken);
    }

    public async Task RemoveProjectPaperLinkAsync(
        Guid projectBoxId,
        string paperId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM ProjectPaperLinks WHERE ProjectBoxId = $projectBoxId AND PaperId = $paperId;";
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        command.Parameters.AddWithValue("$paperId", paperId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetNextProjectPaperLinkSortOrderAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ProjectPaperLinks WHERE ProjectBoxId = $projectBoxId;";
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<ProjectIssue>> GetProjectIssuesAsync(
        Guid projectBoxId,
        bool includeResolved,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = includeResolved
            ? """
              SELECT Id, ProjectBoxId, Title, Description, SolutionState,
                     SolutionText, ResolutionState, PreviousResolutionState,
                     Priority, AssigneeName, DueAt, ResolvedAt, ResolvedBy,
                     SortOrder, CreatedAt, UpdatedAt
              FROM ProjectIssues
              WHERE ProjectBoxId = $projectBoxId
              ORDER BY ResolutionState = 3, SortOrder, CreatedAt;
              """
            : """
              SELECT Id, ProjectBoxId, Title, Description, SolutionState,
                     SolutionText, ResolutionState, PreviousResolutionState,
                     Priority, AssigneeName, DueAt, ResolvedAt, ResolvedBy,
                     SortOrder, CreatedAt, UpdatedAt
              FROM ProjectIssues
              WHERE ProjectBoxId = $projectBoxId AND ResolutionState <> 3
              ORDER BY SortOrder, CreatedAt;
              """;
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());

        var issues = new List<ProjectIssue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            issues.Add(ReadProjectIssue(reader));
        }

        return issues;
    }

    public async Task<ProjectIssue?> GetProjectIssueAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ProjectBoxId, Title, Description, SolutionState,
                   SolutionText, ResolutionState, PreviousResolutionState,
                   Priority, AssigneeName, DueAt, ResolvedAt, ResolvedBy,
                   SortOrder, CreatedAt, UpdatedAt
            FROM ProjectIssues
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", issueId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProjectIssue(reader) : null;
    }

    public async Task AddProjectIssueAsync(
        ProjectIssue issue,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ProjectIssues (
                Id, ProjectBoxId, Title, Description, SolutionState, SolutionText,
                ResolutionState, PreviousResolutionState, Priority, AssigneeName,
                DueAt, ResolvedAt, ResolvedBy, SortOrder, CreatedAt, UpdatedAt)
            VALUES (
                $id, $projectBoxId, $title, $description, $solutionState, $solutionText,
                $resolutionState, $previousResolutionState, $priority, $assigneeName,
                $dueAt, $resolvedAt, $resolvedBy, $sortOrder, $createdAt, $updatedAt);
            """;
        AddProjectIssueParameters(command, issue);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateProjectIssueAsync(
        ProjectIssue issue,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ProjectIssues
            SET Title = $title,
                Description = $description,
                SolutionState = $solutionState,
                SolutionText = $solutionText,
                ResolutionState = $resolutionState,
                PreviousResolutionState = $previousResolutionState,
                Priority = $priority,
                AssigneeName = $assigneeName,
                DueAt = $dueAt,
                ResolvedAt = $resolvedAt,
                ResolvedBy = $resolvedBy,
                SortOrder = $sortOrder,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        AddProjectIssueParameters(command, issue);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Project issue does not exist.");
        }
    }

    public async Task RemoveProjectIssueAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProjectIssues WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Project issue does not exist.");
        }
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetNextBoxSortOrderAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Boxes;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> GetNextItemSortOrderAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Items WHERE BoxId = $boxId;";
        command.Parameters.AddWithValue("$boxId", boxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> GetNextTodoSortOrderAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Todos WHERE BoxId = $boxId;";
        command.Parameters.AddWithValue("$boxId", boxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> GetNextProjectIssueSortOrderAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ProjectIssues WHERE ProjectBoxId = $projectBoxId;";
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// SQLite C 结果码 SQLITE_CANTOPEN。
    /// </summary>
    private const int SqliteErrorUnableToOpen = 14;

    public async Task<IReadOnlyList<ProjectFolderMember>> GetProjectFolderMembersAsync(
        Guid folderBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT members.FolderBoxId, members.ProjectBoxId, boxes.Name, projects.Stage,
                   members.SortOrder, members.CreatedAt, members.UpdatedAt
            FROM ProjectFolderMembers AS members
            INNER JOIN Boxes AS boxes ON boxes.Id = members.ProjectBoxId
            INNER JOIN Projects AS projects ON projects.BoxId = members.ProjectBoxId
            WHERE members.FolderBoxId = $folderBoxId AND boxes.IsArchived = 0
            ORDER BY members.SortOrder, members.CreatedAt;
            """;
        command.Parameters.AddWithValue("$folderBoxId", folderBoxId.ToString());

        var members = new List<ProjectFolderMember>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            members.Add(new ProjectFolderMember(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                ProjectStageCatalog.Get((ProjectStage)reader.GetInt32(3)).Value,
                reader.GetInt32(4),
                FromDb(reader.GetString(5)),
                FromDb(reader.GetString(6))));
        }

        return members;
    }

    public async Task<Guid?> GetProjectFolderForProjectAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT FolderBoxId FROM ProjectFolderMembers WHERE ProjectBoxId = $projectBoxId LIMIT 1;";
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is string value
            ? Guid.Parse(value)
            : null;
    }

    public async Task<IReadOnlySet<Guid>> GetGroupedProjectIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ProjectBoxId FROM ProjectFolderMembers;";
        var projectIds = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projectIds.Add(Guid.Parse(reader.GetString(0)));
        }

        return projectIds;
    }

    public async Task AddProjectFolderMemberAsync(
        Guid folderBoxId,
        Guid projectBoxId,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ProjectFolderMembers (
                FolderBoxId, ProjectBoxId, SortOrder, CreatedAt, UpdatedAt)
            VALUES ($folderBoxId, $projectBoxId, $sortOrder, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$folderBoxId", folderBoxId.ToString());
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$createdAt", ToDb(now));
        command.Parameters.AddWithValue("$updatedAt", ToDb(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveProjectFolderMemberAsync(
        Guid folderBoxId,
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM ProjectFolderMembers WHERE FolderBoxId = $folderBoxId AND ProjectBoxId = $projectBoxId;";
        command.Parameters.AddWithValue("$folderBoxId", folderBoxId.ToString());
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetNextProjectFolderMemberSortOrderAsync(
        Guid folderBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ProjectFolderMembers WHERE FolderBoxId = $folderBoxId;";
        command.Parameters.AddWithValue("$folderBoxId", folderBoxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateProjectFolderMemberOrderAsync(
        Guid folderBoxId,
        IReadOnlyList<Guid> orderedProjectBoxIds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        for (var index = 0; index < orderedProjectBoxIds.Count; index++)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                UPDATE ProjectFolderMembers
                SET SortOrder = $sortOrder, UpdatedAt = $updatedAt
                WHERE FolderBoxId = $folderBoxId AND ProjectBoxId = $projectBoxId;
                """;
            command.Parameters.AddWithValue("$sortOrder", index);
            command.Parameters.AddWithValue("$updatedAt", ToDb(now));
            command.Parameters.AddWithValue("$folderBoxId", folderBoxId.ToString());
            command.Parameters.AddWithValue("$projectBoxId", orderedProjectBoxIds[index].ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> GetProjectFolderMemberCountAsync(
        Guid folderBoxId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM ProjectFolderMembers WHERE FolderBoxId = $folderBoxId;";
        command.Parameters.AddWithValue("$folderBoxId", folderBoxId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // WAL 只解决读写并发；写写并发下默认 busy_timeout=0 会立即抛 SQLITE_BUSY。
            // 给写操作一个短暂的等待窗口，避免重叠写入（如逐项删除循环中又来导入）直接报错冒到 UI。
            DefaultTimeout = 5,
            // 避免连接池复用导致旁路文件句柄残留，便于排查目录权限问题。
            Pooling = false
        };

        return new SqliteConnection(builder.ToString());
    }

    private InvalidOperationException CreateDatabaseAccessException(string databaseDirectory, Exception exception)
    {
        return new InvalidOperationException(
            "无法打开或写入 SQLite 数据库。"
            + Environment.NewLine
            + "数据库: "
            + _databasePath
            + Environment.NewLine
            + "目录: "
            + databaseDirectory
            + Environment.NewLine
            + "请确认该目录可写，或设置环境变量 "
            + AppPaths.DataDirectoryEnvironmentVariableName
            + " 指向可写路径。",
            exception);
    }

    private static bool IsDatabaseAccessFailure(Exception exception)
    {
        if (exception is SqliteException sqliteException
            && sqliteException.SqliteErrorCode == SqliteErrorUnableToOpen)
        {
            return true;
        }

        // 目录创建失败、只读卷、路径冲突等 IO 问题同样应给出可操作的数据目录提示。
        return exception is IOException or UnauthorizedAccessException;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        var existingColumnsCommand = connection.CreateCommand();
        existingColumnsCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using (var reader = await existingColumnsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Box ReadBox(SqliteDataReader reader)
    {
        return new Box(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            (BoxType)reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            FromDb(reader.GetString(5)),
            FromDb(reader.GetString(6)))
        {
            IsArchived = reader.GetInt32(7) != 0,
            ArchivedAt = reader.IsDBNull(8) ? null : FromDb(reader.GetString(8))
        };
    }

    private static DrawerItem ReadItem(SqliteDataReader reader)
    {
        return new DrawerItem(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            (ItemKind)reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            FromDb(reader.GetString(7)),
            FromDb(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10));
    }

    private static TodoItem ReadTodo(SqliteDataReader reader)
    {
        return new TodoItem(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt32(3) != 0,
            reader.GetInt32(4),
            FromDb(reader.GetString(5)),
            FromDb(reader.GetString(6)),
            reader.IsDBNull(7) ? null : FromDb(reader.GetString(7)),
            reader.GetInt32(8) != 0,
            reader.IsDBNull(9) ? null : FromDb(reader.GetString(9)));
    }

    private static NoteDocument ReadNote(SqliteDataReader reader)
    {
        return new NoteDocument(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            FromDb(reader.GetString(2)));
    }

    private static ProjectDetails ReadProject(SqliteDataReader reader)
    {
        return new ProjectDetails(
            Guid.Parse(reader.GetString(0)),
            (ProjectStage)reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : FromDb(reader.GetString(4)),
            reader.IsDBNull(5) ? null : FromDb(reader.GetString(5)),
            FromDb(reader.GetString(6)),
            FromDb(reader.GetString(7)));
    }

    private static ProjectIssue ReadProjectIssue(SqliteDataReader reader)
    {
        return new ProjectIssue(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            (ProjectSolutionState)reader.GetInt32(4),
            reader.GetString(5),
            (ProjectResolutionState)reader.GetInt32(6),
            reader.IsDBNull(7) ? null : (ProjectResolutionState)reader.GetInt32(7),
            (ProjectPriority)reader.GetInt32(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : FromDb(reader.GetString(10)),
            reader.IsDBNull(11) ? null : FromDb(reader.GetString(11)),
            reader.GetString(12),
            reader.GetInt32(13),
            FromDb(reader.GetString(14)),
            FromDb(reader.GetString(15)));
    }

    private static ProjectBoxLink ReadProjectBoxLink(SqliteDataReader reader)
    {
        return new ProjectBoxLink(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            (BoxType)reader.GetInt32(3),
            reader.GetInt32(4) != 0,
            ProjectAttachmentSideCatalog.Normalize((ProjectAttachmentSide)reader.GetInt32(5)),
            reader.GetInt32(6),
            FromDb(reader.GetString(7)),
            FromDb(reader.GetString(8)));
    }

    private static ProjectPaperLink ReadProjectPaperLink(SqliteDataReader reader)
    {
        return new ProjectPaperLink(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            ProjectAttachmentSideCatalog.Normalize((ProjectAttachmentSide)reader.GetInt32(2)),
            reader.GetInt32(3) != 0,
            reader.GetInt32(4),
            FromDb(reader.GetString(5)),
            FromDb(reader.GetString(6)));
    }

    private async Task UpdateProjectPaperLinkAsync(
        Guid projectBoxId,
        string paperId,
        string assignment,
        int value,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE ProjectPaperLinks SET {assignment}, UpdatedAt = $updatedAt "
            + "WHERE ProjectBoxId = $projectBoxId AND PaperId = $paperId;";
        command.Parameters.AddWithValue("$projectBoxId", projectBoxId.ToString());
        command.Parameters.AddWithValue("$paperId", paperId);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("项目收纳盒关联不存在。");
        }
    }

    private static void AddProjectIssueParameters(
        SqliteCommand command,
        ProjectIssue issue)
    {
        command.Parameters.AddWithValue("$id", issue.Id.ToString());
        command.Parameters.AddWithValue("$projectBoxId", issue.ProjectBoxId.ToString());
        command.Parameters.AddWithValue("$title", issue.Title);
        command.Parameters.AddWithValue("$description", issue.Description);
        command.Parameters.AddWithValue("$solutionState", (int)issue.SolutionState);
        command.Parameters.AddWithValue("$solutionText", issue.SolutionText);
        command.Parameters.AddWithValue("$resolutionState", (int)issue.ResolutionState);
        command.Parameters.AddWithValue(
            "$previousResolutionState",
            issue.PreviousResolutionState is null
                ? DBNull.Value
                : (object)(int)issue.PreviousResolutionState.Value);
        command.Parameters.AddWithValue("$priority", (int)issue.Priority);
        command.Parameters.AddWithValue("$assigneeName", issue.AssigneeName);
        command.Parameters.AddWithValue(
            "$dueAt",
            issue.DueAt is null ? DBNull.Value : ToDb(issue.DueAt.Value));
        command.Parameters.AddWithValue(
            "$resolvedAt",
            issue.ResolvedAt is null ? DBNull.Value : ToDb(issue.ResolvedAt.Value));
        command.Parameters.AddWithValue("$resolvedBy", issue.ResolvedBy);
        command.Parameters.AddWithValue("$sortOrder", issue.SortOrder);
        command.Parameters.AddWithValue("$createdAt", ToDb(issue.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(issue.UpdatedAt));
    }

    private static string ToDb(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O");
    }

    private static DateTimeOffset FromDb(string value)
    {
        return DateTimeOffset.Parse(value);
    }
}
