using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

public sealed class TodoService
{
    public const int MaximumTitleLength = 200;

    private readonly DrawerRepository _repository;

    public TodoService(DrawerRepository repository)
    {
        _repository = repository;
    }

    public DrawerRepository Repository => _repository;

    public Task<IReadOnlyList<TodoItem>> GetTodosAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetTodosAsync(boxId, cancellationToken);
    }

    public Task<IReadOnlyList<TodoItem>> GetArchivedTodosAsync(
        Guid? boxId = null,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetArchivedTodosAsync(boxId, cancellationToken);
    }

    public async Task<TodoItem> AddTodoAsync(
        Guid boxId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("待办盒不存在或已被删除。");
        if (box.Type != BoxType.Todo)
        {
            throw new InvalidOperationException("只能向待办盒添加待办事项。");
        }

        var normalizedTitle = NormalizeTitle(title);
        var now = DateTimeOffset.UtcNow;
        var todo = new TodoItem(
            Guid.NewGuid(),
            boxId,
            normalizedTitle,
            IsCompleted: false,
            await _repository.GetNextTodoSortOrderAsync(boxId, cancellationToken),
            now,
            now);

        await _repository.AddTodoAsync(todo, cancellationToken);
        return todo;
    }

    public async Task<TodoItem> SetCompletedAsync(
        Guid todoId,
        bool isCompleted,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetTodoAsync(todoId, cancellationToken)
            ?? throw new InvalidOperationException("待办事项不存在或已被删除。");

        if (existing.IsCompleted == isCompleted)
        {
            return existing;
        }

        var updatedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? completedAt = isCompleted ? updatedAt : null;
        await _repository.UpdateTodoCompletionAsync(
            todoId,
            isCompleted,
            completedAt,
            updatedAt,
            cancellationToken);

        return existing with
        {
            IsCompleted = isCompleted,
            CompletedAt = completedAt,
            UpdatedAt = updatedAt
        };
    }

    public Task DeleteTodoAsync(Guid todoId, CancellationToken cancellationToken = default)
    {
        return _repository.RemoveTodoAsync(todoId, cancellationToken);
    }

    public async Task<int> ArchiveCompletedAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("待办盒不存在或已被删除。");
        if (box.Type != BoxType.Todo)
        {
            throw new InvalidOperationException("只能归档待办盒中的事项。");
        }

        return await _repository.ArchiveCompletedTodosAsync(
            boxId,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public async Task<TodoItem> RestoreArchivedAsync(
        Guid todoId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetTodoAsync(todoId, cancellationToken)
            ?? throw new InvalidOperationException("归档事项不存在或已被删除。");

        if (!existing.IsArchived)
        {
            return existing;
        }

        var updatedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateTodoArchiveStateAsync(
            todoId,
            isArchived: false,
            archivedAt: null,
            updatedAt,
            cancellationToken);

        return existing with
        {
            IsArchived = false,
            ArchivedAt = null,
            UpdatedAt = updatedAt
        };
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = title?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("待办内容不能为空。", nameof(title));
        }

        if (normalized.Length > MaximumTitleLength)
        {
            throw new ArgumentException(
                $"待办内容不能超过 {MaximumTitleLength} 个字符。",
                nameof(title));
        }

        return normalized;
    }
}
