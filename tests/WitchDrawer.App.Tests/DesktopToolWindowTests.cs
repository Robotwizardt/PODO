using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopToolWindowTests
{
    [Fact]
    public void TaskbarCreatedMessage_IsRegistered()
    {
        Assert.NotEqual(0, DesktopToolWindow.TaskbarCreatedMessage);
    }

    [Theory]
    [InlineData(DesktopToolWindow.SystemCommandMessage, 0xF020, true)]
    [InlineData(DesktopToolWindow.SystemCommandMessage, 0xF023, true)]
    [InlineData(DesktopToolWindow.SystemCommandMessage, 0xF060, false)]
    [InlineData(0x0111, 0xF020, false)]
    public void IsMinimizeSystemCommand_RecognizesOnlySystemMinimize(
        int message,
        long command,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopToolWindow.IsMinimizeSystemCommand(message, (nint)command));
    }
}
