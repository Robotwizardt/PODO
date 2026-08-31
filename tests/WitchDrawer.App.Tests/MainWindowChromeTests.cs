using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class MainWindowChromeTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void MainWindow_ClientSurfaceDoesNotCreateAnInnerBlackFrame()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        Assert.NotNull(document.Root);
        var window = document.Root;
        var clientSurface = Assert.Single(
            window.Elements(PresentationNamespace + "Border"));

        Assert.True(
            clientSurface.Attribute("Margin") is null
                or { Value: "0" },
            "The main client surface must reach the native window edge; an inset exposes a dark frame.");
        Assert.Empty(clientSurface.Elements(PresentationNamespace + "Border.Effect"));
    }

    private static string GetMainWindowXamlPath() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "WitchDrawer.App",
                "MainWindow.xaml"));
}
