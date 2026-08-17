namespace WitchDrawer.Core.Models;

/// <summary>
/// 持久化的桌面笔记正文。正文保留 Markdown 原文，界面可以在编辑和预览之间切换。
/// </summary>
public sealed record NoteDocument(
    Guid BoxId,
    string Content,
    DateTimeOffset UpdatedAt);
