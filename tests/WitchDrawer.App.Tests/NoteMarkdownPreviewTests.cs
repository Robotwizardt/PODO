using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Tests;

public sealed class NoteMarkdownPreviewTests
{
    [Fact]
    public void Parse_RecognizesCommonPaperTodoMarkdownBlocks()
    {
        var blocks = NoteMarkdownPreview.Parse(
            "# 标题\n- 第一项\n1. 第二项\n> 提醒\n```\ncode\n```\n---");

        Assert.Collection(
            blocks,
            heading =>
            {
                Assert.Equal(NotePreviewBlockKind.Heading, heading.Kind);
                Assert.Equal("标题", heading.Text);
            },
            bullet =>
            {
                Assert.Equal(NotePreviewBlockKind.Bullet, bullet.Kind);
                Assert.Equal("• 第一项", bullet.Text);
            },
            ordered =>
            {
                Assert.Equal(NotePreviewBlockKind.Ordered, ordered.Kind);
                Assert.Equal("1. 第二项", ordered.Text);
            },
            quote =>
            {
                Assert.Equal(NotePreviewBlockKind.Quote, quote.Kind);
                Assert.Equal("提醒", quote.Text);
            },
            code =>
            {
                Assert.Equal(NotePreviewBlockKind.Code, code.Kind);
                Assert.Equal("code", code.Text);
            },
            divider => Assert.Equal(NotePreviewBlockKind.Divider, divider.Kind));
    }

    [Fact]
    public void Parse_EmptyContentReturnsEditablePlaceholder()
    {
        var block = Assert.Single(NoteMarkdownPreview.Parse(string.Empty));

        Assert.True(block.IsBlank);
        Assert.Equal("开始输入笔记…", block.Text);
    }
}
