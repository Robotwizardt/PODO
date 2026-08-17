using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

public sealed class ProjectFolderService
{
    private readonly DrawerRepository _repository;

    public ProjectFolderService(DrawerRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<ProjectFolderMember>> GetMembersAsync(
        Guid folderBoxId,
        CancellationToken cancellationToken = default) =>
        _repository.GetProjectFolderMembersAsync(folderBoxId, cancellationToken);

    public Task<Guid?> GetFolderForProjectAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default) =>
        _repository.GetProjectFolderForProjectAsync(projectBoxId, cancellationToken);

    public Task<IReadOnlySet<Guid>> GetGroupedProjectIdsAsync(
        CancellationToken cancellationToken = default) =>
        _repository.GetGroupedProjectIdsAsync(cancellationToken);

    public async Task<Box> CreateAsync(
        string name,
        IEnumerable<Guid> projectBoxIds,
        CancellationToken cancellationToken = default)
    {
        var projectIds = projectBoxIds.Distinct().ToArray();
        if (projectIds.Length < 2)
        {
            throw new InvalidOperationException("项目文件夹至少需要两个项目。");
        }

        foreach (var projectId in projectIds)
        {
            await EnsureProjectAsync(projectId, cancellationToken);
            if (await _repository.GetProjectFolderForProjectAsync(projectId, cancellationToken) is not null)
            {
                throw new InvalidOperationException("项目已经属于另一个项目文件夹。");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var folder = new Box(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(name) ? "项目文件夹" : name.Trim(),
            BoxType.ProjectFolder,
            null,
            await _repository.GetNextBoxSortOrderAsync(cancellationToken),
            now,
            now);
        await _repository.AddBoxAsync(folder, cancellationToken);
        try
        {
            for (var index = 0; index < projectIds.Length; index++)
            {
                await _repository.AddProjectFolderMemberAsync(
                    folder.Id,
                    projectIds[index],
                    index,
                    cancellationToken);
            }

            return folder;
        }
        catch
        {
            await _repository.RemoveBoxAsync(folder.Id, CancellationToken.None);
            throw;
        }
    }

    public async Task AddProjectAsync(
        Guid folderBoxId,
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFolderAsync(folderBoxId, cancellationToken);
        await EnsureProjectAsync(projectBoxId, cancellationToken);
        var existingFolderId = await _repository.GetProjectFolderForProjectAsync(
            projectBoxId,
            cancellationToken);
        if (existingFolderId == folderBoxId)
        {
            return;
        }

        if (existingFolderId is Guid previousFolderId)
        {
            await _repository.RemoveProjectFolderMemberAsync(
                previousFolderId,
                projectBoxId,
                cancellationToken);
            await DissolveIfSparseAsync(previousFolderId, cancellationToken);
        }

        await _repository.AddProjectFolderMemberAsync(
            folderBoxId,
            projectBoxId,
            await _repository.GetNextProjectFolderMemberSortOrderAsync(
                folderBoxId,
                cancellationToken),
            cancellationToken);
    }

    public async Task RemoveProjectAsync(
        Guid projectBoxId,
        CancellationToken cancellationToken = default)
    {
        var folderId = await _repository.GetProjectFolderForProjectAsync(
            projectBoxId,
            cancellationToken);
        if (folderId is null)
        {
            return;
        }

        await _repository.RemoveProjectFolderMemberAsync(
            folderId.Value,
            projectBoxId,
            cancellationToken);
        await DissolveIfSparseAsync(folderId.Value, cancellationToken);
    }

    public async Task MoveProjectBeforeAsync(
        Guid folderBoxId,
        Guid projectBoxId,
        Guid targetProjectBoxId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFolderAsync(folderBoxId, cancellationToken);
        if (projectBoxId == targetProjectBoxId)
        {
            return;
        }

        var memberIds = (await GetMembersAsync(folderBoxId, cancellationToken))
            .Select(member => member.ProjectBoxId)
            .ToList();
        if (!memberIds.Remove(projectBoxId))
        {
            throw new InvalidOperationException("要移动的项目不属于该项目文件夹。");
        }

        var targetIndex = memberIds.IndexOf(targetProjectBoxId);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException("目标项目不属于该项目文件夹。");
        }

        memberIds.Insert(targetIndex, projectBoxId);
        await _repository.UpdateProjectFolderMemberOrderAsync(
            folderBoxId,
            memberIds,
            cancellationToken);
    }

    public async Task DissolveAsync(
        Guid folderBoxId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFolderAsync(folderBoxId, cancellationToken);
        await _repository.RemoveBoxAsync(folderBoxId, cancellationToken);
    }

    public async Task DissolveIfSparseAsync(
        Guid folderBoxId,
        CancellationToken cancellationToken = default)
    {
        var folder = await _repository.GetBoxAsync(folderBoxId, cancellationToken);
        if (folder?.Type != BoxType.ProjectFolder)
        {
            return;
        }

        if (await _repository.GetProjectFolderMemberCountAsync(folderBoxId, cancellationToken) <= 1)
        {
            await _repository.RemoveBoxAsync(folderBoxId, cancellationToken);
        }
    }

    private async Task EnsureProjectAsync(Guid projectBoxId, CancellationToken cancellationToken)
    {
        var box = await _repository.GetBoxAsync(projectBoxId, cancellationToken);
        if (box?.Type != BoxType.Project || box.IsArchived)
        {
            throw new InvalidOperationException("项目文件夹只能包含未归档的项目收纳盒。");
        }
    }

    private async Task EnsureFolderAsync(Guid folderBoxId, CancellationToken cancellationToken)
    {
        var box = await _repository.GetBoxAsync(folderBoxId, cancellationToken);
        if (box?.Type != BoxType.ProjectFolder)
        {
            throw new InvalidOperationException("项目文件夹不存在或已被删除。");
        }
    }
}
