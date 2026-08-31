using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class StartupConfigurationTests
{
    [Fact]
    public void InstallerStartupEntry_TargetsTheBuiltApplicationExecutable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "WitchDrawer.App",
            "WitchDrawer.App.csproj"));
        var assemblyName = project
            .Descendants("AssemblyName")
            .Select(element => element.Value.Trim())
            .Single();

        var installerScript = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "installer",
            "WitchDrawer.iss"));
        var installerExecutable = Regex.Match(
            installerScript,
            "#define\\s+MyAppExeName\\s+\"(?<name>[^\"]+)\"")
            .Groups["name"]
            .Value;

        Assert.Equal($"{assemblyName}.exe", installerExecutable);
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
