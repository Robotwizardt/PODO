using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxWindowSurfaceDragTests
{
    [Fact]
    public void CanStartWholeBoxDrag_AllowsAnEmptyFileListSurface()
    {
        var canStart = RunInSta(() =>
            DesktopBoxWindow.CanStartWholeBoxDrag(
                sourceIsDrawerItem: false,
                new ListBox()));

        Assert.True(canStart);
    }

    [Fact]
    public void CanStartWholeBoxDrag_KeepsInteractiveContentInPlace()
    {
        var results = RunInSta(() => new
        {
            FileItem = DesktopBoxWindow.CanStartWholeBoxDrag(
                sourceIsDrawerItem: true,
                new TextBlock()),
            TextInput = DesktopBoxWindow.CanStartWholeBoxDrag(
                sourceIsDrawerItem: false,
                new TextBox()),
            ProjectIssue = DesktopBoxWindow.CanStartWholeBoxDrag(
                sourceIsDrawerItem: false,
                new ListBoxItem())
        });

        Assert.False(results.FileItem);
        Assert.False(results.TextInput);
        Assert.False(results.ProjectIssue);
    }

    [Theory]
    [InlineData(ModifierKeys.None, false)]
    [InlineData(ModifierKeys.Control, false)]
    [InlineData(ModifierKeys.Shift, true)]
    [InlineData(ModifierKeys.Shift | ModifierKeys.Control, true)]
    public void IsExplicitProjectUnlinkModifier_UsesShift(
        ModifierKeys modifiers,
        bool expected)
    {
        Assert.Equal(expected, DesktopBoxWindow.IsExplicitProjectUnlinkModifier(modifiers));
    }

    private static T RunInSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(exception);
        return result!;
    }
}
