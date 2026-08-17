using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxWindowTemplateTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void IconGridTemplate_CentersContentVertically()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var iconList = Assert.Single(
            document.Descendants(PresentationNamespace + "ListBox"),
            element =>
                (string?)element.Attribute(XamlNamespace + "Name") == "IconList");
        var itemTemplate = Assert.Single(
            iconList.Elements(PresentationNamespace + "ListBox.ItemTemplate"));
        var dataTemplate = Assert.Single(
            itemTemplate.Elements(PresentationNamespace + "DataTemplate"));
        var templateRoot = Assert.Single(
            dataTemplate.Elements(),
            element => !element.Name.LocalName.Contains('.', StringComparison.Ordinal));

        Assert.Equal("StackPanel", templateRoot.Name.LocalName);
        Assert.Equal("Center", (string?)templateRoot.Attribute("VerticalAlignment"));
    }

    [Theory]
    [InlineData("ToggleProjectLeftAttachmentsCommand", "-22,0,0,0", "28", "36")]
    [InlineData("ToggleProjectRightAttachmentsCommand", "0,0,-22,0", "28", "36")]
    [InlineData("ToggleProjectTopAttachmentsCommand", "0,-22,0,0", "36", "28")]
    [InlineData("ToggleProjectBottomAttachmentsCommand", "0,0,0,-22", "36", "28")]
    public void ProjectDirectionButtons_AreFlatExternalHoverAffordances(
        string commandName,
        string expectedMargin,
        string expectedWidth,
        string expectedHeight)
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var windowBorder = Assert.Single(
            document.Descendants(PresentationNamespace + "Border"),
            element =>
                (string?)element.Attribute(XamlNamespace + "Name") == "WindowBorder");
        var surface = Assert.Single(
            windowBorder.Elements(PresentationNamespace + "Grid"));
        var button = Assert.Single(
            surface.Elements(PresentationNamespace + "Button"),
            element => ((string?)element.Attribute("Command"))?.Contains(
                commandName,
                StringComparison.Ordinal) == true);

        Assert.Equal("2", (string?)button.Attribute("Grid.RowSpan"));
        Assert.Equal(expectedMargin, (string?)button.Attribute("Margin"));
        Assert.Equal(expectedWidth, (string?)button.Attribute("Width"));
        Assert.Equal(expectedHeight, (string?)button.Attribute("Height"));
        Assert.NotNull(button.Attribute("AutomationProperties.Name"));
        Assert.Empty(button.Descendants(PresentationNamespace + "TextBlock"));

        var chevron = Assert.Single(button.Descendants(PresentationNamespace + "Path"));
        Assert.Equal(
            "{DynamicResource ProjectAttachmentChevronBrush}",
            (string?)chevron.Attribute("Stroke"));
    }

    [Fact]
    public void ProjectDirectionButtonStyle_IsTransparentUntilHovered()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var style = Assert.Single(
            document.Descendants(PresentationNamespace + "Style"),
            element => (string?)element.Attribute(XamlNamespace + "Key")
                == "ProjectDirectionButtonStyle");

        Assert.Equal("Transparent", GetSetterValue(style, "Background"));
        Assert.Equal("0", GetSetterValue(style, "BorderThickness"));
        Assert.Equal("0", GetSetterValue(style, "Opacity"));
        Assert.Equal(
            "{DynamicResource ProjectAttachmentChevronBrush}",
            GetSetterValue(style, "Foreground"));

        var root = Assert.Single(
            style.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DirectionRoot");
        Assert.Equal("{TemplateBinding Background}", (string?)root.Attribute("Background"));
        Assert.Null(root.Attribute("CornerRadius"));

        Assert.Equal("1", GetTriggerSetterValue(style, "IsMouseOver", "Opacity"));
        Assert.Equal("1", GetTriggerSetterValue(style, "IsKeyboardFocused", "Opacity"));
    }

    private static string? GetSetterValue(XElement parent, string property) =>
        parent.Elements(PresentationNamespace + "Setter")
            .SingleOrDefault(element => (string?)element.Attribute("Property") == property)
            ?.Attribute("Value")?.Value;

    private static string? GetTriggerSetterValue(
        XElement style,
        string triggerProperty,
        string setterProperty)
    {
        var trigger = Assert.Single(
            style.Descendants(PresentationNamespace + "Trigger"),
            element => (string?)element.Attribute("Property") == triggerProperty
                && (string?)element.Attribute("Value") == "True");
        return GetSetterValue(trigger, setterProperty);
    }

    private static string GetDesktopBoxWindowXamlPath() =>
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
                "Views",
                "DesktopBoxWindow.xaml"));
}
