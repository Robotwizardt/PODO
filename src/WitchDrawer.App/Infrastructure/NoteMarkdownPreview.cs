using System.Text.RegularExpressions;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 轻量 Markdown 预览。正文仍保存为原文，只把 PaperTodo 常用的标题、列表、引用和代码块
/// 转成适合桌面便签快速浏览的视觉层级。
/// </summary>
public static partial class NoteMarkdownPreview
{
    [GeneratedRegex("^#{1,6}\\s+")]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex("^\\d+\\.\\s+")]
    private static partial Regex OrderedPrefixRegex();

    public static IReadOnlyList<NotePreviewBlockViewModel> Parse(string? content)
    {
        var normalized = (content ?? string.Empty).Replace("\r\n", "\n");
        if (normalized.Length == 0)
        {
            return [new NotePreviewBlockViewModel("开始输入笔记…", NotePreviewBlockKind.Blank)];
        }

        var blocks = new List<NotePreviewBlockViewModel>();
        var inCode = false;
        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                blocks.Add(new NotePreviewBlockViewModel(line, NotePreviewBlockKind.Code));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                blocks.Add(new NotePreviewBlockViewModel(string.Empty, NotePreviewBlockKind.Blank));
                continue;
            }

            if (line is "---" or "***" or "___")
            {
                blocks.Add(new NotePreviewBlockViewModel("", NotePreviewBlockKind.Divider));
                continue;
            }

            var heading = HeadingPrefixRegex().Match(line);
            if (heading.Success)
            {
                blocks.Add(new NotePreviewBlockViewModel(
                    CleanInlineMarkdown(line[heading.Length..]),
                    NotePreviewBlockKind.Heading));
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal)
                || string.Equals(line, ">", StringComparison.Ordinal))
            {
                blocks.Add(new NotePreviewBlockViewModel(
                    CleanInlineMarkdown(line.Length > 1 ? line[1..].TrimStart() : string.Empty),
                    NotePreviewBlockKind.Quote,
                    "│"));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("* ", StringComparison.Ordinal))
            {
                blocks.Add(new NotePreviewBlockViewModel(
                    "• " + CleanInlineMarkdown(line[2..]),
                    NotePreviewBlockKind.Bullet,
                    "•"));
                continue;
            }

            var ordered = OrderedPrefixRegex().Match(line);
            if (ordered.Success)
            {
                blocks.Add(new NotePreviewBlockViewModel(
                    line[..ordered.Length] + CleanInlineMarkdown(line[ordered.Length..]),
                    NotePreviewBlockKind.Ordered,
                    line[..ordered.Length].TrimEnd()));
                continue;
            }

            blocks.Add(new NotePreviewBlockViewModel(
                CleanInlineMarkdown(line),
                NotePreviewBlockKind.Paragraph));
        }

        return blocks;
    }

    private static string CleanInlineMarkdown(string value)
    {
        return value
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("~~", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
    }
}
