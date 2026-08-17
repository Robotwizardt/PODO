using PaperTodo;

namespace WitchDrawer.App.Tests;

public sealed class PaperTodoCountPresentationTests
{
    [Fact]
    public void GetUnfinishedCount_IgnoresCompletedAndBlankItems()
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items =
            [
                new PaperItem { Text = "确认需求" },
                new PaperItem { Text = "完成开发" },
                new PaperItem { Text = "已上线", Done = true },
                new PaperItem { Text = "   " }
            ]
        };

        Assert.Equal(2, PaperTodoCountPresentation.GetUnfinishedCount(paper));
    }

    [Fact]
    public void AppendToCapsuleTitle_ShowsRemainingTodoCount()
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items =
            [
                new PaperItem { Text = "确认需求" },
                new PaperItem { Text = "完成开发" },
                new PaperItem { Text = "已上线", Done = true }
            ]
        };

        Assert.Equal(
            "网站改版 · 2项",
            PaperTodoCountPresentation.AppendToCapsuleTitle(paper, "网站改版"));
    }
}
