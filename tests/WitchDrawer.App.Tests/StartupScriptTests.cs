using System.IO;

namespace WitchDrawer.App.Tests;

public sealed class StartupScriptTests
{
    [Fact]
    public void StarterScript_UsesWindowsLineEndings()
    {
        var path = Path.Combine(FindRepositoryRoot(), "启动PODO.cmd");
        var bytes = File.ReadAllBytes(path);

        Assert.NotEmpty(bytes);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == 0x0A)
            {
                Assert.True(
                    index > 0 && bytes[index - 1] == 0x0D,
                    $"The starter script contains a non-Windows line ending at byte {index}.");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WitchDrawer.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WitchDrawer repository root.");
    }
}
