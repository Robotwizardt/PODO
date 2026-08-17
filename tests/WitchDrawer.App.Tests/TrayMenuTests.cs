namespace WitchDrawer.App.Tests;

public sealed class TrayMenuTests
{
    [Theory]
    [InlineData(true, "隐藏主窗口")]
    [InlineData(false, "显示主窗口")]
    public void CreateTrayMenuItems_UsesCompactMenuWithBoxCreation(
        bool isMainWindowVisible,
        string expectedWindowCommand)
    {
        var items = App.CreateTrayMenuItems(isMainWindowVisible);

        Assert.Equal(
            [
                expectedWindowCommand,
                "显示全部桌面内容",
                "新建收纳盒",
                "管理桌面便签",
                "桌面便签设置",
                "退出 PODO"
            ],
            items.Select(item => item.Label));
        Assert.Equal([1u, 2u, 3u, 4u, 5u, 6u], items.Select(item => item.CommandId));
        Assert.DoesNotContain(items, item => item.Label is "新建待办便签" or "新建笔记便签" or "删除全部桌面便签…");
    }
}
