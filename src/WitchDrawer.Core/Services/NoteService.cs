using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

public sealed class NoteService
{
    public const int MaximumContentLength = 100_000;

    private readonly DrawerRepository _repository;

    public NoteService(DrawerRepository repository)
    {
        _repository = repository;
    }

    public Task<NoteDocument?> GetAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetNoteAsync(boxId, cancellationToken);
    }

    public async Task<NoteDocument> EnsureAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("笔记便签不存在或已被删除。");
        if (box.Type != BoxType.Note)
        {
            throw new InvalidOperationException("只能读取笔记便签正文。");
        }

        var existing = await _repository.GetNoteAsync(boxId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new NoteDocument(boxId, string.Empty, DateTimeOffset.UtcNow);
        await _repository.UpsertNoteAsync(created, cancellationToken);
        return created;
    }

    public async Task<NoteDocument> SaveAsync(
        Guid boxId,
        string? content,
        CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("笔记便签不存在或已被删除。");
        if (box.Type != BoxType.Note)
        {
            throw new InvalidOperationException("只能保存笔记便签正文。");
        }

        var normalized = content ?? string.Empty;
        if (normalized.Length > MaximumContentLength)
        {
            throw new ArgumentException(
                $"笔记正文不能超过 {MaximumContentLength} 个字符。",
                nameof(content));
        }

        var saved = new NoteDocument(boxId, normalized, DateTimeOffset.UtcNow);
        await _repository.UpsertNoteAsync(saved, cancellationToken);
        return saved;
    }
}
