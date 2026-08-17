using Microsoft.Data.Sqlite;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

public sealed class ProjectService
{
    public const int MaximumTitleLength = 240;
    public const int MaximumDescriptionLength = 4000;

    private readonly DrawerRepository _repository;

    public ProjectService(DrawerRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProjectDetails> GetOrCreateProjectAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetProjectAsync(boxId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var project = new ProjectDetails(
            boxId,
            ProjectStageCatalog.DefaultStage,
            string.Empty,
            string.Empty,
            null,
            null,
            now,
            now);
        await _repository.UpsertProjectAsync(project, cancellationToken);
        return project;
    }

    public async Task<ProjectDetails> UpdateProjectAsync(
        ProjectDetails project,
        CancellationToken cancellationToken = default)
    {
        var updated = project with
        {
            OwnerName = NormalizeText(project.OwnerName, 120),
            Description = NormalizeText(project.Description, MaximumDescriptionLength),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _repository.UpsertProjectAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<ProjectBoxLink>> GetLinkedBoxesAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        return await _repository.GetProjectBoxLinksAsync(projectBoxId, cancellationToken);
    }

    public async Task<IReadOnlyList<Box>> GetLinkableBoxesAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        var linkedBoxIds = (await _repository.GetProjectBoxLinksAsync(projectBoxId, cancellationToken))
            .Select(link => link.LinkedBoxId)
            .ToHashSet();
        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        var linkableBoxes = new List<Box>();
        foreach (var box in boxes)
        {
            if (box.Id == projectBoxId
                || !IsFileBoxType(box.Type)
                || linkedBoxIds.Contains(box.Id))
            {
                continue;
            }

            var linkedProjectId = await _repository.GetProjectBoxForLinkedBoxAsync(
                box.Id,
                cancellationToken);
            if (linkedProjectId is null || linkedProjectId == projectBoxId)
            {
                linkableBoxes.Add(box);
            }
        }

        return linkableBoxes;
    }

    public Task<Guid?> GetProjectBoxForLinkedBoxAsync(
        Guid linkedBoxId,
        CancellationToken cancellationToken = default) =>
        _repository.GetProjectBoxForLinkedBoxAsync(linkedBoxId, cancellationToken);

    public async Task<ProjectBoxLink> LinkBoxAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        var linkedBox = await _repository.GetBoxAsync(linkedBoxId, cancellationToken)
            ?? throw new InvalidOperationException("要关联的文件盒不存在或已被删除。");
        if (!IsFileBoxType(linkedBox.Type))
        {
            throw new InvalidOperationException("项目收纳盒只能关联文件收纳盒。");
        }

        var linkedProjectId = await _repository.GetProjectBoxForLinkedBoxAsync(
            linkedBoxId,
            cancellationToken);
        if (linkedProjectId is not null && linkedProjectId != projectBoxId)
        {
            throw new InvalidOperationException("这个文件收纳盒已经关联到另一个项目收纳盒。");
        }

        var existing = (await _repository.GetProjectBoxLinksAsync(projectBoxId, cancellationToken))
            .FirstOrDefault(link => link.LinkedBoxId == linkedBoxId);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var link = new ProjectBoxLink(
            projectBoxId,
            linkedBox.Id,
            linkedBox.Name,
            linkedBox.Type,
            true,
            ProjectAttachmentSide.Right,
            await _repository.GetNextProjectBoxLinkSortOrderAsync(projectBoxId, cancellationToken),
            now,
            now);
        try
        {
            await _repository.UpsertProjectBoxLinkAsync(link, cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            // The database is the final authority for single ownership. A second
            // caller can pass the read-before-write checks above, so re-read after
            // a constraint race and return the same friendly domain error.
            var owner = await _repository.GetProjectBoxForLinkedBoxAsync(
                linkedBoxId,
                cancellationToken);
            if (owner is Guid ownerId && ownerId != projectBoxId)
            {
                throw new InvalidOperationException(
                    "这个文件收纳盒已经关联到另一个项目收纳盒。",
                    exception);
            }

            throw;
        }
        return link;
    }

    /// <summary>
    /// Moves an existing file-box association to another project as one database
    /// operation, so a failed target association cannot leave the file unlinked.
    /// </summary>
    public async Task<ProjectBoxLink> MoveBoxLinkAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        ProjectAttachmentSide attachmentSide,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        var linkedBox = await _repository.GetBoxAsync(linkedBoxId, cancellationToken)
            ?? throw new InvalidOperationException("要关联的文件盒不存在或已被删除。");
        if (!IsFileBoxType(linkedBox.Type))
        {
            throw new InvalidOperationException("项目收纳盒只能关联文件收纳盒。");
        }

        await _repository.MoveProjectBoxLinkAsync(
            projectBoxId,
            linkedBoxId,
            attachmentSide,
            cancellationToken);
        return (await _repository.GetProjectBoxLinksAsync(projectBoxId, cancellationToken))
            .Single(link => link.LinkedBoxId == linkedBoxId);
    }

    public async Task SetLinkedBoxVisibilityAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        bool isVisible,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        await _repository.UpdateProjectBoxLinkVisibilityAsync(
            projectBoxId,
            linkedBoxId,
            isVisible,
            cancellationToken);
    }

    public async Task SetLinkedBoxAttachmentSideAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        ProjectAttachmentSide attachmentSide,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        await _repository.UpdateProjectBoxLinkAttachmentSideAsync(
            projectBoxId,
            linkedBoxId,
            attachmentSide,
            cancellationToken);
    }

    public async Task UnlinkBoxAsync(
        Guid projectBoxId,
        Guid linkedBoxId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        await _repository.RemoveProjectBoxLinkAsync(projectBoxId, linkedBoxId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPaperLink>> GetLinkedPapersAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        return await _repository.GetProjectPaperLinksAsync(projectBoxId, cancellationToken);
    }

    public async Task<ProjectArchiveSnapshot> GetProjectArchiveSnapshotAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        var projectBox = await GetProjectBoxAsync(projectBoxId, cancellationToken);
        return await CreateProjectArchiveSnapshotAsync(projectBox, cancellationToken);
    }

    public async Task<ProjectArchiveSnapshot> ArchiveProjectAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        var projectBox = await GetProjectBoxAsync(projectBoxId, cancellationToken);
        if (projectBox.IsArchived)
        {
            throw new InvalidOperationException("项目收纳盒已经在归档区。");
        }

        var snapshot = await CreateProjectArchiveSnapshotAsync(projectBox, cancellationToken);
        var archivedAt = DateTimeOffset.UtcNow;
        var boxIds = snapshot.LinkedBoxes
            .Select(link => link.LinkedBoxId)
            .Append(projectBox.Id)
            .ToArray();
        await _repository.UpdateBoxArchiveStatesAsync(
            boxIds,
            isArchived: true,
            archivedAt: archivedAt,
            cancellationToken: cancellationToken);
        await new ProjectFolderService(_repository).RemoveProjectAsync(
            projectBoxId,
            cancellationToken);

        return snapshot with
        {
            ProjectBox = projectBox with
            {
                IsArchived = true,
                ArchivedAt = archivedAt,
                UpdatedAt = archivedAt
            }
        };
    }

    public async Task<ProjectArchiveSnapshot> RestoreProjectAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        var projectBox = await GetProjectBoxAsync(projectBoxId, cancellationToken);
        if (!projectBox.IsArchived)
        {
            throw new InvalidOperationException("项目收纳盒不在归档区。");
        }

        var snapshot = await CreateProjectArchiveSnapshotAsync(projectBox, cancellationToken);
        var restoredAt = DateTimeOffset.UtcNow;
        var boxIds = snapshot.LinkedBoxes
            .Select(link => link.LinkedBoxId)
            .Append(projectBox.Id)
            .ToArray();
        await _repository.UpdateBoxArchiveStatesAsync(
            boxIds,
            isArchived: false,
            cancellationToken: cancellationToken);

        return snapshot with
        {
            ProjectBox = projectBox with
            {
                IsArchived = false,
                ArchivedAt = null,
                UpdatedAt = restoredAt
            }
        };
    }

    public Task<Guid?> GetProjectBoxForLinkedPaperAsync(
        string paperId,
        CancellationToken cancellationToken = default) =>
        _repository.GetProjectBoxForLinkedPaperAsync(NormalizePaperId(paperId), cancellationToken);

    public async Task<ProjectPaperLink> LinkPaperAsync(
        Guid projectBoxId,
        string paperId,
        ProjectAttachmentSide attachmentSide,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        var normalizedPaperId = NormalizePaperId(paperId);
        var linkedProjectId = await _repository.GetProjectBoxForLinkedPaperAsync(
            normalizedPaperId,
            cancellationToken);
        if (linkedProjectId is not null && linkedProjectId != projectBoxId)
        {
            throw new InvalidOperationException("这张桌面便签已经关联到另一个项目收纳盒。");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = (await _repository.GetProjectPaperLinksAsync(projectBoxId, cancellationToken))
            .FirstOrDefault(link => string.Equals(link.PaperId, normalizedPaperId, StringComparison.Ordinal));
        var link = new ProjectPaperLink(
            projectBoxId,
            normalizedPaperId,
            ProjectAttachmentSideCatalog.Normalize(attachmentSide),
            existing?.IsVisible ?? true,
            existing?.SortOrder
                ?? await _repository.GetNextProjectPaperLinkSortOrderAsync(projectBoxId, cancellationToken),
            existing?.CreatedAt ?? now,
            now);
        await _repository.UpsertProjectPaperLinkAsync(link, cancellationToken);
        return link;
    }

    public async Task SetLinkedPaperVisibilityAsync(
        Guid projectBoxId,
        string paperId,
        bool isVisible,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        await _repository.UpdateProjectPaperLinkVisibilityAsync(
            projectBoxId,
            NormalizePaperId(paperId),
            isVisible,
            cancellationToken);
    }

    public async Task UnlinkPaperAsync(
        Guid projectBoxId,
        string paperId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        await _repository.RemoveProjectPaperLinkAsync(
            projectBoxId,
            NormalizePaperId(paperId),
            cancellationToken);
    }

    public Task<IReadOnlyList<ProjectIssue>> GetIssuesAsync(
        Guid projectBoxId,
        bool includeResolved,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetProjectIssuesAsync(
            projectBoxId,
            includeResolved,
            cancellationToken);
    }

    public async Task<ProjectIssue> AddIssueAsync(
        Guid projectBoxId,
        string title,
        string description,
        ProjectSolutionState solutionState,
        string solutionText,
        ProjectPriority priority,
        string assigneeName,
        DateTimeOffset? dueAt,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectBoxAsync(projectBoxId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var issue = new ProjectIssue(
            Guid.NewGuid(),
            projectBoxId,
            NormalizeTitle(title),
            NormalizeText(description, MaximumDescriptionLength),
            solutionState,
            NormalizeText(solutionText, MaximumDescriptionLength),
            ProjectResolutionState.Open,
            null,
            priority,
            NormalizeText(assigneeName, 120),
            dueAt,
            null,
            string.Empty,
            await _repository.GetNextProjectIssueSortOrderAsync(projectBoxId, cancellationToken),
            now,
            now);

        await _repository.AddProjectIssueAsync(issue, cancellationToken);
        return issue;
    }

    public async Task<ProjectIssue> UpdateIssueAsync(
        ProjectIssue issue,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetProjectIssueAsync(issue.Id, cancellationToken)
            ?? throw new InvalidOperationException("问题不存在或已被删除。");
        if (existing.ProjectBoxId != issue.ProjectBoxId)
        {
            throw new InvalidOperationException("问题不属于当前项目。");
        }

        var updated = issue with
        {
            Title = NormalizeTitle(issue.Title),
            Description = NormalizeText(issue.Description, MaximumDescriptionLength),
            SolutionText = NormalizeText(issue.SolutionText, MaximumDescriptionLength),
            AssigneeName = NormalizeText(issue.AssigneeName, 120),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (existing.ResolutionState != updated.ResolutionState)
        {
            if (updated.ResolutionState == ProjectResolutionState.Resolved
                && existing.ResolutionState != ProjectResolutionState.Resolved)
            {
                updated = updated with
                {
                    PreviousResolutionState = existing.ResolutionState,
                    ResolvedAt = DateTimeOffset.UtcNow,
                    ResolvedBy = Environment.UserName
                };
            }
            else if (existing.ResolutionState == ProjectResolutionState.Resolved)
            {
                updated = updated with
                {
                    PreviousResolutionState = null,
                    ResolvedAt = null,
                    ResolvedBy = string.Empty
                };
            }
        }
        await _repository.UpdateProjectIssueAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<ProjectIssue> ResolveIssueAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetIssueOrThrowAsync(issueId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var resolved = existing with
        {
            PreviousResolutionState = existing.ResolutionState == ProjectResolutionState.Resolved
                ? existing.PreviousResolutionState
                : existing.ResolutionState,
            ResolutionState = ProjectResolutionState.Resolved,
            ResolvedAt = now,
            ResolvedBy = Environment.UserName,
            UpdatedAt = now
        };
        await _repository.UpdateProjectIssueAsync(resolved, cancellationToken);
        return resolved;
    }

    public async Task<ProjectIssue> ReopenIssueAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetIssueOrThrowAsync(issueId, cancellationToken);
        var reopened = existing with
        {
            ResolutionState = existing.PreviousResolutionState ?? ProjectResolutionState.Open,
            PreviousResolutionState = null,
            ResolvedAt = null,
            ResolvedBy = string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _repository.UpdateProjectIssueAsync(reopened, cancellationToken);
        return reopened;
    }

    public async Task<ProjectIssue> SetResolutionStateAsync(
        Guid issueId,
        ProjectResolutionState state,
        CancellationToken cancellationToken = default)
    {
        if (state == ProjectResolutionState.Resolved)
        {
            return await ResolveIssueAsync(issueId, cancellationToken);
        }

        var existing = await GetIssueOrThrowAsync(issueId, cancellationToken);
        var updated = existing with
        {
            ResolutionState = state,
            PreviousResolutionState = null,
            ResolvedAt = null,
            ResolvedBy = string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _repository.UpdateProjectIssueAsync(updated, cancellationToken);
        return updated;
    }

    public async Task DeleteIssueAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        await _repository.RemoveProjectIssueAsync(issueId, cancellationToken);
    }

    private async Task EnsureProjectBoxAsync(
        Guid boxId,
        CancellationToken cancellationToken)
    {
        _ = await GetProjectBoxAsync(boxId, cancellationToken);
    }

    private async Task<Box> GetProjectBoxAsync(
        Guid boxId,
        CancellationToken cancellationToken)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("项目收纳盒不存在或已被删除。");
        if (box.Type != BoxType.Project)
        {
            throw new InvalidOperationException("只能向项目收纳盒添加模块。");
        }

        return box;
    }

    private async Task<ProjectArchiveSnapshot> CreateProjectArchiveSnapshotAsync(
        Box projectBox,
        CancellationToken cancellationToken)
    {
        var linkedBoxesTask = _repository.GetProjectBoxLinksAsync(projectBox.Id, cancellationToken);
        var linkedPapersTask = _repository.GetProjectPaperLinksAsync(projectBox.Id, cancellationToken);
        await Task.WhenAll(linkedBoxesTask, linkedPapersTask);
        return new ProjectArchiveSnapshot(
            projectBox,
            await linkedBoxesTask,
            await linkedPapersTask);
    }

    private static bool IsFileBoxType(BoxType type) =>
        type is BoxType.Normal
            or BoxType.Mapping
            or BoxType.Pixel
            or BoxType.Drawer
            or BoxType.Bound;

    private async Task<ProjectIssue> GetIssueOrThrowAsync(
        Guid issueId,
        CancellationToken cancellationToken)
    {
        return await _repository.GetProjectIssueAsync(issueId, cancellationToken)
            ?? throw new InvalidOperationException("问题不存在或已被删除。");
    }

    private static string NormalizeTitle(string value)
    {
        var normalized = NormalizeText(value, MaximumTitleLength);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("问题标题不能为空。", nameof(value));
        }

        return normalized;
    }

    private static string NormalizePaperId(string? paperId)
    {
        var normalized = paperId?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 160)
        {
            throw new ArgumentException("便签标识无效。", nameof(paperId));
        }

        return normalized;
    }

    private static string NormalizeText(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"文本不能超过 {maximumLength} 个字符。",
                nameof(value));
        }

        return normalized;
    }
}
