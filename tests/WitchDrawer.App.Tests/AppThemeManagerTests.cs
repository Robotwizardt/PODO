using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class AppThemeManagerTests
{
    [Fact]
    public void SetCrystalBoxTransparency_RaisesEventOnlyWhenValueChanges()
    {
        AppThemeManager.SetCrystalBoxTransparency(false);
        var changes = new List<bool>();
        EventHandler<bool> handler = (_, enabled) => changes.Add(enabled);
        AppThemeManager.CrystalBoxTransparencyChanged += handler;

        try
        {
            AppThemeManager.SetCrystalBoxTransparency(true);
            AppThemeManager.SetCrystalBoxTransparency(true);
            AppThemeManager.SetCrystalBoxTransparency(false);

            Assert.Equal([true, false], changes);
        }
        finally
        {
            AppThemeManager.CrystalBoxTransparencyChanged -= handler;
            AppThemeManager.SetCrystalBoxTransparency(false);
        }
    }
}
